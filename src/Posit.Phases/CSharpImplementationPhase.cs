using System.Diagnostics;
using Posit.AI.Models;
using Posit.Contracts.Artifacts;

namespace Posit.Phases;

/// <summary>
/// Phase 2: C# Implementation. Three sub-steps:
/// (a) Model generates C# implementation for each logic component against the interface
/// (b) io-shell stubs from registry
/// (c) wiring via WiringGenerator (deterministic)
/// dotnet build is the compile gate — correction loop feeds compiler errors back to model.
/// </summary>
public sealed class CSharpImplementationPhase : IPhase
{
    private readonly IModelGateway _model;
    private readonly IPatternRegistry _registry;

    public CSharpImplementationPhase(IModelGateway model, IPatternRegistry registry)
    {
        _model = model;
        _registry = registry;
    }

    public PhaseId Id { get; } = new("csharp-implementation");
    public string Name => "C# Implementation";
    public PhaseId[] Dependencies { get; } = [new("architecture")];
    public ArtifactSchema OutputSchema { get; } = new()
    {
        Kind = ArtifactKind.SourceCodeBundle,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = nameof(SourceCodeBundle)
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        try
        {
            var contract = ExtractContract(context);
            if (contract == null)
                return Fail(context, "No ArchitectureContract in input artifacts");

            var files = new List<SourceCodeFile>();
            var warnings = new List<string>();

            // Write interface files from architect's CSharpInterface
            var stagingDir = GetStagingDir(context);
            Directory.CreateDirectory(stagingDir);
            foreach (var comp in contract.Components)
            {
                if (comp.Classification == ModuleClassification.IoShell) continue;
                if (!string.IsNullOrWhiteSpace(comp.CSharpInterface))
                {
                    var ifacePath = Path.Combine(stagingDir, $"I{comp.Name}.cs");
                    File.WriteAllText(ifacePath, comp.CSharpInterface);
                    files.Add(new SourceCodeFile($"{comp.Name}/I{comp.Name}.cs", comp.CSharpInterface));
                }
            }

            // (a) Model generates C# implementation for logic components
            foreach (var comp in contract.Components)
            {
                if (comp.Classification == ModuleClassification.IoShell)
                {
                    // (b) io-shell stubs from registry
                    foreach (var stubName in comp.StubNames)
                    {
                        if (stubName == "io-console-program")
                        {
                            warnings.Add($"Skipped io-console-program for '{comp.Name}' (entry point, not a stub)");
                            continue;
                        }
                        var stubContent = _registry.ComposeIoShellSkeleton(stubName, comp.Name);
                        var path = $"{comp.Name}/{comp.Name}.{stubName}.cs";
                        files.Add(new SourceCodeFile(path, stubContent));
                    }
                }
                else
                {
                    // Model writes the implementation against the interface
                    var impl = await GenerateImplementationAsync(comp, contract, context, ct);
                    if (string.IsNullOrWhiteSpace(impl))
                    {
                        warnings.Add($"No implementation generated for '{comp.Name}'");
                    }
                    else
                    {
                        files.Add(new SourceCodeFile($"{comp.Name}/{comp.Name}.cs", impl));

                        // Extern portal caps (stubs for I/O that the component calls)
                        foreach (var stubName in comp.StubNames)
                        {
                            var stubContent = _registry.ComposeIoShellSkeleton(stubName, comp.Name);
                            var path = $"{comp.Name}/{comp.Name}Extern.{stubName}.cs";
                            files.Add(new SourceCodeFile(path, stubContent));
                        }
                    }
                }
            }

            // (c) Wiring via deterministic WiringGenerator
            var translatedSigs = ScanSignatures(files, contract);
            var stubSigs = ScanStubSignatures(files, contract);
            var modelWirer = new ModelWiringGenerator(_model);
            foreach (var comp in contract.Components)
            {
                if (comp.Connections.Length == 0) continue;
                var wireContent = WiringGenerator.Generate(comp, contract, translatedSigs, stubSigs);
                if (string.IsNullOrWhiteSpace(wireContent))
                    wireContent = await modelWirer.GenerateAsync(comp, contract, translatedSigs, stubSigs, context, ct);
                if (!string.IsNullOrWhiteSpace(wireContent))
                    files.Add(new SourceCodeFile($"{comp.Name}/Wire.cs", wireContent));
            }

            // Static check C# files
            foreach (var f in files.Where(f => f.Path.EndsWith(".cs")))
            {
                var issues = StaticChecker.CheckCSharp(f.Content);
                if (issues.Count > 0)
                    warnings.Add(StaticChecker.FormatIssues(issues));
            }

            // Deduplicate by path, keep last occurrence
            var deduped = DeduplicateByPath(files);

            var bundle = new SourceCodeBundle
            {
                Files = deduped.ToArray(),
                ProjectPath = Directory.GetCurrentDirectory(),
                TargetFramework = "net10.0"
            };
            var payloadJson = JsonSerializer.SerializeToUtf8Bytes(bundle, PositJson.Options);

            return new PhaseResult
            {
                PhaseId = context.PhaseId,
                Status = PhaseStatus.Success,
                Artifacts = new ArtifactBundle
                {
                    Id = ArtifactId.New(),
                    SessionId = context.SessionId,
                    SourcePhase = context.PhaseId,
                    SchemaVersion = "1.0.0",
                    Kind = ArtifactKind.SourceCodeBundle,
                    PayloadJson = payloadJson,
                    ProducedAt = DateTimeOffset.UtcNow
                },
                Costs = CostSnapshot.Zero,
                Warnings = warnings.ToArray()
            };
        }
        catch (Exception ex)
        {
            return Fail(context, $"C# impl exception: {ex.Message}");
        }
    }

    private const int MaxBuildAttempts = 4;

    /// <summary>
    /// Model generates C# implementation for a logic component against its interface,
    /// with a dotnet build correction loop. The model generates code, we compile it
    /// against the interface in a temp project, and feed compiler errors back on retry.
    /// </summary>
    private async Task<string> GenerateImplementationAsync(
        Component comp, ArchitectureContract contract, PhaseContext context, CancellationToken ct)
    {
        var iface = comp.CSharpInterface ?? "";
        var responsibility = comp.Responsibility;
        var testCases = comp.TestCases.Length > 0
            ? string.Join("\n", comp.TestCases.Select(tc => $"  - {tc.Description} → {tc.ExpectedBehavior}"))
            : "(no test cases)";

        var basePrompt = $"""
            You are a Senior C# Developer. Implement the following C# interface.

            INTERFACE (implement this — match every method signature exactly):
            {iface}

            RESPONSIBILITY:
            {responsibility}

            TEST CASES (your implementation must pass these):
            {testCases}

            RULES:
            1. Create a class that implements the interface: class {comp.Name} : I{comp.Name}
            2. Implement EVERY method declared in the interface — same name, same parameters, same return type.
            3. Do NOT modify the interface. Do NOT add methods to the interface.
            4. Use standard C# — no external packages, no Dafny runtime types.
            5. Use `IReadOnlyList<string>` instead of `seq<string>`, `string` for text, `int`/`double`/`bool` for primitives.
            6. Handle edge cases: empty input, null where applicable, invalid data.
            7. Output ONLY the C# class file — no markdown fences, no explanations.
            8. Include `using` directives at the top.
            9. Put the class in `namespace {comp.Name}` — the wiring code references it as {comp.Name}.{comp.Name}.
            """;

        string? previousCode = null;
        string[]? buildErrors = null;

        for (var attempt = 1; attempt <= MaxBuildAttempts; attempt++)
        {
            var systemPrompt = basePrompt;

            if (buildErrors is { Length: > 0 })
            {
                systemPrompt += $"\n\nPREVIOUS CODE (had compile errors):\n{previousCode}";
                systemPrompt += $"\n\nCOMPILER ERRORS (fix these — keep everything else the same):\n{string.Join("\n", buildErrors)}";
            }
            else if (context.CorrectionSignal is { Length: > 0 } && attempt == 1)
            {
                systemPrompt += $"\n\nCORRECTION SIGNAL:\n{string.Join("\n", context.CorrectionSignal)}";
            }

            var prompt = new PromptTemplate
            {
                PhaseId = context.PhaseId,
                Version = new PromptVersion("1.0.0"),
                SystemPrompt = systemPrompt,
                OutputFormatSpec = "raw C# source code",
                ModelTier = ModelTier.Fast,
                Temperature = 0.1, // small flexibility — 0.0 was too deterministic (T3/T6 regressed)
                MaxOutputTokens = 8192,
                OutputFormat = OutputFormat.PlainText,
                OutputSchemaRef = "CSharpSource",
                Status = PromptStatus.Active
            };

            Console.Error.WriteLine($"[csharp-impl] {comp.Name} attempt {attempt}/{MaxBuildAttempts}");
            var result = await _model.GenerateAsync(context.ModelRoute, prompt, context, ct);
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                Console.Error.WriteLine($"[csharp-impl] {comp.Name} empty model output on attempt {attempt}");
                buildErrors = ["Model returned empty output"];
                continue;
            }

            var code = ExtractCSharp(result.Text);
            if (string.IsNullOrWhiteSpace(code))
            {
                Console.Error.WriteLine($"[csharp-impl] {comp.Name} no C# extracted on attempt {attempt}");
                buildErrors = ["No C# code found in model output"];
                continue;
            }

            // Static check first (free, instant)
            var staticIssues = StaticChecker.CheckCSharp(code);
            if (staticIssues.Count > 0)
            {
                Console.Error.WriteLine($"[csharp-impl] {comp.Name} static check failed on attempt {attempt}");
                buildErrors = staticIssues.Select(i => $"[{i.RuleId}] {i.Message}").ToArray();
                previousCode = code;
                continue;
            }

            // Compile in a temp project
            var (compiles, errors) = await TryCompileAsync(comp, code, iface, context, ct);
            if (compiles)
            {
                Console.Error.WriteLine($"[csharp-impl] {comp.Name} compiled successfully on attempt {attempt}");
                return code;
            }

            Console.Error.WriteLine($"[csharp-impl] {comp.Name} build failed on attempt {attempt}: {errors.Length} errors");
            buildErrors = errors;
            previousCode = code;
        }

        // All attempts failed — return the last code we got (if any)
        Console.Error.WriteLine($"[csharp-impl] {comp.Name} exhausted {MaxBuildAttempts} attempts, returning last output");
        return previousCode ?? "";
    }

    /// <summary>
    /// Compile generated C# against its interface in a temp project.
    /// Returns (true, []) on success, (false, errors) on failure.
    /// </summary>
    private static async Task<(bool Success, string[] Errors)> TryCompileAsync(
        Component comp, string implCode, string interfaceCode, PhaseContext context, CancellationToken ct)
    {
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var tempDir = Path.Combine(Path.GetTempPath(), "posit-build",
            $"{context.SessionId.Value}-{comp.Name}-{shortId}");

        try
        {
            Directory.CreateDirectory(tempDir);

            // Write interface file
            var ifacePath = Path.Combine(tempDir, $"I{comp.Name}.cs");
            File.WriteAllText(ifacePath, interfaceCode);

            // Write implementation file
            var implPath = Path.Combine(tempDir, $"{comp.Name}.cs");
            File.WriteAllText(implPath, implCode);

            // Write a minimal .csproj
            var csprojContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Library</OutputType>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="**\*.cs" />
                  </ItemGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(tempDir, $"{comp.Name}.csproj"), csprojContent);

            // Run dotnet build
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{Path.Combine(tempDir, $"{comp.Name}.csproj")}\" --nologo -v q",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
                return (true, []);

            // Parse errors from stdout/stderr
            var output = string.IsNullOrEmpty(stderr) ? stdout : stdout + "\n" + stderr;
            var errors = ParseBuildErrors(output);
            return (false, errors.Length > 0 ? errors : [output.Trim()]);
        }
        catch (Exception ex)
        {
            return (false, [$"Build exception: {ex.Message}"]);
        }
        finally
        {
            // Clean up temp dir
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch { }
        }
    }

    /// <summary>
    /// Parse dotnet build output for error messages.
    /// Lines with "error CS" are compiler errors.
    /// </summary>
    private static string[] ParseBuildErrors(string buildOutput)
    {
        var errors = new List<string>();
        foreach (var line in buildOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("error CS", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("error NET", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(trimmed);
            }
        }
        return errors.Count > 0
            ? errors.Distinct().ToArray()
            : [buildOutput.Trim()];
    }

    /// <summary>
    /// Extract C# code from model output (strip markdown fences if present).
    /// </summary>
    private static string ExtractCSharp(string text)
    {
        // Strip markdown code fences
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

    private static Dictionary<string, List<CsMethodSignature>> ScanSignatures(
        List<SourceCodeFile> files, ArchitectureContract contract)
    {
        var result = new Dictionary<string, List<CsMethodSignature>>();
        foreach (var comp in contract.Components)
        {
            var file = files.FirstOrDefault(f => f.Path == $"{comp.Name}/{comp.Name}.cs");
            if (file != null)
                result[comp.Name] = TranslatedCSharpScanner.ScanContent(file.Content);
        }
        return result;
    }

    private static Dictionary<string, List<CsMethodSignature>> ScanStubSignatures(
        List<SourceCodeFile> files, ArchitectureContract contract)
    {
        var result = new Dictionary<string, List<CsMethodSignature>>();
        foreach (var comp in contract.Components)
        {
            var prefix = $"{comp.Name}/";
            var stubFiles = files.Where(f => f.Path.StartsWith(prefix) && f.Path != $"{comp.Name}/{comp.Name}.cs");
            var sigs = new List<CsMethodSignature>();
            foreach (var f in stubFiles)
                sigs.AddRange(TranslatedCSharpScanner.ScanContent(f.Content));
            result[comp.Name] = sigs;
        }
        return result;
    }

    private static string GetStagingDir(PhaseContext context) =>
        Path.Combine(Directory.GetCurrentDirectory(), ".posit", "staging",
            context.SessionId.Value, "csharp");

    private static List<SourceCodeFile> DeduplicateByPath(List<SourceCodeFile> files)
    {
        var dict = new Dictionary<string, SourceCodeFile>();
        foreach (var f in files)
            dict[f.Path] = f; // keep last
        return [.. dict.Values];
    }

    private static ArchitectureContract? ExtractContract(PhaseContext ctx)
    {
        foreach (var a in ctx.InputArtifacts)
            if (a.Kind == ArtifactKind.ArchitectureContract)
                try { return JsonSerializer.Deserialize<ArchitectureContract>(a.PayloadJson, PositJson.Options); }
                catch { }
        return null;
    }

    private static PhaseResult Fail(PhaseContext ctx, string error) => new()
    {
        PhaseId = ctx.PhaseId, Status = PhaseStatus.Failed,
        Artifacts = Empty(ctx), Costs = CostSnapshot.Zero, Warnings = [error]
    };

    private static ArtifactBundle Empty(PhaseContext ctx) => new()
    {
        Id = ArtifactId.New(), SessionId = ctx.SessionId, SourcePhase = ctx.PhaseId,
        SchemaVersion = "1.0.0", Kind = ArtifactKind.SourceCodeBundle,
        PayloadJson = [], ProducedAt = DateTimeOffset.UtcNow
    };

    public ValidationResult ValidateOutput(PhaseResult result)
    {
        if (result.Status != PhaseStatus.Success)
            return new ValidationResult { IsValid = false, Errors = result.Warnings };
        return new ValidationResult { IsValid = true };
    }
}