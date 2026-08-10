using System.Text.Json;
using Posit.AI.Models;
using Posit.Data.Repositories;
using Posit.Tools;
using Posit.Contracts.Serialization;
using static Posit.Contracts.Serialization.PositJson;

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
    private static readonly JsonSerializerOptions JsonOptions = Options;

    private readonly IModelGateway _gateway;
    private readonly PatternRegistry _registry;

    public ArchitecturePhase(IModelGateway gateway, PatternRegistry? registry = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _registry = registry ?? new PatternRegistry(GetDefaultPatternsDirectory());
    }

    private static string GetDefaultPatternsDirectory()
    {
        var candidate = Path.Combine(Directory.GetCurrentDirectory(), "patterns");
        if (Directory.Exists(candidate))
            return candidate;

        candidate = Path.Combine(AppContext.BaseDirectory, "patterns");
        if (Directory.Exists(candidate))
            return candidate;

        var assemblyLoc = typeof(ArchitecturePhase).Assembly.Location;
        if (!string.IsNullOrEmpty(assemblyLoc))
        {
            var srcRoot = Directory.GetParent(assemblyLoc);
            while (srcRoot is not null)
            {
                var test = Path.Combine(srcRoot.FullName, "patterns");
                if (Directory.Exists(test))
                    return test;
                srcRoot = srcRoot.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the Posit pattern registry (patterns/ directory). " +
            "It should be at the project root, next to Posit.sln.");
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

        // Compose .dfy skeletons from the object registry for dafny/mixed components.
        // The model returns patternName + stubNames; the pipeline composes the file.
        // This keeps the carapace consistent: every module comes from the quarry.
        var componentsWithPath = new List<Component>();
        foreach (var comp in contract.Components)
        {
            if (comp.Classification is ModuleClassification.Dafny or ModuleClassification.Mixed)
            {
                var patternName = !string.IsNullOrWhiteSpace(comp.PatternName)
                    ? comp.PatternName
                    : PatternRegistry.Suggest(comp).PatternName;

                var stubNames = comp.StubNames?.Length > 0
                    ? comp.StubNames
                    : PatternRegistry.Suggest(comp).StubNames;

                if (!_registry.HasPattern(patternName))
                {
                    Console.Error.WriteLine($"[Posit] Architecture — pattern '{patternName}' not in registry, falling back to suggest for {comp.Name}");
                    var suggestion = PatternRegistry.Suggest(comp);
                    patternName = suggestion.PatternName;
                    stubNames = suggestion.StubNames;
                }

                var dafnyPath = Z3Runner.GetDafnyStagingPath($"skeleton-{comp.Name}");
                var skeleton = _registry.ComposeSkeleton(comp.Name, patternName, stubNames);
                await File.WriteAllTextAsync(dafnyPath, skeleton, ct);

                // Carapace enforcement: check 200-line, 10-method, 5-class caps on composed skeleton
                var skeletonLines = skeleton.Split('\n').Length;
                var skeletonMethods = System.Text.RegularExpressions.Regex.Matches(skeleton, @"^\s*(method|function|lemma)\s", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
                var skeletonClasses = System.Text.RegularExpressions.Regex.Matches(skeleton, @"^\s*class\s", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
                if (skeletonLines > 200 || skeletonMethods > 10 || skeletonClasses > 5)
                {
                    Console.Error.WriteLine($"[Posit] Architecture — CARAPACE WARNING: {comp.Name} skeleton exceeds caps (lines={skeletonLines}, methods={skeletonMethods}, classes={skeletonClasses}). Pattern may need decomposition.");
                }

                _registry.MaterializeDependencies(comp.Name, patternName, stubNames, Path.GetDirectoryName(dafnyPath)!);
                componentsWithPath.Add(comp with
                {
                    PatternName = patternName,
                    StubNames = stubNames,
                    DafnyContractPath = dafnyPath
                });
                Console.Error.WriteLine($"[Posit] Architecture — composed skeleton for {comp.Name}: {patternName} + [{string.Join(",", stubNames)}] => {dafnyPath}");
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

        ArchitectureContract? contract = null;
        try
        {
            contract = JsonSerializer.Deserialize<ArchitectureContract>(output.PayloadJson, JsonOptions);
            if (contract is null)
                errors.Add("validation.missing_required_field: Payload");
        }
        catch (JsonException ex)
        {
            errors.Add($"validation.schema_mismatch: {ex.Message}");
        }

        if (contract is not null)
        {
            ValidateContract(contract, errors);
        }

        return Task.FromResult(new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray()
        });
    }

    private static void ValidateContract(ArchitectureContract contract, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(contract.SystemContext))
            errors.Add("validation.empty.systemContext: systemContext is required");

        if (contract.Components is null || contract.Components.Length == 0)
        {
            errors.Add("validation.empty.components: at least one component required");
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentByName = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        var noiseSuffixes = new[] { ".Implementation", ".Implementations", ".extern", "Implementation", "Implementations", "Extern" };

        for (int i = 0; i < contract.Components.Length; i++)
        {
            var c = contract.Components[i];
            var prefix = $"component[{i}]";

            if (string.IsNullOrWhiteSpace(c.Id))
                errors.Add($"validation.empty.component_field: {prefix}.id");
            if (string.IsNullOrWhiteSpace(c.Name))
                errors.Add($"validation.empty.component_field: {prefix}.name");
            if (string.IsNullOrWhiteSpace(c.Responsibility))
                errors.Add($"validation.empty.component_field: {prefix}.responsibility");

            if (!string.IsNullOrWhiteSpace(c.Name))
            {
                if (!names.Add(c.Name))
                    errors.Add($"validation.duplicate.componentName: '{c.Name}'");

                foreach (var suffix in noiseSuffixes)
                {
                    if (c.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"validation.noisy.componentName: '{c.Name}' ends with '{suffix}'");
                }

                componentByName[c.Name] = c;
            }

            if (c.TestCases is null || c.TestCases.Length == 0)
                errors.Add($"validation.empty.testCases: {prefix} '{c.Name}' must have at least one test case");

            if (c.Classification != ModuleClassification.Dafny &&
                c.Classification != ModuleClassification.IoShell &&
                c.Classification != ModuleClassification.Mixed)
            {
                errors.Add($"validation.invalid.classification: {prefix} '{c.Name}' = {c.Classification}");
            }

            if (c.Classification is ModuleClassification.Dafny or ModuleClassification.Mixed)
            {
                if (string.IsNullOrWhiteSpace(c.PatternName))
                    errors.Add($"validation.missing.patternName: {prefix} '{c.Name}' (dafny/mixed requires patternName)");
            }

            if (c.Classification == ModuleClassification.IoShell)
            {
                if (string.IsNullOrWhiteSpace(c.Tech))
                    errors.Add($"validation.empty.tech: {prefix} '{c.Name}' (io-shell requires tech)");

                var techLower = c.Tech?.ToLowerInvariant() ?? "";
                var isWeb = techLower.Contains("aspnet") || techLower.Contains("asp.net") || techLower.Contains("web") || techLower.Contains("http");
                if (isWeb && (c.PublicSurface is null || !c.PublicSurface.Any(s => s.Contains("Program", StringComparison.OrdinalIgnoreCase))))
                    errors.Add($"validation.missing.entryPoint: {prefix} '{c.Name}' Web/API io-shell component must list 'Program' in publicSurface");
            }

            if (c.Dependencies is not null)
            {
                foreach (var dep in c.Dependencies)
                {
                    if (!componentByName.ContainsKey(dep) &&
                        !contract.Components.Any(x => string.Equals(x.Name, dep, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"validation.unresolved.dependency: {prefix} '{c.Name}' depends on unknown '{dep}'");
                    }
                }
            }
        }

        // Build dependency graph and detect cycles.
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in contract.Components)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            if (graph.ContainsKey(c.Name)) continue; // duplicate name already reported above
            graph[c.Name] = (c.Dependencies ?? [])
                .Where(d => componentByName.ContainsKey(d))
                .ToList();
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Dfs(string node)
        {
            if (visited.Contains(node)) return true;
            if (!visiting.Add(node)) return false; // cycle

            if (graph.TryGetValue(node, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (!Dfs(dep))
                    {
                        errors.Add($"validation.cyclic.dependencies: cycle involving '{node}' -> '{dep}'");
                        return false;
                    }
                }
            }

            visiting.Remove(node);
            visited.Add(node);
            return true;
        }

        foreach (var name in graph.Keys)
        {
            Dfs(name);
        }

        if (contract.DataStores is null)
            errors.Add("validation.null.section: dataStores");
        if (contract.Interfaces is null)
            errors.Add("validation.null.section: interfaces");
        if (contract.QualityAttributes is null)
            errors.Add("validation.null.section: qualityAttributes");
        if (contract.Decisions is null)
            errors.Add("validation.null.section: decisions");
    }
}