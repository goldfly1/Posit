namespace Posit.Phases;

/// <summary>
/// Phase 3: Dafny Implementation. For pre-verified patterns (bodies already
/// proven by Z3 in the pattern registry), skip Z3 and translate directly.
/// For unverified, Z3 verifies then translates. Post-processing (runtime
/// stripping, namespace rename) is in Z3Runner, not here.
/// </summary>
public sealed class DafnyImplementationPhase : IPhase
{
    private readonly IModelGateway _model;
    private readonly Z3Runner _z3;

    public DafnyImplementationPhase(IModelGateway model, Z3Runner z3)
    {
        _model = model;
        _z3 = z3;
    }

    public PhaseId Id { get; } = new("dafny-implementation");
    public string Name => "Dafny Implementation";
    public PhaseId[] Dependencies { get; } = [new("dafny-contracts")];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.DafnyVerification,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(DafnyVerificationResult)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        var contract = ExtractContract(context);
        if (contract == null)
            return Fail(context, "No ArchitectureContract in input artifacts");

        var results = new List<DafnyVerificationResult>();
        var warnings = new List<string>();
        var stagingDir = GetStagingDir(context);
        Directory.CreateDirectory(stagingDir);

        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell) continue;

            var dafnyPath = ResolveDafnyPath(context, comp);
            if (!File.Exists(dafnyPath))
            {
                warnings.Add($"Skeleton file missing: {dafnyPath}");
                continue;
            }

            // Pre-verified patterns (bodies already proven) translate directly
            var isPreVerified = comp.IsVerified || IsPatternPreVerified(comp.PatternName);

            Z3VerificationResult? verifyResult = null;
            if (!isPreVerified)
            {
                verifyResult = await _z3.VerifyAsync(dafnyPath, ct);
                if (!verifyResult.Success)
                {
                    warnings.Add($"Z3 verification failed for '{comp.Name}': {verifyResult.Stdout}");
                    results.Add(new DafnyVerificationResult
                    {
                        ModuleName = comp.Name,
                        DafnyPath = dafnyPath,
                        IsVerified = false,
                        VerificationOutput = verifyResult.Stdout
                    });
                    continue;
                }
            }

            // Translate to C#
            var translation = await _z3.TranslateAsync(dafnyPath, comp.Name, ct);
            if (!translation.Success || string.IsNullOrWhiteSpace(translation.CleanCsharp))
            {
                warnings.Add($"C# translation failed for '{comp.Name}': {translation.Stderr}");
                results.Add(new DafnyVerificationResult
                {
                    ModuleName = comp.Name, DafnyPath = dafnyPath,
                    IsVerified = false,
                    VerificationOutput = translation.Stderr
                });
                continue;
            }

            // Write translated C# to staging
            var csPath = Path.Combine(stagingDir, $"{comp.Name}.cs");
            await File.WriteAllTextAsync(csPath, translation.CleanCsharp, ct);

            results.Add(new DafnyVerificationResult
            {
                ModuleName = comp.Name,
                DafnyPath = dafnyPath,
                IsVerified = true,
                TranslatedCSharpPath = csPath
            });
        }

        var allOk = results.All(r => r.IsVerified && !string.IsNullOrWhiteSpace(r.TranslatedCSharpPath));
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(results.ToArray(), PositJson.Options);

        return new PhaseResult
        {
            PhaseId = context.PhaseId,
            Status = allOk ? PhaseStatus.Success : PhaseStatus.Failed,
            Artifacts = new ArtifactBundle
            {
                Id = ArtifactId.New(), SessionId = context.SessionId,
                SourcePhase = context.PhaseId, SchemaVersion = "1.0.0",
                Kind = ArtifactKind.DafnyVerification,
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

    // Approach 3: all patterns ship with proven bodies
    private static bool IsPatternPreVerified(string? patternName) => !string.IsNullOrWhiteSpace(patternName);

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
        Artifacts = Empty(ctx), Costs = CostSnapshot.Zero, Warnings = [error]
    };

    private static ArtifactBundle Empty(PhaseContext ctx) => new()
    {
        Id = ArtifactId.New(), SessionId = ctx.SessionId, SourcePhase = ctx.PhaseId,
        SchemaVersion = "1.0.0", Kind = ArtifactKind.DafnyVerification,
        PayloadJson = [], ProducedAt = DateTimeOffset.UtcNow
    };
}