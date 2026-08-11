# Supplemental Handoff — Aug 11, 2026 (Session 2)

## Context

This is a supplemental handoff for the second session on Aug 10-11. The primary handoff is at `wiki/handoff-2026-08-08.md`. This covers work done after that handoff was written.

## What Was Done This Session

### Trials Run (9/9 green with real specs)

| Trial | Spec | Components | C# files | Tokens | Domain stubs |
|-------|------|------------|----------|--------|--------------|
| T1 | CSV-to-JSON CLI | 2 | 4 | 21K | — |
| T5 | Document processing | 3 | 4 | 35K | — |
| T7 | Marketplace | 6 | 11 | 49K | — |
| T8 | CI/CD pipeline | 8 | 18 | 94K | — |
| T12 | Task scheduler | 8 | 24 | 78K | scheduling, time-random |
| T13 | E-commerce (Tier 1) | 13 | 25 | 109K | ecommerce |
| T14 | Healthcare (Tier 1) | 9 | 30 | 91K | healthcare, time-random, ecommerce |
| T15 | Chat/messaging | 7 | 22 | 52K | chat, network-io |
| T12 (rerun) | Task scheduler | 8 | 28 | 83K | scheduling, time-random |

All green. All components named for their domain. Zero CSV bias since CLI spec fix.

### Fixes Applied

1. **CLI spec fix** — `args[0]` was `--spec` flag, not spec text. Root cause of CSV bias. Model was blind.
2. **`--allow-warnings` on translate cs** — graph pattern quantifier warning was silently aborting C# translation.
3. **Carapace prefix matching** — `DagResolver` matches `DagResolverExtern.cs`.
4. **Carapace at 3 points** — indexer (200-line, 10-method, 5-class), Architecture phase (skeleton check), C# Implementation (generated file check).
5. **Dedup in indexer** — hash code (minus headers), skip identical variants. 726 make-weight filtered from 1379 files.
6. **Parallel Z3** — ThreadPoolExecutor with 6 workers (ProcessPoolExecutor fails on Windows).
7. **Real string substitution** — Mode 2 in generate-batch.py replaces actual code values (ToUpper→ToLower, delimiter, entityType, etc.).
8. **Domain stubs built out** — 17 templates (6 I/O + 11 domain). PatternRegistry matches spec keywords to domain stubs.
9. **Semantic search wired** — PatternRegistry.SearchVariants() embeds component description via Ollama, queries pgvector for closest match. Suggest() tries semantic first (>0.7 similarity), falls back to keyword matching. Architecture phase logs the result.
10. **Prompt updated** — `patternName` is now OPTIONAL with "LEAVE EMPTY (first option)". `componentDescription` field added. Flash still fills in patterns for specialists (cog trace confirmed this is deliberate judgment, not a bug).
11. **Thinking ON/OFF** — cog trace confirmed flash understands the prompt. Turned back OFF (65K output runaway risk).
12. **Runtime collision** — `--include-runtime` invalid flag removed. Dafny 4.11 includes minimal runtime by default. Single module compiles clean. Multi-module assembly is a packaging problem (see Harness Findings).
13. **DB backup** — schema (12KB) + registry CSV (54KB) + variant files in git. DafnyRuntime DLL project.
14. **Trial artifacts gathered** — 8 trials extracted from DB to `trials/` with INDEX.md.

### Registry State

| Metric | Count |
|--------|-------|
| Files on disk | 1,447 |
| DB indexed | 794 |
| Z3-verified | 690/794 (87%) |
| Unique (deduped) | ~653 |
| Patterns with model-generated variety | 8 (adapter 100, validator 100, observer 99, parser 99, repository 86, cache 60, scheduler 60, pipeline 93) |
| Patterns with substitution only (1 unique) | 8 (aggregator, builder, filter, iterator, reducer, state-machine, strategy, transformer) |
| Graph | 1 (model generation failed on quantifiers, substitution only) |
| Domain stub templates | 17 (6 I/O + 11 domain) |
| Pattern files | 20 (all Z3-verified, all carapace-checked) |

### Harness Findings (verify-trial.py)

**What works:**
- Re-translates Dafny from trial artifacts to C#
- Single module compiles clean (0 errors, 79 warnings) with `--include-runtime`
- Namespace renaming (`_module` → `_module_{name}`) prevents `__default` collisions
- Runtime stripping from non-first files reduces errors from 971 → 61

**What doesn't work yet:**
- Multi-module assembly — 61 errors remain. Each translated file references `_module.Result<T>` (from `result.dfy` include). Renaming the namespace breaks cross-references.
- Stub namespace mismatch — stub templates had `using _module;`, fixed to `using _module_{{ComponentName}};` in templates. Old trial artifacts still have the old reference.
- The DafnyRuntime DLL project is incomplete — missing `Rune` type and others. Single files with `--include-runtime` work; the shared DLL doesn't.

**Root cause:** Dafny's C# translation is designed for one-file-at-a-time. Each file is self-contained with its own runtime + dependencies. Multi-module assembly requires either:
- One DLL per module (Dafny `build` command), reference them
- Or a proper Dafny project file that compiles all modules together
- Or accepting single-module verification as the target

## Moratorium List (trial runs on hold until these are done)

1. **Verify Dafny contracts encode right invariants** — Z3 proves consistency, not that we posited the right contract. Need to read actual contracts and confirm (e.g., RetryManager has retry invariant, not just a method named Retry). Can be done by hand for flavor, needs harness for scale.

2. **Compile C# output into real project** — Harness built (`scripts/verify-trial.py`). Single module compiles. Multi-module assembly needs proper Dafny project or one-DLL-per-module approach. 61 errors remaining (cross-module references).

3. **xUnit tests** — Tests are Dafny `{:axiom}` contract stubs, not executable C#. Need to generate xUnit test projects that call translated methods and assert results. `dotnet test` should pass.

4. **Semantic search firing** — ✅ Wired. Architect still fills in `patternName` for specialists (deliberate, confirmed by cog trace). Registry fires as fallback for generic components. Working as designed — hybrid approach.

5. **Gather trial artifacts** — ✅ Done. 8 trials in `trials/` with INDEX.md.

## Approved Models (architecture phase)

| Model | Status |
|-------|--------|
| deepseek-v4-flash:cloud | ✅ Approved — primary, fast, cheap, good pattern selection |
| glm-5.2:cloud | ✅ Approved — slower, more consistent on edge cases |
| Others | ⚠️ Untested — try as we go, approve if green |

## Key Files Added This Session

- `scripts/verify-trial.py` — verification harness (translate, strip, compile)
- `trials/` — 8 trial directories with artifacts + INDEX.md
- `trials/INDEX.md` — trial scorecard
- `patterns/csharp-stubs/` — 17 domain + I/O stub templates
- `src/Posit.DafnyRuntime/` — shared runtime project (incomplete, see harness findings)
- `.posit/backups/posit-db-schema.sql` — DB schema backup
- `.posit/backups/posit-registry-data.csv` — registry data backup (54KB, no embeddings)

## Next Session Priorities

1. Fix multi-module C# assembly (harness #2) — try one-DLL-per-module or Dafny project file
2. xUnit test generation (#3)
3. Contract review by hand (#1) — read T12 contracts, confirm invariants
4. Registry re-index with graph variants
5. Test more `:cloud` models for approved list
6. Then resume trials (T15-T16 Tier 1, then Tier 2-3)

## Git State

- **Branch:** main
- **Latest commit:** `9c21325` — Harness progress: strip runtime, 61 errors remaining
- **Working tree:** Clean
- **GitHub:** https://github.com/goldfly1/Posit — all pushed
- **Commit count:** ~100+