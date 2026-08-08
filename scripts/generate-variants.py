#!/usr/bin/env python3
"""
Approach 4 — Batch variant generator.

For each pattern, generates variants by:
1. Parameter substitution (no model call — just swap delimiter, types, names)
2. Model generation (call flash for variants that need new logic)

Each variant is Z3-verified. Passes go to the DB. Fails get fed back for retry.

Usage:
    python scripts/generate-variants.py --pattern parser --batch-size 20 --retries 3
    python scripts/generate-variants.py --all --batch-size 10
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
STAGING_DIR = POSIT_ROOT / ".posit" / "staging" / "variants"
DAFNY = "C:/Users/goldf/.dotnet/tools/dafny.exe"
Z3 = "C:/Users/goldf/.dotnet/tools/z3/bin/z3.exe"
OLLAMA_URL = "http://localhost:11434/api/generate"
MODEL = "deepseek-v4-flash:cloud"

# Ensure staging exists
STAGING_DIR.mkdir(parents=True, exist_ok=True)

# ─── Parameter grids per pattern ──────────────────────────────────────────

PARAM_GRIDS = {
    "parser": {
        "delimiter": [",", "|", "\t", ";", " "],
        "quoteChar": ['""', '"', "'"],  # "" = none
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

# ─── Helpers ──────────────────────────────────────────────────────────────

def verify_dafny(dafny_path: str) -> tuple[bool, str]:
    """Run dafny verify, return (success, output)."""
    result = subprocess.run(
        [DAFNY, "verify", dafny_path,
         f"--solver-path", Z3,
         "--standard-libraries", "--allow-warnings"],
        capture_output=True, text=True, timeout=120
    )
    output = result.stdout + result.stderr
    # Dafny exits 0 on success, non-zero on errors
    verified = result.returncode == 0 and "Error" not in output
    if "verified" in output and "Error" not in output:
        verified = True
    return verified, output


def call_flash(prompt: str, system: str = "", timeout: int = 300) -> tuple[str, int, int, float]:
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
    # Strip thinking tags
    if "</think>" in text:
        text = text.split("</think>", 1)[1].strip()
    
    input_tokens = result.get("prompt_eval_count", 0)
    output_tokens = result.get("eval_count", 0)
    return text, input_tokens, output_tokens, elapsed


def extract_dafny(text: str) -> str:
    """Extract .dfy content from model response."""
    # Try ```dafny ... ``` blocks
    if "```dafny" in text:
        start = text.find("```dafny") + 8
        end = text.find("```", start)
        if end > start:
            return text[start:end].strip()
    # Try ``` ... ``` blocks
    if "```" in text:
        start = text.find("```") + 3
        # Skip language tag on same line
        nl = text.find("\n", start)
        if nl > start and nl < start + 20:
            start = nl + 1
        end = text.find("```", start)
        if end > start:
            return text[start:end].strip()
    return text.strip()


# ─── Variant generation strategies ───────────────────────────────────────

def gen_substitution_variant(pattern_name: str, params: dict, index: int) -> str:
    """Generate a variant by substituting parameters into the pattern file. No model call."""
    pattern_file = PATTERNS_DIR / f"{pattern_name}.dfy"
    source = pattern_file.read_text()
    
    # Substitute common placeholders
    for key, val in params.items():
        placeholder = "{{" + key + "}}"
        source = source.replace(placeholder, str(val))
    
    # Add variant metadata as comment
    param_str = ", ".join(f"{k}={v}" for k, v in params.items())
    header = f"// Variant {index}: {pattern_name} ({param_str})\n// Generated by parameter substitution\n\n"
    
    return header + source


def gen_model_variant(pattern_name: str, params: dict, index: int, base_source: str, errors: str = None) -> tuple[str, int, int, float]:
    """Generate a variant by calling the model. Returns (dafny_source, input_tokens, output_tokens, elapsed_s)."""
    param_str = json.dumps(params, indent=2)
    
    # Load the Dafny reference card to prevent common errors
    ref_card_path = PATTERNS_DIR / "dafny-reference-card.dfy"
    ref_card = ref_card_path.read_text() if ref_card_path.exists() else ""
    
    prompt = f"""You are a Dafny code generator. Generate a VARIANT of the {pattern_name} pattern.

Base pattern source:
```dafny
{base_source}
```

Parameters for this variant:
{param_str}

Rules:
1. Output ONLY a complete .dfy file — no explanation, no markdown fences
2. All methods MUST have requires/ensures contracts
3. All recursive functions MUST have decreases clauses
4. Do NOT use reads on string/seq parameters — they are value types, not objects
5. All array/seq access MUST be bounds-checked (prove 0 <= i < |s| before s[i])
6. Do NOT use assert unless you can prove it from the invariants
7. Keep it under 200 lines, 10 methods, 5 classes
8. Must be verifiable by Z3 (Dafny 4.11, --standard-libraries)

Dafny reference card (verified, follows these patterns exactly):
```dafny
{ref_card}
```
"""
    
    if errors:
        prompt += f"""
Previous attempt failed with these Z3 errors:
{errors[:2000]}

Fix these specific errors. The code must verify cleanly.
"""
    
    system = "You are a Dafny expert. You write code that Z3 verifies on the first try. You never use reads on value types. You always prove bounds before indexing. You always supply decreases for recursion. You follow the reference card patterns exactly."
    
    text, inp, out, elapsed = call_flash(prompt, system)
    dafny = extract_dafny(text)
    return dafny, inp, out, elapsed


# ─── Batch runner ─────────────────────────────────────────────────────────

def run_batch(pattern_name: str, batch_size: int, retries: int, use_model: bool = True):
    """Generate batch_size variants for a pattern, Z3-verify each, report results."""
    grid = PARAM_GRIDS.get(pattern_name)
    if not grid:
        print(f"[skip] No parameter grid for {pattern_name}")
        return
    
    # Generate parameter combinations
    keys = list(grid.keys())
    combos = list(product(*[grid[k] for k in keys]))
    
    # Convert to list of dicts
    param_combos = []
    for combo in combos[:batch_size * 3]:  # over-generate, we'll stop at batch_size
        params = dict(zip(keys, combo))
        # Convert lists to readable strings
        for k, v in params.items():
            if isinstance(v, list):
                params[k] = json.dumps(v)
        param_combos.append(params)
    
    print(f"\n{'='*60}")
    print(f"Pattern: {pattern_name} | Batch: {batch_size} | Retries: {retries}")
    print(f"Parameter combinations available: {len(param_combos)}")
    print(f"{'='*60}")
    
    results = {"passed": [], "failed": [], "errors": []}
    base_source = (PATTERNS_DIR / f"{pattern_name}.dfy").read_text()
    
    for i, params in enumerate(param_combos[:batch_size]):
        variant_name = f"{pattern_name}-v{i:03d}"
        variant_path = STAGING_DIR / f"{variant_name}.dfy"
        
        param_str = ", ".join(f"{k}={v}" for k, v in params.items())
        print(f"\n[{i+1}/{batch_size}] {variant_name} ({param_str})")
        
        # Strategy 1: Try parameter substitution first (fast, no model call)
        source = gen_substitution_variant(pattern_name, params, i)
        variant_path.write_text(source)
        
        verified, output = verify_dafny(str(variant_path))
        
        if verified:
            print(f"  ✅ PASS (substitution, no model call)")
            results["passed"].append({"name": variant_name, "params": params, "method": "substitution"})
            continue
        
        # If substitution didn't verify and we're not using model, skip
        if not use_model:
            print(f"  ❌ FAIL (substitution)")
            results["failed"].append({"name": variant_name, "params": params, "method": "substitution"})
            continue
        
        # Strategy 2: Call model with error feedback loop
        current_errors = output
        for attempt in range(retries):
            print(f"  🔄 Model call (attempt {attempt+1}/{retries})...")
            dafny_source, inp, out, elapsed = gen_model_variant(
                pattern_name, params, i, base_source, current_errors if attempt > 0 else None
            )
            
            if not dafny_source.strip():
                print(f"  ❌ Empty response ({elapsed:.1f}s, {inp}+{out} tokens)")
                current_errors = "Empty response from model"
                continue
            
            variant_path.write_text(dafny_source)
            verified, output = verify_dafny(str(variant_path))
            
            if verified:
                print(f"  ✅ PASS (model, attempt {attempt+1}, {elapsed:.1f}s, {inp}+{out} tokens)")
                results["passed"].append({
                    "name": variant_name, "params": params, "method": f"model-retry{attempt}",
                    "tokens": inp + out, "time": elapsed
                })
                break
            else:
                # Extract error summary
                error_lines = [l for l in output.split('\n') if 'Error' in l or 'error' in l]
                error_summary = '\n'.join(error_lines[:10])
                print(f"  ❌ FAIL ({len(error_lines)} errors, {elapsed:.1f}s, {inp}+{out} tokens)")
                current_errors = error_summary
        else:
            results["failed"].append({
                "name": variant_name, "params": params, "method": f"model-{retries}retries",
                "errors": current_errors[:500]
            })
    
    # Summary
    total = len(results["passed"]) + len(results["failed"])
    pass_rate = len(results["passed"]) / total * 100 if total > 0 else 0
    total_tokens = sum(r.get("tokens", 0) for r in results["passed"] + results["failed"])
    
    print(f"\n{'='*60}")
    print(f"SUMMARY: {pattern_name}")
    print(f"  Passed: {len(results['passed'])}/{total} ({pass_rate:.0f}%)")
    print(f"  Failed: {len(results['failed'])}/{total}")
    print(f"  Total tokens: {total_tokens}")
    print(f"  Substitution passes: {len([r for r in results['passed'] if r['method'] == 'substitution'])}")
    print(f"  Model passes: {len([r for r in results['passed'] if 'model' in r['method']])}")
    print(f"{'='*60}\n")
    
    return results


# ─── Main ─────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Batch variant generator for Approach 4")
    parser.add_argument("--pattern", type=str, help="Pattern name (parser, validator, etc.)")
    parser.add_argument("--all", action="store_true", help="Run all patterns")
    parser.add_argument("--batch-size", type=int, default=20, help="Variants per pattern")
    parser.add_argument("--retries", type=int, default=3, help="Model retries per variant")
    parser.add_argument("--no-model", action="store_true", help="Substitution only, no model calls")
    args = parser.parse_args()
    
    patterns = list(PARAM_GRIDS.keys()) if args.all else [args.pattern]
    
    all_results = {}
    for p in patterns:
        results = run_batch(p, args.batch_size, args.retries, use_model=not args.no_model)
        all_results[p] = results
    
    # Final summary
    total_passed = sum(len(r["passed"]) for r in all_results.values())
    total_failed = sum(len(r["failed"]) for r in all_results.values())
    total = total_passed + total_failed
    
    print(f"\n{'#'*60}")
    pass_pct = total_passed / total * 100 if total > 0 else 0
    print(f"FINAL: {total_passed}/{total} variants passed ({pass_pct:.0f}%)")
    print(f"{'#'*60}")


if __name__ == "__main__":
    main()