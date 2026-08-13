using System.Text;
using System.Text.Json;
using Posit.AI.Models;
using Posit.Data.Repositories;
using Posit.Tools;
using Posit.Contracts.Serialization;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Phases;

/// <summary>
/// C# Implementation — Pass 2. Imp writes C# that plugs into the
/// partial class extern holes from Pass 1's translated Dafny output,
/// plus complete C# classes for io-shell modules.
///
/// No Z3 — this is unverified I/O. The build judge checks compilation.
/// On build failure, correction signal with compiler errors (existing
/// Shepherd pattern).
///
/// Model: glm-5.2:cloud
/// </summary>
public sealed class CSharpImplementationPhase : IPhase
{
    private static readonly JsonSerializerOptions JsonOptions = Options;

    private readonly IModelGateway _gateway;
    private readonly PatternRegistry _registry;
    private const int MaxRetries = 2;

    public CSharpImplementationPhase(IModelGateway gateway, PatternRegistry? registry = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _registry = registry ?? new PatternRegistry(FindPatternsDirectory());
    }

    private static string FindPatternsDirectory()
    {
        var searchRoots = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        };

        foreach (var root in searchRoots)
        {
            var dir = root;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, "patterns");
                if (Directory.Exists(candidate))
                    return candidate;
                var parent = Directory.GetParent(dir);
                if (parent is null)
                    break;
                dir = parent.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Could not locate patterns/ directory relative to assembly (base={AppContext.BaseDirectory}, cwd={Environment.CurrentDirectory}).");
    }

    public PhaseId Id => new("csharp-implementation");
    public PhaseName Name => new("C# Implementation (Pass 2)");
    public PhaseId[] Dependencies => [new PhaseId("dafny-implementation")];

    public ArtifactSchema OutputSchema => new()
    {
        Kind = ArtifactKind.SourceCodeBundle,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = "Posit.Contracts.Artifacts.SourceCodeBundle"
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct)
    {
        // Extract translated C# from Pass 1 + io-shell module specs from Architecture
        var (translatedFiles, ioShellSpecs) = ExtractInputs(context);

        if (translatedFiles.Count == 0 && ioShellSpecs.Count == 0)
        {
            Console.Error.WriteLine("[Posit] C# Implementation — no translated C# or io-shell specs found");
            return new PhaseResult
            {
                PhaseId = Id,
                Status = PhaseStatus.Success,
                Artifacts = CreateEmptyBundle(context),
                Costs = CostSnapshot.Zero,
                AttemptNumber = context.AttemptNumber
            };
        }

        var allFiles = new List<SourceCodeFile>();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        // Pass 2a: Fill extern holes in translated Dafny C#
        if (translatedFiles.Count > 0)
        {
            Console.Error.WriteLine($"[Posit] C# Implementation — filling {translatedFiles.Count} extern portal files...");
            var (files, inTok, outTok) = await ImplementExternPortalsAsync(context, translatedFiles, ct);
            allFiles.AddRange(files);
            totalInputTokens += inTok;
            totalOutputTokens += outTok;
        }

        // Pass 2b: Write C# for io-shell modules
        if (ioShellSpecs.Count > 0)
        {
            Console.Error.WriteLine($"[Posit] C# Implementation — writing {ioShellSpecs.Count} io-shell modules...");
            var (files, inTok, outTok) = await ImplementIoShellsAsync(context, ioShellSpecs, ct);
            allFiles.AddRange(files);
            totalInputTokens += inTok;
            totalOutputTokens += outTok;
        }

        // Pass 2c: Wire components together — generate one Wire.cs per seam
        var arch = GetArchitectureContract(context);
        if (arch?.Components is { Length: > 0 } && translatedFiles.Count > 0)
        {
            Console.Error.WriteLine("[Posit] C# Implementation — wiring components from dependency graph...");
            var wiringFiles = GenerateWiring(arch, translatedFiles);
            foreach (var wf in wiringFiles)
            {
                allFiles.Add(wf);
                Console.Error.WriteLine($"[Posit] C# Implementation — wiring file: {wf.Path}");
            }
            Console.Error.WriteLine($"[Posit] C# Implementation — {wiringFiles.Count} wiring files generated");
        }

        Console.Error.WriteLine($"[Posit] C# Implementation — {allFiles.Count} C# files produced");

        // Carapace enforcement: check 200-line cap on generated C# files
        foreach (var file in allFiles)
        {
            var lineCount = file.Content.Split('\n').Length;
            if (lineCount > 200)
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — CARAPACE WARNING: {file.Path} exceeds 200-line cap ({lineCount} lines). May need decomposition.");
            }
        }

        var bundlePayload = new SourceCodeBundle
        {
            Files = [.. allFiles],
            ProjectPath = "src/Generated",
            TargetFramework = "net10.0"
        };

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(bundlePayload, JsonOptions);
        var bundle = new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = Id,
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.SourceCodeBundle,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = context.InputArtifacts
                .Select(a => new ArtifactReference(a.Id, a.Kind, a.SchemaVersion))
                .ToArray()
        };

        return new PhaseResult
        {
            PhaseId = Id,
            Status = PhaseStatus.Success,
            Artifacts = bundle,
            Costs = new CostSnapshot
            {
                InputTokens = totalInputTokens,
                OutputTokens = totalOutputTokens,
                ModelTier = context.ModelRoute.Tier
            },
            AttemptNumber = context.AttemptNumber
        };
    }

    /// <summary>
    /// Extract translated C# from Pass 1 (DafnyVerification artifacts)
    /// and io-shell module specs from Architecture artifacts.
    /// </summary>
    private static (List<(string ModuleName, string CSharpPath)> Translated, List<Component> IoShells) ExtractInputs(PhaseContext context)
    {
        var translated = new List<(string, string)>();
        var ioShells = new List<Component>();
        var ioShellNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: collect io-shell component names from architecture contract
        foreach (var artifact in context.InputArtifacts)
        {
            try
            {
                if (artifact.Kind == ArtifactKind.ArchitectureContract)
                {
                    var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);
                    var archContract = JsonSerializer.Deserialize<ArchitectureContract>(json, JsonOptions);
                    if (archContract?.Components is not null)
                    {
                        foreach (var c in archContract.Components)
                        {
                            if (c.Classification == ModuleClassification.IoShell)
                            {
                                ioShells.Add(c);
                                ioShellNames.Add(c.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — failed to parse architecture artifact: {ex.Message}");
            }
        }

        // Second pass: collect translated Dafny modules, EXCLUDING io-shell components
        // (io-shell components have Dafny skeletons now, but their C# stubs come from
        // the io-shell path, not the Dafny extern path — avoids duplicate definitions)
        foreach (var artifact in context.InputArtifacts)
        {
            try
            {
                if (artifact.Kind == ArtifactKind.DafnyVerification)
                {
                    var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);
                    var results = JsonSerializer.Deserialize<DafnyVerificationResult[]>(json, JsonOptions);
                    if (results is not null)
                    {
                        foreach (var r in results)
                        {
                            if (r.IsVerified && !string.IsNullOrWhiteSpace(r.TranslatedCSharpPath)
                                && File.Exists(r.TranslatedCSharpPath)
                                && !ioShellNames.Contains(r.ModuleName))
                            {
                                translated.Add((r.ModuleName, r.TranslatedCSharpPath!));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — failed to parse Dafny verification artifact: {ex.Message}");
            }
        }

        return (translated, ioShells);
    }

    private async Task<(List<SourceCodeFile>, int, int)> ImplementExternPortalsAsync(
        PhaseContext context, List<(string ModuleName, string CSharpPath)> translatedFiles, CancellationToken ct)
    {
        var files = new List<SourceCodeFile>();
        var totalInput = 0;
        var totalOutput = 0;
        var arch = GetArchitectureContract(context);
        var componentNames = GetComponentNames(context);

        foreach (var (moduleName, csharpPath) in translatedFiles)
        {
            var component = arch?.Components?.FirstOrDefault(c => string.Equals(c.Name, moduleName, StringComparison.OrdinalIgnoreCase));
            var stubs = component is not null ? _registry.SelectCSharpStubs(component) : [];

            if (stubs.Count > 0)
            {
                foreach (var stub in stubs)
                {
                    var rendered = PatternRegistry.RenderCSharpStub(stub, moduleName);
                    var fileName = $"{moduleName}Extern.cs";
                    files.Add(new SourceCodeFile($"{moduleName}/{fileName}", rendered));
                    Console.Error.WriteLine($"[Posit] C# Implementation — '{moduleName}' registry stub '{stub.Name}' -> {fileName}");
                }
            }
            else
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — '{moduleName}' has no registered C# stub cap; skipping");
            }
        }

        return (files, totalInput, totalOutput);
    }

    private async Task<(List<SourceCodeFile>, int, int)> ImplementIoShellsAsync(
        PhaseContext context, List<Component> ioShells, CancellationToken ct)
    {
        var files = new List<SourceCodeFile>();
        var totalInput = 0;
        var totalOutput = 0;
        var componentNames = GetComponentNames(context);

        foreach (var shell in ioShells)
        {
            var stubs = _registry.SelectCSharpStubs(shell);
            if (stubs.Count == 0)
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — io-shell '{shell.Name}' has no registered C# stub cap");
                continue;
            }

            foreach (var stub in stubs)
            {
                var rendered = PatternRegistry.RenderCSharpStub(stub, shell.Name);
                var fileName = $"{shell.Name}.{stub.Name}.cs";
                files.Add(new SourceCodeFile($"{shell.Name}/{fileName}", rendered));
                Console.Error.WriteLine($"[Posit] C# Implementation — io-shell '{shell.Name}' registry stub '{stub.Name}' -> {fileName}");
            }
        }

        return (files, totalInput, totalOutput);
    }

    private static ArchitectureContract? GetArchitectureContract(PhaseContext context)
    {
        foreach (var artifact in context.InputArtifacts)
        {
            if (artifact.Kind != ArtifactKind.ArchitectureContract)
                continue;
            try
            {
                var json = Encoding.UTF8.GetString(artifact.PayloadJson);
                return JsonSerializer.Deserialize<ArchitectureContract>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — failed to parse architecture contract: {ex.Message}");
            }
        }
        return null;
    }

    private static IReadOnlySet<string> GetComponentNames(PhaseContext context)
    {
        var components = context.DesignContext?.Components;
        if (components is null || components.Length == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(components.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildExternPrompt(string moduleName, string csharpPath, string translatedCSharp)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the C# Implementation phase (Pass 2). Your only job is to supply C# bodies for the {:extern} portal methods declared in the translated Dafny C#.");
        sb.AppendLine("The translated file below already contains all verified types, namespaces, and method signatures. Do NOT redeclare them.");
        sb.AppendLine("Do NOT emit a copy of the translated skeleton. Do NOT create a separate project or folder.");
        sb.AppendLine("Do NOT invent new module names or directories that are not in the architecture contract.");
        sb.AppendLine("Do NOT nest component directories inside other component directories (e.g. 'Contracts/Core/'). Each component is a top-level directory only.");
        sb.AppendLine("Do NOT emit duplicate implementations for the same component. One component = one directory.");
        sb.AppendLine("Do NOT emit test files, xUnit attributes, or test classes — QA handles tests.");
        sb.AppendLine("Do NOT emit .csproj files or project files with .cs extensions.");
        sb.AppendLine("Emit files ONLY under the canonical module directory for this extern portal.");
        sb.AppendLine("Emit a single C# file containing only the partial class / __default implementations needed for each {:extern} method.");
        sb.AppendLine();
        sb.AppendLine($"--- MODULE: {moduleName} ---");
        sb.AppendLine($"The translated C# file is at: {csharpPath}");
        sb.AppendLine("--- TRANSLATED C# (authority for signatures only) ---");
        sb.AppendLine(translatedCSharp);
        sb.AppendLine();
        sb.AppendLine("Respond with a single JSON array of {path, content} file objects. Use path like '\"CsvParser/CsvParserExtern.cs\"'. No markdown, no prose.");

        return sb.ToString();
    }

    private static string BuildExternCorrectionPrompt(string moduleName, string translatedCSharp, string error)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the C# Implementation phase (Pass 2). Your previous response could not be parsed.");
        sb.AppendLine("Return ONLY a valid JSON array of {path, content} file objects. No prose, no markdown fences.");
        sb.AppendLine();
        sb.AppendLine($"--- MODULE: {moduleName} ---");
        sb.AppendLine(error);
        sb.AppendLine();
        sb.AppendLine("--- TRANSLATED C# ---");
        sb.AppendLine(translatedCSharp[..Math.Min(2000, translatedCSharp.Length)]);
        sb.AppendLine();
        sb.AppendLine("Respond with valid JSON only.");
        return sb.ToString();
    }

    private static string BuildIoShellPrompt(Component shell)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the C# Implementation phase (Pass 2). Write a complete C# class for this io-shell module.");
        sb.AppendLine("This module does I/O — file reading, database, HTTP, console output. No Dafny, no verification.");
        sb.AppendLine($"You are authorized to emit files ONLY under the '{shell.Name}/' directory.");
        sb.AppendLine("Do NOT create new project directories (e.g. MigrationRunner, TodoApiImplementation, etc.) that are not explicitly in the architecture contract.");
        sb.AppendLine("Do NOT move the entry point (Program.cs) to a different directory or namespace.");
        sb.AppendLine("Do NOT nest component directories inside other component directories (e.g. 'Contracts/Core/'). Each component is a top-level directory only.");
        sb.AppendLine("Do NOT emit duplicate implementations for the same component. One component = one directory.");
        sb.AppendLine($"Therefore the main class MUST NOT be named exactly '{shell.Name}'; use '{shell.Name}Service' or '{shell.Name}Impl' instead.");
        sb.AppendLine("Do NOT emit test files, xUnit attributes, or test classes — QA handles tests.");
        sb.AppendLine("Do NOT emit .csproj files or project files with .cs extensions.");
        sb.AppendLine();
        sb.AppendLine($"Module: {shell.Name}");
        sb.AppendLine($"Responsibility: {shell.Responsibility}");
        sb.AppendLine($"Public Surface: {string.Join(", ", shell.PublicSurface)}");
        if (!string.IsNullOrWhiteSpace(shell.Internals))
            sb.AppendLine($"Internals: {shell.Internals}");
        if (shell.Dependencies.Length > 0)
            sb.AppendLine($"Dependencies: {string.Join(", ", shell.Dependencies)}");
        sb.AppendLine();
        sb.AppendLine("Respond with a single JSON array of {path, content} file objects. Do not include markdown fences or prose.");

        return sb.ToString();
    }

    private static string BuildIoShellCorrectionPrompt(Component shell, string error)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the C# Implementation phase (Pass 2). Your previous response could not be parsed.");
        sb.AppendLine("Return ONLY a valid JSON array of {path, content} file objects. No prose, no markdown fences.");
        sb.AppendLine();
        sb.AppendLine(error);
        sb.AppendLine();
        sb.AppendLine($"Module: {shell.Name}");
        sb.AppendLine($"Responsibility: {shell.Responsibility}");
        sb.AppendLine($"Public Surface: {string.Join(", ", shell.PublicSurface)}");
        sb.AppendLine();
        sb.AppendLine("Respond with valid JSON only.");
        return sb.ToString();
    }

    /// <summary>
    /// Parse the model's JSON response into SourceCodeFile records.
    /// Handles files[] array format and single-file format.
    /// The gateway already stripped reasoning tags and extracted JSON, so we
    /// try direct parsing first and only fall back to extraction on failure.
    /// </summary>
    private static List<SourceCodeFile> ParseFileOutput(string text, string moduleName, IReadOnlySet<string> componentNames)
    {
        var files = new List<SourceCodeFile>();

        if (string.IsNullOrWhiteSpace(text))
            return files;

        // The gateway already runs StripReasoningTags + ExtractJson.
        // Try the provided text directly first.
        if (TryParseFiles(text, files))
        {
            files = NormalizeAndFilter(files, moduleName, componentNames);
            return files;
        }

        // Fallback: re-extract if the gateway's extraction missed or the model
        // wrapped multiple JSON blobs in prose.
        var cleaned = OllamaModelGateway.StripReasoningTags(text);
        var json = OllamaModelGateway.ExtractJson(cleaned);
        if (json != text)
        {
            files.Clear();
            if (TryParseFiles(json, files))
            {
                files = NormalizeAndFilter(files, moduleName, componentNames);
                return files;
            }
        }

        Console.Error.WriteLine($"[Posit] C# Implementation — could not parse any files for '{moduleName}'");
        return files;
    }

    /// <summary>
    /// Normalize generated paths so extern/implementation fragments land in the
    /// parent module folder. Drop raw skeleton duplicates the model sometimes emits.
    /// Drop files that nest another authorized component as a subdirectory — that
    /// is a stray/duplicate implementation, not part of this module.
    /// </summary>
    private static List<SourceCodeFile> NormalizeAndFilter(List<SourceCodeFile> files, string moduleName, IReadOnlySet<string> componentNames)
    {
        var normalized = new List<SourceCodeFile>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var rel = file.Path.Replace('\\', '/').TrimStart('/');
            var parts = rel.Split('/').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (parts.Length == 0) continue;

            // Skip test files emitted by the model by mistake
            var lastPart = parts[^1];
            if (lastPart.Contains("Test", StringComparison.OrdinalIgnoreCase))
                continue;

            // Carapace rule: the C# Implementation phase inlays FUNCTION only.
            // Project files (.csproj), solution files (.sln), and MSBuild imports (.props/.targets)
            // are prefabricated by the orchestrator/verifier from the architecture contract.
            // Drop any such files the model emits, regardless of path.
            var ext = Path.GetExtension(lastPart).ToLowerInvariant();
            if (ext is ".csproj" or ".sln" or ".props" or ".targets")
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — dropping '{rel}': project/solution/MSBuild files are prefabricated, not emitted by C# Implementation");
                continue;
            }

            // Also drop any source file whose content is actually a project file body.
            if (file.Content.TrimStart().StartsWith("\u003cProject", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — dropping '{rel}': content is a project file body, not C# source");
                continue;
            }

            // Reject paths whose first directory is not the current module (or Shared).
            // Files belong to exactly one module; do not pull strays from sibling modules.
            var firstDir = StripDirectoryNoise(parts[0]);
            if (!string.Equals(firstDir, moduleName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(firstDir, "Shared", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — dropping '{rel}': first directory '{firstDir}' is not module '{moduleName}'");
                continue;
            }

            // Reject project/solution files that are not placed in their own component directory.
            // e.g. "Contracts/CLI.csproj" is a stray skeleton; a CLI project file must live under "CLI/".
            // Solution files must live at the root, never inside a component folder.
            var fileExt = Path.GetExtension(lastPart).ToLowerInvariant();
            if (fileExt == ".csproj")
            {
                var projectName = InferProjectNameFromCsproj(lastPart);
                if (!string.Equals(projectName, moduleName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(projectName, firstDir, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[Posit] C# Implementation — dropping '{rel}': .csproj '{lastPart}' belongs to project '{projectName}', not module '{moduleName}'");
                    continue;
                }
            }
            else if (fileExt == ".sln")
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — dropping '{rel}': solution files must be at the generated root, not inside a component directory");
                continue;
            }

            // Reject paths that nest another authorized component as a subdirectory.
            // e.g. "Shared/Core/..." when Core is a component is also disallowed.
            bool nestsAnotherComponent = false;
            for (int i = 1; i < parts.Length - 1; i++)
            {
                var dir = StripDirectoryNoise(parts[i]);
                if (componentNames.Contains(dir) && !string.Equals(dir, moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[Posit] C# Implementation — dropping '{rel}': nests component '{dir}' inside '{moduleName}'");
                    nestsAnotherComponent = true;
                    break;
                }
            }
            if (nestsAnotherComponent)
                continue;

            // Strip implementation/extern noise from intermediate directories
            var cleanedParts = new List<string>();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                cleanedParts.Add(StripDirectoryNoise(parts[i]));
            }
            cleanedParts.Add(parts[^1]);

            // Coerce back to module root if the path strayed into a sibling project
            var rootDir = cleanedParts[0];
            var isStrayProject = rootDir.EndsWith(".Implementation", StringComparison.OrdinalIgnoreCase)
                || rootDir.EndsWith(".Implementations", StringComparison.OrdinalIgnoreCase)
                || rootDir.EndsWith(".extern", StringComparison.OrdinalIgnoreCase);

            if (isStrayProject)
            {
                cleanedParts[0] = StripDirectoryNoise(rootDir);
            }

            // Avoid class/namespace collision for io-shell modules
            var fileName = cleanedParts[^1];
            var cleanName = StripDirectoryNoise(Path.GetFileNameWithoutExtension(fileName));
            if (string.Equals(cleanName, moduleName, StringComparison.OrdinalIgnoreCase)
                && !fileName.Contains("Extern")
                && !fileName.Contains("skeleton-"))
            {
                fileName = $"{cleanName}Impl.cs";
                cleanedParts[^1] = fileName;
            }

            // Coerce back into the canonical module folder
            if (cleanedParts.Count == 1)
            {
                cleanedParts.Insert(0, moduleName);
            }
            else if (!string.Equals(cleanedParts[0], moduleName, StringComparison.OrdinalIgnoreCase))
            {
                cleanedParts[0] = moduleName;
            }

            var newRel = string.Join('/', cleanedParts);
            if (seenPaths.Add(newRel))
                normalized.Add(new SourceCodeFile(newRel, file.Content));
        }

        return normalized;
    }

    private static string StripDirectoryNoise(string name)
    {
        foreach (var suffix in new[] { ".Implementation", ".Implementations", ".extern" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }
        return name;
    }

    /// <summary>
    /// Infer the canonical component/project name from a .csproj file name.
    /// Strips common solution prefixes such as "WorkflowEngine." so that
    /// "WorkflowEngine.Contracts.csproj" maps to "Contracts".
    /// </summary>
    private static string InferProjectNameFromCsproj(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        // Strip the longest known solution prefix if present.
        // We accept any "Word." prefix as a solution name and keep the trailing token,
        // because the model may invent arbitrary solution names.
        var dot = name.IndexOf('.');
        if (dot > 0 && dot < name.Length - 1)
            return name[(dot + 1)..];
        return name;
    }

    private static bool TryParseFiles(string json, List<SourceCodeFile> files)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    var path = element.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var content = element.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(content))
                        files.Add(new SourceCodeFile(path, content));
                }
                return files.Count > 0;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("files", out var filesArr))
                {
                    foreach (var element in filesArr.EnumerateArray())
                    {
                        var path = element.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                        var content = element.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(content))
                            files.Add(new SourceCodeFile(path, content));
                    }
                }
                else
                {
                    var path = root.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(content))
                        files.Add(new SourceCodeFile(path, content));
                }
                return files.Count > 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] C# Implementation — file parse attempt failed: {ex.Message}");
        }

        return false;
    }

    private static ArtifactBundle CreateEmptyBundle(PhaseContext context)
    {
        var emptyBundle = new SourceCodeBundle
        {
            Files = [],
            ProjectPath = "src/Generated",
            TargetFramework = "net10.0"
        };
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(emptyBundle, JsonOptions);
        return new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = new PhaseId("csharp-implementation"),
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.SourceCodeBundle,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = []
        };
    }

    public Task<ValidationResult> ValidateOutputAsync(ArtifactBundle output, CancellationToken ct)
    {
        var errors = new List<string>();

        if (output.Kind != ArtifactKind.SourceCodeBundle)
            errors.Add("validation.schema_mismatch: Kind");
        if (output.SchemaVersion != "1.0.0")
            errors.Add("validation.schema_mismatch: SchemaVersion");

        try
        {
            var bundle = JsonSerializer.Deserialize<SourceCodeBundle>(output.PayloadJson, JsonOptions);
            if (bundle is null)
                errors.Add("validation.missing_required_field: Payload");
        }
        catch (JsonException ex)
        {
            errors.Add($"validation.schema_mismatch: {ex.Message}");
        }

        return Task.FromResult(new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray()
        });
    }

    /// <summary>
    /// Generate wiring code that connects components based on the architecture's
    /// connector specifications. The CLI entry point calls the top-level component,
    /// which calls its dependencies per the connection specs — all the way down.
    ///
    /// This is DETERMINISTIC — no model call, no judgment. It reads the connector
    /// forms (methodSignatures + connections + sharedTypes) from the carapace and
    /// generates real C# wiring code with actual method calls and type conversions.
    /// One Wire.cs per component with connections — each seam wires locally.
    /// </summary>
    private List<SourceCodeFile> GenerateWiring(
        ArchitectureContract arch,
        List<(string ModuleName, string CSharpPath)> translatedFiles)
    {
        var result = new List<SourceCodeFile>();
        var components = arch.Components;
        if (components.Length == 0) return result;

        // Build a lookup: component name → component
        var componentByName = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in components)
            componentByName[c.Name] = c;

        // Find the CLI component (has publicSurface containing "Program" or is classified io-shell with console)
        var cliComponent = components.FirstOrDefault(c =>
            c.PublicSurface?.Contains("Program") == true ||
            (c.Classification == ModuleClassification.IoShell &&
             c.StubNames?.Any(s => s.Contains("console") || s.Contains("io-console")) == true));

        // If no CLI, find the top of the dependency graph (component that nothing depends on)
        if (cliComponent is null)
        {
            var dependedUpon = new HashSet<string>(
                components.SelectMany(c => c.Dependencies ?? []),
                StringComparer.OrdinalIgnoreCase);
            cliComponent = components.FirstOrDefault(c => !dependedUpon.Contains(c.Name));
        }

        if (cliComponent is null) return result;

        // Translated module names for using statements
        var translatedNames = new HashSet<string>(
            translatedFiles.Select(t => t.ModuleName),
            StringComparer.OrdinalIgnoreCase);

        // Collect all shared types across components
        var allSharedTypes = new HashSet<(string Type, string Module)>();
        foreach (var comp in components)
        {
            if (comp.SharedTypes is not null)
            {
                foreach (var st in comp.SharedTypes)
                    allSharedTypes.Add((st.TypeName, st.DefinedInModule));
            }
        }

        // Generate one Wire.cs per component that has connections + method signatures
        var componentsWithConnections = components
            .Where(c => c.Connections?.Length > 0 && c.MethodSignatures?.Length > 0)
            .ToList();

        foreach (var comp in componentsWithConnections)
        {
            var isCli = string.Equals(comp.Name, cliComponent.Name, StringComparison.OrdinalIgnoreCase);
            var wireFile = GenerateComponentWiring(
                comp, isCli, cliComponent, components, componentByName, translatedNames);
            if (wireFile is not null)
                result.Add(wireFile);
        }

        Console.Error.WriteLine($"[Posit] Wiring — generated {result.Count} Wire.cs files ({componentsWithConnections.Count} components with connections)");
        return result;
    }

    /// <summary>
    /// Generate a single Wire.cs for one component. If isCli is true, this is the
    /// program entry point with Run(string[] args). Otherwise it's a wiring class
    /// that chains this component's connections to its dependencies.
    /// </summary>
    private SourceCodeFile? GenerateComponentWiring(
        Component comp,
        bool isCli,
        Component cliComponent,
        Component[] allComponents,
        Dictionary<string, Component> componentByName,
        HashSet<string> translatedNames)
    {
        var hasConnectorSpecs = comp.MethodSignatures?.Length > 0;
        var hasConnections = comp.Connections?.Length > 0;

        if (!hasConnectorSpecs)
        {
            Console.Error.WriteLine($"[Posit] Wiring — REJECT: '{comp.Name}' has no methodSignatures. Architecture contract should have been rejected at validation.");
            return null;
        }

        if (!hasConnections)
        {
            Console.Error.WriteLine($"[Posit] Wiring — REJECT: '{comp.Name}' has methodSignatures but no connections. Cannot wire without connection specs.");
            return null;
        }

        Console.Error.WriteLine($"[Posit] Wiring — generating wiring for '{comp.Name}' ({comp.Connections!.Length} connections, isCli={isCli})");

        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated wiring file — DETERMINISTIC from carapace connector specs.");
        sb.AppendLine("// The orchestrator read methodSignatures + connections from the architecture contract");
        sb.AppendLine("// and generated real C# calls with type conversions. No model judgment.");
        sb.AppendLine();

        // Using statements for connection targets only (not ALL translated modules —
        // that pulls in unnecessary references like shared type modules)
        // Dafny modules use _module_{Name} namespace; io-shell modules use {Name} namespace
        var connectionTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conn in comp.Connections!)
        {
            if (!string.IsNullOrWhiteSpace(conn.ToComponent))
                connectionTargets.Add(conn.ToComponent);
        }
        // Also include this component's own namespace (for the entry call)
        connectionTargets.Add(comp.Name);
        
        foreach (var c in allComponents)
        {
            if (connectionTargets.Contains(c.Name))
            {
                // io-shell components use namespace {ComponentName}, not _module_{ComponentName}
                if (c.Classification == ModuleClassification.IoShell)
                    sb.AppendLine($"using {c.Name};");
                else
                    sb.AppendLine($"using _module_{c.Name};");
            }
        }

        // Dafny runtime types
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine();

        // Namespace is this component's name
        sb.AppendLine($"namespace {comp.Name}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Wiring for {comp.Name} — connects to its dependencies per carapace connector specs.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class Wire");
        sb.AppendLine("    {");

        // Find the entry method signature for this component.
        // Use the PATTERN's actual signature (from registry) if available —
        // the architect may only list 1-2 semantic params, but the real pattern
        // method (e.g. HandleRequest) takes 6. We need ALL params to compile.
        var entrySigs = comp.MethodSignatures!;
        var entrySig = entrySigs.FirstOrDefault() ?? entrySigs[0];
        var entryMethodName = entrySig.PatternMethod ?? entrySig.Name;
        var entryParams = entrySig.Params;

        // Try to get the full pattern signature — it has the real param list.
        var patternFullSigs = GetPatternSignaturesForComponent(comp);
        if (patternFullSigs is { Count: > 0 })
        {
            var patternSig = patternFullSigs.FirstOrDefault(s =>
                string.Equals(s.Name, entryMethodName, StringComparison.OrdinalIgnoreCase))
                ?? patternFullSigs[0];
            entryParams = patternSig.Params;
            // Keep the architect's method name as the call target (PatternMethod maps it)
            if (entrySig.PatternMethod is string pm)
                entryMethodName = pm;
            else
                entryMethodName = patternSig.Name;
        }

        if (isCli)
        {
            // CLI Wire.cs — program entry point with Run(string[] args)
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Calls {comp.Name}.{entryMethodName}() — the program's main entry point.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static int Run(string[] args)");
            sb.AppendLine("        {");

            // Generate arg parsing from CLI args
            sb.AppendLine("            if (args.Length == 0)");
            sb.AppendLine("            {");
            sb.AppendLine($"                System.Console.WriteLine(\"Usage: {comp.Name} <input>\");");
            sb.AppendLine("                return 1;");
            sb.AppendLine("            }");
            sb.AppendLine();

            // Convert CLI args to Dafny types for the entry method call.
            // Only the first N params come from CLI args (typically 1-2: input string).
            // The rest get type-appropriate defaults (e.g., delimiter="|", minFields=2,
            // maxFields=3, empty entity list, nextId=0). The pattern's requires
            // clauses constrain these, but defaults satisfy the basic contract.
            var paramInitLines = new List<string>();
            for (int i = 0; i < entryParams.Length; i++)
            {
                var param = entryParams[i];
                var dafnyType = param.DafnyType ?? param.Type;

                // First param is always the CLI input (args[0]).
                // Additional params get defaults — they're pattern configuration
                // (delimiter, minFields, maxFields, entities, nextId, etc.)
                if (i == 0)
                {
                    if (dafnyType == "string")
                    {
                        paramInitLines.Add($"            var {param.Name} = Dafny.Sequence<Dafny.Rune>.UnicodeFromString(args[0]);");
                    }
                    else if (dafnyType == "int")
                    {
                        paramInitLines.Add($"            var {param.Name} = BigInteger.Parse(args[0]);");
                    }
                    else if (dafnyType == "bool")
                    {
                        paramInitLines.Add($"            var {param.Name} = bool.Parse(args[0]);");
                    }
                    else
                    {
                        paramInitLines.Add($"            // TODO: convert args[0] to {dafnyType} for parameter '{param.Name}'");
                        paramInitLines.Add($"            var {param.Name} = default({MapDafnyTypeToCSharpWire(dafnyType)});");
                    }
                }
                else
                {
                    // Params beyond the first get type-appropriate defaults.
                    // These are pattern configuration params (delimiter, minFields, etc.)
                    var defaultVal = DefaultForDafnyType(dafnyType);
                    paramInitLines.Add($"            var {param.Name} = {defaultVal}; // default for {dafnyType}");
                }
            }
            foreach (var line in paramInitLines)
                sb.AppendLine(line);
            sb.AppendLine();

            // Generate the entry method call
            var paramNames = string.Join(", ", entryParams.Select(p => p.Name));
            var entryClass = $"_module_{comp.Name}.__default";
            sb.AppendLine($"            // Call the proven logic: {comp.Name}.{entryMethodName}({paramNames})");
            sb.AppendLine($"            var result = {entryClass}.{entryMethodName}({paramNames});");
            sb.AppendLine();

            // Generate connection calls
            AppendConnectionCalls(sb, comp, componentByName, entryParams);

            // Output the result
            sb.AppendLine("            // Output result");
            sb.AppendLine("            System.Console.WriteLine(result);");
            sb.AppendLine("            return 0;");
            sb.AppendLine("        }");
        }
        else
        {
            // Non-CLI Wire.cs — wiring method that chains this component's connections
            var paramNames = string.Join(", ", entryParams.Select(p => p.Name));
            var paramDecls = string.Join(", ", entryParams.Select(p => $"{MapDafnyTypeToCSharpWire(p.DafnyType ?? p.Type)} {p.Name}"));

            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Wires {comp.Name}'s connections to its dependencies.");
            sb.AppendLine($"        /// Chains {comp.Connections!.Length} connection calls per the carapace connector specs.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static void Wire_{comp.Name}({paramDecls})");
            sb.AppendLine("        {");

            // Generate the entry call + connection chain
            var entryClass = $"_module_{comp.Name}.__default";
            sb.AppendLine($"            // Call {comp.Name}.{entryMethodName}({paramNames})");
            sb.AppendLine($"            var result = {entryClass}.{entryMethodName}({paramNames});");
            sb.AppendLine();

            AppendConnectionCalls(sb, comp, componentByName, entryParams);

            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        var wiringPath = $"{comp.Name}/Wire.cs";
        var content = sb.ToString();
        Console.Error.WriteLine($"[Posit] Wiring — {wiringPath}: {content.Split('\n').Length} lines");
        return new SourceCodeFile(wiringPath, content);
    }

    /// <summary>
    /// Append the connection calls for a component, chaining return values
    /// and using positional fallback for unresolved arg mappings.
    /// </summary>
    private void AppendConnectionCalls(
        StringBuilder sb,
        Component comp,
        Dictionary<string, Component> componentByName,
        MethodParam[] entryParams)
    {
        sb.AppendLine("            // === Connection calls per carapace connector specs ===");

        // Track return variables: maps source field name → C# variable name
        var sourceToReturnVar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Track prior return variable names IN ORDER (for positional fallback)
        // Each entry is (varName, returnType) so the fallback can skip incompatible types
        var priorReturnVarOrder = new List<(string VarName, string ReturnType)>();

        // CLI input params are available as sources
        foreach (var p in entryParams)
            sourceToReturnVar[p.Name] = p.Name;

        foreach (var conn in comp.Connections!)
        {
            var toComp = componentByName.GetValueOrDefault(conn.ToComponent);
            if (toComp is null)
            {
                sb.AppendLine($"            // WARNING: connection to '{conn.ToComponent}' — component not found");
                continue;
            }

            // Resolve the actual Dafny method name to call on the target component.
            // The architect names the method (e.g., "Parse") but the pattern's real
            // method might be "HandleRequest". Try in order:
            //   1. target component's MethodSignatures[].PatternMethod
            //   2. pattern registry — find the method matching conn.ToMethod by name,
            //      or fall back to the pattern's first method (the universal entry point)
            //   3. conn.ToMethod as-is (last resort)
            var toMethod = conn.ToMethod;
            if (toComp.MethodSignatures is { Length: > 0 })
            {
                var targetSig = toComp.MethodSignatures.FirstOrDefault(s =>
                    string.Equals(s.Name, conn.ToMethod, StringComparison.OrdinalIgnoreCase));
                if (targetSig?.PatternMethod is string patternMethod && !string.IsNullOrWhiteSpace(patternMethod))
                    toMethod = patternMethod;
            }

            // If PatternMethod wasn't set (architect often leaves it blank or same as Name),
            // check the pattern registry for the real method names.
            if (toMethod == conn.ToMethod && !string.IsNullOrWhiteSpace(toComp.PatternName))
            {
                var patternSigs = GetPatternSignaturesForComponent(toComp);
                if (patternSigs is { Count: > 0 })
                {
                    // First: try exact name match in the pattern's methods
                    var patternMatch = patternSigs.FirstOrDefault(s =>
                        string.Equals(s.Name, conn.ToMethod, StringComparison.OrdinalIgnoreCase));
                    if (patternMatch is not null)
                    {
                        toMethod = patternMatch.Name;
                    }
                    else
                    {
                        // The architect's semantic name (e.g. "Parse") doesn't match any
                        // pattern method. The pattern's universal entry point is the first
                        // method (typically HandleRequest). Use that.
                        toMethod = patternSigs[0].Name;
                    }
                }
            }
            
            // Build the class reference: dafny modules use _module_X.__default,
            // io-shell modules use {ComponentName}.{StubClass} (e.g., CsvFileReader.FileIO)
            string toClass;
            if (toComp.Classification == ModuleClassification.IoShell)
            {
                // io-shell stubs define classes like FileIO, ConsoleIO, StreamIO
                // Find which stub class contains the target method by checking stub names
                var stubClassName = ResolveStubClass(toComp, toMethod);
                toClass = $"{conn.ToComponent}.{stubClassName}";
            }
            else
            {
                toClass = $"_module_{conn.ToComponent}.__default";
            }
            var connReturnType = conn.ReturnType ?? "var";
            var returnVarName = $"{conn.ToComponent.ToLowerInvariant()}Result";

            // Build argument list: look up the target method's ACTUAL full signature
            // from the pattern registry (or the target component's MethodSignatures),
            // then fill in ALL params positionally. Args from conn.ArgMappings are
            // mapped to params by position; any unmapped params get type-appropriate
            // defaults. This fixes the HandleRequest 6-param vs 1-2-arg mismatch.
            var targetFullSig = ResolveTargetSignature(toComp, toMethod);
            // If the target method returns void, override the connection's return type.
            // The architect may say "returns success" but PrintLine/Print return void.
            if (targetFullSig is not null && (
                string.IsNullOrWhiteSpace(targetFullSig.ReturnType) ||
                targetFullSig.ReturnType.Equals("void", StringComparison.OrdinalIgnoreCase)))
            {
                connReturnType = "void";
            }
            else if (toComp.Classification == ModuleClassification.IoShell)
            {
                // For io-shell methods without a resolved signature, check common
                // void-returning method names (Print, PrintLine, Write, Clear, etc.)
                var methodLower = toMethod.ToLowerInvariant();
                if (methodLower.Contains("print") || methodLower.Contains("write")
                    || methodLower == "clear" || methodLower.Contains("log"))
                    connReturnType = "void";
            }
            var resolvedArgs = BuildFullArgList(targetFullSig, conn.ArgMappings, sourceToReturnVar, priorReturnVarOrder, entryParams);

            var connArgsStr = string.Join(", ", resolvedArgs);
            sb.AppendLine($"            // {conn.FromMethod} → {conn.ToComponent}.{toMethod}({connArgsStr})");
            sb.AppendLine($"            // {conn.ReturnUsage ?? "result stored"}");

            if (connReturnType != "void")
            {
                sb.AppendLine($"            var {returnVarName} = {toClass}.{toMethod}({connArgsStr});");

                // Register this return variable for subsequent calls
                sourceToReturnVar[conn.ToComponent] = returnVarName;
                sourceToReturnVar[conn.ToComponent.ToLowerInvariant()] = returnVarName;
                priorReturnVarOrder.Add((returnVarName, connReturnType));

                // Map the return type name if specified
                if (!string.IsNullOrWhiteSpace(connReturnType) && connReturnType != "var")
                {
                    var simpleName = connReturnType.Split('<', '(', '.')[0].Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(simpleName) && simpleName != "void")
                        sourceToReturnVar[simpleName] = returnVarName;
                }
            }
            else
            {
                sb.AppendLine($"            {toClass}.{toMethod}({connArgsStr});");
            }
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Get the pattern's method signatures for a component, if it has a PatternName.
    /// Returns null if the component has no pattern or the pattern can't be found.
    /// </summary>
    private List<MethodSignature>? GetPatternSignaturesForComponent(Component comp)
    {
        if (string.IsNullOrWhiteSpace(comp.PatternName))
            return null;
        try
        {
            return _registry.GetPatternSignatures(comp.PatternName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] Wiring — could not get pattern signatures for '{comp.PatternName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve the actual full signature of a target method. Tries:
    /// 1. The pattern registry (if the component has a PatternName) — AUTHORITATIVE,
    ///    because the pattern has the real, complete param list. The architect's
    ///    MethodSignatures may be incomplete (they don't know the real signature).
    /// 2. The target component's MethodSignatures (fallback if no pattern)
    /// 3. Fallback: null (caller will use conn.ArgMappings as before)
    /// </summary>
    private MethodSignature? ResolveTargetSignature(Component toComp, string toMethod)
    {
        // First: look up the pattern's signatures from the registry — AUTHORITATIVE
        var patternSigs = GetPatternSignaturesForComponent(toComp);
        if (patternSigs is { Count: > 0 })
        {
            var sig = patternSigs.FirstOrDefault(s =>
                string.Equals(s.Name, toMethod, StringComparison.OrdinalIgnoreCase));
            if (sig is not null)
                return sig;
        }

        // Second: fall back to the target component's own MethodSignatures
        if (toComp.MethodSignatures is { Length: > 0 })
        {
            var sig = toComp.MethodSignatures.FirstOrDefault(s =>
                string.Equals(s.Name, toMethod, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.PatternMethod, toMethod, StringComparison.OrdinalIgnoreCase));
            if (sig is not null)
                return sig;
        }

        return null;
    }

    /// <summary>
    /// Build the full argument list for a method call, using the target method's
    /// actual signature. Each param is filled positionally:
    /// - If conn.ArgMappings has an entry for this position, resolve it to a variable.
    /// - If not, emit a type-appropriate default value.
    /// 
    /// This is the fix for the HandleRequest 6-param vs 1-2-arg mismatch: the pattern
    /// method takes 6 params but the connection spec only provides 1-2. Now ALL params
    /// get filled — mapped ones from arg mappings, unmapped ones from defaults.
    /// </summary>
    private static List<string> BuildFullArgList(
        MethodSignature? targetSig,
        string[]? argMappings,
        Dictionary<string, string> sourceToReturnVar,
        List<(string VarName, string ReturnType)> priorReturnVarOrder,
        MethodParam[] entryParams)
    {
        // If we don't have the full signature, fall back to the old behavior:
        // just use the arg mappings as-is
        if (targetSig is null || targetSig.Params.Length == 0)
        {
            var fallbackArgs = new List<string>();
            if (argMappings?.Length > 0)
            {
                foreach (var am in argMappings)
                {
                    var arrowIdx = am.IndexOf("->");
                    string source = arrowIdx > 0 ? am[..arrowIdx].Trim() : am.Trim();
                    if (sourceToReturnVar.TryGetValue(source, out var resolvedVar))
                        fallbackArgs.Add(resolvedVar);
                    else
                        fallbackArgs.Add($"/* unresolved: {source} */ null");
                }
            }
            return fallbackArgs;
        }

        // We have the full signature — build args positionally for ALL params
        var fullParams = targetSig.Params;
        var result = new List<string>(fullParams.Length);

        for (int i = 0; i < fullParams.Length; i++)
        {
            var param = fullParams[i];
            string? resolved = null;

            // Try to resolve from arg mappings (positional)
            if (argMappings is { Length: > 0 } && i < argMappings.Length)
            {
                var am = argMappings[i];
                var arrowIdx = am.IndexOf("->");
                string source = arrowIdx > 0 ? am[..arrowIdx].Trim() : am.Trim();

                if (sourceToReturnVar.TryGetValue(source, out var resolvedVar))
                {
                    // Type-check: only accept the mapped variable if its type
                    // is compatible with the target param's type. If the architect
                    // mapped a string source to a seq<seq<string>> param, the
                    // types don't match — skip and let fallback/defaults handle it.
                    var paramDafnyType = param.DafnyType ?? param.Type;
                    // Look up the source variable's type from priorReturnVarOrder.
                    // If not found there, it's an entry param (always string from CLI).
                    var sourceTypeInfo = priorReturnVarOrder.FirstOrDefault(v => v.VarName == resolvedVar);
                    var sourceType = sourceTypeInfo != default ? sourceTypeInfo.ReturnType : "string";
                    if (IsTypeCompatible(sourceType, paramDafnyType))
                    {
                        resolved = resolvedVar;
                    }
                    // else: type mismatch — leave resolved null for fallback
                }
            }

            // If not resolved from arg mappings, try matching by param name
            // (with the same type-check as arg mappings)
            if (resolved is null && sourceToReturnVar.TryGetValue(param.Name, out var nameMatch))
            {
                var paramDafnyType = param.DafnyType ?? param.Type;
                var sourceTypeInfo = priorReturnVarOrder.FirstOrDefault(v => v.VarName == nameMatch);
                var sourceType = sourceTypeInfo != default ? sourceTypeInfo.ReturnType : "string";
                if (IsTypeCompatible(sourceType, paramDafnyType))
                {
                    resolved = nameMatch;
                }
            }

            // If still not resolved, use positional fallback:
            // find the most recent prior call's return variable whose type
            // is compatible with the target param's type.
            // Skip validation/result types when the param expects data (seq, string, etc.)
            if (resolved is null)
            {
                var paramDafnyType = param.DafnyType ?? param.Type;
                var priorReturnVars = priorReturnVarOrder
                    .Where(v => !entryParams.Any(p => p.Name == v.VarName))
                    .Distinct()
                    .ToList();

                // Try type-compatible match first: prefer return vars whose type
                // matches the target param type
                var typeMatch = priorReturnVars.FirstOrDefault(v =>
                    IsTypeCompatible(v.ReturnType, paramDafnyType));

                if (typeMatch != default)
                {
                    resolved = typeMatch.VarName;
                }
                // If no type-compatible match, leave resolved null —
                // the default-value path will emit a type-appropriate default.
                // Do NOT fall back to any non-validation data var — that
                // causes CS1503 type mismatches at compile time.
            }

            // If still not resolved, emit a type-appropriate default
            if (resolved is null)
            {
                var dafnyType = param.DafnyType ?? param.Type;
                resolved = DefaultForDafnyType(dafnyType);
            }

            result.Add(resolved);
        }

        return result;
    }

    /// <summary>
    /// Check if a return type is a validation/result type (not data).
    /// Validation results, Result<T> wrappers, and bools are status signals,
    /// not data to pass downstream.
    /// </summary>
    private static bool IsValidationType(string returnType)
    {
        if (string.IsNullOrWhiteSpace(returnType))
            return false;
        var lower = returnType.ToLowerInvariant().Trim();
        return lower.Contains("validation") || lower.Contains("result") || lower == "bool"
            || lower.Contains("success") || lower.Contains("failure");
    }

    /// <summary>
    /// Check if a return type is compatible with a target parameter type.
    /// Uses recursive matching: seq&lt;X&gt; matches seq&lt;X&gt; but not seq&lt;seq&lt;X&gt;&gt;.
    /// </summary>
    private static bool IsTypeCompatible(string returnType, string paramType)
    {
        if (string.IsNullOrWhiteSpace(returnType) || string.IsNullOrWhiteSpace(paramType))
            return false;
        var r = returnType.ToLowerInvariant().Trim();
        var p = paramType.ToLowerInvariant().Trim();

        // Exact match
        if (r == p) return true;

        // Both seq-based — compare inner types recursively
        if (r.StartsWith("seq<") && r.EndsWith('>') && p.StartsWith("seq<") && p.EndsWith('>'))
        {
            var rInner = r[4..^1].Trim();
            var pInner = p[4..^1].Trim();
            return IsTypeCompatible(rInner, pInner);
        }

        // Both set-based — compare inner types recursively
        if (r.StartsWith("set<") && r.EndsWith('>') && p.StartsWith("set<") && p.EndsWith('>'))
        {
            var rInner = r[4..^1].Trim();
            var pInner = p[4..^1].Trim();
            return IsTypeCompatible(rInner, pInner);
        }

        // Both string-based
        if (r == "string" && p == "string") return true;

        // Both int-based
        if ((r == "int" || r == "bigint") && (p == "int" || p == "bigint")) return true;

        // Return type is "var" (unknown) — accept anything
        if (r == "var") return true;

        return false;
    }

    /// <summary>
    /// Emit a type-appropriate default value for a Dafny type in C#.
    /// Used to fill in unmapped params when the connection spec doesn't provide
    /// values for all of the target method's parameters.
    /// </summary>
    private static string DefaultForDafnyType(string dafnyType)
    {
        var t = dafnyType.Trim();

        // Strip whitespace and normalize
        if (t.StartsWith("seq<", StringComparison.Ordinal) && t.EndsWith('>'))
        {
            var inner = t[4..^1].Trim();
            return $"Dafny.Sequence<{MapDafnyTypeToCSharpWire(inner)}>.Empty";
        }
        if (t.StartsWith("set<", StringComparison.Ordinal) && t.EndsWith('>'))
        {
            var inner = t[4..^1].Trim();
            return $"Dafny.Set<{MapDafnyTypeToCSharpWire(inner)}>.Empty";
        }

        return t switch
        {
            "int" => "BigInteger.Zero",
            "bool" => "false",
            "string" => "Dafny.Sequence<Dafny.Rune>.UnicodeFromString(\"\")",
            _ => $"default({MapDafnyTypeToCSharpWire(t)})"
        };
    }

    /// <summary>
    /// Convert an argument source to a Dafny-compatible C# expression.
    /// Handles common conversions: string → Dafny.Sequence<Rune>.UnicodeFromString()
    /// </summary>
    private static string ConvertArgToDafny(string source)
    {
        // If it looks like a field reference (contains a dot), pass through
        if (source.Contains('.'))
            return source;

        // If it's a known variable, pass through
        return source;
    }

    /// <summary>
    /// Resolve which stub class contains a given method for an io-shell component.
    /// Stub names like "file-io" map to class names like "FileIO".
    /// The method name is matched against common method-to-stub associations.
    /// </summary>
    private static string ResolveStubClass(Component targetComp, string methodName)
    {
        // Map method name patterns to stub class names
        var methodLower = methodName.ToLowerInvariant();
        
        // file-io: ReadFile, WriteFile, AppendFile, ReadAllText, WriteAllText
        if (methodLower.Contains("file") || methodLower.Contains("read") && !methodLower.Contains("console"))
            return "FileIO";
        
        // console-io: Print, ReadLine, Clear, PrintLine
        if (methodLower.Contains("print") || methodLower.Contains("console") || methodLower.Contains("readline"))
            return "ConsoleIO";
        
        // stream-io: OpenStream, ReadChunk, CloseStream
        if (methodLower.Contains("stream") || methodLower.Contains("chunk"))
            return "StreamIO";
        
        // network-io: Get, Post, Put, Delete
        if (methodLower is "get" or "post" or "put" or "delete" or "http")
            return "NetworkIO";
        
        // database-io: Query, Execute, OpenConnection
        if (methodLower.Contains("query") || methodLower.Contains("execute") || methodLower.Contains("connection"))
            return "DatabaseIO";
        
        // time-random: GetTimestamp, Sleep, Random
        if (methodLower.Contains("time") || methodLower.Contains("sleep") || methodLower.Contains("random"))
            return "TimeRandom";
        
        // Fallback: use the first stub name, capitalized
        if (targetComp.StubNames?.Length > 0)
        {
            var stubName = targetComp.StubNames[0];
            return StubNameToClassName(stubName);
        }
        
        // Ultimate fallback — guess FileIO (most common)
        return "FileIO";
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

    /// <summary>
    /// Map Dafny types to C# type names for wiring code.
    /// </summary>
    private static string MapDafnyTypeToCSharpWire(string dafnyType)
    {
        var t = dafnyType.Trim();
        return t switch
        {
            "int" => "BigInteger",
            "bool" => "bool",
            "string" => "Dafny.ISequence<Dafny.Rune>",
            _ when t.StartsWith("seq<") => "Dafny.ISequence<" + MapDafnyTypeToCSharpWire(t[4..^1]) + ">",
            _ when t.StartsWith("set<") => "Dafny.ISet<" + MapDafnyTypeToCSharpWire(t[4..^1]) + ">",
            _ => t
        };
    }
}