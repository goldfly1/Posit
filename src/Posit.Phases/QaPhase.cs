using Posit.Contracts.Artifacts;
using Posit.Tools;

namespace Posit.Phases;

/// <summary>
/// Phase 3: QA. The pseudodata bot generates test data from carapace interfaces
/// and architect test case categories. Deterministic — no LLM call.
/// </summary>
public sealed class QaPhase : IPhase
{
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
        var testDataFiles = new List<TestDataFile>();

        if (contract != null)
        {
            var cliComp = contract.Components.FirstOrDefault(c => c.Connections.Length > 0);
            if (cliComp != null)
            {
                // Find the logic component the CLI calls
                var logicCompName = cliComp.Connections.FirstOrDefault()?.ToComponent;
                var logicComp = logicCompName != null
                    ? contract.Components.FirstOrDefault(c => c.Name == logicCompName)
                    : null;

                if (logicComp != null)
                {
                    // Use the pseudodata bot to generate typed test data
                    var bot = new PseudodataBot();
                    testDataFiles = bot.Generate(cliComp, logicComp, contract.SystemContext);
                }
                else
                {
                    // No logic component found — fall back to raw test case descriptions
                    testDataFiles = GenerateFallback(cliComp);
                }
            }
        }

        // Build expected output maps keyed by test case index
        var expectedOutputs = new Dictionary<string, string>();
        var expectedExitCodes = new Dictionary<string, int>();
        for (var i = 0; i < testDataFiles.Count; i++)
        {
            var key = $"tc{i + 1}";
            expectedOutputs[key] = testDataFiles[i].ExpectedOutput;
            expectedExitCodes[key] = testDataFiles[i].ExpectedExitCode;
        }

        var testSuite = new TestSuite
        {
            TestFiles = testDataFiles.Select(t => new SourceCodeFile(t.FileName, t.Content)).ToArray(),
            ExpectedOutputs = expectedOutputs,
            ExpectedExitCodes = expectedExitCodes,
            Summary = $"QA: {testDataFiles.Count} test data file(s)."
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

    /// <summary>
    /// Fallback: extract test data from architect test case descriptions
    /// when no logic component is available for the pseudodata bot.
    /// </summary>
    private static List<TestDataFile> GenerateFallback(Component cliComp)
    {
        var isStdin = (cliComp.EntryType ?? "file").Equals("stdin", StringComparison.OrdinalIgnoreCase);
        var files = new List<TestDataFile>();

        foreach (var tc in cliComp.TestCases)
        {
            var inputMatch = System.Text.RegularExpressions.Regex.Match(
                tc.Description, @"'([^']+)'");
            var inputContent = inputMatch.Success ? inputMatch.Groups[1].Value : tc.Description;

            files.Add(new TestDataFile
            {
                FileName = isStdin ? $"stdin_{files.Count}.txt" : $"testdata_{files.Count}.txt",
                // Architect's concrete input/answer key are primary (may be empty
                // for legacy contracts — then the description heuristic stands).
                Content = string.IsNullOrWhiteSpace(tc.Input) ? inputContent : tc.Input,
                Description = tc.ExpectedBehavior ?? tc.Description,
                ExpectedOutput = tc.ExpectedOutput ?? "",
                ExpectedExitCode = tc.ExpectedExitCode
            });
        }

        if (files.Count == 0)
        {
            files.Add(new TestDataFile
            {
                FileName = isStdin ? "stdin_default.txt" : "testdata_default.txt",
                Content = "test input",
                Description = "Default test data (no test cases available)"
            });
        }

        return files;
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