using System.Text;
using System.Text.RegularExpressions;
using Posit.Contracts.Artifacts;

namespace Posit.Phases;

/// <summary>
/// Reads actual translated C# files from disk and extracts real method
/// signatures. This replaces guessing from pattern files — we see what
/// Dafny actually emitted and wire against that.
///
/// Two file types:
/// - Dafny-translated C#: namespace _module_X, class __default, static methods
/// - io-shell stubs: namespace X, class ConsoleIO/FileIO/etc, static methods
/// </summary>
public sealed class TranslatedCSharpScanner
{
    /// <summary>
    /// A real method signature extracted from translated C#.
    /// </summary>
    public record CsMethod(
        string Name,
        string ReturnType,
        string[] ParamTypes,
        string[] ParamNames,
        string[] GenericParams,  // e.g. ["__T"] for UnwrapOr<__T>
        bool IsStatic,
        string ClassName,        // e.g. "__default" or "ConsoleIO"
        string Namespace);       // e.g. "_module_CsvParser" or "ConsoleOutput"

    /// <summary>
    /// Scan a translated C# file and extract all public static method signatures.
    /// </summary>
    public List<CsMethod> Scan(string csharpPath)
    {
        if (!File.Exists(csharpPath))
            return new List<CsMethod>();

        var content = File.ReadAllText(csharpPath);
        return ScanContent(content);
    }

    /// <summary>
    /// Scan C# source content and extract method signatures.
    /// </summary>
    public List<CsMethod> ScanContent(string content)
    {
        var methods = new List<CsMethod>();
        var lines = content.Split('\n');

        // Track current namespace and class context
        string? currentNs = null;
        string? currentClass = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();

            // Track namespace
            if (line.StartsWith("namespace "))
            {
                currentNs = ExtractNamespace(line);
                continue;
            }

            // Track class declaration
            if (line.Contains("class ") && (line.Contains("public") || line.Contains("partial")))
            {
                currentClass = ExtractClassName(line);
                continue;
            }

            // Look for public static method declarations
            // Pattern: public static ReturnType MethodName(params)
            // Or:     public static ReturnType MethodName<T>(params)
            if (line.StartsWith("public static ") && !line.Contains("class "))
            {
                var method = ParseMethodLine(line, currentClass, currentNs);
                if (method is not null)
                    methods.Add(method);
            }
        }

        return methods;
    }

    private static string? ExtractNamespace(string line)
    {
        // "namespace _module_CsvParser {" or "namespace ConsoleOutput {"
        var match = Regex.Match(line, @"namespace\s+(\S+)\s*\{?");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractClassName(string line)
    {
        // "public partial class __default {" or "public static partial class ConsoleIO"
        var match = Regex.Match(line, @"class\s+(\S+?)[\s<{]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static CsMethod? ParseMethodLine(string line, string? className, string? ns)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(ns))
            return null;

        // Skip Dafny runtime helpers (DowncastClone, InitNewArray, etc.)
        if (ns == "Dafny" || ns.StartsWith("Dafny."))
            return null;

        // Skip TypeDescriptor/Default/DowncastClone — these are runtime, not logic
        if (line.Contains("DowncastClone") || line.Contains("InitNewArray")
            || line.Contains("_TypeDescriptor"))
            return null;

        // Join continuation lines if the method signature spans multiple lines
        // (Dafny output sometimes wraps long signatures)
        var fullLine = line;
        // Check if parens are balanced; if not, we'd need to join — but Dafny
        // output typically keeps signatures on one line. Skip if unparseable.

        // Pattern: public static [ReturnType] [MethodName][<Generics>](params)
        // Examples:
        //   public static Dafny.ISequence<Dafny.Rune> GetDelimiter(Dafny.ISequence<Dafny.Rune> delimiter)
        //   public static void PrintLine(string message)
        //   public static __T UnwrapOr<__T>(_IResult<__T> r, __T @default)

        var match = Regex.Match(fullLine,
            @"public\s+static\s+(.+?)\s+(\w+)(?:<([^>]+)>)?\s*\((.*)\)");
        if (!match.Success)
            return null;

        var returnType = match.Groups[1].Value.Trim();
        var methodName = match.Groups[2].Value.Trim();
        var genericParams = match.Groups[3].Success
            ? match.Groups[3].Value.Split(',').Select(p => p.Trim()).ToArray()
            : Array.Empty<string>();
        var paramStr = match.Groups[4].Value.Trim();

        var (paramTypes, paramNames) = ParseParams(paramStr);

        return new CsMethod(
            methodName,
            returnType,
            paramTypes,
            paramNames,
            genericParams,
            IsStatic: true,
            className!,
            ns!);
    }

    private static (string[] types, string[] names) ParseParams(string paramStr)
    {
        if (string.IsNullOrWhiteSpace(paramStr))
            return (Array.Empty<string>(), Array.Empty<string>());

        var types = new List<string>();
        var names = new List<string>();

        // Split on commas, but respect nested generics (Foo<Bar, Baz>)
        var parts = SplitParams(paramStr);
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            // Parameter is "Type Name" — split from the right
            // Handle: Dafny.ISequence<Dafny.Rune> delimiter
            // Handle: __T @default
            // Handle: BigInteger minLength
            // Handle: _IResult<__T> r
            var lastSpace = FindLastSpaceBeforeName(p);
            if (lastSpace > 0)
            {
                var type = p[..lastSpace].Trim();
                var name = p[(lastSpace + 1)..].Trim();
                // Strip @ from parameter names (C# reserved words)
                name = name.TrimStart('@');
                types.Add(type);
                names.Add(name);
            }
            else
            {
                // Can't split — use whole as type, empty name
                types.Add(p);
                names.Add("");
            }
        }

        return (types.ToArray(), names.ToArray());
    }

    /// <summary>
    /// Split parameter string on commas, respecting nested angle brackets.
    /// e.g. "Dafny.ISequence<Dafny.Rune> input, BigInteger minLength" → 2 parts
    /// </summary>
    private static List<string> SplitParams(string paramStr)
    {
        var parts = new List<string>();
        var depth = 0;
        var sb = new StringBuilder();

        foreach (var c in paramStr)
        {
            if (c == '<' || c == '(') depth++;
            if (c == '>' || c == ')') depth--;
            if (c == ',' && depth == 0)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
            parts.Add(sb.ToString());

        return parts;
    }

    /// <summary>
    /// Find the space that separates the type from the parameter name.
    /// e.g. "Dafny.ISequence<Dafny.Rune> delimiter" → position after the last '>'
    /// e.g. "BigInteger minLength" → position after "BigInteger"
    /// e.g. "__T @default" → position after "__T"
    /// </summary>
    private static int FindLastSpaceBeforeName(string param)
    {
        // Find the last '>' or non-identifier char that's followed by a space
        // Simple approach: find the last space that's at depth 0 (outside generics)
        var depth = 0;
        var lastSpace = -1;
        for (int i = param.Length - 1; i >= 0; i--)
        {
            var c = param[i];
            if (c == '>') depth++;
            if (c == '<') depth--;
            if (c == ' ' && depth == 0)
            {
                lastSpace = i;
                break;
            }
        }
        return lastSpace;
    }

    /// <summary>
    /// Build a map of module name → list of methods, from a set of translated files.
    /// </summary>
    public Dictionary<string, List<CsMethod>> ScanAll(
        List<(string ModuleName, string CSharpPath)> translatedFiles)
    {
        var map = new Dictionary<string, List<CsMethod>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (moduleName, csharpPath) in translatedFiles)
        {
            var methods = Scan(csharpPath);
            map[moduleName] = methods;
            Console.Error.WriteLine($"[Posit] Scanner — {moduleName}: {methods.Count} methods found");
        }
        return map;
    }
}