namespace Posit.Phases;

/// <summary>
/// Phase 2: Dafny Contracts. Z3 verifies each .dfy skeleton composed by
/// ArchitecturePhase. No model call — purely deterministic. The skeleton
/// files are on disk (the carapace). Z3 confirms the contracts are sound.
/// </summary>
public sealed class DafnyContractsPhase : IPhase
{
    private readonly Z3Runner _z3;

    public DafnyContractsPhase(Z3Runner z3) => _z3 = z3;

    public PhaseId Id { get; } = new("dafny-contracts");
    public string Name => "Dafny Contracts";
    public PhaseId[] Dependencies { get; } = [new("architecture")];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.DafnyContract,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(DafnyContractResult)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        var contract = ExtractContract(context);
        if (contract == null)
            return Fail(context, "No ArchitectureContract in input artifacts");

        var results = new List<DafnyContractResult>();
        var warnings = new List<string>();

        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell) continue;

            var dafnyPath = ResolveDafnyPath(context, comp);
            if (!File.Exists(dafnyPath))
            {
                warnings.Add($"Skeleton file missing: {dafnyPath}");
                continue;
            }

            var verifyResult = await _z3.VerifyAsync(dafnyPath, ct);

            results.Add(new DafnyContractResult
            {
                ModuleName = comp.Name,
                DafnySource = await File.ReadAllTextAsync(dafnyPath, ct),
                DafnyPath = dafnyPath,
                IsVerified = verifyResult.Success,
                VerificationOutput = verifyResult.Stdout,
                ContractSummary = verifyResult.Success ? "Verified" : "Failed"
            });

            if (!verifyResult.Success)
                warnings.Add($"Z3 verification failed for '{comp.Name}': {verifyResult.Stdout}");
        }

        var allVerified = results.All(r => r.IsVerified);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(results.ToArray(), PositJson.Options);

        return new PhaseResult
        {
            PhaseId = context.PhaseId,
            Status = allVerified ? PhaseStatus.Success : PhaseStatus.Failed,
            Artifacts = new ArtifactBundle
            {
                Id = ArtifactId.New(), SessionId = context.SessionId,
                SourcePhase = context.PhaseId, SchemaVersion = "1.0.0",
                Kind = ArtifactKind.DafnyContract,
                PayloadJson = payloadJson, ProducedAt = DateTimeOffset.UtcNow
            },
            Costs = CostSnapshot.Zero,
            Warnings = warnings.ToArray()
        };
    }

    public ValidationResult ValidateOutput(PhaseResult result)
    {
        if (result.Status != PhaseStatus.Success)
            return new ValidationResult { IsValid = false, Errors = result.Warnings };
        return new ValidationResult { IsValid = true };
    }

    private static string ResolveDafnyPath(PhaseContext ctx, Component comp) =>
        !string.IsNullOrWhiteSpace(comp.DafnyContractPath)
            ? comp.DafnyContractPath!
            : Path.Combine(GetStagingDir(ctx), $"{comp.Name}.dfy");

    private static ArchitectureContract? ExtractContract(PhaseContext ctx)
    {
        foreach (var a in ctx.InputArtifacts)
            if (a.Kind == ArtifactKind.ArchitectureContract)
                try { return JsonSerializer.Deserialize<ArchitectureContract>(a.PayloadJson, PositJson.Options); }
                catch { }
        return null;
    }

    private static string GetStagingDir(PhaseContext ctx) =>
        Path.Combine(Directory.GetCurrentDirectory(), ".posit", "staging", ctx.SessionId.Value, "dafny");

    private static PhaseResult Fail(PhaseContext ctx, string error) => new()
    {
        PhaseId = ctx.PhaseId, Status = PhaseStatus.Failed,
        Artifacts = EmptyBundle(ctx), Costs = CostSnapshot.Zero, Warnings = [error]
    };

    private static ArtifactBundle EmptyBundle(PhaseContext ctx) => new()
    {
        Id = ArtifactId.New(), SessionId = ctx.SessionId,
        SourcePhase = ctx.PhaseId, SchemaVersion = "1.0.0",
        Kind = ArtifactKind.DafnyContract,
        PayloadJson = [], ProducedAt = DateTimeOffset.UtcNow
    };
}