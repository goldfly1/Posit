# The Carapace Doctrine

The C# interface is the carapace — the source of truth for everything that is contractual. The orchestrator's job is to be the enforcer that compels every component to comply with it. Nothing leaves the door unless it matches what the interface says.

The orchestrator needs to know and hold every detail of the interface and use it as a contract checklist at every phase boundary. Not just "does a directory exist with this component name" but:

- Does every filename trace back to a component in the architecture contract?
- Does every method in the implementation match a signature in the C# interface?
- Does every component classified as `io-shell` have its stubs, and only its stubs?
- Does every component classified as `logic` have its C# interface, and only its interface?
- Are all types native C# (string, int, bool, string[], double, long)?

The interface says what should exist. The orchestrator enforces that what exists matches. If something exists that the interface doesn't declare, it's rejected. If something the interface declares is missing, it's flagged. That's the carapace principle applied fully — not just at the directory level, but at the method signature, type, and stub-binding level.

## C#-Direct (Aug 24)

Dafny is gone. The carapace is now a C# `interface I<Name> { ... }` file, not a `.dfy` file. The model writes a class that implements the interface. `dotnet build` is the compile gate. Docker tests are the behavioral gate. No Z3, no formal verification.