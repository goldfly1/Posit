using Posit.Tools;
using System.Text;

namespace Posit.Cli.Orchestration;

using Posit.Contracts.Core;

/// <summary>
/// Builds phase-aware prompt templates. The architecture prompt injects the
/// full pattern/stub catalog so the model knows exactly what it can select.
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
        sb.AppendLine("Decompose the user's spec into components. For each component, classify it as:");
        sb.AppendLine("  - \"dafny\": Pure logic that can be proven in Dafny (parsing, validation, transformation, computation).");
        sb.AppendLine("  - \"io-shell\": I/O-bound code that connects to the outside world (file, console, network, database).");
        sb.AppendLine();
        sb.AppendLine("UNIVERSAL STARTER: Every build begins with the \"pipeline\" pattern as its foundation.");
        sb.AppendLine("The pipeline pattern handles parse → validate → transform → store → respond.");
        sb.AppendLine("Use it for the main orchestrator/entry component, then add specialist patterns for specific modules.");
        sb.AppendLine("Do NOT invent pattern names — use EXACTLY one of the AVAILABLE PATTERNS listed below.");
        sb.AppendLine("If no pattern perfectly matches a component's responsibility, pick the CLOSEST one.");
        sb.AppendLine();
        sb.AppendLine("For EACH component you MUST set:");
        sb.AppendLine("  - classification: \"dafny\" or \"io-shell\" (never null)");
        sb.AppendLine("  - For dafny: patternName must be one of the AVAILABLE PATTERNS below (NOT empty)");
        sb.AppendLine("  - For io-shell: stubNames must list at least one AVAILABLE STUB below (NEVER empty)");
        sb.AppendLine("  - methodSignatures: array of {name, params:[{name,type}], returnType, returnDafnyType}");
        sb.AppendLine("  - connections: array of {fromMethod, toComponent, toMethod, argMappings:[\"sourceField->paramName\", ...]}");
        sb.AppendLine("    argMappings MUST be an array of STRINGS in \"source->target\" format, e.g. [\"parsedData->input\", \"config->settings\"].");
        sb.AppendLine("    Do NOT put objects in argMappings. Each string maps a source field to a target parameter.");
        sb.AppendLine("  - testCases: array of {id, name, targetType, description, expectedBehavior}");
        sb.AppendLine();
        sb.AppendLine("═══ STUB USAGE RULES ═══");
        sb.AppendLine("For CSV or line-by-line processing, use ReadLines (returns seq<string> = lines) NOT ReadFile.");
        sb.AppendLine("For JSON, text, or whole-file parsing (where the file is one unit), use ReadFile (returns string) NOT ReadLines.");
        sb.AppendLine("ReadLines returns the right shape for line-by-line: lines → ParseLines → ValidateRows → SerializeToJson.");
        sb.AppendLine("ReadFile returns the right shape for whole-file: content → ParseJson/Tokenize/ParseIni → SerializeToCsv.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL: Every component must have EITHER a patternName (if dafny) OR at least one stubName (if io-shell).");
        sb.AppendLine("A component with empty patternName AND empty stubNames is INVALID and will be rejected.");
        sb.AppendLine("Use PascalCase for ALL names (component names, dependency names, toComponent in connections).");
        sb.AppendLine("NEVER use kebab-case or lowercase in any name field.");
        sb.AppendLine();
        if (registry != null)
        {
            // List cut-outs first — PREFER these over generic patterns
            var cutOuts = registry.GetAllPatterns().Where(p => p.IsCutOut).OrderBy(p => p.Name).ToList();
            if (cutOuts.Count > 0)
            {
                sb.AppendLine("═══ AVAILABLE CUT-OUTS (pre-cut domain modules — PREFER THESE over generic patterns) ═══");
                sb.AppendLine("Cut-outs are pre-written, Z3-verified Dafny modules that do REAL work.");
                sb.AppendLine("If a cut-out matches the component's responsibility, USE IT (set patternName to the cut-out name).");
                sb.AppendLine("Each cut-out has REAL method names — use those EXACT names in methodSignatures and connections.");
                sb.AppendLine("The method signatures are listed below — match them EXACTLY (name, params, return type).");
                foreach (var p in cutOuts)
                {
                    sb.AppendLine($"  - {p.Name}: {p.Responsibility}");
                    // List actual method signatures from the Dafny source
                    var sigs = registry.GetPatternSignatures(p.Name);
                    foreach (var sig in sigs)
                        sb.AppendLine($"      {sig}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("═══ UNIVERSAL STARTER PATTERN ═══");
            var pipe = registry.GetPattern("pipeline");
            if (pipe != null)
                sb.AppendLine($"  - pipeline: {pipe.Responsibility}  ← USE THIS for the main orchestrator component");
            sb.AppendLine();
            sb.AppendLine("═══ SPECIALIST PATTERNS (add alongside pipeline for specific modules) ═══");
            foreach (var p in registry.GetAllPatterns().OrderBy(p => p.Name))
                if (p.Name != "pipeline" && !p.IsCutOut)
                    sb.AppendLine($"  - {p.Name}: {p.Responsibility}");
            sb.AppendLine();
            sb.AppendLine("═══ AVAILABLE STUBS (set stubNames to one or more of these for io-shell components) ═══");
            foreach (var s in registry.GetAllCSharpStubs().OrderBy(s => s.Name))
            {
                var kws = s.AutoBindKeywords.Length > 0 ? $" (keywords: {string.Join(", ", s.AutoBindKeywords)})" : "";
                sb.AppendLine($"  - {s.Name}{kws}");
            }
            sb.AppendLine();
        }
        sb.AppendLine("Output a single JSON object with this shape:");
        sb.AppendLine("{ \"systemContext\": \"...\", \"components\": [{ \"id\": \"...\", \"name\": \"...\", \"responsibility\": \"...\", ");
        sb.AppendLine("  \"publicSurface\": [\"...\"], \"internals\": \"...\", \"dependencies\": [\"...\"], \"layer\": 0, \"tech\": \"C#\",");
        sb.AppendLine("  \"classification\": \"dafny\"|\"io-shell\", \"patternName\": \"...\"|null, \"stubNames\": [\"...\"]|[],");
        sb.AppendLine("  \"methodSignatures\": [{\"name\":\"...\",\"params\":[{\"name\":\"...\",\"type\":\"...\"}],\"returnType\":\"...\",\"returnDafnyType\":\"...\"}],");
        sb.AppendLine("  \"connections\": [{\"fromMethod\":\"...\",\"toComponent\":\"...\",\"toMethod\":\"...\",\"argMappings\":[]}],");
        sb.AppendLine("  \"testCases\": [{\"id\":\"...\",\"name\":\"...\",\"targetType\":\"...\",\"description\":\"...\",\"expectedBehavior\":\"...\"}] }],");
        sb.AppendLine("  \"deploymentTopology\": \"...\" }");
        return sb.ToString();
    }

    private static string BuildArchitectureFormat() =>
        "{ \"systemContext\": \"...\", \"components\": [{ \"id\": \"...\", \"name\": \"...\", ... }], \"deploymentTopology\": \"...\" }";
}