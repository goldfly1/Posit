namespace Posit.Phases;

using System.Text;
using Posit.AI.Models;

/// <summary>
/// Phase 3: Dafny Implementation. Hybrid — deterministic for cut-outs, model-generated for custom logic.
/// - Cut-out components: skeleton exists on disk → Z3 verify → translate (no model call)
/// - Custom components: no skeleton → model writes Dafny from spec + reference card → Z3 verify → translate
/// Z3 is the judge in both paths. Design Review is the QA.
/// </summary>
public sealed class DafnyImplementationPhase : IPhase
{
    private readonly Z3Runner _z3;
    private readonly IModelGateway? _model;
    private WikiSearcher? _wiki;

    public DafnyImplementationPhase(Z3Runner z3) { _z3 = z3; _model = null; }
    public DafnyImplementationPhase(Z3Runner z3, IModelGateway model) { _z3 = z3; _model = model; _wiki = new WikiSearcher(new HttpClient()); }

    public PhaseId Id { get; } = new("dafny-implementation");
    public string Name => "Dafny Implementation";
    public PhaseId[] Dependencies { get; } = [new("dafny-contracts")];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.DafnyVerification,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(DafnyVerificationResult)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        var contract = ExtractContract(context);
        if (contract == null)
            return Fail(context, "No ArchitectureContract in input artifacts");

        var results = new List<DafnyVerificationResult>();
        var warnings = new List<string>();
        var stagingDir = GetStagingDir(context);
        Directory.CreateDirectory(stagingDir);

        foreach (var comp in contract.Components)
        {
            if (comp.Classification == ModuleClassification.IoShell) continue;

            var skeletonPath = ResolveDafnyPath(context, comp);
            // A component needs DafnyImpl (model generation) when:
            // - It has a DafnyInterface (architect wrote the interface, bodies need filling)
            // - It has no patternName (no cut-out skeleton with pre-written bodies)
            // Legacy cut-outs (patternName set, skeleton composed by registry) skip DafnyImpl.
            var needsModelGeneration = !string.IsNullOrWhiteSpace(comp.DafnyInterface)
                || (string.IsNullOrWhiteSpace(comp.PatternName) && _model != null);
            var isCutOut = !needsModelGeneration && File.Exists(skeletonPath);

            // If needs model generation (architect wrote interface, no bodies), generate Dafny
            // with the model using a Z3 correction loop: generate → Z3 reject → feed previous
            // Dafny + Z3 errors back → fix → verify. Max 4 attempts.
            if (needsModelGeneration && _model != null)
            {
                var (generatedDafny, verifyOk, verifyOutput, translateOutput, translatePath) =
                    await GenerateAndVerifyDafnyAsync(comp, context, stagingDir, ct);

                if (!verifyOk)
                {
                    warnings.Add($"Z3 verification failed for '{comp.Name}' after correction loop: {verifyOutput?[..Math.Min(300, verifyOutput.Length)]}");
                    results.Add(new DafnyVerificationResult
                    {
                        ModuleName = comp.Name, DafnyPath = translatePath ?? skeletonPath,
                        IsVerified = false, VerificationOutput = verifyOutput ?? "Z3 rejected"
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(translateOutput))
                {
                    warnings.Add($"C# translation failed for '{comp.Name}' after Z3 verification");
                    results.Add(new DafnyVerificationResult
                    {
                        ModuleName = comp.Name, DafnyPath = translatePath!,
                        IsVerified = false, VerificationOutput = "C# translation failed"
                    });
                    continue;
                }

                var csPath = Path.Combine(stagingDir, $"{comp.Name}.cs");
                await File.WriteAllTextAsync(csPath, translateOutput!, ct);
                results.Add(new DafnyVerificationResult
                {
                    ModuleName = comp.Name, DafnyPath = translatePath!,
                    IsVerified = true, TranslatedCSharpPath = csPath
                });
                continue;
            }
            else if (!isCutOut)
            {
                warnings.Add($"No skeleton and no model for '{comp.Name}' — cannot generate Dafny");
                results.Add(FailResult(comp.Name, skeletonPath, "No skeleton and no model"));
                continue;
            }

            // Cut-out path: skeleton exists on disk → Z3 verify → translate
            var verifyResult = await _z3.VerifyAsync(skeletonPath, ct);
            if (!verifyResult.Success)
            {
                var msg = verifyResult.Stdout[..Math.Min(300, verifyResult.Stdout.Length)];
                warnings.Add($"Z3 verification failed for '{comp.Name}': {msg}");
                results.Add(new DafnyVerificationResult
                {
                    ModuleName = comp.Name, DafnyPath = skeletonPath,
                    IsVerified = false, VerificationOutput = verifyResult.Stdout
                });
                continue;
            }

            // Step 2: Translate verified Dafny to C#
            var translation = await _z3.TranslateAsync(skeletonPath, comp.Name, ct);
            if (!translation.Success || string.IsNullOrWhiteSpace(translation.CleanCsharp))
            {
                warnings.Add($"C# translation failed for '{comp.Name}': {translation.Stderr}");
                results.Add(new DafnyVerificationResult
                {
                    ModuleName = comp.Name, DafnyPath = skeletonPath,
                    IsVerified = false, VerificationOutput = translation.Stderr
                });
                continue;
            }

            var csPath2 = Path.Combine(stagingDir, $"{comp.Name}.cs");
            await File.WriteAllTextAsync(csPath2, translation.CleanCsharp, ct);
            results.Add(new DafnyVerificationResult
            {
                ModuleName = comp.Name, DafnyPath = skeletonPath,
                IsVerified = true, TranslatedCSharpPath = csPath2
            });
        }

        var allOk = results.Count > 0 && results.All(r => r.IsVerified && !string.IsNullOrWhiteSpace(r.TranslatedCSharpPath));
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(results.ToArray(), PositJson.Options);

        return new PhaseResult
        {
            PhaseId = context.PhaseId,
            Status = allOk ? PhaseStatus.Success : PhaseStatus.Failed,
            Artifacts = new ArtifactBundle
            {
                Id = ArtifactId.New(), SessionId = context.SessionId,
                SourcePhase = context.PhaseId, SchemaVersion = "1.0.0",
                Kind = ArtifactKind.DafnyVerification,
                PayloadJson = payloadJson, ProducedAt = DateTimeOffset.UtcNow
            },
            Costs = CostSnapshot.Zero,
            Warnings = warnings.ToArray()
        };
    }

    /// <summary>
    /// Generate Dafny for a component AND verify it with Z3, using a correction
    /// loop: generate → Z3 reject → feed previous Dafny + Z3 errors back → fix → verify.
    /// Max 3 attempts. This is what a human does: write code, compiler says "error",
    /// look at the error, look at the code, fix one line, recompile. The compiler is the teacher.
    /// </summary>
    /// <returns>Tuple of (generatedDafny, verifyOk, verifyOutput, translatedCSharp, dafnyPath).</returns>
    private async Task<(string? Dafny, bool Verified, string? VerifyOutput, string? TranslatedCSharp, string? DafnyPath)>
        GenerateAndVerifyDafnyAsync(Component comp, PhaseContext context, string stagingDir, CancellationToken ct)
    {
        const int maxAttempts = 4;
        var dafnyPath = Path.Combine(stagingDir, $"{comp.Name}.dfy");
        string? currentDafny = null;
        string[] currentErrors = [];
        var errorClassHistory = new List<string>();  // track error classes for escalation
        var currentPseudocode = ExtractPseudocodeForComponent(comp.Name, context);
        string? previousOutput = null;  // track for stuck-output detection

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Generate (or fix) the Dafny
            var generated = attempt == 0
                ? await GenerateDafnyAsync(comp, context, ct)
                : await FixDafnyAsync(comp, context, currentDafny!, currentErrors, ct);

            if (string.IsNullOrWhiteSpace(generated))
            {
                Console.Error.WriteLine($"[dafny-impl] {comp.Name} attempt {attempt + 1}: model returned empty");
                return (null, false, "Model returned empty output", null, dafnyPath);
            }

            // Stuck-output detection: if the model generates identical output twice,
            // stop retrying — the correction signal isn't changing the model's behavior.
            if (previousOutput != null && generated == previousOutput)
            {
                Console.Error.WriteLine($"[dafny-impl] {comp.Name} attempt {attempt + 1}: identical to previous — model is stuck, stopping correction loop");
                return (generated, false, "Model generated identical output twice — correction signal not effective", null, dafnyPath);
            }
            previousOutput = generated;

            currentDafny = generated;
            await File.WriteAllTextAsync(dafnyPath, generated, ct);

            // Static checker — catch known error patterns before Z3 (instant, free)
            var staticIssues = StaticChecker.CheckDafny(generated);
            if (staticIssues.Count > 0)
            {
                var staticFeedback = StaticChecker.FormatIssues(staticIssues, "Dafny");
                Console.Error.WriteLine($"[dafny-impl] {comp.Name} attempt {attempt + 1}: static checker found {staticIssues.Count} issue(s) — feeding back before Z3");

                // Feed static issues back as errors, skip Z3 call
                currentErrors = staticIssues.Select(s => s.Message).ToArray();
                errorClassHistory.Add(StaticChecker.ClassifyStaticIssue(staticIssues));
                continue;
            }

            // Z3 verify — the compiler is the teacher
            var verifyResult = await _z3.VerifyAsync(dafnyPath, ct);
            if (!verifyResult.Success)
            {
                var errors = verifyResult.Errors.Length > 0
                    ? verifyResult.Errors.Take(10).ToArray()
                    : new[] { verifyResult.Stdout[..Math.Min(500, verifyResult.Stdout.Length)] };

                Console.Error.WriteLine($"[dafny-impl] {comp.Name} attempt {attempt + 1}/{maxAttempts}: Z3 rejected:");
                foreach (var err in errors.Take(5))
                    Console.Error.WriteLine($"  {err}");

                // Search the Dafny stdlib for examples relevant to the error
                if (_wiki != null && attempt < maxAttempts - 1)
                {
                    var errorQuery = string.Join(" ", errors.Take(3)) + " " + comp.Responsibility;
                    var wikiExamples = await _wiki.SearchAsync(errorQuery, limit: 2, ct);
                    if (!string.IsNullOrWhiteSpace(wikiExamples))
                    {
                        Console.Error.WriteLine($"[dafny-impl] {comp.Name}: injecting stdlib reference examples");
                        // Append wiki examples to the errors so the correction prompt includes them
                        var enhancedErrors = errors.ToList();
                        enhancedErrors.Add(wikiExamples);
                        currentErrors = enhancedErrors.ToArray();
                    }
                    else
                    {
                        currentErrors = errors;
                    }
                }
                else
                {
                    currentErrors = errors;
                }
                errorClassHistory.Add(ClassifyError(currentErrors));
                continue;
            }

            // Z3 passed — translate to C#
            var translation = await _z3.TranslateAsync(dafnyPath, comp.Name, ct);
            if (!translation.Success || string.IsNullOrWhiteSpace(translation.CleanCsharp))
            {
                Console.Error.WriteLine($"[dafny-impl] {comp.Name} attempt {attempt + 1}: Z3 passed but C# translation failed: {translation.Stderr}");
                // Feed translation error back — the Dafny needs to be adjusted so it translates cleanly
                currentErrors = [$"C# translation failed: {translation.Stderr}", "Fix the Dafny so it translates to C# correctly. Keep method signatures unchanged."];
                errorClassHistory.Add("translation");
                continue;
            }

            Console.Error.WriteLine($"[dafny-impl] {comp.Name} Z3 verified + translated on attempt {attempt + 1}");
            return (generated, true, null, translation.CleanCsharp, dafnyPath);
        }

        // Exhausted all attempts — return the last Z3 error output
        var lastOutput = currentErrors.Length > 0 ? string.Join("\n", currentErrors) : "Z3 verification failed (no details)";
        return (currentDafny, false, lastOutput, null, dafnyPath);
    }

    /// <summary>
    /// Generate Dafny code for a component using the model (first attempt).
    /// Includes the Dafny reference card, the component's spec/responsibility/test cases,
    /// and the crystallized pseudocode from the reduction phase.
    /// </summary>
    private async Task<string?> GenerateDafnyAsync(Component comp, PhaseContext context, CancellationToken ct)
    {
        var prompt = await BuildDafnyPromptAsync(comp, context, isCorrection: false, previousDafny: null, z3Errors: null, ct);
        return await CallModelAndExtractDafny(prompt, context, ct);
    }

    /// <summary>
    /// Fix Dafny code that Z3 rejected. The model sees its previous Dafny AND the
    /// Z3 errors — so it can do a targeted fix instead of rewriting from scratch.
    /// This is what a human does: look at line 38, see the error, fix one line, recompile.
    /// </summary>
    private async Task<string?> FixDafnyAsync(Component comp, PhaseContext context,
        string previousDafny, string[] z3Errors, CancellationToken ct)
    {
        var prompt = await BuildDafnyPromptAsync(comp, context, isCorrection: true,
            previousDafny: previousDafny, z3Errors: z3Errors, ct);
        return await CallModelAndExtractDafny(prompt, context, ct);
    }

    /// <summary>
    /// Build the Dafny generation/fix prompt. On correction, includes the previous
    /// Dafny and Z3 errors so the model can do a targeted fix.
    /// </summary>
    private async Task<StringBuilder> BuildDafnyPromptAsync(Component comp, PhaseContext context,
        bool isCorrection, string? previousDafny, string[]? z3Errors, CancellationToken ct)
    {
        var testCases = comp.TestCases.Length > 0
            ? string.Join("\n", comp.TestCases.Select(tc => $"  // test: {tc.Description} → {tc.ExpectedBehavior}"))
            : "";

        var sigs = comp.MethodSignatures.Length > 0
            ? string.Join("\n", comp.MethodSignatures.Select(m =>
                $"  method {m.Name}({string.Join(", ", m.Params.Select(p => $"{p.Name}: {p.Type}"))}) returns ({m.ReturnType})"))
            : "";

        var pseudocode = ExtractPseudocodeForComponent(comp.Name, context);

        // Read the skeleton .dfy file — this IS the interface the model must implement against
        var skeletonPath = ResolveDafnyPath(context, comp);
        var skeleton = File.Exists(skeletonPath)
            ? File.ReadAllText(skeletonPath)
            : null;

        // Pre-generation wiki search: find relevant Dafny examples
        var wikiExamples = "";
        if (_wiki != null)
        {
            // Search using pseudocode if available, otherwise use responsibility + method signatures
            var searchQuery = !string.IsNullOrWhiteSpace(pseudocode)
                ? $"{comp.Responsibility} {pseudocode[..Math.Min(200, pseudocode.Length)]}"
                : $"{comp.Responsibility} {sigs}";
            wikiExamples = await _wiki.SearchAsync(searchQuery, limit: 3, ct);
            if (!string.IsNullOrWhiteSpace(wikiExamples))
                Console.Error.WriteLine($"[dafny-impl] {comp.Name}: pre-generation wiki search returned examples");
        }

        var sb = new StringBuilder();
        if (isCorrection)
        {
            sb.AppendLine("You are a Dafny coder fixing code that Z3 rejected.");
            sb.AppendLine("Fix the specific lines that caused the errors. Keep everything else unchanged.");
            sb.AppendLine();

            sb.AppendLine("═══ Z3 ERRORS (fix these) ═══");
            if (z3Errors != null)
                foreach (var err in z3Errors)
                    sb.AppendLine($"  {err}");
            sb.AppendLine();
            sb.AppendLine("═══ YOUR PREVIOUS CODE (fix this) ═══");
            sb.AppendLine(previousDafny);
            sb.AppendLine("═══ END PREVIOUS CODE ═══");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("You are a Dafny coder. Implement the method bodies in the interface definition below.");
            sb.AppendLine("The pseudocode IS the algorithm — translate it into valid Dafny. Do not redesign the logic.");
            sb.AppendLine();
        }

        sb.AppendLine($"Component: {comp.Name}");
        sb.AppendLine($"Responsibility: {comp.Responsibility}");
        sb.AppendLine();
        sb.AppendLine("Method signatures (USE THESE EXACT NAMES):");
        sb.AppendLine(sigs);
        sb.AppendLine();

        // Inject the interface definition
        if (!string.IsNullOrWhiteSpace(skeleton))
        {
            sb.AppendLine("═══ INTERFACE DEFINITION ═══");
            sb.AppendLine("Implement the method bodies within this interface. Do NOT change:");
            sb.AppendLine("  - module name, includes, datatype declarations, {:extern} portals");
            sb.AppendLine("  - method signatures, requires/ensures contracts");
            sb.AppendLine("Use the declared types — do not invent new types.");
            sb.AppendLine(skeleton);
            sb.AppendLine("═══ END INTERFACE DEFINITION ═══");
            sb.AppendLine();
        }

        sb.AppendLine("Test cases (MUST satisfy these):");
        sb.AppendLine(testCases);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(pseudocode))
        {
            sb.AppendLine("Pseudocode (this IS the algorithm):");
            sb.AppendLine(pseudocode);
            sb.AppendLine();
        }

        // Inject wiki examples (replaces the 5.6K reference card + C#-ism cheat sheet)
        if (!string.IsNullOrWhiteSpace(wikiExamples))
        {
            sb.AppendLine(wikiExamples);
            sb.AppendLine();
        }

        // Lean rules — 3 rules + DON'T block
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Output ONLY raw Dafny code starting with 'module'. No JSON, no markdown, no explanations.");
        sb.AppendLine("2. 'function' = pure expression (NO var, NO :=, NO while, NO return). Used in requires/ensures.");
        sb.AppendLine("   'method' = imperative code (var, :=, while, if/else, return). CANNOT be called in requires/ensures.");
        sb.AppendLine("   If a helper needs var or loops, it MUST be a 'method'. Call it from the method body, not from ensures.");
        sb.AppendLine("   If a helper is a pure calculation (e.g. ConvertCtoF(v: real): real { v * 9.0 / 5.0 + 32.0 }), use 'function'.");
        sb.AppendLine("3. Add invariants and decreases for loops. The code must pass Z3 verification.");
        sb.AppendLine();
        sb.AppendLine("DON'T LET THIS HAPPEN TO YOU — these ALWAYS fail:");
        sb.AppendLine("  - function with var/while/:= → must be method (function is pure expression only)");
        sb.AppendLine("  - while without invariant + decreases → Z3 always rejects. Keep invariants SIMPLE (0 <= i <= n).");
        sb.AppendLine("  - method call in requires/ensures → must be function");
        sb.AppendLine("  - set comprehension without type: {j | ...} → must be {j: int | ...}");
        sb.AppendLine("  - (char) casts, new string[], C-style for loops → C#-isms, not Dafny");
        sb.AppendLine("  - method called in expression (e.g. string concat) → must be function or use var first");
        sb.AppendLine("  - map[K]V → must be map<K, V> (comma, angle brackets)");
        sb.AppendLine("  - seq[T] → must be seq<T> (angle brackets, not square)");
        sb.AppendLine("  - complex invariants → Z3 can't prove them. Use simple bounds (0 <= i <= n), let ensures capture the math.");
        sb.AppendLine("  - don't reinvent string splitting as recursive function → use method with while loop");

        return sb;
    }
    /// <summary>
    /// Call the model with a prompt and extract Dafny from the response.
    /// Handles reasoning tags, markdown fences, and JSON wrappers.
    /// </summary>
    private async Task<string?> CallModelAndExtractDafny(StringBuilder systemPrompt, PhaseContext context, CancellationToken ct)
    {
        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = systemPrompt.ToString(),
            OutputFormatSpec = "Dafny source code only",
            ModelTier = ModelTier.Fast,
            Temperature = 0.2,
            MaxOutputTokens = 8192,
            OutputFormat = OutputFormat.PlainText,
            OutputSchemaRef = "DafnyModule",
            Status = PromptStatus.Active
        };

        try
        {
            var gen = await _model!.GenerateAsync(context.ModelRoute, prompt, context, ct);
            if (string.IsNullOrWhiteSpace(gen.Text))
                return null;

            var text = OllamaModelGateway.StripReasoningTags(gen.Text).Trim(); Console.Error.WriteLine($"[dafny-impl] RAW MODEL OUTPUT first 80: {text[..Math.Min(80, text.Length)]}");

            // Strip markdown fences if present
            var fenceMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"```(?:dafny)?\s*\n?(.*?)\n?```",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (fenceMatch.Success)
            {
                var fenceContent = CleanDafny(fenceMatch.Groups[1].Value);
                // Fence content might be JSON — extract Dafny from it
                var fenceTrimmed = fenceContent.TrimStart();
                if (fenceTrimmed.StartsWith('{') || fenceTrimmed.StartsWith('[')
                    || fenceTrimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[dafny-impl] Fence content is JSON, extracting Dafny from it");
                    var fenceExtracted = ExtractDafnyFromJson(fenceTrimmed);
                    if (fenceExtracted != null)
                        return CleanDafny(fenceExtracted);
                }
                return fenceContent;
            }

            // Model may wrap Dafny in JSON — extract by scanning for code-like string property
            // Only trigger JSON extraction if the text STARTS with { or [ (actual JSON).
            // Do NOT trigger on {" appearing inside Dafny code (set literals like {"C", "F"}).
            var trimmedForJson = text.TrimStart();
            if (trimmedForJson.StartsWith('{') || trimmedForJson.StartsWith('[')
                || trimmedForJson.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[dafny-impl] JSON detection fired, text starts: {trimmedForJson[..Math.Min(40, trimmedForJson.Length)]}");
                var extracted = ExtractDafnyFromJson(trimmedForJson);
                if (extracted == null)
                    Console.Error.WriteLine("[dafny-impl] ExtractDafnyFromJson returned null!");
                else
                    Console.Error.WriteLine($"[dafny-impl] Extracted {extracted.Length} chars");
                if (extracted != null)
                    return CleanDafny(extracted);
            }

            return CleanDafny(text);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[dafny-impl] Model generation failed: {ex.Message}");
            return null;
        }
    }

    private static string LoadReferenceCard()
    {
        var paths = new[] {
            Path.Combine(Directory.GetCurrentDirectory(), "patterns", "dafny-reference-card.dfy"),
            "C:/Users/goldf/Posit/patterns/dafny-reference-card.dfy"
        };
        foreach (var p in paths)
            if (File.Exists(p))
                return File.ReadAllText(p);
        return "// Dafny reference card not found";
    }

    private static string CleanDafny(string code) =>
        code.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

    /// <summary>
    /// Read the pseudocode reduction artifact and extract the final pass
    /// for each method of the given component. Returns the crystallized
    /// pseudocode as a single string, or null if no artifact found.
    /// </summary>
    private static string? ExtractPseudocodeForComponent(string compName, PhaseContext context)
    {
        foreach (var a in context.InputArtifacts)
        {
            if (a.Kind != ArtifactKind.PseudocodeModule) continue;
            try
            {
                var bundle = JsonSerializer.Deserialize<PseudocodeReductionBundle>(a.PayloadJson, PositJson.Options);
                if (bundle == null) continue;
                var result = bundle.Results.FirstOrDefault(r => r.ModuleName == compName);
                if (result == null) continue;

                var sb = new StringBuilder();
                foreach (var (methodName, passes) in result.MethodReductions)
                {
                    // Get the last non-STOP pass — that's the crystallized pseudocode
                    var finalPass = passes.LastOrDefault(p => !p.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase));
                    if (finalPass != null)
                    {
                        sb.AppendLine($"// {methodName}:");
                        sb.AppendLine(finalPass);
                        sb.AppendLine();
                    }
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Recursively scan JSON for a string property that looks like Dafny code.
    /// The model nests Dafny in arbitrary JSON structures: {methods:[{body:"..."}]}.
    /// Scan all string values at any depth for Dafny keywords.
    /// </summary>
    private static string? ExtractDafnyFromJson(string text)
    {
        // Strip common non-JSON prefixes (model sometimes writes "json\n{...}" or "```json\n{...}")
        var jsonStart = text.IndexOf('{');
        if (jsonStart > 0)
            text = text[jsonStart..];
        // Strip trailing content after the JSON object
        var jsonEnd = text.LastIndexOf('}');
        if (jsonEnd >= 0 && jsonEnd < text.Length - 1)
            text = text[..(jsonEnd + 1)];

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            return ScanJsonForDafny(doc.RootElement);
        }
        catch { return null; }
    }

    private static readonly string[] DafnyCodeMarkers =
        ["method ", "module ", "function ", "datatype ", "requires ", "ensures ", "var ", ":=", "if ", "return "];

    private static string? ScanJsonForDafny(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                var s = element.GetString();
                // Must contain Dafny code markers AND be substantial (not just a field name)
                if (s != null && s.Length > 20 && DafnyCodeMarkers.Any(m => s.Contains(m, StringComparison.OrdinalIgnoreCase)))
                {
                    // If it looks like a complete module, return as-is
                    if (s.Contains("module ", StringComparison.OrdinalIgnoreCase) ||
                        s.Contains("method ", StringComparison.OrdinalIgnoreCase))
                        return s;
                    // It's a code fragment (body field) — wrap it in a module structure
                    // The caller will need to reconstruct the full module
                    return s;
                }
                return null;
            case System.Text.Json.JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var found = ScanJsonForDafny(prop.Value);
                    if (found != null) return found;
                }
                return null;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = ScanJsonForDafny(item);
                    if (found != null) return found;
                }
                return null;
            default:
                return null;
        }
    }

    public ValidationResult ValidateOutput(PhaseResult result)
    {
        if (result.Status != PhaseStatus.Success)
            return new ValidationResult { IsValid = false, Errors = result.Warnings };
        return new ValidationResult { IsValid = true };
    }

    private static DafnyVerificationResult FailResult(string name, string path, string error) => new()
    {
        ModuleName = name, DafnyPath = path, IsVerified = false, VerificationOutput = error
    };

    private static string ResolveDafnyPath(PhaseContext ctx, Component comp) =>
        !string.IsNullOrWhiteSpace(comp.DafnyContractPath)
            ? comp.DafnyContractPath!
            : Path.Combine(GetStagingDir(ctx), $"{comp.Name}.dfy");

    // ── Pseudocode re-reduction support ──────────────────────────────────────

    /// <summary>
    /// Classify Z3 errors into error classes for escalation detection.
    /// Returns a short string like "cs-ism-for-loop", "parse-error", "type-mismatch".
    /// When the same class appears twice, we escalate to pseudocode re-reduction.
    /// </summary>
    private static string ClassifyError(string[] errors)
    {
        var combined = string.Join(" ", errors).ToLowerInvariant();

        // C#-ism: for loops with C syntax
        if (combined.Contains("for (") || combined.Contains("for(") ||
            combined.Contains("i = 0") || combined.Contains("i++"))
            return "cs-ism-for-loop";

        // C#-ism: char casts
        if (combined.Contains("(char)") || combined.Contains("char cast"))
            return "cs-ism-char-cast";

        // C#-ism: map/seq syntax — rbracket expected
        if (combined.Contains("rbracket expected"))
            return "rbracket-error";

        // Parse errors — not Dafny (JSON, prose, etc.)
        if (combined.Contains("this symbol not expected") ||
            combined.Contains("unexpected token") ||
            combined.Contains("parse error"))
            return "parse-error";

        // while inside function (the #1 error before the function ban)
        if (combined.Contains("invalid unaryexpression") ||
            (combined.Contains("while") && combined.Contains("function")))
            return "while-in-function";

        // Type mismatch / resolution
        if (combined.Contains("type mismatch") || combined.Contains("cannot find") ||
            combined.Contains("unresolved"))
            return "type-mismatch";

        return "unknown";
    }

    /// <summary>
    /// Build a bone chart: align original pseudocode with the Dafny that failed,
    /// annotated with the Z3 error. This is what the pseudocode reducer sees
    /// when it needs to fix a fragment that caused a Dafny error.
    /// </summary>
    private static string BuildBoneChart(string originalPseudocode, string failingDafny, string[] z3Errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ BONE CHART: Pseudocode → Dafny Alignment ═══");
        sb.AppendLine();
        sb.AppendLine("Z3 ERRORS:");
        foreach (var err in z3Errors.Take(5))
            sb.AppendLine($"  ❌ {err}");
        sb.AppendLine();
        sb.AppendLine("ORIGINAL PSEUDOCODE (the intent):");
        sb.AppendLine(originalPseudocode);
        sb.AppendLine();
        sb.AppendLine("FAILING DAFNY (what was generated from the pseudocode):");
        sb.AppendLine(failingDafny[..Math.Min(3000, failingDafny.Length)]);
        sb.AppendLine();
        sb.AppendLine("═══ END BONE CHART ═══");
        return sb.ToString();
    }

    /// <summary>
    /// Re-reduce pseudocode after Z3 rejects Dafny with a repeated error class.
    /// The reducer sees the bone chart (original pseudocode + failing Dafny + Z3 errors)
    /// and the reference card. It fixes the specific fragments that caused the error.
    /// </summary>
    private async Task<string?> ReReducePseudocodeAsync(
        Component comp, PhaseContext context,
        string originalPseudocode, string failingDafny, string[] z3Errors,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(originalPseudocode))
            return null;

        var boneChart = BuildBoneChart(originalPseudocode, failingDafny, z3Errors);

        var sb = new StringBuilder();
        sb.AppendLine("You are a pseudocode reducer fixing a fragment that caused a Dafny verification error.");
        sb.AppendLine("The bone chart below shows the original pseudocode (the intent) and the Dafny that was");
        sb.AppendLine("generated from it. Z3 rejected the Dafny. Your job: fix the pseudocode fragments that");
        sb.AppendLine("caused the error so they use valid Dafny tokens from the reference card.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Fix ONLY the fragments that caused the Z3 error. Keep everything else unchanged.");
        sb.AppendLine("2. Replace C#-isms with Dafny equivalents (for→while+invariant, (char)→char(), map[K]V→map[K,V]).");
        sb.AppendLine("3. Use 'function' for pure helpers called in requires/ensures. Use 'method' for code with loops or assignment.");
        sb.AppendLine("4. Use Dafny syntax: method (not function), while+invariant, :=, char(), seq<T>, map[K,V].");
        sb.AppendLine("5. Output the COMPLETE corrected pseudocode (all methods, not just the fixed fragment).");
        sb.AppendLine("6. The pseudocode IS the algorithm — do not redesign the logic, just fix the syntax.");
        sb.AppendLine();
        sb.AppendLine(boneChart);
        sb.AppendLine();

        var prompt = new PromptTemplate
        {
            PhaseId = context.PhaseId,
            Version = new PromptVersion("1.0.0"),
            SystemPrompt = sb.ToString(),
            OutputFormatSpec = "Corrected pseudocode",
            ModelTier = ModelTier.Fast,
            Temperature = 0.1,
            MaxOutputTokens = 4096,
            OutputFormat = OutputFormat.PlainText,
            OutputSchemaRef = "Pseudocode",
            Status = PromptStatus.Active
        };

        try
        {
            var gen = await _model!.GenerateAsync(context.ModelRoute, prompt, context, ct);
            if (string.IsNullOrWhiteSpace(gen.Text))
                return null;
            return OllamaModelGateway.StripReasoningTags(gen.Text).Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[dafny-impl] re-reduction model call failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generate Dafny using specific (re-reduced) pseudocode instead of the
    /// pseudocode from the artifact. This is used after re-reduction to give
    /// DafnyImpl a fresh start with improved pseudocode.
    /// </summary>
    private async Task<string?> GenerateDafnyWithPseudocodeAsync(
        Component comp, PhaseContext context, string pseudocode, CancellationToken ct)
    {
        var prompt = await BuildDafnyPromptWithPseudocodeAsync(comp, context, pseudocode, isCorrection: false, ct);
        return await CallModelAndExtractDafny(prompt, context, ct);
    }

    /// <summary>
    /// Build a Dafny generation prompt using specific pseudocode (not from artifact).
    /// </summary>
    private async Task<StringBuilder> BuildDafnyPromptWithPseudocodeAsync(
        Component comp, PhaseContext context, string pseudocode, bool isCorrection, CancellationToken ct)
    {
        // Reuse the existing prompt builder but inject the re-reduced pseudocode
        var sb = await BuildDafnyPromptAsync(comp, context, isCorrection, null, null, ct);
        // The existing prompt already has the old pseudocode from ExtractPseudocodeForComponent.
        // We need to replace it. Find the pseudocode section and swap it.
        // Simplest: append the re-reduced pseudocode as an override.
        sb.AppendLine();
        sb.AppendLine("═══ RE-REDUCED PSEUDOCODE (use THIS version, not the one above) ═══");
        sb.AppendLine(pseudocode);
        sb.AppendLine("═══ END RE-REDUCED PSEUDOCODE ═══");
        return sb;
    }

    private static ArchitectureContract? ExtractContract(PhaseContext ctx)
    {
        foreach (var a in ctx.InputArtifacts)
            if (a.Kind == ArtifactKind.ArchitectureContract)
                try { return JsonSerializer.Deserialize<ArchitectureContract>(a.PayloadJson, PositJson.Options); }
                catch { }
        return null;
    }

    private static string GetStagingDir(PhaseContext ctx) =>
        Path.Combine(Directory.GetCurrentDirectory(), ".posit", "staging", ctx.SessionId.Value, "dafny");

    private static PhaseResult Fail(PhaseContext ctx, string error) => new()
    {
        PhaseId = ctx.PhaseId, Status = PhaseStatus.Failed,
        Artifacts = Empty(ctx), Costs = CostSnapshot.Zero, Warnings = [error]
    };

    private static ArtifactBundle Empty(PhaseContext ctx) => new()
    {
        Id = ArtifactId.New(), SessionId = ctx.SessionId, SourcePhase = ctx.PhaseId,
        SchemaVersion = "1.0.0", Kind = ArtifactKind.DafnyVerification,
        PayloadJson = [], ProducedAt = DateTimeOffset.UtcNow
    };
}