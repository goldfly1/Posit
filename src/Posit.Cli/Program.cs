using Posit.Cli.Orchestration;

namespace Posit.Cli;

/// <summary>Adapter from PatternRegistry to IPatternRegistry.</summary>
internal sealed class PatternRegistryAdapter : IPatternRegistry
{
    private readonly PatternRegistry _inner;
    public PatternRegistryAdapter(PatternRegistry inner) => _inner = inner;

    public string PatternsDirectory => "patterns";
    public Dictionary<string, string> CSharpStubs =>
        _inner.GetAllCSharpStubs().ToDictionary(s => s.Name, s => s.Template);
    public bool HasPattern(string name) => _inner.HasPattern(name);
    public string GetPattern(string name) => _inner.GetPattern(name)?.Body ?? "";
    public string[] GetPatternSignatures(string patternName) =>
        _inner.GetPatternSignatures(patternName).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    public MethodSignature[] ExtractMethodSignatures(string patternName) => [];
    public string[] SelectCSharpStubs(string[] stubNames) =>
        [.. _inner.SelectCSharpStubs("", stubNames).Select(s => s.Name)];
    public string ComposeSkeleton(string patternName, string[] stubNames, string? parametersJson) =>
        _inner.ComposeSkeleton(patternName, stubNames, parametersJson ?? patternName);
    public string ComposeIoShellSkeleton(string stubName, string componentName) =>
        _inner.ComposeIoShellSkeleton([stubName], componentName);
    public (string Name, string Responsibility)[] GetAllPatterns() =>
        _inner.GetAllPatterns().Select(p => (p.Name, p.Responsibility ?? "")).ToArray();
    public string[] MaterializeDependencies(string patternName, string stagingDir)
    { _inner.MaterializeDependencies(stagingDir, patternName); return []; }
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
        var harness = new BotHarness(new ArtifactRepository(), model: gateway);
        var result = await harness.RunAsync(sessionId);
        Console.Error.WriteLine($"[harness] success={result.Success} tests={result.Results.Length}");
        if (result.Error is not null) Console.Error.WriteLine($"[harness] error: {result.Error}");
        foreach (var tc in result.Results)
            Console.Error.WriteLine($"  {tc.Id}: {(tc.Matches ? "PASS" : "FAIL")} — {tc.Output}");

        // Wiring retry loop: if Docker build failed, call the WireFixer —
        // a dedicated agent that gets ONLY the compile errors + Wire.cs + ISequence API.
        // Like a plumber: doesn't redesign the building, just fixes the leaking pipe.
        const int maxWiringRetries = 3;
        for (var retry = 0; retry < maxWiringRetries && !result.Success && IsDockerBuildFailure(result); retry++)
        {
            var compileErrors = ExtractCompileErrors(result.Error ?? "Docker build failed");
            Console.Error.WriteLine($"[harness] Docker build failed — calling WireFixer ({retry + 1}/{maxWiringRetries})...");
            foreach (var ce in compileErrors)
                Console.Error.WriteLine($"  {ce}");

            var prevWire = ExtractPreviousWireCs(result.TempDir);
            if (string.IsNullOrWhiteSpace(prevWire))
            {
                Console.Error.WriteLine("[harness] No previous Wire.cs found — cannot fix, falling back to full re-run");
                break;
            }

            // Call the dedicated fixer — gets ONLY the errors + Wire.cs + API reference
            var fixer = new WireFixer(gateway);
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
            var fixedWire = await fixer.FixAsync(prevWire, compileErrors, fixContext);

            if (string.IsNullOrWhiteSpace(fixedWire))
            {
                Console.Error.WriteLine("[harness] WireFixer returned empty — cannot fix");
                break;
            }

            // Write the fixed Wire.cs back to the DB so the harness picks it up
            // on re-run (the harness re-materializes from DB every time).
            await UpdateWireCsInDbAsync(sessionId, fixedWire, new ArtifactRepository());

            // Re-run harness with the fixed Wire.cs (from DB)
            result = await harness.RunAsync(sessionId);
            Console.Error.WriteLine($"[harness] retry {retry + 1}: success={result.Success} tests={result.Results.Length}");
            if (result.Error is not null && IsDockerBuildFailure(result))
            {
                // Still a build failure — show the new errors for the next iteration
                var newErrors = ExtractCompileErrors(result.Error);
                foreach (var ne in newErrors)
                    Console.Error.WriteLine($"  {ne}");
            }
            else if (result.Error is not null)
                Console.Error.WriteLine($"[harness] error: {result.Error}");
            foreach (var tc in result.Results)
                Console.Error.WriteLine($"  {tc.Id}: {(tc.Matches ? "PASS" : "FAIL")} — {tc.Output}");
        }

        return result.Success ? 0 : 1;
    }

    private static async Task<int> HarnessCommand(string[] args)
    {
        var id = args.Length > 0 ? args[0] : "";
        if (string.IsNullOrWhiteSpace(id))
        { Console.Error.WriteLine("Error: sessionId is required for 'harness'"); return 1; }

        var gateway = new OllamaModelGateway(new HttpClient());
        var harness = new BotHarness(new ArtifactRepository(), model: gateway);
        var result = await harness.RunAsync(new SessionId(id));
        Console.Error.WriteLine($"[harness] success={result.Success} tests={result.Results.Length}");
        if (result.Error is not null) Console.Error.WriteLine($"[harness] error: {result.Error}");
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
        var z3 = new Z3Runner(
            @"C:\Users\goldf\.dotnet\tools\dafny.exe",
            @"C:\Users\goldf\.dotnet\tools\z3\bin\z3.exe");
        var registry = new PatternRegistry(@"C:\Users\goldf\Posit\patterns");
        var adapter = new PatternRegistryAdapter(registry);
        var artifactRepo = new ArtifactRepository();
        var stateStore = new StateStore();
        var fsm = new FsmReducer();
        var graph = new DependencyGraphEngine();

        var controller = new PhaseController();
        controller.Register(new ArchitecturePhase(gateway, registry));
        controller.Register(new DafnyContractsPhase(z3));
        controller.Register(new DafnyImplementationPhase(z3, gateway));
        controller.Register(new CSharpImplementationPhase(gateway, adapter));
        controller.Register(new QaPhase(gateway, adapter));

        return (new PositOrchestrator(controller, fsm, graph, artifactRepo, stateStore, registry), stateStore);
    }

    private static ProjectProfile CreateProfile(SessionId sessionId) => new()
    {
        Id = new ProjectId($"posit-{sessionId.Value[..8]}"),
        Name = "Posit Run",
        Phases = [KnownPhases.Architecture, KnownPhases.DafnyContracts,
                  KnownPhases.DafnyImplementation, KnownPhases.CSharpImplementation, KnownPhases.Qa],
        MaxRetriesPerPhase = 5,
        Budget = new BudgetRemaining { Amount = 10m, Cap = 10m },
        Approvals = new ApprovalConfig
        {
            TimeoutPolicy = GateTimeoutPolicy.AutoApprove,
            GateTimeout = TimeSpan.FromMinutes(5)
        }
    };

    private static DependencyGraph BuildGraph() => new DependencyGraphEngine().Build(
        [KnownPhases.Architecture, KnownPhases.DafnyContracts,
         KnownPhases.DafnyImplementation, KnownPhases.CSharpImplementation, KnownPhases.Qa],
        [[], [KnownPhases.Architecture], [KnownPhases.DafnyContracts],
         [KnownPhases.DafnyImplementation], [KnownPhases.CSharpImplementation]]);

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
            return new[] { "Docker build failed. Wire.cs has compile errors. Check the C# syntax and Dafny runtime API usage." };
        // Limit to 10 errors to avoid overwhelming the model
        return errors.Take(10).ToArray();
    }

    /// <summary>
    /// Extract the previous Wire.cs content from the harness temp directory.
    /// </summary>
    private static string? ExtractPreviousWireCs(string? tempDir)
    {
        if (string.IsNullOrWhiteSpace(tempDir) || !Directory.Exists(tempDir)) return null;
        var wireFiles = Directory.GetFiles(tempDir, "Wire.cs", SearchOption.AllDirectories);
        if (wireFiles.Length == 0) return null;
        try { return File.ReadAllText(wireFiles[0]); }
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
    /// Model route for the WireFixer — same model as the rest of the pipeline.
    /// </summary>
    private static ModelRoute GetModelForFixer() => new()
    {
        Tier = ModelTier.Fast, ProviderId = "ollama",
        ModelId = "deepseek-v4-flash:cloud", MaxOutputTokens = 4096, Temperature = 0.1
    };

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