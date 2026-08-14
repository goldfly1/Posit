namespace Posit.Phases;

using System.Text;

/// <summary>
/// Phase 3: Dafny Implementation. For each dafny component, call the LLM to
/// write a spec-specific Dafny body that satisfies the pattern's contracts.
/// Z3 verifies the new body. If it fails, the correction signal (Z3 errors)
/// goes back to the model on retry. On success, translate to C#.
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

            var skeletonPath = ResolveDafnyPath(context, comp);
            if (!File.Exists(skeletonPath))
            {
                warnings.Add($"Skeleton file missing: {skeletonPath}");
                continue;
            }

            var skeleton = await File.ReadAllTextAsync(skeletonPath, ct);

            // Call LLM to write a spec-specific Dafny body
            var prompt = BuildImplPrompt(comp, skeleton, context);
            var gen = await _model.GenerateAsync(context.ModelRoute, prompt, context, ct);

            if (string.IsNullOrWhiteSpace(gen.Text))
            {
                warnings.Add($"LLM returned empty Dafny for '{comp.Name}'");
                results.Add(FailResult(comp.Name, skeletonPath, "Empty LLM output"));
                continue;
            }

            // Extract Dafny code from the response
            var dafnyCode = ExtractDafny(gen.Text);
            if (string.IsNullOrWhiteSpace(dafnyCode))
            {
                warnings.Add($"No Dafny code in LLM response for '{comp.Name}'");
                results.Add(FailResult(comp.Name, skeletonPath, "No Dafny code extracted"));
                continue;
            }

            // Write the new Dafny implementation to staging
            var implPath = Path.Combine(stagingDir, $"{comp.Name}.impl.dfy");
            await File.WriteAllTextAsync(implPath, dafnyCode, ct);

            // Z3 verify the new body
            var verifyResult = await _z3.VerifyAsync(implPath, ct);
            if (!verifyResult.Success)
            {
                warnings.Add($"Z3 verification failed for '{comp.Name}': {verifyResult.Stdout[..Math.Min(200, verifyResult.Stdout.Length)]}");
                results.Add(new DafnyVerificationResult
                {
                    ModuleName = comp.Name, DafnyPath = implPath,
                    IsVerified = false, VerificationOutput = verifyResult.Stdout
                });
                continue;
            }

            // Translate verified Dafny to C#
            var translation = await _z3.TranslateAsync(implPath, comp.Name, ct);
            if (!translation.Success || string.IsNullOrWhiteSpace(translation.CleanCsharp))
            {
                warnings.Add($"C# translation failed for '{comp.Name}': {translation.Stderr}");
                results.Add(new DafnyVerificationResult
                {
                    ModuleName = comp.Name, DafnyPath = implPath,
                    IsVerified = false, VerificationOutput = translation.Stderr
                });
                continue;
            }

            var csPath = Path.Combine(stagingDir, $"{comp.Name}.cs");
            await File.WriteAllTextAsync(csPath, translation.CleanCsharp, ct);

            results.Add(new DafnyVerificationResult
            {
                ModuleName = comp.Name, DafnyPath = implPath,
                IsVerified = true, TranslatedCSharpPath = csPath
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

    private static PromptTemplate BuildImplPrompt(Component comp, string skeleton, PhaseContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are writing a Dafny implementation for a component in a spec compiler pipeline.");
        sb.AppendLine("The architect has decomposed the spec and selected a pattern skeleton.");
        sb.AppendLine("Your job: write a COMPLETE Dafny module that implements the SPEC's requirements,");
        sb.AppendLine("using the skeleton's contracts and method signatures as the starting point.");
        sb.AppendLine();
        sb.AppendLine($"Component: {comp.Name}");
        sb.AppendLine($"Responsibility: {comp.Responsibility}");
        sb.AppendLine($"Pattern: {comp.PatternName}");
        sb.AppendLine($"Classification: {comp.Classification}");
        if (comp.ParametersJson is { Length: > 0 })
            sb.AppendLine($"Parameters: {comp.ParametersJson}");
        if (comp.TestCases is { Length: > 0 })
        {
            sb.AppendLine("Test cases (your implementation MUST satisfy these):");
            foreach (var tc in comp.TestCases)
                sb.AppendLine($"  - {tc.Name}: {tc.Description} → {tc.ExpectedBehavior}");
        }
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Output ONLY Dafny code. No markdown fences, no explanations.");
        sb.AppendLine("2. Keep the same module name and method signatures as the skeleton.");
        sb.AppendLine("3. Keep all {:extern} declarations unchanged — these are I/O portals.");
        sb.AppendLine("4. Write real method bodies that implement the spec's logic, not generic algorithm code.");
        sb.AppendLine("5. Ensure all requires/ensures clauses from the skeleton are preserved or strengthened.");
        sb.AppendLine("6. The code must pass Z3 verification.");
        sb.AppendLine();
        sb.AppendLine("Pattern skeleton (reference — adapt this to the spec):");
        sb.AppendLine(skeleton);

        return new PromptTemplate
        {
            PhaseId = ctx.PhaseId, Version = new PromptVersion("1.0.0"),
            SystemPrompt = sb.ToString(),
            OutputFormatSpec = "Dafny source code only",
            ModelTier = ModelTier.Fast, Temperature = 0.2, MaxOutputTokens = 8192,
            OutputFormat = OutputFormat.PlainText, OutputSchemaRef = "DafnyModule",
            Status = PromptStatus.Active
        };
    }

    private static string ExtractDafny(string text)
    {
        text = OllamaModelGateway.StripReasoningTags(text);
        // Strip markdown code fences
        var fenceMatch = System.Text.RegularExpressions.Regex.Match(
            text, @"```(?:dafny)?\s*\n?(.*?)\n?```", System.Text.RegularExpressions.RegexOptions.Singleline);
        if ( fenceMatch.Success)
            return fenceMatch.Groups[1].Value.Trim();
        return text.Trim();
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