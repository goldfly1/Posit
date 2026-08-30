#!/usr/bin/env python3
"""Technique store lifecycle: extract, trust, demote, auto-delete.

Post-harness hook — runs after every trial (pass or fail).

PASS: extract techniques from implementation code, store with trust=1.
      Per-session dedup — same technique from different trials = independent evidence.
FAIL: if techniques were injected into the prompt, decrement their trust.
      Trust < -2 → auto-delete. No human, no LLM judgment.

Also: re-seed from all passing trials in the DB (one-time bulk extraction).
"""
import json, os, sys, subprocess, urllib.request, re

OLLAMA_URL = "http://127.0.0.1:11434"

# Pattern catalog: (pattern_name, regex, description)
PATTERN_CATALOG = [
    ("uppercase-transform", r"\.ToUpper\(\)", "Transform: convert string to uppercase using ToUpper()"),
    ("lowercase-transform", r"\.ToLower\(\)", "Transform: convert string to lowercase using ToLower()"),
    ("reverse-string", r"\.Reverse\(\)|Array\.Reverse", "Transform: reverse a string or array"),
    ("split-by-comma", r"\.Split\(['\"]?,['\"]?\)", "Parse: split string by comma delimiter"),
    ("split-by-whitespace", r"\.Split\(.*?new\s*\[\]|\.Split\(['\"]?\s['\"]?|\.Split\(char", "Parse: split string by whitespace"),
    ("read-all-lines", r"File\.ReadAllLines", "I/O: read file as string[] using File.ReadAllLines"),
    ("read-all-text", r"File\.ReadAllText", "I/O: read file as single string using File.ReadAllText"),
    ("count-rows-exclude-header", r"\.Skip\(1\)|\.Length\s*-\s*1|\.Count\s*-\s*1", "Count: skip header row when counting data rows"),
    ("filter-contains", r"\.Contains\(", "Filter: filter by substring match using Contains()"),
    ("dictionary-override", r"Dictionary<.*?>|\.Add\(|\[.*?\]\s*=", "Merge: use Dictionary with key-based override for merging"),
    ("sort-by-key", r"\.OrderBy\(|\.Sort\(|OrderByDescending", "Sort: sort collection by key using OrderBy/Sort"),
    ("string-join-newline", r'string\.Join\(["\']\\\\n["\']', "Format: join string[] with newlines using string.Join"),
    ("json-serialize", r"JsonSerializer\.Serialize", "Format: serialize object to JSON using JsonSerializer"),
    ("int-parse", r"int\.Parse\(", "Convert: parse string to int using int.Parse()"),
    ("double-parse", r"double\.Parse\(", "Convert: parse string to double using double.Parse()"),
    ("regex-match", r"Regex\.(Match|Matches|IsMatch)", "Extract: use Regex for pattern matching"),
    ("count-matching-lines", r"\.Count\(.*?Contains|.*?Where\(.*?Contains.*?Count", "Count: count lines matching a filter condition"),
    ("format-prefix", r'\$\s*"[A-Z]+:.*?\{', "Format: output with prefix format (e.g. 'ERROR: {value}')"),
    ("modulo-even-odd", r"%.*?2.*?==.*?0", "Logic: check even/odd using modulo operator"),
    ("replace-chars", r"\.Replace\(", "Transform: replace characters or substrings using Replace()"),
    ("is-null-or-empty", r"string\.IsNullOrEmpty|string\.IsNullOrWhiteSpace", "Check: guard against empty/null input using IsNullOrEmpty"),
    ("console-readline", r"Console\.ReadLine", "I/O: read from stdin using Console.ReadLine"),
]

def get_embedding(text):
    payload = json.dumps({"model": "nomic-embed-text", "prompt": text[:4000]}).encode()
    req = urllib.request.Request(f"{OLLAMA_URL}/api/embeddings", payload, {"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read()).get("embedding", [])
    except:
        return []

def get_source_bundle(session_id):
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c",
         f"SELECT payload_json::text FROM posit_artifacts.artifacts WHERE session_id='{session_id}' AND kind='SourceCodeBundle' LIMIT 1"],
        capture_output=True, text=True, timeout=30)
    raw = r.stdout.strip()
    if not raw: return None
    try: return json.loads(raw)
    except: return None

def get_architecture_contract_spec(session_id):
    """Get the spec text from the ArchitectureContract artifact."""
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c",
         f"SELECT payload_json->>'specText' FROM posit_artifacts.artifacts WHERE session_id='{session_id}' AND kind='ArchitectureContract' LIMIT 1"],
        capture_output=True, text=True, timeout=30)
    return r.stdout.strip() or None

def extract_techniques(code):
    matched = []
    for name, pattern, desc in PATTERN_CATALOG:
        if re.search(pattern, code, re.IGNORECASE):
            matched.append((name, desc))
    return matched

def store_technique(spec_text, technique_name, description, session_id):
    """Store a technique with trust=1. Per-session dedup."""
    section = f"technique-{technique_name}-{session_id[:8]}"
    # Check if already stored
    check_sql = f"SELECT 1 FROM wiki.wiki_chunks WHERE type='technique' AND section='{section}' LIMIT 1"
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c", check_sql],
        capture_output=True, text=True, timeout=30)
    if r.stdout.strip():
        return False, "already stored"

    content = f"Technique: {description}\n\nSource: trial {session_id[:8]}\nShape: {technique_name}"
    emb = get_embedding(spec_text[:4000])
    if not emb: return False, "no embedding"
    emb_str = "[" + ",".join(str(x) for x in emb) + "]"
    content_escaped = content.replace("'", "''")

    sql = f"""
    INSERT INTO wiki.wiki_chunks (file, section, title, content, type, tags, embedding, trust)
    VALUES ('techniques', '{section}', 'Technique: {technique_name}',
            '{content_escaped}', 'technique',
            'technique,{technique_name}', '{emb_str}'::vector, 1)
    ON CONFLICT DO NOTHING;
    """
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-c", sql],
        capture_output=True, text=True, timeout=30)
    return r.returncode == 0, r.stderr[:100] if r.returncode != 0 else ""

def demote_techniques(session_id):
    """Decrement trust for techniques from this session. Auto-delete at < -2."""
    # Find techniques from this session
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c",
         f"SELECT section, trust FROM wiki.wiki_chunks WHERE type='technique' AND section LIKE '%{session_id[:8]}%'"],
        capture_output=True, text=True, timeout=30)
    lines = [l.strip() for l in r.stdout.strip().splitlines() if l.strip()]
    demoted = 0
    deleted = 0
    for line in lines:
        parts = line.split('|')
        if len(parts) != 2: continue
        section, trust = parts[0], int(parts[1])
        new_trust = trust - 1
        if new_trust < -2:
            # Auto-delete
            del_sql = f"DELETE FROM wiki.wiki_chunks WHERE section='{section}' AND type='technique'"
            subprocess.run(["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-c", del_sql],
                capture_output=True, text=True, timeout=30)
            deleted += 1
        else:
            upd_sql = f"UPDATE wiki.wiki_chunks SET trust={new_trust} WHERE section='{section}' AND type='technique'"
            subprocess.run(["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-c", upd_sql],
                capture_output=True, text=True, timeout=30)
            demoted += 1
    return demoted, deleted

def get_passing_sessions():
    """Get all sessions that have SourceCodeBundle with implementation files."""
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c",
         """SELECT DISTINCT a.session_id
         FROM posit_artifacts.artifacts a
         WHERE a.kind = 'SourceCodeBundle'
           AND jsonb_array_length(a.payload_json->'files') > 0
           AND a.payload_json::text NOT LIKE '%Extern%'
           AND a.payload_json::text NOT LIKE '%DafnyRuntime%'
         ORDER BY a.session_id"""],
        capture_output=True, text=True, timeout=30)
    return [l.strip() for l in r.stdout.strip().splitlines() if l.strip()]

def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "seed"

    if mode == "demote":
        # Demote techniques from a failed session
        session_id = sys.argv[2]
        demoted, deleted = demote_techniques(session_id)
        print(f"Demoted: {demoted}, Auto-deleted: {deleted}")
        return

    if mode == "seed":
        # Bulk extraction from all passing sessions
        sessions = get_passing_sessions()
        print(f"Found {len(sessions)} sessions with SourceCodeBundle")
        stored = 0
        skipped = 0
        for sid in sessions:
            bundle = get_source_bundle(sid)
            if not bundle: continue
            files = bundle.get("files", [])
            impl_files = [f for f in files if f["path"].endswith(".cs")
                          and not f["path"].endswith("Wire.cs")
                          and not f["path"].endswith("file-io.cs")
                          and not f["path"].endswith("console-io.cs")
                          and not f["path"].endswith(".csproj")]
            if not impl_files: continue
            # Get spec text
            spec = get_architecture_contract_spec(sid) or ""
            if not spec:
                # Try to infer from file names
                spec = f"Implementation with components: {', '.join(os.path.basename(f['path']) for f in impl_files)}"
            all_code = "\n".join(f["content"] for f in impl_files)
            techniques = extract_techniques(all_code)
            if not techniques: continue
            for name, desc in techniques:
                ok, detail = store_technique(spec, name, desc, sid)
                if ok:
                    print(f"  STORED [{sid[:8]}]: {name}")
                    stored += 1
                else:
                    skipped += 1
        print(f"\nStored: {stored}, Skipped: {skipped}")
        # Verify
        r = subprocess.run(
            ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-c",
             "SELECT COUNT(*), COUNT(DISTINCT section) FROM wiki.wiki_chunks WHERE type='technique'"],
            capture_output=True, text=True, timeout=30)
        print(f"Total technique chunks: {r.stdout.strip()}")

if __name__ == "__main__":
    main()