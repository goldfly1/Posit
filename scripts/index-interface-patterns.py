#!/usr/bin/env python3
"""Index interface-pattern markdown files into wiki.wiki_chunks with embeddings.

Uses Ollama nomic-embed-text for embeddings (same as WikiSearcher).
Each file becomes one chunk with type='interface-pattern'.
"""
import os, sys, json, urllib.request, glob

OLLAMA_URL = "http://127.0.0.1:11434"
WIKI_DIR = os.path.join(os.path.dirname(__file__), "..", "wiki", "interface-patterns")

def get_embedding(text):
    payload = json.dumps({"model": "nomic-embed-text", "prompt": text}).encode()
    req = urllib.request.Request(f"{OLLAMA_URL}/api/embeddings", payload, {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        data = json.loads(resp.read())
        return data.get("embedding", [])

def main():
    files = sorted(glob.glob(os.path.join(WIKI_DIR, "*.md")))
    if not files:
        print("No interface-pattern files found")
        return 1

    # Build INSERT statements
    inserts = []
    for fpath in files:
        fname = os.path.basename(fpath)
        stem = fname.replace(".md", "")
        content = open(fpath, encoding="utf-8").read()
        # Use first 4000 chars for embedding (nomic-embed-text has input limits;
        # the full content goes into the DB, but the embedding only needs the
        # problem shape + spec verbs to match on spec similarity)
        embed_text = content[:4000]
        emb = get_embedding(embed_text)
        if not emb:
            print(f"WARNING: no embedding for {fname}")
            continue
        emb_str = "[" + ",".join(str(x) for x in emb) + "]"
        # Escape content for SQL
        content_escaped = content.replace("'", "''")
        inserts.append(f"""
        INSERT INTO wiki.wiki_chunks (file, section, title, content, type, tags, embedding)
        VALUES ('interface-patterns/{stem}', '', '', '{content_escaped}', 'interface-pattern', 'decomposition,interface,pattern', '{emb_str}'::vector)
        ON CONFLICT DO NOTHING;
        """)

    if not inserts:
        print("No embeddings generated")
        return 1

    sql = "\n".join(inserts)
    sql_file = os.path.join(os.environ.get("TEMP", "/tmp"), "index-interface-patterns.sql")
    with open(sql_file, "w", encoding="utf-8") as f:
        f.write(sql)
    print(f"Generated {len(inserts)} INSERT statements → {sql_file}")
    print("Run: docker exec -i shepherd-postgres psql -U shepherd -d shepherd < " + sql_file)
    return 0

if __name__ == "__main__":
    sys.exit(main())