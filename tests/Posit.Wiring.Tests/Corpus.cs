using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;
using Posit.Phases;

/// <summary>
/// The corpus: every Wire.cs shape observed in trials T1/T6/T8/T12 plus the
/// T12 failure shape that produced `error CS1026: ) expected` ×8.
/// Each entry builds a complete temp project and returns the project dir.
/// </summary>
public static class Corpus
{
    public static Dictionary<string, Func<string>> All => new()
    {
        // ── Case 1: T8 LogAnalyzer — ReadLines stub → logic(string[], string) ──
        ["T8_readlines_to_logic"] = () =>
        {
            var logic = """
namespace LogAnalyzer {
    public interface ILogAnalyzer { int CountLines(string[] lines, string level); }
    public class LogAnalyzer : ILogAnalyzer {
        public int CountLines(string[] lines, string level) {
            var n = 0;
            foreach (var l in lines) if (l.Contains(level)) n++;
            return n;
        }
    }
}
""";
            var stub = """
namespace LogAnalyzer {
    public static class LogAnalyzerFileIo {
        public static string[] ReadLines(string path) => System.IO.File.ReadAllLines(path);
    }
}
""";
            var comp = new Component("cli", "LogCli", "orchestrator", ["Run"], "", [], 0, "C#")
            {
                EntryType = "file",
                Connections = [new ConnectionSpec("Run", "LogAnalyzer", "CountLines", [])],
            };
            var logicComp = new Component("logic", "LogAnalyzer", "counts", ["CountLines"], "", ["LogCli"], 1, "C#")
            { Classification = ModuleClassification.Logic };
            var contract = new ArchitectureContract
            {
                SystemContext = "log counting", Components = [comp, logicComp],
            };
            return WriteProject("LogCli", "LogCli", logic,
                stub: stub, wire: WiringGenerator.Generate(comp, contract,
                    ImplSigs("LogAnalyzer", "CountLines", "int", [("string[]", "lines"), ("string", "level")]),
                    StubSigs("LogAnalyzer", "LogAnalyzerFileIo", "ReadLines", "string[]", [("string", "path")],
                             second: ("ReadFile", "string", [("string", "path")]))),
                extraFiles: [("LogAnalyzer/LogAnalyzer.cs", logic)]); // interface + class in one file
        },

        // ── Case 2: T12 ConfigMerger — ParseIni → MergeConfigs (Dictionary chain) ──
        // This is the EXACT failure shape: Wire.cs(18,69) CS1026 — ParseIni's
        // Dictionary<string,string> output was never chained into MergeConfigs.
        ["T12_dictionary_chain"] = () =>
        {
            var iface = """
namespace ConfigMerger {
    public class MergeResult {
        public System.Collections.Generic.Dictionary<string, string> Merged { get; set; }
        public System.Collections.Generic.List<string> Conflicts { get; set; }
    }
    public interface IConfigMerger {
        System.Collections.Generic.Dictionary<string, string> ParseIni(string content);
        MergeResult MergeConfigs(System.Collections.Generic.Dictionary<string, string> file1,
            System.Collections.Generic.Dictionary<string, string> file2);
    }
}
""";
            var impl = """
using System.Collections.Generic;
namespace ConfigMerger {
    public class ConfigMerger : IConfigMerger {
        public Dictionary<string, string> ParseIni(string content) {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(content)) return d;
            foreach (var line in content.Split('\n')) {
                var t = line.Trim(); var eq = t.IndexOf('=');
                if (eq > 0) d[t[..eq].Trim()] = t[(eq + 1)..].Trim();
            }
            return d;
        }
        public MergeResult MergeConfigs(Dictionary<string, string> file1, Dictionary<string, string> file2) {
            var m = new Dictionary<string, string>(file1 ?? new());
            var conflicts = new List<string>();
            if (file2 != null)
                foreach (var kvp in file2) {
                    if (m.ContainsKey(kvp.Key) && m[kvp.Key] != kvp.Value)
                        conflicts.Add($"{kvp.Key}: {m[kvp.Key]} -> {kvp.Value}");
                    m[kvp.Key] = kvp.Value;
                }
            return new MergeResult { Merged = m, Conflicts = conflicts };
        }
    }
}
""";
            var comp = new Component("cli", "ConfigCli", "orchestrator", ["Run"], "", [], 0, "C#")
            {
                EntryType = "file",
                Connections =
                [
                    new ConnectionSpec("Run", "ConfigMerger", "ParseIni", []),
                    new ConnectionSpec("Run", "ConfigMerger", "MergeConfigs", []),
                ],
            };
            var logicComp = new Component("logic", "ConfigMerger", "merge", ["ParseIni"], "", ["ConfigCli"], 1, "C#")
            { Classification = ModuleClassification.Logic };
            var contract = new ArchitectureContract
            {
                SystemContext = "config merging", Components = [comp, logicComp],
            };
            return WriteProject("ConfigCli", "ConfigCli", impl,
                wire: WiringGenerator.Generate(comp, contract,
                    ImplSigs("ConfigMerger", "ParseIni", "Dictionary<string, string>", [("string", "content")],
                             second: ("MergeConfigs", "MergeResult",
                                      [("Dictionary<string,string>", "file1"), ("Dictionary<string,string>", "file2")])),
                    stubSignatures: []),
                extraFiles: [("ConfigMerger/IConfigMerger.cs", iface), ("ConfigMerger/ConfigMerger.cs", impl)]);
        },

        // ── Case 3: T6 TempConverter — stdin → logic(double, string, string) ──
        ["T6_stdin_multiargs"] = () =>
        {
            var impl = """
namespace TempConverter {
    public interface ITempConverter { double Convert(double temp, string fromUnit, string toUnit); }
    public class TempConverter : ITempConverter {
        public double Convert(double temp, string fromUnit, string toUnit) {
            double c = fromUnit == "F" ? (temp - 32) * 5.0 / 9.0 : temp;
            return toUnit == "F" ? c * 9.0 / 5.0 + 32 : c;
        }
    }
}
""";
            var comp = new Component("cli", "TempCli", "orchestrator", ["Run"], "", [], 0, "C#")
            {
                EntryType = "stdin",
                Connections = [new ConnectionSpec("Run", "TempConverter", "Convert", [])],
            };
            var logicComp = new Component("logic", "TempConverter", "converts", ["Convert"], "", ["TempCli"], 1, "C#")
            { Classification = ModuleClassification.Logic };
            var contract = new ArchitectureContract
            {
                SystemContext = "temperature conversion", Components = [comp, logicComp],
            };
            return WriteProject("TempCli", "TempCli", impl,
                wire: WiringGenerator.Generate(comp, contract,
                    ImplSigs("TempConverter", "Convert", "double",
                             [("double", "temp"), ("string", "fromUnit"), ("string", "toUnit")]), []),
                extraFiles: [("TempConverter/TempConverter.cs", impl)]);
        },

        // ── Case 4: T8 file/scalar boundary (commit a529667) ──────────────────
        // File-entry CLI where the logic takes (string fileContent, string scalar).
        // First string param reads the file via ReadAllText; the SUBSEQUENT string
        // param must be args[i] PASSTHROUGH — not File.ReadAllText'd (T8 a1/a2
        // read the level word as a path → count 0). Asserts the emitted wire.
        ["T8_file_scalar_mix"] = () =>
        {
            var impl = """
namespace LogFilter {
    public interface ILogFilter { int CountByLevel(string content, string level); }
    public class LogFilter : ILogFilter {
        public int CountByLevel(string content, string level) {
            if (string.IsNullOrEmpty(content)) return 0;
            var n = 0;
            foreach (var line in content.Split('\n'))
                if (line.Split(' ') is { Length: >= 2 } parts && parts[1] == level) n++;
            return n;
        }
    }
}
""";
            var comp = new Component("cli", "LogCli", "orchestrator", ["Run"], "", [], 0, "C#")
            {
                EntryType = "file",
                Connections = [new ConnectionSpec("Run", "LogFilter", "CountByLevel", [])],
            };
            var logicComp = new Component("logic", "LogFilter", "counts", ["CountByLevel"], "", ["LogCli"], 1, "C#")
            { Classification = ModuleClassification.Logic };
            var contract = new ArchitectureContract
            { SystemContext = "log level counting", Components = [comp, logicComp] };
            var wire = WiringGenerator.Generate(comp, contract,
                ImplSigs("LogFilter", "CountByLevel", "int", [("string", "content"), ("string", "level")]), []);
            // Deterministic assertions on the emitted wiring (shape AND absence):
            if (!wire.Contains("File.ReadAllText(args[0])"))
                throw new InvalidOperationException("a529667 regression: first string param must read file content");
            if (!wire.Contains("scalarArg1 = args.Length > 1 ? args[1]"))
                throw new InvalidOperationException("a529667 regression: second string param must be scalar args[1] passthrough");
            if (wire.Contains("ReadAllText(args[1])"))
                throw new InvalidOperationException("a529667 regression: scalar arg must NOT be read as a file");
            return WriteProject("LogCli", "LogCli", impl, wire: wire,
                extraFiles: [("LogFilter/LogFilter.cs", impl)]);
        },

        // ── Case 5: T8 known-gap documentation — IoShell stub path double-read ──
        // NOT yet fixed (role-dispatch refactor is the fix): the file-param rule
        // reads args[0] as CONTENT, then hands it to an IoShell stub whose param
        // is a PATH (FileIO.ReadFile(path)). This corpus case exists to document
        // the shape and fail the gate the day the fix lands with different
        // semantics than expected — it asserts the CURRENT behavior so the
        // role-dispatch refactor must consciously update this case.
        ["T8_stub_path_double_read"] = () =>
        {
            var stub = """
using System.IO;
namespace LogTools {
    public static class FileIO {
        public static string ReadFile(string path) => File.ReadAllText(path);
    }
}
""";
            var comp = new Component("cli", "LogCli", "orchestrator", ["Run"], "", [], 0, "C#")
            {
                EntryType = "file",
                Connections = [new ConnectionSpec("Run", "LogTools", "ReadFile", [])],
            };
            var logicComp = new Component("logic", "LogTools", "reads", ["ReadFile"], "", ["LogCli"], 1, "C#")
            { Classification = ModuleClassification.IoShell };
            var contract = new ArchitectureContract
            { SystemContext = "file read", Components = [comp, logicComp] };
            var wire = WiringGenerator.Generate(comp, contract,
                StubSigs("LogTools", "FileIO", "ReadFile", "string", [("string", "path")]), []);
            // CURRENT (buggy) behavior — asserted so a future fix flips this loudly:
            if (!wire.Contains("File.ReadAllText(args[0])"))
                throw new InvalidOperationException("unexpected: double-read case changed shape before role-dispatch fix");
            if (!wire.Contains("ReadFile(fileContent0)"))
                throw new InvalidOperationException("unexpected: double-read case changed shape before role-dispatch fix");
            return WriteProject("LogCli", "LogCli", stub, stub: stub, wire: wire);
        },
    };

    // ── helpers ────────────────────────────────────────────────────────────────

    internal static Dictionary<string, List<CsMethodSignature>> ImplSigs(
        string comp, string name, string ret, (string type, string pname)[] ps,
        (string name, string ret, (string, string)[])? second = null)
    {
        var list = new List<CsMethodSignature>
        {
            new(name, ret, ps.Select(p => p.type).ToArray(), ps.Select(p => p.pname).ToArray(),
                [], [], comp, comp),
        };
        if (second is { } s)
            list.Add(new CsMethodSignature(s.name, s.ret, s.Item3.Select(p => p.Item1).ToArray(),
                s.Item3.Select(p => p.Item2).ToArray(), [], [], comp, comp));
        return new Dictionary<string, List<CsMethodSignature>> { [comp] = list };
    }

    internal static Dictionary<string, List<CsMethodSignature>> StubSigs(
        string comp, string cls, string name, string ret, (string type, string pname)[] ps,
        (string name, string ret, (string, string)[])? second = null)
    {
        var list = new List<CsMethodSignature>
        {
            new(name, ret, ps.Select(p => p.type).ToArray(), ps.Select(p => p.pname).ToArray(),
                [], [], cls, comp),
        };
        if (second is { } s)
            list.Add(new CsMethodSignature(s.name, s.ret, s.Item3.Select(p => p.Item1).ToArray(),
                s.Item3.Select(p => p.Item2).ToArray(), [], [], cls, comp));
        return new Dictionary<string, List<CsMethodSignature>> { [comp] = list };
    }

    /// <summary>Write a complete buildable temp project: csproj + files + wire.cs.</summary>
    internal static string WriteProject(
        string projName, string exeName, string logicContent,
        string? stub = null, string? wire = null,
        (string path, string content)[]? extraFiles = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "posit-wiring-tests", projName + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, projName));

        var files = new List<(string, string)>();
        if (extraFiles != null) files.AddRange(extraFiles);
        if (stub != null) files.Add(($"{projName}/{projName}.file-io.cs", stub));
        if (wire != null) files.Add(($"{projName}/Wire.cs", wire));

        foreach (var (path, content) in files)
        {
            var fp = Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fp)!);
            File.WriteAllText(fp, content);
        }

        // csproj
        var csprojContent = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../*/*.cs" Exclude="../**/obj/**;../**/bin/**" />
  </ItemGroup>
</Project>
""";
        File.WriteAllText(Path.Combine(dir, projName, $"{projName}.csproj"), csprojContent.TrimStart());
        return Path.Combine(dir, projName);
    }
}