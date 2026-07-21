# Built-in features: tables, mentions, TOC, marks, stats, horizontal rule, tab indent, block gutter

Read this before changing anything under `Extensions/` or the matching `js/src/*.ts`
(`table.ts`, `mentions.ts`, `toc.ts`, `marks.ts`, `stats.ts`, `hr.ts`, `tabindent.ts`, and
`registerBlockGutters` in `overlays.ts`). The DOM markers and command tokens these features use are in
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

## Table of contents

`js/src/toc.ts` + `Extensions/Toc/`. Register-only (no nodes), static in core — it needs
only `lexical` + `@lexical/rich-text`, both already bundled. Driven by its **own**
debounced update listener (~150 ms); it never rides the core content channel.

**The editor state is never mutated.** Anchors are stamped with `setAttribute('id', slug)`
on the element `editor.getElementByKey` returns, *outside* `editor.update()` — no dirty
node, no undo step, no serialization change. A document authored with the extension
serializes byte-identically to one authored without it, which is what `TocTests` asserts.
The price is that slugs are content-derived: renaming a heading invalidates saved
`#fragment` links, and ids are page-global (hence `AnchorPrefix`). Both are documented on
`LexicalTocEntry.AnchorId`.

A **signature gate** (`level|slug|text` joined over every heading) short-circuits the tick
when nothing outline-relevant changed, so typing inside a paragraph costs one string
compare — no DOM writes, no render, no push.

Two surfaces, independent and both opt-in:

- `TargetSelector` ⇒ JS renders a nested `<ol class="blazor-lexical__toc">` into a
  host-supplied element (usually *outside* the editor root — that is the point) and owns
  click-to-scroll. Zero interop.
- `OnTocChanged` ⇒ the tree is additionally pushed to .NET (invariant 1: `HasInvokeHandler`
  narrows to `.HasDelegate`). `LexicalTocList` renders that model in Blazor, emitting the
  **same markup** the JS renderer does so one stylesheet serves both. It takes data, not an
  editor, so it can sit anywhere on the page without the sibling-`Editor=` hazard of
  invariant 6.

## Marks

`js/src/marks.ts` + `Extensions/Marks/`. Contributes `@lexical/mark`'s `MarkNode`; static
in core (measured **+1.5 kb gzipped** including the package — the mentions precedent, not
the table one). Grow `LexicalTheme` (`Mark`, `MarkOverlap`) alongside it.

**Mark ids are app-minted and opaque.** The library never generates or interprets one. An
app storing a quote beside the mark takes it from `LexicalSelectionState.Text` (pushed at
selection time), not from a read at click time — the click may already have collapsed the
selection.

**`registerNestedElementResolver` is load-bearing.** `$wrapSelectionInMarkNode` nests a new
MarkNode *inside* an existing one when wraps overlap; the resolver flattens that into a
single node carrying both ids. Without it, overlapping marks render as nested `<mark>`
elements each holding one id and `markOverlap` is unreachable — which is exactly what the
`OverlappingMarks_MergeTheirIds` test caught. Do not drop it.

The click channel resolves ids from the **clicked element** via
`editor.read(() => $getNearestNodeFromDOMNode(...))` — not from the selection, which has
not moved to the click point yet at `CLICK_COMMAND` time — and `editor.read` (not
`getEditorState().read`) because `$getNearestNodeFromDOMNode` needs the active editor set.
The handler returns `false`, so it observes the click and never swallows it.

`SetActiveMarkAsync` is decoration only (`data-lexical-mark-active` on the DOM), re-applied
from the update listener; it changes neither the document nor the undo stack.
`RemoveMarkAsync(silent: true)` runs under `[silentUpdateTag, 'history-merge']` for
app-driven cleanup.

## Document statistics

`js/src/stats.ts` + `Extensions/Stats/`. The smallest of the built-ins: read-only, no
nodes, its own debounced tick, and skipped entirely when the computed tuple is unchanged.
Same dual surface as the TOC — `TargetSelector` + `Template` writes a formatted line
client-side (zero interop), `OnStatsChanged` pushes `LexicalDocumentStats`.

## Horizontal rule

`js/src/hr.ts` + `Extensions/HorizontalRule/`. A `DecoratorNode` rendering `<hr>`, ported
from `@lexical/extension` so the `"horizontalrule"` type and the `hr` / `hrSelected` theme
keys match upstream and documents round-trip with other Lexical apps. Inserted by the
`hr:insert` token (toolbar button and slash item, both gated on
`HasExtension<LexicalHorizontalRule>()`) or `InsertAsync()` from C#. Clicking a rule puts
it in a node selection — which is what makes Delete/Backspace meaningful and what
`hrSelected` styles — painted by an update listener rather than upstream's signals.
Zero interop. Opt-in, so an `<hr>` in HTML loaded into an editor without the extension is
dropped, the same trade tables make.

## Tab indentation

`js/src/tabindent.ts` + `Extensions/TabIndent/`. A port of upstream's
`registerTabIndentation`: Tab/Shift+Tab indent and outdent blocks, `MaxIndent` caps the
depth, and a caret mid-text still inserts a tab. No node, no theme, no interop —
behaviour only, which is the shape the contract has to accommodate.

**Opt-in for accessibility, not taste.** Binding Tab inside the editor takes it away from
keyboard navigation and traps focus; Lexical's own docs discourage the behaviour for that
reason. It is a component rather than an editor flag so that enabling it is a deliberate
act, and the C# doc comment says so.

## Block gutters (the per-block hover rails)

`registerBlockGutters` in `js/src/overlays.ts` + `Extensions/Overlays/`. Plain overlays,
**not** `LexicalExtension`s.

**`LexicalBlockGutter` is a container, the way `LexicalToolbar` is.** That is the whole
shape of this feature: a rail holds a list of items, each addable on its own. There is no
monolithic drag-handle component — the playground's left rail is *composed*:

```razor
<LexicalBlockGutter Position="LexicalGutterPosition.LeftInside">
    <LexicalAddBlockButton />
    <LexicalDragHandle />   @* the grip, an item — not a rail *@
</LexicalBlockGutter>
<LexicalBlockGutter>
    <LexicalGutterButton OnClick="Comment">💬</LexicalGutterButton>
</LexicalBlockGutter>
```

`LexicalDragHandle` and `LexicalAddBlockButton` are **pure markup** (`data-lexical-drag-grip`,
`data-lexical-add-block`); JS delegates both from the editor root, which is what lets them
work in whichever rail they are placed. `LexicalGutterButton` is the C# escape hatch with
the built-in styling, and any plain `<button @onclick>` works too.

**One registration drives every rail.** `create()` uses `querySelectorAll`, and
`registerBlockGutters` owns a single `trackHoveredBlock` hit-test (per-rail tracking would
repeat the same `editor.read` N times per mousemove), a single drop-line, and one delegated
drag/click pair. Rails sharing a position stack outward from their anchor edge using their
measured width — which only works because a hidden rail uses `visibility`, not `display`.
Do not reintroduce per-rail registration or CSS side offsets; the two would fight.

**Reachability is a timer, not geometry.** A rail is an absolutely positioned child of the
root, but the pixels between the text and the rail are not — so travelling to one fires the
root's `mouseleave`. Hiding on that immediately makes the rail vanish before the pointer
arrives and its buttons unclickable. `GUTTER_HIDE_GRACE_MS` defers the hide; arriving on a
rail cancels it. That is what lets `LexicalGutterPosition.*Outside` exist at all — do not
"simplify" it back to an immediate hide.

The .NET side mirrors that: `_blockGutters` is a **list**, the channel is armed if *any*
rail wants it, and one crossing fans out to all of them. An earlier single-field version
was silently wrong with two rails (JS drove the first, .NET pushed to the last).

The push stays opt-in: `notify.blockHover` is armed when `OnBlockHovered` has a delegate
**or** the rail holds a `LexicalGutterButton` — a per-block button that cannot tell which
block it is on would be useless, so placing one is itself the opt-in. Deduped by node key.
`LexicalBlockRef.NodeKey` is **ephemeral** (it does not survive a parse/serialize round
trip), so anything persisted should use `Index` or a mark id.
