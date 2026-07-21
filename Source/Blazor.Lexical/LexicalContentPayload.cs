namespace Blazor.Lexical;

/// <summary>
/// What the content-changed push carries across the interop boundary. Selected with
/// <see cref="LexicalEditor.ContentPayload"/>; only relevant when
/// <see cref="LexicalEditor.OnContentChanged"/> is subscribed (with no subscriber the
/// channel is off entirely and nothing crosses).
/// <para>
/// Declaring the format you actually want is what keeps the channel to <b>one</b>
/// crossing per change: the document is serialized in JS inside the debounce and
/// arrives ready to use, instead of a plain-text push the handler discards followed by
/// a <c>Get…Async</c> round trip to fetch the real thing.
/// </para>
/// </summary>
public enum LexicalContentPayload
{
    /// <summary>
    /// The document's plain text — the default, and the right choice for word counts,
    /// previews, and search indexing.
    /// </summary>
    Text,

    /// <summary>
    /// Nothing but the fact that something changed: the callback fires with an empty
    /// <see cref="LexicalContent"/> and <see cref="LexicalEditor.LastContent"/> is left
    /// alone. Use it for dirty-tracking and autosave triggers, where even the text
    /// would be paid for on every debounce tick for no benefit — it matters most on
    /// Blazor Server with large documents. Read the content on demand when you need it.
    /// </summary>
    SignalOnly,

    /// <summary>The document serialized to HTML.</summary>
    Html,

    /// <summary>
    /// The document serialized to Markdown. Loads the lazily-loaded Markdown chunk on
    /// the first push (once, then reused).
    /// </summary>
    Markdown,

    /// <summary>
    /// The canonical editor-state JSON — the highest-fidelity format, and the one to
    /// persist. The natural choice for a save-on-change host.
    /// </summary>
    EditorStateJson,
}

internal static class LexicalContentPayloadExtensions
{
    /// <summary>Maps to the token carried on the JS <c>notify</c> flags.</summary>
    public static string ToJsToken(this LexicalContentPayload payload) => payload switch
    {
        LexicalContentPayload.SignalOnly => "signalOnly",
        LexicalContentPayload.Text => "text",
        LexicalContentPayload.Html => "html",
        LexicalContentPayload.Markdown => "markdown",
        LexicalContentPayload.EditorStateJson => "stateJson",
        _ => throw new ArgumentOutOfRangeException(nameof(payload), payload, null),
    };

    /// <summary>
    /// The format a pushed payload is written in, or <c>null</c> for
    /// <see cref="LexicalContentPayload.SignalOnly"/> — which carries no document, so
    /// there is no format to name (and nothing worth caching).
    /// </summary>
    public static LexicalContentFormat? ToContentFormat(this LexicalContentPayload payload) =>
        payload switch
        {
            LexicalContentPayload.Text => LexicalContentFormat.Text,
            LexicalContentPayload.Html => LexicalContentFormat.Html,
            LexicalContentPayload.Markdown => LexicalContentFormat.Markdown,
            LexicalContentPayload.EditorStateJson => LexicalContentFormat.EditorStateJson,
            _ => null,
        };
}
