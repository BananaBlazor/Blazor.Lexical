# JS contract & glue

Reference for `js/src/*.ts` and the DOM contracts between Blazor and JS. Read this before
changing the bundle, the command tokens, or any overlay.

## Module layout

- `index.ts` — bootstrap, `create()`, toolbar dispatch, exports.
- `overlays.ts` — floating toolbar, slash menu, drag handle, link editor.
- `mentions.ts` — mention nodes, typeahead, freeform highlight. Static in core (~4 kb
  gzipped), activated when configs exist.
- `table.ts` — table node runtime, in-cell action menu, insert grid picker. Lazy chunk.
- `markdown.ts` — lazily `import()`ed `@lexical/markdown`, keeping it and
  `@lexical/code-core` out of core.
- `extension.ts` — the consumer extension contract. **Types only**, imported with
  `import type` so it never lands in the bundle.
- `tags.ts` — shared update tags, its own module so producers and consumers of a tag never
  import each other (a cycle that would drag the core entry into any lazy chunk).

`mentions.ts` and `table.ts` both **default-export a `LexicalExtensionFactory`**, so
`create()` loads them through the same contract as a consumer extension.

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
- `clear-formatting`

Style via `[data-lexical-active]` / `[data-lexical-disabled]` in
`wwwroot/blazor-lexical.css` — never a Blazor-set `.is-active` class.

## Overlays

Blazor authors the markup as `ChildContent`; `overlays.ts` only positions and drives it.
Each overlay **activates by marker presence** — `create()` scans the root once and wires
behavior only if the marker exists. Overlay buttons are ordinary `data-lexical-command`
markup, so the overlays add **no interop of their own**.

- `data-lexical-floating-toolbar` — shown above a non-empty selection.
- `data-lexical-slash-menu` — typeahead opened by `/`; items carry
  `data-lexical-slash-item` + `data-lexical-slash-keywords`; JS sets
  `data-lexical-slash-active` and deletes the `/query` text before the command runs. An
  item without a `data-lexical-command` (a C# `OnSelect`) is the only slash interop.
- `data-lexical-drag-handle` — left-gutter handle; `data-lexical-drag-grip` (`draggable`)
  reorders via node moves, `data-lexical-add-block` inserts a paragraph + `/`. JS owns a
  `data-lexical-drop-line` indicator.
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
