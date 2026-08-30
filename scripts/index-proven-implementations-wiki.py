#!/usr/bin/env python3
"""Index proven implementations (SourceCodeBundle .cs files) from passing trials
into wiki.wiki_chunks as type='proven-implementation'.

Reads SourceCodeBundle artifacts from posit_artifacts for sessions known to have
passed the Docker harness. Embeds the spec (from the ArchitectureContract's spec
or a hardcoded mapping) and indexes the implementation code alongside the
existing proven contracts and interface patterns.

Uses the SAME wiki.wiki_chunks table — no new schema, no new table.
"""
import json, os, sys, subprocess, urllib.request

OLLAMA_URL = "http://127.0.0.1:11434"

# Sessions that PASSED the harness (from this session's trial runs)
# Map: session_id → spec text (for embedding)
PASSING_SESSIONS = {
    "DdpFP4S-SkqPk1lFlG5eHg0000": "A CLI tool that reads a CSV file, filters rows where the second column contains 'active', and writes the filtered CSV to stdout including header row.",
    "r5RauSz6w0CDJXzGrQ-V-w0000": "A CLI tool that reads a text file, splits the content into words by whitespace, counts the frequency of each word, and prints results as count word lines sorted by count descending then alphabetically.",
    "svHR6h4GT0u7GSAapQo07Q0000": "A CLI tool that reads a CSV of products, filters out products under $10, converts prices from USD to EUR, groups by category, and outputs JSON.",
    "SMxK6AynKkyEcd4awDX7CQ0000": "A log file analyzer CLI. Read a log file, filter by level, count entries, print LEVEL: N. If empty, print No entries.",
}

def get_embedding(text):
    """Get embedding via Ollama nomic-embed-text."""
    payload = json.dumps({"model": "nomic-embed-text", "prompt": text}).encode()
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

def main():
    inserted = 0
    for session_id, spec in PASSING_SESSIONS.items():
        bundle = get_source_bundle(session_id)
        if bundle is None:
            print(f"SKIP {session_id[:12]}: no SourceCodeBundle")
            continue

        # Build the wiki chunk content: spec + file listing + each .cs file
        files = bundle.get("files", [])
        # Filter to logic component implementations (skip Wire.cs, file-io stubs, interfaces)
        impl_files = [f for f in files if not f["path"].endswith("Wire.cs")
                      and not f["path"].endswith("file-io.cs")
                      and not f["path"].startswith("I")  # interface files
                      and f["path"].endswith(".cs")]

        if not impl_files:
            print(f"SKIP {session_id[:12]}: no implementation .cs files found")
            continue

        content_parts = [
            f"# Proven Implementation (trial {session_id[:8]})",
            f"\n## Spec\n{spec}\n",
            f"\n## Implementation Files ({len(impl_files)} logic components)\n",
        ]
        for f in impl_files:
            fname = os.path.basename(f["path"])
            content_parts.append(f"### {fname}\n```csharp\n{f['content']}\n```\n")

        content = "\n".join(content_parts)

        # Embed the spec (same as proven contracts)
        emb = get_embedding(spec[:4000])  # truncate for embedding input limit
        if not emb:
            print(f"SKIP {session_id[:12]}: no embedding")
            continue

        emb_str = "[" + ",".join(str(x) for x in emb) + "]"
        content_escaped = content.replace("'", "''")
        section = f"proven-impl-{session_id[:8]}"

        sql = f"""
        INSERT INTO wiki.wiki_chunks (file, section, title, content, type, tags, embedding)
        VALUES ('proven-implementations', '{section}', 'Proven Implementation {session_id[:8]}',
                '{content_escaped}', 'proven-implementation',
                'implementation,example,code,proven', '{emb_str}'::vector)
        ON CONFLICT DO NOTHING;
        """
        r = subprocess.run(
            ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-c", sql],
            capture_output=True, text=True, timeout=30
        )
        if r.returncode == 0:
            component_names = [os.path.basename(f["path"]) for f in impl_files]
            print(f"OK   {session_id[:12]}: {component_names}")
            inserted += 1
        else:
            print(f"FAIL {session_id[:12]}: {r.stderr[:100]}")

    print(f"\nIndexed: {inserted} proven implementations into wiki.wiki_chunks")

    # Verify
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-c",
         "SELECT COUNT(*) FROM wiki.wiki_chunks WHERE type = 'proven-implementation'"],
        capture_output=True, text=True, timeout=30
    )
    print(f"Total proven-implementation chunks: {r.stdout.strip()}")

if __name__ == "__main__":
    main()