using Posit.Cli.Orchestration;

namespace Posit.Cli;

/// <summary>Adapter from PatternRegistry to IPatternRegistry.</summary>
internal sealed class PatternRegistryAdapter : IPatternRegistry
{
    private readonly PatternRegistry _inner;
    public PatternRegistryAdapter(PatternRegistry inner) => _inner = inner;

    public string ComposeIoShellSkeleton(string stubName, string componentName) =>
        _inner.ComposeIoShellSkeleton(stubName, componentName);
    public (string Name, string Responsibility)[] GetAllPatterns() =>
        _inner.GetAllPatterns().Select(p => (p.Name, p.Responsibility ?? "")).ToArray();
}

/// <summary>CLI entry point: run, harness, status, resume, artifacts.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 1; }
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "run" => await RunCommand(args[1..]),
                "harness" => await HarnessCommand(args[1..]),
                "status" => await StatusCommand(),
                "resume" => await ResumeCommand(args[1..]),
                "artifacts" => await ArtifactsCommand(args[1..]),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}\n{ex.StackTrace}");
            return 1;
        }
    }

    private static async Task<int> RunCommand(string[] args)
    {
        var spec = ParseStringArg(args, "--spec") ?? "";
        if (string.IsNullOrWhiteSpace(spec))
        { Console.Error.WriteLine("Error: --spec is required for 'run'"); return 1; }

        var (orchestrator, stateStore) = BuildOrchestrator();
        var sessionId = SessionId.New();
        var state = SessionState.Create(sessionId, CreateProfile(sessionId),
            new InitialRequest { Prompt = spec, Language = "C#", Framework = ".NET 10" });
        state = state.WithDependencyGraph(BuildGraph());
        await stateStore.SaveSessionAsync(sessionId, state);
        Console.Error.WriteLine($"[posit] session {sessionId} starting: {spec[..Math.Min(80, spec.Length)]}...");
        state = await orchestrator.RunAsync(state);
        Console.Error.WriteLine($"[posit] session {sessionId} finished: {state.Status}");

        if (state.Status != SessionStatus.Completed)
            return 1;

        // Auto-run Docker harness after pipeline completes, with wiring retry loop.
        // If Docker build fails on Wire.cs, feed the compile error back to the
        // wiring model and re-run csharp-implementation, up to 3 times.
        Console.Error.WriteLine($"[posit] auto-launching Docker harness for {sessionId}...");
        var gateway = new OllamaModelGateway(new HttpClient());
        var harness = new BotHarness(new ArtifactRepository());
        var result = await harness.RunAsync(sessionId);
        Console.Error.WriteLine($"[harness] success={result.Success} tests={result.Results.Length}");
        if (result.Error is not null) Console.Error.WriteLine($"[harness] error: {result.Error}");
        if (result.Report is not null)
            Console.Error.WriteLine($"[qa] {result.Report.Summary}");
        foreach (var tc in result.Results)
            Console.Error.WriteLine($"  {tc.Id}: {(tc.Matches ? "PASS" : "FAIL")} — {tc.Output}");

        // Retry loop: build failures → WireFixer (C# wiring).
        // Test failures → ImplFixer directly (WireFixer only knows Wire.cs; a
        // logic bug it cannot fix just burns the retry budget — observed in T8:
        // 2 WireFixer calls on 'ERROR: 0' before ImplFixer ever saw the failure).
        // Capped at 2 retries each. Restart after that.
        const int maxRetries = 2;
        for (var retry = 0; retry < maxRetries && !result.Success; retry++)
        {
            var isBuildFailure = IsDockerBuildFailure(result);
            var isTestFailure = !isBuildFailure && result.Results.Any(r => !r.Matches);

            if (!isBuildFailure && !isTestFailure) break;

            if (isTestFailure)
            {
                // Logic bug — skip WireFixer entirely, break to ImplFixer loop.
                Console.Error.WriteLine("[harness] Test failures (build OK) — routing to ImplFixer (WireFixer can't fix logic)");
                break;
            }

            var fixInstructions = new List<string> { "Wire.cs compile errors:" };
            fixInstructions.AddRange(ExtractCompileErrors(result.Error ?? "Docker build failed"));
            Console.Error.WriteLine($"[harness] Docker build failed — calling WireFixer ({retry + 1}/{maxRetries})...");

            foreach (var fi in fixInstructions)
                Console.Error.WriteLine($"  {fi}");

            // WireFixer: fix the C# wiring
            var prevWire = await ExtractPreviousWireCsAsync(result.TempDir, sessionId);
            if (string.IsNullOrWhiteSpace(prevWire))
            {
                Console.Error.WriteLine("[harness] No previous Wire.cs found — cannot fix, falling back to full re-run");
                break;
            }

            var fixer = new WireFixer(gateway);
            var translatedTypes = await ExtractTranslatedCSharpAsync(sessionId);
            var fixContext = new PhaseContext
            {
                SessionId = sessionId,
                PhaseId = new PhaseId("wire-fix"),
                Prompt = new PromptTemplate
                {
                    PhaseId = new PhaseId("wire-fix"), Version = new PromptVersion("1.0.0"),
                    SystemPrompt = "", OutputFormatSpec = "C# code",
                    ModelTier = ModelTier.Fast, Temperature = 0.1, MaxOutputTokens = 4096,
                    OutputFormat = OutputFormat.PlainText, OutputSchemaRef = "WireCs",
                    Status = PromptStatus.Active
                },
                ModelRoute = GetModelForFixer(),
                BudgetRemaining = new BudgetRemaining { Amount = 10m, Cap = 10m }
            };
            var fixedWire = await fixer.FixAsync(prevWire, fixInstructions.ToArray(), translatedTypes, fixContext);

            if (string.IsNullOrWhiteSpace(fixedWire))
            {
                Console.Error.WriteLine("[harness] WireFixer returned empty — cannot fix");
                break;
            }

            await UpdateWireCsInDbAsync(sessionId, fixedWire, new ArtifactRepository());

            // Re-run harness with the fix applied (from DB)
            result = await harness.RunAsync(sessionId);
            Console.Error.WriteLine($"[harness] retry {retry + 1}: success={result.Success} tests={result.Results.Length}");
            if (result.Report is not null)
                Console.Error.WriteLine($"[qa] {result.Report.Summary}");
            if (result.Error is not null && IsDockerBuildFailure(result))
            {
                var newErrors = ExtractCompileErrors(result.Error);
                foreach (var ne in newErrors)
                    Console.Error.WriteLine($"  {ne}");
            }
            else if (result.Error is not null)
                Console.Error.WriteLine($"[harness] error: {result.Error}");
            foreach (var tc in result.Results)
                Console.Error.WriteLine($"  {tc.Id}: {(tc.Matches ? "PASS" : "FAIL")} — {tc.Output}");
        }

        // ── Implementation correction loop ──────────────────────────────────
        // If WireFixer couldn't fix the test failures, the bug is in the component
        // implementation, not the wiring. Feed the test failures back to the model
        // to regenerate the failing component code.
        // Capped at 2 retries — cheaper than a full restart, but if the logic is
        // still wrong after 2, restart with fresh architecture.
        const int maxImplRetries = 2;
        for (var implRetry = 0; implRetry < maxImplRetries && !result.Success; implRetry++)
        {
            // Only engage if there are test failures (not build failures)
            if (IsDockerBuildFailure(result)) break;
            var testFailures = result.Results.Where(r => !r.Matches).ToArray();
            if (testFailures.Length == 0) break;

            Console.Error.WriteLine($"[impl-fixer] WireFixer couldn't fix — feeding test failures to implementation ({implRetry + 1}/{maxImplRetries})...");

            // Get the architecture contract (interfaces + test cases)
            var repo = new ArtifactRepository();
            var artifacts = await repo.ListBySessionAsync(sessionId);
            var contractArtifact = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.ArchitectureContract);
            var bundleArtifact = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.SourceCodeBundle);
            if (contractArtifact == null || bundleArtifact == null)
            {
                Console.Error.WriteLine("[impl-fixer] No contract or bundle found — cannot fix");
                break;
            }

            var contract = Deserialize<ArchitectureContract>(contractArtifact.PayloadJson);
            var bundle = Deserialize<SourceCodeBundle>(bundleArtifact.PayloadJson);
            if (contract == null || bundle == null) break;

            // Snapshot for the regression gate: if this fix round makes the score
            // worse, we restore this payload and the previous harness result.
            var previousPayload = bundleArtifact.PayloadJson;
            var previousResult = result;
            var previousPasses = result.Results.Count(r => r.Matches);

            // Rich failure table: per test case — the INPUT fed, the EXACT expected
            // output, the actual output, exit codes. The architect's answer key
            // (Phase A) carries input/expected; the harness result carries actual.
            // Without the input column the model cannot trace which data broke.
            var cliCompForFix = contract.Components.FirstOrDefault(c => c.Connections.Length > 0);
            var contractTcById = (cliCompForFix?.TestCases ?? [])
                .Concat(contract.Components.SelectMany(c => c.TestCases))
                .GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
            var failureReport = new List<string>();
            foreach (var tc in testFailures)
            {
                failureReport.Add($"Test '{tc.Id}' ({tc.Name}):");
                // FedInput = what the harness ACTUALLY fed the program this run
                // (pseudodata content / stdin payload). The contract's tc.Input is
                // intent — if it differs from FedInput, the bot's data won.
                if (contractTcById.TryGetValue(tc.Id, out var ct) && !string.IsNullOrWhiteSpace(ct.Input))
                {
                    failureReport.Add($"  Input actually fed to program: '{tc.FedInput}'");
                    if (tc.FedInput != ct.Input)
                        failureReport.Add($"  (NOTE: architect's intended input was '{ct.Input}' — pseudodata differed. Debug against the input actually fed.)");
                    failureReport.Add($"  Expected stdout: '{(!string.IsNullOrEmpty(ct.ExpectedOutput) ? ct.ExpectedOutput : tc.Expected)}'");
                    failureReport.Add($"  Expected exit code: {ct.ExpectedExitCode}");
                }
                else
                {
                    failureReport.Add($"  Input actually fed to program: '{tc.FedInput}'");
                    failureReport.Add($"  Expected stdout: '{tc.Expected}'");
                }
                failureReport.Add($"  Actual stdout: '{tc.Output}'");
                failureReport.Add($"  Verdict: {(tc.Verdict != null ? $"{tc.Verdict.Layer}: {tc.Verdict.Reason}" : "failed")}");
            }

            // Regenerate each logic component with test failure feedback
            var updatedFiles = bundle.Files.ToDictionary(f => f.Path, f => f.Content);
            var anyUpdated = false;

            foreach (var comp in contract.Components)
            {
                if (comp.Classification == ModuleClassification.IoShell) continue;
                if (string.IsNullOrWhiteSpace(comp.CSharpInterface)) continue;

                var compPath = $"{comp.Name}/{comp.Name}.cs";
                if (!updatedFiles.TryGetValue(compPath, out var currentCode)) continue;

                Console.Error.WriteLine($"[impl-fixer] Regenerating {comp.Name}...");

                // Retry diversity: attempt 2+ must not resubmit the same logic.
                // Raise temperature and demand a different approach — same prompt
                // + same model + same temp = same output (observed: identical
                // 675-char regenerations in T8).
                var diversityNote = implRetry > 0
                    ? $"""

                    ATTEMPT {implRetry + 1} — DIVERSITY REQUIRED:
                    Your previous fix did NOT resolve these failures. Do NOT submit
                    the same logic again with cosmetic changes. Take a DIFFERENT
                    algorithmic approach: re-read the test input format, reconsider
                    how you parse and transform the data.
                    """
                    : "";

                var fixPrompt = $"""
                    You are a Senior C# Developer. Your implementation has a bug — it produces wrong output.

                    ORIGINAL SPEC:
                    {spec}

                    INTERFACE (implement this — match every method signature exactly):
                    {comp.CSharpInterface}

                    RESPONSIBILITY:
                    {comp.Responsibility}

                    TEST FAILURES (your code produces wrong output — fix the logic):
                    {string.Join("\n", failureReport)}

                    YOUR CURRENT CODE (has a bug — find and fix it):
                    {currentCode}

                    RULES:
                    1. Keep the same class name, namespace, and interface implementation.
                    2. Fix the logic that produces wrong output.
                    3. Do NOT modify the interface.
                    4. Output ONLY the C# class file — no markdown fences, no explanations.
                    5. Include `using` directives at the top.
                    6. Put the class in `namespace {comp.Name}`.
                    7. Re-read the ORIGINAL SPEC carefully — make sure your input parsing matches the spec's input format exactly.{diversityNote}
                    """;

                // Retry diversity: higher temperature on attempt 2+ — same prompt
                // + same model + same temp = same output (observed in T8: two
                // identical 675-char regenerations).
                var fixTemperature = 0.1; // targeted fixes, not creative leaps
                var fixPromptTemplate = new PromptTemplate
                {
                    PhaseId = new PhaseId("impl-fix"), Version = new PromptVersion("1.0.0"),
                    SystemPrompt = fixPrompt, OutputFormatSpec = "raw C# source code",
                    ModelTier = ModelTier.Fast, Temperature = fixTemperature, MaxOutputTokens = 8192,
                    OutputFormat = OutputFormat.PlainText, OutputSchemaRef = "CSharpSource",
                    Status = PromptStatus.Active
                };
                // ImplFixer seat: glm-5.2:cloud (Phase F roster) — stronger at
                // reasoning about WHY a test failed (diagnosis, not just code
                // generation). Low frequency (0-2 calls/trial), free (no token
                // billing), already installed. 16K gives thinking room.
                var fixModelRoute = new ModelRoute { Tier = ModelTier.Standard, ProviderId = "ollama",
                    ModelId = "glm-5.2:cloud", MaxOutputTokens = 16384, Temperature = fixTemperature };
                var fixResult = await gateway.GenerateAsync(
                    fixModelRoute,
                    fixPromptTemplate,
                    new PhaseContext
                    {
                        SessionId = sessionId, PhaseId = new PhaseId("impl-fix"),
                        Prompt = fixPromptTemplate,
                        // Full fix context goes in the USER prompt — the channel
                        // the gateway sends as the actual user turn. Leaving it
                        // null made userLen=132 ("Respond according to the system
                        // instructions") while the context sat in the system
                        // prompt. The "═══" marker routes it verbatim.
                        UserRequest = fixPrompt,
                        ModelRoute = fixModelRoute,
                        BudgetRemaining = new BudgetRemaining { Amount = 10m, Cap = 10m }
                    },
                    CancellationToken.None);

                var newCode = ExtractCSharpFromModel(fixResult.Text);
                if (!string.IsNullOrWhiteSpace(newCode))
                {
                    updatedFiles[compPath] = newCode;
                    anyUpdated = true;
                    Console.Error.WriteLine($"[impl-fixer] Updated {compPath} ({newCode.Length} chars)");
                }
                else
                {
                    Console.Error.WriteLine($"[impl-fixer] Model returned empty for {comp.Name}");
                }
            }

            if (!anyUpdated)
            {
                Console.Error.WriteLine("[impl-fixer] No components updated — stopping");
                break;
            }

            // Update the SourceCodeBundle in DB
            var newBundle = new SourceCodeBundle
            {
                Files = updatedFiles.Select(kv => new SourceCodeFile(kv.Key, kv.Value)).ToArray(),
                ProjectPath = bundle.ProjectPath,
                TargetFramework = bundle.TargetFramework
            };
            var newPayload = JsonSerializer.SerializeToUtf8Bytes(newBundle, PositJson.Options);
            await repo.StageAsync(bundleArtifact with { PayloadJson = newPayload });

            // Re-run harness
            result = await harness.RunAsync(sessionId);
            Console.Error.WriteLine($"[impl-fixer] retry {implRetry + 1}: success={result.Success} tests={result.Results.Length}");
            if (result.Report is not null)
                Console.Error.WriteLine($"[qa] {result.Report.Summary}");
            foreach (var tc in result.Results)
                Console.Error.WriteLine($"  {tc.Id}: {(tc.Matches ? "PASS" : "FAIL")} — {tc.Output}");

            // Regression gate: a fix that makes the score worse is auto-reverted.
            // Fixers must never leave the pipeline worse than they found it.
            var newPasses = result.Results.Count(r => r.Matches);
            if (newPasses < previousPasses)
            {
                Console.Error.WriteLine($"[impl-fixer] REGRESSION: {previousPasses}→{newPasses} passing — reverting fix and stopping loop");
                await repo.StageAsync(bundleArtifact with { PayloadJson = previousPayload });
                result = previousResult;
                break;
            }
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<int> HarnessCommand(string[] args)
    {
        var id = args.Length > 0 ? args[0] : "";
        if (string.IsNullOrWhiteSpace(id))
        { Console.Error.WriteLine("Error: sessionId is required for 'harness'"); return 1; }

        var gateway = new OllamaModelGateway(new HttpClient());
        var harness = new BotHarness(new ArtifactRepository());
        var result = await harness.RunAsync(new SessionId(id));
        Console.Error.WriteLine($"[harness] success={result.Success} tests={result.Results.Length}");
        if (result.Error is not null) Console.Error.WriteLine($"[harness] error: {result.Error}");
        if (result.Report is not null)
            Console.Error.WriteLine($"[qa] {result.Report.Summary}");
        foreach (var tc in result.Results)
            Console.Error.WriteLine($"  {tc.Id}: {(tc.Matches ? "PASS" : "FAIL")} — {tc.Output}");
        return result.Success ? 0 : 1;
    }

    private static async Task<int> StatusCommand()
    {
        var sessions = await new StateStore().ListAllSessionsAsync();
        if (sessions.Length == 0) { Console.WriteLine("No sessions found."); return 0; }
        Console.WriteLine($"{"Session",-30} {"Status",-12} {"Phase",-22} {"Att",-4} {"Done",-4}");
        Console.WriteLine(new string('-', 75));
        foreach (var s in sessions)
            Console.WriteLine(
                $"{s.SessionId.Value,-30} {s.Status,-12} {s.CurrentPhaseId?.Value ?? "-",-22} {s.CurrentAttempt,-4} {s.CompletedPhases.Length,-4}");
        return 0;
    }

    private static async Task<int> ResumeCommand(string[] args)
    {
        var id = args.Length > 0 ? args[0] : "";
        if (string.IsNullOrWhiteSpace(id))
        { Console.Error.WriteLine("Error: sessionId is required for 'resume'"); return 1; }

        var (orchestrator, stateStore) = BuildOrchestrator();
        var state = await stateStore.LoadSessionAsync(new SessionId(id));
        if (state is null) { Console.Error.WriteLine($"Session not found: {id}"); return 1; }
        state = state.WithStatus(SessionStatus.Planning);
        Console.Error.WriteLine($"[posit] resuming session {id}");
        state = await orchestrator.RunAsync(state);
        Console.Error.WriteLine($"[posit] session {id} finished: {state.Status}");
        return state.Status == SessionStatus.Completed ? 0 : 1;
    }

    private static async Task<int> ArtifactsCommand(string[] args)
    {
        var id = args.Length > 0 ? args[0] : "";
        if (string.IsNullOrWhiteSpace(id))
        { Console.Error.WriteLine("Error: sessionId is required for 'artifacts'"); return 1; }

        var artifacts = await new ArtifactRepository().ListBySessionAsync(new SessionId(id));
        if (artifacts.Length == 0) { Console.WriteLine($"No artifacts for session {id}"); return 0; }
        Console.WriteLine($"Artifacts for session {id} ({artifacts.Length} total):");
        foreach (var a in artifacts)
            Console.WriteLine($"  [{a.SourcePhase.Value,-22}] {a.Kind,-22} {a.SchemaVersion} {a.ProducedAt:O}");
        return 0;
    }

    private static (PositOrchestrator, StateStore) BuildOrchestrator()
    {
        var gateway = new OllamaModelGateway(new HttpClient());
        var registry = new PatternRegistry(@"C:\Users\goldf\Posit\patterns");
        var adapter = new PatternRegistryAdapter(registry);
        var artifactRepo = new ArtifactRepository();
        var stateStore = new StateStore();
        var fsm = new FsmReducer();
        var graph = new DependencyGraphEngine();

        var controller = new PhaseController();
        controller.Register(new ArchitecturePhase(gateway, registry));
        controller.Register(new CSharpImplementationPhase(gateway, adapter));
        controller.Register(new QaPhase());

        return (new PositOrchestrator(controller, fsm, graph, artifactRepo, stateStore, registry), stateStore);
    }

    private static ProjectProfile CreateProfile(SessionId sessionId) => new()
    {
        Id = new ProjectId($"posit-{sessionId.Value[..8]}"),
        Name = "Posit Run",
        Phases = [KnownPhases.Architecture,
                  KnownPhases.CSharpImplementation, KnownPhases.Qa],
        MaxRetriesPerPhase = 5,
        Budget = new BudgetRemaining { Amount = 10m, Cap = 10m },
        Approvals = new ApprovalConfig
        {
            TimeoutPolicy = GateTimeoutPolicy.AutoApprove,
            GateTimeout = TimeSpan.FromMinutes(5)
        }
    };

    private static DependencyGraph BuildGraph() => new DependencyGraphEngine().Build(
        [KnownPhases.Architecture,
         KnownPhases.CSharpImplementation, KnownPhases.Qa],
        [[], [KnownPhases.Architecture], [KnownPhases.CSharpImplementation]]);

    private static string? ParseStringArg(string[] args, string name)
    {
        foreach (var arg in args)
            if (arg.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
                return arg[(name.Length + 1)..].Trim('"');
        return null;
    }

    private static int UnknownCommand(string command)
    { Console.Error.WriteLine($"Unknown command: {command}"); PrintUsage(); return 1; }

    /// <summary>
    /// Check if the harness failure is a Docker build failure (not a test failure).
    /// Build failures have compile errors in the Error field. Test failures have
    /// Success=false but Error=null (individual tests failed, but build succeeded).
    /// </summary>
    private static bool IsDockerBuildFailure(BotHarnessResult result) =>
        !result.Success && !string.IsNullOrWhiteSpace(result.Error)
        && (result.Error.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains("error CS", StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains("failed to build", StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains("did not complete successfully", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Extract a truncated build error message for logging.
    /// </summary>
    private static string ExtractBuildError(BotHarnessResult result, int maxLen) =>
        result.Error is null ? "" : (result.Error.Length <= maxLen ? result.Error : result.Error[..maxLen] + "...");

    /// <summary>
    /// Extract only the C# compile error lines from a Docker build log.
    /// Filters out Docker progress lines, warnings, and noise.
    /// Returns a string[] of error lines suitable for CorrectionSignal.
    /// </summary>
    private static string[] ExtractCompileErrors(string dockerLog)
    {
        if (string.IsNullOrWhiteSpace(dockerLog)) return new[] { "Docker build failed (no error details)" };
        var lines = dockerLog.Replace("\r\n", "\n").Split('\n');
        var errors = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Strip Docker step prefixes like "#11 7.063 "
            var stripped = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^#\d+\s+\d+\.\d+\s+", "");
            // Keep only error CS lines (compile errors)
            if (stripped.Contains("error CS", StringComparison.OrdinalIgnoreCase))
                errors.Add(stripped);
            // Also keep "Build FAILED" as a summary line
            if (stripped.Equals("Build FAILED.", StringComparison.OrdinalIgnoreCase))
                errors.Add(stripped);
        }
        if (errors.Count == 0)
            return new[] { "Docker build failed. Wire.cs has compile errors. Check the C# syntax." };
        // Limit to 10 errors to avoid overwhelming the model
        return errors.Take(10).ToArray();
    }

    /// <summary>
    /// Extract the previous Wire.cs content. Tries the harness temp directory first
    /// (fast path — files are on disk). Falls back to the DB SourceCodeBundle if the
    /// temp dir is gone (e.g. harness already cleaned up or re-ran).
    /// </summary>
    private static async Task<string?> ExtractPreviousWireCsAsync(string? tempDir, SessionId sessionId)
    {
        // Fast path: read from temp dir if it still exists
        if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
        {
            var wireFiles = Directory.GetFiles(tempDir, "Wire.cs", SearchOption.AllDirectories);
            if (wireFiles.Length > 0)
            {
                try { return File.ReadAllText(wireFiles[0]); }
                catch { /* fall through to DB */ }
            }
        }

        // Fallback: read from DB SourceCodeBundle
        try
        {
            var repo = new ArtifactRepository();
            var artifacts = await repo.ListBySessionAsync(sessionId);
            var bundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.SourceCodeBundle);
            if (bundle == null) return null;
            var sourceCode = Deserialize<SourceCodeBundle>(bundle.PayloadJson);
            if (sourceCode == null) return null;
            var wireFile = sourceCode.Files.FirstOrDefault(f => f.Path.EndsWith("Wire.cs"));
            return wireFile?.Content;
        }
        catch { return null; }
    }

    /// <summary>
    /// Extract the translated C# type definitions (non-Wire.cs files) from the
    /// SourceCodeBundle. The WireFixer needs these to see the actual property names
    /// on translated types (e.g. _IConversionResult.isValid vs IsValid).
    /// Returns the concatenated C# source of all non-Wire.cs files, truncated to 4000 chars.
    /// </summary>
    private static async Task<string?> ExtractTranslatedCSharpAsync(SessionId sessionId)
    {
        try
        {
            var repo = new ArtifactRepository();
            var artifacts = await repo.ListBySessionAsync(sessionId);
            var bundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.SourceCodeBundle);
            if (bundle == null) return null;
            var sourceCode = Deserialize<SourceCodeBundle>(bundle.PayloadJson);
            if (sourceCode == null) return null;

            var sb = new System.Text.StringBuilder();
            foreach (var f in sourceCode.Files)
            {
                if (f.Path.EndsWith("Wire.cs")) continue; // skip the file being fixed
                if (string.IsNullOrWhiteSpace(f.Content)) continue;
                sb.AppendLine($"// === {f.Path} ===");
                sb.AppendLine(f.Content);
                sb.AppendLine();
            }
            var result = sb.ToString().Trim();
            if (string.IsNullOrEmpty(result)) return null;
            // Truncate to avoid drowning the model — it needs the type definitions, not every line
            if (result.Length > 4000)
                result = result[..4000] + "\n// ... (truncated — see type definitions above)";
            return result;
        }
        catch { return null; }
    }

    /// <summary>
    /// Find the file path of Wire.cs in the harness temp directory.
    /// </summary>
    private static string? FindWireCsPath(string? tempDir)
    {
        if (string.IsNullOrWhiteSpace(tempDir) || !Directory.Exists(tempDir)) return null;
        var wireFiles = Directory.GetFiles(tempDir, "Wire.cs", SearchOption.AllDirectories);
        return wireFiles.Length > 0 ? wireFiles[0] : null;
    }

    /// <summary>
    /// Model route for the WireFixer — deepseek-v4-flash:cloud (Fast tier).
    /// WireFixer handles compile errors (mechanical, high-frequency) — stays on
    /// flash. The ImplFixer (logic failures, low-frequency reasoning) uses
    /// glm-5.2:cloud via the hardcoded route in the harness section above.
    /// </summary>
    private static ModelRoute GetModelForFixer() => new()
    {
        Tier = ModelTier.Fast, ProviderId = "ollama",
        ModelId = "deepseek-v4-flash:cloud", MaxOutputTokens = 8192, Temperature = 0.0
    };

    /// <summary>
    /// Extract C# code from model output — strips markdown fences if present.
    /// </summary>
    private static string ExtractCSharpFromModel(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (text.Contains("```"))
        {
            var start = text.IndexOf("```");
            var afterFence = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("```", afterFence);
            if (end > afterFence)
                return text[afterFence..end].Trim();
        }
        return text.Trim();
    }

    /// <summary>
    /// Update Wire.cs in the DB's SourceCodeBundle artifact so the harness
    /// picks up the fix on re-run. Reads the existing bundle, replaces Wire.cs
    /// content, re-stages with same ID (ON CONFLICT updates payload).
    /// </summary>
    private static async Task UpdateWireCsInDbAsync(SessionId sessionId, string fixedWireCs, ArtifactRepository repo)
    {
        var artifacts = await repo.ListBySessionAsync(sessionId);
        var bundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.SourceCodeBundle);
        if (bundle == null)
        {
            Console.Error.WriteLine("[harness] No SourceCodeBundle found in DB — cannot update Wire.cs");
            return;
        }

        var sourceCode = Deserialize<SourceCodeBundle>(bundle.PayloadJson);
        if (sourceCode == null) return;

        var updated = false;
        var files = sourceCode.Files.Select(f =>
        {
            if (f.Path.EndsWith("Wire.cs"))
            {
                updated = true;
                return f with { Content = fixedWireCs };
            }
            return f;
        }).ToArray();

        if (!updated)
        {
            Console.Error.WriteLine("[harness] No Wire.cs found in SourceCodeBundle — cannot update");
            return;
        }

        var newBundle = new SourceCodeBundle
        {
            Files = files,
            ProjectPath = sourceCode.ProjectPath,
            TargetFramework = sourceCode.TargetFramework
        };
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(newBundle, PositJson.Options);

        // Re-stage with same ID — ON CONFLICT updates payload_json
        await repo.StageAsync(bundle with { PayloadJson = payloadJson });
    }

    private static T? Deserialize<T>(byte[] payload) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payload, PositJson.Options); }
        catch { return null; }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: posit <command> [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine("  run --spec=\"<spec text>\"   Run the full pipeline on a spec");
        Console.Error.WriteLine("  harness <sessionId>         Run the bot harness on a session");
        Console.Error.WriteLine("  status                      List all sessions");
        Console.Error.WriteLine("  resume <sessionId>         Resume a paused/failed session");
        Console.Error.WriteLine("  artifacts <sessionId>       List artifacts for a session");
    }
}