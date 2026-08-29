# Interface Pattern: Config Merger (Provider Chain)

## Problem Shape
Read multiple key-value sources → merge into a unified view → later sources override earlier ones → expose merged key-value access. This is the proven .NET pattern for "read N sources, merge, query." Maps directly to T12 (config merger) and T5 (CSV merger).

## Source
Microsoft.Extensions.Configuration from dotnet/runtime.
Path: `src/libraries/Microsoft.Extensions.Configuration` and `Microsoft.Extensions.Configuration.Abstractions`.
Reference: https://github.com/dotnet/runtime

## Spec Verbs
add source, build, load, try-get, set, get-child-keys

## Architecture Overview

The .NET config system decomposes multi-source merging into four interface layers:

```
IConfigurationSource        — factory: knows how to create ONE provider
        ↓ .Build(builder)
IConfigurationProvider      — reader: loads + serves key-values from ONE source
        ↓ registered in
IConfigurationBuilder       — collector: accumulates sources, builds root
        ↓ .Build()
IConfigurationRoot          — merged view: iterates providers, last-wins
        ↓ indexer / GetSection
IConfiguration / IConfigurationSection — consumer-facing read API
```

## Component Interfaces (Actual C# Signatures)

### IConfigurationSource — source factory

```csharp
namespace Microsoft.Extensions.Configuration
{
    // Represents a source of configuration key/values.
    // Each source knows how to build its own provider.
    public interface IConfigurationSource
    {
        // Builds the provider for this source.
        // Called by ConfigurationBuilder.Build() for each registered source.
        IConfigurationProvider Build(IConfigurationBuilder builder);
    }
}
```

### IConfigurationProvider — per-source reader

```csharp
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Extensions.Configuration
{
    // Provides configuration key/values for a single source.
    public interface IConfigurationProvider
    {
        // Tries to get a configuration value for the specified key.
        // Returns true if found; false if this provider doesn't have the key.
        bool TryGet(string key, out string? value);

        // Sets a configuration value for the specified key (in-memory override).
        void Set(string key, string? value);

        // Change-tracking token (can return NullChangeToken if not supported).
        IChangeToken GetReloadToken();

        // Loads configuration values from the source (file, env vars, etc.).
        void Load();

        // Returns immediate descendant keys for a parent path.
        // earlierKeys = keys from preceding providers (for merging child key sets).
        IEnumerable<string> GetChildKeys(
            IEnumerable<string> earlierKeys,
            string? parentPath);
    }
}
```

### IConfigurationBuilder — source collector

```csharp
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration
{
    // Builds application configuration from multiple sources.
    public interface IConfigurationBuilder
    {
        // Shared state between builder and sources (for cross-source data passing).
        IDictionary<string, object> Properties { get; }

        // Ordered list of sources added via Add().
        IList<IConfigurationSource> Sources { get; }

        // Adds a new source. Returns same builder (fluent chain).
        IConfigurationBuilder Add(IConfigurationSource source);

        // Builds the merged root from all registered sources.
        IConfigurationRoot Build();
    }
}
```

### IConfigurationRoot — merged view

```csharp
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration
{
    // Root of the configuration hierarchy — holds the provider chain.
    public interface IConfigurationRoot : IConfiguration
    {
        // Forces reload from all underlying providers.
        void Reload();

        // The ordered provider chain (sources in registration order).
        IEnumerable<IConfigurationProvider> Providers { get; }
    }
}
```

### IConfiguration — consumer-facing read API

```csharp
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Extensions.Configuration
{
    // Key/value application configuration properties.
    public interface IConfiguration
    {
        // Indexer: get or set a value by key.
        string? this[string key] { get; set; }

        // Gets a sub-section (never null — returns empty section if missing).
        IConfigurationSection GetSection(string key);

        // Gets immediate descendant sub-sections.
        IEnumerable<IConfigurationSection> GetChildren();

        // Change-tracking token.
        IChangeToken GetReloadToken();
    }
}
```

### IConfigurationSection — hierarchical node

```csharp
namespace Microsoft.Extensions.Configuration
{
    // A section of configuration values within the hierarchy.
    public interface IConfigurationSection : IConfiguration
    {
        string Key { get; }       // key in parent
        string Path { get; }      // full path from root
        string? Value { get; set; } // leaf value
    }
}
```

## Type Chain

```
IConfigurationSource → (Build) → IConfigurationProvider
IList<IConfigurationSource> → (ConfigurationBuilder.Build) → IConfigurationRoot
IConfigurationRoot → (indexer) → string? value
IConfigurationRoot → (GetSection) → IConfigurationSection → (Value) → string?
```

Data flow through the pipeline:

```
Source1.Build(builder) → Provider1
Source2.Build(builder) → Provider2
Source3.Build(builder) → Provider3
                     ↓
    ConfigurationRoot([Provider1, Provider2, Provider3])
                     ↓
    this["key"] → GetConfiguration iterates providers[Count-1 → 0]
                 → first TryGet hit wins (last provider wins)
```

## Merge Semantics: Last Provider Wins

The critical merge logic is in `ConfigurationRoot.GetConfiguration` — an internal
static method that implements the override chain:

```csharp
internal static string? GetConfiguration(
    IList<IConfigurationProvider> providers,
    string key)
{
    // Iterate BACKWARDS — last registered provider wins.
    for (int i = providers.Count - 1; i >= 0; i--)
    {
        IConfigurationProvider provider = providers[i];

        if (provider.TryGet(key, out string? value))
        {
            return value;  // first hit (from the end) = override value
        }
    }

    return null;  // no provider has this key
}
```

**Key insight:** The provider list is in *registration order*. Reading iterates
in *reverse* order. So `builder.Add(jsonSource).Add(envSource).Add(cliSource)`
means CLI overrides env overrides JSON. The last `Add()` call has the highest
priority.

### Set semantics: write to ALL providers

```csharp
internal static void SetConfiguration(
    IList<IConfigurationProvider> providers,
    string key, string? value)
{
    if (providers.Count == 0)
        throw new InvalidOperationException("No sources");

    // Set propagates to every provider in the chain.
    foreach (IConfigurationProvider provider in providers)
    {
        provider.Set(key, value);
    }
}
```

**Asymmetric read/write:** Read = last-wins (reverse iteration). Write = broadcast to all providers.

## Build Pipeline: Source → Provider → Root

`ConfigurationBuilder.Build()` converts sources to providers:

```csharp
public IConfigurationRoot Build()
{
    var providers = new List<IConfigurationProvider>();

    foreach (IConfigurationSource source in _sources)
    {
        IConfigurationProvider provider = source.Build(this);
        providers.Add(provider);
    }

    return new ConfigurationRoot(providers);
}
```

`ConfigurationRoot` constructor loads all providers:

```csharp
public ConfigurationRoot(IList<IConfigurationProvider> providers)
{
    _providers = providers;

    foreach (IConfigurationProvider p in providers)
    {
        p.Load();  // each provider loads its own source (file, env, etc.)
        // ... change token registration
    }
}
```

## Child Key Merging

`GetChildKeys` merges key sets across providers. Each provider receives
`earlierKeys` (keys from preceding providers) and returns its own keys
merged with the earlier set. This is a **forward-chaining merge** for key
enumeration — the opposite direction of value reads:

```csharp
// In each provider's GetChildKeys implementation:
// 1. Start with earlierKeys (from preceding providers)
// 2. Add this provider's keys for parentPath
// 3. Return the union, sorted
IEnumerable<string> GetChildKeys(
    IEnumerable<string> earlierKeys,
    string? parentPath);
```

**Key insight:** Value reads use *reverse* iteration (last wins). Key enumeration
uses *forward* iteration (accumulate all keys). This dual-direction pattern lets
the merged view present all available keys while serving the highest-priority
value for each key.

## Posit Application: T12 and T5

### T12 — Multi-File Config Merger (two INI files, conflict detection)

Map the .NET config pattern to Posit's carapace:

| .NET Role              | Posit Component         | Interface                    |
|------------------------|-------------------------|------------------------------|
| IConfigurationSource   | ConfigSource            | `IConfigSource`              |
| IConfigurationProvider | ConfigReader            | `IConfigReader`              |
| ConfigurationBuilder   | Merger                  | `IConfigMerger`              |
| IConfigurationRoot     | MergedConfig            | `IMergedConfig`              |

Recommended Posit interfaces:

```csharp
// Reads one config file into key-value pairs
interface IConfigReader {
    // Returns key-value pairs from one source file.
    // Throws on parse error (malformed INI syntax).
    List<KeyValuePair<string, string>> Read(string filePath);
}

// Merges two parsed sources with last-wins override semantics
interface IConfigMerger {
    // Merges two key-value collections. Second source overrides first.
    // Returns merged list. Throws on conflicting duplicate keys
    // (optional: conflict detection mode).
    List<KeyValuePair<string, string>> Merge(
        List<KeyValuePair<string, string>> source1,
        List<KeyValuePair<string, string>> source2);
}

// Serializes merged config to output format
interface IConfigSerializer {
    string Serialize(List<KeyValuePair<string, string>> merged);
}
```

Type chain for T12:
```
string (file1 path) → Read → List<KV> (parsed1)
string (file2 path) → Read → List<KV> (parsed2)
(List<KV> + List<KV>) → Merge → List<KV> (merged) → Serialize → string (output)
```

### T5 — Multi-File CSV Merger (two CSV files, validate + merge)

Same structural pattern — adapt the reader/merger/serializer trio:

```csharp
interface ICsvParser {
    string[][] Parse(string[] lines);
}

interface IMergeValidator {
    // Validates compatibility (same column count) and merges.
    // Returns merged rows or throws on mismatch.
    string[][] ValidateAndMerge(string[][] rows1, string[][] rows2);
}

interface ICsvSerializer {
    string Serialize(string[][] rows);
}
```

### Pattern Rules for the Code Generator

1. **Source → Provider separation:** Each source knows how to build its own reader.
   Don't conflate "where data comes from" with "how data is read."

2. **Ordered list, reverse-read:** Store sources in a list in registration order.
   Read in reverse order for last-wins override semantics.

3. **TryGet pattern (not Get):** Use `TryGet(key, out value)` returning bool, not
   `Get(key)` that returns null. This distinguishes "key absent" from "key present
   with null value" — critical for override chains where a provider may not have
   the key at all.

4. **Merge = validate + combine in one method:** The merge method takes BOTH
   parsed inputs and returns the merged result. Do NOT split into separate
   `Validate(bool)` + `Merge(data)` — that creates a type-chain mismatch.
   (This matches the constraint in multi-file-merger.md.)

5. **Asymmetric read/write:** Reads do reverse iteration (last wins). Writes
   broadcast to all. For Posit's read-only merge trials, only the read path
   matters — but the pattern is worth documenting for future read-write trials.

6. **Child key enumeration = forward accumulation:** If the trial needs to list
   all keys, accumulate forward across sources. If it only needs values, use
   reverse iteration. These are different merge directions.

7. **Builder as fluent collector:** `Add().Add().Build()` is a clean carapace
   pattern — the builder accumulates sources, Build() converts them to providers.
   For Posit's two-file trials, the builder may be implicit (two Read calls
   followed by one Merge call), but the pattern scales.

## Proven Trials
- T12 (Config Merger — two INI files, merge with conflict detection)
- T5 (Multi-File CSV Merger — two CSV files, validate compatibility, merge)

## Entry Type
file (two file paths as args[0] and args[1])

## Key Constraint
The merge/validate method takes TWO inputs (both parsed datasets) and returns
the merged result. Do NOT split into separate Validate(bool) + Merge(data) —
that creates a type-chain mismatch. Combine validation and merging into one
method that returns data. This mirrors `IConfigurationProvider.GetChildKeys`
which receives `earlierKeys` (previous provider's data) and returns merged
output — data flows through, not around.