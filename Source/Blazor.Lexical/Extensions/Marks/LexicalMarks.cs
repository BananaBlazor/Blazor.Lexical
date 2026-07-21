using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// Adds mark (highlight) nodes to a <see cref="LexicalEditor"/>: nest
/// <c>&lt;LexicalMarks /&gt;</c> inside the editor and you can attach one of
/// <b>your own</b> ids to a span of text — the building block for comments,
/// annotations, suggestions and search highlighting.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mark ids are yours.</b> The library never generates one and never interprets one:
/// pass whatever key your application already has (a comment id, a suggestion GUID, a
/// search-term hash) to <see cref="WrapSelectionInMarkAsync"/>, and it comes back
/// unchanged from <see cref="GetMarkIdsAsync"/>, the click callback, and the document's
/// serialized JSON.
/// </para>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalToolbar /&gt;
///   &lt;LexicalMarks @@ref="_marks" OnMarkClicked="ShowComments" /&gt;
/// &lt;/LexicalEditor&gt;
/// &lt;button @@onclick="() =&gt; _marks.WrapSelectionInMarkAsync(comment.Id)"&gt;Comment&lt;/button&gt;
/// </code>
/// <para>
/// <b>Marks overlap.</b> A mark node carries an id <i>array</i>, so wrapping a second
/// mark across part of a first splits the text and merges the id sets rather than
/// replacing anything. Everything here is therefore plural — the caret can sit inside
/// several marks at once, and <see cref="GetMarkIdsAtSelectionAsync"/> returns all of
/// them.
/// </para>
/// <para>
/// Interop is opt-in as usual: with neither <see cref="OnMarkClicked"/> nor
/// <see cref="OnActiveMarksChanged"/> wired, a purely decorative highlighter performs
/// zero JS→.NET calls — the .NET→JS methods below still work, because those are calls
/// this side initiates.
/// </para>
/// </remarks>
public sealed class LexicalMarks : LexicalExtension
{
    internal override string? BuiltIn => "marks";

    /// <summary>
    /// Fires when the user clicks inside a marked span, carrying every mark id covering
    /// the click (several when marks overlap). The click is never swallowed — the caret
    /// still moves.
    /// </summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> OnMarkClicked { get; set; }

    /// <summary>
    /// Fires (debounced) when the set of mark ids under the caret/selection changes,
    /// carrying the current ids — empty when the selection has left every mark. Use it to
    /// keep a comment sidebar in step with the caret.
    /// </summary>
    [Parameter] public EventCallback<IReadOnlyList<string>> OnActiveMarksChanged { get; set; }

    /// <summary>
    /// The ids from the most recent <see cref="OnActiveMarksChanged"/> push, or empty
    /// before the first one. Only populated while that callback is wired — subscribing is
    /// what arms the channel.
    /// </summary>
    public IReadOnlyList<string> ActiveMarkIds { get; private set; } = [];

    /// <inheritdoc />
    protected override bool HasInvokeHandler =>
        OnMarkClicked.HasDelegate || OnActiveMarksChanged.HasDelegate;

    /// <summary>
    /// Wraps the current selection in a mark carrying <paramref name="markId"/>, returning
    /// whether there was a (non-empty) selection to wrap. Overlapping an existing mark
    /// merges the ids rather than replacing them. No-op (<c>false</c>) before the editor
    /// is created.
    /// </summary>
    /// <param name="markId">Your own opaque id for this mark.</param>
    public async Task<bool> WrapSelectionInMarkAsync(string markId) =>
        await InvokeJsAsync("wrap", Args(markId)) == "true";

    /// <summary>The extension channel's JSON argument array for a single mark id.</summary>
    private static string Args(string markId) => JsonSerializer.Serialize(
        new[] { markId }, LexicalJsonSerializerContext.Default.StringArray);

    /// <summary>
    /// Removes <paramref name="markId"/> from the document, returning how many mark nodes
    /// it was cleared from. A node that carried only this id is unwrapped, leaving its
    /// text; one that carried others keeps them.
    /// </summary>
    /// <param name="markId">The id to remove.</param>
    /// <param name="silent">
    /// When <c>true</c>, the edit adds no undo step and does not raise
    /// <see cref="LexicalEditor.OnContentChanged"/> — for app-driven cleanup (a resolved
    /// comment thread, a cleared search) that the user did not perform and should not have
    /// to undo. Leave <c>false</c> for a removal the user asked for.
    /// </param>
    public async Task<int> RemoveMarkAsync(string markId, bool silent = false)
    {
        var args = $"[{JsonSerializer.Serialize(markId, LexicalJsonSerializerContext.Default.String)}"
            + $",{(silent ? "true" : "false")}]";
        return int.TryParse(await InvokeJsAsync("remove", args), out var removed) ? removed : 0;
    }

    /// <summary>
    /// Every mark id in the document, in document order and without duplicates. Empty
    /// before the editor is created.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetMarkIdsAsync() =>
        Deserialize(await InvokeJsAsync("ids"));

    /// <summary>
    /// The mark ids covering the current selection — several when marks overlap, empty
    /// when the selection is outside every mark.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetMarkIdsAtSelectionAsync() =>
        Deserialize(await InvokeJsAsync("idsAtSelection"));

    /// <summary>
    /// Decorates the spans carrying <paramref name="markId"/> with
    /// <c>data-lexical-mark-active</c> (<c>null</c> clears it) — the "this is the thread
    /// you're reading" highlight. Purely visual: it touches the DOM only, so it changes
    /// neither the document nor the undo stack.
    /// </summary>
    /// <param name="markId">The mark to highlight, or <c>null</c> to clear.</param>
    public Task SetActiveMarkAsync(string? markId) =>
        InvokeJsAsync("setActive", Args(markId ?? string.Empty));

    /// <summary>
    /// Scrolls the first span carrying <paramref name="markId"/> into view, returning
    /// whether one was found.
    /// </summary>
    /// <param name="markId">The mark to scroll to.</param>
    public async Task<bool> ScrollToMarkAsync(string markId) =>
        await InvokeJsAsync("scrollTo", Args(markId)) == "true";

    /// <summary>Receives the click and active-selection pushes from the JS half.</summary>
    protected override async Task<string?> OnInvokeAsync(string method, string argsJson)
    {
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

        var ids = Deserialize(idsJson);
        switch (method)
        {
            case "clicked":
                await OnMarkClicked.InvokeAsync(ids);
                return null;

            case "active":
                ActiveMarkIds = ids;
                await OnActiveMarksChanged.InvokeAsync(ids);
                return null;

            default:
                return null;
        }
    }

    /// <summary>Parses a JSON string array, tolerating null/empty.</summary>
    private static IReadOnlyList<string> Deserialize(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize(
                json, LexicalJsonSerializerContext.Default.StringArray) ?? [];
}
