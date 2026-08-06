using System.Net.Http;
using Posit.AI.Models;
using Posit.Cli.Orchestration;
using Posit.Core.Graph;
using Posit.Core.State;
using Posit.Phases;
using Posit.Tools;

// === Posit CLI — spec compiler pipeline ===
// Usage: posit run "build a CSV parser" [--phase=architecture,dafny-contracts,...]
//        posit status <session-id>
//        posit artifacts <session-id>

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (cliArgs.Length == 0)
{
    Console.Error.WriteLine("Posit — a spec compiler. Nothing ships unproven.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  posit run <request> [--phase=<phases>]     Run the pipeline");
    Console.Error.WriteLine("  posit status <session-id>                  Show session status");
    Console.Error.WriteLine("  posit artifacts <session-id>               List artifacts");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Phases: ideation, architecture, dafny-contracts, dafny-implementation,");
    Console.Error.WriteLine("        csharp-implementation, qa");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Models (via Ollama on localhost:11434):");
    Console.Error.WriteLine("  deepseek-v4-pro:cloud  — Architecture, Dafny Implementation");
    Console.Error.WriteLine("  kimi-2.7-code:cloud    — Design Review, Imp Appeal");
    Console.Error.WriteLine("  glm-5.2:cloud          — C# Implementation, QA");
    return 0;
}

var command = cliArgs[0];

try
{
    return command switch
    {
        "run" => await RunCommand(args.Skip(1).ToArray()),
        "status" => StatusCommand(args.Skip(1).ToArray()),
        "artifacts" => ArtifactsCommand(args.Skip(1).ToArray()),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[Posit] FATAL: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    Console.Error.WriteLine("Use: posit run <request> | posit status <id> | posit artifacts <id>");
    return 1;
}

static int StatusCommand(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("Usage: posit status <session-id>");
        return 1;
    }

    // Session state is in-memory only for now — would need DB persistence for real use
    Console.Error.WriteLine("[Posit] Session state is in-memory. Status requires a running session.");
    Console.Error.WriteLine($"Requested session: {args[0]}");
    return 0;
}

static int ArtifactsCommand(string[] args)
{
    if (args.Length < 1)
    {
        Console.Error.WriteLine("Usage: posit artifacts <session-id>");
        return 1;
    }

    Console.Error.WriteLine("[Posit] Session artifacts are in-memory. Artifacts require a running session.");
    Console.Error.WriteLine($"Requested session: {args[0]}");
    return 0;
}

static async Task<int> RunCommand(string[] args)
{
    if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
    {
        Console.Error.WriteLine("Usage: posit run <request> [--phase=<phases>]");
        Console.Error.WriteLine("Example: posit run \"Build a CSV parser library\"");
        return 1;
    }

    var request = args[0];
    var phaseArg = args.Skip(1).FirstOrDefault(a => a.StartsWith("--phase="));
    var phases = phaseArg is not null
        ? phaseArg["--phase=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries)
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
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Posit] DB not available (running in-memory): {ex.Message}");
    }

    var phases_impl = new IPhase[]
    {
        new ArchitecturePhase(gateway),
        new DafnyContractsPhase(z3Runner),
        new DafnyImplementationPhase(gateway, z3Runner),
        new CSharpImplementationPhase(gateway),
        new QaPhase(gateway)
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

    var artifacts = orchestrator.GetArtifacts(sessionId);
    Console.Error.WriteLine($"[Posit] Artifacts produced: {artifacts.Count}");
    foreach (var artifact in artifacts)
    {
        Console.Error.WriteLine($"  - {artifact.Kind} from {artifact.SourcePhase.Value} ({artifact.PayloadJson.Length} bytes)");
    }

    return finalState.Status == SessionStatus.Completed ? 0 : 1;
}