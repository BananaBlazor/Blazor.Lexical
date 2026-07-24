namespace Blazor.Lexical;

/// <summary>
/// A confirmed comment from a <see cref="LexicalCommentComposer"/>: the app-owned
/// <paramref name="MarkId"/> the selected span was wrapped under, and the
/// <paramref name="Text"/> the user typed.
/// </summary>
/// <remarks>
/// The <see cref="MarkId"/> is whatever the app supplied — either the id passed to
/// <see cref="LexicalCommentComposer.OpenAsync(string)"/> or the one its
/// <see cref="LexicalCommentComposer.NewMarkId"/> factory returned. When neither was
/// provided (an <c>&lt;LexicalAddCommentButton&gt;</c> with no factory) the composer
/// mints a UUIDv7 and reports it here, so the app always learns the id it must store
/// its comment thread under. The same id round-trips through <see cref="LexicalMarks"/>
/// — its click and query callbacks, and the document's serialized JSON.
/// </remarks>
/// <param name="MarkId">The app-owned id the commented span was wrapped under.</param>
/// <param name="Text">The comment text the user typed.</param>
public readonly record struct CommentComposition(string MarkId, string Text);
