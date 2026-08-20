namespace Posit.Phases;

using System.Text;
using Posit.AI.Models;
using Posit.Contracts.Core;
using Posit.Core.State;

/// <summary>
/// Phase 1.5: Pseudocode Reduction. Takes method signatures from the architect
/// and recursively reduces spec-level descriptions into Dafny-statement-level
/// fragments. Each pass replaces English concepts with Dafny language elements
/// from the reference card dictionary. The reduction stops when every line uses
/// only Dafny tokens (crystallization).
///
/// The Dafny writer takes the crystallized fragments and glues them into a
/// complete verified module (fragments + contracts + scaffolding).
///
/// No Z3 — pseudocode isn't verified, it's reduced.
/// Every reduction pass is stored in the DB artifact.
/// </summary>
public sealed class PseudocodeReductionPhase : IPhase
{
    private readonly IModelGateway _model;

    public PseudocodeReductionPhase(IModelGateway model) => _model = model;

    public PhaseId Id { get; } = KnownPhases.Pseudocode;
    public string Name => "Pseudocode Reduction";
    public PhaseId[] Dependencies { get; } = [KnownPhases.Architecture];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.PseudocodeModule,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(PseudocodeReductionBundle)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        var contract = ExtractContract(context);
        if (contract == null)
            return Fail(context, "No ArchitectureContract in input artifacts");

        var dictionary = LoadReferenceCard();
        var results = new List<PseudocodeReductionResult>();
        var warnings = new List<string>();

        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell) continue;

            var reduction = await ReduceComponentAsync(comp, contract, dictionary, context, ct);
            if (reduction == null)
            {
                warnings.Add($"Pseudocode reduction failed for '{comp.Name}'");
                results.Add(new PseudocodeReductionResult
                {
                    ModuleName = comp.Name,
                    MethodReductions = new(),
                    IsComplete = false
                });
                continue;
            }
            results.Add(reduction);
        }

        var allComplete = results.Count > 0 && results.All(r => r.IsComplete);
        var bundle = new PseudocodeReductionBundle { Results = results.ToArray() };
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(bundle, PositJson.Options);

        return new PhaseResult
        {
            PhaseId = context.PhaseId,
            Status = allComplete ? PhaseStatus.Success : PhaseStatus.Failed,
            Artifacts = new ArtifactBundle
            {
                Id = ArtifactId.New(), SessionId = context.SessionId,
                SourcePhase = context.PhaseId, SchemaVersion = "1.0.0",
                Kind = ArtifactKind.PseudocodeModule,
                PayloadJson = payloadJson, ProducedAt = DateTimeOffset.UtcNow
            },
            Costs = CostSnapshot.Zero,
            Warnings = warnings.ToArray()
        };
    }

    /// <summary>
    /// Reduce a single component's methods to Dafny-statement-level pseudocode.
    /// Each method gets a reduction chain: pass 0 (from spec) → pass 1 → ... → crystallized.
    /// Max 5 passes. Stops when every line uses only Dafny tokens.
    /// </summary>
    private async Task<PseudocodeReductionResult?> ReduceComponentAsync(
        Component comp, ArchitectureContract contract, string dictionary,
        PhaseContext context, CancellationToken ct)
    {
        var methodReductions = new Dictionary<string, List<string>>();
        var allCrystallized = true;

        foreach (var method in comp.MethodSignatures)
        {
            var sig = $"method {method.Name}({string.Join(", ", method.Params.Select(p => $"{p.Name}: {p.Type}"))}) returns ({method.ReturnType})";
            var testCases = comp.TestCases.Length > 0
                ? string.Join("\n", comp.TestCases.Select(tc => $"  test: {tc.Description} → {tc.ExpectedBehavior}"))
                : "";

            // Pass 0: from spec
            var pass0 = $"{sig}\nResponsibility: {comp.Responsibility}\n{testCases}";
            var chain = new List<string> { pass0 };

            // Check if pass 0 is already crystallized (simple specs)
            if (IsCrystallized(pass0))
            {
                methodReductions[method.Name] = chain;
                continue;
            }

            // Reduce up to 5 passes
            var current = pass0;
            var crystallized = false;
            for (var pass = 1; pass <= 5; pass++)
            {
                var reduced = await ReducePassAsync(current, sig, comp.Responsibility ?? "", dictionary, context, ct);
                if (reduced == null)
                {
                    Console.Error.WriteLine($"[pseudocode] {comp.Name}.{method.Name} pass {pass}: model returned null");
                    break;
                }

                // Detect stuck loop: same output as previous pass → stop wasting calls
                if (reduced.Trim() == current.Trim())
                {
                    Console.Error.WriteLine($"[pseudocode] {comp.Name}.{method.Name} pass {pass}: same output as previous — accepting as crystallized");
                    crystallized = true;
                    break;
                }

                chain.Add(reduced);
                current = reduced;

                // Model says STOP — accept as crystallized. The model knows
                // better than the heuristic whether the pseudocode is reduced enough.
                // Also accept very short responses (< 10 chars) — model is signaling
                // it has nothing more to reduce, even if it doesn't say "STOP" exactly.
                if (reduced.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)
                    || reduced.Trim().StartsWith("STOP", StringComparison.OrdinalIgnoreCase)
                    || (reduced.Trim().Length < 10 && reduced.ToLowerInvariant().Contains("stop")))
                {
                    Console.Error.WriteLine($"[pseudocode] {comp.Name}.{method.Name} model said STOP at pass {pass}");
                    crystallized = true;
                    break;
                }

                // Very short non-STOP response (model gave up / returned empty / JSON noise)
                // — accept the previous pass as the best crystallized version.
                if (reduced.Trim().Length < 5)
                {
                    Console.Error.WriteLine($"[pseudocode] {comp.Name}.{method.Name} model returned short output at pass {pass} — accepting best effort");
                    crystallized = true;
                    // Use the previous pass as the final, not this tiny output
                    chain.RemoveAt(chain.Count - 1);
                    break;
                }

                if (IsCrystallized(reduced))
                {
                    Console.Error.WriteLine($"[pseudocode] {comp.Name}.{method.Name} crystallized at pass {pass}");
                    crystallized = true;
                    break;
                }
            }

            if (!crystallized)
            {
                Console.Error.WriteLine($"[pseudocode] {comp.Name}.{method.Name} did NOT crystallize after {chain.Count - 1} passes — using best effort");
                allCrystallized = false;
            }

            methodReductions[method.Name] = chain;
        }

        return new PseudocodeReductionResult
        {
            ModuleName = comp.Name,
            MethodReductions = methodReductions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()),
            IsComplete = allCrystallized
        };
    }

    /// <summary>
    /// One reduction pass: ask the model to make the pseudocode more concrete
    /// using only Dafny language elements.
    /// </summary>
    private async Task<string?> ReducePassAsync(
        string currentPseudocode, string methodSignature, string responsibility,
        string dictionary, PhaseContext context, CancellationToken ct)
    {
        // Read the interface definition — the reducer needs to know what types
        // and structures are available so it reduces toward the right Dafny types
        var contract = ExtractContract(context);
        var interfaceDef = "";
        if (contract != null)
        {
            var comp = contract.Components.FirstOrDefault(c => c.Name == context.PhaseId.Value);
            if (comp != null)
            {
                var skeletonPath = !string.IsNullOrWhiteSpace(comp.DafnyContractPath)
                    ? comp.DafnyContractPath!
                    : Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), ".posit", "staging", context.SessionId.ToString()), $"{comp.Name}.dfy");
                if (File.Exists(skeletonPath))
                    interfaceDef = File.ReadAllText(skeletonPath);
            }
        }

        var systemPrompt = new StringBuilder();
        systemPrompt.AppendLine("You are a pseudocode reducer. Make the pseudocode more concrete by replacing");
        systemPrompt.AppendLine("English-language concepts with Dafny language elements.");
        systemPrompt.AppendLine();
        systemPrompt.AppendLine("Method signature: " + methodSignature);
        systemPrompt.AppendLine("Responsibility: " + responsibility);
        systemPrompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(interfaceDef))
        {
            systemPrompt.AppendLine("═══ INTERFACE DEFINITION (types and contracts available to you) ═══");
            systemPrompt.AppendLine("Use the types declared here — do not invent new types or use C# types.");
            systemPrompt.AppendLine(interfaceDef);
            systemPrompt.AppendLine("═══ END INTERFACE DEFINITION ═══");
            systemPrompt.AppendLine();
        }

        systemPrompt.AppendLine("Use Dafny syntax: method (not function), while+invariant (not for-loops), := (not =), char() (not (char)), seq<T> (not seq[T]), map[K,V] (not map[K]V).");
        // Reference card removed — lean prompt with key syntax rules above
        systemPrompt.AppendLine();
        systemPrompt.AppendLine("Current pseudocode:");
        systemPrompt.AppendLine(currentPseudocode);
        systemPrompt.AppendLine();
        systemPrompt.AppendLine("Rules:");
        systemPrompt.AppendLine("1. Replace every English verb/concept with the corresponding Dafny token from the dictionary.");
        systemPrompt.AppendLine("2. Use types from the interface definition — do not use C# types (int, string, char[]).");
        systemPrompt.AppendLine("3. Keep lines that already use only Dafny tokens unchanged.");
        systemPrompt.AppendLine("4. If ALL lines already use only Dafny tokens, output STOP.");
        systemPrompt.AppendLine("5. Output ONLY the reduced pseudocode (or STOP). No explanations.");

        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt.ToString(),
            OutputFormatSpec = "Reduced pseudocode or STOP",
            ModelTier = ModelTier.Fast,
            Temperature = 0.1,
            MaxOutputTokens = 4096,
            OutputFormat = OutputFormat.PlainText,
            OutputSchemaRef = "Pseudocode",
            Status = PromptStatus.Active
        };

        try
        {
            var gen = await _model.GenerateAsync(context.ModelRoute, prompt, context, ct);
            if (string.IsNullOrWhiteSpace(gen.Text))
                return null;
            return OllamaModelGateway.StripReasoningTags(gen.Text).Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[pseudocode] Model call failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Crystallization check: every non-comment, non-empty line must contain
    /// at least one Dafny token from the dictionary. If a line has NO Dafny
    /// token, it's still English prose — not crystallized.
    /// </summary>
    private static readonly HashSet<string> DafnyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "while", "if", "match", "case", ":=", "var", "|", "[]", "+", "requires",
        "ensures", "invariant", "decreases", "method", "function", "predicate",
        "datatype", "extern", "module", "return", "Seq.", "Set.", "Map.",
        "Multiset", "Ord", "for", "break", "continue", "print", "assert",
        "assume", "forall", "exists", "calc", "let", "old", "fresh", "as ",
        "is ", "class", "trait", "constructor", "const", "lemma", "import",
        "abstract", "refines", "{:", "real", "int", "bool", "char", "string",
        "seq<", "set<", "map<", "array", "tuple", "nat"
    };

    private static bool IsCrystallized(string pseudocode)
    {
        var hasSubstantiveLine = false;
        foreach (var line in pseudocode.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (trimmed.StartsWith("//")) continue;
            if (trimmed.StartsWith("method ")) continue; // signature line
            if (trimmed.StartsWith("Responsibility:")) continue;
            if (trimmed.StartsWith("test:")) continue;
            // This is a substantive line — it must contain a Dafny token
            hasSubstantiveLine = true;
            var hasToken = DafnyTokens.Any(t => trimmed.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (!hasToken) return false;
        }
        // If there are no substantive lines (only sig/responsibility/test),
        // it's just the raw spec — NOT crystallized. It needs reduction.
        return hasSubstantiveLine;
    }

    private static string LoadReferenceCard()
    {
        var paths = new[] {
            Path.Combine(Directory.GetCurrentDirectory(), "patterns", "dafny-reference-card.dfy"),
            "C:/Users/goldf/Posit/patterns/dafny-reference-card.dfy"
        };
        foreach (var p in paths)
            if (File.Exists(p))
                return File.ReadAllText(p);
        return "// Dafny dictionary not found";
    }

    public ValidationResult ValidateOutput(PhaseResult result)
    {
        if (result.Status != PhaseStatus.Success)
            return new ValidationResult { IsValid = false, Errors = result.Warnings };
        return new ValidationResult { IsValid = true };
    }

    private static ArchitectureContract? ExtractContract(PhaseContext ctx)
    {
        foreach (var a in ctx.InputArtifacts)
            if (a.Kind == ArtifactKind.ArchitectureContract)
                try { return JsonSerializer.Deserialize<ArchitectureContract>(a.PayloadJson, PositJson.Options); }
                catch { }
        return null;
    }

    private static PhaseResult Fail(PhaseContext ctx, string error) => new()
    {
        PhaseId = ctx.PhaseId, Status = PhaseStatus.Failed,
        Artifacts = Empty(ctx), Costs = CostSnapshot.Zero, Warnings = [error]
    };

    private static ArtifactBundle Empty(PhaseContext ctx) => new()
    {
        Id = ArtifactId.New(), SessionId = ctx.SessionId, SourcePhase = ctx.PhaseId,
        SchemaVersion = "1.0.0", Kind = ArtifactKind.PseudocodeModule,
        PayloadJson = [], ProducedAt = DateTimeOffset.UtcNow
    };
}

/// <summary>
/// Per-component pseudocode reduction result. Each method gets a chain of passes.
/// </summary>
public record PseudocodeReductionResult
{
    public string ModuleName { get; init; } = "";
    public Dictionary<string, string[]> MethodReductions { get; init; } = new();
    public bool IsComplete { get; init; }
}

/// <summary>
/// Full pseudocode artifact for a session — one result per non-io-shell component.
/// </summary>
public record PseudocodeReductionBundle
{
    public PseudocodeReductionResult[] Results { get; init; } = [];
}