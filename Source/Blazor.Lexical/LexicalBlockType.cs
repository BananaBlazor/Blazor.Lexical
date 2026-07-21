namespace Blazor.Lexical;

/// <summary>
/// A block-level type for the current selection, applied via
/// <see cref="LexicalEditor.SetBlockTypeAsync"/> and reported by
/// <see cref="LexicalSelectionState.BlockType"/>.
/// </summary>
public enum LexicalBlockType
{
    /// <summary>Plain paragraph.</summary>
    Paragraph,

    /// <summary>Heading level 1 (<c>&lt;h1&gt;</c>).</summary>
    Heading1,

    /// <summary>Heading level 2 (<c>&lt;h2&gt;</c>).</summary>
    Heading2,

    /// <summary>Heading level 3 (<c>&lt;h3&gt;</c>).</summary>
    Heading3,

    /// <summary>Heading level 4 (<c>&lt;h4&gt;</c>).</summary>
    Heading4,

    /// <summary>Heading level 5 (<c>&lt;h5&gt;</c>).</summary>
    Heading5,

    /// <summary>Heading level 6 (<c>&lt;h6&gt;</c>).</summary>
    Heading6,

    /// <summary>Block quote.</summary>
    Quote,

    /// <summary>Bulleted (unordered) list.</summary>
    BulletList,

    /// <summary>Numbered (ordered) list.</summary>
    NumberList,
}

/// <summary>
/// Maps <see cref="LexicalBlockType"/> to and from the JS command tokens the editor
/// speaks, so custom chrome never has to hardcode them. The token is the argument of
/// the <c>block:</c> command a button declares:
/// <code>
/// &lt;button data-lexical-command="@($"block:{LexicalBlockType.Heading2.ToJsToken()}")"&gt;H2&lt;/button&gt;
/// </code>
/// </summary>
public static class LexicalBlockTypeExtensions
{
    /// <summary>
    /// True for list block types, which route through the list commands rather
    /// than the JS <c>setBlockType</c> function.
    /// </summary>
    internal static bool IsList(this LexicalBlockType type) =>
        type is LexicalBlockType.BulletList or LexicalBlockType.NumberList;

    /// <summary>
    /// Maps a non-list block type to the JS <c>setBlockType</c> token (the part after
    /// <c>block:</c> in a <c>data-lexical-command</c> attribute). Lists are not block
    /// tokens — they ride the <c>list:</c> command instead.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is a list type (or undefined), which has no block token.
    /// </exception>
    public static string ToJsToken(this LexicalBlockType type) => type switch
    {
        LexicalBlockType.Paragraph => "paragraph",
        LexicalBlockType.Heading1 => "h1",
        LexicalBlockType.Heading2 => "h2",
        LexicalBlockType.Heading3 => "h3",
        LexicalBlockType.Heading4 => "h4",
        LexicalBlockType.Heading5 => "h5",
        LexicalBlockType.Heading6 => "h6",
        LexicalBlockType.Quote => "quote",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a block type token."),
    };

    /// <summary>
    /// The <c>&lt;option&gt;</c> value for the block-type <c>&lt;select&gt;</c>. Unlike
    /// <see cref="ToJsToken"/>, this also covers lists ('bullet'/'number'), so it
    /// matches the token JS reports in the selection state and the selected option
    /// stays in sync with the caret. The change handler routes list values through
    /// the list commands.
    /// </summary>
    internal static string ToSelectValue(this LexicalBlockType type) => type switch
    {
        LexicalBlockType.BulletList => "bullet",
        LexicalBlockType.NumberList => "number",
        _ => type.ToJsToken(),
    };

    /// <summary>
    /// Parses the JS <c>blockType</c> token reported in the selection state.
    /// Unknown tokens fall back to <see cref="LexicalBlockType.Paragraph"/>.
    /// </summary>
    public static LexicalBlockType FromJsToken(string? token) => token switch
    {
        "h1" => LexicalBlockType.Heading1,
        "h2" => LexicalBlockType.Heading2,
        "h3" => LexicalBlockType.Heading3,
        "h4" => LexicalBlockType.Heading4,
        "h5" => LexicalBlockType.Heading5,
        "h6" => LexicalBlockType.Heading6,
        "quote" => LexicalBlockType.Quote,
        "bullet" => LexicalBlockType.BulletList,
        "number" => LexicalBlockType.NumberList,
        _ => LexicalBlockType.Paragraph,
    };
}
