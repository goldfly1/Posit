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

    public DafnyImplementationPhase(Z3Runner z3) { _z3 = z3; _model = null; }
    public DafnyImplementationPhase(Z3Runner z3, IModelGateway model) { _z3 = z3; _model = model; }

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
            var isCutOut = File.Exists(skeletonPath);

            // If no skeleton (not a cut-out), generate Dafny with the model
            // using a Z3 correction loop: generate → Z3 reject → feed previous
            // Dafny + Z3 errors back → fix → verify. Max 3 attempts.
            // This is what a human does: write code, compiler says "error", look
            // at the error, look at the code, fix one line, recompile.
            if (!isCutOut && _model != null)
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

            currentDafny = generated;
            await File.WriteAllTextAsync(dafnyPath, generated, ct);

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

                currentErrors = errors;
                continue;
            }

            // Z3 passed — translate to C#
            var translation = await _z3.TranslateAsync(dafnyPath, comp.Name, ct);
            if (!translation.Success || string.IsNullOrWhiteSpace(translation.CleanCsharp))
            {
                Console.Error.WriteLine($"[dafny-impl] {comp.Name} attempt {attempt + 1}: Z3 passed but C# translation failed: {translation.Stderr}");
                // Feed translation error back — the Dafny needs to be adjusted so it translates cleanly
                currentErrors = [$"C# translation failed: {translation.Stderr}", "Fix the Dafny so it translates to C# correctly. Keep method signatures unchanged."];
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
        var prompt = BuildDafnyPrompt(comp, context, isCorrection: false, previousDafny: null, z3Errors: null);
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
        var prompt = BuildDafnyPrompt(comp, context, isCorrection: true,
            previousDafny: previousDafny, z3Errors: z3Errors);
        return await CallModelAndExtractDafny(prompt, context, ct);
    }

    /// <summary>
    /// Build the Dafny generation/fix prompt. On correction, includes the previous
    /// Dafny and Z3 errors so the model can do a targeted fix.
    /// </summary>
    private static StringBuilder BuildDafnyPrompt(Component comp, PhaseContext context,
        bool isCorrection, string? previousDafny, string[]? z3Errors)
    {
        var referenceCard = LoadReferenceCard();
        var testCases = comp.TestCases.Length > 0
            ? string.Join("\n", comp.TestCases.Select(tc => $"  // test: {tc.Description} → {tc.ExpectedBehavior}"))
            : "";

        var sigs = comp.MethodSignatures.Length > 0
            ? string.Join("\n", comp.MethodSignatures.Select(m =>
                $"  method {m.Name}({string.Join(", ", m.Params.Select(p => $"{p.Name}: {p.Type}"))}) returns ({m.ReturnType})"))
            : "";

        var pseudocode = ExtractPseudocodeForComponent(comp.Name, context);

        var sb = new StringBuilder();
        if (isCorrection)
        {
            sb.AppendLine("You are fixing a Dafny module that Z3 rejected. The compiler found errors.");
            sb.AppendLine("Look at YOUR previous code below, find the specific lines that caused the errors,");
            sb.AppendLine("and fix ONLY those lines. Keep everything else unchanged.");
            sb.AppendLine();

            // Detect "not Dafny" errors — the model output JSON or prose instead of Dafny
            var isNotDafnyError = z3Errors != null && z3Errors.Any(e =>
                e.Contains("this symbol not expected", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("unexpected token", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("parse error", StringComparison.OrdinalIgnoreCase));
            if (isNotDafnyError)
            {
                sb.AppendLine("⚠️  CRITICAL: Your previous output was NOT valid Dafny code!");
                sb.AppendLine("The error 'this symbol not expected' means the compiler received something");
                sb.AppendLine("that isn't Dafny (probably JSON or prose). You MUST output ONLY raw Dafny source code.");
                sb.AppendLine("No JSON, no markdown, no explanations — just the Dafny module.");
                sb.AppendLine("Start with 'module' and end with the closing '}'.");
                sb.AppendLine();
            }

            sb.AppendLine("═══ Z3 ERRORS (fix these) ═══");
            if (z3Errors != null)
                foreach (var err in z3Errors)
                    sb.AppendLine($"  {err}");
            sb.AppendLine();
            sb.AppendLine("═══ YOUR PREVIOUS OUTPUT (fix this, don't rewrite from scratch) ═══");
            sb.AppendLine(previousDafny);
            sb.AppendLine("═══ END PREVIOUS OUTPUT ═══");
            sb.AppendLine();
            if (isNotDafnyError)
            {
                sb.AppendLine("Output a COMPLETE Dafny module starting with 'module' and ending with '}'.");
                sb.AppendLine("ONLY Dafny code. No JSON wrappers, no markdown fences, no explanations.");
            }
            else
            {
                sb.AppendLine("Look at the errors above, find the specific line(s) in your code that caused them,");
                sb.AppendLine("and fix ONLY those lines. Keep method signatures, {:extern} declarations, and");
                sb.AppendLine("module structure exactly as-is. Output the complete fixed Dafny module.");
            }
        }
        else
        {
            sb.AppendLine("You are refactoring reduced pseudocode into valid Dafny.");
            sb.AppendLine("The pseudocode below IS the algorithm. Do NOT redesign it.");
            sb.AppendLine("Your job: wrap it in the method signatures, add contracts, fix syntax to valid Dafny. That's all.");
            sb.AppendLine("Z3 will verify your code — it must pass verification.");
        }
        sb.AppendLine();
        sb.AppendLine($"Component: {comp.Name}");
        sb.AppendLine($"Responsibility: {comp.Responsibility}");
        sb.AppendLine($"Pattern: {comp.PatternName ?? "custom"}");
        sb.AppendLine();
        sb.AppendLine("Method signatures to implement (USE THESE EXACT NAMES):");
        sb.AppendLine(sigs);
        sb.AppendLine();
        sb.AppendLine("Test cases (your implementation MUST satisfy these):");
        sb.AppendLine(testCases);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(pseudocode))
        {
            sb.AppendLine("Pseudocode to refactor into Dafny (this IS the algorithm — wrap it, don't redesign it):");
            sb.AppendLine(pseudocode);
            sb.AppendLine();
        }

        sb.AppendLine("Dafny Language Dictionary:");
        sb.AppendLine(referenceCard);
        sb.AppendLine();
        sb.AppendLine("═══ SYNTAX ERRORS TO AVOID (these are NOT Dafny — do NOT write them) ═══");
        sb.AppendLine("Dafny is NOT C#. These C# constructs do NOT work in Dafny:");
        sb.AppendLine("  BAD: (char)('0' + d)      → GOOD: char('0' + d)  [no C-style casts]");
        sb.AppendLine("  BAD: new string[|s|]       → GOOD: new char[|s|]   [Dafny arrays need element type]");
        sb.AppendLine("  BAD: for (i=0; i<n; i++)   → GOOD: for i := 0 to n-1  [Dafny for loop syntax]");
        sb.AppendLine("  BAD: map[string]int[]      → GOOD: map[string, int]  [comma, not concatenation]");
        sb.AppendLine("  BAD: map<string,int>()     → GOOD: map[]              [empty map literal, no parens]");
        sb.AppendLine("  BAD: seq[string]           → GOOD: seq<string>       [angle brackets for generics]");
        sb.AppendLine("  BAD: arr[i] = x            → GOOD: arr[i] := x       [Dafny uses := not = for assignment]");
        sb.AppendLine("  BAD: string.Join(...)      → GOOD: use recursive concatenation or Seq.Concat");
        sb.AppendLine("  BAD: int.TryParse(...)     → GOOD: write a helper method with a loop");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Output ONLY raw Dafny code. No JSON, no markdown fences, no explanations.");
        sb.AppendLine("2. Keep method signatures as specified above — use the EXACT method names.");
        sb.AppendLine("3. Keep all {:extern} declarations unchanged.");
        sb.AppendLine("4. ALWAYS use `method` — NEVER use `function`. Functions are pure expressions (no loops, no mutable assignment). Methods allow imperative code. Always use method.");
        sb.AppendLine("5. The code must pass Z3 verification.");
        sb.AppendLine("6. The pseudocode IS the algorithm — translate it into proper Dafny with contracts. Do not redesign the logic.");
        sb.AppendLine("7. Add requires/ensures clauses, invariants, and decreases for loops.");
        sb.AppendLine("8. Dafny is NOT C#. Do not write C# syntax. Use := for assignment, char() not (char), for i := 0 to n not for(i=0...), map[K,V] not map[K]V.");

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

            var text = OllamaModelGateway.StripReasoningTags(gen.Text).Trim();

            // Strip markdown fences if present
            var fenceMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"```(?:dafny)?\s*\n?(.*?)\n?```",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (fenceMatch.Success)
                return CleanDafny(fenceMatch.Groups[1].Value);

            // Model may wrap Dafny in JSON — extract by scanning for code-like string property
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
            {
                var extracted = ExtractDafnyFromJson(text.TrimStart());
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