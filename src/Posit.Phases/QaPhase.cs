using Posit.AI.Models;
using Posit.Contracts.Artifacts;

namespace Posit.Phases;

/// <summary>
/// Phase 5: QA. AI-assisted test data generation + failure analysis.
/// - Generates spec-specific test data via LLM (edge cases, boundary conditions)
/// - Bot harness runs the tests deterministically
/// - LLM analyzes failures and classifies them
/// - Verified (Dafny) modules: "proof IS the test" — no test generation needed
/// - Unverified (io-shell) modules: bot harness tests with AI-generated data
/// </summary>
public sealed class QaPhase : IPhase
{
    private readonly IModelGateway? _model;

    public QaPhase() { _model = null; }
    public QaPhase(IModelGateway model) { _model = model; }

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

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        var contract = ExtractContract(context);
        var moduleResults = new List<QaModuleResult>();
        var testDataFiles = new List<TestDataFile>();

        if (contract != null)
        {
            // Generate test data for the CLI component using AI
            var cliComp = contract.Components.FirstOrDefault(c => c.Connections.Length > 0);
            if (cliComp != null && _model != null)
            {
                var generated = await GenerateTestDataAsync(cliComp, contract, context, ct);
                if (generated != null)
                    testDataFiles.AddRange(generated);
            }
            else if (cliComp != null && _model == null)
            {
                // Fallback: no model available, use deterministic stopgap data
                testDataFiles.Add(new TestDataFile
                {
                    FileName = "testdata_default.csv",
                    Content = "name,age\nAlice,30\nBob,25",
                    Description = "Default test data (no AI available)"
                });
            }

            foreach (var comp in contract.Components)
            {
                var isVerified = comp.Classification != ModuleClassification.IoShell && comp.IsVerified;
                moduleResults.Add(new QaModuleResult
                {
                    ModuleName = comp.Name,
                    IsVerified = isVerified,
                    TestCount = isVerified ? 0 : testDataFiles.Count,
                    Notes = isVerified
                        ? "proof IS the test"
                        : $"bot harness will test with {testDataFiles.Count} AI-generated test data file(s)"
                });
            }
        }

        var testSuite = new TestSuite
        {
            TestFiles = testDataFiles.Select(t => new SourceCodeFile(t.FileName, t.Content)).ToArray(),
            ModuleResults = moduleResults.ToArray(),
            Summary = $"QA: {moduleResults.Count} modules, " +
                      $"{moduleResults.Count(m => m.IsVerified)} verified, " +
                      $"{moduleResults.Count(m => !m.IsVerified)} unverified. " +
                      $"{testDataFiles.Count} test data file(s) generated."
        };

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(testSuite, PositJson.Options);

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
                Kind = ArtifactKind.TestSuite,
                PayloadJson = payloadJson,
                ProducedAt = DateTimeOffset.UtcNow
            },
            Costs = CostSnapshot.Zero
        };
    }

    /// <summary>
    /// Generate spec-specific test data with edge cases using the LLM.
    /// The model sees the spec, the CLI component's connections, and the
    /// edge case catalog, then generates concrete test input files.
    /// </summary>
    private async Task<List<TestDataFile>?> GenerateTestDataAsync(
        Component cliComp, ArchitectureContract contract, PhaseContext context, CancellationToken ct)
    {
        var spec = context.UserRequest ?? "";
        var connections = string.Join(", ", cliComp.Connections.Select(c => $"{c.FromMethod}→{c.ToComponent}.{c.ToMethod}"));

        var systemPrompt = $"""
            You are the QA phase of the Posit spec compiler.
            Generate test data files for this program. Each file is a concrete input
            that exercises a specific behavior or edge case.

            Spec: {spec}

            Pipeline: {connections}

            Rules:
            1. Output JSON array of test data files.
            2. Each file has: "fileName" (string), "content" (string), "description" (string).
            3. Generate 3-5 files: valid input, empty/edge case, invalid input, file-not-found case.
            4. For file-not-found: use fileName "NONEXISTENT" and content "".
            5. Content must be the actual file content the program would read.
            6. For JSON input: content starts with [. For CSV input: content has commas and newlines.
            """;

        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt,
            OutputFormatSpec = "[{\"fileName\":\"...\",\"content\":\"...\",\"description\":\"...\"}]",
            ModelTier = ModelTier.Fast,
            Temperature = 0.3,
            MaxOutputTokens = 4096,
            OutputFormat = OutputFormat.Json,
            OutputSchemaRef = "TestDataFile[]",
            Status = PromptStatus.Active
        };

        try
        {
            var gen = await _model!.GenerateAsync(context.ModelRoute, prompt, context, ct);
            if (string.IsNullOrWhiteSpace(gen.Text))
                return null;

            var cleaned = OllamaModelGateway.ExtractJson(gen.Text);
            var files = JsonSerializer.Deserialize<TestDataFile[]>(cleaned, PositJson.Options);
            return files?.ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[qa] AI test data generation failed: {ex.Message}");
            return null;
        }
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

public record TestDataFile
{
    public string FileName { get; init; } = "";
    public string Content { get; init; } = "";
    public string Description { get; init; } = "";
}