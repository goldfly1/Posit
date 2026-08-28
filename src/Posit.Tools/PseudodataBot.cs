using System.Text.RegularExpressions;
using Posit.Contracts.Artifacts;

namespace Posit.Tools;

/// <summary>
/// Deterministic pseudodata bot. Reads carapace interfaces (C# method signatures)
/// and architect test case categories to generate typed test data. No LLM call.
///
/// For computable transforms, the bot parses // test: comments from the C# interface
/// to learn the transform rule, then applies it to compute expected output.
/// </summary>
public sealed class PseudodataBot
{
    /// <summary>
    /// Generate test data files for a pipeline. Reads the CLI component's entry type,
    /// the logic component's method signatures, and the test case categories.
    /// </summary>
    public List<TestDataFile> Generate(Component cliComponent, Component logicComponent, string systemContext)
    {
        var isStdin = (cliComponent.EntryType ?? "file").Equals("stdin", StringComparison.OrdinalIgnoreCase);
        var testCases = cliComponent.TestCases.Length > 0
            ? cliComponent.TestCases
            : logicComponent.TestCases;

        // Parse the transform rule from // test: comments in the C# interface
        var transformRule = ParseTransformRule(logicComponent.CSharpInterface ?? "");

        // Get the input shape from the logic component's method signatures
        var inputShape = GetInputShape(logicComponent);

        var result = new List<TestDataFile>();

        foreach (var tc in testCases)
        {
            var category = ClassifyTestCase(tc.Name);
            var (content, expectedOutput, expectedExitCode) = GenerateForCategory(
                category, inputShape, transformRule, isStdin, tc, systemContext);

            // Architect's answer key is PRIMARY — overrides bot heuristics.
            // The architect was prompted to provide concrete input/expectedOutput
            // per test case; the bot's category generators are the fallback for
            // test cases that lack them (legacy contracts).
            if (!string.IsNullOrWhiteSpace(tc.Input))
                content = tc.Input;
            if (!string.IsNullOrWhiteSpace(tc.ExpectedOutput))
            {
                expectedOutput = tc.ExpectedOutput;
                expectedExitCode = tc.ExpectedExitCode;
            }

            result.Add(new TestDataFile
            {
                FileName = isStdin ? $"stdin_{result.Count}.txt" : $"testdata_{result.Count}.txt",
                Content = content,
                Description = tc.ExpectedBehavior ?? tc.Description,
                ExpectedOutput = expectedOutput,
                ExpectedExitCode = expectedExitCode
            });
        }

        return result;
    }

    /// <summary>
    /// Classify a test case by name into a category.
    /// </summary>
    private static TestCaseCategory ClassifyTestCase(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("empty") || lower.Contains("no data") || lower.Contains("blank"))
            return TestCaseCategory.Empty;
        if (lower.Contains("invalid") || lower.Contains("malformed") || lower.Contains("bad")
            || lower.Contains("error") || lower.Contains("inconsistent") || lower.Contains("mismatch"))
            return TestCaseCategory.Invalid;
        if (lower.Contains("edge") || lower.Contains("boundary") || lower.Contains("single")
            || lower.Contains("one row") || lower.Contains("minimum"))
            return TestCaseCategory.Edge;
        return TestCaseCategory.Valid;
    }

    /// <summary>
    /// Get the input shape from the logic component's method signatures.
    /// Returns the first method's parameter types (the input the program transforms).
    /// </summary>
    private static InputShape GetInputShape(Component logicComponent)
    {
        var method = logicComponent.MethodSignatures.FirstOrDefault();
        if (method == null)
            return new InputShape(["string"], "string");

        var paramTypes = method.Params.Select(p => p.Type).ToArray();
        return new InputShape(paramTypes, method.ReturnType);
    }

    /// <summary>
    /// Parse // test: comments from the C# interface to learn the transform rule.
    /// Format: // test: "input" → "output"  (for string transforms)
    /// Format: // test: 0 C to F → "32 F"  (for temperature-style transforms)
    /// </summary>
    private static TransformRule? ParseTransformRule(string csharpInterface)
    {
        if (string.IsNullOrWhiteSpace(csharpInterface))
            return null;

        var tests = new List<TestExample>();
        var lines = csharpInterface.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("// test:"))
                continue;

            var body = trimmed["// test:".Length..].Trim();

            // Format: "input" → "output"  or  input → "output"
            var arrowIdx = body.IndexOf("→");
            if (arrowIdx < 0) arrowIdx = body.IndexOf("->");
            if (arrowIdx < 0) continue;

            var inputPart = body[..arrowIdx].Trim().Trim('"');
            var outputPart = body[(arrowIdx + (body[arrowIdx] == '→' ? 1 : 2))..].Trim().Trim('"');

            tests.Add(new TestExample(inputPart, outputPart));
        }

        if (tests.Count == 0)
            return null;

        // Detect transform type from examples
        // Temperature: "0 C to F → 32 F" — input has number + unit, output has number + unit
        // CSV→JSON: lines → JSON array
        // String transform: string → string
        return new TransformRule(tests);
    }

    /// <summary>
    /// Generate test data for a specific category.
    /// </summary>
    private static (string content, string expectedOutput, int expectedExitCode) GenerateForCategory(
        TestCaseCategory category,
        InputShape inputShape,
        TransformRule? transformRule,
        bool isStdin,
        ComponentTestCase tc,
        string systemContext)
    {
        // Determine the input format from the input shape + system context
        var isCsv = inputShape.ParamTypes.Length == 1
            && inputShape.ParamTypes[0] == "string[]"
            && systemContext.Contains("CSV", StringComparison.OrdinalIgnoreCase);

        var isTemperature = inputShape.ParamTypes.Length == 2
            && inputShape.ParamTypes[0] == "double"
            && inputShape.ParamTypes[1] == "string";

        return category switch
        {
            TestCaseCategory.Valid => GenerateValid(isCsv, isTemperature, isStdin, transformRule, tc),
            TestCaseCategory.Edge => GenerateEdge(isCsv, isTemperature, isStdin, transformRule, tc),
            TestCaseCategory.Invalid => GenerateInvalid(isCsv, isTemperature, isStdin, tc),
            TestCaseCategory.Empty => GenerateEmpty(isCsv, isStdin, tc),
            _ => GenerateValid(isCsv, isTemperature, isStdin, transformRule, tc)
        };
    }

    private static (string, string, int) GenerateValid(bool isCsv, bool isTemperature, bool isStdin,
        TransformRule? rule, ComponentTestCase tc)
    {
        if (isTemperature)
        {
            // Generate "0 C" for C-to-F, "100 C" for C-to-K, etc.
            var input = "0 C";
            string expected = "";
            if (rule?.Examples.Count > 0)
            {
                var first = rule.Examples[0];
                input = first.Input;
                expected = first.Output;
            }
            return (input, expected, 0);
        }

        if (isCsv)
        {
            var content = "name,age\nAlice,30\nBob,25";
            string expected = "";
            // If we have a transform rule, compute expected output
            if (rule?.Examples.Count > 0)
            {
                var first = rule.Examples[0];
                // Try to compute: parse the input lines, apply the transform
                expected = TryComputeCsvTransform(first.Input, first.Output, content);
            }
            return (content, expected, 0);
        }

        // Generic: use the test case description as-is
        var generic = ExtractQuotedContent(tc.Description) ?? "test input";
        return (generic, "", 0);
    }

    private static (string, string, int) GenerateEdge(bool isCsv, bool isTemperature, bool isStdin,
        TransformRule? rule, ComponentTestCase tc)
    {
        if (isTemperature)
        {
            // Edge: negative temperature
            var input = "-40 C";
            string expected = "";
            if (rule?.Examples.Count > 0)
            {
                // Compute: -40 C to F = -40 * 9/5 + 32 = -40 (same point!)
                expected = "-40 F";
            }
            return (input, expected, 0);
        }

        if (isCsv)
        {
            // Edge: single row (header + 1 data row)
            var content = "name,age\nAlice,30";
            return (content, "", 0);
        }

        return ("a", "", 0);
    }

    private static (string, string, int) GenerateInvalid(bool isCsv, bool isTemperature, bool isStdin,
        ComponentTestCase tc)
    {
        if (isTemperature)
        {
            // Invalid: bad unit
            return ("32 X", "", 1);
        }

        if (isCsv)
        {
            // Invalid: inconsistent field count
            var content = "name,age\nAlice,30,extra";
            return (content, "", 1);
        }

        return ("invalid_input", "", 1);
    }

    private static (string, string, int) GenerateEmpty(bool isCsv, bool isStdin, ComponentTestCase tc)
    {
        if (isCsv)
        {
            // Empty: no content at all
            return ("", "", 1);
        }

        if (isStdin)
        {
            return ("", "", 1);
        }

        return ("", "", 1);
    }

    /// <summary>
    /// Try to compute the expected CSV transform output.
    /// Given a known test example (input → output), apply the same transform to new input.
    /// This is a simple pattern matcher: if the example shows CSV→JSON, replicate the JSON format.
    /// </summary>
    private static string TryComputeCsvTransform(string exampleInput, string exampleOutput, string actualInput)
    {
        // Parse the example: if input is ["name,age","Alice,30","Bob,25"] and output is
        // [{"name":"Alice","age":"30"},{"name":"Bob","age":"25"}], we can replicate.
        // For now, return empty — the judge's exact match layer will handle this when
        // the transform rule is computable. This is a placeholder for the bot's compute engine.
        return "";
    }

    /// <summary>
    /// Extract content from single quotes in a description string.
    /// </summary>
    private static string? ExtractQuotedContent(string description)
    {
        var match = Regex.Match(description, @"'([^']+)'");
        return match.Success ? match.Groups[1].Value : null;
    }

    private enum TestCaseCategory { Valid, Edge, Invalid, Empty }

    private sealed record InputShape(string[] ParamTypes, string ReturnType);

    private sealed record TransformRule(List<TestExample> Examples);

    private sealed record TestExample(string Input, string Output);
}