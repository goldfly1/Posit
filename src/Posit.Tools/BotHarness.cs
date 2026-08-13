using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;
using Posit.Contracts.Serialization;
using Posit.Data.Repositories;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Tools;

/// <summary>
/// The bot harness — deterministic CLI test runner. Not an LLM.
///
/// Takes a session ID, materializes the generated C# code, builds it,
/// finds the CLI entry point (Wire.cs → Run(string[] args)), generates
/// test data, runs the CLI, captures output, and compares to expected
/// results from the spec.
///
/// This is the TEST step in the seed → assemble → test → prove → carve loop.
/// The bot is a script: push data, get output, compare. No bleary-eyed humans.
/// </summary>
public sealed class BotHarness
{
    private readonly ArtifactRepository _artifactRepo;
    private readonly string _dockerPath;

    public BotHarness(ArtifactRepository artifactRepo, string? dockerPath = null)
    {
        _artifactRepo = artifactRepo ?? throw new ArgumentNullException(nameof(artifactRepo));
        _dockerPath = dockerPath ?? "docker";
    }

    /// <summary>
    /// Run the bot harness against a session: build the generated code,
    /// find the CLI, push test data through it, capture output, compare.
    /// </summary>
    public async Task<BotHarnessResult> RunAsync(SessionId sessionId, CancellationToken ct = default)
    {
        var contextDir = Path.Combine(Path.GetTempPath(), $"posit-harness-{sessionId.Value}");
        Directory.CreateDirectory(contextDir);

        try
        {
            // 1. Load artifacts from DB
            var (arch, source, tests, dafnyVer) = await LoadArtifactsAsync(sessionId, ct);
            if (source is null)
                return BotHarnessResult.Fail("No SourceCodeBundle artifact found.");
            if (source.Files is null or { Length: 0 })
                return BotHarnessResult.Fail("SourceCodeBundle contains no files.");

            Console.Error.WriteLine($"[Posit] Bot Harness — loaded {source.Files.Length} source files");

            // 2. Find the CLI component
            var cliComponent = FindCliComponent(arch?.Components ?? []);
            if (cliComponent is null)
                return BotHarnessResult.Fail("No CLI component found in architecture contract.");

            Console.Error.WriteLine($"[Posit] Bot Harness — CLI component: {cliComponent.Name}");

            // 3. Materialize all files to disk
            var filesByRelPath = MaterializeFiles(source, dafnyVer, tests, arch, contextDir);

            // 4. Generate project files and solution
            var targetFramework = DetectTargetFramework(filesByRelPath.Values) ?? "net10.0";
            GenerateProjects(contextDir, arch, filesByRelPath, targetFramework);

            // 5. Build in Docker
            Console.Error.WriteLine("[Posit] Bot Harness — building in Docker...");
            var buildResult = await BuildInDockerAsync(contextDir, targetFramework, ct);
            if (!buildResult.Success)
                return BotHarnessResult.Fail($"Build failed:\n{buildResult.Output}");

            Console.Error.WriteLine("[Posit] Bot Harness — build succeeded");

            // 6. Generate test data from the spec
            var testData = GenerateTestData(arch, cliComponent);
            Console.Error.WriteLine($"[Posit] Bot Harness — generated {testData.Count} test cases");

            // 7. Run the CLI with each test case
            var testResults = new List<TestCaseResult>();
            foreach (var testCase in testData)
            {
                Console.Error.WriteLine($"[Posit] Bot Harness — running test: {testCase.Name}");
                var runResult = await RunCliAsync(contextDir, cliComponent, testCase, targetFramework, ct);
                testResults.Add(runResult);
                Console.Error.WriteLine($"[Posit] Bot Harness —   {(runResult.Passed ? "PASS" : "FAIL")} ({runResult.ExitCode}) in {runResult.ElapsedMs}ms");
            }

            // 8. Summary
            var passed = testResults.Count(r => r.Passed);
            var failed = testResults.Count - passed;
            var summary = $"Bot Harness: {passed}/{testResults.Count} passed, {failed} failed";
            Console.Error.WriteLine($"[Posit] Bot Harness — {summary}");

            return new BotHarnessResult
            {
                Success = failed == 0,
                Summary = summary,
                BuildOutput = buildResult.Output,
                TestResults = testResults,
                CliComponent = cliComponent.Name
            };
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("POSIT_HARNESS_KEEP") != "1")
            {
                try { Directory.Delete(contextDir, recursive: true); }
                catch { /* best effort */ }
            }
            else
            {
                Console.Error.WriteLine($"[Posit] Preserved harness context: {contextDir}");
            }
        }
    }

    // === Step 1: Load artifacts from DB ===

    private async Task<(ArchitectureContract? Arch, SourceCodeBundle? Source, TestSuite? Tests, DafnyVerificationResult[]? DafnyVer)> LoadArtifactsAsync(
        SessionId sessionId, CancellationToken ct)
    {
        var artifacts = await _artifactRepo.ListBySessionAsync(sessionId, ct);
        var sourceArtifact = artifacts.LastOrDefault(a => a.Kind == ArtifactKind.SourceCodeBundle);
        var testArtifact = artifacts.LastOrDefault(a => a.Kind == ArtifactKind.TestSuite);
        var dafnyArtifact = artifacts.LastOrDefault(a => a.Kind == ArtifactKind.DafnyVerification);
        var archArtifact = artifacts.LastOrDefault(a => a.Kind == ArtifactKind.ArchitectureContract);

        ArchitectureContract? arch = null;
        if (archArtifact is not null)
            arch = JsonSerializer.Deserialize<ArchitectureContract>(archArtifact.PayloadJson, Options);

        SourceCodeBundle? source = null;
        if (sourceArtifact is not null)
            source = JsonSerializer.Deserialize<SourceCodeBundle>(sourceArtifact.PayloadJson, Options);

        TestSuite? tests = null;
        if (testArtifact is not null)
            tests = JsonSerializer.Deserialize<TestSuite>(testArtifact.PayloadJson, Options);

        DafnyVerificationResult[]? dafnyVer = null;
        if (dafnyArtifact is not null)
            dafnyVer = JsonSerializer.Deserialize<DafnyVerificationResult[]>(dafnyArtifact.PayloadJson, Options);

        return (arch, source, tests, dafnyVer);
    }

    // === Step 2: Find CLI component ===

    private static Component? FindCliComponent(Component[] components)
    {
        var cli = components.FirstOrDefault(c =>
            c.PublicSurface?.Contains("Program") == true ||
            (c.Classification == ModuleClassification.IoShell &&
             c.StubNames?.Any(s => s.Contains("console") || s.Contains("io-console")) == true));

        cli ??= components.FirstOrDefault(c =>
            !components.Any(other => (other.Dependencies ?? []).Contains(c.Name, StringComparer.OrdinalIgnoreCase)));

        return cli;
    }

    // === Step 3: Materialize files to disk ===

    private static Dictionary<string, SourceCodeFile> MaterializeFiles(
        SourceCodeBundle source,
        DafnyVerificationResult[]? dafnyVer,
        TestSuite? tests,
        ArchitectureContract? arch,
        string contextDir)
    {
        var filesByRelPath = new Dictionary<string, SourceCodeFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in source.Files!)
            AddFile(filesByRelPath, file);

        if (dafnyVer is not null)
        {
            foreach (var r in dafnyVer.DistinctBy(r => r.ModuleName))
            {
                if (!string.IsNullOrWhiteSpace(r.TranslatedCSharpPath) && File.Exists(r.TranslatedCSharpPath))
                {
                    var fileName = Path.GetFileName(r.TranslatedCSharpPath);
                    var rel = $"{r.ModuleName}/{fileName}";
                    var content = File.ReadAllText(r.TranslatedCSharpPath);
                    filesByRelPath[rel] = new SourceCodeFile(rel, content);
                }
            }
        }

        if (tests?.TestFiles is not null)
        {
            foreach (var file in tests.TestFiles)
                AddFile(filesByRelPath, file);
        }

        // Post-process test files: inject using statements for io-shell class names
        // that the QA model references without namespace qualification.
        // The model writes tests that use FileIO, StreamIO, etc. as bare names,
        // but these classes live in the io-shell component's namespace.
        InjectIoShellUsings(filesByRelPath, arch);

        // Write all files to disk
        foreach (var file in filesByRelPath.Values)
        {
            var fullPath = Path.Combine(contextDir, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
        }

        return filesByRelPath;
    }

    private static void AddFile(Dictionary<string, SourceCodeFile> filesByRelPath, SourceCodeFile file)
    {
        var rel = file.Path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(rel)) return;
        filesByRelPath[rel] = new SourceCodeFile(rel, file.Content);
    }

    /// <summary>
    /// Scan test files for io-shell class names (FileIO, StreamIO, ConsoleIO, etc.)
    /// used without namespace qualification. Inject the correct using statements
    /// at the top of the file so they compile.
    ///
    /// The QA model generates tests that reference these classes as bare names
    /// (e.g., "FileIO.ReadFile(...)") but doesn't know which namespace they're in.
    /// We map class names to component namespaces from the architecture contract.
    /// </summary>
    private static void InjectIoShellUsings(
        Dictionary<string, SourceCodeFile> filesByRelPath,
        ArchitectureContract? arch)
    {
        if (arch?.Components is null || arch.Components.Length == 0)
            return;

        // Build a map: io-shell class name → component namespace
        // Each io-shell component's namespace is its Name (e.g., "CsvFileReader")
        // and the stub classes are FileIO, ConsoleIO, StreamIO, etc.
        var classToNamespace = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comp in arch.Components)
        {
            if (comp.Classification != ModuleClassification.IoShell)
                continue;

            // Map common stub class names to this component's namespace
            var stubNames = comp.StubNames ?? [];
            foreach (var stubName in stubNames)
            {
                var className = StubNameToClassName(stubName);
                if (!string.IsNullOrEmpty(className))
                    classToNamespace.TryAdd(className, comp.Name);
            }
        }

        if (classToNamespace.Count == 0)
            return;

        // Known io-shell class names to scan for
        var knownClassNames = new HashSet<string>(classToNamespace.Keys, StringComparer.OrdinalIgnoreCase);

        // Scan test files (files with "Test" in the name or containing [Fact]/[Theory])
        var testFilePaths = filesByRelPath
            .Where(kvp => kvp.Key.Contains("Test", StringComparison.OrdinalIgnoreCase)
                          || kvp.Value.Content.Contains("[Fact]")
                          || kvp.Value.Content.Contains("[Theory]"))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var testPath in testFilePaths)
        {
            var file = filesByRelPath[testPath];
            var content = file.Content;

            // Find which class names are referenced in this file
            var neededUsings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var className in knownClassNames)
            {
                // Look for the class name as a word in the file (not in a using statement already)
                if (content.Contains(className, StringComparison.OrdinalIgnoreCase)
                    && !AlreadyHasUsingFor(content, className, classToNamespace[className]))
                {
                    neededUsings.Add(classToNamespace[className]);
                }
            }

            if (neededUsings.Count == 0)
                continue;

            // Inject using statements at the top of the file, after existing using directives.
            // Only match lines that are using DIRECTIVES (e.g. "using System;"),
            // NOT using STATEMENTS (e.g. "using var sw = new StringWriter();").
            var lines = content.Split('\n').ToList();
            var lastUsingIdx = -1;
            for (int i = 0; i < lines.Count && i < 50; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("using ", StringComparison.OrdinalIgnoreCase))
                {
                    // Using directive: "using Namespace;" or "using static X;"
                    // NOT a using statement: "using var x = ..." or "using (x) {"
                    var afterUsing = trimmed[6..].TrimStart();
                    if (!afterUsing.StartsWith("var ") && !afterUsing.StartsWith("(")
                        && !char.IsLower(afterUsing[0]))
                    {
                        lastUsingIdx = i;
                    }
                }
            }

            var usingLines = neededUsings
                .OrderBy(n => n)
                .Select(n => $"using {n};")
                .ToList();

            if (lastUsingIdx >= 0)
            {
                lines.InsertRange(lastUsingIdx + 1, usingLines);
            }
            else
            {
                // No existing usings — insert at the very top
                usingLines.Add("");
                lines.InsertRange(0, usingLines);
            }

            var newContent = string.Join('\n', lines);
            filesByRelPath[testPath] = new SourceCodeFile(testPath, newContent);
            Console.Error.WriteLine($"[Posit] Bot Harness — injected {neededUsings.Count} using statements into test file: {testPath}");
        }
    }

    /// <summary>
    /// Check if the file already has a using statement for the given namespace.
    /// </summary>
    private static bool AlreadyHasUsingFor(string content, string className, string namespaceName)
    {
        return content.Contains($"using {namespaceName};", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Convert a stub name like "file-io" to a class name like "FileIO".
    /// Multi-letter abbreviations like "io" are fully uppercased.
    /// </summary>
    private static string StubNameToClassName(string stubName)
    {
        var knownAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "io", "ci", "cd" };

        var parts = stubName.Split('-');
        return string.Concat(parts.Select(p =>
        {
            if (p.Length == 0) return "";
            if (knownAbbreviations.Contains(p))
                return p.ToUpperInvariant();
            return char.ToUpperInvariant(p[0]) + p[1..];
        }));
    }

    // === Step 4: Generate projects and solution ===
    // Adapted from DockerVerifier — detects test projects, infers packages.

    private static void GenerateProjects(
        string contextDir,
        ArchitectureContract? arch,
        Dictionary<string, SourceCodeFile> filesByRelPath,
        string targetFramework)
    {
        var archRefs = arch?.Components?.DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)?.ToDictionary(
            c => c.Name,
            c => (IReadOnlySet<string>)(c.Dependencies ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        // Copy the Dafny runtime DLL into the build context so generated projects can reference it.
        // The Dafny-translated C# uses Dafny.Rune, Dafny.ISequence<>, etc.
        var dafnyRuntimeSrc = FindDafnyRuntimeDll();
        var dafnyRuntimeDir = Path.Combine(contextDir, "DafnyRuntime");
        Directory.CreateDirectory(dafnyRuntimeDir);
        if (dafnyRuntimeSrc is not null)
        {
            File.Copy(dafnyRuntimeSrc, Path.Combine(dafnyRuntimeDir, "DafnyRuntime.dll"), overwrite: true);
            Console.Error.WriteLine($"[Posit] Bot Harness — copied DafnyRuntime.dll from {dafnyRuntimeSrc}");
        }
        else
        {
            Console.Error.WriteLine("[Posit] Bot Harness — WARNING: DafnyRuntime.dll not found, Dafny-translated C# may fail to compile");
        }

        var candidateProjectDirs = Directory.GetDirectories(contextDir, "*", SearchOption.TopDirectoryOnly)
            .Where(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories).Any())
            .Select(d => Path.GetFileName(d)!)
            .ToList();

        foreach (var projectName in candidateProjectDirs)
        {
            var projectDir = Path.Combine(contextDir, projectName);
            if (Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories).Any())
                continue;

            var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
            var isExe = File.Exists(Path.Combine(projectDir, "Program.cs")) ||
                        filesByRelPath.Any(f => f.Key.StartsWith($"{projectName}/", StringComparison.OrdinalIgnoreCase) &&
                                               f.Value.Content.Contains("public static int Run("));
            var isTest = IsTestProjectDirectory(projectDir, projectName);
            var refs = new HashSet<string>(archRefs.GetValueOrDefault(projectName) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            // Test projects need references to all NON-TEST projects (they test the components, not each other)
            if (isTest)
            {
                foreach (var other in candidateProjectDirs)
                {
                    if (string.Equals(other, projectName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var otherDir = Path.Combine(contextDir, other);
                    var otherIsTest = IsTestProjectDirectory(otherDir, other);
                    if (!otherIsTest)
                        refs.Add(other);
                }
            }

            var (pkgs, fws) = InferPackagesAndFrameworks(projectDir, isTest, targetFramework);
            File.WriteAllText(csprojPath, GenerateCsproj(projectName, targetFramework, isExe, refs, pkgs, fws, isTest, dafnyRuntimeSrc is not null));

            // If this is an Exe project but has no Program.cs, generate one.
            // Wire.cs has `public static int Run(string[] args)` but .NET requires
            // a `static Main(string[] args)` entry point. Generate a thin wrapper.
            if (isExe && !File.Exists(Path.Combine(projectDir, "Program.cs")))
            {
                var wireNamespace = projectName; // Wire.cs uses namespace {ComponentName}
                var programContent = $""""
                    // Auto-generated entry point — calls Wire.Run(args)
                    using {wireNamespace};

                    var exitCode = Wire.Run(args);
                    Environment.Exit(exitCode);
                    """";
                File.WriteAllText(Path.Combine(projectDir, "Program.cs"), programContent);
                Console.Error.WriteLine($"[Posit] Bot Harness — generated Program.cs entry point for {projectName}");
            }
        }

        // Generate solution
        var sln = Path.Combine(contextDir, "PositGenerated.sln");
        File.WriteAllText(sln, GenerateSolution(contextDir, candidateProjectDirs));
    }

    /// <summary>
    /// Find the DafnyRuntime.dll by searching common locations.
    /// </summary>
    private static string? FindDafnyRuntimeDll()
    {
        var candidates = new List<string>();

        // 1. Posit.DafnyRuntime project directory
        var searchRoots = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory };
        foreach (var root in searchRoots)
        {
            var dir = root;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "src", "Posit.DafnyRuntime", "DafnyRuntime.dll");
                if (File.Exists(candidate))
                    candidates.Add(candidate);
                candidate = Path.Combine(dir, "src", "Posit.DafnyRuntime", "bin", "Debug", "net8.0", "Posit.DafnyRuntime.dll");
                if (File.Exists(candidate))
                    candidates.Add(candidate);
                var parent = Directory.GetParent(dir);
                if (parent is null) break;
                dir = parent.FullName;
            }
        }

        // 2. Dafny installation
        var dafnyHome = Environment.GetEnvironmentVariable("DAFNY_HOME");
        if (!string.IsNullOrEmpty(dafnyHome))
        {
            var candidate = Path.Combine(dafnyHome, "bin", "DafnyRuntime.dll");
            if (File.Exists(candidate)) candidates.Add(candidate);
        }

        // 3. dotnet tools
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var toolsPath = Path.Combine(userHome, ".dotnet", "tools");
        if (Directory.Exists(toolsPath))
        {
            foreach (var f in Directory.GetFiles(toolsPath, "DafnyRuntime.dll", SearchOption.AllDirectories))
                candidates.Add(f);
        }

        return candidates.FirstOrDefault();
    }

    private static bool IsTestProjectDirectory(string projectDir, string projectName)
    {
        if (projectName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains("Integration", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var csFile in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csFile);
            if (content.Contains("[Fact]") || content.Contains("[Theory]") || content.Contains("[InlineData("))
                return true;
        }
        return false;
    }

    private static (HashSet<string> Packages, HashSet<string> Frameworks) InferPackagesAndFrameworks(
        string projectDir, bool isTestProject, string targetFramework)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allSource = string.Join("\n", Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        // xUnit / testing
        if (isTestProject || allSource.Contains("[Fact]") || allSource.Contains("[Theory]"))
        {
            packages.Add("Microsoft.NET.Test.Sdk|17.12.0");
            packages.Add("xunit|2.9.2");
            packages.Add("xunit.runner.visualstudio|2.8.2");
        }

        // ASP.NET Core shared framework
        if (allSource.Contains("Microsoft.AspNetCore") ||
            allSource.Contains("WebApplication") ||
            allSource.Contains("IApplicationBuilder") ||
            allSource.Contains("IEndpointRouteBuilder"))
        {
            frameworks.Add("Microsoft.AspNetCore.App");
        }

        // Entity Framework Core
        if (allSource.Contains("Microsoft.EntityFrameworkCore") ||
            allSource.Contains("DbContext") || allSource.Contains("DbSet"))
        {
            packages.Add("Microsoft.EntityFrameworkCore|9.0.0");
        }

        return (packages, frameworks);
    }

    private static string GenerateCsproj(
        string name, string targetFramework, bool isExe,
        IReadOnlySet<string> references, HashSet<string> packages, HashSet<string> frameworks,
        bool isTestProject = false, bool includeDafnyRuntime = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        if (isExe)
            sb.AppendLine("    <OutputType>Exe</OutputType>");
        sb.AppendLine($"    <TargetFramework>{targetFramework}</TargetFramework>");
        sb.AppendLine("    <LangVersion>latest</LangVersion>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        if (isTestProject || name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("    <IsPackable>false</IsPackable>");
            sb.AppendLine("    <IsTestProject>true</IsTestProject>");
        }
        sb.AppendLine("  </PropertyGroup>");

        if (references.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var r in references)
                sb.AppendLine($"    <ProjectReference Include=\"../{r}/{r}.csproj\" />");
            sb.AppendLine("  </ItemGroup>");
        }

        if (frameworks.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var fw in frameworks)
                sb.AppendLine($"    <FrameworkReference Include=\"{fw}\" />");
            sb.AppendLine("  </ItemGroup>");
        }

        if (packages.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in packages)
            {
                var parts = pkg.Split('|');
                sb.AppendLine($"    <PackageReference Include=\"{parts[0]}\" Version=\"{parts[1]}\" />");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        // Dafny runtime DLL reference — needed for Dafny-translated C# (Dafny.Rune, Dafny.ISequence, etc.)
        if (includeDafnyRuntime)
        {
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <Reference Include=\"DafnyRuntime\">");
            sb.AppendLine("      <HintPath>../DafnyRuntime/DafnyRuntime.dll</HintPath>");
            sb.AppendLine("    </Reference>");
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    private static string GenerateSolution(string contextDir, List<string> projects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.0");
        sb.AppendLine("# Visual Studio Version 17");
        sb.AppendLine("VisualStudioVersion = 17.0.31903.59");
        sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
        sb.AppendLine();

        var projectGuids = new Dictionary<string, string>();
        foreach (var project in projects)
        {
            var guid = Guid.NewGuid().ToString("B").ToUpperInvariant();
            projectGuids[project] = guid;
            var csprojPath = Path.Combine(project, $"{project}.csproj");
            sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{project}\", \"{csprojPath}\", \"{guid}\"");
            sb.AppendLine("EndProject");
        }
        sb.AppendLine("Global");
        sb.AppendLine("    GlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("        Debug|Any CPU = Debug|Any CPU");
        sb.AppendLine("        Release|Any CPU = Release|Any CPU");
        sb.AppendLine("    EndGlobalSection");
        sb.AppendLine("    GlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var project in projects)
        {
            var guid = projectGuids[project];
            sb.AppendLine($"        {guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            sb.AppendLine($"        {guid}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            sb.AppendLine($"        {guid}.Release|Any CPU.ActiveCfg = Release|Any CPU");
            sb.AppendLine($"        {guid}.Release|Any CPU.Build.0 = Release|Any CPU");
        }
        sb.AppendLine("    EndGlobalSection");
        sb.AppendLine("EndGlobal");
        return sb.ToString();
    }

    // === Step 5: Build in Docker ===

    private async Task<(bool Success, string Output)> BuildInDockerAsync(
        string contextDir, string targetFramework, CancellationToken ct)
    {
        var dockerfile = Path.Combine(contextDir, "Dockerfile");
        var sdkTag = targetFramework.StartsWith("net10") ? "10.0" :
                     targetFramework.StartsWith("net9") ? "9.0" : "8.0";

        File.WriteAllText(dockerfile, $"""
            FROM mcr.microsoft.com/dotnet/sdk:{sdkTag} AS build
            WORKDIR /src
            COPY . .
            RUN dotnet build PositGenerated.sln -c Release
            """);

        return await RunDockerAsync(contextDir, dockerfile, ct);
    }

    private async Task<(bool Success, string Output)> RunDockerAsync(
        string contextDir, string dockerfile, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _dockerPath,
            Arguments = $"build --no-cache --tag posit-harness:latest \"{contextDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var sb = new StringBuilder();
        var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { } }

        return (proc.ExitCode == 0, sb.ToString());
    }

    // === Step 6: Generate test data from the spec ===

    private static List<TestCase> GenerateTestData(ArchitectureContract? arch, Component cliComponent)
    {
        var tests = new List<TestCase>();

        // Basic smoke test: run with no args (should print usage and exit 1)
        tests.Add(new TestCase("no-args", [], "Usage:", 1, "Should print usage message and exit non-zero"));

        // Run with a simple input (smoke test — does it produce output without crashing?)
        tests.Add(new TestCase("smoke-test", ["test-input"], null, null, "Should produce output without crashing"));

        // If we have architecture contract test cases, use them
        if (arch is not null)
        {
            foreach (var comp in arch.Components)
            {
                if (comp.TestCases is null || comp.TestCases.Length == 0) continue;
                foreach (var tc in comp.TestCases)
                {
                    tests.Add(new TestCase(
                        Name: $"arch-{comp.Name}-{tc.Id}",
                        Args: ["test-data"],
                        ExpectedOutputContains: tc.ExpectedBehavior,
                        ExpectedExitCode: null,
                        Description: $"{tc.Name}: {tc.Description} → {tc.ExpectedBehavior}"
                    ));
                }
            }
        }

        return tests;
    }

    // === Step 7: Run the CLI with a test case ===

    private async Task<TestCaseResult> RunCliAsync(
        string contextDir,
        Component cliComponent,
        TestCase testCase,
        string targetFramework,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var sdkTag = targetFramework.StartsWith("net10") ? "10.0" :
                     targetFramework.StartsWith("net9") ? "9.0" : "8.0";

        var argsStr = string.Join(" ", testCase.Args.Select(a => $"\"{a}\""));

        // Dockerfile that builds, then runs the CLI with the test args
        var runDockerfile = $"""
            FROM mcr.microsoft.com/dotnet/sdk:{sdkTag} AS build
            WORKDIR /src
            COPY . .
            RUN dotnet build PositGenerated.sln -c Release

            FROM mcr.microsoft.com/dotnet/runtime:{sdkTag}
            WORKDIR /app
            COPY --from=build /src/bin/Release/{targetFramework}/ ./
            ENTRYPOINT ["dotnet", "{cliComponent.Name}.dll"]
            """;

        var runDockerfilePath = Path.Combine(contextDir, "Dockerfile.run");
        File.WriteAllText(runDockerfilePath, runDockerfile);

        // Build the run image
        var buildResult = await RunDockerBuildAsync(contextDir, "Dockerfile.run", "posit-harness-run:latest", ct);
        if (!buildResult.Success)
            return new TestCaseResult(testCase.Name, false, -1, sw.ElapsedMilliseconds, "", $"Failed to build run image: {buildResult.Output}");

        // Run the container with the test args
        var runResult = await RunDockerContainerAsync("posit-harness-run:latest", testCase.Args, ct);
        sw.Stop();

        var passed = EvaluateResult(testCase, runResult);
        return new TestCaseResult(
            testCase.Name,
            passed,
            runResult.ExitCode,
            sw.ElapsedMilliseconds,
            runResult.Output,
            runResult.Error);
    }

    private async Task<(bool Success, string Output)> RunDockerBuildAsync(
        string contextDir, string dockerfileName, string tag, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _dockerPath,
            Arguments = $"build --no-cache -f \"{dockerfileName}\" --tag {tag} \"{contextDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var sb = new StringBuilder();
        var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { } }

        return (proc.ExitCode == 0, sb.ToString());
    }

    private async Task<(int ExitCode, string Output, string Error)> RunDockerContainerAsync(
        string image, string[] args, CancellationToken ct)
    {
        var argsStr = string.Join(" ", args.Select(a => $"\"{a}\""));
        var psi = new ProcessStartInfo
        {
            FileName = _dockerPath,
            Arguments = $"run --rm {image} {argsStr}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputSb = new StringBuilder();
        var errorSb = new StringBuilder();
        var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) outputSb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) errorSb.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { } }

        return (proc.ExitCode, outputSb.ToString(), errorSb.ToString());
    }

    // === Step 8: Evaluate test results ===

    private static bool EvaluateResult(TestCase testCase, (int ExitCode, string Output, string Error) run)
    {
        if (testCase.ExpectedExitCode is int expectedExit)
        {
            if (run.ExitCode != expectedExit)
                return false;
        }

        if (testCase.ExpectedOutputContains is string expected)
        {
            var combined = run.Output + run.Error;
            if (!combined.Contains(expected, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // If no expectations set, pass if it didn't crash
        if (testCase.ExpectedExitCode is null && testCase.ExpectedOutputContains is null)
        {
            return run.ExitCode == 0 || !string.IsNullOrWhiteSpace(run.Output);
        }

        return true;
    }

    // === Helpers ===

    private static string? DetectTargetFramework(IEnumerable<SourceCodeFile> files)
    {
        var content = string.Join('\n', files.Select(f => f.Content));
        if (content.Contains("net10.0") || content.Contains("System.Numerics.Tensors"))
            return "net10.0";
        if (content.Contains("net9.0"))
            return "net9.0";
        if (content.Contains("net8.0"))
            return "net8.0";
        return "net10.0";
    }
}

// === Result types ===

public record BotHarnessResult
{
    public bool Success { get; init; }
    public string Summary { get; init; } = "";
    public string BuildOutput { get; init; } = "";
    public List<TestCaseResult> TestResults { get; init; } = [];
    public string CliComponent { get; init; } = "";

    public static BotHarnessResult Fail(string reason) => new()
    {
        Success = false,
        Summary = $"Bot Harness failed: {reason}"
    };
}

public record TestCaseResult(
    string Name,
    bool Passed,
    int ExitCode,
    long ElapsedMs,
    string Output,
    string Error);

public record TestCase(
    string Name,
    string[] Args,
    string? ExpectedOutputContains,
    int? ExpectedExitCode,
    string Description);