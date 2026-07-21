using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// Adds a live table of contents to a <see cref="LexicalEditor"/>: nest
/// <c>&lt;LexicalToc /&gt;</c> inside the editor and every heading gets an <c>id</c>
/// derived from its text, so <c>#fragment</c> links work — and, optionally, an outline
/// is rendered into an element you name (<see cref="TargetSelector"/>) and/or pushed to
/// .NET (<see cref="OnTocChanged"/>).
/// </summary>
/// <remarks>
/// <para>
/// Both surfaces are opt-in and independent. With only a <see cref="TargetSelector"/>
/// the whole feature is client-side — JS builds the list and owns the click-to-scroll
/// behaviour, and no interop happens at all. Wire <see cref="OnTocChanged"/> instead (or
/// as well) to receive the model in C# and render it yourself, most simply with
/// <see cref="LexicalTocList"/>.
/// </para>
/// <code>
/// &lt;nav id="outline"&gt;&lt;/nav&gt;
/// &lt;LexicalEditor&gt;
///   &lt;LexicalToolbar /&gt;
///   &lt;LexicalToc TargetSelector="#outline" MaxLevel="3" /&gt;
/// &lt;/LexicalEditor&gt;
/// </code>
/// <para>
/// <b>The document is never modified.</b> Anchors are written straight onto the rendered
/// DOM, outside any editor update, so they add no undo step and do not change what
/// <see cref="LexicalEditor.GetEditorStateJsonAsync"/> returns — a document authored with
/// this extension serializes identically to one authored without it. The cost of that is
/// spelled out on <see cref="LexicalTocEntry.AnchorId"/>: anchors follow the heading
/// text, so renaming a heading invalidates saved links to it.
/// </para>
/// <para>
/// The <see cref="TargetSelector"/> element must already exist when the editor is
/// created — the extension resolves it once, and there is no rescan (the same limitation
/// the overlays have).
/// </para>
/// </remarks>
public sealed class LexicalToc : LexicalExtension
{
    internal override string? BuiltIn => "toc";

    /// <summary>
    /// CSS selector of the element the outline is rendered into — typically a
    /// <c>&lt;nav&gt;</c> outside the editor. JS builds a nested
    /// <c>&lt;ol class="blazor-lexical__toc"&gt;</c> there and handles clicks itself, so
    /// this surface costs no interop. Leave unset to receive the model in .NET only.
    /// </summary>
    [Parameter] public string? TargetSelector { get; set; }

    /// <summary>Shallowest heading level to include (1 = <c>&lt;h1&gt;</c>). Default 1.</summary>
    [Parameter] public int MinLevel { get; set; } = 1;

    /// <summary>Deepest heading level to include (3 = <c>&lt;h3&gt;</c>). Default 3.</summary>
    [Parameter] public int MaxLevel { get; set; } = 3;

    /// <summary>
    /// Prepended to every generated anchor. Element ids are page-global, so give each
    /// editor a distinct prefix when a page hosts more than one — otherwise two documents
    /// with a "Background" heading both claim <c>#background</c>.
    /// </summary>
    [Parameter] public string? AnchorPrefix { get; set; }

    /// <summary>
    /// When <c>true</c> (the default), the item for the heading currently at the top of
    /// the scroll container is marked with <c>data-lexical-toc-active</c>. Requires
    /// <see cref="TargetSelector"/>. The scroll container is found by walking up from the
    /// editor surface, which is a heuristic — exotic layouts may not be detected.
    /// </summary>
    [Parameter] public bool ScrollSpy { get; set; } = true;

    /// <summary>When <c>true</c> (the default), clicking an item scrolls smoothly.</summary>
    [Parameter] public bool SmoothScroll { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, clicking an item also places the caret in that heading. Default
    /// <c>false</c>: navigating an outline is usually reading, not editing.
    /// </summary>
    [Parameter] public bool FocusOnClick { get; set; }

    /// <summary>
    /// Fires (debounced) whenever the outline changes, carrying the heading tree. Wiring
    /// it is what enables the .NET push at all: with no delegate this extension performs
    /// zero JS→.NET calls, and typing that doesn't change a heading never pushes either
    /// way.
    /// </summary>
    [Parameter] public EventCallback<IReadOnlyList<LexicalTocEntry>> OnTocChanged { get; set; }

    /// <summary>
    /// The most recent outline, or an empty list before the first push. Only populated
    /// while <see cref="OnTocChanged"/> is wired — that is what arms the channel.
    /// </summary>
    public IReadOnlyList<LexicalTocEntry> Entries { get; private set; } = [];

    /// <summary>
    /// Raised whenever <see cref="Entries"/> updates. Multicast, unlike the
    /// consumer-owned <see cref="OnTocChanged"/>, mirroring
    /// <see cref="LexicalEditor.ContentChanged"/>: it fans out a crossing that has already
    /// happened, so it adds no interop of its own.
    /// </summary>
    public event Action<IReadOnlyList<LexicalTocEntry>>? TocChanged;

    /// <inheritdoc />
    protected override bool HasInvokeHandler => OnTocChanged.HasDelegate;

    /// <inheritdoc />
    protected override object? GetOptions() => JsonSerializer.SerializeToElement(
        new TocExtensionOptionsDto
        {
            TargetSelector = TargetSelector,
            MinLevel = MinLevel,
            MaxLevel = MaxLevel,
            AnchorPrefix = AnchorPrefix,
            ScrollSpy = ScrollSpy,
            SmoothScroll = SmoothScroll,
            FocusOnClick = FocusOnClick,
        },
        LexicalJsonSerializerContext.Default.TocExtensionOptionsDto);

    /// <summary>
    /// Reads the current outline on demand — the pull counterpart of
    /// <see cref="OnTocChanged"/>, for hosts that want the tree without subscribing.
    /// Empty before the editor is created.
    /// </summary>
    public async Task<IReadOnlyList<LexicalTocEntry>> GetTocAsync() =>
        Deserialize(await InvokeJsAsync("get"));

    /// <summary>
    /// Scrolls the heading carrying <paramref name="anchorId"/> into view, returning
    /// whether one was found. No-op (<c>false</c>) before the editor is created.
    /// </summary>
    /// <param name="anchorId">An <see cref="LexicalTocEntry.AnchorId"/>.</param>
    public async Task<bool> ScrollToAnchorAsync(string anchorId)
    {
        var json = await InvokeJsAsync(
            "scrollTo", JsonSerializer.Serialize(new[] { anchorId }));
        return json == "true";
    }

    /// <summary>Receives the outline push from the JS half.</summary>
    protected override async Task<string?> OnInvokeAsync(string method, string argsJson)
    {
        if (method != "toc")
        {
            return null;
        }

        string? treeJson;
        using (var document = JsonDocument.Parse(argsJson))
        {
            var args = document.RootElement;
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() == 0)
            {
                return null;
            }
            treeJson = args[0].GetString();
        }

        Entries = Deserialize(treeJson);
        TocChanged?.Invoke(Entries);
        await OnTocChanged.InvokeAsync(Entries);
        return null;
    }

    /// <summary>Parses a JSON heading tree, tolerating null/empty.</summary>
    private static IReadOnlyList<LexicalTocEntry> Deserialize(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize(
                json, LexicalJsonSerializerContext.Default.LexicalTocEntryArray) ?? [];
}
