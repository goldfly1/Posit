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
        // Pre-generation wiki search: find relevant C# pattern examples AND
        // proven contract examples (both indexed in wiki.wiki_chunks).
        var wikiExamples = "";
        var searchQuery = context.UserRequest ?? "";
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            wikiExamples = await _wiki.SearchAsync(searchQuery, limit: 15, ct);
            if (!string.IsNullOrWhiteSpace(wikiExamples))
                Console.Error.WriteLine("[architecture] pre-generation wiki search returned examples");
        }

        // Inject wiki examples into the USER prompt, not the system prompt tail.
        // System-prompt injection suffers "lost in the middle" — the model reads
        // 9.5K chars of architecture instructions, forms its plan, and ignores the
        // wiki examples at the tail. Putting them in the user prompt alongside the
        // spec makes them visible as "here's how to decompose THIS spec."
        var prompt = context.Prompt;
        var originalSpec = context.UserRequest ?? "";  // save before injection
        if (!string.IsNullOrWhiteSpace(wikiExamples))
        {
            context = context with { UserRequest = wikiExamples + "\n\n---\n\nSpec to decompose:\n" + originalSpec };
        }

        // Surgical-edit retry (hot-potato): if this is a retry (attempt > 0) and
        // we have the previous contract + correction signal, inject them so the
        // architect EDITS the previous contract instead of regenerating from
        // scratch. The correction signal contains the specific gate failure
        // (e.g. "ValidateColumnCounts returns bool but Merge expects string[]").
        var isRetry = context.AttemptNumber > 0;
        var previousContract = context.PreviousOutput ?? "";
        var correctionSignal = context.CorrectionSignal.Length > 0
            ? string.Join("\n", context.CorrectionSignal) : "";
        // On retries, bump temperature to break deterministic regurgitation.
        // v4-pro at temp=0.3 produces byte-identical output across retries —
        // the correction signal is received but the model can't escape its
        // own previous answer. Higher temperature forces diversity.
        if (isRetry && !string.IsNullOrWhiteSpace(previousContract) && !string.IsNullOrWhiteSpace(correctionSignal))
        {
            context = context with { ModelRoute = context.ModelRoute with { Temperature = 0.7 } };
            var editInstruction = $"""
                --- SURGICAL EDIT REQUIRED ---

                Your PREVIOUS contract (attempt {context.AttemptNumber - 1}) had this issue:

                {correctionSignal}

                Here is your previous contract JSON:
                ```json
                {previousContract}
                ```

                FIX the specific issue above. Keep everything else the same.
                Return the FULL corrected contract JSON. Do NOT start from scratch — EDIT the previous contract.
                ---
                """;
            // Replace the wiki examples with the edit instruction — on retries,
            // the surgical fix is more important than seeing patterns again.
            context = context with { UserRequest = editInstruction + "\n\n---\n\nSpec to decompose:\n" + originalSpec };
        }

        var result = await _model.GenerateAsync(
            context.ModelRoute, prompt, context, ct);

        if (string.IsNullOrWhiteSpace(result.Text))
            return Fail(context, "Model returned empty output", result);

        var contract = ParseContract(result.Text);
        if (contract == null)
            return Fail(context, "Failed to parse ArchitectureContract from model output", result);

        contract = SanitizeContractInterfaces(contract);

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
        var fidelityErrors = ContractFidelityChecker.Check(contract, originalSpec);
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

    /// <summary>
    /// Strip implementation lines from C# interface fields.
    /// The model sometimes puts for/while/if/var/return blocks in the interface
    /// (which should be signatures only). This removes lines containing
    /// implementation keywords, keeping only namespace/interface/method
    /// declarations, braces, doc comments, and test-case comments.
    /// </summary>
    private static ArchitectureContract SanitizeContractInterfaces(ArchitectureContract contract)
    {
        var implKeywords = new[] { "for", "while", "if", "var", "return", "foreach", "switch" };
        var sanitized = new List<Component>();
        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell || string.IsNullOrWhiteSpace(comp.CSharpInterface))
            {
                sanitized.Add(comp);
                continue;
            }
            var iface = comp.CSharpInterface;
            var lines = iface.Split('\n');
            var kept = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Keep structural lines (namespace, interface, braces, comments, blank)
                if (string.IsNullOrWhiteSpace(trimmed) ||
                    trimmed.StartsWith("//") ||
                    trimmed.StartsWith("namespace") ||
                    trimmed.StartsWith("public interface") ||
                    trimmed.StartsWith("{") ||
                    trimmed.StartsWith("}") ||
                    trimmed.StartsWith("///") ||
                    trimmed.StartsWith("using"))
                {
                    kept.Add(line);
                    continue;
                }
                // Strip implementation lines — match keywords as whole words
                // (regex \b) so "for(" (no space) is caught, and "Format" is NOT.
                // Check this BEFORE the method-signature check — a for loop has
                // ( and ) so it would pass as a "method signature" if checked first.
                var isImpl = implKeywords.Any(k =>
                    System.Text.RegularExpressions.Regex.IsMatch(
                        trimmed, $@"\b{k}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                if (isImpl)
                    continue; // skip implementation lines
                // Keep method signatures (lines with '(' and ')' and ';' or no body)
                if (trimmed.Contains("(") && (trimmed.Contains(")") || trimmed.Contains(";")))
                {
                    kept.Add(line);
                    continue;
                }
                // Keep anything else (might be method params on continuation lines)
                kept.Add(line);
            }
            sanitized.Add(comp with { CSharpInterface = string.Join("\n", kept) });
        }
        return contract with { Components = [.. sanitized] };
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