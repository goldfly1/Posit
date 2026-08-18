using Posit.Tools;
using System.Text;

namespace Posit.Cli.Orchestration;

using Posit.Contracts.Core;

/// <summary>
/// Builds phase-aware prompt templates. The architecture prompt is deliberately
/// SHORT — pointing the model at resources rather than inlining them.
/// The correction loop (PreviousOutput + CorrectionSignal) handles errors.
/// </summary>
public static class PromptBuilder
{
    public static PromptTemplate Build(PhaseId phaseId, PatternRegistry? registry)
    {
        var (system, format) = phaseId.Value switch
        {
            "architecture" => (BuildArchitecturePrompt(registry), BuildArchitectureFormat()),
            _ => ($"You are executing the {phaseId.Value} phase of the Posit spec compiler. " +
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

    private static string BuildArchitecturePrompt(PatternRegistry? registry)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the Architecture phase of the Posit spec compiler.");
        sb.AppendLine("Decompose the user's spec into components that will be verified in Dafny and wired in C#.");
        sb.AppendLine();
        sb.AppendLine("KEEP IT SIMPLE: 2-3 components. One io-shell for I/O, one or two dafny for logic.");
        sb.AppendLine("NEVER more than 4. Over-decomposition is the #1 cause of failure.");
        sb.AppendLine();
        sb.AppendLine("Each component needs:");
        sb.AppendLine("  - classification: \"dafny\" (provable logic) or \"io-shell\" (I/O)");
        sb.AppendLine("  - patternName: ALWAYS null for dafny components. The pipeline writes custom");
        sb.AppendLine("    Dafny from the method signatures you declare. Z3 verifies it.");
        sb.AppendLine("  - io-shell: stubNames = one or more stub names from the list below");
        sb.AppendLine("  - methodSignatures: [{name, params:[{name,type}], returnType, returnDafnyType}]");
        sb.AppendLine("    These signatures ARE the spec for the Dafny implementation. Be precise.");
        sb.AppendLine("  - dependencies: names of OTHER components this one calls");
        sb.AppendLine("  - connections: [{fromMethod, toComponent, toMethod, argMappings:[]}]");
        sb.AppendLine("    ONLY the orchestrator (io-shell with entryType) has connections. Dafny components have NONE.");
        sb.AppendLine("    fromMethod = method on THIS orchestrator (usually \"Run\"). toComponent = component Name. toMethod = their method.");
        sb.AppendLine("    Connections form a chain: each step's output feeds the next. Last step prints the result.");
        sb.AppendLine("    For multi-input specs, chain multiple readers before the merge step.");
        sb.AppendLine("  - entryType: \"file\" or \"stdin\" (on the orchestrator)");
        sb.AppendLine("  - branchCondition: error branch description if a step returns isValid bool");
        sb.AppendLine("  - testCases: [{id, name, targetType, description, expectedBehavior}]");
        sb.AppendLine("    expectedBehavior = SHAPE of output (\"prints result\", \"prints error and exits 1\"), NOT specific values.");
        sb.AppendLine();

        if (registry != null)
        {
            // Stubs — still needed for io-shell (C# I/O portals)
            sb.AppendLine("STUBS (for io-shell components):");
            foreach (var s in registry.GetAllCSharpStubs().OrderBy(s => s.Name))
            {
                var kws = s.AutoBindKeywords.Length > 0 ? $" [{string.Join(", ", s.AutoBindKeywords)}]" : "";
                sb.AppendLine($"  {s.Name}{kws}");
            }
            sb.AppendLine();
            sb.AppendLine("For CSV: ReadLines (seq<string>). For JSON/text: ReadFile (string).");
            sb.AppendLine();
        }

        sb.AppendLine("Dafny syntax: method Name(params) returns (r: type) { body }");
        sb.AppendLine("  Types: string, int, bool, seq<T>, seq<seq<T>>. |s|=length, s[i]=element, +=concat, :=assign.");
        sb.AppendLine("  Reference: wiki/reference/dafny-stdlib.md (64 modules), wiki/reference/dafny-runtime-cs.md (C# runtime).");
        sb.AppendLine();
        sb.AppendLine("Use PascalCase for all names. Never kebab-case in name fields.");
        sb.AppendLine();
        sb.AppendLine("Output JSON: { \"systemContext\": \"...\", \"components\": [{ \"id\": \"...\", \"name\": \"...\",");
        sb.AppendLine("  \"responsibility\": \"...\", \"publicSurface\": [\"...\"], \"internals\": \"...\", \"dependencies\": [\"...\"],");
        sb.AppendLine("  \"layer\": 0, \"tech\": \"C#\", \"classification\": \"dafny\"|\"io-shell\", \"patternName\": \"...\"|null,");
        sb.AppendLine("  \"stubNames\": [\"...\"]|[], \"methodSignatures\": [{\"name\":\"...\",\"params\":[{\"name\":\"...\",\"type\":\"...\"}],");
        sb.AppendLine("  \"returnType\":\"...\",\"returnDafnyType\":\"...\"}], \"connections\": [{\"fromMethod\":\"...\",\"toComponent\":\"...\",");
        sb.AppendLine("  \"toMethod\":\"...\",\"argMappings\":[]}], \"entryType\": \"file\"|\"stdin\", \"branchCondition\": \"...\"|null,");
        sb.AppendLine("  \"testCases\": [{\"id\":\"...\",\"name\":\"...\",\"targetType\":\"...\",\"description\":\"...\",\"expectedBehavior\":\"...\"}] }],");
        sb.AppendLine("  \"deploymentTopology\": \"...\" }");
        return sb.ToString();
    }

    private static string BuildArchitectureFormat() =>
        "{ \"systemContext\": \"...\", \"components\": [{ \"id\": \"...\", \"name\": \"...\", ... }], \"deploymentTopology\": \"...\" }";
}