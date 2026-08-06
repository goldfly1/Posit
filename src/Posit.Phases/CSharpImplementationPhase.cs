using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Posit.AI.Models;
using Posit.Data.Repositories;

namespace Posit.Phases;

/// <summary>
/// C# Implementation — Pass 2. Imp writes C# that plugs into the
/// partial class extern holes from Pass 1's translated Dafny output,
/// plus complete C# classes for io-shell modules.
///
/// No Z3 — this is unverified I/O. The build judge checks compilation.
/// On build failure, correction signal with compiler errors (existing
/// Shepherd pattern).
///
/// Model: glm-5.2:cloud
/// </summary>
public sealed class CSharpImplementationPhase : IPhase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IModelGateway _gateway;
    private const int MaxRetries = 2;

    public CSharpImplementationPhase(IModelGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public PhaseId Id => new("csharp-implementation");
    public PhaseName Name => new("C# Implementation (Pass 2)");
    public PhaseId[] Dependencies => [new PhaseId("dafny-implementation")];

    public ArtifactSchema OutputSchema => new()
    {
        Kind = ArtifactKind.SourceCodeBundle,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = "Posit.Contracts.Artifacts.SourceCodeBundle"
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct)
    {
        // Extract translated C# from Pass 1 + io-shell module specs from Architecture
        var (translatedFiles, ioShellSpecs) = ExtractInputs(context);

        if (translatedFiles.Count == 0 && ioShellSpecs.Count == 0)
        {
            Console.Error.WriteLine("[Posit] C# Implementation — no translated C# or io-shell specs found");
            return new PhaseResult
            {
                PhaseId = Id,
                Status = PhaseStatus.Success,
                Artifacts = CreateEmptyBundle(context),
                Costs = CostSnapshot.Zero,
                AttemptNumber = context.AttemptNumber
            };
        }

        var allFiles = new List<SourceCodeFile>();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        // Pass 2a: Fill extern holes in translated Dafny C#
        if (translatedFiles.Count > 0)
        {
            Console.Error.WriteLine($"[Posit] C# Implementation — filling {translatedFiles.Count} extern portal files...");
            var (files, inTok, outTok) = await ImplementExternPortalsAsync(context, translatedFiles, ct);
            allFiles.AddRange(files);
            totalInputTokens += inTok;
            totalOutputTokens += outTok;
        }

        // Pass 2b: Write C# for io-shell modules
        if (ioShellSpecs.Count > 0)
        {
            Console.Error.WriteLine($"[Posit] C# Implementation — writing {ioShellSpecs.Count} io-shell modules...");
            var (files, inTok, outTok) = await ImplementIoShellsAsync(context, ioShellSpecs, ct);
            allFiles.AddRange(files);
            totalInputTokens += inTok;
            totalOutputTokens += outTok;
        }

        Console.Error.WriteLine($"[Posit] C# Implementation — {allFiles.Count} C# files produced");

        var bundlePayload = new SourceCodeBundle
        {
            Files = [.. allFiles],
            ProjectPath = "src/Generated",
            TargetFramework = "net10.0"
        };

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(bundlePayload, JsonOptions);
        var bundle = new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = Id,
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.SourceCodeBundle,
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
    /// Extract translated C# from Pass 1 (DafnyVerification artifacts)
    /// and io-shell module specs from Architecture artifacts.
    /// </summary>
    private static (List<(string ModuleName, string CSharp)> Translated, List<Component> IoShells) ExtractInputs(PhaseContext context)
    {
        var translated = new List<(string, string)>();
        var ioShells = new List<Component>();

        foreach (var artifact in context.InputArtifacts)
        {
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);

                if (artifact.Kind == ArtifactKind.DafnyVerification)
                {
                    var results = JsonSerializer.Deserialize<DafnyVerificationResult[]>(json, JsonOptions);
                    if (results is not null)
                    {
                        foreach (var r in results)
                        {
                            if (r.IsVerified && !string.IsNullOrWhiteSpace(r.TranslatedCSharp))
                                translated.Add((r.ModuleName, r.TranslatedCSharp!));
                        }
                    }
                }
                else if (artifact.Kind == ArtifactKind.ArchitectureContract)
                {
                    var archContract = JsonSerializer.Deserialize<ArchitectureContract>(json, JsonOptions);
                    if (archContract?.Components is not null)
                    {
                        foreach (var c in archContract.Components)
                        {
                            if (c.Classification == ModuleClassification.IoShell)
                                ioShells.Add(c);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — failed to parse artifact: {ex.Message}");
            }
        }

        return (translated, ioShells);
    }

    private async Task<(List<SourceCodeFile>, int, int)> ImplementExternPortalsAsync(
        PhaseContext context, List<(string ModuleName, string CSharp)> translatedFiles, CancellationToken ct)
    {
        var files = new List<SourceCodeFile>();
        var totalInput = 0;
        var totalOutput = 0;

        foreach (var (moduleName, csharp) in translatedFiles)
        {
            var systemPrompt = BuildExternPrompt(moduleName, csharp);
            var prompt = context.Prompt with { SystemPrompt = systemPrompt };

            var generation = await _gateway.GenerateAsync(context.ModelRoute, prompt, context, ct);
            totalInput += generation.InputTokens;
            totalOutput += generation.OutputTokens;

            // Capture the prompt→response pair
            await PromptLogger.LogPromptAsync(
                context.SessionId.Value, Id.Value, context.AttemptNumber,
                moduleName, "generate",
                context.ModelRoute.ProviderId, context.ModelRoute.ModelId,
                systemPrompt, null,
                generation.Text,
                generation.InputTokens, generation.OutputTokens,
                generation.CostUsd, (long)generation.Latency.TotalMilliseconds,
                null, null, ct);

            var parsedFiles = ParseFileOutput(generation.Text, moduleName);
            files.AddRange(parsedFiles);

            Console.Error.WriteLine($"[Posit] C# Implementation — '{moduleName}': {parsedFiles.Count} C# files");
        }

        return (files, totalInput, totalOutput);
    }

    private async Task<(List<SourceCodeFile>, int, int)> ImplementIoShellsAsync(
        PhaseContext context, List<Component> ioShells, CancellationToken ct)
    {
        var files = new List<SourceCodeFile>();
        var totalInput = 0;
        var totalOutput = 0;

        foreach (var shell in ioShells)
        {
            var systemPrompt = BuildIoShellPrompt(shell);
            var prompt = context.Prompt with { SystemPrompt = systemPrompt };

            var generation = await _gateway.GenerateAsync(context.ModelRoute, prompt, context, ct);
            totalInput += generation.InputTokens;
            totalOutput += generation.OutputTokens;

            // Capture the prompt→response pair
            await PromptLogger.LogPromptAsync(
                context.SessionId.Value, Id.Value, context.AttemptNumber,
                shell.Name, "generate",
                context.ModelRoute.ProviderId, context.ModelRoute.ModelId,
                systemPrompt, null,
                generation.Text,
                generation.InputTokens, generation.OutputTokens,
                generation.CostUsd, (long)generation.Latency.TotalMilliseconds,
                null, null, ct);

            var parsedFiles = ParseFileOutput(generation.Text, shell.Name);
            files.AddRange(parsedFiles);

            Console.Error.WriteLine($"[Posit] C# Implementation — io-shell '{shell.Name}': {parsedFiles.Count} C# files");
        }

        return (files, totalInput, totalOutput);
    }

    private static string BuildExternPrompt(string moduleName, string translatedCSharp)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the C# Implementation phase (Pass 2). Fill in the extern portal holes in this translated Dafny C#.");
        sb.AppendLine("Write partial class implementations for each {:extern} method. Match the Dafny signature.");
        sb.AppendLine("Do NOT modify the translated Dafny code — write only the partial class implementations.");
        sb.AppendLine();
        sb.AppendLine($"--- MODULE: {moduleName} ---");
        sb.AppendLine("--- TRANSLATED C# (fill the extern holes) ---");
        sb.AppendLine(translatedCSharp);
        sb.AppendLine();
        sb.AppendLine("Respond with a JSON array of {path, content} file objects.");

        return sb.ToString();
    }

    private static string BuildIoShellPrompt(Component shell)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the C# Implementation phase (Pass 2). Write a complete C# class for this io-shell module.");
        sb.AppendLine("This module does I/O — file reading, database, HTTP, console output. No Dafny, no verification.");
        sb.AppendLine();
        sb.AppendLine($"Module: {shell.Name}");
        sb.AppendLine($"Responsibility: {shell.Responsibility}");
        sb.AppendLine($"Public Surface: {string.Join(", ", shell.PublicSurface)}");
        if (!string.IsNullOrWhiteSpace(shell.Internals))
            sb.AppendLine($"Internals: {shell.Internals}");
        if (shell.Dependencies.Length > 0)
            sb.AppendLine($"Dependencies: {string.Join(", ", shell.Dependencies)}");
        sb.AppendLine();
        sb.AppendLine("Respond with a JSON array of {path, content} file objects.");

        return sb.ToString();
    }

    /// <summary>
    /// Parse the model's JSON response into SourceCodeFile records.
    /// Handles files[] array format and single-file format.
    /// </summary>
    private static List<SourceCodeFile> ParseFileOutput(string text, string moduleName)
    {
        var files = new List<SourceCodeFile>();

        if (string.IsNullOrWhiteSpace(text))
            return files;

        try
        {
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            var json = OllamaModelGateway.ExtractJson(cleaned);

            // Try array of {path, content}
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    var path = element.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var content = element.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(content))
                        files.Add(new SourceCodeFile(path, content));
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Single file or files[] wrapper
                if (root.TryGetProperty("files", out var filesArr))
                {
                    foreach (var element in filesArr.EnumerateArray())
                    {
                        var path = element.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                        var content = element.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(content))
                            files.Add(new SourceCodeFile(path, content));
                    }
                }
                else
                {
                    var path = root.TryGetProperty("path", out var p) ? p.GetString() ?? "" : $"{moduleName}.cs";
                    var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(content))
                        files.Add(new SourceCodeFile(path, content));
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] C# Implementation — failed to parse file output: {ex.Message}");
        }

        return files;
    }

    private static ArtifactBundle CreateEmptyBundle(PhaseContext context)
    {
        var emptyBundle = new SourceCodeBundle
        {
            Files = [],
            ProjectPath = "src/Generated",
            TargetFramework = "net10.0"
        };
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(emptyBundle, JsonOptions);
        return new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = new PhaseId("csharp-implementation"),
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.SourceCodeBundle,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = []
        };
    }

    public Task<ValidationResult> ValidateOutputAsync(ArtifactBundle output, CancellationToken ct)
    {
        var errors = new List<string>();

        if (output.Kind != ArtifactKind.SourceCodeBundle)
            errors.Add("validation.schema_mismatch: Kind");
        if (output.SchemaVersion != "1.0.0")
            errors.Add("validation.schema_mismatch: SchemaVersion");

        try
        {
            var bundle = JsonSerializer.Deserialize<SourceCodeBundle>(output.PayloadJson, JsonOptions);
            if (bundle is null)
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