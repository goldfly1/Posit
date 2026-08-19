#!/usr/bin/env python3
"""Session migration script — backfill past sessions into memory.facts.

Reads conversation transcripts from state.db, sends each to Ollama for
LLM-driven fact extraction, embeds each fact, and inserts into Postgres.

Usage:
    python scripts/migrate-sessions-to-memory.py [--limit N] [--dry-run]

Options:
    --limit N     Only process the N most recent sessions (default: all)
    --dry-run     Extract facts but don't store them (for testing)
    --session ID  Process only the specified session ID
"""
from __future__ import annotations

import argparse
import json
import logging
import os
import re
import sqlite3
import sys
import time
import urllib.request
import urllib.error

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("migrate-memory")

# --- config ---
# Try profile-scoped DB first, fall back to global
_PROFILE_DB = os.path.join(os.environ.get("HERMES_HOME", ""), "state.db")
_GLOBAL_DB = r"C:\Users\goldf\AppData\Local\hermes\state.db"
if os.path.exists(_PROFILE_DB):
    STATE_DB = _PROFILE_DB
elif os.path.exists(_GLOBAL_DB):
    STATE_DB = _GLOBAL_DB
else:
    STATE_DB = _PROFILE_DB  # will fail with clear error

PG = {"host": "localhost", "port": 5434, "database": "shepherd", "user": "shepherd", "password": "shepherd"}
OLLAMA_URL = "http://localhost:11434"
EMBED_MODEL = "nomic-embed-text"
EXTRACT_MODEL = "deepseek-v4-flash:cloud"

EXTRACT_SYSTEM = """You are a memory extraction engine. Extract durable, reusable facts from this conversation transcript.

Only extract facts that would be useful in FUTURE sessions:
- User preferences and working style
- Project decisions and architectural choices
- Technical findings and proven results
- Environment details and toolchain facts
- Stable conventions and lessons learned

DO NOT extract:
- Transient task progress ("fixed bug X", "submitted PR Y")
- Temporary state ("currently working on...")
- Session-specific debugging steps
- Raw data dumps or file contents

Return a JSON array. Each fact:
{"content": "concise statement", "category": "user_pref|project|environment|tooling|general", "trust": 0.0-1.0, "entities": ["name1"]}

Trust: 0.9+ verified/proven, 0.7-0.9 preference/convention, 0.5-0.7 observation, 0.3-0.5 tentative, 0.0-0.3 uncertain.

Output ONLY the JSON array. No explanations."""


def ollama_embed(text: str) -> list[float] | None:
    try:
        payload = json.dumps({"model": EMBED_MODEL, "prompt": text}).encode()
        req = urllib.request.Request(f"{OLLAMA_URL}/api/embeddings", data=payload,
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=15) as resp:
            return json.loads(resp.read()).get("embedding")
    except Exception as e:
        log.debug("embed failed: %s", e)
        return None


def ollama_extract(transcript: str) -> str | None:
    try:
        payload = json.dumps({
            "model": EXTRACT_MODEL,
            "messages": [
                {"role": "system", "content": EXTRACT_SYSTEM},
                {"role": "user", "content": transcript[:8000]},  # cap transcript length
            ],
            "stream": False,
            "options": {"temperature": 0.1},
        }).encode()
        req = urllib.request.Request(f"{OLLAMA_URL}/api/chat", data=payload,
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=60) as resp:
            return json.loads(resp.read()).get("message", {}).get("content", "")
    except Exception as e:
        log.warning("extract failed: %s", e)
        return None


def parse_facts(text: str) -> list[dict]:
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass
    match = re.search(r'\[.*\]', text, re.DOTALL)
    if match:
        try:
            return json.loads(match.group())
        except json.JSONDecodeError:
            pass
    return []


def get_pg_conn():
    import psycopg2
    return psycopg2.connect(**PG)


def store_fact(conn, content: str, category: str, trust: float, session_id: str,
               source: str = "migration", entities: list[str] = None) -> int | None:
    content = content.strip()[:1000]
    if not content:
        return None
    emb = ollama_embed(content)
    cur = conn.cursor()
    try:
        if emb:
            cur.execute(
                """INSERT INTO memory.facts (content, category, trust_score, source, session_id, embedding)
                   VALUES (%s, %s, %s, %s, %s, %s::vector)
                   ON CONFLICT (content) DO UPDATE SET updated_at = NOW()
                   RETURNING fact_id""",
                (content, category, trust, source, session_id, str(emb)),
            )
        else:
            cur.execute(
                """INSERT INTO memory.facts (content, category, trust_score, source, session_id)
                   VALUES (%s, %s, %s, %s, %s)
                   ON CONFLICT (content) DO UPDATE SET updated_at = NOW()
                   RETURNING fact_id""",
                (content, category, trust, source, session_id),
            )
        fact_id = cur.fetchone()[0]
        if entities and fact_id:
            for ent_name in entities:
                cur.execute(
                    "INSERT INTO memory.entities (name) VALUES (%s) ON CONFLICT DO NOTHING RETURNING entity_id",
                    (ent_name,),
                )
                row = cur.fetchone()
                if row:
                    cur.execute(
                        "INSERT INTO memory.fact_entities (fact_id, entity_id) VALUES (%s, %s) ON CONFLICT DO NOTHING",
                        (fact_id, row[0]),
                    )
        conn.commit()
        return fact_id
    except Exception as e:
        log.debug("store failed: %s", e)
        conn.rollback()
        return None
    finally:
        cur.close()


def load_session_messages(db: sqlite3.Connection, session_id: str) -> list[dict]:
    cur = db.cursor()
    cur.execute(
        "SELECT role, content FROM messages WHERE session_id = ? AND role IN ('user', 'assistant') "
        "AND content IS NOT NULL AND content != '' ORDER BY id",
        (session_id,),
    )
    msgs = [{"role": r[0], "content": r[1]} for r in cur.fetchall() if r[1] and isinstance(r[1], str)]
    cur.close()
    return msgs


def build_transcript(msgs: list[dict], max_chars: int = 8000) -> str:
    lines = []
    total = 0
    for msg in msgs[-80:]:  # last 80 messages
        content = msg["content"][:2000]
        label = "User" if msg["role"] == "user" else "Assistant"
        line = f"{label}: {content}"
        if total + len(line) > max_chars:
            break
        lines.append(line)
        total += len(line)
    return "\n\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Migrate sessions to memory.facts")
    parser.add_argument("--limit", type=int, default=0, help="Max sessions to process (0=all)")
    parser.add_argument("--dry-run", action="store_true", help="Extract but don't store")
    parser.add_argument("--session", type=str, default="", help="Process only this session ID")
    parser.add_argument("--list", action="store_true", help="List candidate sessions and exit")
    args = parser.parse_args()

    db = sqlite3.connect(STATE_DB)
    db.row_factory = sqlite3.Row

    # Find sessions
    if args.session:
        cur = db.cursor()
        cur.execute("SELECT id, title, message_count, started_at FROM sessions WHERE id = ?", (args.session,))
        sessions = cur.fetchall()
        cur.close()
    else:
        cur = db.cursor()
        # Focus on sessions with substantial content
        cur.execute("""
            SELECT id, title, message_count, started_at
            FROM sessions
            WHERE message_count >= 20 AND archived = 0
            ORDER BY started_at DESC
        """)
        sessions = cur.fetchall()
        cur.close()

    if args.list:
        print(f"Found {len(sessions)} sessions:")
        for s in sessions:
            print(f"  {s['id']}  msgs={s['message_count']:>5}  title={s['title'] or '(none)'}")
        return

    if args.limit:
        sessions = sessions[:args.limit]

    log.info("Processing %d sessions", len(sessions))

    conn = None
    if not args.dry_run:
        conn = get_pg_conn()

    total_facts = 0
    for i, s in enumerate(sessions):
        sid = s["id"]
        title = s["title"] or "(untitled)"
        msg_count = s["message_count"]
        log.info("[%d/%d] %s — %s (%d msgs)", i + 1, len(sessions), sid[:20], title, msg_count)

        msgs = load_session_messages(db, sid)
        if len(msgs) < 5:
            log.info("  skipping — only %d messages", len(msgs))
            continue

        transcript = build_transcript(msgs)
        if len(transcript) < 100:
            log.info("  skipping — transcript too short")
            continue

        response = ollama_extract(transcript)
        if not response:
            log.warning("  extraction failed")
            continue

        facts = parse_facts(response)
        log.info("  extracted %d facts", len(facts))

        if args.dry_run:
            for f in facts:
                print(f"    [{f.get('category','?')} trust={f.get('trust',0):.1f}] {f.get('content','')[:80]}")
        else:
            stored = 0
            for f in facts:
                fid = store_fact(conn, f.get("content", ""), f.get("category", "general"),
                                 float(f.get("trust", 0.5)), sid,
                                 entities=f.get("entities", []))
                if fid:
                    stored += 1
            log.info("  stored %d/%d facts", stored, len(facts))
            total_facts += stored

        # Rate limit — don't hammer Ollama
        time.sleep(1)

    log.info("Done. Total facts stored: %d", total_facts)

    if conn:
        conn.close()
    db.close()


if __name__ == "__main__":
    main()