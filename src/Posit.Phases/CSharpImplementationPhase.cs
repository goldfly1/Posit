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
        var dir = AppContext.BaseDirectory;
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
        throw new DirectoryNotFoundException("Could not locate patterns/ directory relative to assembly.");
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

        Console.Error.WriteLine($"[Posit] C# Implementation — {allFiles.Count} C# files produced");

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

        foreach (var artifact in context.InputArtifacts)
        {
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);

                if (artifact.Kind == ArtifactKind.DafnyVerification)
                {
                    var results = JsonSerializer.Deserialize<DafnyVerificationResult[]>(json, JsonOptions);
                    if (results is not null)
                    {
                        foreach (var r in results)
                        {
                            if (r.IsVerified && !string.IsNullOrWhiteSpace(r.TranslatedCSharpPath)
                                && File.Exists(r.TranslatedCSharpPath))
                                translated.Add((r.ModuleName, r.TranslatedCSharpPath!));
                        }
                    }
                }
                else if (artifact.Kind == ArtifactKind.ArchitectureContract)
                {
                    var archContract = JsonSerializer.Deserialize<ArchitectureContract>(json, JsonOptions);
                    if (archContract?.Components is not null)
                    {
                        foreach (var c in archContract.Components)
                        {
                            if (c.Classification == ModuleClassification.IoShell)
                                ioShells.Add(c);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Posit] C# Implementation — failed to parse artifact: {ex.Message}");
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
                var fileName = $"{shell.Name}{stub.Name.ToLowerInvariant().Replace("-", "")}.cs";
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
}