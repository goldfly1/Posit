using System.Text;
using System.Text.Json;
using Posit.AI.Models;
using Posit.Data.Repositories;
using Posit.Tools;
using Posit.Contracts.Serialization;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Phases;

/// <summary>
/// Dafny Implementation — Pass 1. Imp fills in Dafny method bodies from
/// verified skeletons, Z3 verifies the complete program, and on success
/// `dafny translate cs` produces C# with partial class extern holes.
///
/// The exoskeleton (requires/ensures/predicates/externs) is fixed —
/// Imp writes only the bodies. If Z3 fails, the correction signal goes
/// back to Imp with the exact proof error (retry within phase, not
/// loopback to Architecture — that's for skeleton failures).
///
/// Model: deepseek-v4-pro:cloud
/// </summary>
public sealed class DafnyImplementationPhase : IPhase
{
    private static readonly JsonSerializerOptions JsonOptions = Options;

    private readonly IModelGateway _gateway;
    private readonly Z3Runner _z3Runner;
    private const int MaxRetries = 2;

    public DafnyImplementationPhase(IModelGateway gateway, Z3Runner z3Runner)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _z3Runner = z3Runner ?? throw new ArgumentNullException(nameof(z3Runner));
    }

    public PhaseId Id => new("dafny-implementation");
    public PhaseName Name => new("Dafny Implementation (Pass 1)");
    public PhaseId[] Dependencies => [new PhaseId("dafny-contracts")];

    public ArtifactSchema OutputSchema => new()
    {
        Kind = ArtifactKind.DafnyVerification,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = typeof(DafnyVerificationResult).FullName!
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct)
    {
        // Extract verified skeletons from Dafny Contracts phase output
        var skeletons = ExtractSkeletons(context);

        if (skeletons.Count == 0)
        {
            Console.Error.WriteLine("[Posit] Dafny Implementation — no verified skeletons found");
            return new PhaseResult
            {
                PhaseId = Id,
                Status = PhaseStatus.Success,
                Artifacts = CreateEmptyBundle(context),
                Costs = CostSnapshot.Zero,
                AttemptNumber = context.AttemptNumber
            };
        }

        var results = new List<DafnyVerificationResult>();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var anyFailed = false;

        foreach (var (moduleName, skeletonSource) in skeletons)
        {
            Console.Error.WriteLine($"[Posit] Dafny Implementation — filling bodies for '{moduleName}'...");

            var (result, inputTokens, outputTokens, verified) =
                await ImplementModuleAsync(context, moduleName, skeletonSource, ct);

            results.Add(result);
            totalInputTokens += inputTokens;
            totalOutputTokens += outputTokens;
            if (!verified)
                anyFailed = true;

            Console.Error.WriteLine(
                $"[Posit] Dafny Implementation — module '{moduleName}' verified={verified}");
        }

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(results, JsonOptions);
        var bundle = new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = Id,
            SchemaVersion = OutputSchema.SchemaVersion,
            Kind = OutputSchema.Kind,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = context.InputArtifacts
                .Select(a => new ArtifactReference(a.Id, a.Kind, a.SchemaVersion))
                .ToArray()
        };

        return new PhaseResult
        {
            PhaseId = Id,
            Status = anyFailed ? PhaseStatus.Failed : PhaseStatus.Success,
            Artifacts = bundle,
            Costs = new CostSnapshot
            {
                InputTokens = totalInputTokens,
                OutputTokens = totalOutputTokens,
                ModelTier = context.ModelRoute.Tier
            },
            AttemptNumber = context.AttemptNumber,
            Warnings = anyFailed
                ? [$"dafny.verification_failed: {results.Count(r => r.IsVerified)}/{results.Count} modules verified. See DafnyVerification artifact for errors."]
                : []
        };
    }

    /// <summary>
    /// Implement a single module: call model to fill bodies, Z3 verify, translate.
    /// Retries up to MaxRetries times on Z3 failure with correction signal.
    /// </summary>
    private async Task<(DafnyVerificationResult, int, int, bool)> ImplementModuleAsync(
        PhaseContext context, string moduleName, string skeletonPath, CancellationToken ct)
    {
        // Read the skeleton from disk — the file is the authority
        var skeletonSource = await File.ReadAllTextAsync(skeletonPath, ct);

        var systemPrompt = BuildPrompt(context, moduleName, skeletonPath, skeletonSource);
        var prompt = context.Prompt with { SystemPrompt = systemPrompt };

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var generation = await _gateway.GenerateAsync(context.ModelRoute, prompt, context, ct);
            var (result, parseError) = ParseDafnyResult(generation.Text, moduleName);

            // Capture the prompt→response pair
            await PromptLogger.LogPromptAsync(
                context.SessionId.Value, Id.Value, context.AttemptNumber,
                moduleName, attempt == 0 ? "generate" : "retry",
                context.ModelRoute.ProviderId, context.ModelRoute.ModelId,
                systemPrompt, null,
                generation.Text,
                generation.InputTokens, generation.OutputTokens,
                generation.CostUsd, (long)generation.Latency.TotalMilliseconds,
                result?.IsVerified == true ? "success" : "fallback",
                parseError, ct);

            if (result is null || string.IsNullOrWhiteSpace(result.DafnySource))
            {
                Console.Error.WriteLine(
                    $"[Posit] Dafny Implementation — '{moduleName}' attempt {attempt + 1}: failed to parse Dafny source");

                if (attempt == MaxRetries)
                    return (new DafnyVerificationResult
                    {
                        ModuleName = moduleName,
                        DafnySource = "",
                        ContractSummary = "Failed to generate valid Dafny source",
                        IsVerified = false,
                        VerificationOutput = parseError
                    }, generation.InputTokens, generation.OutputTokens, false);
                continue;
            }

            // Write completed Dafny source back to the SAME file — the carapace is updated in place
            var dafnyPath = string.IsNullOrWhiteSpace(result.DafnyPath)
                ? skeletonPath  // write back to the skeleton's path
                : result.DafnyPath;
            await File.WriteAllTextAsync(dafnyPath, result.DafnySource, ct);

            var (verified, output) = await _z3Runner.VerifyAsync(dafnyPath, ct);

            if (verified)
            {
                // Translate to C# on success — output goes to a file on disk
                var csharpPath = await _z3Runner.TranslateToCSharpAsync(dafnyPath, moduleName, ct);
                var finalResult = result with
                {
                    IsVerified = true,
                    VerificationOutput = output,
                    DafnyPath = dafnyPath,
                    TranslatedCSharpPath = csharpPath
                };
                Console.Error.WriteLine($"[Posit] Dafny Implementation — '{moduleName}' VERIFIED ✓");
                return (finalResult, generation.InputTokens, generation.OutputTokens, true);
            }

            Console.Error.WriteLine(
                $"[Posit] Dafny Implementation — '{moduleName}' attempt {attempt + 1}: verification FAILED");
            Console.Error.WriteLine($"  {output[..Math.Min(300, output.Length)]}");

            if (attempt < MaxRetries)
            {
                // Build correction prompt with Z3 error
                systemPrompt = BuildCorrectionPrompt(moduleName, result.DafnySource, output);
                prompt = context.Prompt with { SystemPrompt = systemPrompt };
            }
            else
            {
                var failedResult = result with
                {
                    IsVerified = false,
                    VerificationOutput = output
                };
                return (failedResult, generation.InputTokens, generation.OutputTokens, false);
            }
        }

        return (new DafnyVerificationResult
        {
            ModuleName = moduleName,
            DafnySource = "",
            ContractSummary = "Exhausted retries",
            IsVerified = false
        }, 0, 0, false);
    }

    /// <summary>
    /// Extract verified skeletons from Dafny Contracts phase artifacts.
    /// Only verified skeletons are passed to Implementation — failed skeletons
    /// should have been downgraded to io-shell by the correction loopback.
    /// </summary>
    private static List<(string ModuleName, string DafnyPath)> ExtractSkeletons(PhaseContext context)
    {
        var skeletons = new List<(string, string)>();

        foreach (var artifact in context.InputArtifacts)
        {
            if (artifact.Kind != ArtifactKind.DafnyContract)
                continue;

            try
            {
                var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);
                var contractResults = JsonSerializer.Deserialize<DafnyContractResult[]>(json, JsonOptions);
                if (contractResults is null)
                    continue;

                foreach (var cr in contractResults)
                {
                    if (cr.IsVerified && !string.IsNullOrWhiteSpace(cr.DafnyPath) && File.Exists(cr.DafnyPath))
                        skeletons.Add((cr.ModuleName, cr.DafnyPath));
                    else if (cr.IsVerified && !string.IsNullOrWhiteSpace(cr.DafnySource))
                        Console.Error.WriteLine($"[Posit] Dafny Implementation — warning: contract result for '{cr.ModuleName}' has source but no path; skipping");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Posit] Dafny Implementation — failed to parse Dafny Contracts artifact: {ex.Message}");
            }
        }

        // Also check DesignContext for skeleton paths (snowballed)
        if (context.DesignContext?.DafnyContracts is { Length: > 0 } dafnyContracts)
        {
            foreach (var dc in dafnyContracts)
            {
                if (dc.IsVerified && !string.IsNullOrWhiteSpace(dc.DafnyPath) && File.Exists(dc.DafnyPath))
                {
                    if (!skeletons.Any(s => s.Item1 == dc.ModuleName))
                        skeletons.Add((dc.ModuleName, dc.DafnyPath));
                }
            }
        }

        return skeletons;
    }

    private static string BuildPrompt(PhaseContext context, string moduleName, string skeletonPath, string skeletonSource)
    {
        var sb = new StringBuilder();

        // Load prompt template
        var promptPath = Path.Combine(AppContext.BaseDirectory, "prompts", "dafny", "1.0.0.md");
        if (!File.Exists(promptPath))
            promptPath = Path.Combine(Directory.GetCurrentDirectory(), "prompts", "dafny", "1.0.0.md");

        if (File.Exists(promptPath))
            sb.AppendLine(File.ReadAllText(promptPath));
        else
            sb.AppendLine("You are the Dafny Implementation phase. Fill in method bodies in the Dafny skeleton. Do NOT modify contracts. Respond with JSON: {moduleName, dafnySource, verifiedTypes, contractSummary}.");

        sb.AppendLine();
        sb.AppendLine($"--- MODULE: {moduleName} ---");
        sb.AppendLine($"The skeleton file is at: {skeletonPath}");
        sb.AppendLine("Fill in the method and constructor bodies in this Dafny skeleton.");
        sb.AppendLine("Do NOT modify requires, ensures, predicates, {:extern} methods, or datatype declarations.");
        sb.AppendLine("Return the COMPLETE .dfy file with bodies filled in. The names, types, and contracts are the authority — do not change them.");
        sb.AppendLine();
        sb.AppendLine("--- SKELETON (read this — names and contracts are tattooed on the carapace) ---");
        sb.AppendLine(skeletonSource);

        return sb.ToString();
    }

    private static string BuildCorrectionPrompt(string moduleName, string dafnySource, string errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the Dafny Implementation phase. The previous Dafny source FAILED verification.");
        sb.AppendLine("Fix the method bodies to resolve the verification errors. Do NOT modify the contracts.");
        sb.AppendLine();
        sb.AppendLine($"--- MODULE: {moduleName} ---");
        sb.AppendLine();
        sb.AppendLine("--- PREVIOUS DAFNY SOURCE (fix the bodies, keep the contracts) ---");
        sb.AppendLine(dafnySource);
        sb.AppendLine();
        sb.AppendLine("--- VERIFICATION ERRORS (fix these) ---");
        var errorText = errors.Length > 4000 ? errors[..4000] + "\n... (truncated)" : errors;
        sb.AppendLine(errorText);
        sb.AppendLine();
        sb.AppendLine("Common fixes:");
        sb.AppendLine("- Add stronger invariant clauses to while loops");
        sb.AppendLine("- Add decreases clauses for recursion");
        sb.AppendLine("- Add assert statements to guide Z3");
        sb.AppendLine("- When using `forall` statements to prove a postcondition, add an `ensures` clause to the forall so the conclusion is visible outside");
        sb.AppendLine("- Simplify the implementation if too complex to verify");
        sb.AppendLine("- Use {:termination false} if termination checking fails");
        sb.AppendLine();
        sb.AppendLine("Return the COMPLETE fixed .dfy file as a JSON object.");

        return sb.ToString();
    }

    private static (DafnyVerificationResult?, string? parseError) ParseDafnyResult(string text, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "Empty response");

        try
        {
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            var json = OllamaModelGateway.ExtractJson(cleaned);

            // Normalize snake_case
            json = json.Replace("\"dafny_source\"", "\"dafnySource\"")
                       .Replace("\"contract_summary\"", "\"contractSummary\"")
                       .Replace("\"verified_types\"", "\"verifiedTypes\"");

            var result = JsonSerializer.Deserialize<DafnyVerificationResult>(json, JsonOptions);
            if (result is not null && !string.IsNullOrWhiteSpace(result.DafnySource))
            {
                if (string.IsNullOrWhiteSpace(result.ModuleName))
                    result = result with { ModuleName = moduleName };
                return (result, null);
            }

            // Fallback: files[] format
            if (json.Contains("\"files\""))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                {
                    var firstFile = files[0];
                    var content = firstFile.TryGetProperty("content", out var c) ? c.GetString() : "";
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return (new DafnyVerificationResult
                        {
                            ModuleName = moduleName,
                            DafnySource = content,
                            ContractSummary = "Extracted from files[] format"
                        }, null);
                    }
                }
            }

            if (result is not null)
            {
                if (string.IsNullOrWhiteSpace(result.ModuleName))
                    result = result with { ModuleName = moduleName };
                return (result, null);
            }

            return (null, "Failed to deserialize DafnyVerificationResult");
        }
        catch (Exception ex)
        {
            return (null, $"JSON parse error: {ex.Message}");
        }
    }

    private static ArtifactBundle CreateEmptyBundle(PhaseContext context)
    {
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(
            Array.Empty<DafnyVerificationResult>(), JsonOptions);
        return new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = new PhaseId("dafny-implementation"),
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.DafnyVerification,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = []
        };
    }

    public Task<ValidationResult> ValidateOutputAsync(ArtifactBundle output, CancellationToken ct)
    {
        var errors = new List<string>();

        if (output.Kind != ArtifactKind.DafnyVerification)
            errors.Add("validation.schema_mismatch: Kind");
        if (output.SchemaVersion != "1.0.0")
            errors.Add("validation.schema_mismatch: SchemaVersion");

        try
        {
            var results = JsonSerializer.Deserialize<DafnyVerificationResult[]>(output.PayloadJson, JsonOptions);
            if (results is null)
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