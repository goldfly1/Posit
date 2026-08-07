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

            var targetFramework = DetectTargetFramework(filesByRelPath.Values) ?? "net9.0";

            // Materialize files
            foreach (var file in filesByRelPath.Values)
            {
                var fullPath = Path.Combine(contextDir, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                var content = SanitizeSourceFile(file.Content);
                await File.WriteAllTextAsync(fullPath, content, ct);
            }

            // Build project directory list and create missing .csproj files.
            // If a directory already contains any .csproj, trust the model's project file
            // and only generate one when the directory is completely missing a project file.
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
                var isExe = File.Exists(Path.Combine(projectDir, "Program.cs"));
                var isTest = IsTestProjectDirectory(projectDir, projectName);
                var refs = InferReferences(projectName, candidateProjectDirs, contextDir);
                var (pkgs, fws) = InferPackagesAndFrameworks(projectDir, isTest, targetFramework);
                await File.WriteAllTextAsync(csprojPath, GenerateCsproj(projectName, targetFramework, isExe, refs, pkgs, fws, isTest), ct);
            }

            // Normalize only project files that we generated ourselves. Existing model .csproj
            // files are preserved so authored package/project references are not destroyed.
            var existingCsprojs = Directory.GetFiles(contextDir, "*.csproj", SearchOption.AllDirectories);
            foreach (var csproj in existingCsprojs)
            {
                var projectDir = Path.GetDirectoryName(csproj)!;
                var projectName = Path.GetFileNameWithoutExtension(csproj);
                var isTest = IsTestProjectDirectory(projectDir, projectName);
                var (pkgs, fws) = InferPackagesAndFrameworks(projectDir, isTest, targetFramework);
                MergeMissingRefsIntoCsproj(csproj, projectName, candidateProjectDirs, contextDir, pkgs, fws);
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

    /// <summary>
    /// Bounded source repairs for common model omissions that would otherwise fail
    /// deterministic compilation. These are standard-library extension methods whose
    /// namespace the QA model often forgets to import.
    /// </summary>
    private static string SanitizeSourceFile(string content)
    {
        var usesDiNamespace = content.Contains("using Microsoft.Extensions.DependencyInjection;") ||
                              content.Contains("using Microsoft.Extensions.DependencyInjection"); // trailing whitespace variants handled below

        var needsExtensionsNamespace = content.Contains("RemoveAll") ||
                                       content.Contains("TryAdd") ||
                                       content.Contains(".Replace(") ||
                                       content.Contains("AddLogging");

        if (usesDiNamespace && needsExtensionsNamespace &&
            !content.Contains("using Microsoft.Extensions.DependencyInjection.Extensions;"))
        {
            // Find the line with the DI using and insert the extension using right after it.
            var diUsingIndex = content.IndexOf("using Microsoft.Extensions.DependencyInjection;");
            if (diUsingIndex >= 0)
            {
                var endOfLine = content.IndexOf('\n', diUsingIndex);
                if (endOfLine < 0) endOfLine = content.Length;
                content = content.Insert(endOfLine + 1, "using Microsoft.Extensions.DependencyInjection.Extensions;\n");
            }
            else
            {
                var lastUsing = content.LastIndexOf("using ");
                if (lastUsing >= 0)
                {
                    var endOfLine = content.IndexOf('\n', lastUsing);
                    if (endOfLine < 0) endOfLine = content.Length;
                    content = content.Insert(endOfLine + 1, "using Microsoft.Extensions.DependencyInjection.Extensions;\n");
                }
                else
                {
                    content = "using Microsoft.Extensions.DependencyInjection.Extensions;\n" + content;
                }
            }
        }

        return content;
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

        // Only reference projects whose directory actually contains a .csproj file.
        var validProjects = allProjects
            .Where(p => !string.Equals(p, projectName, StringComparison.OrdinalIgnoreCase))
            .Where(p => Directory.Exists(Path.Combine(contextDir, p)) && Directory.GetFiles(Path.Combine(contextDir, p), "*.csproj", SearchOption.AllDirectories).Any())
            .ToList();

        var thisSource = string.Join("\n", Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        // Types declared in this project — don't infer references for those.
        var localTypes = ExtractLikelyTypeNames(thisSource);

        foreach (var other in validProjects)
        {
            var otherDir = Path.Combine(contextDir, other);
            var otherSource = string.Join("\n", Directory.GetFiles(otherDir, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

            // 1. Namespace-level usage
            if (thisSource.Contains($"using {other};") || thisSource.Contains($"{other}."))
                refs.Add(other);

            // 2. Type-level usage: if a public type declared in the other project is used in this project.
            var typeCandidates = ExtractLikelyTypeNames(otherSource);
            foreach (var typeName in typeCandidates)
            {
                if (localTypes.Contains(typeName))
                    continue;

                var patterns = new[]
                {
                    " " + typeName + " ",
                    " " + typeName + ";",
                    " " + typeName + ".",
                    "(" + typeName + " ",
                    "<" + typeName + ">",
                    " " + typeName + "\""
                };
                if (patterns.Any(p => thisSource.Contains(p)))
                {
                    refs.Add(other);
                    break;
                }
            }
        }

        // Test projects reference their subject project automatically
        if (projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
        {
            var subject = projectName[..^".Tests".Length];
            if (validProjects.Contains(subject))
                refs.Add(subject);
        }

        return refs;
    }

    private static HashSet<string> ExtractLikelyTypeNames(string source)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // class/record/struct/interface/enum declarations
        foreach (var keyword in new[] { "class", "record", "struct", "interface", "enum" })
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(source, $@"\b{keyword}\s+(\w+)");
            foreach (System.Text.RegularExpressions.Match m in matches)
                types.Add(m.Groups[1].Value);
        }
        return types;
    }

    private static bool IsTestProjectDirectory(string projectDir, string projectName)
    {
        var nameHint = projectName.Contains("Test", StringComparison.OrdinalIgnoreCase)
            || projectName.Contains("Integration", StringComparison.OrdinalIgnoreCase);
        if (nameHint) return true;

        foreach (var csFile in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csFile);
            if (content.Contains("[Fact]") || content.Contains("[Theory]") || content.Contains("[InlineData("))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detect required NuGet packages and framework references by scanning source files.
    /// This is the systemic alternative to hard-coding package names per project name.
    /// Package versions are chosen to match the target framework.
    /// </summary>
    private static (HashSet<string> Packages, HashSet<string> Frameworks) InferPackagesAndFrameworks(string projectDir, bool isTestProject, string targetFramework)
    {
        var tfmMajor = targetFramework.Length >= 5 && int.TryParse(targetFramework[3..].Split('.')[0], out var major) ? major : 8;

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
            allSource.Contains("IEndpointRouteBuilder") ||
            allSource.Contains("RouteHandlerBuilder"))
        {
            frameworks.Add("Microsoft.AspNetCore.App");
        }

        // Use the latest stable package versions for inferred references, regardless of
        // target framework. Newer packages are backward-compatible and this avoids the
        // endless downgrade/version-mismatch dance.
        const string EfVersion = "9.0.0";
        const string AspNetVersion = "9.0.0";
        const string ExtensionsVersion = "9.0.0";

        // ASP.NET Core integration testing
        if (allSource.Contains("WebApplicationFactory") || allSource.Contains("CustomWebApplicationFactory"))
            packages.Add($"Microsoft.AspNetCore.Mvc.Testing|{AspNetVersion}");

        if (allSource.Contains("TestServer"))
            packages.Add($"Microsoft.AspNetCore.TestHost|{AspNetVersion}");

        // Entity Framework Core
        if (allSource.Contains("Microsoft.EntityFrameworkCore") ||
            allSource.Contains("DbContext") ||
            allSource.Contains("DbSet") ||
            allSource.Contains("MigrationBuilder") ||
            allSource.Contains("ModelBuilder"))
        {
            packages.Add($"Microsoft.EntityFrameworkCore|{EfVersion}");
        }

        // EF Core Relational (MigrateAsync, ToTable, HasColumnName, annotations)
        if (allSource.Contains("MigrateAsync") ||
            allSource.Contains(".ToTable(") ||
            allSource.Contains("HasColumnName") ||
            allSource.Contains("HasAnnotation") ||
            allSource.Contains("UseIdentityByDefaultColumns") ||
            allSource.Contains("NpgsqlModelBuilderExtensions"))
        {
            packages.Add($"Microsoft.EntityFrameworkCore.Relational|{EfVersion}");
        }

        // PostgreSQL / Npgsql
        if (allSource.Contains("Npgsql.EntityFrameworkCore.PostgreSQL") ||
            allSource.Contains("UseNpgsql") ||
            allSource.Contains("NpgsqlDbContextOptionsExtensions"))
        {
            packages.Add($"Npgsql.EntityFrameworkCore.PostgreSQL|{EfVersion}");
        }
        else if (allSource.Contains("Npgsql"))
        {
            packages.Add("Npgsql|9.0.2");
        }

        // Generic host / DI / logging / configuration
        var mayProvideLoggingAbstractions = false;

        if (allSource.Contains("Microsoft.Extensions.Hosting") || allSource.Contains("IHost"))
        {
            packages.Add($"Microsoft.Extensions.Hosting|{ExtensionsVersion}");
            mayProvideLoggingAbstractions = true;
        }

        if (allSource.Contains("Microsoft.Extensions.DependencyInjection") || allSource.Contains("IServiceCollection"))
        {
            packages.Add($"Microsoft.Extensions.DependencyInjection|{ExtensionsVersion}");
            // IServiceCollection extension methods such as RemoveAll live in the abstractions package.
            packages.Add($"Microsoft.Extensions.DependencyInjection.Abstractions|{ExtensionsVersion}");
        }

        if (allSource.Contains("Microsoft.Extensions.Logging") || allSource.Contains("ILogger"))
        {
            // Only add Logging.Abstractions explicitly if nothing else is already going to bring it in
            // at a higher version (Npgsql, EF Core, and Hosting all reference it transitively).
            if (!mayProvideLoggingAbstractions && !packages.Any(p => p.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)))
                packages.Add($"Microsoft.Extensions.Logging.Abstractions|{ExtensionsVersion}");
        }

        if (allSource.Contains("Microsoft.Extensions.Configuration"))
            packages.Add($"Microsoft.Extensions.Configuration|{ExtensionsVersion}");

        return (packages, frameworks);
    }

    private static string GenerateCsproj(string name, string targetFramework, bool isExe, HashSet<string> references, bool isTestProject = false)
    {
        var projectDir = string.Empty; // not used here; packages inferred by caller
        return GenerateCsproj(name, targetFramework, isExe, references, new HashSet<string>(), new HashSet<string>(), isTestProject);
    }

    private static string GenerateCsproj(string name, string targetFramework, bool isExe, HashSet<string> references, HashSet<string> packages, HashSet<string> frameworks, bool isTestProject = false)
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

        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    /// <summary>
    /// Merge missing package/framework/project references into an existing .csproj without
    /// destroying the model's authored references or target framework.
    /// </summary>
    private static void MergeMissingRefsIntoCsproj(
        string csprojPath,
        string projectName,
        List<string> allProjects,
        string contextDir,
        HashSet<string> inferredPackages,
        HashSet<string> inferredFrameworks,
        HashSet<string>? inferredProjectRefs = null)
    {
        var xml = File.ReadAllText(csprojPath);
        var refs = inferredProjectRefs ?? InferReferences(projectName, allProjects, contextDir);

        // Helper: parse existing package references by name.
        var existingPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pkgMatches = System.Text.RegularExpressions.Regex.Matches(xml, "<PackageReference\\s+([^\u003e]+)/\u003e");
        foreach (System.Text.RegularExpressions.Match m in pkgMatches)
        {
            var attrs = m.Groups[1].Value;
            var incMatch = System.Text.RegularExpressions.Regex.Match(attrs, "Include=\"([^\"]+)\"");
            var verMatch = System.Text.RegularExpressions.Regex.Match(attrs, "Version=\"([^\"]+)\"");
            if (incMatch.Success && verMatch.Success)
                existingPackages[incMatch.Groups[1].Value] = verMatch.Groups[1].Value;
        }

        bool HasInclude(string elementName, string includeValue)
        {
            var pattern = $"<{elementName}\\s+[^\u003e]*Include=\"{System.Text.RegularExpressions.Regex.Escape(includeValue)}\"";
            return System.Text.RegularExpressions.Regex.IsMatch(xml, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        static bool IsHigherVersion(string candidate, string current)
        {
            if (System.Version.TryParse(candidate, out var cV) && System.Version.TryParse(current, out var eV))
                return cV > eV;
            return true;
        }

        var packagesToAdd = inferredPackages.Where(p =>
        {
            var name = p.Split('|')[0];
            if (!existingPackages.TryGetValue(name, out var existingVersion))
                return true;
            return IsHigherVersion(p.Split('|')[1], existingVersion);
        }).ToList();

        var frameworksToAdd = inferredFrameworks.Where(f => !HasInclude("FrameworkReference", f)).ToList();

        // Resolve the actual .csproj file inside a referenced directory and break cycles.
        string? ResolveTargetCsproj(string targetDirName)
        {
            var targetDir = Path.Combine(contextDir, targetDirName);
            if (!Directory.Exists(targetDir)) return null;
            var candidates = Directory.GetFiles(targetDir, "*.csproj", SearchOption.AllDirectories);
            return candidates.Length == 0 ? null : candidates.First();
        }

        bool TargetReferencesCurrent(string targetDirName)
        {
            var targetCsproj = ResolveTargetCsproj(targetDirName);
            if (targetCsproj is null) return false;
            var targetXml = File.ReadAllText(targetCsproj);
            var thisAssumedPath = $"../{projectName}/{projectName}.csproj";
            return targetXml.Contains(thisAssumedPath, StringComparison.OrdinalIgnoreCase)
                || targetXml.Contains($"../{projectName}/", StringComparison.OrdinalIgnoreCase);
        }

        var projectRefsToAdd = refs.Where(r =>
        {
            var assumed = $"../{r}/{r}.csproj";
            if (xml.Contains(assumed, StringComparison.OrdinalIgnoreCase))
                return false;

            var targetCsproj = ResolveTargetCsproj(r);
            if (targetCsproj is null) return false;

            // Avoid circular references: if the target already references us, don't reference back.
            if (TargetReferencesCurrent(r)) return false;

            var expectedRelative = Path.GetRelativePath(Path.GetDirectoryName(csprojPath)!, targetCsproj).Replace('\\', '/');
            return !xml.Contains(expectedRelative, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        if (packagesToAdd.Count == 0 && frameworksToAdd.Count == 0 && projectRefsToAdd.Count == 0)
            return;

        var insert = new StringBuilder();
        if (projectRefsToAdd.Count > 0)
        {
            insert.AppendLine("  <ItemGroup>");
            foreach (var r in projectRefsToAdd)
            {
                var targetCsproj = ResolveTargetCsproj(r)!;
                var refPath = Path.GetRelativePath(Path.GetDirectoryName(csprojPath)!, targetCsproj).Replace('\\', '/');
                insert.AppendLine($"    <ProjectReference Include=\"{refPath}\" />");
            }
            insert.AppendLine("  </ItemGroup>");
        }
        if (frameworksToAdd.Count > 0)
        {
            insert.AppendLine("  <ItemGroup>");
            foreach (var fw in frameworksToAdd)
                insert.AppendLine($"    <FrameworkReference Include=\"{fw}\" />");
            insert.AppendLine("  </ItemGroup>");
        }
        if (packagesToAdd.Count > 0)
        {
            insert.AppendLine("  <ItemGroup>");
            foreach (var pkg in packagesToAdd)
            {
                var parts = pkg.Split('|');
                var name = parts[0];
                var version = parts[1];
                if (existingPackages.TryGetValue(name, out var oldVersion))
                {
                    // Upgrade existing reference in place rather than appending a duplicate.
                    xml = xml.Replace($"Version=\"{oldVersion}\"", $"Version=\"{version}\"", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    insert.AppendLine($"    <PackageReference Include=\"{name}\" Version=\"{version}\" />");
                }
            }
            insert.AppendLine("  </ItemGroup>");
        }

        // Insert before the closing </Project> tag.
        var closing = "</Project>";
        var idx = xml.LastIndexOf(closing, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            xml = xml[..idx] + insert.ToString() + "\n" + xml[idx..];
        }
        else
        {
            xml += "\n" + insert.ToString() + "</Project>";
        }

        // Ensure IsTestProject if project name suggests tests and source contains xUnit attributes.
        if (!xml.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase))
        {
            var projectDir = Path.GetDirectoryName(csprojPath)!;
            if (IsTestProjectDirectory(projectDir, projectName))
            {
                var propClose = "</PropertyGroup>";
                var propIdx = xml.IndexOf(propClose, StringComparison.OrdinalIgnoreCase);
                if (propIdx >= 0)
                {
                    xml = xml[..(propIdx + propClose.Length)] + "\n  <PropertyGroup>\n    <IsPackable>false</IsPackable>\n    <IsTestProject>true</IsTestProject>\n  </PropertyGroup>" + xml[(propIdx + propClose.Length)..];
                }
            }
        }

        File.WriteAllText(csprojPath, xml);
    }

    private static string GenerateSolution(string contextDir, string targetFramework)
    {
        var projectDirs = Directory.GetDirectories(contextDir, "*", SearchOption.TopDirectoryOnly)
            .Where(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories).Any())
            .Select(d => Path.GetFileName(d)!)
            .ToList();

        // Generate missing .csproj files only for dirs that don't already have one.
        foreach (var projectName in projectDirs)
        {
            var projectDir = Path.Combine(contextDir, projectName);
            if (Directory.GetFiles(projectDir, "*.csproj", SearchOption.AllDirectories).Any())
                continue;

            var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
            var isExe = File.Exists(Path.Combine(projectDir, "Program.cs"));
            var isTest = IsTestProjectDirectory(projectDir, projectName);
            var refs = InferReferences(projectName, projectDirs, contextDir);
            var (pkgs, fws) = InferPackagesAndFrameworks(projectDir, isTest, targetFramework);
            File.WriteAllText(csprojPath, GenerateCsproj(projectName, targetFramework, isExe, refs, pkgs, fws, isTest));
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

        // After possible renames, merge any missing inferred package/framework/project references
        // into existing .csproj files without destroying the model's authored references.
        foreach (var csproj in Directory.GetFiles(contextDir, "*.csproj", SearchOption.AllDirectories))
        {
            var projectDir = Path.GetDirectoryName(csproj)!;
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            var isTest = IsTestProjectDirectory(projectDir, projectName);
            var refs = InferReferences(projectName, uniqueProjects.Select(p => p.Name).ToList(), contextDir);
            var (pkgs, fws) = InferPackagesAndFrameworks(projectDir, isTest, targetFramework);
            MergeMissingRefsIntoCsproj(csproj, projectName, uniqueProjects.Select(p => p.Name).ToList(), contextDir, pkgs, fws, refs);
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
