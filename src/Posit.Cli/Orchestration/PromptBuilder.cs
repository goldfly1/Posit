using Posit.Tools;
using System.Text;

namespace Posit.Cli.Orchestration;

using Posit.Contracts.Core;

/// <summary>
/// Builds phase-aware prompt templates for the C#-direct pipeline.
/// Architecture: model decomposes spec, writes C# interfaces, defines test cases.
/// C# Implementation: model writes C# class bodies implementing the interfaces.
/// QA: model generates test data from spec + test case descriptions.
/// </summary>
public static class PromptBuilder
{
    public static PromptTemplate Build(PhaseId phaseId, PatternRegistry? registry)
    {
        var (system, format) = phaseId.Value switch
        {
            "architecture" => (BuildArchitecturePrompt(registry), BuildArchitectureFormat()),
            "csharp-implementation" => (BuildCSharpImplPrompt(), "{ }"),
            "qa" => (BuildQaPrompt(), "{ }"),
            _ => ($"You are a Senior Developer executing the {phaseId.Value} phase. " +
                  "Follow the input artifacts and correction signals. Respond with valid JSON only.",
                  "{ }")
        };
        return new PromptTemplate
        {
            PhaseId = phaseId, Version = new PromptVersion("1.0.0"),
            SystemPrompt = system, OutputFormatSpec = format,
            ModelTier = ModelTier.Fast, Temperature = 0.2, MaxOutputTokens = 8192,
            OutputFormat = OutputFormat.Json, OutputSchemaRef = "ArchitectureContract",
            Status = PromptStatus.Active
        };
    }

    // ── Architecture ──────────────────────────────────────────────────────────

    private static string BuildArchitecturePrompt(PatternRegistry? registry)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a Senior Software Architect. Your job: decompose the user's spec into components that will be implemented in C# and wired together.");
        sb.AppendLine();
        sb.AppendLine("KEEP IT SIMPLE: 2-3 components. One io-shell for I/O, one or two logic components.");
        sb.AppendLine("NEVER more than 4. Over-decomposition is the #1 cause of failure.");
        sb.AppendLine();
        sb.AppendLine("Each component needs:");
        sb.AppendLine("  - classification: \"logic\" (pure logic, no I/O) or \"io-shell\" (I/O handling)");
        sb.AppendLine("  - stubNames: for io-shell components, one or more stub names from the list below");
        sb.AppendLine("  - methodSignatures: [{name, params:[{name,type}], returnType}]");
        sb.AppendLine("    These signatures define the C# interface. Use native C# types:");
        sb.AppendLine("    string, int, bool, string[], List<string>, long, double.");
        sb.AppendLine("    Be precise — the wiring code connects these signatures deterministically.");
        sb.AppendLine("  - csharpInterface: for logic components, write the C# interface definition.");
        sb.AppendLine("    This IS the carapace — the structural contract the implementation must satisfy.");
        sb.AppendLine("    Rules for the interface:");
        sb.AppendLine("      - Wrap in 'namespace <Name> { ... }' — the class and interface must share the namespace");
        sb.AppendLine("      - Start with 'public interface I<Name> {'");
        sb.AppendLine("      - Method signatures only — no implementation, no default methods");
        sb.AppendLine("      - Use native C# types (string, int, bool, string[], etc.)");
        sb.AppendLine("      - Add XML doc comments describing each method's contract");
        sb.AppendLine("      - Test cases as comments: // test: <description> → <expected>");
        sb.AppendLine("    Example interface:");
        sb.AppendLine("      namespace TempConverter {");
        sb.AppendLine("          public interface ITempConverter {");
        sb.AppendLine("              /// <summary>Convert temperature between units.</summary>");
        sb.AppendLine("              double Convert(double temp, string fromUnit, string toUnit);");
        sb.AppendLine("              // test: 32 F → 0 C");
        sb.AppendLine("              // test: 0 C → 32 F");
        sb.AppendLine("          }");
        sb.AppendLine("      }");
        sb.AppendLine("  - dependencies: names of OTHER components this one calls");
        sb.AppendLine("  - connections: [{fromMethod, toComponent, toMethod, argMappings:[]}]");
        sb.AppendLine("    ONLY the orchestrator (io-shell with entryType) has connections. Logic components have NONE.");
        sb.AppendLine("    fromMethod = method on THIS orchestrator (usually \"Run\"). toComponent = component Name. toMethod = their method.");
        sb.AppendLine("    Connections form a chain: each step's output feeds the next. Last step prints the result.");
        sb.AppendLine("  - entryType: \"file\" or \"stdin\" (on the orchestrator)");
        sb.AppendLine("  - branchCondition: error branch description if a step returns isValid bool");
        sb.AppendLine("  - testCases: [{id, name, targetType, description, expectedBehavior}]");
        sb.AppendLine("    expectedBehavior = SHAPE of output (\"prints result\", \"prints error and exits 1\"), NOT specific values.");
        sb.AppendLine();

        if (registry != null)
        {
            sb.AppendLine("STUBS (for io-shell components):");
            foreach (var s in registry.GetAllCSharpStubs().OrderBy(s => s.Name))
            {
                var kws = s.AutoBindKeywords.Length > 0 ? $" [{string.Join(", ", s.AutoBindKeywords)}]" : "";
                sb.AppendLine($"  {s.Name}{kws}");
            }
            sb.AppendLine();
            sb.AppendLine("STUB SELECTION — match the stub to the logic method's input type:");
            sb.AppendLine("  If the method takes string[] (lines): use ReadLines.");
            sb.AppendLine("  If the method takes string (file content): use ReadFile.");
            sb.AppendLine("  If the method takes int/bool: parse from string args.");
            sb.AppendLine("CONNECTION CHAIN: the orchestrator's Run method calls the stub FIRST, then passes the result to the logic method.");
            sb.AppendLine("  Example: Run(filePath) → ReadLines(filePath) → AnalyzeLogs(lines, filterLevel)");
            sb.AppendLine();
        }

        sb.AppendLine("Use PascalCase for all names. Never kebab-case in name fields.");
        sb.AppendLine();
        sb.AppendLine("Output JSON: { \"systemContext\": \"...\", \"components\": [{ \"id\": \"...\", \"name\": \"...\",");
        sb.AppendLine("  \"responsibility\": \"...\", \"publicSurface\": [\"...\"], \"internals\": \"...\", \"dependencies\": [\"...\"],");
        sb.AppendLine("  \"layer\": 0, \"tech\": \"C#\", \"classification\": \"logic\"|\"io-shell\",");
        sb.AppendLine("  \"stubNames\": [\"...\"]|[], \"methodSignatures\": [{\"name\":\"...\",\"params\":[{\"name\":\"...\",\"type\":\"...\"}],");
        sb.AppendLine("  \"returnType\":\"...\"}], \"csharpInterface\": \"public interface I<Name> { ... }\"|null,");
        sb.AppendLine("  \"connections\": [{\"fromMethod\":\"...\",\"toComponent\":\"...\",");
        sb.AppendLine("  \"toMethod\":\"...\",\"argMappings\":[]}], \"entryType\": \"file\"|\"stdin\", \"branchCondition\": \"...\"|null,");
        sb.AppendLine("  \"testCases\": [{\"id\":\"...\",\"name\":\"...\",\"targetType\":\"...\",\"description\":\"...\",\"expectedBehavior\":\"...\"}] }],");
        sb.AppendLine("  \"deploymentTopology\": \"...\" }");
        return sb.ToString();
    }

    private static string BuildArchitectureFormat() =>
        "{ \"systemContext\": \"...\", \"components\": [{ \"id\": \"...\", \"name\": \"...\", ... }], \"deploymentTopology\": \"...\" }";

    // ── C# Implementation ──────────────────────────────────────────────────────

    private static string BuildCSharpImplPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a Senior C# Developer. Your job: implement the logic methods defined in the C# interface.");
        sb.AppendLine();
        sb.AppendLine("For each logic component:");
        sb.AppendLine("  1. Read the C# interface — it defines the contract you must satisfy");
        sb.AppendLine("  2. Write a class that implements the interface: class <Name> : I<Name>");
        sb.AppendLine("  3. Implement every method from the interface with correct logic");
        sb.AppendLine("  4. Use only native C# types — no Dafny runtime types");
        sb.AppendLine("  5. Handle edge cases: null inputs, empty collections, boundary values");
        sb.AppendLine();
        sb.AppendLine("Output: raw C# source code (no markdown fences, no explanation).");
        sb.AppendLine("The code will be compiled with dotnet build and tested in Docker.");
        return sb.ToString();
    }

    // ── QA ──────────────────────────────────────────────────────────────────────

    private static string BuildQaPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a QA Engineer. Your job: generate test input data for the Docker test harness.");
        sb.AppendLine();
        sb.AppendLine("For each test case defined in the architecture contract:");
        sb.AppendLine("  1. Generate realistic input data that exercises the described scenario");
        sb.AppendLine("  2. The input must match the entry type (file content or stdin line)");
        sb.AppendLine("  3. Expected behavior describes the SHAPE of the output, not exact values");
        sb.AppendLine();
        sb.AppendLine("Output JSON: array of { \"testCaseId\": \"...\", \"input\": \"...\" }");
        return sb.ToString();
    }
}