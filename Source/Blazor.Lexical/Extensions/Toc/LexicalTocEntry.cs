namespace Blazor.Lexical;

/// <summary>
/// One heading in a document's table of contents, with its nested sub-headings.
/// Produced by <see cref="LexicalToc"/> and rendered by <see cref="LexicalTocList"/>.
/// </summary>
/// <param name="AnchorId">
/// The heading's anchor — the <c>id</c> stamped onto its element, so
/// <c>&lt;a href="#{AnchorId}"&gt;</c> navigates to it.
/// <para>
/// It is <b>derived from the heading's text</b> (lowercased, non-alphanumerics collapsed
/// to dashes, deduped with a numeric suffix), which means renaming a heading changes its
/// anchor and invalidates any link saved against the old one. Anchors are also
/// page-global: give each editor its own <see cref="LexicalToc.AnchorPrefix"/> when a
/// page hosts more than one.
/// </para>
/// </param>
/// <param name="Level">The heading level: 1 for <c>&lt;h1&gt;</c> through 6 for <c>&lt;h6&gt;</c>.</param>
/// <param name="Text">The heading's plain text.</param>
/// <param name="Children">
/// The headings nested under this one. A level jump (an <c>h3</c> directly under an
/// <c>h1</c>) is preserved rather than normalized — the outline mirrors the document.
/// </param>
public sealed record LexicalTocEntry(
    string AnchorId,
    int Level,
    string Text,
    IReadOnlyList<LexicalTocEntry> Children);
