namespace Posit.Phases;

/// <summary>
/// Phase 5: QA. Deterministic — no model call, no test generation.
/// Records metadata only:
/// - Verified (Dafny) modules: "proof IS the test"
/// - Unverified (io-shell) modules: "bot harness will test"
/// </summary>
public sealed class QaPhase : IPhase
{
    public QaPhase() { }

    public PhaseId Id { get; } = new("qa");
    public string Name => "QA";
    public PhaseId[] Dependencies { get; } = [new("csharp-implementation")];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.TestSuite,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(TestSuite)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        var contract = ExtractContract(context);
        var moduleResults = new List<QaModuleResult>();

        if (contract != null)
        {
            foreach (var comp in contract.Components)
            {
                moduleResults.Add(new QaModuleResult
                {
                    ModuleName = comp.Name,
                    IsVerified = comp.Classification != ModuleClassification.IoShell && comp.IsVerified,
                    TestCount = 0,
                    Notes = comp.Classification != ModuleClassification.IoShell && comp.IsVerified
                        ? "proof IS the test"
                        : "bot harness will test"
                });
            }
        }

        var testSuite = new TestSuite
        {
            TestFiles = [],
            ModuleResults = moduleResults.ToArray(),
            Summary = $"QA metadata: {moduleResults.Count} modules, " +
                      $"{moduleResults.Count(m => m.IsVerified)} verified, " +
                      $"{moduleResults.Count(m => !m.IsVerified)} unverified (bot harness)"
        };

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(testSuite, PositJson.Options);

        return Task.FromResult(new PhaseResult
        {
            PhaseId = context.PhaseId,
            Status = PhaseStatus.Success,
            Artifacts = new ArtifactBundle
            {
                Id = ArtifactId.New(),
                SessionId = context.SessionId,
                SourcePhase = context.PhaseId,
                SchemaVersion = "1.0.0",
                Kind = ArtifactKind.TestSuite,
                PayloadJson = payloadJson,
                ProducedAt = DateTimeOffset.UtcNow
            },
            Costs = CostSnapshot.Zero
        });
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
}