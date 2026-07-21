# Built-in features: tables & mentions

Read this before changing `Extensions/Table/`, `Extensions/Mentions/`, `js/src/table.ts`,
or `js/src/mentions.ts`. The DOM markers and command tokens these features use are in
[js-contract.md](js-contract.md); the contract they ride is in [extensions.md](extensions.md).

## Tables (lazy chunk + chrome gating)

Declared in markup as `<LexicalTables />` (public, `BuiltIn = "table"`). There is no
`EnableTables` parameter. `create()` is `async` and does `await import('./table')` — the
only reference is dynamic, so esbuild splits `@lexical/table` (~90 kb) out of core.
`table.ts`'s default export is the extension factory: `nodes` are the table nodes, and
`register(ctx)` runs `registerTable` (`registerTablePlugin` +
`registerTableSelectionObserver`) and wires the two table overlays by marker presence.
`table:`/`insertTable` are inert without the chunk.

Chrome asks `editor.HasExtension<LexicalTables>()` so it never renders as a dead control.
Two halves are load-bearing — don't collapse them:

- It answers from the set **frozen at `create()`**, not the live registry, so an extension
  added later can't light chrome up. *Before* create it falls back to the live registry,
  because chrome rendering a JS-scanned marker (the picker's `data-lexical-table-picker`)
  must be in the DOM by the time `create()` scans the root.
- **`internal event Action? ExtensionsChanged`** fires on each child registration and again
  when the set freezes. `LexicalToolbar`/`LexicalSlashMenu` subscribe in `OnInitialized`,
  self-invalidate via `InvokeAsync(StateHasChanged)`, unsubscribe in `Dispose`. This closes
  the ordering hazard where a `<LexicalToolbar/>` declared *above* `<LexicalTables/>`
  renders first — the event brings it back within the same render batch, so the marker
  exists before `create()`. The harness declares `<LexicalTables/>` last to hold this down.

The table runtime registers in the extension `register` pass — after core plugins and after
initial content — not beside `registerRichText`. `Tests/Integration/TableTests.cs` and the
initial-content tests hold that down; keep them green.

## Mentions

`js/src/mentions.ts` + `LexicalMention.razor`. Hosts nest one or more
`<LexicalMention Initiator="@" Color=… Provider=… Freeform=…/>` configs; each is
independent. Mirrors the slash menu but does **not** reuse `registerSlashMenu`:

- **Two custom nodes**, both `TextNode` subclasses in `createEditor`'s `nodes[]` (added
  only when configs exist): `MentionNode` — segmented/atomic, carrying an opaque
  **app-owned** `value` + optional `url` + `configId`/`trigger`/`color`, serialized
  (`exportJSON`/`exportDOM` → `data-lexical-mention`); and `MentionHighlightNode` — an
  `isTextEntity()` node for freeform highlighting via `registerLexicalTextEntity` (from
  `@lexical/text`). Grow `LexicalTheme` (`mention`, `mentionHighlight`) alongside them;
  per-config colour rides inline as `--blazor-lexical-mention-color`, the class stays typed.
- **The picker is JS-created**, not Blazor-authored — rows come from provider data, so
  `registerMentions` builds `[data-lexical-mention-menu]` under the root. The one overlay
  with no Blazor marker.

**One extension owns all configs** (`LexicalMentionExtension`), not one per
`<LexicalMention>`: a single combined entity matcher must serve all freeform initiators, or
`registerLexicalTextEntity`'s shared reverse-transform would unwrap another config's
initiator, so per-config extension instances would fight each other. Configs travel as the
extension's opaque `GetOptions()` payload (`MentionsExtensionOptionsDto`).

Interop is opt-in per config (invariant 1): no `Provider` ⇒ zero JS→.NET calls, since
freeform highlighting is a pure-JS text-entity transform. Both .NET calls ride the
**extension channel**, not callbacks of their own — `invokeDotNet('resolve', configId,
query)` (debounced) and `invokeDotNet('selected', …)` (gated on `OnSelected`), landing in
`LexicalMentionExtension.OnInvokeAsync` via `InvokeExtensionAsync`. `HasInvokeHandler` is
false when every config is freeform-only, so an all-freeform editor is told it may not call
in at all.

**Slow data degrades gracefully** — the reference implementation of the rule extensions
follow. The picker shows itself empty with `data-lexical-mention-loading` the moment a query
dispatches (default CSS paints a spinner — a language-free affordance, no string to
localize), so a slow source reads as "working" rather than a dead trigger. `QueryTimeout`
(default 5s) drops a query that never answers, closes the session, and warns to the console;
`requestSeq` makes a late answer inert; `Escape` closes even while loading.

**Refreshes are silent.** The editor never resolves display text on load (no per-node
interop storm — it renders the stored text immediately). Hosts re-resolve on their own
schedule via `GetMentionsAsync` + `RefreshMentionAsync`/`RefreshMentionsByValueAsync`,
applied with `SILENT_UPDATE_TAG` + `history-merge` so they add no undo step and the content
push skips them. So opening a document and refreshing stale names never marks it dirty.
