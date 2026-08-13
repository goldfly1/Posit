# The Carapace Doctrine

The skeleton is the carapace — the source of truth for everything that is contractual. The orchestrator's job is to be the enforcer that compels every component to comply with it. Nothing leaves the door unless it matches what the skeleton says.

The orchestrator needs to know and hold every detail of the skeleton and use it as a contract checklist at every phase boundary. Not just "does a directory exist with this component name" but:

- Does every filename trace back to a skeleton entry?
- Does every stub reference resolve to a real Dafny module?
- Does every `using _module_X` have a corresponding `_module_X` in the Dafny output?
- Does every component classified as `io-shell` have its stubs, and only its stubs?
- Does every component classified as `dafny` have its pattern, and only its pattern?

The skeleton says what should exist. The orchestrator enforces that what exists matches. If something exists that the skeleton doesn't name, it's rejected. If something the skeleton names is missing, it's flagged. That's the carapace principle applied fully — not just at the directory level, but at the filename, type reference, and stub-binding level.