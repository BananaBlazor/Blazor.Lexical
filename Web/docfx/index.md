# Blazor.Lexical

A Blazor Razor Class Library that wraps the [Lexical](https://lexical.dev) rich-text
editor behind a single `<LexicalEditor>` component. It ships the JS glue as a
self-contained RCL asset, so there is no separate npm step for consumers.

Works with both Blazor Server (InteractiveServer) and Blazor WebAssembly.

> [!TIP]
> **<a href="../">▶ Try the live demo</a>** — the editor running as a WebAssembly app.

## Features

- Rich text editing with history (undo/redo)
- Bulleted / numbered lists and links
- Tables (lazily loaded — only downloaded when enabled)
- Configurable @-mentions (typeahead + freeform highlighting)
- In-editor overlays: floating toolbar, slash (`/`) menu, drag handle, link editor
- Get/set content as plain text, HTML, Markdown, or canonical editor-state JSON
- Debounced `OnContentChanged` callback
- Read-only mode, placeholder text, and per-instance or app-wide themes

## Where to go next

- **[Getting Started](getting-started.md)** — install, register, and drop in an editor.
- **[Toolbar & Overlays](toolbar-and-overlays.md)** — the opt-in interop model and the
  `data-lexical-command` contract behind the built-in controls.
- **[API Reference](xref:Blazor.Lexical)** — every public type and member, generated
  from the library's XML documentation.

## License

MIT
