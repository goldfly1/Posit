using System.Text.Json;
using System.Text.Json.Serialization;
using Posit.AI.Models;
using Posit.Data.Repositories;
using Posit.Tools;

namespace Posit.Phases;

/// <summary>
/// Architecture phase — the architect decomposes the system, classifies modules
/// as dafny/io-shell/mixed, and writes .dfy skeletons with formal contracts.
///
/// This is where the Dafny sidewalk starts. The architect walks down pure logic
/// with requires/ensures, places {:extern} portals for I/O, and emits bodyless
/// declarations with {:axiom}. Z3 will verify these skeletons in the Dafny
/// Contracts phase immediately downstream.
///
/// Model: deepseek-v4-pro:cloud (better formal reasoning)
/// </summary>
public sealed class ArchitecturePhase : IPhase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new ModuleClassificationConverter() }
    };

    private readonly IModelGateway _gateway;

    public ArchitecturePhase(IModelGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public PhaseId Id => new("architecture");
    public PhaseName Name => new("Architecture");
    public PhaseId[] Dependencies => [new PhaseId("ideation")];

    public ArtifactSchema OutputSchema => new()
    {
        Kind = ArtifactKind.ArchitectureContract,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = typeof(ArchitectureContract).FullName!
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct)
    {
        // Load the prompt template from the prompt registry (or use embedded default)
        var systemPrompt = context.Prompt.SystemPrompt;
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            systemPrompt = LoadEmbeddedPrompt();
        }

        var prompt = context.Prompt with { SystemPrompt = systemPrompt };

        Console.Error.WriteLine($"[Posit] Architecture — calling model '{context.ModelRoute.ModelId}'...");

        var generation = await _gateway.GenerateAsync(context.ModelRoute, prompt, context, ct);

        // Capture the prompt→response pair (the data harvest)
        await PromptLogger.LogPromptAsync(
            context.SessionId.Value, Id.Value, context.AttemptNumber,
            null, "generate",
            context.ModelRoute.ProviderId, context.ModelRoute.ModelId,
            systemPrompt, null,
            generation.Text,
            generation.InputTokens, generation.OutputTokens,
            generation.CostUsd, (long)generation.Latency.TotalMilliseconds,
            null, null, ct);

        var (contract, parseError) = ParseArchitectureContract(generation.Text);

        if (contract is null || contract.Components.Length == 0)
        {
            Console.Error.WriteLine($"[Posit] Architecture — failed to parse architecture contract: {parseError}");
            return new PhaseResult
            {
                PhaseId = Id,
                Status = PhaseStatus.Failed,
                Artifacts = CreateErrorBundle(context, generation.Text),
                Costs = new CostSnapshot
                {
                    InputTokens = generation.InputTokens,
                    OutputTokens = generation.OutputTokens,
                    ModelTier = context.ModelRoute.Tier
                },
                AttemptNumber = context.AttemptNumber,
                Warnings = [$"architecture.parse_failed: {parseError}"],
                RawOutput = generation.Text
            };
        }

        // Count dafny vs io-shell vs mixed
        var dafnyCount = contract.Components.Count(c => c.Classification == ModuleClassification.Dafny);
        var ioShellCount = contract.Components.Count(c => c.Classification == ModuleClassification.IoShell);
        var mixedCount = contract.Components.Count(c => c.Classification == ModuleClassification.Mixed);

        // Write .dfy skeletons to staging. The model returns source as a JSON string;
        // we write it to disk and store the path. The file is the authority.
        var componentsWithPath = new List<Component>();
        foreach (var comp in contract.Components)
        {
            if ((comp.Classification is ModuleClassification.Dafny or ModuleClassification.Mixed)
                && !string.IsNullOrWhiteSpace(comp.DafnyContractPath))
            {
                // Model returned the source in DafnyContractPath (legacy field name from JSON)
                // Write it to staging and replace with the file path
                var dafnyPath = Z3Runner.GetDafnyStagingPath($"skeleton-{comp.Name}");
                await File.WriteAllTextAsync(dafnyPath, comp.DafnyContractPath!, ct);
                componentsWithPath.Add(comp with { DafnyContractPath = dafnyPath });
                Console.Error.WriteLine($"[Posit] Architecture — skeleton written: {dafnyPath}");
            }
            else
            {
                componentsWithPath.Add(comp);
            }
        }
        contract = contract with { Components = [.. componentsWithPath] };

        var withDafnySource = contract.Components.Count(c => !string.IsNullOrWhiteSpace(c.DafnyContractPath));

        Console.Error.WriteLine(
            $"[Posit] Architecture — {contract.Components.Length} components: " +
            $"{dafnyCount} dafny, {ioShellCount} io-shell, {mixedCount} mixed, " +
            $"{withDafnySource} with .dfy source");

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(contract, JsonOptions);
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

        var warnings = new List<string>();
        if (withDafnySource < dafnyCount + mixedCount)
            warnings.Add($"architecture.missing_dafny_source: {dafnyCount + mixedCount - withDafnySource} dafny/mixed components missing .dfy skeletons");

        return new PhaseResult
        {
            PhaseId = Id,
            Status = PhaseStatus.Success,
            Artifacts = bundle,
            Costs = new CostSnapshot
            {
                InputTokens = generation.InputTokens,
                OutputTokens = generation.OutputTokens,
                ModelTier = context.ModelRoute.Tier
            },
            AttemptNumber = context.AttemptNumber,
            Warnings = warnings.ToArray(),
            RawOutput = generation.Text
        };
    }

    /// <summary>
    /// Parse the model's JSON response into an ArchitectureContract.
    /// Handles reasoning tags, markdown fences, and snake_case normalization.
    /// </summary>
    private static (ArchitectureContract? contract, string? error) ParseArchitectureContract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "Empty response");

        try
        {
            // Strip reasoning tags and extract JSON
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            var json = OllamaModelGateway.ExtractJson(cleaned);

            // Normalize snake_case to camelCase for known fields
            json = json.Replace("\"system_context\"", "\"systemContext\"")
                       .Replace("\"public_surface\"", "\"publicSurface\"")
                       .Replace("\"deployment_topology\"", "\"deploymentTopology\"")
                       .Replace("\"quality_attributes\"", "\"qualityAttributes\"")
                       .Replace("\"open_risks\"", "\"openRisks\"")
                       .Replace("\"dafny_contract_source\"", "\"dafnyContractPath\"")
                       .Replace("\"dafny_contract_path\"", "\"dafnyContractPath\"")
                       .Replace("\"test_cases\"", "\"testCases\"")
                       .Replace("\"target_type\"", "\"targetType\"")
                       .Replace("\"expected_behavior\"", "\"expectedBehavior\"");

            var contract = JsonSerializer.Deserialize<ArchitectureContract>(json, JsonOptions);
            if (contract is null)
                return (null, "Failed to deserialize ArchitectureContract");

            if (contract.Components is { Length: 0 })
                return (null, "No components in architecture contract");

            return (contract, null);
        }
        catch (Exception ex)
        {
            return (null, $"JSON parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the architecture prompt from the prompts directory.
    /// Falls back to an inline minimal prompt if the file is not found.
    /// </summary>
    private static string LoadEmbeddedPrompt()
    {
        var promptPath = Path.Combine(AppContext.BaseDirectory, "prompts", "architecture", "1.0.0.md");
        if (File.Exists(promptPath))
            return File.ReadAllText(promptPath);

        // Fallback: search relative to the working directory
        promptPath = Path.Combine(Directory.GetCurrentDirectory(), "prompts", "architecture", "1.0.0.md");
        if (File.Exists(promptPath))
            return File.ReadAllText(promptPath);

        // Minimal inline prompt
        return """
            You are the architect of a Dafny-first spec compiler pipeline.
            Decompose the system into modules. Classify each as dafny, io-shell, or mixed.
            For dafny modules, write .dfy skeletons with requires/ensures (bodyless, {:axiom}).
            Use {:extern} for I/O portals. Predicates have bodies; methods don't.
            Respond with valid JSON matching the ArchitectureContract schema.
            """;
    }

    private static ArtifactBundle CreateErrorBundle(PhaseContext context, string rawOutput)
    {
        var emptyContract = new ArchitectureContract
        {
            SystemContext = "PARSE_ERROR",
            Components = [],
            DataStores = [],
            Interfaces = [],
            DeploymentTopology = "",
            QualityAttributes = [],
            Decisions = []
        };
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(emptyContract, JsonOptions);
        return new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = new PhaseId("architecture"),
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.ArchitectureContract,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = []
        };
    }

    public Task<ValidationResult> ValidateOutputAsync(ArtifactBundle output, CancellationToken ct)
    {
        var errors = new List<string>();

        if (output.Kind != ArtifactKind.ArchitectureContract)
            errors.Add("validation.schema_mismatch: Kind");
        if (output.SchemaVersion != "1.0.0")
            errors.Add("validation.schema_mismatch: SchemaVersion");

        try
        {
            var contract = JsonSerializer.Deserialize<ArchitectureContract>(output.PayloadJson, JsonOptions);
            if (contract is null)
                errors.Add("validation.missing_required_field: Payload");
            else if (contract.Components.Length == 0)
                errors.Add("validation.empty: Components");
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