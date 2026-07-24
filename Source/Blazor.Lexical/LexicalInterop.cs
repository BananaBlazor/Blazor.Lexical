using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazor.Lexical;

/// <summary>
/// Wire model for the options object passed to the JS <c>create</c> function.
/// Serialized with the source-generated <see cref="LexicalJsonSerializerContext"/>
/// so the Blazor↔JS payload needs no runtime reflection (trim/AOT-safe).
/// </summary>
internal sealed class LexicalCreateOptions
{
    /// <summary>
    /// Lexical <c>EditorThemeClasses</c>. Pre-serialized to a <see cref="JsonElement"/>
    /// so the arbitrary theme shape rides through as opaque JSON.
    /// </summary>
    public JsonElement? Theme { get; set; }

    public bool ReadOnly { get; set; }

    public bool EnableHistory { get; set; }

    /// <summary>
    /// Which JS→.NET push channels to enable. Both off (the default) means JS
    /// never calls into .NET — typing and formatting stay entirely client-side.
    /// </summary>
    public LexicalNotifyFlags Notify { get; set; } = new();

    /// <summary>
    /// Every extension this editor runs, in load order — the library's own features
    /// (table, mentions) first, then the consumer extensions declared as children.
    /// One list, because both tiers speak the same descriptor contract and differ only
    /// in how their JS is bundled (<see cref="ExtensionDescriptorDto.BuiltIn"/> vs
    /// <see cref="ExtensionDescriptorDto.ModuleUrl"/>). Empty by default.
    /// </summary>
    public ExtensionDescriptorDto[] Extensions { get; set; } = [];

    /// <summary>
    /// Content to load into the editor as it is created, from whichever single
    /// <c>Initial*</c> parameter the host set. <c>null</c> for an empty editor.
    /// </summary>
    public LexicalInitialContentDto? InitialContent { get; set; }
}

/// <summary>
/// Wire model for the editor's initial content. One nested object rather than four
/// parallel fields, so the JS side has a single branch — and the "at most one format"
/// rule is enforced once, in C#.
/// </summary>
internal sealed class LexicalInitialContentDto
{
    /// <summary>Which format <see cref="Value"/> is in ('text' | 'html' | 'markdown' | 'stateJson').</summary>
    public string Format { get; set; } = "";

    /// <summary>The content itself, in <see cref="Format"/>.</summary>
    public string Value { get; set; } = "";
}

/// <summary>
/// Wire model for one <see cref="LexicalExtension"/> passed to JS <c>create</c>. Like
/// <see cref="MentionConfigDto"/>, no delegate crosses interop — only
/// <see cref="HasInvokeHandler"/> (whether JS may call back at all) does; calls are
/// routed back to the owning extension by <see cref="Id"/>.
/// </summary>
internal sealed class ExtensionDescriptorDto
{
    /// <summary>Stable extension id; routes calls in both directions.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Names a module compiled into our own bundle (<c>"table"</c>, <c>"mentions"</c>),
    /// which JS resolves through a closed literal-<c>import()</c> switch. This is the
    /// built-in bundling tier; <c>null</c> for everything a consumer writes. Takes
    /// precedence over <see cref="ModuleUrl"/>.
    /// </summary>
    public string? BuiltIn { get; set; }

    /// <summary>
    /// URL of the extension's ESM module, imported at runtime before the editor is
    /// created. <c>null</c> for a built-in, or for a C#-only extension (no JS half).
    /// </summary>
    public string? ModuleUrl { get; set; }

    /// <summary>
    /// Opaque per-instance configuration, pre-serialized so an arbitrary
    /// extension-owned shape rides through as JSON. Passed to the module's factory.
    /// </summary>
    public JsonElement? Options { get; set; }

    /// <summary>
    /// When true, the extension accepts JS→.NET calls (<c>InvokeExtensionAsync</c>).
    /// False means the JS half's <c>invokeDotNet</c> throws — zero interop.
    /// </summary>
    public bool HasInvokeHandler { get; set; }
}

/// <summary>
/// The mentions built-in's <c>GetOptions()</c> payload — the whole set of configs an
/// editor declared. Mentions is one extension instance rather than one per
/// <see cref="LexicalMention"/> because its freeform highlighting uses a single shared
/// text-entity matcher across every initiator; splitting it per config would register
/// competing transforms that unwrap each other's tokens.
/// </summary>
internal sealed class MentionsExtensionOptionsDto
{
    /// <summary>Every <see cref="LexicalMention"/> nested in the editor, in declaration order.</summary>
    public MentionConfigDto[] Configs { get; set; } = [];
}

/// <summary>
/// Wire model for one mention configuration, carried inside
/// <see cref="MentionsExtensionOptionsDto"/>. The .NET provider delegate itself never
/// crosses interop — only <see cref="HasProvider"/> (whether to query) does; queries are
/// routed back by <see cref="Id"/>.
/// </summary>
internal sealed class MentionConfigDto
{
    /// <summary>Stable config id; routes provider queries and is stored on inserted nodes.</summary>
    public string Id { get; set; } = "";

    /// <summary>The trigger character (e.g. '@', '#', '!').</summary>
    public string Initiator { get; set; } = "";

    /// <summary>CSS colour applied to the inserted/highlighted node.</summary>
    public string Color { get; set; } = "";

    /// <summary>When true, matching <c>&lt;initiator&gt;token</c> text is highlighted live.</summary>
    public bool Freeform { get; set; }

    /// <summary>When true, typing the initiator queries .NET for suggestions.</summary>
    public bool HasProvider { get; set; }

    /// <summary>When true, a confirmed selection notifies .NET (the <c>selected</c> invoke).</summary>
    public bool NotifySelected { get; set; }

    /// <summary>
    /// How long (ms) the picker waits for a provider response before giving up and
    /// closing the session. Zero disables the timeout. Ignored without a provider.
    /// </summary>
    public int QueryTimeoutMs { get; set; }
}

/// <summary>
/// Opt-in JS→.NET push channels. A channel is enabled only when the matching
/// <see cref="LexicalEditor"/> callback has a delegate, so the bare editor does
/// no interop.
/// </summary>
internal sealed class LexicalNotifyFlags
{
    /// <summary>Push debounced plain text on change (<c>OnContentChangedInternal</c>).</summary>
    public bool Content { get; set; }

    /// <summary>
    /// What the content push carries ('text' | 'signalOnly'); see
    /// <see cref="LexicalContentPayload"/>. Ignored while <see cref="Content"/> is false.
    /// </summary>
    public string ContentPayload { get; set; } = "text";

    /// <summary>Push selection formatting state (<c>OnSelectionChangedInternal</c>).</summary>
    public bool Selection { get; set; }

    /// <summary>
    /// Push the hovered top-level block (<c>OnBlockHoveredInternal</c>). Armed only when a
    /// <see cref="LexicalBlockGutter"/> is present <i>and</i> its
    /// <see cref="LexicalBlockGutter.OnBlockHovered"/> is wired.
    /// </summary>
    public bool BlockHover { get; set; }
}

/// <summary>
/// The TOC built-in's <c>GetOptions()</c> payload — the mirror of the JS
/// <c>TocOptionsDto</c>. Everything here is per-instance configuration; the outline model
/// itself travels the other way, over the extension channel.
/// </summary>
internal sealed class TocExtensionOptionsDto
{
    /// <summary>CSS selector of the element the outline is rendered into; null ⇒ none.</summary>
    public string? TargetSelector { get; set; }

    /// <summary>Shallowest heading level to include (1 = h1).</summary>
    public int MinLevel { get; set; } = 1;

    /// <summary>Deepest heading level to include (3 = h3).</summary>
    public int MaxLevel { get; set; } = 3;

    /// <summary>Prepended to every generated anchor, so two editors can't collide.</summary>
    public string? AnchorPrefix { get; set; }

    /// <summary>Mark the item for the heading at the top of the scroll container.</summary>
    public bool ScrollSpy { get; set; } = true;

    /// <summary>Scroll smoothly when an item is clicked.</summary>
    public bool SmoothScroll { get; set; } = true;

    /// <summary>Place the caret in the heading when an item is clicked.</summary>
    public bool FocusOnClick { get; set; }
}

/// <summary>
/// The stats built-in's <c>GetOptions()</c> payload — the mirror of the JS
/// <c>StatsOptionsDto</c>.
/// </summary>
internal sealed class StatsExtensionOptionsDto
{
    /// <summary>CSS selector of the element the formatted line is written into; null ⇒ none.</summary>
    public string? TargetSelector { get; set; }

    /// <summary>Template for that line; <c>{words}</c> and friends are substituted.</summary>
    public string? Template { get; set; }

    /// <summary>Reading speed used for the reading-time estimate.</summary>
    public int WordsPerMinute { get; set; } = 200;
}

/// <summary>
/// Options payload for the tab-indent extension, mirrored by the JS
/// <c>TabIndentOptionsDto</c>.
/// </summary>
internal sealed class TabIndentExtensionOptionsDto
{
    /// <summary>Maximum indent depth, or null for no cap.</summary>
    public int? MaxIndent { get; set; }
}

/// <summary>
/// The comment composer built-in's <c>GetOptions()</c> payload — the mirror of the JS
/// <c>CommentComposerOptionsDto</c>.
/// </summary>
internal sealed class CommentComposerExtensionOptionsDto
{
    /// <summary>
    /// Whether the host supplied a <c>NewMarkId</c> factory. When true the add-comment
    /// button asks .NET for an id (the <c>compose</c> push) before the box opens; when
    /// false the JS half mints a UUIDv7 and reports it back through <c>OnSubmit</c>.
    /// </summary>
    public bool HasMarkIdFactory { get; set; }

    /// <summary>Whether a mousedown outside the open box cancels the compose.</summary>
    public bool CloseOnClickAway { get; set; } = true;
}

/// <summary>
/// One highlight request on its way to the highlights built-in — the flattened
/// <see cref="LexicalTextQuote"/> plus the id it paints under. Mirrored JS-side by the
/// object <c>highlights.ts</c>'s <c>invoke</c> destructures.
/// </summary>
internal sealed class HighlightRequestDto
{
    /// <summary>The highlight set this belongs to (its <c>::highlight()</c> name).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The text to find. Whitespace is normalized on both sides before matching.</summary>
    public string Exact { get; set; } = string.Empty;

    /// <summary>Text expected immediately before <see cref="Exact"/>; disambiguates only.</summary>
    public string? Prefix { get; set; }

    /// <summary>Text expected immediately after <see cref="Exact"/>; disambiguates only.</summary>
    public string? Suffix { get; set; }

    /// <summary>Whether to scroll the match into view.</summary>
    public bool Scroll { get; set; }
}

/// <summary>
/// Wire model for the selection-state object pushed from JS via
/// <c>OnSelectionChangedInternal</c>. Deserialized with the source-generated
/// context, then projected into the public <see cref="LexicalSelectionState"/>
/// (see <see cref="LexicalSelectionState.FromDto"/>).
/// </summary>
internal sealed class LexicalSelectionStateDto
{
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderline { get; set; }
    public bool IsStrikethrough { get; set; }
    public bool IsCode { get; set; }
    public bool IsSubscript { get; set; }
    public bool IsSuperscript { get; set; }
    public bool IsLowercase { get; set; }
    public bool IsUppercase { get; set; }

    /// <summary>Raw block-type token ('paragraph' | 'h1'..'h6' | 'quote' | 'bullet' | 'number').</summary>
    public string? BlockType { get; set; }

    public bool IsLink { get; set; }

    /// <summary>Raw alignment token ('' | 'left' | 'center' | 'right' | 'justify').</summary>
    public string? Alignment { get; set; }

    public bool CanUndo { get; set; }
    public bool CanRedo { get; set; }

    /// <summary>The selected text ('' when the selection is collapsed).</summary>
    public string? Text { get; set; }
}

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> context for the Blazor↔JS
/// interop payloads, so serialization is reflection-free. Null leaves are omitted
/// so a <see cref="LexicalTheme"/> only emits the keys that were actually set.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LexicalCreateOptions))]
[JsonSerializable(typeof(LexicalNotifyFlags))]
[JsonSerializable(typeof(LexicalTheme))]
[JsonSerializable(typeof(LexicalSelectionStateDto))]
[JsonSerializable(typeof(MentionConfigDto))]
[JsonSerializable(typeof(MentionsExtensionOptionsDto))]
[JsonSerializable(typeof(ExtensionDescriptorDto))]
[JsonSerializable(typeof(MentionItem[]))]
[JsonSerializable(typeof(LexicalMentionRef[]))]
[JsonSerializable(typeof(TocExtensionOptionsDto))]
[JsonSerializable(typeof(LexicalTocEntry[]))]
[JsonSerializable(typeof(StatsExtensionOptionsDto))]
[JsonSerializable(typeof(TabIndentExtensionOptionsDto))]
[JsonSerializable(typeof(CommentComposerExtensionOptionsDto))]
[JsonSerializable(typeof(LexicalDocumentStats))]
[JsonSerializable(typeof(LexicalBlockRef))]
[JsonSerializable(typeof(HighlightRequestDto[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
internal partial class LexicalJsonSerializerContext : JsonSerializerContext
{
}
