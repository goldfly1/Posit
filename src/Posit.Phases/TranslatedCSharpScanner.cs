namespace Posit.Phases;

/// <summary>
/// A scanned C# method signature — the ACTUAL types from the C# interface.
/// </summary>
public record CsMethodSignature(
    string Name,
    string ReturnType,
    string[] ParamTypes,
    string[] ParamNames,
    string[] OutParamTypes,
    string[] GenericParams,
    string ClassName,
    string Namespace);

/// <summary>
/// Reads translated C# files and extracts real method signatures by parsing
/// line by line. Also scans io-shell stub templates. Used by WiringGenerator
/// to track ACTUAL C# types for type-safe wiring.
/// </summary>
public static class TranslatedCSharpScanner
{
    /// <summary>
    /// Scan a C# file on disk. Returns method signatures found.
    /// </summary>
    public static List<CsMethodSignature> ScanFile(string path)
    {
        if (!File.Exists(path)) return [];
        var content = File.ReadAllText(path);
        return ScanContent(content);
    }

    /// <summary>
    /// Parse C# content line by line. Track namespace, track class,
    /// extract `public static` methods.
    /// </summary>
    public static List<CsMethodSignature> ScanContent(string content)
    {
        var results = new List<CsMethodSignature>();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var ns = string.Empty;
        var cls = string.Empty;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Track namespace
            if (line.StartsWith("namespace "))
            {
                var nsPart = line["namespace ".Length..].Trim().Trim('{', ' ');
                ns = nsPart;
                continue;
            }

            // Track class declaration
            if (line.Contains("class ") && (line.Contains("public") || line.Contains("internal") || line.Contains("partial")))
            {
                var classMatch = ExtractClassName(line);
                if (!string.IsNullOrEmpty(classMatch))
                    cls = classMatch;
                continue;
            }

            // Extract public static methods
            if (!line.Contains("public static")) continue;
            if (!line.Contains("(") && !line.Contains(")")) continue;

            var sig = ExtractMethod(line, cls, ns, lines, i);
            if (sig != null) results.Add(sig);
        }

        return results;
    }

    private static string? ExtractClassName(string line)
    {
        var idx = line.IndexOf("class ");
        if (idx < 0) return null;
        var rest = line[(idx + 6)..].Trim();
        var name = new StringBuilder();
        foreach (var c in rest)
        {
            if (char.IsLetterOrDigit(c) || c == '_') name.Append(c);
            else break;
        }
        return name.Length > 0 ? name.ToString() : null;
    }

    private static CsMethodSignature? ExtractMethod(string line, string cls, string ns, string[] lines, int lineIdx)
    {
        // Find method name and return type: "public static RETURNTYPE Name(..."
        var stripped = line.Replace("public static", "").Trim();
        var parenIdx = stripped.IndexOf('(');
        if (parenIdx < 0) return null;

        var beforeParen = stripped[..parenIdx].Trim();
        var lastSpace = beforeParen.LastIndexOf(' ');
        if (lastSpace < 0) return null;

        var returnType = beforeParen[..lastSpace].Trim();
        var methodName = beforeParen[(lastSpace + 1)..].Trim();

        // Handle generics on method name: Name<T> -> Name, [T]
        var genericParams = Array.Empty<string>();
        var angleIdx = methodName.IndexOf('<');
        if (angleIdx >= 0 && methodName.Contains('>'))
        {
            var generics = methodName[(angleIdx + 1)..];
            var closeIdx = generics.IndexOf('>');
            if (closeIdx >= 0)
            {
                genericParams = generics[..closeIdx].Split(',', StringSplitOptions.TrimEntries);
                methodName = methodName[..angleIdx];
            }
        }

        // Extract parameter types and names from parenthesized section
        var paramTypes = new List<string>();
        var paramNames = new List<string>();
        var outParamTypes = new List<string>();
        var paramSection = stripped[parenIdx..];
        var closeParen = FindMatchingParen(paramSection);
        if (closeParen > 1)
        {
            var paramText = paramSection[1..closeParen];
            if (!string.IsNullOrWhiteSpace(paramText))
                ParseParams(paramText, paramTypes, paramNames, outParamTypes);
        }

        return new CsMethodSignature(methodName, returnType,
            paramTypes.ToArray(), paramNames.ToArray(), outParamTypes.ToArray(),
            genericParams, cls, ns);
    }

    private static int FindMatchingParen(string s)
    {
        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static void ParseParams(string text, List<string> types, List<string> names, List<string> outTypes)
    {
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            // Detect "out Type name" — out params are the real return for void methods
            if (part.StartsWith("out "))
            {
                var outPart = part[4..].Trim();
                var lastSpace = outPart.LastIndexOf(' ');
                if (lastSpace >= 0)
                    outTypes.Add(outPart[..lastSpace].Trim());
                continue;
            }
            var lastSpace2 = part.LastIndexOf(' ');
            if (lastSpace2 < 0) continue;
            types.Add(part[..lastSpace2].Trim());
            names.Add(part[(lastSpace2 + 1)..].Trim());
        }
    }
}