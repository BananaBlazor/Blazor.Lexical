# Blazor.Lexical

A Blazor Razor Class Library that wraps [Lexical](https://lexical.dev), Meta's
extensible rich-text editor, behind a single `<LexicalEditor>` component.

[![NuGet](https://img.shields.io/nuget/v/Blazor.Lexical.svg)](https://www.nuget.org/packages/Blazor.Lexical)

**[Live demo and docs →](https://bananablazor.github.io/Blazor.Lexical/)**

## Features

- **Rich text** — bold/italic/underline/strikethrough/code, headings, quotes,
  alignment, undo/redo history, bulleted and numbered lists, links.
- **Tables** — an opt-in extension; declaring `<LexicalTables />` is what pulls in
  the `@lexical/table` chunk and lights up the toolbar picker, the `/table` slash
  item, and the in-cell action menu.
- **Mentions and hashtags** — `<LexicalMention />` configs. An `@` config with a
  `Provider` queries C# (debounced) for suggestions; a `Freeform` `#` config
  highlights tokens live with zero JS→.NET calls.
- **Toolbar and overlays** — a fixed `<LexicalToolbar />`, a floating selection
  toolbar, a link editor, a `/`-triggered slash menu, and a hover drag handle.
  All are optional child markup, and none add interop: they position
  Blazor-authored markup in JS and dispatch the same commands the toolbar does.
- **Table of contents** — `<LexicalToc />` gives every heading a slug `id` (so
  `#fragment` links work) and renders a live outline, either into an element you
  name or as a C# model you render with `<LexicalTocList />`. The document itself
  is never modified.
- **Marks** — `<LexicalMarks />` attaches *your own* ids to spans of text: the
  building block for comments, annotations, suggestions and search highlighting.
  Marks overlap, and their ids survive serialization.
- **Document statistics** — `<LexicalStats />` for live word/character/paragraph
  counts and a reading-time estimate, written straight into the page or pushed to C#.
- **Block gutters** — `<LexicalBlockGutter>` is a per-block hover rail you compose like
  a toolbar: drop in the built-in `<LexicalDragHandle />` (drag to reorder) and
  `<LexicalAddBlockButton />` ("+"), your own `<LexicalGutterButton>`, or all three.
  Several rails per editor, on either margin.
- **Four content formats** — read and write plain text, HTML, Markdown, or
  canonical editor-state JSON.
- **An extension SDK** — contribute custom Lexical nodes, buttons, and callbacks
  from your own RCL, against the public surface only.

## Install

```
dotnet add package Blazor.Lexical
```

Targets **net10.0**.

Register the services in `Program.cs`:

```csharp
using Blazor.Lexical;

builder.Services.AddLexicalBlazor();
```

Reference the stylesheet from your host page (`App.razor` / `index.html`):

```html
<link rel="stylesheet" href="_content/Blazor.Lexical/blazor-lexical.css" />
```

No `<script>` tag is needed — the component `import()`s the ESM module itself.

## Usage

The minimal editor needs no `@ref`: `InitialContent` loads the document,
`OnContentChanged` reports changes.

```razor
@using Blazor.Lexical

<LexicalEditor Placeholder="Start typing…"
               InitialContent="@LexicalContent.FromHtml(_doc.Body)"
               ContentPayload="LexicalContentPayload.EditorStateJson"
               OnContentChanged="OnChanged" />

@code {
    private async Task OnChanged(LexicalContent content)
    {
        // content.Format is EditorStateJson; content.Text is ready to persist.
        await _store.SaveAsync(content.Text);
    }
}
```

`InitialContent` is applied while the editor is created, so the document is on
screen in the first frame and undo can't erase it. `LexicalContent.FromText` /
`FromHtml` / `FromMarkdown` / `FromEditorStateJson` all build one, so content
loaded from a database can pick its format at runtime.

Everything else is opt-in child markup:

```razor
<LexicalEditor Theme="LexicalTheme.Default">
    <LexicalToolbar />
    <LexicalFloatingToolbar />
    <LexicalLinkEditor />
    <LexicalBlockGutter Position="LexicalGutterPosition.LeftInside">
        <LexicalAddBlockButton />
        <LexicalDragHandle />
    </LexicalBlockGutter>
    <LexicalBlockGutter>
        <LexicalGutterButton OnClick="CommentOnBlock">💬</LexicalGutterButton>
    </LexicalBlockGutter>
    <LexicalTables />
    <LexicalTableEditor />
    <LexicalToc TargetSelector="#outline" />
    <LexicalMarks @ref="_marks" />
    <LexicalStats TargetSelector="#word-count" />
    <LexicalMention Initiator="@@" Provider="SearchPeople" OnSelected="OnPicked" />
    <LexicalMention Initiator="#" Freeform="true" />
    <LexicalSlashMenu>
        <LexicalSlashItem Command="block:h1" Label="Heading 1" Keywords="heading title" />
        <LexicalSlashItem Label="Insert today's date" OnSelect="InsertDate" />
    </LexicalSlashMenu>
</LexicalEditor>
```

## Documentation

- [Getting started](https://bananablazor.github.io/Blazor.Lexical/docs/getting-started.html)
- [Toolbar and overlays](https://bananablazor.github.io/Blazor.Lexical/docs/toolbar-and-overlays.html)
- [Writing an extension](https://bananablazor.github.io/Blazor.Lexical/docs/writing-an-extension.html)
  — create custom nodes, buttons, and callbacks from your own RCL.
  `Samples/Extensions.Badge` is the worked reference example.

## Contributing

For contribution guidelines, see [CONTRIBUTING.md](CONTRIBUTING.md).
