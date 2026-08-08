using System.Text;
using System.Text.Json;
using Posit.AI.Models;
using Posit.Data.Repositories;
using Posit.Contracts.Serialization;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Phases;

/// <summary>
/// QA phase — compiles translated C# (verified modules) and generates
/// tests for unverified (io-shell) modules.
///
/// For verified (Dafny) modules: compile only. The proof IS the test.
/// For unverified (io-shell) modules: full test generation via model.
///
/// If a test fails and Imp appeals, the orchestrator routes the appeal
/// to kimi-2.7-code:cloud (independent reviewer). This phase does not
/// handle appeals directly.
///
/// Model: glm-5.2:cloud
/// </summary>
public sealed class QaPhase : IPhase
{
    private static readonly JsonSerializerOptions JsonOptions = Options;

    private readonly IModelGateway _gateway;

    /// <summary>
    /// The QA phase needs more output tokens than other phases because it must
    /// generate complete test files as JSON. Default cap is 64K; QA gets 64K.
    /// </summary>
    private const int QaMaxOutputTokens = 64000;

    public QaPhase(IModelGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

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

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct)
    {
        // Extract source code bundle from Pass 2 + verification results from Pass 1
        var (sourceFiles, moduleVerification) = ExtractInputs(context);

        if (sourceFiles.Count == 0 && moduleVerification.Count == 0)
        {
            Console.Error.WriteLine("[Posit] QA — no source files or verification results found");
            return new PhaseResult
            {
                PhaseId = Id,
                Status = PhaseStatus.Success,
                Artifacts = CreateEmptyBundle(context),
                Costs = CostSnapshot.Zero,
                AttemptNumber = context.AttemptNumber
            };
        }

        // Split modules into verified (compile only) and unverified (generate tests)
        var verifiedModules = moduleVerification.Where(m => m.Value).Select(m => m.Key).ToHashSet();
        var unverifiedFiles = sourceFiles
            .Where(f => !IsVerifiedFile(f.path, verifiedModules))
            .ToList();

        Console.Error.WriteLine(
            $"[Posit] QA — {verifiedModules.Count} verified (compile only), " +
            $"{unverifiedFiles.Count} unverified files (test generation)");

        var testFiles = new List<SourceCodeFile>();
        var moduleResults = new List<QaModuleResult>();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        // Verified modules: no tests, just record metadata
        foreach (var moduleName in verifiedModules)
        {
            moduleResults.Add(new QaModuleResult
            {
                ModuleName = moduleName,
                IsVerified = true,
                TestCount = 0,
                Notes = "Verified by Z3 — no tests generated (proof IS the test)"
            });
        }

        // Unverified modules: generate tests
        if (unverifiedFiles.Count > 0)
        {
            var (generatedTests, inTok, outTok) = await GenerateTestsAsync(context, unverifiedFiles, ct);
            testFiles.AddRange(generatedTests);
            totalInputTokens += inTok;
            totalOutputTokens += outTok;

            // Record module results for unverified modules
            foreach (var file in unverifiedFiles)
            {
                var moduleName = ExtractModuleName(file.path);
                if (!string.IsNullOrEmpty(moduleName))
                {
                    moduleResults.Add(new QaModuleResult
                    {
                        ModuleName = moduleName,
                        IsVerified = false,
                        TestCount = generatedTests.Count(t => t.Path.Contains(moduleName, StringComparison.OrdinalIgnoreCase)),
                        Notes = "Test suite generated"
                    });
                }
            }
        }

        var testSuite = new TestSuite
        {
            TestFiles = [.. testFiles],
            ModuleResults = [.. moduleResults],
            Summary = $"{verifiedModules.Count} verified (compile only), {testFiles.Count} test files for unverified modules"
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

        return new PhaseResult
        {
            PhaseId = Id,
            Status = PhaseStatus.Success,
            Artifacts = bundle,
            Costs = new CostSnapshot
            {
                InputTokens = totalInputTokens,
                OutputTokens = totalOutputTokens,
                ModelTier = context.ModelRoute.Tier
            },
            AttemptNumber = context.AttemptNumber
        };
    }

    /// <summary>
    /// Extract source files from C# Implementation (Pass 2) and
    /// verification results from Dafny Implementation (Pass 1).
    /// </summary>
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

    private async Task<(List<SourceCodeFile>, int, int)> GenerateTestsAsync(
        PhaseContext context, List<(string path, string content)> unverifiedFiles, CancellationToken ct)
    {
        var systemPrompt = BuildQaPrompt(unverifiedFiles);
        var prompt = context.Prompt with { SystemPrompt = systemPrompt };

        Console.Error.WriteLine($"[Posit] QA — calling model for test generation...");

        // QA needs a larger output budget than other phases
        var route = context.ModelRoute with { MaxOutputTokens = QaMaxOutputTokens };
        var generation = await _gateway.GenerateAsync(route, prompt, context, ct);

        // Capture the prompt→response pair
        await PromptLogger.LogPromptAsync(
            context.SessionId.Value, Id.Value, context.AttemptNumber,
            null, "generate",
            context.ModelRoute.ProviderId, context.ModelRoute.ModelId,
            systemPrompt, null,
            generation.Text,
            generation.InputTokens, generation.OutputTokens,
            generation.CostUsd, (long)generation.Latency.TotalMilliseconds,
            null, null, ct);

        var testFiles = ParseTestOutput(generation.Text);
        Console.Error.WriteLine($"[Posit] QA — model returned {testFiles.Count} test files");

        return (testFiles, generation.InputTokens, generation.OutputTokens);
    }

    private static string BuildQaPrompt(List<(string path, string content)> unverifiedFiles)
    {
        var sb = new StringBuilder();

        // Load prompt template
        var promptPath = Path.Combine(AppContext.BaseDirectory, "prompts", "qa", "1.0.0.md");
        if (!File.Exists(promptPath))
            promptPath = Path.Combine(Directory.GetCurrentDirectory(), "prompts", "qa", "1.0.0.md");

        if (File.Exists(promptPath))
            sb.AppendLine(File.ReadAllText(promptPath));
        else
            sb.AppendLine("You are the QA phase. Generate xUnit tests for the unverified C# modules. Respond with JSON: {testFiles: [{path, content}], moduleResults: [{moduleName, isVerified, testCount, notes}], summary}.");

        sb.AppendLine();
        sb.AppendLine("--- UNVERIFIED MODULES (generate tests for these) ---");

        foreach (var (path, content) in unverifiedFiles)
        {
            sb.AppendLine($"File: {path}");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        sb.AppendLine("Generate xUnit tests for each module above. Respond with valid JSON.");

        return sb.ToString();
    }

    private static List<SourceCodeFile> ParseTestOutput(string text)
    {
        var files = new List<SourceCodeFile>();

        if (string.IsNullOrWhiteSpace(text))
            return files;

        try
        {
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            var json = OllamaModelGateway.ExtractJson(cleaned);
            json = SanitizeJson(json);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("testFiles", out var testFilesEl))
            {
                foreach (var element in testFilesEl.EnumerateArray())
                {
                    var path = element.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var content = element.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(content))
                        files.Add(new SourceCodeFile(path, content));
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                // Fallback: bare array of files
                foreach (var element in root.EnumerateArray())
                {
                    var path = element.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var content = element.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(content))
                        files.Add(new SourceCodeFile(path, content));
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] QA — failed to parse test output: {ex.Message}");
        }

        return files;
    }

    /// <summary>
    /// Sanitizes JSON returned by models that may include invalid escape sequences
    /// or other malformations that cause JsonDocument.Parse to fail.
    /// </summary>
    private static string SanitizeJson(string json)
    {
        // Remove stray backslashes that aren't valid JSON escape sequences
        // (e.g., \S, \T, \n in C# code strings that the model didn't escape properly)
        var sb = new StringBuilder(json.Length);
        var i = 0;
        while (i < json.Length)
        {
            if (json[i] == '\\' && i + 1 < json.Length)
            {
                var next = json[i + 1];
                // Valid JSON escape characters: " \ / b f n r t u
                if (next == '"' || next == '\\' || next == '/' || next == 'b' || next == 'f'
                    || next == 'n' || next == 'r' || next == 't' || next == 'u')
                {
                    if (next == 'u' && i + 5 < json.Length)
                    {
                        // Copy \uXXXX as-is
                        sb.Append(json, i, 6);
                        i += 6;
                        continue;
                    }
                    sb.Append(json[i]);
                    sb.Append(next);
                    i += 2;
                    continue;
                }
                // Invalid escape — replace with double backslash
                sb.Append("\\\\");
                i++;
                continue;
            }
            sb.Append(json[i]);
            i++;
        }
        return sb.ToString();
    }

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