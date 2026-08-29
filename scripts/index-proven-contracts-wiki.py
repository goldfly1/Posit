#!/usr/bin/env python3
"""Index proven ArchitectureContracts from passing trials into wiki.wiki_chunks.

The wiki is the production knowledge base — WikiSearcher retrieves from it
and injects into the architecture prompt. Proven contracts go here as
complete JSON worked examples, not in a separate DB.
"""
import json, os, sys, subprocess, urllib.request, glob

OLLAMA_URL = "http://127.0.0.1:11434"

# Passing trial sessions → (spec, session_id)
PASSING = {
    "FPeODR4RKE-0XlFXoTzT0A0000": "A CLI tool that reads a CSV file, parses each line into fields, validates that all rows have the same number of fields, transforms each row into a JSON object with field names from the header row, and prints the JSON array to stdout.",
    "L1zitdFAEU650yc9Wi0XtQ0000": "A CLI tool that reads a JSON array of objects from a file, extracts field names from the first object as CSV headers, converts each object to a CSV row, and prints the CSV to stdout.",
    "k0Jz9AslcU2bnZKI1yKcaA0000": "A CLI tool that reads a text file, splits the content into words by whitespace, counts the frequency of each word, and prints results as count word lines sorted by count descending.",
    "SMxK6AynKkyEcd4awDX7CQ0000": "A log file analyzer CLI. Read a log file, filter by level, count entries, print LEVEL: N. If empty, print No entries.",
    "m1MIbp8shUitJn-TtJJPpA0000": "A log file analyzer CLI. Read a log file, filter by level, count entries, print LEVEL: N. If empty, print No entries.",
    "VDx_qhNpAEqpHxsKXFHA6A0000": "A CLI tool that reads a CSV file, validates each row for correct field count, counts valid and invalid rows, and prints a report.",
    "9Qajdmhq3UijOQqwL0accQ0000": "A CLI tool that reads a CSV of products, filters out products under $10, converts prices from USD to EUR, groups by category, and outputs JSON.",
    "BRAJTeNS30yLRkWD6wp1lQ0000": "A CLI tool that reads a Markdown file, extracts all links in [text](url) format, and outputs them as a JSON array of {text, url} objects.",
    "EeUHQzll3UWf4R_lVw4uZA0000": "A CLI tool that merges two INI config files given as command-line arguments. The second file's values override the first file's for duplicate keys.",
}

def get_embedding(text):
    payload = json.dumps({"model": "nomic-embed-text", "prompt": text}).encode()
    req = urllib.request.Request(f"{OLLAMA_URL}/api/embeddings", payload, {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read()).get("embedding", [])

def get_contract(session_id):
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-A", "-c",
         f"SELECT payload_json::text FROM posit_artifacts.artifacts WHERE session_id='{session_id}' AND kind='ArchitectureContract' LIMIT 1"],
        capture_output=True, text=True, timeout=30
    )
    raw = r.stdout.strip()
    if not raw: return None
    try: return json.loads(raw)
    except: return None

def main():
    inserted = 0
    for session_id, spec in PASSING.items():
        contract = get_contract(session_id)
        if contract is None:
            print(f"SKIP {session_id[:12]}: no artifact")
            continue
        # Build the wiki chunk content: spec + complete JSON contract
        contract_json = json.dumps(contract, indent=2)
        content = f"# Proven ArchitectureContract (trial {session_id[:8]})\n\n"
        content += f"## Spec\n{spec}\n\n"
        content += f"## Complete Contract JSON (passed all Docker harness tests)\n```json\n{contract_json}\n```\n"
        content += f"\n## Components: {[c.get('name') for c in contract.get('components', [])]}\n"

        emb = get_embedding(spec)
        if not emb:
            print(f"SKIP {session_id[:12]}: no embedding")
            continue

        emb_str = "[" + ",".join(str(x) for x in emb) + "]"
        content_escaped = content.replace("'", "''")
        section = f"proven-{session_id[:8]}"

        sql = f"""
        INSERT INTO wiki.wiki_chunks (file, section, title, content, type, tags, embedding)
        VALUES ('proven-contracts', '{section}', 'Proven Contract {session_id[:8]}', '{content_escaped}', 'proven-contract', 'decomposition,example,contract', '{emb_str}'::vector)
        ON CONFLICT DO NOTHING;
        """
        r = subprocess.run(
            ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-c", sql],
            capture_output=True, text=True, timeout=30
        )
        if r.returncode == 0:
            components = [c.get("name", "?") for c in contract.get("components", [])]
            print(f"OK   {session_id[:12]}: {components}")
            inserted += 1
        else:
            print(f"FAIL {session_id[:12]}: {r.stderr[:100]}")

    print(f"\nIndexed: {inserted} proven contracts into wiki.wiki_chunks")
    # Verify
    r = subprocess.run(
        ["docker", "exec", "shepherd-postgres", "psql", "-U", "shepherd", "-d", "shepherd", "-t", "-c",
         "SELECT COUNT(*) FROM wiki.wiki_chunks WHERE type = 'proven-contract'"],
        capture_output=True, text=True, timeout=30
    )
    print(f"Total proven-contract chunks: {r.stdout.strip()}")

if __name__ == "__main__":
    main()