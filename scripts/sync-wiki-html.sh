#!/bin/bash
# Regenerates docs/wiki.html from the wiki/ directory.
# Run after any wiki update: bash scripts/sync-wiki-html.sh
# Or use --watch to auto-regenerate on file changes.

set -e

WIKI_DIR="${WIKI_DIR:-C:\\Users\\goldf\\Posit\\wiki}"
OUTPUT="${OUTPUT:-C:\\Users\\goldf\\Posit\\docs\\wiki.html}"
INDEX_SCRIPT="C:\\Users\\goldf\\AppData\\Local\\hermes\\skills\\software-development\\knowledge-wiki\\scripts\\index_wiki.py"
HTML_SCRIPT="C:\\Users\\goldf\\AppData\\Local\\hermes\\skills\\software-development\\knowledge-wiki\\scripts\\generate_html.py"

echo "[sync-wiki] Indexing wiki..."
python "$INDEX_SCRIPT" --wiki "$WIKI_DIR"

echo "[sync-wiki] Generating HTML..."
python "$HTML_SCRIPT" --wiki "$WIKI_DIR" --output "$OUTPUT"

echo "[sync-wiki] Done: $OUTPUT"