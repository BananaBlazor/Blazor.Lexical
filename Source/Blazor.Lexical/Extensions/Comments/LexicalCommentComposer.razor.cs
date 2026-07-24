using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// A floating, in-editor comment input — the Lexical playground's
/// <c>CommentPlugin</c>/<c>CommentInputBox</c> as a reusable primitive. Nest it inside a
/// <see cref="LexicalEditor"/> (alongside a <see cref="LexicalMarks"/>, which it builds
/// on) and open it over the current selection; on confirm it wraps that selection in a
/// mark carrying <b>your own</b> id and raises <see cref="OnSubmit"/>.
/// </summary>
/// <remarks>
/// <para>
/// The reason this is a library primitive rather than something you hand-roll app-side is
/// the three things that are genuinely hard to reproduce over interop: floating the box at
/// the selection rect; the <i>blur problem</i> (a textarea taking focus collapses the
/// editor's selection, so there is nothing left to wrap by the time you confirm — solved
/// by capturing the selection the instant the box opens); and keeping the target visible
/// while you type (a transient compose-highlight, painted with the CSS Custom Highlight API
/// so it survives the selection moving into the textarea).
/// </para>
/// <para>
/// <b>It requires a sibling <see cref="LexicalMarks"/>.</b> Confirming wraps into
/// <c>@lexical/mark</c>'s <c>MarkNode</c> — the same node, and the same overlap-merge
/// behaviour, that <see cref="LexicalMarks"/> registers — so place both:
/// </para>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalMarks @@ref="_marks" OnMarkClicked="ShowThread" /&gt;
///   &lt;LexicalCommentComposer @@ref="_composer" OnSubmit="SaveComment" /&gt;
///   &lt;LexicalFloatingToolbar&gt;
///     &lt;LexicalAddCommentButton /&gt;
///   &lt;/LexicalFloatingToolbar&gt;
/// &lt;/LexicalEditor&gt;
/// </code>
/// <para>
/// <b>Mark ids are yours</b> (the <see cref="LexicalMarks"/> contract). Supply the id per
/// open via <see cref="OpenAsync(string)"/>, or a <see cref="NewMarkId"/> factory the
/// bundled <see cref="LexicalAddCommentButton"/> calls. If you supply neither and open
/// from the button, the composer mints a UUIDv7 — and <see cref="OnSubmit"/> always
/// carries the final id, so you still learn the key to file your comment thread under.
/// </para>
/// <para>
/// Interop is opt-in as usual: <see cref="OpenAsync(string)"/> works regardless (it is a
/// call this side initiates), but the JS→.NET pushes are armed only when you wire
/// <see cref="OnSubmit"/>/<see cref="OnCancel"/> or supply <see cref="NewMarkId"/>.
/// </para>
/// </remarks>
public sealed partial class LexicalCommentComposer : LexicalExtension
{
    internal override string? BuiltIn => "comments";

    /// <summary>Placeholder text for the comment textarea.</summary>
    [Parameter] public string Placeholder { get; set; } = "Add a comment…";

    /// <summary>Extra CSS class(es) for the floating box.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>
    /// Whether a pointer press outside the open box cancels the compose (raising
    /// <see cref="OnCancel"/>). <c>true</c> by default.
    /// </summary>
    [Parameter] public bool CloseOnClickAway { get; set; } = true;

    /// <summary>
    /// Optional factory for the mark id when the compose is opened by a
    /// <see cref="LexicalAddCommentButton"/> (rather than by
    /// <see cref="OpenAsync(string)"/>, which carries its own id). Called once per open,
    /// on the .NET side, so it can draw on your own id scheme (a comment id, a GUID).
    /// <c>null</c> (the default) lets the composer mint a UUIDv7 instead.
    /// </summary>
    [Parameter] public Func<string>? NewMarkId { get; set; }

    /// <summary>
    /// Fires when the user confirms a comment (the Comment button, or ⌘/Ctrl+Enter),
    /// after the selection has been wrapped in its mark. Carries the app-owned mark id and
    /// the typed text.
    /// </summary>
    [Parameter] public EventCallback<CommentComposition> OnSubmit { get; set; }

    /// <summary>
    /// Fires when the user dismisses the box (the Cancel button, Escape, or a click away)
    /// without wrapping anything.
    /// </summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    // The .NET→JS `open` call is always available; the gate is only over JS→.NET. It is
    // armed when either push is wired, or when a factory is supplied (the add button's
    // `compose` request rides the same channel).
    /// <inheritdoc />
    protected override bool HasInvokeHandler =>
        OnSubmit.HasDelegate || OnCancel.HasDelegate || NewMarkId is not null;

    /// <inheritdoc />
    protected override object? GetOptions() => JsonSerializer.SerializeToElement(
        new CommentComposerExtensionOptionsDto
        {
            HasMarkIdFactory = NewMarkId is not null,
            CloseOnClickAway = CloseOnClickAway,
        },
        LexicalJsonSerializerContext.Default.CommentComposerExtensionOptionsDto);

    /// <summary>
    /// Opens the composer over the current selection, capturing it so the textarea can
    /// take focus without losing the range. Returns whether it opened — <c>false</c> when
    /// the selection is collapsed or empty (nothing to comment on), or before the editor
    /// is created. On confirm the captured span is wrapped in a mark carrying
    /// <paramref name="markId"/>.
    /// </summary>
    /// <param name="markId">Your own opaque id for the comment's mark.</param>
    public async Task<bool> OpenAsync(string markId) =>
        await InvokeJsAsync("open", Args(markId)) == "true";

    /// <summary>The extension channel's JSON argument array for a single string.</summary>
    private static string Args(string value) => JsonSerializer.Serialize(
        new[] { value }, LexicalJsonSerializerContext.Default.StringArray);

    /// <summary>Receives the submit/cancel/compose pushes from the JS half.</summary>
    protected override async Task<string?> OnInvokeAsync(string method, string argsJson)
    {
        switch (method)
        {
            case "submit":
            {
                var (markId, text) = ReadSubmitArgs(argsJson);
                await OnSubmit.InvokeAsync(new CommentComposition(markId, text));
                return null;
            }

            case "cancel":
                await OnCancel.InvokeAsync();
                return null;

            case "compose":
                // The add-comment button was pressed with a NewMarkId factory in play: mint
                // the id here and open. A round trip is fine — the selection survives it
                // (the button's mousedown preventDefault kept it), and OpenAsync captures
                // the live selection when it lands.
                if (NewMarkId is not null)
                {
                    await OpenAsync(NewMarkId());
                }
                return null;

            default:
                return null;
        }
    }

    /// <summary>Parses the <c>[markId, text]</c> argument array of a submit push.</summary>
    private static (string MarkId, string Text) ReadSubmitArgs(string argsJson)
    {
        using var document = JsonDocument.Parse(argsJson);
        var args = document.RootElement;
        if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() < 2)
        {
            return (string.Empty, string.Empty);
        }
        return (args[0].GetString() ?? string.Empty, args[1].GetString() ?? string.Empty);
    }
}
