namespace Posit.Phases;

/// <summary>
/// Phase 1: Architecture. The architect decomposes the spec, classifies
/// components, selects patterns from the registry, and fills the carapace.
/// The gateway injects CorrectionSignal into the prompt (handled by IModelGateway).
/// After model output, compose .dfy skeletons from PatternRegistry, then
/// run ContractScanner validation. If scan fails, return Failed with correction
/// listing so the FSM retries.
/// </summary>
public sealed class ArchitecturePhase : IPhase
{
    private readonly IModelGateway _model;
    private readonly PatternRegistry _registry;

    public ArchitecturePhase(IModelGateway model, PatternRegistry registry)
    {
        _model = model;
        _registry = registry;
    }

    public PhaseId Id { get; } = new("architecture");
    public string Name => "Architecture";
    public PhaseId[] Dependencies { get; } = [];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.ArchitectureContract,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(ArchitectureContract)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        var result = await _model.GenerateAsync(
            context.ModelRoute, context.Prompt, context, ct);

        if (string.IsNullOrWhiteSpace(result.Text))
            return Fail(context, "Model returned empty output", result);

        var contract = ParseContract(result.Text);
        if (contract == null)
            return Fail(context, "Failed to parse ArchitectureContract from model output", result);

        // Compose .dfy skeletons from PatternRegistry for dafny/mixed components
        var composeErrors = ComposeSkeletons(contract, context);
        if (composeErrors.Count > 0)
            return Fail(context, string.Join("\n", composeErrors), result);

        // Scan contract against registry — reject if any name doesn't match
        var scanErrors = ContractScanner.Scan(contract, _registry);
        if (scanErrors.Count > 0)
        {
            var listing = ContractScanner.FormatCorrectionListing(scanErrors);
            return Fail(context, listing, result);
        }

        // Pre-Dafny type chain check — the data flow spec validation.
        var chainErrors = TypeChainChecker.CheckPreDafny(contract);
        if (chainErrors.Count > 0)
        {
            var chainMsg = TypeChainChecker.FormatErrors(chainErrors);
            chainMsg += "\nFix the method signatures or connection order so types chain correctly.";
            chainMsg += "\nCommon fixes: use ReadLines (seq<string>) for CSV, ReadFile (string) for JSON/text.";
            return Fail(context, chainMsg, result);
        }

        // Cut-out type cross-check: compare the architect's DECLARED return types
        // against the cut-out's ACTUAL return types from the registry.
        // This catches the #1 T4 bug: architect declares CountFrequency returns 'string'
        // but the cut-out actually returns 'seq<seq<string>>'. The pre-Dafny checker
        // can't catch this because it trusts the declared types.
        var cutOutErrors = CheckCutOutTypes(contract, _registry);
        if (cutOutErrors.Count > 0)
        {
            var msg = $"Cut-out type mismatch — {cutOutErrors.Count} error(s):\n";
            foreach (var e in cutOutErrors)
                msg += $"  {e}\n";
            msg += "\nYour declared return types don't match the actual cut-out return types.";
            msg += "\nFIX: Either (a) match the declared type to the cut-out's actual type,";
            msg += "\n     (b) write custom Dafny instead of using this cut-out, or";
            msg += "\n     (c) add a serialization step that converts the cut-out output to string.";
            return Fail(context, msg, result);
        }

        return Success(context, contract, result);
    }

    public ValidationResult ValidateOutput(PhaseResult result)
    {
        if (result.Status != PhaseStatus.Success)
            return new ValidationResult { IsValid = false, Errors = result.Warnings };
        return new ValidationResult { IsValid = true };
    }

    private ArchitectureContract? ParseContract(string text)
    {
        try
        {
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            cleaned = OllamaModelGateway.ExtractJson(cleaned);
            return JsonSerializer.Deserialize<ArchitectureContract>(cleaned, PositJson.Options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[architecture PARSE] {ex.Message}");
            Console.Error.WriteLine($"[architecture PARSE] raw length={text.Length}, first 200={text[..Math.Min(200, text.Length)]}");
            return null;
        }
    }

    private List<string> ComposeSkeletons(ArchitectureContract contract, PhaseContext context)
    {
        var errors = new List<string>();
        var stagingDir = GetStagingDir(context);
        Directory.CreateDirectory(stagingDir);

        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell) continue;

            if (string.IsNullOrWhiteSpace(comp.PatternName))
            {
                errors.Add($"Component '{comp.Name}' is {comp.Classification} but has no patternName");
                continue;
            }

            if (!_registry.HasPattern(comp.PatternName!))
            {
                errors.Add($"Component '{comp.Name}' pattern '{comp.PatternName}' not in registry");
                continue;
            }

            var skeleton = _registry.ComposeSkeleton(
                comp.PatternName!, comp.StubNames, comp.Name);
            var path = Path.Combine(stagingDir, $"{comp.Name}.dfy");
            File.WriteAllText(path, skeleton);

            // Materialize pattern dependencies (includes like result.dfy)
            _registry.MaterializeDependencies(stagingDir, comp.PatternName!);
        }

        return errors;
    }

    private static string GetStagingDir(PhaseContext context) =>
        Path.Combine(Directory.GetCurrentDirectory(), ".posit", "staging",
            context.SessionId.Value, "dafny");

    /// <summary>
    /// Cross-check declared method return types against the cut-out's ACTUAL
    /// return types from the registry. The architect often declares a return
    /// type of 'string' when the cut-out actually returns 'seq<seq<string>>'.
    /// This catches that BEFORE Dafny runs, so the correction routes to the
    /// architect with the actual types.
    /// </summary>
    private static List<string> CheckCutOutTypes(ArchitectureContract contract, PatternRegistry registry)
    {
        var errors = new List<string>();
        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell) continue;
            if (string.IsNullOrWhiteSpace(comp.PatternName)) continue;
            if (!registry.HasPattern(comp.PatternName!)) continue;

            var realSigs = registry.GetMethodSignatures(comp.PatternName!);
            if (realSigs.Count == 0) continue;

            foreach (var declared in comp.MethodSignatures)
            {
                // Find the matching real method by name
                var realSig = realSigs.FirstOrDefault(s => s.Name == declared.Name);
                if (realSig == null) continue; // name mismatch caught by ContractScanner

                var declaredRet = !string.IsNullOrWhiteSpace(declared.ReturnDafnyType)
                    ? declared.ReturnDafnyType : declared.ReturnType;
                // Real return type may include "name: type" prefix from Dafny returns clause
                // e.g. "tokens: seq<string>" — strip the name prefix
                var rawRealRet = realSig.ReturnType ?? "void";
                var realRet = StripDafnyReturnName(rawRealRet);

                // Normalize for comparison
                var dNorm = declaredRet.Trim();
                var rNorm = realRet.Trim();

                if (dNorm == rNorm) continue;

                // Check if they're compatible (same depth seq, etc.)
                // string vs seq<...> is a mismatch unless the seq is 1D (string↔seq<string> is OK)
                bool dIsSeq = dNorm.Contains("seq<");
                bool rIsSeq = rNorm.Contains("seq<");
                if (dIsSeq || rIsSeq)
                {
                    // seq<string> ↔ string is OK (1D, join/split at boundary)
                    if ((dNorm == "string" && rNorm == "seq<string>") ||
                        (rNorm == "string" && dNorm == "seq<string>"))
                        continue;
                    // Different types — mismatch
                    errors.Add($"Component '{comp.Name}': method '{declared.Name}' declared as returning '{dNorm}' " +
                               $"but cut-out '{comp.PatternName}' actually returns '{rNorm}'. " +
                               $"Either declare the correct return type, or don't use this cut-out.");
                }
            }
        }
        return errors;
    }

    /// <summary>
    /// Strip the "name: " prefix from a Dafny return type.
    /// Dafny returns clause is "returns (name: type, name2: type2)" — the regex
    /// captures the full content. We only want the type, not the name.
    /// e.g. "tokens: seq<string>" → "seq<string>"
    ///      "result: seq<seq<string>>" → "seq<seq<string>>"
    /// For multi-return, take the first type (the data return).
    /// </summary>
    private static string StripDafnyReturnName(string raw)
    {
        var s = raw.Trim();
        // Handle comma-separated multi-return: take first
        if (s.Contains(','))
            s = s.Split(',')[0].Trim();
        // Strip "name: " prefix
        var colonIdx = s.IndexOf(':');
        if (colonIdx >= 0)
            s = s[(colonIdx + 1)..].Trim();
        return s;
    }

    private static PhaseResult Fail(PhaseContext ctx, string error, GenerationResult gen) => new()
    {
        PhaseId = ctx.PhaseId, Status = PhaseStatus.Failed,
        Artifacts = EmptyBundle(ctx),
        Costs = new CostSnapshot { InputTokens = gen.InputTokens, OutputTokens = gen.OutputTokens },
        Warnings = [error], RawOutput = gen.Text
    };

    private static PhaseResult Success(PhaseContext ctx, ArchitectureContract contract, GenerationResult gen)
    {
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(contract, PositJson.Options);
        return new PhaseResult
        {
            PhaseId = ctx.PhaseId, Status = PhaseStatus.Success,
            Artifacts = new ArtifactBundle
            {
                Id = ArtifactId.New(), SessionId = ctx.SessionId,
                SourcePhase = ctx.PhaseId, SchemaVersion = "1.0.0",
                Kind = ArtifactKind.ArchitectureContract,
                PayloadJson = payloadJson, ProducedAt = DateTimeOffset.UtcNow
            },
            Costs = new CostSnapshot { InputTokens = gen.InputTokens, OutputTokens = gen.OutputTokens },
            RawOutput = gen.Text
        };
    }

    private static ArtifactBundle EmptyBundle(PhaseContext ctx) => new()
    {
        Id = ArtifactId.New(), SessionId = ctx.SessionId,
        SourcePhase = ctx.PhaseId, SchemaVersion = "1.0.0",
        Kind = ArtifactKind.ArchitectureContract,
        PayloadJson = [], ProducedAt = DateTimeOffset.UtcNow
    };
}