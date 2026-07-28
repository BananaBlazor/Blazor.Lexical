namespace Blazor.Lexical;

/// <summary>
/// Raised by <see cref="LexicalCommentComposer.OnOpened"/> the moment the composer
/// floats over a selection — carrying the app-owned <paramref name="MarkId"/> the
/// confirmed mark will use and the <paramref name="SelectedText"/> that was selected.
/// </summary>
/// <remarks>
/// It exists for the <see cref="LexicalCommentComposer.ChildContent"/> case: a custom
/// form (a department/severity/body layout, a suggested-edit field) uses this to reset
/// itself and show what is being commented on, since — unlike the built-in textarea — the
/// library does not know or manage the form's fields. The <see cref="MarkId"/> is the same
/// id <see cref="CommentComposition.MarkId"/> will carry on confirm, so the form can file
/// its draft under it before the user submits.
/// </remarks>
/// <param name="MarkId">The app-owned id the commented span will be wrapped under on confirm.</param>
/// <param name="SelectedText">The text of the selected span the composer opened over.</param>
public readonly record struct CommentComposerOpened(string MarkId, string SelectedText);
