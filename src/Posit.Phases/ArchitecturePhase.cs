namespace Posit.Phases;

/// <summary>
/// Phase 1: Architecture. The architect decomposes the spec, classifies
/// components, writes C# interfaces, and defines test cases.
/// After model output, write C# interface files to staging, then
/// run ContractScanner validation. If scan fails, return Failed with correction
/// listing so the FSM retries.
/// </summary>
public sealed class ArchitecturePhase : IPhase
{
    private readonly IModelGateway _model;
    private readonly PatternRegistry _registry;
    private readonly WikiSearcher _wiki;

    public ArchitecturePhase(IModelGateway model, PatternRegistry registry)
    {
        _model = model;
        _registry = registry;
        _wiki = new WikiSearcher(new HttpClient());
    }

    public PhaseId Id { get; } = new("architecture");
    public string Name => "Architecture";
    public PhaseId[] Dependencies { get; } = [];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.ArchitectureContract,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(ArchitectureContract)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        // Pre-generation wiki search: find relevant C# pattern examples
        var wikiExamples = "";
        var searchQuery = context.UserRequest ?? "";
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            wikiExamples = await _wiki.SearchAsync(searchQuery, limit: 3, ct);
            if (!string.IsNullOrWhiteSpace(wikiExamples))
                Console.Error.WriteLine("[architecture] pre-generation wiki search returned examples");
        }

        // Inject wiki examples into the system prompt
        var prompt = context.Prompt;
        if (!string.IsNullOrWhiteSpace(wikiExamples))
        {
            prompt = prompt with { SystemPrompt = prompt.SystemPrompt + "\n" + wikiExamples };
        }

        var result = await _model.GenerateAsync(
            context.ModelRoute, prompt, context, ct);

        if (string.IsNullOrWhiteSpace(result.Text))
            return Fail(context, "Model returned empty output", result);

        var contract = ParseContract(result.Text);
        if (contract == null)
            return Fail(context, "Failed to parse ArchitectureContract from model output", result);

        // Write C# interface files to staging for logic components
        var composeErrors = WriteInterfaces(contract, context);
        if (composeErrors.Count > 0)
            return Fail(context, string.Join("\n", composeErrors), result);

        // Scan contract — validate C# interface structure and stub names
        var scanErrors = ContractScanner.Scan(contract, _registry);
        if (scanErrors.Count > 0)
        {
            var listing = ContractScanner.FormatCorrectionListing(scanErrors);
            return Fail(context, listing, result);
        }

        // Phase E: contract-fidelity gate — does the contract actually COVER
        // the spec's intent? Rejects degenerate contracts (1-component collapse
        // of a multi-verb spec) and missing-verb contracts before they waste
        // the impl/QA/harness cycle. T8 a3 root cause: architect produced
        // FileIO.ReadFile for a parse→filter→count spec — well-formed but
        // semantically empty.
        var fidelityErrors = ContractFidelityChecker.Check(contract, context.UserRequest);
        if (fidelityErrors.Count > 0)
        {
            var fidelityMsg = ContractFidelityChecker.FormatErrors(fidelityErrors);
            return Fail(context, fidelityMsg, result);
        }

        // Type chain check — validate data flow types
        var chainErrors = TypeChainChecker.CheckPreImpl(contract);
        if (chainErrors.Count > 0)
        {
            var chainMsg = TypeChainChecker.FormatErrors(chainErrors);
            chainMsg += "\nFix the method signatures or connection order so types chain correctly.";
            return Fail(context, chainMsg, result);
        }

        return Success(context, contract, result);
    }

    public ValidationResult ValidateOutput(PhaseResult result)
    {
        if (result.Status != PhaseStatus.Success)
            return new ValidationResult { IsValid = false, Errors = result.Warnings };
        return new ValidationResult { IsValid = true };
    }

    private ArchitectureContract? ParseContract(string text)
    {
        try
        {
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            cleaned = OllamaModelGateway.ExtractJson(cleaned);
            return JsonSerializer.Deserialize<ArchitectureContract>(cleaned, PositJson.Options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[architecture PARSE] {ex.Message}");
            Console.Error.WriteLine($"[architecture PARSE] raw length={text.Length}, first 200={text[..Math.Min(200, text.Length)]}");
            return null;
        }
    }

    /// <summary>
    /// Write C# interface files to staging for logic components.
    /// The interface IS the carapace — method signatures, types, records.
    /// </summary>
    private List<string> WriteInterfaces(ArchitectureContract contract, PhaseContext context)
    {
        var errors = new List<string>();
        var stagingDir = GetStagingDir(context);
        Directory.CreateDirectory(stagingDir);

        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell) continue;

            if (!string.IsNullOrWhiteSpace(comp.CSharpInterface))
            {
                var path = Path.Combine(stagingDir, $"I{comp.Name}.cs");
                File.WriteAllText(path, comp.CSharpInterface);
            }
            else
            {
                errors.Add($"Component '{comp.Name}' has no csharpInterface — cannot create carapace");
            }
        }

        return errors;
    }

    private static string GetStagingDir(PhaseContext context) =>
        Path.Combine(Directory.GetCurrentDirectory(), ".posit", "staging",
            context.SessionId.Value, "csharp");

    private static PhaseResult Fail(PhaseContext ctx, string error, GenerationResult gen) => new()
    {
        PhaseId = ctx.PhaseId, Status = PhaseStatus.Failed,
        Artifacts = EmptyBundle(ctx),
        Costs = new CostSnapshot { InputTokens = gen.InputTokens, OutputTokens = gen.OutputTokens },
        Warnings = [error], RawOutput = gen.Text
    };

    private static PhaseResult Success(PhaseContext ctx, ArchitectureContract contract, GenerationResult gen)
    {
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(contract, PositJson.Options);
        return new PhaseResult
        {
            PhaseId = ctx.PhaseId, Status = PhaseStatus.Success,
            Artifacts = new ArtifactBundle
            {
                Id = ArtifactId.New(), SessionId = ctx.SessionId,
                SourcePhase = ctx.PhaseId, SchemaVersion = "1.0.0",
                Kind = ArtifactKind.ArchitectureContract,
                PayloadJson = payloadJson, ProducedAt = DateTimeOffset.UtcNow
            },
            Costs = new CostSnapshot { InputTokens = gen.InputTokens, OutputTokens = gen.OutputTokens },
            RawOutput = gen.Text
        };
    }

    private static ArtifactBundle EmptyBundle(PhaseContext ctx) => new()
    {
        Id = ArtifactId.New(), SessionId = ctx.SessionId,
        SourcePhase = ctx.PhaseId, SchemaVersion = "1.0.0",
        Kind = ArtifactKind.ArchitectureContract,
        PayloadJson = [], ProducedAt = DateTimeOffset.UtcNow
    };
}