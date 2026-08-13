using System.Text.Json;
using Posit.Contracts.Serialization;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Phases;

/// <summary>
/// QA phase — DETERMINISTIC. No model call.
///
/// For verified (Dafny) modules: records that the proof IS the test.
/// For unverified (io-shell) modules: records that the bot harness will test them.
///
/// The bot harness (BotHarness.cs) IS the test — it pushes data through the CLI,
/// captures output, and compares to spec. No model judgment in the test phase.
/// This phase just records metadata. The actual testing happens in the harness.
/// </summary>
public sealed class QaPhase : IPhase
{
    private static readonly JsonSerializerOptions JsonOptions = Options;

    public QaPhase() { }

    public PhaseId Id => new("qa");
    public PhaseName Name => new("QA");
    public PhaseId[] Dependencies => [new PhaseId("csharp-implementation")];

    public ArtifactSchema OutputSchema => new()
    {
        Kind = ArtifactKind.TestSuite,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = typeof(TestSuite).FullName!
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct) => Task.CompletedTask;

    public Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct)
    {
        // Extract source code bundle from Pass 2 + verification results from Pass 1
        var (sourceFiles, moduleVerification) = ExtractInputs(context);

        if (sourceFiles.Count == 0 && moduleVerification.Count == 0)
        {
            Console.Error.WriteLine("[Posit] QA — no source files or verification results found");
            return Task.FromResult(new PhaseResult
            {
                PhaseId = Id,
                Status = PhaseStatus.Success,
                Artifacts = CreateEmptyBundle(context),
                Costs = CostSnapshot.Zero,
                AttemptNumber = context.AttemptNumber
            });
        }

        // Split modules into verified (Dafny) and unverified (io-shell)
        var verifiedModules = moduleVerification.Where(m => m.Value).Select(m => m.Key).ToHashSet();
        var unverifiedFiles = sourceFiles
            .Where(f => !IsVerifiedFile(f.path, verifiedModules))
            .ToList();

        Console.Error.WriteLine(
            $"[Posit] QA — {verifiedModules.Count} verified (proof IS the test), " +
            $"{unverifiedFiles.Count} unverified (bot harness will test)");

        var moduleResults = new List<QaModuleResult>();

        // Verified modules: proof IS the test
        foreach (var moduleName in verifiedModules)
        {
            moduleResults.Add(new QaModuleResult
            {
                ModuleName = moduleName,
                IsVerified = true,
                TestCount = 0,
                Notes = "Verified by Z3 — proof IS the test"
            });
        }

        // Unverified modules: bot harness will test
        foreach (var file in unverifiedFiles)
        {
            var moduleName = ExtractModuleName(file.path);
            if (!string.IsNullOrEmpty(moduleName))
            {
                moduleResults.Add(new QaModuleResult
                {
                    ModuleName = moduleName,
                    IsVerified = false,
                    TestCount = 0,
                    Notes = "Bot harness will test (deterministic — push data, compare output)"
                });
            }
        }

        // No test files generated — the bot harness IS the test.
        // It pushes data through the CLI, captures output, compares to spec.
        var testSuite = new TestSuite
        {
            TestFiles = [],
            ModuleResults = [.. moduleResults],
            Summary = $"{verifiedModules.Count} verified (proof IS the test), {unverifiedFiles.Count} unverified (bot harness)"
        };

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(testSuite, JsonOptions);
        var bundle = new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = Id,
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.TestSuite,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = context.InputArtifacts
                .Select(a => new ArtifactReference(a.Id, a.Kind, a.SchemaVersion))
                .ToArray()
        };

        return Task.FromResult(new PhaseResult
        {
            PhaseId = Id,
            Status = PhaseStatus.Success,
            Artifacts = bundle,
            Costs = CostSnapshot.Zero,
            AttemptNumber = context.AttemptNumber
        });
    }

    private static (List<(string path, string content)> Files, Dictionary<string, bool> Verification) ExtractInputs(PhaseContext context)
    {
        var files = new List<(string, string)>();
        var verification = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in context.InputArtifacts)
        {
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);

                if (artifact.Kind == ArtifactKind.SourceCodeBundle)
                {
                    var sourceBundle = JsonSerializer.Deserialize<SourceCodeBundle>(json, JsonOptions);
                    if (sourceBundle?.Files is not null)
                    {
                        foreach (var f in sourceBundle.Files)
                            files.Add((f.Path, f.Content));
                    }
                }
                else if (artifact.Kind == ArtifactKind.DafnyVerification)
                {
                    var results = JsonSerializer.Deserialize<DafnyVerificationResult[]>(json, JsonOptions);
                    if (results is not null)
                    {
                        foreach (var r in results)
                            verification[r.ModuleName] = r.IsVerified;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Posit] QA — failed to parse artifact: {ex.Message}");
            }
        }

        return (files, verification);
    }

    private static bool IsVerifiedFile(string path, HashSet<string> verifiedModules)
    {
        if (verifiedModules.Count == 0)
            return false;

        var fileName = Path.GetFileNameWithoutExtension(path);
        return verifiedModules.Contains(fileName) ||
               verifiedModules.Any(m => fileName.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractModuleName(string path)
        => Path.GetFileNameWithoutExtension(path);

    private static ArtifactBundle CreateEmptyBundle(PhaseContext context)
    {
        var emptySuite = new TestSuite
        {
            TestFiles = [],
            ModuleResults = [],
            Summary = "No modules to test"
        };
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(emptySuite, JsonOptions);
        return new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = new PhaseId("qa"),
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.TestSuite,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = []
        };
    }

    public Task<ValidationResult> ValidateOutputAsync(ArtifactBundle output, CancellationToken ct)
    {
        var errors = new List<string>();

        if (output.Kind != ArtifactKind.TestSuite)
            errors.Add("validation.schema_mismatch: Kind");
        if (output.SchemaVersion != "1.0.0")
            errors.Add("validation.schema_mismatch: SchemaVersion");

        try
        {
            var suite = JsonSerializer.Deserialize<TestSuite>(output.PayloadJson, JsonOptions);
            if (suite is null)
                errors.Add("validation.missing_required_field: Payload");
        }
        catch (JsonException ex)
        {
            errors.Add($"validation.schema_mismatch: {ex.Message}");
        }

        return Task.FromResult(new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray()
        });
    }
}