using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// Renders inline <b>suggested edits</b> — the "reviewer with a pen" diff. Nest
/// <c>&lt;LexicalSuggestions /&gt;</c> inside a <see cref="LexicalEditor"/> and, given a
/// span of existing text and a proposed replacement, it strikes the original through and
/// inserts the proposal after it, highlighted (Google Docs suggestion mode, but
/// display-only).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is display-only, and it mutates the in-memory document.</b> The viewer never
/// persists editor state — a review app loads content with
/// <c>InitialContent</c>/<c>SetEditorStateJsonAsync</c> and repaints from its own source of
/// truth every time — so turning a run of text into a struck-through deletion plus a
/// proposed insertion is safe: nothing round-trips back out. There are deliberately <b>no
/// accept/reject affordances</b> here; the consuming app renders its own chrome and applies
/// accept/reject in its own store, using <see cref="OnSuggestionClicked"/> to know which
/// suggestion the reviewer touched.
/// </para>
/// <para>
/// <b>Works read-only.</b> Suggestions are the read-only review experience's whole point, so
/// every method here works with <c>ReadOnly="true"</c>.
/// </para>
/// <para>
/// <b>Suggestion ids are yours.</b> Like mark ids, the library never generates or interprets
/// one — pass whatever key your app already has and it comes back unchanged from
/// <see cref="OnSuggestionClicked"/>.
/// </para>
/// <code>
/// &lt;LexicalEditor ReadOnly="true" InitialContent="@_doc"&gt;
///   &lt;LexicalSuggestions @@ref="_suggestions" OnSuggestionClicked="ShowSuggestion" /&gt;
/// &lt;/LexicalEditor&gt;
/// @@code {
///   await _suggestions.ShowSuggestionAsync(
///       edit.Id, new LexicalTextQuote(edit.Quote, edit.Prefix, edit.Suffix), edit.Proposed);
/// }
/// </code>
/// <para>
/// Styling is the <see cref="LexicalTheme.SuggestionDeletion"/> and
/// <see cref="LexicalTheme.SuggestionInsertion"/> theme classes, styled by the bundled
/// stylesheet by default. Interop is opt-in: wire <see cref="OnSuggestionClicked"/> to learn
/// about clicks; the show/remove/clear methods are calls this side initiates and always work.
/// </para>
/// </remarks>
public sealed class LexicalSuggestions : LexicalExtension
{
    internal override string? BuiltIn => "suggestions";

    /// <summary>
    /// Fires when the reviewer clicks either half of a suggestion (the struck-through
    /// original or the proposed insertion), carrying the app-owned suggestion id(s) at the
    /// click. The click is never swallowed. Wire it to open your own accept/reject chrome.
    /// </summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> OnSuggestionClicked { get; set; }

    /// <inheritdoc />
    protected override bool HasInvokeHandler => OnSuggestionClicked.HasDelegate;

    /// <summary>
    /// Renders a suggested edit: strikes through the span <paramref name="target"/> describes
    /// and inserts <paramref name="proposedText"/> after it, both tagged with
    /// <paramref name="suggestionId"/>. The target is resolved against the live document with
    /// the same <c>TextQuoteSelector</c> matching <see cref="LexicalHighlights"/> and
    /// <see cref="LexicalMarks.WrapTextQuoteInMarkAsync"/> use.
    /// </summary>
    /// <param name="suggestionId">Your own opaque id for this suggestion.</param>
    /// <param name="target">The existing text to strike through, optionally with the words either side.</param>
    /// <param name="proposedText">The replacement text to insert after the struck-through span.</param>
    /// <returns>
    /// <c>true</c> when the target resolved and the diff was rendered; <c>false</c> when the
    /// target does not appear in the document, or before the editor is created.
    /// </returns>
    public async ValueTask<bool> ShowSuggestionAsync(
        string suggestionId,
        LexicalTextQuote target,
        string proposedText)
    {
        ArgumentNullException.ThrowIfNull(target);
        var idJson = JsonSerializer.Serialize(suggestionId, LexicalJsonSerializerContext.Default.String);
        var targetJson = JsonSerializer.Serialize(target, LexicalJsonSerializerContext.Default.LexicalTextQuote);
        var textJson = JsonSerializer.Serialize(proposedText, LexicalJsonSerializerContext.Default.String);
        return await InvokeJsAsync("show", $"[{idJson},{targetJson},{textJson}]") == "true";
    }

    /// <summary>
    /// Removes the suggestion <paramref name="suggestionId"/> — restoring the struck-through
    /// original to plain text and dropping the proposed insertion — and returns how many
    /// suggestion nodes were cleared. Call it when the app accepts or rejects a suggestion and
    /// repaints the affected span itself.
    /// </summary>
    /// <param name="suggestionId">The suggestion to remove.</param>
    public async ValueTask<int> RemoveSuggestionAsync(string suggestionId) =>
        int.TryParse(await InvokeJsAsync("remove", Args(suggestionId)), out var removed) ? removed : 0;

    /// <summary>
    /// Removes <b>every</b> suggestion this editor is rendering, restoring the document to
    /// plain text. Returns how many suggestion nodes were cleared.
    /// </summary>
    public async ValueTask<int> ClearSuggestionsAsync() =>
        int.TryParse(await InvokeJsAsync("clear"), out var removed) ? removed : 0;

    /// <summary>The extension channel's JSON argument array for a single string.</summary>
    private static string Args(string value) => JsonSerializer.Serialize(
        new[] { value }, LexicalJsonSerializerContext.Default.StringArray);

    /// <summary>Receives the click push from the JS half.</summary>
    protected override async Task<string?> OnInvokeAsync(string method, string argsJson)
    {
        if (method != "clicked")
        {
            return null;
        }

        string? idsJson;
        using (var document = JsonDocument.Parse(argsJson))
        {
            var args = document.RootElement;
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() == 0)
            {
                return null;
            }
            idsJson = args[0].GetString();
        }

        var ids = string.IsNullOrEmpty(idsJson)
            ? []
            : JsonSerializer.Deserialize(idsJson, LexicalJsonSerializerContext.Default.StringArray) ?? [];
        await OnSuggestionClicked.InvokeAsync(ids);
        return null;
    }
}
