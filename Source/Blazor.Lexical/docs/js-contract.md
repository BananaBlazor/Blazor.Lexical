# JS contract & glue

Reference for `js/src/*.ts` and the DOM contracts between Blazor and JS. Read this before
changing the bundle, the command tokens, or any overlay.

## Module layout

- `index.ts` — bootstrap, `create()`, toolbar dispatch, exports.
- `overlays.ts` — floating toolbar, slash menu, block gutters (the per-block hover rails,
  including the drag grip and "+"), link editor.
- `mentions.ts` — mention nodes, typeahead, freeform highlight. Static in core (~4 kb
  gzipped), activated when configs exist.
- `toc.ts` — heading outline: slugs, DOM-only anchor stamping, optional `<ol>` renderer,
  scrollspy. Static in core (needs only `lexical` + `@lexical/rich-text`).
- `marks.ts` — the `@lexical/mark` MarkNode plus wrap/remove/query/decorate. Static in
  core (+1.5 kb gzipped measured, package included) — the mentions precedent, not the
  table one.
- `stats.ts` — word/character/paragraph counts and reading time. Static in core.
- `table.ts` — table node runtime, in-cell action menu, insert grid picker. Lazy chunk.
- `markdown.ts` — lazily `import()`ed `@lexical/markdown`, keeping it and
  `@lexical/code-core` out of core.
- `hr.ts` — horizontal rule. Static in core (~1 kb). Ports upstream's `HorizontalRuleNode`
  and adds a non-signal selection painter; see `architecture.md` for why it is a port.
- `tabindent.ts` — Tab/Shift+Tab block indentation. Static in core, behaviour only (no
  node, no theme). Also a port of upstream's `registerTabIndentation`.
- `extension.ts` — the consumer extension contract. **Types only**, imported with
  `import type` so it never lands in the bundle.
- `tags.ts` — shared update tags, its own module so producers and consumers of a tag never
  import each other (a cycle that would drag the core entry into any lazy chunk).

`mentions.ts`, `table.ts`, `toc.ts`, `marks.ts` and `stats.ts` all **default-export a
`LexicalExtensionFactory`**, so `create()` loads them through the same contract as a
consumer extension — resolved by the closed `builtIn` switch
(`'table' | 'mentions' | 'toc' | 'marks' | 'stats'`).

## Bundling

esbuild bundles `index.ts` with `--splitting` into `wwwroot/blazor-lexical.mjs` (the entry
Blazor imports), shared-core chunks, and `table`/`markdown` chunks — all named
`blazor-lexical*.mjs`. Because chunk hashes change with sources, the `.csproj` keeps them
out of the evaluation-time content glob and `RegisterLexicalBundle` adds whatever exists
post-build as static web assets; `build-js.ps1` deletes stale `blazor-lexical*.mjs` before
each run.

`build-js.ps1` also copies `extension.ts` verbatim to
`wwwroot/blazor-lexical-extension.d.ts` (a shipped static web asset) so extension authors
get the contract typed; the `.csproj` registers it alongside the generated `.mjs` files.

Only `index.ts`'s exports are the Blazor↔JS contract, asserted by
`../../Tests/Integration/ModuleContractTests.cs` — update it when adding or removing
exports (the `table.ts` chunk's exports are internal, reached only via the dynamic import).
Type-check before shipping bundle changes: `npm run typecheck --prefix js`.

## The `data-lexical-command` contract

Buttons declare a token; JS (`runCommandToken` / `updateToolbarDom` / `isCommandActive` in
`index.ts`) interprets it. Keep C# and JS in lockstep.

- `format:{bold|italic|underline|strikethrough|code|subscript|superscript|lowercase|uppercase}`
- `block:{paragraph|h1..h6|quote}` and `block:select` (the `<select>`; option values are
  bare selection tokens, so it also carries `bullet`/`number` inline — the change handler
  routes those through `list:`)
- `list:{bullet|number|remove}` (bullet/number toggle off when already active)
- `history:{undo|redo}` (JS sets `data-lexical-disabled` from can-undo/redo)
- `align:{left|center|right|justify}` (empty clears)
- `link:{toggle|remove}` — `toggle` unwraps when the selection sits in a link, otherwise
  inserts a placeholder `https://` link and dispatches `OPEN_LINK_EDITOR_COMMAND` so the
  floating editor opens on it. There is no URL-input token; the URL is typed in the
  `[data-lexical-link-editor]` popup.
- `table:insert[:RxC]` — default 3×3 with header row; `table:3x4` sets size. Used by the
  slash menu; the toolbar grid picker inserts directly, not via this token. No active state.
- `hr:insert` — dispatches `INSERT_HORIZONTAL_RULE_COMMAND`. Needs no module handle (unlike
  `table:`): without `<LexicalHorizontalRule>` nothing has registered a handler, so the
  token is a silent no-op. No active state.
- `clear-formatting`

Style via `[data-lexical-active]` / `[data-lexical-disabled]` in
`wwwroot/blazor-lexical.css` — never a Blazor-set `.is-active` class.

## Overlays

Blazor authors the markup as `ChildContent`; `overlays.ts` only positions and drives it.
Each overlay **activates by marker presence** — `create()` scans the root once and wires
behavior only if the marker exists. Overlay buttons are ordinary `data-lexical-command`
markup, so the overlays add **no interop of their own** — with the single, still-opt-in
exception of the block gutter's hover push (below).

- `data-lexical-floating-toolbar` — shown above a non-empty selection.
- `data-lexical-slash-menu` — typeahead opened by `/`; items carry
  `data-lexical-slash-item` + `data-lexical-slash-keywords`; JS sets
  `data-lexical-slash-active` and deletes the `/query` text before the command runs. An
  item without a `data-lexical-command` (a C# `OnSelect`) is the only slash interop.
- `data-lexical-block-gutter` — a per-block hover **rail**. `registerBlockGutters` scans
  with `querySelectorAll` and drives **all** of them from one registration: one
  `trackHoveredBlock` hit-test, one `data-lexical-drop-line`, one delegated drag/click
  pair on the root. Each rail floats beside the hovered block on the side named by
  `data-lexical-block-gutter-position` (`left-inside` | `left-outside` | `right-inside` |
  `right-outside`, default `right-inside`). `*-inside` anchors to the text column — the
  content box inset by its own padding — and is clamped to the card; `*-outside` anchors
  to the card edge and hangs into the page. Rails sharing a position stack outward in DOM
  order, measured from `getBoundingClientRect().width` (which is why they hide with
  `visibility`, not `display`). Each carries
  `data-lexical-visible` plus `data-lexical-block-key` / `data-lexical-block-index` /
  `data-lexical-block-type`. Rails stay visible for a short **grace window** after the pointer leaves the editor
  (cancelled when it arrives on a rail), which is what makes their buttons reachable at
  all: the gutter gap — and the whole page, for an `outside` rail — is not part of the
  root, so travelling to a rail fires the root's `mouseleave`. Hiding on that event
  directly makes a rail vanish mid-journey.

  Rail **items** are just markup, delegated from the root so they work in whichever rail
  they sit in: `data-lexical-drag-grip` (`draggable`, `<LexicalDragHandle>`) reorders via
  node moves, `data-lexical-add-block` (`<LexicalAddBlockButton>`) inserts a paragraph +
  `/`. Anything else is the host's own `@onclick` markup (invariant 4) — typically
  `<LexicalGutterButton>`, which shares the `blazor-lexical__gutter-item` shape.
- `data-lexical-link-editor` — shows `[data-lexical-link-view]` (preview anchor +
  `[data-lexical-link-edit]` + a `link:remove` button) while the caret is in a link, and
  `[data-lexical-link-edit-form]` (`[data-lexical-link-input]` + cancel/confirm) when
  editing. JS swaps rows via `[hidden]` and updates the URL in place (empty unwraps).
  Confirm/cancel/edit are handled locally, *not* as `data-lexical-command`; only `remove`
  rides the delegated dispatch.
- `data-lexical-table-menu` — in-cell action menu (`registerTableActionMenu` in
  `table.ts`). Floats `[data-lexical-table-trigger]` in the caret cell; the trigger toggles
  `[data-lexical-table-dropdown]`, whose buttons carry
  `data-lexical-table-action="{row-above|row-below|col-left|col-right|row-delete|col-delete|table-delete|row-header|col-header}"`
  and are handled locally against the current cell selection (kept alive by the popup's
  `mousedown` preventDefault).

`<LexicalTableButton>` carries `data-lexical-table-picker` (`registerTablePicker`). It
lives in the toolbar, not the editable surface, but is still scanned from the root at
`create()`; its popover is CSS-anchored and grid cells carry
`data-lexical-table-grid-cell` + `data-row`/`data-col`.

Overlays must be present at `create()` time; conditionally-added overlays aren't rescanned
in v1.

## Push channels

`notify` carries one flag per JS→.NET channel, each derived from a `.HasDelegate` on the
C# side and mirrored in `setNotifications` so it can be armed after `create()`:

| Flag | `[JSInvokable]` | Armed by |
|---|---|---|
| `content` | `OnContentChangedInternal` | `LexicalEditor.OnContentChanged` |
| `selection` | `OnSelectionChangedInternal` | `LexicalEditor.OnSelectionChanged` |
| `blockHover` | `OnBlockHoveredInternal` | `LexicalBlockGutter.OnBlockHovered`, or any `LexicalGutterButton` in a rail |

`blockHover` is the only overlay-owned channel. It is deduped by node key — one crossing
per *block*, not per mousemove — and carries the whole `LexicalBlockRef`, so a subscriber
never needs a follow-up call. There is **one** crossing regardless of how many rails the
editor has: the editor fans the payload out to every `LexicalBlockGutter`, since they all
sit beside the same block. Extensions never ride these: they push over their own
id-routed extension channel (see [extensions.md](extensions.md)).

## Update tags

`blazor-lexical-silent` (`SILENT_UPDATE_TAG`) lives in `tags.ts` alongside
`HISTORY_MERGE_TAG`. Extensions reach it as `setup.silentUpdateTag` — never a literal.
Reserve it for edits the user did not make: a silently-tagged *user* edit would never reach
the host, because the content push in `create()`'s update listener skips tagged updates.

## Initial content ordering

Initial content is applied inside `create()` — after the core plugins, *before*
`registerHistory` and the update listener, with `{ discrete: true }`. That ordering is what
makes a preloaded document paint in the first frame, sit outside the undo stack, and raise
no content push; moving it quietly breaks all three.
