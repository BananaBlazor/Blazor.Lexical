namespace Blazor.Lexical;

/// <summary>
/// An inline text format that can be toggled over the current selection via
/// <see cref="LexicalEditor.FormatTextAsync"/>.
/// </summary>
public enum LexicalTextFormat
{
    /// <summary>Bold (<c>&lt;strong&gt;</c>).</summary>
    Bold,

    /// <summary>Italic (<c>&lt;em&gt;</c>).</summary>
    Italic,

    /// <summary>Underline.</summary>
    Underline,

    /// <summary>Strikethrough.</summary>
    Strikethrough,

    /// <summary>Inline code.</summary>
    Code,

    /// <summary>Subscript.</summary>
    Subscript,

    /// <summary>Superscript.</summary>
    Superscript,

    /// <summary>Lowercases the selected text (CSS <c>text-transform</c>).</summary>
    Lowercase,

    /// <summary>Uppercases the selected text (CSS <c>text-transform</c>).</summary>
    Uppercase,
}

/// <summary>
/// Maps <see cref="LexicalTextFormat"/> to and from the JS command tokens the editor
/// speaks, so custom chrome never has to hardcode them. The token is the argument of
/// the <c>format:</c> command a button declares:
/// <code>
/// &lt;button data-lexical-command="@($"format:{LexicalTextFormat.Bold.ToJsToken()}")"&gt;B&lt;/button&gt;
/// </code>
/// </summary>
public static class LexicalTextFormatExtensions
{
    /// <summary>
    /// Maps to the JS <c>FORMAT_TEXT_COMMAND</c> token (the part after
    /// <c>format:</c> in a <c>data-lexical-command</c> attribute).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined format.</exception>
    public static string ToJsToken(this LexicalTextFormat format) => format switch
    {
        LexicalTextFormat.Bold => "bold",
        LexicalTextFormat.Italic => "italic",
        LexicalTextFormat.Underline => "underline",
        LexicalTextFormat.Strikethrough => "strikethrough",
        LexicalTextFormat.Code => "code",
        LexicalTextFormat.Subscript => "subscript",
        LexicalTextFormat.Superscript => "superscript",
        LexicalTextFormat.Lowercase => "lowercase",
        LexicalTextFormat.Uppercase => "uppercase",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>
    /// Parses a JS text-format token back into a <see cref="LexicalTextFormat"/>.
    /// Returns <c>null</c> for an unknown or empty token, so a caller can tell a
    /// mis-typed token from a valid one (unlike the block/alignment parsers, there
    /// is no meaningful "no format" member to fall back to).
    /// </summary>
    public static LexicalTextFormat? FromJsToken(string? token) => token switch
    {
        "bold" => LexicalTextFormat.Bold,
        "italic" => LexicalTextFormat.Italic,
        "underline" => LexicalTextFormat.Underline,
        "strikethrough" => LexicalTextFormat.Strikethrough,
        "code" => LexicalTextFormat.Code,
        "subscript" => LexicalTextFormat.Subscript,
        "superscript" => LexicalTextFormat.Superscript,
        "lowercase" => LexicalTextFormat.Lowercase,
        "uppercase" => LexicalTextFormat.Uppercase,
        _ => null,
    };
}
