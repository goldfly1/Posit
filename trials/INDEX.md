# Trial Index

All trial outputs extracted from the DB. Each directory contains the architecture, Dafny contracts, C# code, and tests.

| Trial | Components | C# files | Test files | Green? |
|-------|------------|----------|------------|--------|
| T1-csv-to-json | 2 | 3 | 2 | ✅ |
| T12-task-scheduler | 8 | 10 | 4 | ✅ |
| T13-ecommerce | 13 | 13 | 1 | ✅ |
| T14-healthcare | 9 | 9 | 2 | ✅ |
| T5-document-processing | 3 | 3 | 2 | ✅ |
| T7-marketplace | 6 | 6 | 1 | ✅ |
| T8-cicd-pipeline | 9 | 10 | 2 | ✅ |
| trial-pPUgAs9F | 10 | 10 | 2 | ✅ |

## Directory Structure

Each trial directory contains:
- `architecture.json` — architect's component decomposition, pattern selections, stub assignments
- `dafny-contract.json` — verified Dafny contracts (Z3-proven)
- `dafny-verification.json` — Dafny with bodies filled and translated
- `csharp/` — individual C# files extracted from the SourceCodeBundle
- `tests/` — test files from the TestSuite
- `source-code-bundle.json` — raw SourceCodeBundle artifact
- `test-suite.json` — raw TestSuite artifact

## Trial Tiers

| Tier | Trials | Scale |
|------|--------|-------|
| Tier 0 | T1-T12 | 2-8 components |
| Tier 1 | T13-T16 | 10-15 components |
| Tier 2 | T17-T20 | 15-25 components |
| Tier 3 | T21-T24 | 25-40 components |