# Getting Started

## Install

```
dotnet add package Blazor.Lexical
```

## Register the services

In `Program.cs`:

```csharp
using Blazor.Lexical;

builder.Services.AddLexicalBlazor();
```

## Reference the stylesheet

From your host page (`App.razor` for Server, `wwwroot/index.html` for WebAssembly):

```html
<link rel="stylesheet" href="_content/Blazor.Lexical/blazor-lexical.css" />
```

## Use the component

```razor
@using Blazor.Lexical

<LexicalEditor @ref="_editor"
               Placeholder="Start typing…"
               OnContentChanged="OnChanged" />

@code {
    private LexicalEditor _editor = default!;

    private void OnChanged(LexicalContent content) => Console.WriteLine(content.Text);

    private async Task SaveAsync()
    {
        var json = await _editor.GetEditorStateJsonAsync();
        // persist json…
    }
}
```

## Loading initial content

Pass the document — and the format it is written in — as `InitialContent`. It is
applied while the editor is created, so it is on screen in the first frame (no flash of
an empty editor) and it is the undo baseline rather than an undoable step:

```razor
<LexicalEditor InitialContent="@LexicalContent.FromHtml(_doc.Body)"
               OnContentChanged="OnChanged" />
```

`LexicalContent.FromText` / `FromHtml` / `FromMarkdown` / `FromEditorStateJson` all
build one, so a document loaded from a database or an API can pick its format at
runtime. `InitialContent` is read once, at creation — call `SetHtmlAsync` (or a sibling
`Set*Async`) to replace the content later.

With `InitialContent` for the document, `OnContentChanged` for live changes, and the
`LastContentText` property for cheap reads, most editors need no `@ref` at all.

### Why there is no `@bind-Value`

The editor is a *document*, not a value input: its source of truth is Lexical's editor
state, and any C# string is a projection of it in one of four formats. Two-way binding
would have to pick one format, re-parse the whole document on every external set —
losing the selection, IME composition, and undo history — and stream the document
across the JS boundary continuously. So the two halves are separate, and each is
pay-for-play: `InitialContent` in, the opt-in `OnContentChanged` out, and the
`Get*Async`/`Set*Async` pairs when you want the whole document in a specific format.

## Watching for changes

`OnContentChanged` fires (debounced) with a `LexicalContent` — the document **in the
format you declare**, so a handler that wants HTML or state-JSON gets it directly
instead of receiving plain text and then having to ask again:

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

The serialization happens in JS inside the debounce, so this remains **one** interop
crossing per change no matter which format you pick. And if you only need a *dirty*
signal (autosave, enabling a Save button), set
`ContentPayload="LexicalContentPayload.SignalOnly"`: the callback still fires on every
debounced change, but carries no document at all.

`LastContent` holds the most recent push, if you'd rather read it than store it
yourself.

## Content methods

`GetTextAsync` / `SetTextAsync`, `GetHtmlAsync` / `SetHtmlAsync`,
`GetMarkdownAsync` / `SetMarkdownAsync`, `GetEditorStateJsonAsync` /
`SetEditorStateJsonAsync`, plus `InsertUnorderedListAsync`, `InsertOrderedListAsync`,
`RemoveListAsync`, `SetLinkAsync`, and `RemoveLinkAsync`.

Persist `GetEditorStateJsonAsync()` — it is the highest-fidelity format.

Every `Set*Async` takes an optional `silent: true` for content the app supplied rather
than content the user typed — a remote revision, a server refresh, an autosave
reconcile. A silent apply adds no undo step and raises no `OnContentChanged`, so the
host is not echoed back what it just applied and the user's next undo does not wobble
back to the pre-apply document:

```csharp
await editor.SetEditorStateJsonAsync(revisionFromServer, silent: true);
```

When the format is a value rather than something known at the call site, use the
generic pair instead: `GetContentAsync(LexicalContentFormat)` returns a
`LexicalContent`, and `SetContentAsync(LexicalContent)` takes one (and the same `silent` option) — the same type
`InitialContent` accepts, so a document round-trips without a `switch`.

See the [API Reference](xref:Blazor.Lexical) for the full list of parameters and
methods on `LexicalEditor` and the related option types.
