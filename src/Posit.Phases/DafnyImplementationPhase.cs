namespace Posit.Phases;

using System.Text;
using Posit.AI.Models;

/// <summary>
/// Phase 3: Dafny Implementation. Hybrid — deterministic for cut-outs, model-generated for custom logic.
/// - Cut-out components: skeleton exists on disk → Z3 verify → translate (no model call)
/// - Custom components: no skeleton → model writes Dafny from spec + reference card → Z3 verify → translate
/// Z3 is the judge in both paths. Design Review is the QA.
/// </summary>
public sealed class DafnyImplementationPhase : IPhase
{
    private readonly Z3Runner _z3;
    private readonly IModelGateway? _model;

    public DafnyImplementationPhase(Z3Runner z3) { _z3 = z3; _model = null; }
    public DafnyImplementationPhase(Z3Runner z3, IModelGateway model) { _z3 = z3; _model = model; }

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
            var isCutOut = File.Exists(skeletonPath);

            // If no skeleton (not a cut-out), generate Dafny with the model
            if (!isCutOut && _model != null)
            {
                var generated = await GenerateDafnyAsync(comp, context, ct);
                if (string.IsNullOrWhiteSpace(generated))
                {
                    warnings.Add($"Model returned empty Dafny for '{comp.Name}'");
                    results.Add(FailResult(comp.Name, skeletonPath, "Empty model output"));
                    continue;
                }
                skeletonPath = Path.Combine(stagingDir, $"{comp.Name}.dfy");
                await File.WriteAllTextAsync(skeletonPath, generated, ct);
            }
            else if (!isCutOut)
            {
                warnings.Add($"No skeleton and no model for '{comp.Name}' — cannot generate Dafny");
                results.Add(FailResult(comp.Name, skeletonPath, "No skeleton and no model"));
                continue;
            }

            // Step 1: Verify the Dafny with Z3 (works for both cut-outs and generated)
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

    /// <summary>
    /// Generate Dafny code for a component using the model.
    /// Includes the Dafny reference card and the component's spec/responsibility/test cases.
    /// </summary>
    private async Task<string?> GenerateDafnyAsync(Component comp, PhaseContext context, CancellationToken ct)
    {
        var referenceCard = LoadReferenceCard();
        var testCases = comp.TestCases.Length > 0
            ? string.Join("\n", comp.TestCases.Select(tc => $"  // test: {tc.Description} → {tc.ExpectedBehavior}"))
            : "";

        var sigs = comp.MethodSignatures.Length > 0
            ? string.Join("\n", comp.MethodSignatures.Select(m =>
                $"  method {m.Name}({string.Join(", ", m.Params.Select(p => $"{p.Name}: {p.Type}"))}) returns ({m.ReturnType})"))
            : "";

        var systemPrompt = new StringBuilder();
        systemPrompt.AppendLine("You are writing a Dafny module for the Posit spec compiler.");
        systemPrompt.AppendLine("Write a COMPLETE Dafny module that implements the spec.");
        systemPrompt.AppendLine("Z3 will verify your code — it must pass verification.");
        systemPrompt.AppendLine();
        systemPrompt.AppendLine($"Component: {comp.Name}");
        systemPrompt.AppendLine($"Responsibility: {comp.Responsibility}");
        systemPrompt.AppendLine($"Pattern: {comp.PatternName ?? "custom"}");
        systemPrompt.AppendLine();
        systemPrompt.AppendLine("Method signatures to implement:");
        systemPrompt.AppendLine(sigs);
        systemPrompt.AppendLine();
        systemPrompt.AppendLine("Test cases (your implementation MUST satisfy these):");
        systemPrompt.AppendLine(testCases);
        systemPrompt.AppendLine();
        systemPrompt.AppendLine("Dafny Reference Card:");
        systemPrompt.AppendLine(referenceCard);
        systemPrompt.AppendLine();
        systemPrompt.AppendLine("Rules:");
        systemPrompt.AppendLine("1. Output ONLY raw Dafny code. No JSON, no markdown fences, no explanations.");
        systemPrompt.AppendLine("2. Keep method signatures as specified above.");
        systemPrompt.AppendLine("3. Keep all {:extern} declarations unchanged.");
        systemPrompt.AppendLine("4. Write real method bodies that implement the spec's logic.");
        systemPrompt.AppendLine("5. The code must pass Z3 verification.");
        systemPrompt.AppendLine("6. Use Dafny built-ins: seq concat (+), string concat (+), |s| for length, s[i] for access.");
        systemPrompt.AppendLine("7. Simple operations (parse, format, concat) do NOT need a pattern — write them inline.");

        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt.ToString(),
            OutputFormatSpec = "Dafny source code only",
            ModelTier = ModelTier.Fast,
            Temperature = 0.2,
            MaxOutputTokens = 8192,
            OutputFormat = OutputFormat.PlainText,
            OutputSchemaRef = "DafnyModule",
            Status = PromptStatus.Active
        };

        try
        {
            var gen = await _model!.GenerateAsync(context.ModelRoute, prompt, context, ct);
            if (string.IsNullOrWhiteSpace(gen.Text))
                return null;

            // Extract Dafny from response — strip reasoning tags, markdown fences, JSON wrappers
            var text = OllamaModelGateway.StripReasoningTags(gen.Text).Trim();

            // Strip markdown fences if present
            var fenceMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"```(?:dafny)?\s*\n?(.*?)\n?```",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (fenceMatch.Success)
                return CleanDafny(fenceMatch.Groups[1].Value);

            // Model may wrap Dafny in JSON — extract by scanning for code-like string property
            if (text.TrimStart().StartsWith('{'))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(text.TrimStart()[text.TrimStart().IndexOf('{')..]);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var s = prop.Value.GetString();
                            if (s != null && (s.Contains("method ") || s.Contains("module ")))
                                return CleanDafny(s);
                        }
                    }
                }
                catch { }
            }

            return CleanDafny(text);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[dafny-impl] Model generation failed for '{comp.Name}': {ex.Message}");
            return null;
        }
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
        return "// Dafny reference card not found";
    }

    private static string CleanDafny(string code) =>
        code.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

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