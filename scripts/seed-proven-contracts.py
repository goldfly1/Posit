#!/usr/bin/env python3
"""Seed posit_proven_contracts from successful trial ArchitectureContract artifacts.

Extracts (spec, contract_json, session_id) from posit_artifacts.artifacts where
the ArchitectureContract kind exists and the session had a successful harness run.
Embeds the spec with nomic-embed-text and inserts into the proven contracts store.
"""
import json, os, sys, subprocess, urllib.request

OLLAMA_URL = "http://127.0.0.1:11434"

# Passing trial sessions from this session's rack runs
PASSING_SESSIONS = {
    # T1
    "FPeODR4RKE-0XlFXoTzT0A0000": "A CLI tool that reads a CSV file, parses each line into fields, validates that all rows have the same number of fields, transforms each row into a JSON object with field names from the header row, and prints the JSON array to stdout.",
    # T2
    "L1zitdFAEU650yc9Wi0XtQ0000": "A CLI tool that reads a JSON array of objects from a file, extracts field names from the first object as CSV headers, converts each object to a CSV row, and prints the CSV to stdout.",
    # T4
    "k0Jz9AslcU2bnZKI1yKcaA0000": "A CLI tool that reads a text file, splits the content into words by whitespace, counts the frequency of each word, and prints results as 'count word' lines sorted by count descending.",
    # T8 (role-dispatch era)
    "SMxK6AynKkyEcd4awDX7CQ0000": "A log file analyzer CLI. Read a log file, filter by level, count entries, print 'LEVEL: N'. If empty, print 'No entries'.",
    # T8 (Phase E era — fidelity gate forced proper decomposition)
    "m1MIbp8shUitJn-TtJJPpA0000": "A log file analyzer CLI. Read a log file, filter by level, count entries, print 'LEVEL: N'. If empty, print 'No entries'.",
    # T9 (v4-pro era)
    "VDx_qhNpAEqpHxsKXFHA6A0000": "A CLI tool that reads a CSV file, validates each row for correct field count, counts valid and invalid rows, and prints a report.",
    # T10
    "9Qajdmhq3UijOQqwL0accQ0000": "A CLI tool that reads a CSV of products, filters out products under $10, converts prices from USD to EUR, groups by category, and outputs JSON.",
    # T11
    "BRAJTeNS30yLRkWD6wp1lQ0000": "A CLI tool that reads a Markdown file, extracts all links in [text](url) format, and outputs them as a JSON array of {text, url} objects.",
    # T12 confirm
    "EeUHQzll3UWf4R_lVw4uZA0000": "A CLI tool that merges two INI config files given as command-line arguments. The second file's values override the first file's for duplicate keys.",
}

def get_embedding(text):
    payload = json.dumps({"model": "nomic-embed-text", "prompt": text}).encode()
    req = urllib.request.Request(f"{OLLAMA_URL}/api/embeddings", payload, {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read()).get("embedding", [])

def get_contract(session_id):
    """Extract ArchitectureContract from posit_artifacts for a session."""
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c",
         f"SELECT payload_json::text FROM posit_artifacts.artifacts WHERE session_id='{session_id}' AND kind='ArchitectureContract' LIMIT 1"],
        capture_output=True, text=True, timeout=30
    )
    raw = r.stdout.strip()
    if not raw or raw == "":
        return None
    try:
        return json.loads(raw)
    except:
        return None

def insert_contract(spec, contract, session_id, trial_id, embedding):
    """Insert into posit_proven_contracts."""
    contract_str = json.dumps(contract).replace("'", "''")
    spec_escaped = spec.replace("'", "''")
    emb_str = "[" + ",".join(str(x) for x in embedding) + "]"
    sql = f"""
    INSERT INTO posit_proven_contracts (spec_text, contract_json, spec_embedding, trial_id, session_id)
    VALUES ('{spec_escaped}', '{contract_str}'::jsonb, '{emb_str}'::vector, '{trial_id}', '{session_id}')
    ON CONFLICT DO NOTHING;
    """
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-c", sql],
        capture_output=True, text=True, timeout=30
    )
    return r.returncode == 0

def main():
    inserted = 0
    skipped = 0
    for session_id, spec in PASSING_SESSIONS.items():
        contract = get_contract(session_id)
        if contract is None:
            print(f"SKIP {session_id[:12]}: no ArchitectureContract artifact found")
            skipped += 1
            continue
        emb = get_embedding(spec)
        if not emb:
            print(f"SKIP {session_id[:12]}: no embedding")
            skipped += 1
            continue
        trial_id = session_id[:8]
        ok = insert_contract(spec, contract, session_id, trial_id, emb)
        if ok:
            components = [c.get("name", "?") for c in contract.get("components", [])]
            print(f"OK   {session_id[:12]}: {len(components)} components — {components}")
            inserted += 1
        else:
            print(f"FAIL {session_id[:12]}: insert failed")
            skipped += 1

    print(f"\nSeeded: {inserted} proven contracts, skipped: {skipped}")

    # Verify
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-c",
         "SELECT COUNT(*) FROM posit_proven_contracts"],
        capture_output=True, text=True, timeout=30
    )
    print(f"Total in store: {r.stdout.strip()}")

if __name__ == "__main__":
    main()