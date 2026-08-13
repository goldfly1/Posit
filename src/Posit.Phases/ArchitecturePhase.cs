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
                // Pattern selection: if architect specified patternName, use it (override).
                // If patternName is empty, use semantic search on component description.
                // If semantic search fails (<0.7 similarity), fall back to keyword matching.
                string patternName;
                string[] stubNames;

                if (!string.IsNullOrWhiteSpace(comp.PatternName))
                {
                    // Architect specified a pattern — use it directly
                    patternName = comp.PatternName;
                    stubNames = comp.StubNames?.Length > 0
                        ? comp.StubNames
                        : PatternRegistry.Suggest(comp).StubNames;
                    Console.Error.WriteLine($"[Posit] Architecture — architect selected pattern '{patternName}' for {comp.Name}");
                }
                else
                {
                    // Architect left patternName empty — let the registry decide via semantic search
                    var suggestion = PatternRegistry.Suggest(comp);
                    patternName = suggestion.PatternName;
                    stubNames = comp.StubNames?.Length > 0
                        ? comp.StubNames
                        : suggestion.StubNames;

                    if (suggestion.SimilarityScore > 0.7f && suggestion.BestVariantDescription != null)
                    {
                        Console.Error.WriteLine($"[Posit] Architecture — semantic match for {comp.Name}: pattern={suggestion.PatternName} variant={suggestion.BestVariantDescription} (similarity={suggestion.SimilarityScore:F2})");
                    }
                    else
                    {
                        Console.Error.WriteLine($"[Posit] Architecture — keyword fallback for {comp.Name}: pattern={patternName} (semantic similarity={suggestion.SimilarityScore:F2})");
                    }
                }

                if (!_registry.HasPattern(patternName))
                {
                    Console.Error.WriteLine($"[Posit] Architecture — pattern '{patternName}' not in registry, falling back to suggest for {comp.Name}");
                    var fallback = PatternRegistry.Suggest(comp);
                    patternName = fallback.PatternName;
                    stubNames = fallback.StubNames;
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
            else if (comp.Classification == ModuleClassification.IoShell)
            {
                // io-shell components get a skeleton too — stubs wrapped in a module.
                // The carapace doctrine: every component has a contract, even I/O portals.
                var stubNames = comp.StubNames?.Length > 0
                    ? comp.StubNames
                    : PatternRegistry.Suggest(comp).StubNames;

                var dafnyPath = Z3Runner.GetDafnyStagingPath($"skeleton-{comp.Name}");
                var skeleton = _registry.ComposeIoShellSkeleton(comp.Name, stubNames);
                await File.WriteAllTextAsync(dafnyPath, skeleton, ct);

                _registry.MaterializeIoShellDependencies(stubNames, Path.GetDirectoryName(dafnyPath)!);
                componentsWithPath.Add(comp with
                {
                    StubNames = stubNames,
                    DafnyContractPath = dafnyPath
                });
                Console.Error.WriteLine($"[Posit] Architecture — composed io-shell skeleton for {comp.Name}: stubs=[{string.Join(",", stubNames)}] => {dafnyPath}");
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
                       .Replace("\"expected_behavior\"", "\"expectedBehavior\"")
                       .Replace("\"method_signatures\"", "\"methodSignatures\"")
                       .Replace("\"pattern_method\"", "\"patternMethod\"")
                       .Replace("\"return_dafny_type\"", "\"returnDafnyType\"")
                       .Replace("\"dafny_type\"", "\"dafnyType\"")
                       .Replace("\"arg_mappings\"", "\"argMappings\"")
                       .Replace("\"return_type\"", "\"returnType\"")
                       .Replace("\"return_usage\"", "\"returnUsage\"")
                       .Replace("\"from_method\"", "\"fromMethod\"")
                       .Replace("\"to_component\"", "\"toComponent\"")
                       .Replace("\"to_method\"", "\"toMethod\"")
                       .Replace("\"shared_types\"", "\"sharedTypes\"")
                       .Replace("\"defined_in_module\"", "\"definedInModule\"")
                       .Replace("\"connections\"", "\"connections\"");

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

    private void ValidateContract(ArchitectureContract contract, List<string> errors)
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

            if (c.Dependencies is not null && c.Dependencies.Length > 0)
            {
                foreach (var dep in c.Dependencies)
                {
                    if (!componentByName.ContainsKey(dep) &&
                        !contract.Components.Any(x => string.Equals(x.Name, dep, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"validation.unresolved.dependency: {prefix} '{c.Name}' depends on unknown '{dep}'");
                    }
                }

                // Carapace enforcement: connector forms are REQUIRED for any component
                // with dependencies. No connector specs → reject → send back to the model.
                // The orchestrator cannot wire without these. Cotton candy prevention.
                //
                // BUT: not all dependencies are method calls. Some are type-only dependencies
                // (e.g., a Contracts module that defines datatypes used via Dafny `include`).
                // Type-only dependencies need sharedTypes, not connections.
                // A dependency is "type-only" if its publicSurface contains no method-like names
                // (i.e., all entries start with uppercase and look like type names, not verbs).
                
                // Classify dependencies: method-call deps vs type-only deps
                var methodCallDeps = new List<string>();
                var typeOnlyDeps = new List<string>();
                foreach (var dep in c.Dependencies)
                {
                    if (componentByName.TryGetValue(dep, out var depComp))
                    {
                        // A type-only dependency has no methodSignatures (or empty)
                        // and its publicSurface looks like type names (not method calls)
                        var hasMethods = depComp.MethodSignatures?.Length > 0 ||
                            (depComp.PublicSurface?.Any(s => !string.IsNullOrEmpty(s) && 
                                char.IsLower(s[0]) || s.Contains("()")) == true);
                        
                        // If the dependency has no publicSurface at all (pure types module),
                        // or its surface entries all look like type names, it's type-only
                        if (!hasMethods || depComp.PublicSurface is null or { Length: 0 })
                        {
                            typeOnlyDeps.Add(dep);
                        }
                        else
                        {
                            // Check if ALL public surface entries look like types (PascalCase, no parens)
                            var allTypes = depComp.PublicSurface!.All(s => 
                                !string.IsNullOrEmpty(s) && 
                                char.IsUpper(s[0]) && 
                                !s.Contains("()") &&
                                !s.EndsWith("Async", StringComparison.OrdinalIgnoreCase));
                            
                            if (allTypes && (depComp.MethodSignatures is null || depComp.MethodSignatures.Length == 0))
                                typeOnlyDeps.Add(dep);
                            else
                                methodCallDeps.Add(dep);
                        }
                    }
                    else
                    {
                        methodCallDeps.Add(dep); // unknown dep — treat as method-call to get an error
                    }
                }

                // methodSignatures required if the component has ANY dependencies
                if (c.MethodSignatures is null || c.MethodSignatures.Length == 0)
                    errors.Add($"validation.missing.methodSignatures: {prefix} '{c.Name}' has dependencies but no methodSignatures — the orchestrator needs these to wire deterministically");

                // Connections required only for method-call dependencies
                if (methodCallDeps.Count > 0 && (c.Connections is null || c.Connections.Length == 0))
                    errors.Add($"validation.missing.connections: {prefix} '{c.Name}' has method-call dependencies {string.Join(", ", methodCallDeps)} but no connections — specify how this component calls each");

                // Validate that every method-call dependency has a connection spec
                if (c.Connections is not null && methodCallDeps.Count > 0)
                {
                    var connectedDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var conn in c.Connections)
                    {
                        if (!string.IsNullOrWhiteSpace(conn.ToComponent))
                            connectedDeps.Add(conn.ToComponent);

                        // Validate connection references resolve
                        if (!string.IsNullOrWhiteSpace(conn.ToComponent) &&
                            !componentByName.ContainsKey(conn.ToComponent) &&
                            !contract.Components.Any(x => string.Equals(x.Name, conn.ToComponent, StringComparison.OrdinalIgnoreCase)))
                        {
                            errors.Add($"validation.unresolved.connection: {prefix} '{c.Name}' connection references unknown component '{conn.ToComponent}'");
                        }

                        // Validate fromMethod matches a methodSignature name
                        // For io-shell components, also accept publicSurface entries (e.g., "Program" entry point)
                        if (!string.IsNullOrWhiteSpace(conn.FromMethod))
                        {
                            var fromMethodMatched = false;
                            if (c.MethodSignatures is not null)
                                fromMethodMatched = c.MethodSignatures.Any(ms => string.Equals(ms.Name, conn.FromMethod, StringComparison.OrdinalIgnoreCase));
                            if (!fromMethodMatched && c.Classification == ModuleClassification.IoShell && c.PublicSurface is not null)
                                fromMethodMatched = c.PublicSurface.Any(s => string.Equals(s, conn.FromMethod, StringComparison.OrdinalIgnoreCase));
                            if (!fromMethodMatched)
                                errors.Add($"validation.mismatch.connection_fromMethod: {prefix} '{c.Name}' connection fromMethod '{conn.FromMethod}' does not match any methodSignature name");
                        }

                        // Carapace enforcement: toMethod must exist on the target component's pattern.
                        // The architect invents method names (e.g., "Parse") but the pattern provides
                        // real methods (e.g., "ParseLine"). Reject if toMethod doesn't match any
                        // real pattern method on the target component.
                        if (!string.IsNullOrWhiteSpace(conn.ToMethod) &&
                            !string.IsNullOrWhiteSpace(conn.ToComponent) &&
                            componentByName.TryGetValue(conn.ToComponent, out var targetComp) &&
                            !string.IsNullOrWhiteSpace(targetComp.PatternName))
                        {
                            var patternSigs = _registry.GetPatternSignatures(targetComp.PatternName);
                            if (patternSigs.Count > 0)
                            {
                                // Check toMethod against both the pattern's real method names
                                // and the target component's MethodSignatures (which may include PatternMethod mappings)
                                var patternMethodNames = patternSigs.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                                var targetSigNames = targetComp.MethodSignatures?
                                    .Select(s => s.PatternMethod ?? s.Name)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                                // The toMethod should match either a real pattern method or a declared method signature
                                if (!patternMethodNames.Contains(conn.ToMethod) &&
                                    !targetSigNames.Contains(conn.ToMethod) &&
                                    !targetComp.MethodSignatures?.Any(ms => string.Equals(ms.Name, conn.ToMethod, StringComparison.OrdinalIgnoreCase)) == true)
                                {
                                    var realMethods = string.Join(", ", patternMethodNames);
                                    errors.Add($"validation.mismatch.connection_toMethod: {prefix} '{c.Name}' connection toMethod '{conn.ToMethod}' does not exist on target '{conn.ToComponent}' (pattern '{targetComp.PatternName}'). Real methods: {realMethods}");
                                }
                            }
                        }
                    }

                    foreach (var dep in methodCallDeps)
                    {
                        if (!connectedDeps.Contains(dep))
                            errors.Add($"validation.missing.connection_for_dependency: {prefix} '{c.Name}' depends on '{dep}' (method-call) but no connection spec targets it");
                    }
                }

                // sharedTypes required if the component has type-only dafny dependencies
                if (typeOnlyDeps.Count > 0 && (c.SharedTypes is null || c.SharedTypes.Length == 0))
                {
                    errors.Add($"validation.missing.sharedTypes: {prefix} '{c.Name}' has type-only dependencies {string.Join(", ", typeOnlyDeps)} but no sharedTypes — list types shared via Dafny include");
                }
            }
        }

        // Build dependency graph and detect cycles.
        // Auto-repair: io-shell components are LEAF I/O providers. They should never
        // depend on business-logic (dafny) components. The model sometimes creates
        // cycles by having io-shell ↔ CLI components depend on each other. Strip
        // io-shell → non-io-shell dependencies before cycle detection.
        var ioShellNames = new HashSet<string>(
            contract.Components
                .Where(c => c.Classification == ModuleClassification.IoShell)
                .Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in contract.Components)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            if (graph.ContainsKey(c.Name)) continue; // duplicate name already reported above
            var deps = (c.Dependencies ?? [])
                .Where(d => componentByName.ContainsKey(d))
                .ToList();

            // Auto-repair: if this is an io-shell component, strip dependencies
            // on non-io-shell components. Io-shell = leaf, no back-edges to logic.
            if (ioShellNames.Contains(c.Name))
            {
                var stripped = deps.Where(d => ioShellNames.Contains(d)).ToList();
                if (stripped.Count < deps.Count)
                {
                    var removed = deps.Except(stripped).ToList();
                    Console.Error.WriteLine($"[Posit] Architecture — auto-repair: stripped io-shell '{c.Name}' dependencies on non-io-shell components: {string.Join(", ", removed)}");
                    deps = stripped;
                }
            }

            graph[c.Name] = deps;
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