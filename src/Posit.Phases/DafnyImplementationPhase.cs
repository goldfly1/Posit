namespace Posit.Phases;

/// <summary>
/// Phase 3: Dafny Implementation. Purely deterministic — NO model call.
/// Pre-verified pattern skeletons are translated directly to C#.
/// If a skeleton fails Z3, the correction signal routes back to Architecture.
/// (Spec: "Code (no model): Dafny Verify → Dafny Translate")
/// </summary>
public sealed class DafnyImplementationPhase : IPhase
{
    private readonly Z3Runner _z3;

    public DafnyImplementationPhase(Z3Runner z3) => _z3 = z3;

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

            var skeletonPath = ResolveDafnyPath(context, comp);
            if (!File.Exists(skeletonPath))
            {
                warnings.Add($"Skeleton file missing: {skeletonPath}");
                results.Add(FailResult(comp.Name, skeletonPath, "Skeleton file missing"));
                continue;
            }

            // Step 1: Verify the skeleton with Z3
            var verifyResult = await _z3.VerifyAsync(skeletonPath, ct);
            if (!verifyResult.Success)
            {
                var msg = verifyResult.Stdout[..Math.Min(300, verifyResult.Stdout.Length)];
                warnings.Add($"Z3 verification failed for '{comp.Name}': {msg}");
                results.Add(new DafnyVerificationResult
                {
                    ModuleName = comp.Name, DafnyPath = skeletonPath,
                    IsVerified = false, VerificationOutput = verifyResult.Stdout
                });
                continue;
            }

            // Step 2: Translate verified Dafny to C#
            var translation = await _z3.TranslateAsync(skeletonPath, comp.Name, ct);
            if (!translation.Success || string.IsNullOrWhiteSpace(translation.CleanCsharp))
            {
                warnings.Add($"C# translation failed for '{comp.Name}': {translation.Stderr}");
                results.Add(new DafnyVerificationResult
                {
                    ModuleName = comp.Name, DafnyPath = skeletonPath,
                    IsVerified = false, VerificationOutput = translation.Stderr
                });
                continue;
            }

            var csPath = Path.Combine(stagingDir, $"{comp.Name}.cs");
            await File.WriteAllTextAsync(csPath, translation.CleanCsharp, ct);
            results.Add(new DafnyVerificationResult
            {
                ModuleName = comp.Name, DafnyPath = skeletonPath,
                IsVerified = true, TranslatedCSharpPath = csPath
            });
        }

        var allOk = results.Count > 0 && results.All(r => r.IsVerified && !string.IsNullOrWhiteSpace(r.TranslatedCSharpPath));
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

    private static DafnyVerificationResult FailResult(string name, string path, string error) => new()
    {
        ModuleName = name, DafnyPath = path, IsVerified = false, VerificationOutput = error
    };

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