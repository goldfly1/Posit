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
            // Strip markdown fences and reasoning tags before deserialization
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            cleaned = OllamaModelGateway.ExtractJson(cleaned);
            return JsonSerializer.Deserialize<ArchitectureContract>(cleaned, PositJson.Options);
        }
        catch { return null; }
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