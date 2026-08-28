using Posit.Contracts.Artifacts;

namespace Posit.Tui;

/// <summary>
/// Key-mapping convention for the TUI terminal.
///
/// Standardized so the QA bot always knows where every field is without
/// discovery. The carapace standardizes the code interface (method signatures).
/// The key-mapping standardizes the user interface (field positions, navigation).
/// Both serve the same purpose: the consumer knows where things are.
///
/// Convention:
/// - Ctrl+F1 = first page, focus on first field
/// - Tab = next field (standard accessibility)
/// - Every field has a known position in tab order
/// - Pages are standardized — same layout conventions across all programs
/// </summary>
public static class KeyMap
{
    /// <summary>Go to the first page and focus the first field.</summary>
    public const string FirstPageFocus = "Ctrl+F1";

    /// <summary>Move to the next field in tab order.</summary>
    public const string NextField = "Tab";

    /// <summary>Submit the current form / execute the current action.</summary>
    public const string Submit = "Enter";

    /// <summary>Cancel / go back.</summary>
    public const string Cancel = "Escape";

    /// <summary>
    /// The standard keystroke sequence to fill a form:
    /// Ctrl+F1 (focus first field) → type value → Tab → type value → ... → Enter
    /// </summary>
    public static string[] BuildFillFormSequence(params string[] fieldValues)
    {
        var seq = new List<string> { FirstPageFocus };
        for (var i = 0; i < fieldValues.Length; i++)
        {
            seq.Add(fieldValues[i]);
            if (i < fieldValues.Length - 1)
                seq.Add(NextField);
        }
        seq.Add(Submit);
        return seq.ToArray();
    }
}

/// <summary>
/// A page in the TUI. Pages are standardized: same layout conventions
/// across all programs. Each page has fields in a known tab order.
/// </summary>
public sealed class TuiPage
{
    public string Name { get; init; } = "";
    public TuiField[] Fields { get; init; } = [];
    /// <summary>
    /// The carapace method this page renders. When the user submits,
    /// this method is called with the field values as arguments.
    /// </summary>
    public string? CarapaceMethod { get; init; }
}

/// <summary>
/// A field on a TUI page. Has a known position in tab order.
/// </summary>
public sealed class TuiField
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public string Type { get; init; } = "";
    /// <summary>Position in tab order (0-based).</summary>
    public int TabOrder { get; init; }
}

/// <summary>
/// A TUI terminal. Renders carapace methods to terminal panes.
/// The QA bot drives it with deterministic keystrokes.
///
/// This is the abstract definition. The concrete implementation will
/// use a terminal library (Spectre.Console or similar) to render
/// fields and capture input. For now, this defines the contract.
/// </summary>
public interface ITuiTerminal
{
    /// <summary>Render a page with its fields.</summary>
    void Render(TuiPage page);

    /// <summary>Send a keystroke to the terminal.</summary>
    void SendKey(string key);

    /// <summary>Type text into the currently focused field.</summary>
    void TypeText(string text);

    /// <summary>Read the current content of the terminal screen.</summary>
    string ReadScreen();

    /// <summary>Execute the carapace method for the current page with the field values.</summary>
    string Execute();
}

/// <summary>
/// Maps a carapace interface (C# method signatures) to TUI pages.
/// Each method becomes a page. Each parameter becomes a field.
/// The method name is the page name. Parameter names are field labels.
/// Parameter types are field types.
/// </summary>
public static class TuiMapper
{
    /// <summary>
    /// Build TUI pages from a component's method signatures.
    /// </summary>
    public static TuiPage[] BuildPages(MethodSignature[] methods)
    {
        return methods.Select(m => new TuiPage
        {
            Name = m.Name,
            CarapaceMethod = m.Name,
            Fields = m.Params.Select((p, i) => new TuiField
            {
                Name = p.Name,
                Label = p.Name,
                Type = p.Type,
                TabOrder = i
            }).ToArray()
        }).ToArray();
    }
}