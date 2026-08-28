using Posit.Contracts.Artifacts;

namespace Posit.Phases;

/// <summary>
/// Phase 3: QA. Produces test data files from the architect's test cases.
/// The deterministic pseudodata bot will replace this (planned). For now,
/// test data is derived from the architect's test case descriptions — no LLM call.
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
                var isStdin = (cliComp.EntryType ?? "file").Equals("stdin", StringComparison.OrdinalIgnoreCase);
                foreach (var tc in cliComp.TestCases)
                {
                    var inputMatch = System.Text.RegularExpressions.Regex.Match(
                        tc.Description, @"'([^']+)'");
                    var inputContent = inputMatch.Success ? inputMatch.Groups[1].Value : tc.Description;

                    testDataFiles.Add(new TestDataFile
                    {
                        FileName = isStdin ? $"stdin_{testDataFiles.Count}.txt" : $"testdata_{testDataFiles.Count}.txt",
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
    /// <summary>
    /// The exact stdout output expected when the program processes this test input.
    /// Empty string = no exact match (use fuzzy comparison).
    /// </summary>
    public string ExpectedOutput { get; init; } = "";
    /// <summary>
    /// Expected exit code. 0 = success, 1 = error. Default 0.
    /// </summary>
    public int ExpectedExitCode { get; init; } = 0;
}