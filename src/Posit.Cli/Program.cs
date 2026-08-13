using System.Net.Http;
using Posit.AI.Models;
using Posit.Cli.Orchestration;
using Posit.Core.Graph;
using Posit.Core.State;
using Posit.Phases;
using Posit.Tools;

// === Posit CLI — spec compiler pipeline ===
// Usage: posit run --spec="build a CSV parser" [--phases=architecture,dafny-contracts,...]
//        posit status <session-id>
//        posit artifacts <session-id>

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (cliArgs.Length == 0)
{
    Console.Error.WriteLine("Posit — a spec compiler. Nothing ships unproven.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  posit run --spec=\"<request>\" [--phases=<phases>]   Run the pipeline");
    Console.Error.WriteLine("  posit status <session-id>                          Show session status");
    Console.Error.WriteLine("  posit resume <session-id>                           Resume a failed/unfinished session");
    Console.Error.WriteLine("  posit artifacts <session-id>                        List artifacts");
    Console.Error.WriteLine("  posit verify <session-id>                           Verify C# output in Docker");
    Console.Error.WriteLine("  posit harness <session-id>                          Run bot harness (build + test CLI)");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Phases: architecture, dafny-contracts, dafny-implementation,");
    Console.Error.WriteLine("        csharp-implementation, qa");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Models (via Ollama on localhost:11434):");
    Console.Error.WriteLine("  deepseek-v4-flash:cloud  — All phases (architecture, QA, etc.)");
    Console.Error.WriteLine("  glm-5.2:cloud            — Alternative architect (more consistent pattern selection)");
    return 0;
}

var command = cliArgs[0];

try
{
    return command switch
    {
        "run" => await RunCommand(args.Skip(1).ToArray()),
        "status" => StatusCommand(args.Skip(1).ToArray()),
        "resume" => await ResumeCommand(args.Skip(1).ToArray()),
        "artifacts" => await ArtifactsCommand(args.Skip(1).ToArray()),
        "verify" => await VerifyCommand(args.Skip(1).ToArray()),
        "harness" => await HarnessCommand(args.Skip(1).ToArray()),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[Posit] FATAL: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

static string GetRunPatternsDirectory()
{
    var candidate = Path.Combine(Directory.GetCurrentDirectory(), "patterns");
    if (Directory.Exists(candidate))
        return candidate;

    candidate = Path.Combine(AppContext.BaseDirectory, "patterns");
    if (Directory.Exists(candidate))
        return candidate;

    var assemblyLoc = typeof(Program).Assembly.Location;
    if (!string.IsNullOrEmpty(assemblyLoc))
    {
        var root = Directory.GetParent(assemblyLoc);
        while (root is not null)
        {
            var test = Path.Combine(root.FullName, "patterns");
            if (Directory.Exists(test))
                return test;
            root = root.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "Pattern registry not found. Run from a directory containing a 'patterns' folder, " +
        "or ensure patterns/ is copied to the output directory.");
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    Console.Error.WriteLine("Use: posit run <request> | posit status <id> | posit artifacts <id> | posit resume <id> | posit verify <id>");
    return 1;
}

static int StatusCommand(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("Usage: posit status <session-id>");
        return 1;
    }

    var sessionId = new SessionId(args[0]);
    var dataSource = Posit.Data.Configuration.DbConnectionProvider.CreateDataSource();
    var stateStore = new StateStore(dataSource);
    var state = stateStore.LoadSessionAsync(sessionId).GetAwaiter().GetResult();
    if (state is null)
    {
        Console.Error.WriteLine($"[Posit] Session {sessionId.Value} not found.");
        return 1;
    }

    Console.Error.WriteLine($"[Posit] Session {sessionId.Value}:");
    Console.Error.WriteLine($"  Status: {state.Status}");
    Console.Error.WriteLine($"  Current phase: {state.CurrentPhaseId?.Value ?? "(none)"}");
    Console.Error.WriteLine($"  Current attempt: {state.CurrentAttempt}");
    Console.Error.WriteLine($"  Completed phases: {string.Join(", ", state.CompletedPhases.Select(p => p.Value))}");
    return 0;
}

static async Task<int> ResumeCommand(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("Usage: posit resume <session-id>");
        return 1;
    }

    var sessionId = new SessionId(args[0]);
    Console.Error.WriteLine($"[Posit] Resuming session {sessionId.Value}...");

    var http = new HttpClient();
    var gateway = new OllamaModelGateway(http);
    var z3Runner = new Z3Runner();
    var reducer = new FsmReducer();
    var graphEngine = new DependencyGraphEngine();
    var phaseController = new PhaseController();

    var dataSource = Posit.Data.Configuration.DbConnectionProvider.CreateDataSource();
    var migrationRunner = new Posit.Data.Migrations.MigrationRunner(
        dataSource,
        Path.Combine(AppContext.BaseDirectory, "migrations"));
    if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "migrations")))
    {
        migrationRunner = new Posit.Data.Migrations.MigrationRunner(
            dataSource,
            Path.Combine(Directory.GetCurrentDirectory(), "migrations"));
    }

    ArtifactRepository? artifactRepo = null;
    StateStore? stateStore = null;
    try
    {
        await migrationRunner.ApplyAsync();
        artifactRepo = new ArtifactRepository(dataSource);
        stateStore = new StateStore(dataSource);
        PromptLogger.Initialize(dataSource);
        AuditRepository.Initialize(dataSource);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Posit] DB not available: {ex.Message}");
        return 1;
    }

    var phases_impl = new IPhase[]
    {
        new ArchitecturePhase(gateway, new PatternRegistry(GetRunPatternsDirectory())),
        new DafnyContractsPhase(z3Runner),
        new DafnyImplementationPhase(gateway, z3Runner),
        new CSharpImplementationPhase(gateway),
        new QaPhase()
    };

    var orchestrator = new PositOrchestrator(reducer, graphEngine, phaseController, phases_impl, artifactRepo, stateStore);

    var loaded = await orchestrator.ResumeAsync(sessionId);
    if (!loaded)
        return 1;

    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
    var finalState = await orchestrator.RunAsync(sessionId, cts.Token);

    Console.Error.WriteLine();
    Console.Error.WriteLine($"[Posit] === PIPELINE COMPLETE ===");
    Console.Error.WriteLine($"[Posit] Final status: {finalState.Status}");
    Console.Error.WriteLine($"[Posit] Completed phases: {string.Join(", ", finalState.CompletedPhases.Select(p => p.Value))}");
    Console.Error.WriteLine($"[Posit] Total cost: {finalState.RunningCosts.AmountUsd:C}");
    Console.Error.WriteLine($"[Posit] Input tokens: {finalState.RunningCosts.InputTokens:N0}");
    Console.Error.WriteLine($"[Posit] Output tokens: {finalState.RunningCosts.OutputTokens:N0}");

    return finalState.Status == SessionStatus.Completed ? 0 : 1;
}

static async Task<int> ArtifactsCommand(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("Usage: posit artifacts <session-id>");
        return 1;
    }

    var sessionId = new SessionId(args[0]);
    var dataSource = Posit.Data.Configuration.DbConnectionProvider.CreateDataSource();
    var repo = new ArtifactRepository(dataSource);
    var artifacts = await repo.ListBySessionAsync(sessionId);
    Console.Error.WriteLine($"[Posit] Artifacts for session {sessionId.Value}: {artifacts.Length}");
    foreach (var a in artifacts)
    {
        Console.Error.WriteLine($"  - {a.Kind} from {a.SourcePhase.Value} ({a.PayloadJson.Length} bytes) at {a.ProducedAt:O}");
    }
    return 0;
}

static async Task<int> VerifyCommand(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("Usage: posit verify <session-id>");
        return 1;
    }

    var sessionId = new SessionId(args[0]);
    var dataSource = Posit.Data.Configuration.DbConnectionProvider.CreateDataSource();
    var repo = new ArtifactRepository(dataSource);
    var verifier = new DockerVerifier(repo);

    Console.Error.WriteLine($"[Posit] Verifying session {sessionId.Value} in Docker...");
    var (success, output) = await verifier.VerifyAsync(sessionId);
    Console.WriteLine(output);
    Console.Error.WriteLine(success
        ? "[Posit] Docker verification succeeded."
        : "[Posit] Docker verification failed.");
    return success ? 0 : 1;
}

static async Task<int> HarnessCommand(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("Usage: posit harness <session-id>");
        return 1;
    }

    var sessionId = new SessionId(args[0]);
    var dataSource = Posit.Data.Configuration.DbConnectionProvider.CreateDataSource();
    var repo = new ArtifactRepository(dataSource);
    var harness = new BotHarness(repo);

    Console.Error.WriteLine($"[Posit] Bot Harness — testing session {sessionId.Value}...");
    var result = await harness.RunAsync(sessionId);

    Console.Error.WriteLine();
    Console.Error.WriteLine($"=== Bot Harness Result ===");
    Console.Error.WriteLine($"  Success: {result.Success}");
    Console.Error.WriteLine($"  Summary: {result.Summary}");
    Console.Error.WriteLine($"  CLI Component: {result.CliComponent}");
    Console.Error.WriteLine($"  Tests: {result.TestResults.Count}");

    foreach (var tc in result.TestResults)
    {
        var status = tc.Passed ? "PASS" : "FAIL";
        Console.Error.WriteLine($"  [{status}] {tc.Name} (exit={tc.ExitCode}, {tc.ElapsedMs}ms)");
        if (!tc.Passed && !string.IsNullOrEmpty(tc.Error))
            Console.Error.WriteLine($"         error: {tc.Error[..Math.Min(tc.Error.Length, 200)]}");
    }

    return result.Success ? 0 : 1;
}

static async Task<int> RunCommand(string[] args)
{
    // Parse --spec="..." or --spec "..." and --phases=... from args
    string? request = null;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--spec="))
        {
            request = args[i]["--spec=".Length..];
        }
        else if (args[i] == "--spec" && i + 1 < args.Length)
        {
            request = args[++i];
        }
        else if (args[i].StartsWith("--phases="))
        {
            // handled below
        }
        else if (request is null && !args[i].StartsWith("--"))
        {
            // First non-flag argument is the request (backward compat)
            request = args[i];
        }
    }

    if (string.IsNullOrWhiteSpace(request))
    {
        Console.Error.WriteLine("Usage: posit run --spec=\"<request>\" [--phases=<phases>]");
        Console.Error.WriteLine("Example: posit run --spec=\"Build a CSV parser library\"");
        return 1;
    }

    var phaseArg = args.FirstOrDefault(a => a.StartsWith("--phases="));
    var phases = phaseArg is not null
        ? phaseArg["--phases=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new PhaseId(p.Trim())).ToArray()
        : [KnownPhases.Architecture, KnownPhases.DafnyContracts,
           KnownPhases.DafnyImplementation, KnownPhases.CSharpImplementation,
           KnownPhases.Qa];

    Console.Error.WriteLine($"[Posit] Request: {request}");
    Console.Error.WriteLine($"[Posit] Phases: {string.Join(" → ", phases.Select(p => p.Value))}");
    Console.Error.WriteLine();

    // Wire up the pipeline
    var http = new HttpClient();
    var gateway = new OllamaModelGateway(http);
    var z3Runner = new Z3Runner();
    var reducer = new FsmReducer();
    var graphEngine = new DependencyGraphEngine();
    var phaseController = new PhaseController();

    // Database layer — run migrations, then create repos
    var dataSource = Posit.Data.Configuration.DbConnectionProvider.CreateDataSource();
    var migrationRunner = new Posit.Data.Migrations.MigrationRunner(
        dataSource,
        Path.Combine(AppContext.BaseDirectory, "migrations"));
    // Try to find migrations relative to working directory too
    if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "migrations")))
    {
        migrationRunner = new Posit.Data.Migrations.MigrationRunner(
            dataSource,
            Path.Combine(Directory.GetCurrentDirectory(), "migrations"));
    }

    ArtifactRepository? artifactRepo = null;
    StateStore? stateStore = null;
    try
    {
        Console.Error.WriteLine("[Posit] Running migrations...");
        var applied = await migrationRunner.ApplyAsync();
        Console.Error.WriteLine($"[Posit] {applied.Count} migrations applied");
        artifactRepo = new ArtifactRepository(dataSource);
        stateStore = new StateStore(dataSource);
        PromptLogger.Initialize(dataSource);
        AuditRepository.Initialize(dataSource);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Posit] DB not available (running in-memory): {ex.Message}");
    }

    var phases_impl = new IPhase[]
    {
        new ArchitecturePhase(gateway, new PatternRegistry(GetRunPatternsDirectory())),
        new DafnyContractsPhase(z3Runner),
        new DafnyImplementationPhase(gateway, z3Runner),
        new CSharpImplementationPhase(gateway),
        new QaPhase()
    };

    var orchestrator = new PositOrchestrator(reducer, graphEngine, phaseController, phases_impl, artifactRepo, stateStore);

    // Create profile
    var profile = new ProjectProfile
    {
        Id = new ProjectId("posit-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")),
        Name = "Posit Run",
        Phases = phases,
        Budget = new BudgetRemaining { Amount = 1000, Cap = 1000 },
        Approvals = new ApprovalConfig
        {
            TimeoutPolicy = GateTimeoutPolicy.AutoReject,
            GateTimeout = TimeSpan.FromMinutes(10)
        }
    };

    var initialRequest = new InitialRequest
    {
        Prompt = request,
        Language = "C#",
        Framework = ".NET 10"
    };

    // Start session
    var sessionId = await orchestrator.StartSessionAsync(profile, initialRequest);
    Console.Error.WriteLine($"[Posit] Session: {sessionId.Value}");
    Console.Error.WriteLine();

    // Run the pipeline
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
    var finalState = await orchestrator.RunAsync(sessionId, cts.Token);

    // Report results
    Console.Error.WriteLine();
    Console.Error.WriteLine($"[Posit] === PIPELINE COMPLETE ===");
    Console.Error.WriteLine($"[Posit] Final status: {finalState.Status}");
    Console.Error.WriteLine($"[Posit] Completed phases: {string.Join(", ", finalState.CompletedPhases.Select(p => p.Value))}");

    // Cost tracking
    Console.Error.WriteLine($"[Posit] Total cost: {finalState.RunningCosts.AmountUsd:C}");
    Console.Error.WriteLine($"[Posit] Input tokens: {finalState.RunningCosts.InputTokens:N0}");
    Console.Error.WriteLine($"[Posit] Output tokens: {finalState.RunningCosts.OutputTokens:N0}");

    var artifacts = orchestrator.GetArtifacts(sessionId);
    Console.Error.WriteLine($"[Posit] Artifacts produced: {artifacts.Count}");
    foreach (var artifact in artifacts)
    {
        Console.Error.WriteLine($"  - {artifact.Kind} from {artifact.SourcePhase.Value} ({artifact.PayloadJson.Length} bytes)");
    }

    return finalState.Status == SessionStatus.Completed ? 0 : 1;
}