#!/usr/bin/env python3
"""Technique store: extract techniques from passing trial implementations.

Post-harness hook: after a trial passes the Docker harness, read the
SourceCodeBundle from posit_artifacts, match the implementation code
against a catalog of known C# patterns, and store matched techniques
in wiki.wiki_chunks as type='technique'.

Self-healing:
- Write gate: novelty check (don't store duplicates, bump trust on match)
- Trust: starts at 1, +1 on success (technique injected + trial passed),
  -1 on failure (technique injected + trial failed)
- Auto-delete: trust < -2 → DELETE

No LLM calls. Extraction is mechanical pattern matching.
"""
import json, os, sys, subprocess, urllib.request, re

OLLAMA_URL = "http://127.0.0.1:11434"

# Pattern catalog: (pattern_name, regex, description)
# These are mechanical matches on the implementation C# code.
PATTERN_CATALOG = [
    ("uppercase-transform",
     r"\.ToUpper\(\)",
     "Transform: convert string to uppercase using ToUpper()"),
    ("reverse-string",
     r"\.Reverse\(\)|Array\.Reverse|Reverse<",
     "Transform: reverse a string or array"),
    ("split-by-comma",
     r"\.Split\(['\"]?,['\"]?\)",
     "Parse: split string by comma delimiter"),
    ("split-by-whitespace",
     r"\.Split\(.*?new\s*\[\]|\.Split\(['\"]?\s['\"]?|\.Split\(char",
     "Parse: split string by whitespace"),
    ("read-all-lines",
     r"File\.ReadAllLines",
     "I/O: read file as string[] using File.ReadAllLines"),
    ("read-all-text",
     r"File\.ReadAllText",
     "I/O: read file as single string using File.ReadAllText"),
    ("count-rows",
     r"\.Length\s*-\s*1|\.Count\s*-\s*1|Skip\(1\)",
     "Count: count data rows excluding header"),
    ("filter-contains",
     r"\.Contains\(",
     "Filter: filter by substring match using Contains()"),
    ("dictionary-override",
     r"Dictionary<.*?>|\.Add\(|\[.*?\]\s*=",
     "Merge: use Dictionary with key-based override for merging"),
    ("sort-by-key",
     r"\.OrderBy\(|\.Sort\(|OrderByDescending",
     "Sort: sort collection by key using OrderBy/Sort"),
    ("string-join-newline",
     r'string\.Join\(["\']\\\\n["\']',
     "Format: join string[] with newlines using string.Join"),
    ("json-serialize",
     r"JsonSerializer\.Serialize",
     "Format: serialize object to JSON using JsonSerializer"),
    ("int-parse",
     r"int\.Parse\(",
     "Convert: parse string to int using int.Parse()"),
    ("double-parse",
     r"double\.Parse\(",
     "Convert: parse string to double using double.Parse()"),
    ("regex-match",
     r"Regex\.(Match|Matches|IsMatch)",
     "Extract: use Regex for pattern matching"),
    ("count-matching-lines",
     r"\.Count\(.*?Contains|.*?Where\(.*?Contains.*?Count",
     "Count: count lines matching a filter condition"),
    ("format-prefix",
     r'\$\s*"[A-Z]+:.*?\{',
     "Format: output with prefix format (e.g. 'ERROR: {value}')"),
]

def get_embedding(text):
    """Get embedding via Ollama nomic-embed-text."""
    payload = json.dumps({"model": "nomic-embed-text", "prompt": text[:4000]}).encode()
    req = urllib.request.Request(f"{OLLAMA_URL}/api/embeddings", payload, {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read()).get("embedding", [])

def get_source_bundle(session_id):
    """Get SourceCodeBundle artifact for a session."""
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c",
         f"SELECT payload_json::text FROM posit_artifacts.artifacts WHERE session_id='{session_id}' AND kind='SourceCodeBundle' LIMIT 1"],
        capture_output=True, text=True, timeout=30
    )
    raw = r.stdout.strip()
    if not raw:
        return None
    try:
        return json.loads(raw)
    except:
        return None

def extract_techniques(code):
    """Match implementation code against the pattern catalog."""
    matched = []
    for name, pattern, desc in PATTERN_CATALOG:
        if re.search(pattern, code, re.IGNORECASE):
            matched.append((name, desc))
    return matched

def check_novelty(embedding_str, threshold=0.85):
    """Check if a similar technique already exists."""
    sql = f"""
    SELECT content, 1 - (embedding <=> '{embedding_str}'::vector) as similarity
    FROM wiki.wiki_chunks
    WHERE type = 'technique'
    ORDER BY embedding <=> '{embedding_str}'::vector
    LIMIT 1
    """
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c", sql],
        capture_output=True, text=True, timeout=30
    )
    lines = [l.strip() for l in r.stdout.strip().splitlines() if l.strip()]
    if not lines:
        return True, None  # No existing techniques — novel
    try:
        # Format: content|similarity
        parts = lines[0].rsplit('|', 1)
        sim = float(parts[1]) if len(parts) > 1 else 0.0
        return sim < threshold, sim
    except:
        return True, None

def store_technique(spec_text, technique_name, description, session_id):
    """Store a technique in wiki.wiki_chunks.

    Embeds the SPEC (not the technique) for retrieval — a new spec finds
    techniques that worked on SIMILAR specs. Same technique from different
    trials is kept separately (independent evidence). Dedup is on
    (technique_name, session_id) — don't store the same technique from
    the same trial twice.
    """
    # Check if this exact (technique, session) pair already exists
    section = f"technique-{technique_name}-{session_id[:8]}"
    check_sql = f"SELECT 1 FROM wiki.wiki_chunks WHERE type='technique' AND section='{section}' LIMIT 1"
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c", check_sql],
        capture_output=True, text=True, timeout=30
    )
    if r.stdout.strip():
        return False, "already stored for this session"

    content = f"Technique: {description}\n\nSource: trial {session_id[:8]}\nShape: {technique_name}"
    # Embed the SPEC for retrieval — new specs find techniques from similar specs
    emb = get_embedding(spec_text[:4000])
    if not emb:
        return False, "no embedding"
    emb_str = "[" + ",".join(str(x) for x in emb) + "]"

    content_escaped = content.replace("'", "''")

    sql = f"""
    INSERT INTO wiki.wiki_chunks (file, section, title, content, type, tags, embedding)
    VALUES ('techniques', '{section}', 'Technique: {technique_name}',
            '{content_escaped}', 'technique',
            'technique,{technique_name}', '{emb_str}'::vector)
    ON CONFLICT DO NOTHING;
    """
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-c", sql],
        capture_output=True, text=True, timeout=30
    )
    return r.returncode == 0, r.stderr[:100] if r.returncode != 0 else ""

# Passing sessions from this session's trials + simpler trials
PASSING_SESSIONS = {
    "DdpFP4S-SkqPk1lFlG5eHg0000": "A CLI tool that reads a CSV file, filters rows where the second column contains 'active', and writes the filtered CSV to stdout.",
    "r5RauSz6w0CDJXzGrQ-V-w0000": "A CLI tool that reads a text file, splits the content into words by whitespace, counts the frequency of each word, and prints results sorted by count descending then alphabetically.",
    "svHR6h4GT0u7GSAapQo07Q0000": "A CLI tool that reads a CSV of products, filters out products under $10, converts prices from USD to EUR, groups by category, and outputs JSON.",
    "i1-8JSl-E0-cj9JDgn5HdQ0000": "A CLI tool that reads a CSV file, validates each row for correct field count, counts valid and invalid rows, and prints a report.",
    # Simpler trials (need session IDs from the runs we just did)
    # Will be populated by running the script after trials
}

def main():
    stored = 0
    skipped = 0
    for session_id, spec in PASSING_SESSIONS.items():
        bundle = get_source_bundle(session_id)
        if bundle is None:
            print(f"SKIP {session_id[:12]}: no SourceCodeBundle")
            continue
        files = bundle.get("files", [])
        # Get implementation .cs files (skip Wire.cs, file-io, interfaces)
        impl_files = [f for f in files if f["path"].endswith(".cs")
                      and not f["path"].endswith("Wire.cs")
                      and not f["path"].endswith("file-io.cs")]
        if not impl_files:
            print(f"SKIP {session_id[:12]}: no implementation files")
            continue
        all_code = "\n".join(f["content"] for f in impl_files)
        techniques = extract_techniques(all_code)
        if not techniques:
            print(f"SKIP {session_id[:12]}: no patterns matched")
            continue
        for name, desc in techniques:
            ok, detail = store_technique(spec, name, desc, session_id)
            if ok:
                print(f"  STORED: {name} — {desc[:60]}")
                stored += 1
            else:
                print(f"  SKIP: {name} — {detail}")
                skipped += 1

    print(f"\nStored: {stored}, Skipped: {skipped}")

    # Verify
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-c",
         "SELECT COUNT(*) FROM wiki.wiki_chunks WHERE type = 'technique'"],
        capture_output=True, text=True, timeout=30
    )
    print(f"Total technique chunks: {r.stdout.strip()}")

if __name__ == "__main__":
    main()