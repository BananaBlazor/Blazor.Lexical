using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Lexical;

/// <summary>
/// A Blazor wrapper around the Lexical rich-text editor. Renders a
/// contenteditable surface, wires Lexical rich-text + history, and exposes
/// text get/set plus a change callback across the JS interop boundary.
/// </summary>
public partial class LexicalEditor : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private LexicalOptions Options { get; set; } = default!;

    /// <summary>Placeholder text shown while the editor is empty.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>When true, the editor is read-only.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Extra CSS class(es) applied to the outer wrapper.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>
    /// Optional theme for this instance. Prefer a strongly-typed
    /// <see cref="LexicalTheme"/> (e.g. <see cref="LexicalTheme.Default"/>); a raw
    /// anonymous object matching Lexical's <c>EditorThemeClasses</c> shape is also
    /// accepted as an escape hatch for keys not modelled by <see cref="LexicalTheme"/>.
    /// </summary>
    [Parameter] public object? Theme { get; set; }

    /// <summary>When true (default), Lexical history (undo/redo) is enabled.</summary>
    [Parameter] public bool EnableHistory { get; set; } = true;

    /// <summary>Stable element id; auto-generated when not supplied.</summary>
    [Parameter] public string Id { get; set; } = $"lexical-{Guid.NewGuid():N}";

    /// <summary>
    /// The document to load as the editor is created, in whichever format it is
    /// written — build it with the <see cref="LexicalContent"/> factories
    /// (<c>LexicalContent.FromHtml(html)</c> and friends). <c>null</c> (the default)
    /// starts empty.
    /// <para>
    /// This is the no-<c>@ref</c> way to preload a document: the content is applied
    /// inside the JS <c>create()</c> call, so it is there on the first paint (no
    /// empty flash) and it is the history baseline — undo cannot erase it.
    /// </para>
    /// <para>
    /// It is read <b>once</b>, at creation; changing it afterwards has no effect.
    /// That is deliberate — this is initial content, not a binding. To replace the
    /// content later, call <see cref="SetHtmlAsync"/> (or the sibling <c>Set*</c>
    /// method), which is explicit about re-parsing the whole document.
    /// </para>
    /// </summary>
    [Parameter] public LexicalContent? InitialContent { get; set; }

    /// <summary>
    /// Fires (debounced) when the content changes, carrying the document in the format
    /// named by <see cref="ContentPayload"/> — the format rides along on
    /// <see cref="LexicalContent.Format"/>, so a handler never has to assume. Under
    /// <see cref="LexicalContentPayload.SignalOnly"/> the argument is an empty
    /// <see cref="LexicalContent"/>: the signal, with no document.
    /// <para>
    /// Subscribing is what enables the channel at all: with no delegate, JS never calls
    /// into .NET on typing.
    /// </para>
    /// </summary>
    [Parameter] public EventCallback<LexicalContent> OnContentChanged { get; set; }

    /// <summary>
    /// What the content-changed push carries (default
    /// <see cref="LexicalContentPayload.Text"/>). Declaring the format you actually
    /// want keeps the channel to one crossing per change — the document is serialized
    /// JS-side inside the debounce rather than pushed as text and then fetched again.
    /// Use <see cref="LexicalContentPayload.SignalOnly"/> when a dirty signal is all
    /// you need. Only meaningful while <see cref="OnContentChanged"/> is subscribed.
    /// </summary>
    [Parameter] public LexicalContentPayload ContentPayload { get; set; } = LexicalContentPayload.Text;

    /// <summary>
    /// The document carried by the most recent <see cref="OnContentChanged"/> push, or
    /// <c>null</c> before the first one. A cheap read for hosts that already subscribe
    /// to the content channel — no interop call, unlike <see cref="GetTextAsync"/> and
    /// friends. Stays <c>null</c> when the channel is off, and under
    /// <see cref="LexicalContentPayload.SignalOnly"/> (nothing is pushed to keep).
    /// </summary>
    public LexicalContent? LastContent { get; private set; }

    /// <summary>
    /// Whether the underlying JS editor exists yet — <c>true</c> from just before
    /// <see cref="OnReady"/> fires until the component is disposed. Every programmatic
    /// method silently no-ops while this is <c>false</c>, so it is the guard to use in
    /// code that may run before the first render completes.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Fires once, after the underlying editor has been created and its JS module is
    /// loaded — i.e. the first moment the programmatic methods (<see cref="SetHtmlAsync"/>,
    /// <see cref="SetTextAsync"/>, etc.) will actually take effect. Use it to preload
    /// initial content: calls made before this fires are silently ignored.
    /// </summary>
    [Parameter] public EventCallback OnReady { get; set; }

    /// <summary>
    /// Fires when the selection's formatting changes, carrying the current
    /// <see cref="LexicalSelectionState"/>. Drives toolbar active/enabled state.
    /// </summary>
    [Parameter] public EventCallback<LexicalSelectionState> OnSelectionChanged { get; set; }

    /// <summary>
    /// The most recent selection formatting snapshot, or <c>null</c> before the
    /// first selection event. Updated just before <see cref="OnSelectionChanged"/> fires.
    /// </summary>
    public LexicalSelectionState? SelectionState { get; private set; }

    /// <summary>
    /// Raised whenever <see cref="SelectionState"/> updates. Multicast, unlike the
    /// consumer-owned <see cref="OnSelectionChanged"/>. Fires only when the selection
    /// push channel is active (i.e. <see cref="OnSelectionChanged"/> is subscribed).
    /// </summary>
    public event Action<LexicalSelectionState>? SelectionStateChanged;

    /// <summary>
    /// Raised whenever the content channel pushes, carrying the same
    /// <see cref="LexicalContent"/> <see cref="OnContentChanged"/> receives. Multicast,
    /// unlike the consumer-owned <see cref="OnContentChanged"/>, so a C#-only extension
    /// can observe the document without a channel of its own.
    /// <para>
    /// It fans out something that already crossed, so it adds no interop — and it is
    /// silent unless the <b>host</b> armed the channel: with no
    /// <see cref="OnContentChanged"/> delegate nothing is pushed, and the format is
    /// whichever one the host's <see cref="ContentPayload"/> named.
    /// </para>
    /// </summary>
    public event Action<LexicalContent>? ContentChanged;

    /// <summary>
    /// Editor chrome to render inside the editor root, above the editable surface —
    /// typically a <see cref="LexicalToolbar"/>. Rendered within the root so the JS
    /// delegated command dispatcher (and cascaded editor) reach the toolbar buttons.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private ElementReference _root;
    private IJSObjectReference? _module;
    private DotNetObjectReference<LexicalEditor>? _dotNetRef;
    private bool _appliedReadOnly;
    private bool _notifyContent;
    private bool _notifySelection;
    private LexicalContentPayload _contentPayload = LexicalContentPayload.Text;
    private readonly List<LexicalMention> _mentions = [];
    private readonly List<LexicalExtension> _extensions = [];
    private readonly List<LexicalExtension> _builtIns = [];
    private IReadOnlyList<LexicalExtension>? _createdExtensions;

    /// <summary>
    /// Raised when this editor's extension set changes in a way chrome should react to:
    /// once per child registration, and again when <c>create()</c> freezes the set
    /// <see cref="HasExtension{T}"/> answers from. <see cref="LexicalToolbar"/> and
    /// <see cref="LexicalSlashMenu"/> subscribe so a <c>&lt;LexicalTables/&gt;</c>
    /// declared *after* them still lights their table chrome up. Not part of the
    /// public API.
    /// </summary>
    internal event Action? ExtensionsChanged;

    /// <summary>
    /// This editor's features and extensions in load order, materializing the built-in
    /// ones (mentions) on first use. They are the same contract as a consumer
    /// extension — only their JS bundling differs — so they ride the same descriptor
    /// list and the same id-routed invoke channel, which is what keeps the two tiers
    /// honest: nothing here can reach anything a consumer extension cannot.
    /// </summary>
    private IEnumerable<LexicalExtension> AllExtensions()
    {
        if (_builtIns.Count == 0 && _mentions.Count > 0)
        {
            _builtIns.Add(new LexicalMentionExtension(_mentions));
        }

        return _builtIns.Concat(_extensions);
    }

    /// <summary>
    /// Whether an extension of type <typeparamref name="T"/> is part of this editor —
    /// the way chrome asks whether a feature is present (e.g. the toolbar's table picker
    /// checks <c>HasExtension&lt;LexicalTables&gt;()</c>).
    /// <para>
    /// Once the underlying editor exists the answer comes from the set frozen at
    /// <c>create()</c> — never the live registry — so chrome can never light up for a
    /// chunk that was not fetched: extensions are collected once, and one added
    /// afterwards is not loaded until the editor is recreated. Before that it reads the
    /// registry as it stands, because chrome that renders a JS-scanned marker (the
    /// toolbar's table picker) must have it in the DOM by the time <c>create()</c> looks
    /// — which is what <c>ExtensionsChanged</c> re-renders for.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The extension type to look for.</typeparam>
    public bool HasExtension<T>() where T : LexicalExtension =>
        _createdExtensions is { } created
            ? created.Any(e => e is T)
            : _extensions.Any(e => e is T);

    /// <summary>
    /// Registers a child <see cref="LexicalMention"/> config. Called from the config's
    /// <c>OnInitialized</c>, so every config is collected before the editor is created.
    /// Not part of the public API.
    /// </summary>
    internal void RegisterMention(LexicalMention mention)
    {
        if (!_mentions.Contains(mention))
        {
            _mentions.Add(mention);
        }
    }

    /// <summary>Removes a child <see cref="LexicalMention"/> config. Not part of the public API.</summary>
    internal void UnregisterMention(LexicalMention mention) => _mentions.Remove(mention);

    /// <summary>
    /// Registers a child <see cref="LexicalExtension"/>. Called from the extension's
    /// <c>OnInitialized</c>, so every extension is collected — and its custom nodes can
    /// still be declared — before the editor is created. Not part of the public API.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A second instance of an extension type whose <c>AllowMultiple</c> is <c>false</c>
    /// is nested in this editor. Duplicating a single-instance extension would register
    /// its nodes and listeners twice, so it fails loudly rather than misbehaving.
    /// </exception>
    internal void RegisterExtension(LexicalExtension extension)
    {
        if (_extensions.Contains(extension))
        {
            return;
        }

        if (!extension.AllowMultiple
            && _extensions.Any(e => e.GetType() == extension.GetType()))
        {
            throw new InvalidOperationException(
                $"More than one <{extension.GetType().Name}> is nested in <LexicalEditor> " +
                $"'{Id}', but that extension is single-instance. Remove the duplicate, or " +
                "override AllowMultiple to allow several per editor.");
        }

        _extensions.Add(extension);
        ExtensionsChanged?.Invoke();
    }

    /// <summary>Removes a child <see cref="LexicalExtension"/>. Not part of the public API.</summary>
    internal void UnregisterExtension(LexicalExtension extension) => _extensions.Remove(extension);

    /// <summary>
    /// Calls an extension's JS half (the .NET→JS direction of the extension channel);
    /// returns its result as JSON. No-op (<c>null</c>) before the editor is created.
    /// Not part of the public API — reached via <c>LexicalExtension.InvokeJsAsync</c>.
    /// </summary>
    internal async Task<string?> InvokeExtensionJsAsync(
        string extensionId, string method, string argsJson)
    {
        if (_module is null)
        {
            return null;
        }
        return await _module.InvokeAsync<string?>(
            "invokeExtension", Id, extensionId, method, argsJson);
    }

    /// <summary>
    /// <see cref="InitialContent"/> as the interop wire model, or <c>null</c> for an
    /// empty editor. Because the format rides with the string, there is no ambiguity
    /// to resolve — and nothing to validate.
    /// </summary>
    private LexicalInitialContentDto? ToInitialContentDto() =>
        InitialContent is { } content
            ? new() { Format = content.Format.ToJsToken(), Value = content.Text ?? string.Empty }
            : null;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _module = await JS.InvokeAsync<IJSObjectReference>("import", Options.ModuleUrl);
        _dotNetRef = DotNetObjectReference.Create(this);

        // Enable a JS→.NET push channel only when its callback is actually wired.
        // With neither subscribed, JS performs no interop at all.
        _notifyContent = OnContentChanged.HasDelegate;
        _notifySelection = OnSelectionChanged.HasDelegate;
        _contentPayload = ContentPayload;

        var createOptions = new LexicalCreateOptions
        {
            Theme = ToThemeElement(Theme ?? Options.DefaultTheme),
            ReadOnly = ReadOnly,
            EnableHistory = EnableHistory,
            InitialContent = ToInitialContentDto(),
            Notify = new LexicalNotifyFlags
            {
                Content = _notifyContent,
                Selection = _notifySelection,
                ContentPayload = _contentPayload.ToJsToken(),
            },
            // One descriptor list for both tiers: the library's own features first (so
            // their nodes keep their place in createEditor's nodes[]), then the
            // consumer extensions. Built once here, since every input is settled by the
            // time the children have registered.
            Extensions = [.. AllExtensions().Select(e => e.ToDto())],
        };

        // Freeze the extension set the moment it is handed to JS. HasExtension answers
        // from this snapshot, so chrome can only ever reflect modules that were actually
        // loaded — and the event below is what lets chrome rendered before this point
        // (a <LexicalToolbar/> declared above <LexicalTables/>) pick the answer up.
        List<LexicalExtension> createdExtensions = [.. AllExtensions()];
        _createdExtensions = createdExtensions;
        ExtensionsChanged?.Invoke();

        // Serialize the options through the source-generated context, then pass the
        // resulting JsonElement (handled by a built-in interop converter). This keeps
        // the Blazor→JS payload reflection-free rather than relying on interop's
        // default reflection-based serializer.
        var optionsElement = JsonSerializer.SerializeToElement(
            createOptions, LexicalJsonSerializerContext.Default.LexicalCreateOptions);

        await _module.InvokeVoidAsync("create", Id, _root, _dotNetRef, optionsElement);
        _appliedReadOnly = ReadOnly;
        IsReady = true;

        // Extensions get their ready moment first — it is the first point at which
        // InvokeJsAsync reaches a live module — in registration order, each isolated the
        // same way a broken extension is isolated at load: one that throws is logged and
        // skipped rather than taking the editor (or its siblings) down.
        foreach (var extension in createdExtensions)
        {
            try
            {
                await extension.NotifyEditorReadyAsync();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(
                    $"[Blazor.Lexical] extension '{extension.GetType().Name}' failed in " +
                    $"OnEditorReadyAsync: {error}");
            }
        }

        // The editor and its methods are now live; let the host preload content.
        if (OnReady.HasDelegate)
        {
            await OnReady.InvokeAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_module is null)
        {
            return;
        }

        // Sync ReadOnly changes after the editor exists; initial state is applied
        // by create() above. Uses the JS setEditable touchpoint.
        if (_appliedReadOnly != ReadOnly)
        {
            _appliedReadOnly = ReadOnly;
            await _module.InvokeVoidAsync("setEditable", Id, !ReadOnly);
        }

        // Flip the push channels if callbacks were added/removed after create — or if
        // the content channel's payload mode changed.
        var content = OnContentChanged.HasDelegate;
        var selection = OnSelectionChanged.HasDelegate;
        if (content != _notifyContent
            || selection != _notifySelection
            || ContentPayload != _contentPayload)
        {
            _notifyContent = content;
            _notifySelection = selection;
            _contentPayload = ContentPayload;
            var flags = new LexicalNotifyFlags
            {
                Content = content,
                Selection = selection,
                ContentPayload = _contentPayload.ToJsToken(),
            };
            var flagsElement = JsonSerializer.SerializeToElement(
                flags, LexicalJsonSerializerContext.Default.LexicalNotifyFlags);
            await _module.InvokeVoidAsync("setNotifications", Id, flagsElement);
        }
    }

    /// <summary>
    /// Normalizes a user-supplied theme (Lexical <c>EditorThemeClasses</c>) into a
    /// <see cref="JsonElement"/> for the source-generated options payload. A theme
    /// passed as a <see cref="JsonElement"/> travels reflection-free; any other
    /// object shape is serialized with the default serializer, since its runtime
    /// type is arbitrary and cannot be known ahead of time.
    /// </summary>
    private static JsonElement? ToThemeElement(object? theme) => theme switch
    {
        null => null,
        JsonElement element => element,
        LexicalTheme typed => JsonSerializer.SerializeToElement(
            typed, LexicalJsonSerializerContext.Default.LexicalTheme),
        _ => JsonSerializer.SerializeToElement(theme),
    };

    /// <summary>Reads the current plain-text content from the editor.</summary>
    public async Task<string> GetTextAsync()
    {
        if (_module is null)
        {
            return string.Empty;
        }
        return await _module.InvokeAsync<string>("getText", Id);
    }

    /// <summary>Replaces the editor content with a single paragraph of <paramref name="text"/>.</summary>
    public async Task SetTextAsync(string text)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("setText", Id, text);
    }

    /// <summary>
    /// Inserts <paramref name="text"/> at the current selection, replacing any selected
    /// range (unlike <see cref="SetTextAsync"/>, which replaces the whole document).
    /// No-op when there is no active selection.
    /// </summary>
    public async Task InsertTextAsync(string text)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("insertText", Id, text);
    }

    /// <summary>Serializes the editor content to an HTML string.</summary>
    public async Task<string> GetHtmlAsync()
    {
        if (_module is null)
        {
            return string.Empty;
        }
        return await _module.InvokeAsync<string>("getHtml", Id);
    }

    /// <summary>Replaces the editor content with nodes parsed from <paramref name="html"/>.</summary>
    public async Task SetHtmlAsync(string html)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("setHtml", Id, html);
    }

    /// <summary>Serializes the editor content to a Markdown string.</summary>
    public async Task<string> GetMarkdownAsync()
    {
        if (_module is null)
        {
            return string.Empty;
        }
        return await _module.InvokeAsync<string>("getMarkdown", Id);
    }

    /// <summary>Replaces the editor content with nodes parsed from <paramref name="markdown"/>.</summary>
    public async Task SetMarkdownAsync(string markdown)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("setMarkdown", Id, markdown);
    }

    /// <summary>
    /// Serializes the full editor state to its canonical JSON string. This is the
    /// highest-fidelity format and the one to persist.
    /// </summary>
    public async Task<string> GetEditorStateJsonAsync()
    {
        if (_module is null)
        {
            return string.Empty;
        }
        return await _module.InvokeAsync<string>("getEditorStateJson", Id);
    }

    /// <summary>Restores the editor from a canonical editor-state JSON string.</summary>
    public async Task SetEditorStateJsonAsync(string json)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("setEditorStateJson", Id, json);
    }

    /// <summary>
    /// Replaces the editor content with <paramref name="content"/>, in whichever
    /// format it carries — the runtime-dispatch counterpart of
    /// <see cref="SetHtmlAsync"/> and friends, and the imperative twin of
    /// <see cref="InitialContent"/>. Use it when the format is a value rather than
    /// something known at the call site (content out of a database, say); call the
    /// format-specific method when you already know which one you mean.
    /// </summary>
    public Task SetContentAsync(LexicalContent content) => content.Format switch
    {
        LexicalContentFormat.Text => SetTextAsync(content.Text),
        LexicalContentFormat.Html => SetHtmlAsync(content.Text),
        LexicalContentFormat.Markdown => SetMarkdownAsync(content.Text),
        LexicalContentFormat.EditorStateJson => SetEditorStateJsonAsync(content.Text),
        _ => throw new ArgumentOutOfRangeException(nameof(content), content.Format, null),
    };

    /// <summary>
    /// Reads the editor content in <paramref name="format"/>, returned as a
    /// <see cref="LexicalContent"/> so the format travels with the text — feed the
    /// result straight back to <see cref="SetContentAsync"/> or another editor's
    /// <see cref="InitialContent"/>. The format-specific <c>Get*Async</c> methods
    /// return the bare string when that is all you want.
    /// </summary>
    public async Task<LexicalContent> GetContentAsync(LexicalContentFormat format) =>
        new(format, format switch
        {
            LexicalContentFormat.Text => await GetTextAsync(),
            LexicalContentFormat.Html => await GetHtmlAsync(),
            LexicalContentFormat.Markdown => await GetMarkdownAsync(),
            LexicalContentFormat.EditorStateJson => await GetEditorStateJsonAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        });

    /// <summary>
    /// Inserts a <paramref name="rows"/>×<paramref name="columns"/> table at the
    /// current selection. When <paramref name="includeHeaderRow"/> is <c>true</c>
    /// (the default) the first row is rendered as a header row. Requires a
    /// <see cref="LexicalTables"/> extension nested in this editor (otherwise this is a
    /// no-op, since the table chunk is not loaded).
    /// </summary>
    public async Task InsertTableAsync(int rows = 3, int columns = 3, bool includeHeaderRow = true)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("insertTable", Id, rows, columns, includeHeaderRow);
    }

    /// <summary>Converts the blocks at the current selection into a bulleted list.</summary>
    public async Task InsertUnorderedListAsync()
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("insertUnorderedList", Id);
    }

    /// <summary>Converts the blocks at the current selection into a numbered list.</summary>
    public async Task InsertOrderedListAsync()
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("insertOrderedList", Id);
    }

    /// <summary>Converts the list items at the current selection back into paragraphs.</summary>
    public async Task RemoveListAsync()
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("removeList", Id);
    }

    /// <summary>
    /// Wraps the current selection in a link to <paramref name="url"/>, or updates
    /// an existing link. Passing a null/empty URL unwraps the link
    /// (see <see cref="RemoveLinkAsync"/>).
    /// </summary>
    public async Task SetLinkAsync(string? url)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("toggleLink", Id, url);
    }

    /// <summary>Removes the link at the current selection.</summary>
    public async Task RemoveLinkAsync()
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("toggleLink", Id, (string?)null);
    }

    /// <summary>Toggles an inline text <paramref name="format"/> over the current selection.</summary>
    public async Task FormatTextAsync(LexicalTextFormat format)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("formatText", Id, format.ToJsToken());
    }

    /// <summary>
    /// Sets the block type at the current selection. List types route through the
    /// list commands; all other types go through the JS <c>setBlockType</c>.
    /// </summary>
    public async Task SetBlockTypeAsync(LexicalBlockType blockType)
    {
        if (_module is null)
        {
            return;
        }
        switch (blockType)
        {
            case LexicalBlockType.BulletList:
                await _module.InvokeVoidAsync("insertUnorderedList", Id);
                break;
            case LexicalBlockType.NumberList:
                await _module.InvokeVoidAsync("insertOrderedList", Id);
                break;
            default:
                await _module.InvokeVoidAsync("setBlockType", Id, blockType.ToJsToken());
                break;
        }
    }

    /// <summary>Sets the block alignment at the current selection.</summary>
    public async Task SetAlignmentAsync(LexicalAlignment alignment)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("formatAlignment", Id, alignment.ToJsToken());
    }

    /// <summary>Undoes the last change (no-op when history is disabled or empty).</summary>
    public async Task UndoAsync()
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("undo", Id);
    }

    /// <summary>Redoes the last undone change.</summary>
    public async Task RedoAsync()
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("redo", Id);
    }

    /// <summary>Clears any active inline text formats over the current selection.</summary>
    public async Task ClearFormattingAsync()
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("clearFormatting", Id);
    }

    /// <summary>Returns keyboard focus to the editor (e.g. after a toolbar button click).</summary>
    public async Task FocusAsync()
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("focus", Id);
    }

    /// <summary>
    /// Returns a snapshot of every mention reference node currently in the document —
    /// each with its node key, config id, initiator, app-owned value, display text, and
    /// url. Use it to decide which references need re-resolving (e.g. a renamed
    /// contact), then apply updates with <see cref="RefreshMentionAsync"/> or
    /// <see cref="RefreshMentionsByValueAsync"/>. The editor never resolves display text
    /// on its own — on load it renders the last-known text immediately.
    /// </summary>
    public async Task<IReadOnlyList<LexicalMentionRef>> GetMentionsAsync()
    {
        if (_module is null)
        {
            return [];
        }
        var json = await _module.InvokeAsync<string>("getMentions", Id);
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }
        return JsonSerializer.Deserialize(
            json, LexicalJsonSerializerContext.Default.LexicalMentionRefArray) ?? [];
    }

    /// <summary>
    /// Updates one mention's display text (and optional link) by node key — the key
    /// comes from <see cref="GetMentionsAsync"/>. The update is silent: it adds no undo
    /// step and does not raise <see cref="OnContentChanged"/>, so refreshing stale names
    /// on load never marks the document dirty. No-op if the node no longer exists.
    /// </summary>
    public async Task RefreshMentionAsync(string nodeKey, string text, string? url = null)
    {
        if (_module is null)
        {
            return;
        }
        await _module.InvokeVoidAsync("refreshMention", Id, nodeKey, text, url);
    }

    /// <summary>
    /// Updates every mention matching (<paramref name="configId"/>,
    /// <paramref name="value"/>) — so a contact renamed once updates all its
    /// occurrences — and returns how many nodes changed. Same silent semantics as
    /// <see cref="RefreshMentionAsync"/>.
    /// </summary>
    public async Task<int> RefreshMentionsByValueAsync(
        string configId, string value, string text, string? url = null)
    {
        if (_module is null)
        {
            return 0;
        }
        return await _module.InvokeAsync<int>(
            "refreshMentionsByValue", Id, configId, value, text, url);
    }

    /// <summary>
    /// JS interop entry point — invoked from the JS glue when the selection changes;
    /// not part of the public API. It must be <c>public</c> for <c>[JSInvokable]</c>
    /// dispatch, so it is hidden from IntelliSense. Do not call it directly.
    /// </summary>
    [JSInvokable]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task OnSelectionChangedInternal(string json)
    {
        var dto = JsonSerializer.Deserialize(
            json, LexicalJsonSerializerContext.Default.LexicalSelectionStateDto);
        if (dto is null)
        {
            return;
        }

        SelectionState = LexicalSelectionState.FromDto(dto);
        SelectionStateChanged?.Invoke(SelectionState);
        StateHasChanged();

        if (OnSelectionChanged.HasDelegate)
        {
            await OnSelectionChanged.InvokeAsync(SelectionState);
        }
    }

    /// <summary>
    /// JS interop entry point — invoked from the JS glue when the content changes;
    /// not part of the public API. It must be <c>public</c> for <c>[JSInvokable]</c>
    /// dispatch, so it is hidden from IntelliSense. Do not call it directly.
    /// </summary>
    [JSInvokable]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task OnContentChangedInternal(string payload)
    {
        // Placeholder visibility is handled entirely in JS/CSS (data-lexical-empty),
        // so this channel exists only to forward the document to a subscribed consumer
        // — and JS only invokes it when OnContentChanged is wired. The payload arrives
        // already serialized in the declared format (one crossing, nothing wasted); the
        // format itself never travels, since this side is what chose it.
        var format = _contentPayload.ToContentFormat();
        var content = new LexicalContent(format ?? LexicalContentFormat.Text, payload);

        // SignalOnly carries no document, so there is nothing to cache; leaving
        // LastContent null keeps "null means nothing was pushed" honest.
        if (format is not null)
        {
            LastContent = content;
        }

        if (OnContentChanged.HasDelegate)
        {
            ContentChanged?.Invoke(content);
            await OnContentChanged.InvokeAsync(content);
        }
    }

    /// <summary>
    /// JS interop entry point — invoked from an extension's JS half to call its own C#
    /// (<c>LexicalExtension.OnInvokeAsync</c>), routed by extension id; not part of the
    /// public API. Must be <c>public</c> for <c>[JSInvokable]</c> dispatch, so it is
    /// hidden from IntelliSense. Returns the handler's result as JSON, or <c>null</c>.
    /// <para>
    /// This is the <b>only</b> JS→.NET channel extensions have, built-in ones included:
    /// the mentions runtime's provider queries and selection notifications arrive here
    /// too, rather than through callbacks of their own.
    /// </para>
    /// </summary>
    [JSInvokable]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task<string?> InvokeExtensionAsync(string extensionId, string method, string argsJson)
    {
        var extension = AllExtensions().FirstOrDefault(e => e.ExtensionId == extensionId);
        if (extension is null)
        {
            return null;
        }
        return await extension.DispatchInvokeAsync(method, argsJson);
    }

    /// <summary>Tears down the JS editor instance and releases interop references.</summary>
    public async ValueTask DisposeAsync()
    {
        IsReady = false;
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("dispose", Id);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit already torn down (Server host); nothing to clean up.
        }
        catch (JSException)
        {
            // Module teardown raced with disposal; safe to ignore.
        }

        _dotNetRef?.Dispose();
    }
}
