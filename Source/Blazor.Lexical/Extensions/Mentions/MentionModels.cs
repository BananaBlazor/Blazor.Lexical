namespace Blazor.Lexical;

/// <summary>
/// A single mention candidate returned by a <see cref="LexicalMention.Provider"/>.
/// The confirmed candidate is inserted as an atomic, styled reference token that
/// stores <see cref="Id"/> (an opaque, app-owned key) and the optional
/// <see cref="Url"/>, both of which survive HTML/JSON round-trips so the host can
/// re-resolve the reference later (see <see cref="LexicalEditor.GetMentionsAsync"/>
/// and <see cref="LexicalEditor.RefreshMentionsByValueAsync"/>).
/// </summary>
/// <param name="Id">
/// The opaque, app-owned identifier for the referenced entity. Blazor.Lexical never
/// interprets it — pack whatever the host needs (a GUID, a JSON blob). It is stored on
/// the inserted node and is the key used to refresh the display text later.
/// </param>
/// <param name="Text">The display text shown in the picker and inserted into the editor.</param>
/// <param name="Url">An optional link carried by the inserted reference.</param>
/// <param name="Secondary">
/// Optional secondary text shown beneath <see cref="Text"/> in the picker row
/// (e.g. a handle or subtitle). Not stored on the inserted node.
/// </param>
public sealed record MentionItem(string Id, string Text, string? Url = null, string? Secondary = null);

/// <summary>
/// Payload for <see cref="LexicalMention.OnSelected"/>, describing the candidate a
/// user confirmed from the picker. The reference node has already been inserted when
/// this fires; the callback exists so the host can record the selection (e.g. the
/// chosen <see cref="Id"/>) without diffing the document.
/// </summary>
/// <param name="ConfigId">The <see cref="LexicalMention"/> that produced the selection.</param>
/// <param name="Id">The confirmed candidate's opaque, app-owned id.</param>
/// <param name="Text">The confirmed candidate's display text.</param>
/// <param name="Url">The confirmed candidate's optional link.</param>
public sealed record LexicalMentionSelected(string ConfigId, string Id, string Text, string? Url);

/// <summary>
/// A snapshot of one mention reference node in the document, returned by
/// <see cref="LexicalEditor.GetMentionsAsync"/>. The host uses it to decide which
/// references to re-resolve, then applies updates with
/// <see cref="LexicalEditor.RefreshMentionAsync"/> or
/// <see cref="LexicalEditor.RefreshMentionsByValueAsync"/>.
/// </summary>
/// <param name="NodeKey">The Lexical node key, stable for the current editor session.</param>
/// <param name="ConfigId">The <see cref="LexicalMention"/> id this reference was created from.</param>
/// <param name="Initiator">The trigger character (e.g. "@", "#").</param>
/// <param name="Value">The opaque, app-owned id stored on the node (the re-resolution key).</param>
/// <param name="Text">The current display text.</param>
/// <param name="Url">The current link, or <c>null</c>.</param>
public sealed record LexicalMentionRef(
    string NodeKey,
    string ConfigId,
    string Initiator,
    string Value,
    string Text,
    string? Url);
