#!/usr/bin/env python3
"""
Approach 4 — Batch variant generator (batch mode).

One model call generates ALL variants for a pattern. Z3 sweeps them all.
Failures fed back in one batch correction call.

Usage:
    python scripts/generate-batch.py --pattern parser --batch-size 10
    python scripts/generate-batch.py --all --batch-size 10
"""

import json
import os
import subprocess
import sys
import time
import urllib.request
import argparse
from pathlib import Path
from itertools import product

# Paths
POSIT_ROOT = Path(__file__).parent.parent
PATTERNS_DIR = POSIT_ROOT / "patterns"
STAGING_DIR = POSIT_ROOT / ".posit" / "staging" / "batch-variants"
DAFNY = "C:/Users/goldf/.dotnet/tools/dafny.exe"
Z3 = "C:/Users/goldf/.dotnet/tools/z3/bin/z3.exe"
OLLAMA_URL = "http://localhost:11434/api/generate"
MODEL = "deepseek-v4-flash:cloud"

STAGING_DIR.mkdir(parents=True, exist_ok=True)

# ─── Parameter grids (same as generate-variants.py) ───────────────────────

PARAM_GRIDS = {
    "parser": {
        "delimiter": [",", "|", "\t", ";", " "],
        "quoteChar": ['""', '"', "'"],
        "hasHeader": ["true", "false"],
    },
    "validator": {
        "rule": ["nonEmpty", "minLength", "maxLength", "typeCheck", "range"],
        "minVal": ["0", "1", "5"],
        "maxVal": ["10", "50", "100", "1000"],
    },
    "repository": {
        "entityType": ["User", "Product", "Order", "Session", "Config", "Task"],
        "idType": ["int", "string"],
    },
    "state-machine": {
        "states": [
            ["Idle", "Active", "Done"],
            ["Open", "Closed", "Error"],
            ["Pending", "Running", "Complete", "Failed"],
            ["Draft", "Review", "Published", "Archived"],
        ],
        "events": [["start", "finish"], ["open", "close", "fail"], ["submit", "approve", "reject"]],
    },
    "transformer": {
        "operation": ["ToUpper", "Reverse", "Sort", "Trim", "Duplicate"],
    },
    "aggregator": {
        "operation": ["Sum", "Max", "Min", "CountPositive", "CountNegative", "Average"],
        "collectionType": ["seq<int>", "seq<string>"],
    },
    "builder": {
        "separator": [", ", " | ", " - ", "; ", "\n"],
        "validateEmpty": ["true", "false"],
    },
    "iterator": {
        "collectionType": ["seq<int>", "seq<string>", "seq<seq<string>>"],
    },
}


def verify_dafny(dafny_path: str) -> tuple[bool, str]:
    """Run dafny verify, return (success, output)."""
    try:
        result = subprocess.run(
            [DAFNY, "verify", dafny_path,
             "--solver-path", Z3,
             "--standard-libraries", "--allow-warnings"],
            capture_output=True, text=True, timeout=120
        )
        output = result.stdout + result.stderr
        verified = result.returncode == 0 and "Error" not in output
        return verified, output
    except subprocess.TimeoutExpired:
        return False, "Z3 timeout (120s)"
    except Exception as e:
        return False, str(e)


def call_flash(prompt: str, system: str = "", timeout: int = 600) -> tuple[str, int, int, float]:
    """Call Ollama, return (text, input_tokens, output_tokens, elapsed_s)."""
    full_prompt = f"{system}\n\n{prompt}" if system else prompt
    data = json.dumps({
        "model": MODEL,
        "prompt": full_prompt,
        "stream": False,
        "options": {"num_ctx": 32768}
    }).encode()

    start = time.time()
    req = urllib.request.Request(OLLAMA_URL, data=data)
    resp = urllib.request.urlopen(req, timeout=timeout)
    result = json.loads(resp.read())
    elapsed = time.time() - start

    text = result.get("response", "")
    if "</think>" in text:
        text = text.split("</think>", 1)[1].strip()

    input_tokens = result.get("prompt_eval_count", 0)
    output_tokens = result.get("eval_count", 0)
    return text, input_tokens, output_tokens, elapsed


def extract_dafny_files(text: str) -> list[str]:
    """Extract multiple .dfy file blocks from model response.
    
    Expected format:
    === FILE: variant-001.dfy ===
    ```dafny
    ...code...
    ```
    === FILE: variant-002.dfy ===
    ```dafny
    ...code...
    ```
    
    Also handles ```dafny blocks without headers (assigns sequential names).
    """
    files = []
    
    # Try === FILE: name === format
    import re
    blocks = re.split(r'=== FILE:.*?===', text)
    for block in blocks[1:]:  # skip preamble
        dafny = extract_single_dafny(block)
        if dafny:
            files.append(dafny)
    
    if files:
        return files
    
    # Fallback: extract all ```dafny blocks
    parts = text.split("```dafny")
    for part in parts[1:]:
        end = part.find("```")
        if end > 0:
            files.append(part[:end].strip())
    
    # Fallback: extract all ``` blocks
    if not files:
        parts = text.split("```")
        for i in range(1, len(parts), 2):
            content = parts[i]
            # Skip language tag line
            nl = content.find("\n")
            if nl > 0 and nl < 20:
                content = content[nl+1:]
            if content.strip():
                files.append(content.strip())
    
    return files


def extract_single_dafny(text: str) -> str:
    """Extract a single .dfy block from text."""
    if "```dafny" in text:
        start = text.find("```dafny") + 8
        end = text.find("```", start)
        if end > start:
            return text[start:end].strip()
    if "```" in text:
        start = text.find("```") + 3
        nl = text.find("\n", start)
        if nl > start and nl < start + 20:
            start = nl + 1
        end = text.find("```", start)
        if end > start:
            return text[start:end].strip()
    return text.strip()


def get_param_combos(pattern_name: str, batch_size: int) -> list[dict]:
    """Get parameter combinations for a pattern."""
    grid = PARAM_GRIDS.get(pattern_name)
    if not grid:
        return []
    
    keys = list(grid.keys())
    combos = list(product(*[grid[k] for k in keys]))
    
    result = []
    for combo in combos[:batch_size]:
        params = dict(zip(keys, combo))
        for k, v in params.items():
            if isinstance(v, list):
                params[k] = json.dumps(v)
        result.append(params)
    return result


def gen_substitution_variant(pattern_name: str, params: dict, index: int) -> str:
    """Generate a variant by substituting parameters into the pattern file. No model call."""
    pattern_file = PATTERNS_DIR / f"{pattern_name}.dfy"
    source = pattern_file.read_text()
    
    for key, val in params.items():
        placeholder = "{{" + key + "}}"
        source = source.replace(placeholder, str(val))
    
    param_str = ", ".join(f"{k}={v}" for k, v in params.items())
    header = f"// Variant {index}: {pattern_name} ({param_str})\n// Generated by parameter substitution\n\n"
    
    return header + source


def run_batch_pattern(pattern_name: str, batch_size: int, retries: int, use_wiki: bool = True):
    """Generate all variants for a pattern:
    1. Try substitution (free, no model call)
    2. Batch-generate remaining variants (one model call)
    3. Z3 sweep all variants
    4. Batch correction for failures
    """
    combos = get_param_combos(pattern_name, batch_size)
    if not combos:
        print(f"[skip] No parameter grid for {pattern_name}")
        return {"passed": 0, "failed": 0, "tokens": 0, "model_calls": 0}
    
    base_source = (PATTERNS_DIR / f"{pattern_name}.dfy").read_text()
    ref_card = ""
    if use_wiki:
        ref_path = PATTERNS_DIR / "dafny-reference-card.dfy"
        ref_card = ref_path.read_text() if ref_path.exists() else ""
    
    print(f"\n{'='*60}")
    print(f"Pattern: {pattern_name} | Batch: {len(combos)} | Retries: {retries}")
    print(f"Wiki reference: {'ON' if ref_card else 'OFF'}")
    print(f"{'='*60}")
    
    results = {"passed": [], "failed": []}
    total_tokens = 0
    model_calls = 0
    
    # ─── Step 1: Substitution sweep (free — no model call) ────────────
    print(f"\n[1] Substitution sweep — trying {len(combos)} parameter substitutions...")
    needs_generation = []
    
    for i, params in enumerate(combos):
        variant_name = f"{pattern_name}-v{i:03d}"
        variant_path = STAGING_DIR / f"{variant_name}.dfy"
        
        source = gen_substitution_variant(pattern_name, params, i)
        variant_path.write_text(source)
        verified, output = verify_dafny(str(variant_path))
        
        if verified:
            print(f"    {variant_name}: ✅ PASS (substitution, free)")
            results["passed"].append({"name": variant_name, "index": i})
        else:
            needs_generation.append((i, params, variant_name))
    
    if not needs_generation:
        total = len(results["passed"])
        print(f"\n{'='*60}")
        print(f"SUMMARY: {pattern_name}")
        print(f"  Passed: {total}/{total} (100% — all substitution!)")
        print(f"  Failed: 0/{total}")
        print(f"  Total tokens: 0")
        print(f"  Model calls: 0")
        print(f"{'='*60}\n")
        return {"passed": total, "failed": 0, "tokens": 0, "model_calls": 0}
    
    print(f"    {len(results['passed'])} passed by substitution, {len(needs_generation)} need model generation")
    
    # ─── Step 2: Batch-generate remaining variants (one call) ─────────
    variant_descriptions = []
    for idx, params, vname in needs_generation:
        param_str = ", ".join(f"{k}={v}" for k, v in params.items())
        variant_descriptions.append(f"Variant {idx:03d}: {param_str}")
    
    variants_block = "\n".join(variant_descriptions)
    
    prompt = f"""You are a Dafny code generator. Generate {len(needs_generation)} VARIANTS of the {pattern_name} pattern.

Base pattern source:
```dafny
{base_source}
```

Generate these variants:
{variants_block}

Rules:
1. Output each variant separated by === FILE: variant-NNN.dfy ===
2. Each variant is a COMPLETE .dfy file in a ```dafny block
3. All methods MUST have requires/ensures contracts
4. All recursive functions MUST have decreases clauses
5. Do NOT use reads on string/seq parameters — they are value types
6. All seq access MUST be bounds-checked (prove 0 <= i < |s| before s[i])
7. Do NOT use assert unless provable from invariants
8. Keep each variant under 200 lines, 10 methods, 5 classes
9. Must be verifiable by Z3 (Dafny 4.11, --standard-libraries)
"""
    
    if ref_card:
        prompt += f"""
Dafny reference card (verified — follow these patterns exactly):
```dafny
{ref_card}
```
"""
    
    system = "You are a Dafny expert. You write code that Z3 verifies on the first try. You never use reads on value types. You always prove bounds before indexing. You always supply decreases for recursion. You follow the reference card patterns exactly. You generate multiple complete files in one response, each separated by === FILE: name.dfy ==="
    
    print(f"\n[2] Generating {len(needs_generation)} variants in one model call...")
    text, inp, out, elapsed = call_flash(prompt, system, timeout=600)
    total_tokens += inp + out
    model_calls += 1
    print(f"    Done: {elapsed:.1f}s, {inp}+{out} tokens")
    
    dafny_files = extract_dafny_files(text)
    print(f"    Extracted {len(dafny_files)} .dfy files from response")
    
    if len(dafny_files) < len(needs_generation):
        print(f"    WARNING: expected {len(needs_generation)}, got {len(dafny_files)}")
    
    # ─── Step 3: Z3 sweep — verify all at once ────────────────────────
    # NOTE: Self-review step removed — testing showed the model rewrites
    # files that were fine, introducing errors. Z3 error feedback is more
    # reliable than self-review.
    print(f"\n[3] Z3 sweep — verifying {len(dafny_files)} files...")
    
    for j, (idx, params, vname) in enumerate(needs_generation):
        if j >= len(dafny_files):
            results["failed"].append({
                "name": vname, "index": idx,
                "source": "", "errors": "No file generated"
            })
            continue
        
        variant_path = STAGING_DIR / f"{vname}.dfy"
        variant_path.write_text(dafny_files[j])
        verified, output = verify_dafny(str(variant_path))
        error_lines = [l for l in output.split('\n') if 'Error' in l]
        
        if verified:
            print(f"    {vname}: ✅ PASS")
            results["passed"].append({"name": vname, "index": idx})
        else:
            print(f"    {vname}: ❌ FAIL ({len(error_lines)} errors)")
            results["failed"].append({
                "name": vname, "index": idx,
                "source": dafny_files[j],
                "errors": '\n'.join(error_lines[:10])
            })
    
    # ─── Step 4: Batch correction (if failures and retries remain) ────
    for attempt in range(retries):
        if not results["failed"]:
            break
        
        print(f"\n[4.{attempt+1}] Batch correction — {len(results['failed'])} failures, feeding errors back...")
        
        corrections = []
        for fail in results["failed"]:
            corrections.append(f"""
=== FILE: {fail['name']}.dfy ===
Previous errors:
{fail['errors'][:1000]}

Original code:
```dafny
{fail['source'][:2000]}
```

Fix the errors and output the corrected file.
""")
        
        correction_prompt = f"""You are a Dafny code generator. {len(results['failed'])} variants failed Z3 verification.
Fix each one. Output each corrected file separated by === FILE: name.dfy ===

{chr(10).join(corrections)}

Rules: same as before. All methods need requires/ensures. No reads on value types.
Bounds-check all seq access. Decreases on all recursion. Must verify by Z3.
"""
        if ref_card:
            correction_prompt += f"""
Dafny reference card:
```dafny
{ref_card}
```
"""
        
        text, cinp, cout, celapsed = call_flash(correction_prompt, system, timeout=600)
        total_tokens += cinp + cout
        model_calls += 1
        print(f"    Correction done: {celapsed:.1f}s, {cinp}+{cout} tokens")
        
        corrected_files = extract_dafny_files(text)
        print(f"    Extracted {len(corrected_files)} corrected files")
        
        # Re-verify corrections
        new_failed = []
        for j, fail in enumerate(results["failed"]):
            if j < len(corrected_files):
                variant_path = STAGING_DIR / f"{fail['name']}.dfy"
                variant_path.write_text(corrected_files[j])
                verified, output = verify_dafny(str(variant_path))
                
                if verified:
                    print(f"    {fail['name']}: ✅ PASS (correction attempt {attempt+1})")
                    results["passed"].append({"name": fail["name"], "index": fail["index"]})
                else:
                    error_lines = [l for l in output.split('\n') if 'Error' in l]
                    print(f"    {fail['name']}: ❌ FAIL ({len(error_lines)} errors)")
                    new_failed.append({
                        "name": fail["name"], "index": fail["index"],
                        "source": corrected_files[j],
                        "errors": '\n'.join(error_lines[:10])
                    })
            else:
                new_failed.append(fail)
        
        results["failed"] = new_failed
    
    # ─── Summary ──────────────────────────────────────────────────────
    total = len(results["passed"]) + len(results["failed"])
    pass_rate = len(results["passed"]) / total * 100 if total > 0 else 0
    sub_passes = total - len(needs_generation)
    
    print(f"\n{'='*60}")
    print(f"SUMMARY: {pattern_name}")
    print(f"  Passed: {len(results['passed'])}/{total} ({pass_rate:.0f}%)")
    print(f"  Failed: {len(results['failed'])}/{total}")
    print(f"  Total tokens: {total_tokens}")
    print(f"  Model calls: {model_calls}")
    print(f"  Substitution passes: {sub_passes} (free)")
    print(f"  Model passes: {len(results['passed']) - sub_passes}")
    print(f"{'='*60}\n")
    
    return {
        "passed": len(results["passed"]),
        "failed": len(results["failed"]),
        "tokens": total_tokens,
        "model_calls": model_calls,
        "pass_rate": pass_rate
    }


def main():
    parser = argparse.ArgumentParser(description="Batch variant generator (batch mode)")
    parser.add_argument("--pattern", type=str, help="Pattern name")
    parser.add_argument("--all", action="store_true", help="Run all patterns")
    parser.add_argument("--batch-size", type=int, default=10, help="Variants per pattern")
    parser.add_argument("--retries", type=int, default=3, help="Batch correction rounds")
    parser.add_argument("--no-wiki", action="store_true", help="Disable reference card")
    args = parser.parse_args()
    
    patterns = list(PARAM_GRIDS.keys()) if args.all else [args.pattern]
    use_wiki = not args.no_wiki
    
    all_results = {}
    for p in patterns:
        results = run_batch_pattern(p, args.batch_size, args.retries, use_wiki)
        all_results[p] = results
    
    # Final summary
    total_passed = sum(r["passed"] for r in all_results.values())
    total_failed = sum(r["failed"] for r in all_results.values())
    total = total_passed + total_failed
    total_tokens = sum(r["tokens"] for r in all_results.values())
    total_calls = sum(r.get("model_calls", 0) for r in all_results.values())
    pass_pct = total_passed / total * 100 if total > 0 else 0
    
    print(f"\n{'#'*60}")
    print(f"FINAL: {total_passed}/{total} variants passed ({pass_pct:.0f}%)")
    print(f"Total tokens: {total_tokens}")
    print(f"Total model calls: {total_calls}")
    print(f"Wiki: {'ON' if use_wiki else 'OFF'}")
    print(f"{'#'*60}")


if __name__ == "__main__":
    main()