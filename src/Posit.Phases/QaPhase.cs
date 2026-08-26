using Posit.AI.Models;
using Posit.Contracts.Artifacts;

namespace Posit.Phases;

/// <summary>
/// Phase 3: QA. AI-assisted test data generation + Docker harness execution.
/// - Generates spec-specific test data via LLM (edge cases, boundary conditions)
/// - Bot harness runs the tests deterministically
/// - All components get tests (no Z3-verified shortcut)
/// </summary>
public sealed class QaPhase : IPhase
{
    private readonly IModelGateway? _model;
    private readonly IPatternRegistry? _registry;

    public QaPhase() { _model = null; _registry = null; }
    public QaPhase(IModelGateway model, IPatternRegistry? registry = null) { _model = model; _registry = registry; }

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
                else
                {
                    // AI generation failed — fall back to architect's test cases
                    var isStdin = (cliComp.EntryType ?? "file").Equals("stdin", StringComparison.OrdinalIgnoreCase);
                    foreach (var tc in cliComp.TestCases)
                    {
                        var inputMatch = System.Text.RegularExpressions.Regex.Match(
                            tc.Description, @"'([^']+)'");
                        var inputContent = inputMatch.Success ? inputMatch.Groups[1].Value : tc.Description;

                        testDataFiles.Add(new TestDataFile
                        {
                            FileName = isStdin ? $"stdin_{testDataFiles.Count}.txt"
                                               : $"testdata_{testDataFiles.Count}.txt",
                            Content = inputContent,
                            Description = tc.ExpectedBehavior ?? tc.Description
                        });
                    }
                    if (testDataFiles.Count == 0)
                    {
                        testDataFiles.Add(new TestDataFile
                        {
                            FileName = isStdin ? "stdin_default.txt" : "testdata_default.txt",
                            Content = "test input",
                            Description = "Default test data (no test cases available)"
                        });
                    }
                }
            }
            else if (cliComp != null && _model == null)
            {
                // No model — fall back to architect's test cases
                var isStdin = (cliComp.EntryType ?? "file").Equals("stdin", StringComparison.OrdinalIgnoreCase);
                foreach (var tc in cliComp.TestCases)
                {
                    var inputMatch = System.Text.RegularExpressions.Regex.Match(
                        tc.Description, @"'([^']+)'");
                    var inputContent = inputMatch.Success ? inputMatch.Groups[1].Value : tc.Description;
                    testDataFiles.Add(new TestDataFile
                    {
                        FileName = isStdin ? $"stdin_{testDataFiles.Count}.txt"
                                           : $"testdata_{testDataFiles.Count}.txt",
                        Content = inputContent,
                        Description = tc.ExpectedBehavior ?? tc.Description
                    });
                }
                if (testDataFiles.Count == 0)
                {
                    testDataFiles.Add(new TestDataFile
                    {
                        FileName = "testdata_default.txt",
                        Content = "test input",
                        Description = "Default test data (no model, no test cases)"
                    });
                }
            }

            // All components get tests — no Z3-verified shortcut
            foreach (var comp in contract.Components)
            {
                moduleResults.Add(new QaModuleResult
                {
                    ModuleName = comp.Name,
                    IsVerified = false,
                    TestCount = testDataFiles.Count,
                    Notes = comp.Classification == ModuleClassification.IoShell
                        ? "io-shell stub (bot harness tests I/O)"
                        : "logic component (bot harness tests behavior)"
                });
            }
        }

        var testSuite = new TestSuite
        {
            TestFiles = testDataFiles.Select(t => new SourceCodeFile(t.FileName, t.Content)).ToArray(),
            ModuleResults = moduleResults.ToArray(),
            Summary = $"QA: {moduleResults.Count} modules — {testDataFiles.Count} test data file(s)."
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

    private async Task<List<TestDataFile>?> GenerateTestDataAsync(
        Component cliComp, ArchitectureContract contract, PhaseContext context, CancellationToken ct)
    {
        var spec = context.UserRequest ?? "";
        var connections = string.Join(", ", cliComp.Connections.Select(c => $"{c.FromMethod}→{c.ToComponent}.{c.ToMethod}"));
        var testCasesText = cliComp.TestCases.Length > 0
            ? string.Join("\n", cliComp.TestCases.Select(tc => $"  - {tc.Description} → {tc.ExpectedBehavior}"))
            : "(no test cases defined by architect — generate from spec)";
        var entryType = cliComp.EntryType ?? "file";
        var isStdin = entryType.Equals("stdin", StringComparison.OrdinalIgnoreCase);

        var systemPrompt = $"""
            You are a Senior QA Engineer. Generate test data for this program based on the spec and the architect's test cases.

            Spec: {spec}

            Pipeline: {connections}

            Entry type: {(isStdin ? "stdin (reads from Console.ReadLine)" : "file (reads from args[0])")}

            Architect's test cases (use these as the basis for your test data):
            {testCasesText}

            Rules:
            1. Output a JSON ARRAY directly — NOT wrapped in an object. Start with [ and end with ].
            2. Each element has: "fileName" (string), "content" (string), "description" (string).
            3. Generate 3-6 test cases covering: valid input, edge case, invalid input, and empty input.
            4. Content must be the ACTUAL input the program would receive.
            5. For stdin programs: content is the line(s) typed at the console.
            6. For file-based programs: content is the file content.
            7. Match the input format described in the spec — do NOT default to CSV or JSON unless the spec requires it.
            8. For file-not-found: use fileName "NONEXISTENT" and content "".
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

            // Model may wrap array in an object like { "testData": [...] } — unwrap if so
            List<TestDataFile>? files = null;
            if (cleaned.TrimStart().StartsWith('['))
            {
                files = JsonSerializer.Deserialize<List<TestDataFile>>(cleaned, PositJson.Options);
            }
            else if (cleaned.TrimStart().StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(cleaned);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        files = JsonSerializer.Deserialize<List<TestDataFile>>(prop.Value.GetRawText(), PositJson.Options);
                        break;
                    }
                }
            }

            return files;
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