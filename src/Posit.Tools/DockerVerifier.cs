using System.Text;
using System.Text.Json;
using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;
using Posit.Contracts.Serialization;
using Posit.Data.Repositories;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Tools;

/// <summary>
/// Verifies generated C# artifacts by materializing them in a Docker container
/// and running `dotnet build` plus `dotnet test`. Uses the posit_artifacts table
/// as the source of truth.
/// </summary>
public sealed class DockerVerifier
{
    private readonly ArtifactRepository _artifactRepo;
    private readonly string _dockerPath;

    public DockerVerifier(ArtifactRepository artifactRepo, string? dockerPath = null)
    {
        _artifactRepo = artifactRepo ?? throw new ArgumentNullException(nameof(artifactRepo));
        _dockerPath = dockerPath ?? "docker";
    }

    /// <summary>
    /// Build and test the artifacts from the given session in a container.
    /// Returns the container output and a boolean for overall success.
    /// </summary>
    public async Task<(bool Success, string Output)> VerifyAsync(SessionId sessionId, CancellationToken ct = default)
    {
        var contextDir = Path.Combine(Path.GetTempPath(), $"posit-verify-{sessionId.Value}");
        Directory.CreateDirectory(contextDir);

        try
        {
            var artifacts = await _artifactRepo.ListBySessionAsync(sessionId, ct);
            var sourceBundle = artifacts.LastOrDefault(a => a.Kind == ArtifactKind.SourceCodeBundle);
            var testSuite = artifacts.LastOrDefault(a => a.Kind == ArtifactKind.TestSuite);
            var dafnyVerification = artifacts.LastOrDefault(a => a.Kind == ArtifactKind.DafnyVerification);

            if (sourceBundle is null)
                return (false, "No SourceCodeBundle artifact found.");

            var source = JsonSerializer.Deserialize<SourceCodeBundle>(sourceBundle.PayloadJson, Options);
            if (source?.Files is null or { Length: 0 })
                return (false, "SourceCodeBundle contains no files.");

            var tests = testSuite is not null
                ? JsonSerializer.Deserialize<TestSuite>(testSuite.PayloadJson, Options)
                : null;

            var filesByRelPath = new Dictionary<string, SourceCodeFile>();

            foreach (var file in source.Files)
                AddFile(filesByRelPath, file);

            if (dafnyVerification is not null)
            {
                var results = JsonSerializer.Deserialize<DafnyVerificationResult[]>(dafnyVerification.PayloadJson, Options);
                if (results is not null)
                {
                    foreach (var r in results.DistinctBy(r => r.ModuleName))
                    {
                        if (!string.IsNullOrWhiteSpace(r.TranslatedCSharpPath) && File.Exists(r.TranslatedCSharpPath))
                        {
                            var fileName = Path.GetFileName(r.TranslatedCSharpPath);
                            var rel = $"{r.ModuleName}/{fileName}";
                            var content = await File.ReadAllTextAsync(r.TranslatedCSharpPath, ct);
                            filesByRelPath[rel] = new SourceCodeFile(rel, content);
                        }
                    }
                }
            }

            if (tests?.TestFiles is not null)
            {
                foreach (var file in tests.TestFiles)
                    AddFile(filesByRelPath, file);
            }

            var targetFramework = DetectTargetFramework(filesByRelPath.Values) ?? "net8.0";

            // Materialize files
            foreach (var file in filesByRelPath.Values)
            {
                var fullPath = Path.Combine(contextDir, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, file.Content, ct);
            }

            // Build project directory list and create missing .csproj files
            var projectDirs = Directory.GetDirectories(contextDir, "*", SearchOption.TopDirectoryOnly)
                .Where(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories).Any())
                .Select(d => Path.GetFileName(d)!)
                .ToList();

            foreach (var projectName in projectDirs)
            {
                var projectDir = Path.Combine(contextDir, projectName);
                var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
                if (!File.Exists(csprojPath))
                {
                    var isExe = File.Exists(Path.Combine(projectDir, "Program.cs"));
                    var isTest = IsTestProjectDirectory(projectDir);
                    var refs = InferReferences(projectName, projectDirs, contextDir);
                    await File.WriteAllTextAsync(csprojPath, GenerateCsproj(projectName, targetFramework, isExe, refs, isTest), ct);
                }
            }

            // Normalize existing .csproj files
            foreach (var csproj in Directory.GetFiles(contextDir, "*.csproj", SearchOption.AllDirectories))
            {
                var projectDir = Path.GetDirectoryName(csproj)!;
                var projectName = Path.GetFileNameWithoutExtension(csproj);
                var isExe = File.Exists(Path.Combine(projectDir, "Program.cs"));
                var isTest = IsTestProjectDirectory(projectDir);
                var refs = InferReferences(projectName, projectDirs, contextDir);
                await File.WriteAllTextAsync(csproj, GenerateCsproj(projectName, targetFramework, isExe, refs, isTest), ct);
            }

            // Generate solution
            var sln = Path.Combine(contextDir, "PositGenerated.sln");
            await File.WriteAllTextAsync(sln, GenerateSolution(contextDir, targetFramework), ct);

            // Dockerfile
            var dockerfile = Path.Combine(contextDir, "Dockerfile");
            await File.WriteAllTextAsync(dockerfile, GetDockerfile(targetFramework), ct);

            var result = await RunDockerAsync(contextDir, dockerfile, ct);
            return result;
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("POSIT_VERIFY_KEEP") != "1")
            {
                try { Directory.Delete(contextDir, recursive: true); }
                catch { /* best effort cleanup */ }
            }
            else
            {
                Console.Error.WriteLine($"[Posit] Preserved verify context: {contextDir}");
            }
        }
    }

    private static void AddFile(Dictionary<string, SourceCodeFile> filesByRelPath, SourceCodeFile file)
    {
        var rel = NormalizePath(file.Path);
        if (string.IsNullOrEmpty(rel)) return;

        var (module, newName) = ClassifyFragment(rel);
        var newRel = string.IsNullOrEmpty(module) ? newName : $"{module}/{newName}";
        filesByRelPath[newRel] = new SourceCodeFile(newRel, file.Content);
    }

    private static string NormalizePath(string path)
    {
        var cleaned = path.Replace('\\', '/').TrimStart('/');
        if (cleaned.Contains(':'))
        {
            var parts = cleaned.Split('/').Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            var fileName = parts[^1];
            return fileName;
        }
        return cleaned;
    }

    private static readonly string[] ModuleNoiseSuffixes = { ".Implementation", ".Implementations", ".extern", "Extern", "Implementation", "Implementations" };

    private static (string Module, string FileName) ClassifyFragment(string relPath)
    {
        var parts = relPath.Split('/').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        if (parts.Length == 0)
            return (string.Empty, string.Empty);

        // Clean directory noise: SqlGenerator.extern -> SqlGenerator
        for (int i = 0; i < parts.Length - 1; i++)
        {
            parts[i] = StripModuleNoise(parts[i]);
        }

        var fileName = parts[^1];

        // Strip skeleton- prefix
        if (fileName.StartsWith("skeleton-", StringComparison.OrdinalIgnoreCase))
        {
            var rest = fileName["skeleton-".Length..];
            var dotIdx = rest.IndexOf('.');
            var module = dotIdx > 0 ? rest[..dotIdx] : Path.GetFileNameWithoutExtension(rest);
            return (module, fileName);
        }

        // Strip known suffixes to find module name
        foreach (var suffix in ModuleNoiseSuffixes)
        {
            if (fileName.EndsWith(suffix + ".cs", StringComparison.OrdinalIgnoreCase))
            {
                var module = fileName[..^(suffix.Length + ".cs".Length)];
                return (module, fileName);
            }
        }

        // Path like Module/File.cs
        if (parts.Length >= 2)
        {
            return (parts[0], fileName);
        }

        // Fallback
        return (Path.GetFileNameWithoutExtension(fileName), fileName);
    }

    private static string StripModuleNoise(string name)
    {
        foreach (var suffix in ModuleNoiseSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }
        return name;
    }

    private static string? DetectTargetFramework(IEnumerable<SourceCodeFile> files)
    {
        foreach (var file in files.Where(f => f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var match = System.Text.RegularExpressions.Regex.Match(file.Content, "<TargetFramework>([^" + "<" + "]+)</TargetFramework>");
            if (match.Success) return match.Groups[1].Value.Trim();
        }
        return null;
    }

    private static HashSet<string> InferReferences(string projectName, List<string> allProjects, string contextDir)
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectDir = Path.Combine(contextDir, projectName);
        if (!Directory.Exists(projectDir)) return refs;

        foreach (var csFile in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csFile);
            foreach (var other in allProjects.Where(p => !string.Equals(p, projectName, StringComparison.OrdinalIgnoreCase)))
            {
                if (content.Contains($"using {other};") || content.Contains($"{other}."))
                    refs.Add(other);
            }
        }

        // Test projects reference their subject project automatically
        if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
        {
            var subject = projectName[..^".Tests".Length];
            if (allProjects.Contains(subject))
                refs.Add(subject);
        }

        return refs;
    }

    private static bool IsTestProjectDirectory(string projectDir)
    {
        foreach (var csFile in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csFile);
            if (content.Contains("[Fact]") || content.Contains("[Theory]") || content.Contains("[InlineData("))
                return true;
        }
        return false;
    }

    private static string GenerateCsproj(string name, string targetFramework, bool isExe, HashSet<string> references, bool isTestProject = false)
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
        if (isTestProject || name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.12.0\" />");
            sb.AppendLine("    <PackageReference Include=\"xunit\" Version=\"2.9.2\" />");
            sb.AppendLine("    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"2.8.2\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        if (name.Contains("Sql"))
        {
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("    <PackageReference Include=\"Npgsql\" Version=\"9.0.2\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    private static string GenerateSolution(string contextDir, string targetFramework)
    {
        // Renormalize existing csproj files before generating the solution so references are current.
        var projectDirs = Directory.GetDirectories(contextDir, "*", SearchOption.TopDirectoryOnly)
            .Where(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories).Any())
            .Select(d => Path.GetFileName(d)!)
            .ToList();

        foreach (var projectName in projectDirs)
        {
            var projectDir = Path.Combine(contextDir, projectName);
            var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
            if (!File.Exists(csprojPath))
            {
                var isExe = File.Exists(Path.Combine(projectDir, "Program.cs"));
                var isTest = IsTestProjectDirectory(projectDir);
                var refs = InferReferences(projectName, projectDirs, contextDir);
                File.WriteAllText(csprojPath, GenerateCsproj(projectName, targetFramework, isExe, refs, isTest));
            }
        }

        var projects = Directory.GetFiles(contextDir, "*.csproj", SearchOption.AllDirectories)
            .Select(p => (Name: Path.GetFileNameWithoutExtension(p), Rel: Path.GetRelativePath(contextDir, p).Replace('\\', '/')))
            .OrderBy(p => p.Name)
            .ToList();

        // Deduplicate project names by appending a numeric suffix
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueProjects = new List<(string Name, string Rel, string Guid)>();
        foreach (var p in projects)
        {
            var current = p;
            var name = current.Name;
            var suffix = 1;
            while (!seen.Add(name))
            {
                name = $"{current.Name}_{suffix}";
                suffix++;
            }
            if (name != current.Name)
            {
                // Rename the actual file so the .sln reference matches
                var oldPath = Path.Combine(contextDir, current.Rel);
                var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, $"{name}.csproj");
                File.Move(oldPath, newPath);
                current = (name, Path.GetRelativePath(contextDir, newPath).Replace('\\', '/'));
            }
            uniqueProjects.Add((current.Name, current.Rel, Guid.NewGuid().ToString().ToUpperInvariant()));
        }

        // Re-normalize references after possible renames
        foreach (var csproj in Directory.GetFiles(contextDir, "*.csproj", SearchOption.AllDirectories))
        {
            var projectDir = Path.GetDirectoryName(csproj)!;
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            var isExe = File.Exists(Path.Combine(projectDir, "Program.cs"));
            var isTest = IsTestProjectDirectory(projectDir);
            var refs = InferReferences(projectName, uniqueProjects.Select(p => p.Name).ToList(), contextDir);
            File.WriteAllText(csproj, GenerateCsproj(projectName, targetFramework, isExe, refs, isTest));
        }

        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio Version 17");
        sb.AppendLine("VisualStudioVersion = 17.0.31903.59");
        sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

        for (var i = 0; i < uniqueProjects.Count; i++)
        {
            var p = uniqueProjects[i];
            sb.AppendLine($"Project(\"{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}\") = \"{p.Name}\", \"{p.Rel}\", \"{{{p.Guid}}}\"");
            sb.AppendLine("EndProject");
        }

        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
        sb.AppendLine("\tEndGlobalSection");

        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var p in uniqueProjects)
        {
            sb.AppendLine($"\t\t{{{p.Guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            sb.AppendLine($"\t\t{{{p.Guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            sb.AppendLine($"\t\t{{{p.Guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU");
            sb.AppendLine($"\t\t{{{p.Guid}}}.Release|Any CPU.Build.0 = Release|Any CPU");
        }
        sb.AppendLine("\tEndGlobalSection");

        sb.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
        sb.AppendLine("\t\tHideSolutionNode = FALSE");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("EndGlobal");
        return sb.ToString();
    }

    private static string GetDockerfile(string targetFramework) =>
        $"""
        FROM mcr.microsoft.com/dotnet/sdk:{GetSdkTag(targetFramework)} AS build
        WORKDIR /src
        COPY . .
        RUN dotnet build PositGenerated.sln -c Release
        {GetTestCommand(targetFramework)}
        """;

    private static string GetSdkTag(string targetFramework)
    {
        if (targetFramework.StartsWith("net10")) return "10.0";
        if (targetFramework.StartsWith("net9")) return "9.0";
        if (targetFramework.StartsWith("net8")) return "8.0";
        return "8.0";
    }

    private static string GetTestCommand(string targetFramework) =>
        """
        RUN if grep -R "<IsTestProject>true</IsTestProject>" . --include="*.csproj" -q; then \
              dotnet test PositGenerated.sln -c Release --no-build; \
            else \
              echo "No test projects found"; \
            fi
        """;

    private async Task<(bool Success, string Output)> RunDockerAsync(string contextDir, string dockerfile, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _dockerPath,
            Arguments = $"build --no-cache --tag posit-verify:latest \"{contextDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var sb = new StringBuilder();
        var proc = System.Diagnostics.Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            using (ct.Register(() => tcs.TrySetResult(true)))
            {
                await Task.WhenAny(proc.WaitForExitAsync(), tcs.Task);
            }

            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (false, sb + "\nDocker build timed out.");
            }

            var output = sb.ToString();
            var success = proc.ExitCode == 0
                && output.Contains("Build succeeded.")
                && !output.Contains("0 project(s)");
            return (success, output);
        }
        finally
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
            }
        }
    }
}
