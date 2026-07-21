namespace Blazor.Lexical;

/// <summary>
/// The top-level block the pointer is over, as reported by
/// <see cref="LexicalBlockGutter.OnBlockHovered"/>.
/// </summary>
/// <param name="NodeKey">
/// The block's Lexical node key.
/// <para>
/// <b>Ephemeral.</b> Node keys are assigned per editor instance and are <i>not</i>
/// serialized: they do not survive a save/reload, a
/// <see cref="LexicalEditor.SetEditorStateJsonAsync"/> round trip, or a second editor
/// showing the same document. Use one only within the life of this editor instance — for
/// anything you persist, use <paramref name="Index"/>, or attach a
/// <see cref="LexicalMarks">mark</see> whose id you own.
/// </para>
/// </param>
/// <param name="Index">The block's zero-based position among the document's top-level blocks.</param>
/// <param name="BlockType">
/// The rendered element's tag name, lowercased (<c>"p"</c>, <c>"h2"</c>, <c>"ul"</c>,
/// <c>"blockquote"</c>, <c>"table"</c>…).
/// </param>
/// <param name="TextPreview">The block's first 80 characters of text, for labelling UI.</param>
public sealed record LexicalBlockRef(
    string NodeKey,
    int Index,
    string BlockType,
    string TextPreview);

/// <summary>
/// Where a <see cref="LexicalBlockGutter"/> floats: which margin, and whether it sits
/// within the editor card or hangs off it. Rails sharing a position stack outward from
/// their anchor edge in declaration order.
/// </summary>
/// <remarks>
/// <c>Inside</c> rails live in the empty margin the editable surface already reserves, so
/// they cost no page width — the natural home for compact chrome like a drag grip.
/// <c>Outside</c> rails hang off the card into the page, which suits wider or
/// app-specific content that would crowd the text. Either stays reachable: the rail lingers
/// briefly once the pointer leaves the editor, so it can be travelled to.
/// </remarks>
public enum LexicalGutterPosition
{
    /// <summary>The right margin, within the editor card (the default).</summary>
    RightInside,

    /// <summary>The right margin, hanging off the outside of the card.</summary>
    RightOutside,

    /// <summary>
    /// The left margin, within the card — where the playground-style rail of
    /// <see cref="LexicalAddBlockButton"/> + <see cref="LexicalDragHandle"/> conventionally
    /// goes.
    /// </summary>
    LeftInside,

    /// <summary>The left margin, hanging off the outside of the card.</summary>
    LeftOutside,
}
