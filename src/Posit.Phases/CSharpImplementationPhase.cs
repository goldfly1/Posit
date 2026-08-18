namespace Posit.Phases;

/// <summary>
/// Phase 4: C# Assembly. Three sub-steps, all deterministic (no model):
/// (a) extern portal caps from registry stubs
/// (b) io-shell stubs from registry (NEVER io-console-program)
/// (c) wiring via WiringGenerator
/// Include translated Dafny C# in source bundle (renamed to {Module}.cs,
/// NOT skeleton-*.cs). Deduplicate by path, keep last.
/// </summary>
public sealed class CSharpImplementationPhase : IPhase
{
    private readonly IModelGateway _model;
    private readonly IPatternRegistry _registry;

    public CSharpImplementationPhase(IModelGateway model, IPatternRegistry registry)
    {
        _model = model;
        _registry = registry;
    }

    public PhaseId Id { get; } = new("csharp-implementation");
    public string Name => "C# Implementation";
    public PhaseId[] Dependencies { get; } = [new("dafny-implementation")];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.SourceCodeBundle,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(SourceCodeBundle)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        try
        {
        var contract = ExtractContract(context);
        if (contract == null)
            return Fail(context, "No ArchitectureContract in input artifacts");

        var verificationResults = ExtractVerificationResults(context);
        var files = new List<SourceCodeFile>();
        var warnings = new List<string>();

        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell)
            {
                // Sub-step (b): io-shell stubs from registry (NEVER io-console-program)
                foreach (var stubName in comp.StubNames)
                {
                    if (stubName == "io-console-program")
                    {
                        warnings.Add($"Skipped io-console-program for '{comp.Name}' (entry point, not a stub)");
                        continue;
                    }
                    var stubContent = _registry.ComposeIoShellSkeleton(stubName, comp.Name);
                    var path = $"{comp.Name}/{comp.Name}.{stubName}.cs";
                    files.Add(new SourceCodeFile(path, stubContent));
                }
            }
            else
            {
                // Include translated Dafny C# (renamed to {Module}.cs, NOT skeleton-*.cs)
                var vr = verificationResults.FirstOrDefault(v => v.ModuleName == comp.Name);
                if (vr != null && !string.IsNullOrWhiteSpace(vr.TranslatedCSharpPath) && File.Exists(vr.TranslatedCSharpPath))
                {
                    var content = File.ReadAllText(vr.TranslatedCSharpPath);
                    files.Add(new SourceCodeFile($"{comp.Name}/{comp.Name}.cs", content));

                    // Sub-step (a): extern portal caps from registry stubs
                    foreach (var stubName in comp.StubNames)
                    {
                        var stubContent = _registry.ComposeIoShellSkeleton(stubName, comp.Name);
                        var path = $"{comp.Name}/{comp.Name}Extern.{stubName}.cs";
                        files.Add(new SourceCodeFile(path, stubContent));
                    }
                }
                else
                {
                    warnings.Add($"No translated C# found for '{comp.Name}'");
                }
            }
        }

        // Sub-step (c): wiring via deterministic WiringGenerator (primary)
        // The deterministic generator uses the ACTUAL translated C# method signatures
        // and type conversions — no guessing property names. The model generator is
        // only used as a fallback if the deterministic one can't handle the wiring.
        var translatedSigs = ScanTranslatedSignatures(files, contract);
        var stubSigs = ScanStubSignatures(files, contract);
        var modelWirer = new ModelWiringGenerator(_model);
        foreach (var comp in contract.Components)
        {
            if (comp.Connections.Length == 0) continue;
            // Primary: deterministic wiring (uses exact names from translated C#)
            var wireContent = WiringGenerator.Generate(comp, contract, translatedSigs, stubSigs);
            if (string.IsNullOrWhiteSpace(wireContent))
            {
                // Fallback to model if deterministic generator fails
                wireContent = await modelWirer.GenerateAsync(comp, contract, translatedSigs, stubSigs, context, ct);
            }
            files.Add(new SourceCodeFile($"{comp.Name}/Wire.cs", wireContent));
        }

        // Deduplicate by path, keep last occurrence
        var deduped = DeduplicateByPath(files);

        var bundle = new SourceCodeBundle
        {
            Files = deduped.ToArray(),
            ProjectPath = Directory.GetCurrentDirectory(),
            TargetFramework = "net10.0"
        };
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(bundle, PositJson.Options);

        return new PhaseResult
        {
            PhaseId = context.PhaseId,
            Status = PhaseStatus.Success,
            Artifacts = new ArtifactBundle
            {
                Id = ArtifactId.New(),
                SessionId = context.SessionId,
                SourcePhase = context.PhaseId,
                SchemaVersion = "1.0.0",
                Kind = ArtifactKind.SourceCodeBundle,
                PayloadJson = payloadJson,
                ProducedAt = DateTimeOffset.UtcNow
            },
            Costs = CostSnapshot.Zero,
            Warnings = warnings.ToArray()
        };
        }
        catch (Exception ex)
        {
            return Fail(context, $"C# impl exception: {ex.Message}");
        }
    }

    public ValidationResult ValidateOutput(PhaseResult result)
    {
        if (result.Status != PhaseStatus.Success)
            return new ValidationResult { IsValid = false, Errors = result.Warnings };
        return new ValidationResult { IsValid = true };
    }

    private static Dictionary<string, List<CsMethodSignature>> ScanTranslatedSignatures(
        List<SourceCodeFile> files, ArchitectureContract contract)
    {
        var result = new Dictionary<string, List<CsMethodSignature>>();
        foreach (var comp in contract.Components)
        {
            var file = files.FirstOrDefault(f => f.Path == $"{comp.Name}/{comp.Name}.cs");
            if (file != null)
                result[comp.Name] = TranslatedCSharpScanner.ScanContent(file.Content);
        }
        return result;
    }

    private static Dictionary<string, List<CsMethodSignature>> ScanStubSignatures(
        List<SourceCodeFile> files, ArchitectureContract contract)
    {
        var result = new Dictionary<string, List<CsMethodSignature>>();
        foreach (var comp in contract.Components)
        {
            var prefix = $"{comp.Name}/";
            var stubFiles = files.Where(f => f.Path.StartsWith(prefix) && f.Path != $"{comp.Name}/{comp.Name}.cs");
            var sigs = new List<CsMethodSignature>();
            foreach (var f in stubFiles)
                sigs.AddRange(TranslatedCSharpScanner.ScanContent(f.Content));
            result[comp.Name] = sigs;
        }
        return result;
    }

    private static List<SourceCodeFile> DeduplicateByPath(List<SourceCodeFile> files)
    {
        var dict = new Dictionary<string, SourceCodeFile>();
        foreach (var f in files)
            dict[f.Path] = f; // keep last
        return [.. dict.Values];
    }

    private static ArchitectureContract? ExtractContract(PhaseContext ctx)
    {
        foreach (var a in ctx.InputArtifacts)
            if (a.Kind == ArtifactKind.ArchitectureContract)
                try { return JsonSerializer.Deserialize<ArchitectureContract>(a.PayloadJson, PositJson.Options); }
                catch { }
        return null;
    }

    private static DafnyVerificationResult[] ExtractVerificationResults(PhaseContext ctx)
    {
        foreach (var a in ctx.InputArtifacts)
            if (a.Kind == ArtifactKind.DafnyVerification)
                try { return JsonSerializer.Deserialize<DafnyVerificationResult[]>(a.PayloadJson, PositJson.Options) ?? []; }
                catch { }
        return [];
    }

    private static PhaseResult Fail(PhaseContext ctx, string error) => new()
    {
        PhaseId = ctx.PhaseId, Status = PhaseStatus.Failed,
        Artifacts = Empty(ctx), Costs = CostSnapshot.Zero, Warnings = [error]
    };

    private static ArtifactBundle Empty(PhaseContext ctx) => new()
    {
        Id = ArtifactId.New(), SessionId = ctx.SessionId, SourcePhase = ctx.PhaseId,
        SchemaVersion = "1.0.0", Kind = ArtifactKind.SourceCodeBundle,
        PayloadJson = [], ProducedAt = DateTimeOffset.UtcNow
    };
}