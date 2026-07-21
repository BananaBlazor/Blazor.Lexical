namespace Blazor.Lexical;

/// <summary>
/// Strongly-typed Lexical <c>EditorThemeClasses</c>: each property is the CSS
/// class name(s) applied to the corresponding node type. Assign to
/// <see cref="LexicalEditor.Theme"/> (or <see cref="LexicalOptions.DefaultTheme"/>).
/// Only the keys you set are emitted; unset keys are omitted.
/// </summary>
/// <remarks>
/// For advanced or unknown Lexical theme keys not modelled here, the
/// <see cref="LexicalEditor.Theme"/> parameter also accepts a raw anonymous object
/// as an escape hatch. Use <see cref="Default"/> for the class names styled by the
/// bundled <c>blazor-lexical.css</c>.
/// </remarks>
public sealed class LexicalTheme
{
    /// <summary>Class for paragraph blocks.</summary>
    public string? Paragraph { get; set; }

    /// <summary>Class for quote blocks.</summary>
    public string? Quote { get; set; }

    /// <summary>Classes for heading levels.</summary>
    public LexicalHeadingTheme Heading { get; set; } = new();

    /// <summary>Classes for lists and list items.</summary>
    public LexicalListTheme List { get; set; } = new();

    /// <summary>Class for links.</summary>
    public string? Link { get; set; }

    /// <summary>Class for the <c>&lt;table&gt;</c> element.</summary>
    public string? Table { get; set; }

    /// <summary>Class for table rows (<c>&lt;tr&gt;</c>).</summary>
    public string? TableRow { get; set; }

    /// <summary>Class for table cells (<c>&lt;td&gt;</c>/<c>&lt;th&gt;</c>).</summary>
    public string? TableCell { get; set; }

    /// <summary>Class for header table cells (added alongside <see cref="TableCell"/>).</summary>
    public string? TableCellHeader { get; set; }

    /// <summary>Class applied to the table while it holds a multi-cell selection.</summary>
    public string? TableSelected { get; set; }

    /// <summary>Class applied to each cell within a multi-cell selection.</summary>
    public string? TableCellSelected { get; set; }

    /// <summary>Classes for inline text formats.</summary>
    public LexicalTextTheme Text { get; set; } = new();

    /// <summary>
    /// Class for inserted mention reference tokens (the atomic node created when a
    /// suggestion is confirmed). The per-config colour is applied inline, so this class
    /// carries only the shared shape (padding, radius, weight).
    /// </summary>
    public string? Mention { get; set; }

    /// <summary>
    /// Class for freeform mention highlights (the hashtag-style node wrapped live as the
    /// user types). As with <see cref="Mention"/>, the per-config colour rides inline.
    /// </summary>
    public string? MentionHighlight { get; set; }

    /// <summary>
    /// The theme whose class names are styled by the bundled <c>blazor-lexical.css</c>,
    /// giving formatting a sensible default appearance out of the box.
    /// </summary>
    public static LexicalTheme Default => new()
    {
        Paragraph = "blazor-lexical__paragraph",
        Quote = "blazor-lexical__quote",
        Link = "blazor-lexical__link",
        Mention = "blazor-lexical__mention",
        MentionHighlight = "blazor-lexical__mention-highlight",
        Table = "blazor-lexical__table",
        TableCell = "blazor-lexical__table-cell",
        TableCellHeader = "blazor-lexical__table-cell-header",
        TableSelected = "blazor-lexical__table-selected",
        TableCellSelected = "blazor-lexical__table-cell-selected",
        Heading = new LexicalHeadingTheme
        {
            H1 = "blazor-lexical__h1",
            H2 = "blazor-lexical__h2",
            H3 = "blazor-lexical__h3",
            H4 = "blazor-lexical__h4",
            H5 = "blazor-lexical__h5",
            H6 = "blazor-lexical__h6",
        },
        List = new LexicalListTheme
        {
            Ul = "blazor-lexical__ul",
            Ol = "blazor-lexical__ol",
            Listitem = "blazor-lexical__li",
            Nested = new LexicalNestedListTheme { Listitem = "blazor-lexical__nested-li" },
        },
        Text = new LexicalTextTheme
        {
            Bold = "blazor-lexical__text-bold",
            Italic = "blazor-lexical__text-italic",
            Underline = "blazor-lexical__text-underline",
            Strikethrough = "blazor-lexical__text-strikethrough",
            UnderlineStrikethrough = "blazor-lexical__text-underline-strikethrough",
            Code = "blazor-lexical__text-code",
            Subscript = "blazor-lexical__text-subscript",
            Superscript = "blazor-lexical__text-superscript",
            Lowercase = "blazor-lexical__text-lowercase",
            Uppercase = "blazor-lexical__text-uppercase",
        },
    };
}

/// <summary>Heading-level theme classes (maps to Lexical's <c>heading</c> theme key).</summary>
public sealed class LexicalHeadingTheme
{
    /// <summary>Class for <c>&lt;h1&gt;</c>.</summary>
    public string? H1 { get; set; }

    /// <summary>Class for <c>&lt;h2&gt;</c>.</summary>
    public string? H2 { get; set; }

    /// <summary>Class for <c>&lt;h3&gt;</c>.</summary>
    public string? H3 { get; set; }

    /// <summary>Class for <c>&lt;h4&gt;</c>.</summary>
    public string? H4 { get; set; }

    /// <summary>Class for <c>&lt;h5&gt;</c>.</summary>
    public string? H5 { get; set; }

    /// <summary>Class for <c>&lt;h6&gt;</c>.</summary>
    public string? H6 { get; set; }
}

/// <summary>List theme classes (maps to Lexical's <c>list</c> theme key).</summary>
public sealed class LexicalListTheme
{
    /// <summary>Class for unordered (<c>&lt;ul&gt;</c>) lists.</summary>
    public string? Ul { get; set; }

    /// <summary>Class for ordered (<c>&lt;ol&gt;</c>) lists.</summary>
    public string? Ol { get; set; }

    /// <summary>Class for list items (<c>&lt;li&gt;</c>).</summary>
    public string? Listitem { get; set; }

    /// <summary>Classes for nested lists.</summary>
    public LexicalNestedListTheme Nested { get; set; } = new();
}

/// <summary>Nested-list theme classes (maps to Lexical's <c>list.nested</c> key).</summary>
public sealed class LexicalNestedListTheme
{
    /// <summary>Class for nested list items.</summary>
    public string? Listitem { get; set; }
}

/// <summary>Inline text-format theme classes (maps to Lexical's <c>text</c> theme key).</summary>
public sealed class LexicalTextTheme
{
    /// <summary>Class for bold text.</summary>
    public string? Bold { get; set; }

    /// <summary>Class for italic text.</summary>
    public string? Italic { get; set; }

    /// <summary>Class for underlined text.</summary>
    public string? Underline { get; set; }

    /// <summary>Class for struck-through text.</summary>
    public string? Strikethrough { get; set; }

    /// <summary>Class for combined underline + strikethrough text.</summary>
    public string? UnderlineStrikethrough { get; set; }

    /// <summary>Class for inline code text.</summary>
    public string? Code { get; set; }

    /// <summary>Class for subscript text.</summary>
    public string? Subscript { get; set; }

    /// <summary>Class for superscript text.</summary>
    public string? Superscript { get; set; }

    /// <summary>Class for lowercased text.</summary>
    public string? Lowercase { get; set; }

    /// <summary>Class for uppercased text.</summary>
    public string? Uppercase { get; set; }
}
