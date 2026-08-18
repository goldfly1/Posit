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
        sb.AppendLine("═══ KEEP IT SIMPLE ═══");
        sb.AppendLine("Use 2-3 components: one io-shell for I/O, one or two dafny for logic. NEVER more than 4.");
        sb.AppendLine("A temperature converter is ONE dafny component + ONE io-shell. Not six components.");
        sb.AppendLine("A CSV validator is ONE dafny component (parse+validate) + ONE io-shell (read+print).");
        sb.AppendLine("Over-decomposition is the #1 cause of failure. Fewer components = fewer wiring bugs = higher pass rate.");
        sb.AppendLine("Combine related logic into a single dafny component. Don't split parsing from validation from transformation.");
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
        sb.AppendLine("  - dependencies: array of OTHER COMPONENT NAMES (e.g. [\"CsvParser\", \"Printer\"]).");
        sb.AppendLine("    NOT pattern names, NOT stub names. Use the Name field of components this one depends on.");
        sb.AppendLine();
        sb.AppendLine("  - connections: array of {fromMethod, toComponent, toMethod, argMappings:[]}");
        sb.AppendLine("    CONNECTIONS describe how the ORCHESTRATOR calls other components.");
        sb.AppendLine("    ONLY the orchestrator component (io-shell with entryType) has connections. Dafny components have NONE.");
        sb.AppendLine();
        sb.AppendLine("    CONNECTION FIELD RULES (READ CAREFULLY — most errors come from misunderstanding these):");
        sb.AppendLine("    - fromMethod: a method name on THIS orchestrator component (from its methodSignatures).");
        sb.AppendLine("      Usually just \"Run\". NOT the target's method name. NOT \"ReadLines\" or \"ParseLines\".");
        sb.AppendLine("    - toComponent: the Name of the target component (e.g. \"CsvParser\", \"FileReader\").");
        sb.AppendLine("      NOT a pattern name (\"csv-parser\"). NOT a stub name (\"file-io\"). NOT a type name.");
        sb.AppendLine("    - toMethod: the method to call on the target component (from its methodSignatures).");
        sb.AppendLine("      e.g. \"ParseLines\", \"ValidateRows\", \"PrintLine\".");
        sb.AppendLine("    - argMappings: array of STRINGS in \"source->target\" format. Usually empty [] for linear chains.");
        sb.AppendLine();
        sb.AppendLine("    EXAMPLE — CSV to JSON with 4 components (Orchestrator, CsvParser, RowValidator, JsonPrinter):");
        sb.AppendLine("      Orchestrator connections:");
        sb.AppendLine("        {\"fromMethod\":\"Run\", \"toComponent\":\"FileReader\", \"toMethod\":\"ReadLines\", \"argMappings\":[]},");
        sb.AppendLine("        {\"fromMethod\":\"Run\", \"toComponent\":\"CsvParser\", \"toMethod\":\"ParseLines\", \"argMappings\":[]},");
        sb.AppendLine("        {\"fromMethod\":\"Run\", \"toComponent\":\"RowValidator\", \"toMethod\":\"ValidateRows\", \"argMappings\":[]},");
        sb.AppendLine("        {\"fromMethod\":\"Run\", \"toComponent\":\"JsonPrinter\", \"toMethod\":\"PrintLine\", \"argMappings\":[]}");
        sb.AppendLine("      Orchestrator methodSignatures: [{\"name\":\"Run\",\"params\":[],\"returnType\":\"void\",\"returnDafnyType\":\"void\"}]");
        sb.AppendLine("      Orchestrator dependencies: [\"FileReader\",\"CsvParser\",\"RowValidator\",\"JsonPrinter\"]");
        sb.AppendLine("      FileReader: io-shell, stubNames=[\"file-io\"], methodSignatures=[{\"name\":\"ReadLines\",...}]");
        sb.AppendLine("      CsvParser: dafny, patternName=\"csv-parser\", connections=[] (NONE — dafny components don't have connections)");
        sb.AppendLine();
        sb.AppendLine("    CONNECTIONS FORM A CHAIN. Each step's output feeds the next step's input.");
        sb.AppendLine("    The last connection should be to a Print/PrintLine stub that outputs the result.");
        sb.AppendLine("    FOR MULTI-INPUT SPECS (e.g. merge two files): the orchestrator can read multiple inputs");
        sb.AppendLine("    by having multiple connections to different readers. The wiring model handles the merge logic.");
        sb.AppendLine("    Example: ReadLines(file1) → ReadLines(file2) → Merge → Validate → Serialize → Print");
        sb.AppendLine("    The orchestrator's Run method calls each reader in sequence, merges the results, then chains forward.");
        sb.AppendLine("  - entryType: \"file\" (reads args[0] as file path) or \"stdin\" (reads Console.ReadLine). REQUIRED on the orchestrator component.");
        sb.AppendLine("  - branchCondition: if a step returns isValid (bool), set this to describe the error branch, e.g. \"if !isValid: print error, exit 1\".");
        sb.AppendLine("    This tells the wiring generator to emit an if-branch. Without this, the error path is invisible to the wiring.");
        sb.AppendLine("  - testCases: array of {id, name, targetType, description, expectedBehavior}");
        sb.AppendLine("    EXPECTED BEHAVIOR RULES (CRITICAL — wrong test expectations cause false failures):");
        sb.AppendLine("    - Describe the SHAPE of the output, NOT specific values. The architect doesn't know");
        sb.AppendLine("      what the program will compute — that's the program's job.");
        sb.AppendLine("    - GOOD: \"prints converted temperature with unit\", \"prints merged CSV rows\", \"prints error and exits 1\"");
        sb.AppendLine("    - BAD:  \"prints '0 C'\", \"prints '32 F'\" — you don't know what the conversion yields.");
        sb.AppendLine("    - BAD:  \"prints 'name,age\\\\nAlice,30\\\\nCarol,35'\" — you don't know the input data ahead of time.");
        sb.AppendLine("    - For error tests: \"prints error message and exits 1\" is sufficient.");
        sb.AppendLine("    - For valid tests: \"prints output in correct format\" or \"prints result\" is sufficient.");
        sb.AppendLine("    - The harness compares actual output against expectedBehavior using fuzzy matching");
        sb.AppendLine("      (substring, shape checks for JSON/CSV/error/empty). Specific values will FAIL.");
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
            // List cut-outs as optional shortcuts (not required)
            var cutOuts = registry.GetAllPatterns().Where(p => p.IsCutOut).OrderBy(p => p.Name).ToList();
            if (cutOuts.Count > 0)
            {
                sb.AppendLine("═══ OPTIONAL CUT-OUTS (pre-written Dafny modules — use ONLY if one matches exactly) ═══");
                sb.AppendLine("Cut-outs are shortcuts. If one matches your component's responsibility exactly, use it.");
                sb.AppendLine("If none matches, WRITE YOUR OWN Dafny method using the reference card below — Z3 will verify it.");
                sb.AppendLine("Do NOT force a cut-out to fit — write custom Dafny instead.");
                sb.AppendLine();
                foreach (var p in cutOuts)
                {
                    sb.AppendLine($"  {p.Name}: {p.Responsibility}");
                    var sigs = registry.GetMethodSignatures(p.Name);
                    foreach (var sig in sigs)
                    {
                        var inputType = ExtractInputType(sig.Params);
                        var outputType = sig.ReturnType ?? "void";
                        sb.AppendLine($"    {sig.Name}: {inputType} → {outputType}");
                    }
                }
                sb.AppendLine();
            }
            sb.AppendLine("═══ UNIVERSAL STARTER PATTERN ═══");
            var pipe = registry.GetPattern("pipeline");
            if (pipe != null)
                sb.AppendLine($"  - pipeline: {pipe.Responsibility}  ← USE THIS for the main orchestrator component");
            sb.AppendLine();
            sb.AppendLine("═══ DAFNY REFERENCE (for writing your own methods) ═══");
            sb.AppendLine("Write Dafny methods directly — Z3 will verify them. Key syntax:");
            sb.AppendLine("  method Name(params) returns (result: type) { body }");
            sb.AppendLine("  Types: string, int, bool, seq<T>, seq<seq<T>>");
            sb.AppendLine("  Seq concat: rows1 + rows2    String concat: s1 + s2");
            sb.AppendLine("  Length: |s|    Element: s[i] (needs 0 <= i < |s|)    Slice: s[a..b]");
            sb.AppendLine("  Empty seq: var x: seq<string> := []");
            sb.AppendLine("  Loops: while i < |s| invariant 0 <= i <= |s| decreases |s| - i { ... }");
            sb.AppendLine("  Assignment: := (not =)    If/else: if cond { } else { }");
            sb.AppendLine();
            sb.AppendLine("═══ SPECIALIST PATTERNS (ONLY if no cut-out matches — cut-outs are PREFERRED) ═══");
            sb.AppendLine("WARNING: These are GENERIC patterns with GENERIC methods. They rarely match the spec exactly.");
            sb.AppendLine("Only use these if NO cut-out covers the component's responsibility.");
            sb.AppendLine("If a cut-out matches even partially, USE THE CUT-OUT — not a generic pattern.");
            sb.AppendLine();
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
        sb.AppendLine("  \"entryType\": \"file\"|\"stdin\",");
        sb.AppendLine("  \"branchCondition\": \"...\"|null,");
        sb.AppendLine("  \"testCases\": [{\"id\":\"...\",\"name\":\"...\",\"targetType\":\"...\",\"description\":\"...\",\"expectedBehavior\":\"...\"}] }],");
        sb.AppendLine("  \"deploymentTopology\": \"...\" }");
        return sb.ToString();
    }

    private static string BuildArchitectureFormat() =>
        "{ \"systemContext\": \"...\", \"components\": [{ \"id\": \"...\", \"name\": \"...\", ... }], \"deploymentTopology\": \"...\" }";

    /// <summary>
    /// Extract the primary input type from a method's param string.
    /// E.g., "lines: seq<string>, delimiter: string" → "seq<string>"
    /// </summary>
    private static string ExtractInputType(string paramsStr)
    {
        if (string.IsNullOrWhiteSpace(paramsStr)) return "void";
        // First param before comma
        var firstParam = paramsStr.Contains(',') ? paramsStr[..paramsStr.IndexOf(',')] : paramsStr;
        var colonIdx = firstParam.IndexOf(':');
        return colonIdx >= 0 ? firstParam[(colonIdx + 1)..].Trim() : firstParam.Trim();
    }
}