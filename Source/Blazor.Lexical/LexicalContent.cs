namespace Blazor.Lexical;

/// <summary>
/// The format a <see cref="LexicalContent"/> value is expressed in — one of the four
/// projections the editor can parse and serialize.
/// </summary>
public enum LexicalContentFormat
{
    /// <summary>Plain text: loaded as a single paragraph.</summary>
    Text,

    /// <summary>An HTML fragment.</summary>
    Html,

    /// <summary>Markdown.</summary>
    Markdown,

    /// <summary>
    /// Lexical's canonical editor-state JSON — the highest-fidelity format, and the
    /// one to persist (see <see cref="LexicalEditor.GetEditorStateJsonAsync"/>).
    /// </summary>
    EditorStateJson,
}

/// <summary>
/// A document plus the format it is written in. The editor's content has one source,
/// so the format travels *with* the string rather than being implied by which of
/// several parameters was set — which also means content loaded from a database, a
/// file, or an API can pick its format at runtime:
/// <code>
/// InitialContent="@(doc.IsMarkdown ? LexicalContent.FromMarkdown(doc.Body)
///                                  : LexicalContent.FromHtml(doc.Body))"
/// </code>
/// Build one with the <c>From*</c> factories rather than the constructor.
/// </summary>
/// <param name="Format">The format <paramref name="Text"/> is written in.</param>
/// <param name="Text">The document itself.</param>
public readonly record struct LexicalContent(LexicalContentFormat Format, string Text)
{
    /// <summary>Plain text, loaded as a single paragraph.</summary>
    public static LexicalContent FromText(string text) =>
        new(LexicalContentFormat.Text, text ?? throw new ArgumentNullException(nameof(text)));

    /// <summary>An HTML fragment, parsed into nodes.</summary>
    public static LexicalContent FromHtml(string html) =>
        new(LexicalContentFormat.Html, html ?? throw new ArgumentNullException(nameof(html)));

    /// <summary>
    /// Markdown, parsed into nodes. Pulls in the lazily-loaded Markdown chunk, so
    /// prefer <see cref="FromHtml"/> when either format would do.
    /// </summary>
    public static LexicalContent FromMarkdown(string markdown) =>
        new(LexicalContentFormat.Markdown,
            markdown ?? throw new ArgumentNullException(nameof(markdown)));

    /// <summary>
    /// A canonical editor-state JSON string, as produced by
    /// <see cref="LexicalEditor.GetEditorStateJsonAsync"/> — the round-trip-safe
    /// option for persisted documents.
    /// </summary>
    public static LexicalContent FromEditorStateJson(string json) =>
        new(LexicalContentFormat.EditorStateJson,
            json ?? throw new ArgumentNullException(nameof(json)));
}

internal static class LexicalContentFormatExtensions
{
    /// <summary>Maps to the token the JS glue branches on when loading content.</summary>
    public static string ToJsToken(this LexicalContentFormat format) => format switch
    {
        LexicalContentFormat.Text => "text",
        LexicalContentFormat.Html => "html",
        LexicalContentFormat.Markdown => "markdown",
        LexicalContentFormat.EditorStateJson => "stateJson",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
