namespace Blazor.Lexical;

/// <summary>
/// Block alignment applied via <see cref="LexicalEditor.SetAlignmentAsync"/> and
/// reported by <see cref="LexicalSelectionState.Alignment"/>.
/// </summary>
public enum LexicalAlignment
{
    /// <summary>No explicit alignment (inherits the default direction).</summary>
    None,

    /// <summary>Left-aligned.</summary>
    Left,

    /// <summary>Center-aligned.</summary>
    Center,

    /// <summary>Right-aligned.</summary>
    Right,

    /// <summary>Justified.</summary>
    Justify,
}

/// <summary>
/// Maps <see cref="LexicalAlignment"/> to and from the JS command tokens the editor
/// speaks, so custom chrome never has to hardcode them. The token is the argument of
/// the <c>align:</c> command a button declares:
/// <code>
/// &lt;button data-lexical-command="@($"align:{LexicalAlignment.Center.ToJsToken()}")"&gt;≡&lt;/button&gt;
/// </code>
/// </summary>
public static class LexicalAlignmentExtensions
{
    /// <summary>
    /// Maps to the JS <c>FORMAT_ELEMENT_COMMAND</c> token. <see cref="LexicalAlignment.None"/>
    /// maps to the empty string, which clears any explicit alignment.
    /// </summary>
    public static string ToJsToken(this LexicalAlignment alignment) => alignment switch
    {
        LexicalAlignment.None => "",
        LexicalAlignment.Left => "left",
        LexicalAlignment.Center => "center",
        LexicalAlignment.Right => "right",
        LexicalAlignment.Justify => "justify",
        _ => "",
    };

    /// <summary>
    /// Parses the JS alignment token reported in the selection state. An empty or
    /// unknown token maps to <see cref="LexicalAlignment.None"/>.
    /// </summary>
    public static LexicalAlignment FromJsToken(string? token) => token switch
    {
        "left" => LexicalAlignment.Left,
        "center" => LexicalAlignment.Center,
        "right" => LexicalAlignment.Right,
        "justify" => LexicalAlignment.Justify,
        _ => LexicalAlignment.None,
    };
}
