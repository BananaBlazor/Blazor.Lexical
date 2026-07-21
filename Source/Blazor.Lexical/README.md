# Blazor.Lexical

A Blazor Razor Class Library that wraps the [Lexical](https://lexical.dev) rich-text
editor behind a single `<LexicalEditor>` component. It ships the JS glue as a
self-contained RCL asset, so there is no separate npm step for consumers.

Works with both Blazor Server (InteractiveServer) and Blazor WebAssembly.

## Features

- Rich text editing with history (undo/redo)
- Bulleted / numbered lists and links
- Get/set content as plain text, HTML, Markdown, or canonical editor-state JSON
- Preload a document with `InitialContent` — no `@ref`, no empty first paint
- Debounced `OnContentChanged` callback, in the format you ask for (or a dirty signal)
- Read-only mode, placeholder text, and per-instance or app-wide themes
- Tables, mentions/hashtags, a live table of contents, marks (app-owned highlight ids),
  document statistics, and a per-block hover gutter you can put your own buttons in —
  each an opt-in child component that costs nothing when you don't declare it

## Install

```
dotnet add package Blazor.Lexical
```

## Usage

Register the services in `Program.cs`:

```csharp
using Blazor.Lexical;

builder.Services.AddLexicalBlazor();
```

Reference the stylesheet from your host page (`App.razor` / `index.html`):

```html
<link rel="stylesheet" href="_content/Blazor.Lexical/blazor-lexical.css" />
```

Then use the component:

```razor
@using Blazor.Lexical

<LexicalEditor @ref="_editor"
               Placeholder="Start typing…" />

@code {
    private LexicalEditor _editor = default!;

    private async Task SaveAsync()
    {
        var json = await _editor.GetEditorStateJsonAsync();
        // persist json…
    }
}
```

### Loading initial content

Pass the document — and the format it is written in — as `InitialContent`. It is
applied while the editor is created, so it is on screen in the first frame and undo
can't erase it:

```razor
<LexicalEditor InitialContent="@LexicalContent.FromHtml(_doc.Body)"
               OnContentChanged="OnChanged" />
```

`LexicalContent.FromText` / `FromHtml` / `FromMarkdown` / `FromEditorStateJson` all
build one, so content loaded from a database or an API can choose its format at
runtime. It is read once, at creation; call `SetHtmlAsync` (or a sibling `Set*Async`)
to replace the content later.

With `InitialContent` in for content, `OnContentChanged` for live changes, and
`LastContentText` for cheap reads, a typical editor needs no `@ref` at all.

#### Why there is no `@bind-Value`

The editor is a document, not a value input: its source of truth is Lexical's editor
state, and a C# string is only a projection of it in one of four formats. Two-way
binding would have to pick one, re-parse the entire document on every external set
(losing selection, IME composition, and undo history), and stream the document across
the JS boundary continuously. Instead the two halves are separate and each is opt-in:
`InitialContent` in, `OnContentChanged` out, `Get*Async`/`Set*Async` on demand.

### Parameters

| Parameter | Type | Description |
| --- | --- | --- |
| `Placeholder` | `string?` | Text shown while the editor is empty. |
| `ReadOnly` | `bool` | Renders the editor read-only. |
| `CssClass` | `string?` | Extra CSS class(es) on the outer wrapper. |
| `Theme` | `object?` | Lexical `EditorThemeClasses` for this instance. |
| `EnableHistory` | `bool` | Enables undo/redo (default `true`). |
| `Id` | `string` | Stable element id; auto-generated when omitted. |
| `InitialContent` | `LexicalContent?` | Document to load as the editor is created. |
| `OnContentChanged` | `EventCallback<LexicalContent>` | Fires (debounced) with the document, in the format `ContentPayload` names. |
| `ContentPayload` | `LexicalContentPayload` | `Text` (default), `Html`, `Markdown`, `EditorStateJson`, or `SignalOnly`. |

### Watching for changes

The push channel carries the document **in the format you declare**, so a handler that
wants HTML doesn't get plain text and then have to ask again:

```razor
<LexicalEditor ContentPayload="LexicalContentPayload.EditorStateJson"
               OnContentChanged="OnChanged" />

@code {
    private async Task OnChanged(LexicalContent content)
    {
        // content.Format is EditorStateJson; content.Text is ready to persist.
        await _store.SaveAsync(content.Text);
    }
}
```

The document is serialized in JS inside the debounce, so this stays one interop
crossing per change. If you only need to know that *something* changed — an autosave
timer, an enabled Save button — use `LexicalContentPayload.SignalOnly` and nothing but
the signal crosses.

### Properties

`IsReady` (true once the underlying editor exists — every method no-ops before that)
and `LastContent` (the most recent pushed document, with no interop call).

### Content methods

`GetTextAsync` / `SetTextAsync`, `GetHtmlAsync` / `SetHtmlAsync`,
`GetMarkdownAsync` / `SetMarkdownAsync`, `GetEditorStateJsonAsync` /
`SetEditorStateJsonAsync`, plus `InsertUnorderedListAsync`,
`InsertOrderedListAsync`, `RemoveListAsync`, `SetLinkAsync`, and `RemoveLinkAsync`.

When the format is a value rather than something known at the call site, use
`GetContentAsync(LexicalContentFormat)` / `SetContentAsync(LexicalContent)` — the same
`LexicalContent` that `InitialContent` takes, so a document round-trips without a
`switch`.

Persist `GetEditorStateJsonAsync()` — it is the highest-fidelity format.

Every `Set*Async` takes an optional `silent: true` for content the app supplied rather
than content the user typed (a remote revision, a server refresh): the apply adds no undo
step and raises no `OnContentChanged`.

## License

MIT
