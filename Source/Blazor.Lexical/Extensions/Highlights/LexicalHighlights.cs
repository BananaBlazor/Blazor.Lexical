using System.Text.Json;

namespace Blazor.Lexical;

/// <summary>
/// A span of text described by its <i>content</i> rather than by a position — the
/// W3C <c>TextQuoteSelector</c> shape, and the argument
/// <see cref="LexicalHighlights.HighlightTextAsync(LexicalTextQuote, string, bool)"/> takes.
/// </summary>
/// <param name="Exact">The text to find.</param>
/// <param name="Prefix">
/// The text expected immediately <i>before</i> <paramref name="Exact"/>, if known. Used
/// only to choose between several occurrences — never to reject one.
/// </param>
/// <param name="Suffix">
/// The text expected immediately <i>after</i> <paramref name="Exact"/>, if known. Same
/// disambiguate-only role as <paramref name="Prefix"/>.
/// </param>
/// <remarks>
/// Whitespace is normalized before matching on both sides — every run of spaces,
/// newlines and block boundaries counts as one space — so a quote written as prose
/// matches a document whose words are split across paragraphs, bold runs or marks.
/// </remarks>
public sealed record LexicalTextQuote(string Exact, string? Prefix = null, string? Suffix = null);

/// <summary>How a <see cref="LexicalTextQuote"/> resolved against the document.</summary>
public enum LexicalTextAnchorResult
{
    /// <summary>The text does not appear in the document; nothing was highlighted.</summary>
    NotFound = 0,

    /// <summary>Exactly one occurrence was the best match for the supplied context.</summary>
    Matched = 1,

    /// <summary>
    /// The text was found and highlighted, but several occurrences matched the context
    /// equally well — the first was used. Treat it as a weak anchor: worth re-capturing
    /// with more <see cref="LexicalTextQuote.Prefix"/>/<see cref="LexicalTextQuote.Suffix"/>,
    /// or worth telling the user the comment may have drifted.
    /// </summary>
    MatchedAmbiguously = 2,
}

/// <summary>
/// Paints transient highlights over text found by its content: nest
/// <c>&lt;LexicalHighlights /&gt;</c> inside the editor and you can light up a quote an
/// AI reviewer, a spell-checker or a find-bar produced, without knowing where in the
/// document it is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Highlights are not in the document.</b> They are painted with the browser's CSS
/// Custom Highlight API, so nothing is inserted, nothing serializes, no undo step is
/// added and the document is never marked dirty. That is the difference from
/// <see cref="LexicalMarks"/>: a mark is a node the app already knows the position of and
/// wants to keep; a highlight is a decoration the app describes by its text and throws
/// away. Reach for marks when it must survive a save, highlights when it must not.
/// </para>
/// <para>
/// Unlike a selection, a highlight survives the user clicking elsewhere and can span
/// several blocks — which is what makes it usable for review UI the user works alongside.
/// It also follows its text: the anchor is re-resolved after every edit, so a highlight
/// stays on its sentence as the paragraph above it grows, and quietly disappears if the
/// quoted words are deleted.
/// </para>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalHighlights @@ref="_highlights" /&gt;
/// &lt;/LexicalEditor&gt;
/// @@code {
///   var found = await _highlights.HighlightTextAsync(
///       new LexicalTextQuote(comment.Quote, comment.Prefix, comment.Suffix), "ai");
///   if (found == LexicalTextAnchorResult.NotFound) { comment.Orphaned = true; }
/// }
/// </code>
/// <para>
/// <b>Styling is yours, and it is per id.</b> Each <c>highlightId</c> paints under the
/// CSS highlight name <c>blazor-lexical-&lt;id&gt;</c>, so several sets coexist and are
/// styled independently — that is how AI suggestions go yellow and a human reviewer's go
/// blue, or spelling pink and structure green:
/// </para>
/// <code>
/// ::highlight(blazor-lexical-ai)      { background-color: #fef08a; }
/// ::highlight(blazor-lexical-reviewer){ background-color: #bfdbfe; }
/// </code>
/// <para>
/// The bundled stylesheet styles only the default id. Ids must be valid CSS identifiers,
/// since they end up inside <c>::highlight()</c>. There is no
/// <see cref="LexicalTheme"/> key here on purpose: a highlight is not a node, so there is
/// no element to hang a class on.
/// </para>
/// <para>
/// This extension performs <b>zero</b> JS→.NET calls — every method below is a call this
/// side initiates.
/// </para>
/// </remarks>
public sealed class LexicalHighlights : LexicalExtension
{
    /// <summary>
    /// The highlight set used when a call does not name one. Paints under
    /// <c>::highlight(blazor-lexical-default)</c>, which the bundled stylesheet styles.
    /// </summary>
    public const string DefaultHighlightId = "default";

    internal override string? BuiltIn => "highlights";

    /// <summary>
    /// Highlights the text <paramref name="quote"/> describes, replacing whatever
    /// <paramref name="highlightId"/> was painting, and reports how well it anchored.
    /// </summary>
    /// <param name="quote">The text to find, optionally with the words either side of it.</param>
    /// <param name="highlightId">
    /// The highlight set to paint into — its <c>::highlight()</c> name and the handle
    /// <see cref="ClearHighlightsAsync"/> takes. Use one per category (comments,
    /// spelling, search) so they can be styled and cleared apart.
    /// </param>
    /// <param name="scroll">Whether to scroll the match into view. Defaults to <c>true</c>.</param>
    /// <returns>
    /// <see cref="LexicalTextAnchorResult.NotFound"/> when the text is not in the document
    /// (nothing is painted and the id is left clear), otherwise whether the match was
    /// unambiguous.
    /// </returns>
    public async Task<LexicalTextAnchorResult> HighlightTextAsync(
        LexicalTextQuote quote,
        string highlightId = DefaultHighlightId,
        bool scroll = true)
    {
        ArgumentNullException.ThrowIfNull(quote);
        var request = new HighlightRequestDto
        {
            Id = highlightId,
            Exact = quote.Exact,
            Prefix = quote.Prefix,
            Suffix = quote.Suffix,
            Scroll = scroll,
        };
        var result = (LexicalTextAnchorResult)ParseInt(await InvokeJsAsync("highlight", Args(request)));
        return Enum.IsDefined(result) ? result : LexicalTextAnchorResult.NotFound;
    }

    /// <summary>
    /// Highlights <paramref name="text"/> — the no-context overload, for when the caller
    /// has nothing but the words and is happy with the first occurrence.
    /// </summary>
    /// <param name="text">The text to find.</param>
    /// <param name="highlightId">The highlight set to paint into.</param>
    /// <param name="scroll">Whether to scroll the match into view. Defaults to <c>true</c>.</param>
    /// <returns>
    /// The same verdict as the <see cref="LexicalTextQuote"/> overload —
    /// <see cref="LexicalTextAnchorResult.MatchedAmbiguously"/> here simply means the text
    /// occurs more than once, which with no context to go on is expected rather than
    /// alarming.
    /// </returns>
    public Task<LexicalTextAnchorResult> HighlightTextAsync(
        string text,
        string highlightId = DefaultHighlightId,
        bool scroll = true) =>
        HighlightTextAsync(new LexicalTextQuote(text), highlightId, scroll);

    /// <summary>
    /// Highlights <b>every</b> occurrence of <paramref name="text"/> under
    /// <paramref name="highlightId"/>, returning how many were painted — the find-all /
    /// replace-all shape, where the point is the whole set rather than one anchor.
    /// </summary>
    /// <param name="text">The text to find. Context would be meaningless here, so there is none.</param>
    /// <param name="highlightId">The highlight set to paint into.</param>
    public async Task<int> HighlightAllAsync(
        string text,
        string highlightId = DefaultHighlightId)
    {
        var request = new HighlightRequestDto { Id = highlightId, Exact = text };
        return ParseInt(await InvokeJsAsync("highlightAll", Args(request)));
    }

    /// <summary>
    /// Removes the highlights painted under <paramref name="highlightId"/>, or — with
    /// <c>null</c> — every highlight this editor is painting.
    /// </summary>
    /// <param name="highlightId">The set to clear, or <c>null</c> for all of them.</param>
    public Task ClearHighlightsAsync(string? highlightId = DefaultHighlightId) =>
        InvokeJsAsync("clear", Args(highlightId ?? string.Empty));

    /// <summary>
    /// Scrolls the first highlight painted under <paramref name="highlightId"/> into view,
    /// returning whether there was one. Re-resolves the anchor first, so it is also the
    /// cheap way to ask "is this still anchored?" after the user has been editing.
    /// </summary>
    /// <param name="highlightId">The set to scroll to.</param>
    public async Task<bool> ScrollToHighlightAsync(string highlightId = DefaultHighlightId) =>
        await InvokeJsAsync("scrollTo", Args(highlightId)) == "true";

    /// <summary>The extension channel's JSON argument array for a highlight request.</summary>
    private static string Args(HighlightRequestDto request) => JsonSerializer.Serialize(
        new[] { request }, LexicalJsonSerializerContext.Default.HighlightRequestDtoArray);

    /// <summary>The extension channel's JSON argument array for a single highlight id.</summary>
    private static string Args(string highlightId) => JsonSerializer.Serialize(
        new[] { highlightId }, LexicalJsonSerializerContext.Default.StringArray);

    /// <summary>Parses the JS half's numeric result, tolerating null (editor not created).</summary>
    private static int ParseInt(string? json) => int.TryParse(json, out var value) ? value : 0;
}
