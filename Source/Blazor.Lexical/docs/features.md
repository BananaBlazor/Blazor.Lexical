# Built-in features: tables, mentions, TOC, marks, comment composer, highlights, suggestions, stats, horizontal rule, page break, tab indent, indent, block gutter

Read this before changing anything under `Extensions/` or the matching `js/src/*.ts`
(`table.ts`, `mentions.ts`, `toc.ts`, `marks.ts`, `comments.ts`, `highlights.ts`, `suggestions.ts`, `stats.ts`, `hr.ts`, `page-break.ts`, `atomic-block.ts`, `tabindent.ts`, `indent.ts`, and
`registerBlockGutters` in `overlays.ts`). The text-quote anchor engine `marks`, `highlights` and
`suggestions` share lives in `text-quote.ts`; the read-only selection bridge those first two use
lives in `dom-selection.ts`. The DOM markers and command tokens these features use are in
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
`<LexicalMention Initiator="@" Color=… Provider=… Freeform=… AllowSpacesInQuery=…/>`
configs; each is independent. `Initiator` starts a session; by default the query is one
`[\w-]*` run, so the first space ends it. `AllowSpacesInQuery` (opt-in, default off) widens
`makeTriggerRegex` to up to four single-space-separated words plus one trailing space, so
`@jane do` stays open and the provider gets `"jane do"`; a double space, newline, fifth word,
←/→ or Escape still ends it. `triggerOffset` is derived from the whole match, so commit
replaces every word. ←/→ and Escape record the trigger as `dismissed`: it stays closed while
that trigger (node key + offset) keeps matching — typing on or moving the caret inside the
query does not reopen it — and clears once it stops matching or a new trigger starts. ↑/↓
cycle the highlighted row (wrapping) and keep it scrolled into view. Mirrors the slash menu but does **not** reuse `registerSlashMenu`:

- **Two custom nodes**, both `TextNode` subclasses in `createEditor`'s `nodes[]` (added
  only when configs exist): `MentionNode` — token mode (atomic), carrying an opaque
  **app-owned** `value` + optional `url` + `configId`/`trigger`/`color`, serialized
  (`exportJSON`/`exportDOM` → `data-lexical-mention`); and `MentionHighlightNode` — an
  `isTextEntity()` node for freeform highlighting via `registerLexicalTextEntity` (from
  `@lexical/text`). Grow `LexicalTheme` (`mention`, `mentionHighlight`) alongside them;
  per-config colour rides inline as `--blazor-lexical-mention-color`, the class stays typed.
- **The picker is JS-created**, not Blazor-authored — rows come from provider data, so
  `registerMentions` builds `[data-lexical-mention-menu]` under the root. The one overlay
  with no Blazor marker. It is a manual popover placed in viewport coordinates
  (`floating.ts`, see "Positioning" in `js-contract.md`), so a short editor inside an
  `overflow: hidden` card still shows the whole list, flipped above the caret when the
  space below is short.
- **A committed mention is one inline object** (`registerAtomicMentions`, wired for every
  editor with configs, provider or not — mentions also arrive via Markdown/HTML). Token
  mode makes edits atomic: typing over a selected mention replaces it, and Backspace/Delete
  next to one removes it whole. `readMentionMatch` ignores token/segmented anchors, so a
  caret on a mention never opens the picker. A `SELECTION_CHANGE_COMMAND` handler moves
  any endpoint out of a mention — a collapsed caret inside selects it whole (this is what
  a click does), a range endpoint moves to the mention's far edge — but not while the
  pointer is down (a drag is fixed on `pointerup`). ←/→ step over a mention and Shift+←/→
  extend over it whole; at a block end the caret rests at the mention's edge, where
  typing starts a new text node. A mention a range covers whole carries
  `data-lexical-mention-selected` (outlined in CSS). `importJSON` forces token mode, since
  documents saved earlier carry `"mode":"segmented"`.
- **The picker is a WAI-ARIA combobox popup.** Focus never leaves the content element: the
  menu is `role="listbox"` (id `<content id>-mention-menu`), rows are `role="option"` with
  unique ids, `tabindex=-1` and `aria-selected`, and JS mirrors the state onto the content
  element — `aria-expanded`/`aria-controls`/`aria-autocomplete="list"` while open,
  `aria-activedescendant` on the highlighted row; closing sets `aria-expanded="false"` and
  drops the descendant. These are JS-owned: the component never renders them, and Blazor's
  diff leaves attributes it didn't render alone across re-renders.

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

**Starting a session from a button.** `<LexicalMentionButton Initiator="@@">` renders a
`mention:@` command token, so the delegated listener calls `startMention` with no interop and
mirrors `canStartMention` onto `data-lexical-disabled` (see `js-contract.md`).
`StartMentionAsync(initiator)` is the programmatic path to the same function. `startMention`
inserts the initiator as an ordinary (undoable, notifying) edit and focuses the editor; the update
listener then opens the picker exactly as for a typed trigger, so there is no second open
path. The trigger regex only fires at a text node's start or after whitespace, so the insert
adds a space when `$charBeforeCaret` is not whitespace; it reads across formatting runs,
links and mentions, so across nodes the space is what the user sees rather than what the
regex needs. No selection (a never-focused editor) inserts at the document end; a range is
removed first. A click on the host's button needs no `mousedown` handling: Lexical ignores
a DOM selection outside the editor, so its own selection survives, and `editor.focus()`
restores it to the DOM the picker positions from. Refused (false) for a read-only editor, a
node or table selection, or an initiator with no provider config.

**Markdown round-trip is opt-in per config** via `MarkdownUrlPrefix`. `markdown.ts` prepends
a text-match transformer (ahead of stock `LINK`, since on an equal start index the import
loop keeps the first match) when any config has a prefix. Both directions key on the
*url*, not the node's `configId` (a per-session id, stale in restored JSON): export writes a
mention whose url starts with a registered prefix as `[label](url)` (label
backslash-escaped for `\`, `[`, `]` and `` *_`~ ``), and import turns a link whose url
starts with a prefix (longest wins) into a `MentionNode` of that config, `value` = the url
minus the prefix. Imported nodes take the *current* `configId`/`color`. Every other mention
still exports as plain text, as does one whose url has whitespace or parentheses (the
import pattern could not read it back). Formatting (bold etc.) round-trips around the link. The chunk finds configs through
`getMentionConfigs(editor)`, which `create()` binds via `bindMentionConfigs` after
`createEditor` and the core plugins — before initial content, which runs ahead of extension `register()` — so
Markdown `InitialContent` maps mentions too. The import direction stays core ← chunk.

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

**Two annotation entry points beyond a live selection.** `WrapSelectionInMarkAsync` (the
`wrap` invoke) works read-only via the `dom-selection.ts` fallback (invariant #8) — a reviewer
selecting text in a `ReadOnly` editor. `WrapTextQuoteInMarkAsync` (the `wrapQuote` invoke)
needs no selection at all: it resolves a stored `LexicalTextQuote` against the live DOM with
the shared `text-quote.ts` engine (the same matcher [Highlights](#highlights) uses), turns the
matched `Range` into a Lexical selection via `$selectionFromDomRange`, and wraps that. It is
how an app **re-hydrates** its annotations after a load — the library persists nothing, so the
app keeps its own anchor data and repaints. Both paths share the wrap/overlap-merge code, so a
hydrated mark is indistinguishable from a user-wrapped one (`ScrollToMarkAsync`,
`SetActiveMarkAsync`, the click channel all work on it).

## Comment composer

`js/src/comments.ts` + `Extensions/Comments/`. The Lexical playground's
`CommentPlugin`/`CommentInputBox` as a primitive: a floating in-editor comment box that, on
confirm, wraps the selection in a mark carrying an **app-owned** id and raises `OnSubmit`.
Static in core; contributes no node of its own.

**It is built ON marks, not beside them, and requires a sibling `<LexicalMarks>`.**
Confirming calls `@lexical/mark`'s `$wrapSelectionInMarkNode` — the same node, and the same
overlap-merge behaviour (via marks' `registerNestedElementResolver`), that
[Marks](#marks) registers. Owning `MarkNode` here too would collide on `getType() ===
'mark'` and the loader would skip the later module (see
[extensions.md](extensions.md)), so it deliberately does **not**; the C# doc comment says
so, and a composer without marks throws on wrap.

**It is an extension that also owns a floating overlay** — the hybrid the other overlays
(link editor, gutters) aren't. `register(ctx)` drives `[data-lexical-comment-composer]`
from `ctx.root`, mirroring `registerLinkEditor` for show/hide + selection-rect positioning;
but unlike the create()-scanned overlays it *also* has an id-routed extension channel, which
is exactly what an app-driven `OpenAsync` and the opt-in `OnSubmit`/`OnCancel` pushes need.
So it is a `LexicalExtension` (`BuiltIn = "comments"`) rendering markup, not a pure overlay.

**The blur problem is solved by capturing at open.** A textarea taking focus collapses the
editor's selection, so the span to wrap is gone by confirm time. `openBox` snapshots the
Lexical `RangeSelection` (cloned) the instant it opens and wraps *that* on confirm. The DOM
range for the compose-highlight and positioning is built from the Lexical selection with
`@lexical/selection`'s `createDOMRange` — **not** `window.getSelection()` — so `OpenAsync`
works even when called from a control outside the editor that has taken DOM focus (Lexical
retains its selection across that; the browser's does not). `openBox` reads through
`editor.read` and, when the editor is read-only and `$getSelection()` is empty, falls back to
`$rangeSelectionFromDom` (invariant #8) — so the composer opens over a reviewer's selection in
a `ReadOnly` editor.

**The textarea is optional: `ChildContent` swaps in a full form.** The built-in box is a
single textarea; a review app needs a whole form (department/severity/body, a suggested-edit
field) floating at the selection. Supplying `ChildContent` renders it *in place of* the
textarea — the box, its positioning, the compose-highlight and the wrap-into-mark are
unchanged. JS detects the absence of `[data-lexical-comment-input]` and switches to
form-driven mode: the app drives the box with C#-side `ConfirmAsync()`/`CancelAsync()` (the
`confirm`/`cancel` invokes) from its own buttons and reads its own fields, and `OnOpened`
(`CommentComposerOpened(markId, selectedText)`, the `opened` push captured at open) tells it
when the box appears and over what text so it can reset/populate. The built-in textarea path
is untouched and back-compatible; `OnSubmit` still carries the app-owned mark id either way.

**The compose-highlight follows the highlights precedent, not the marks one.** It is painted
with the CSS Custom Highlight API (`CSS.highlights.set('blazor-lexical-comment-compose', …)`),
so it survives the selection moving into the textarea, spans blocks, needs no node and no
undo step — and therefore has **no `LexicalTheme` key** (there is no element to hang a class
on; the mark it eventually creates is themed by marks' `Mark` key). Styling is
`::highlight(blazor-lexical-comment-compose)` in the core CSS.

**Mark ids stay app-owned, with a deliberate fallback.** Supply the id per open via
`OpenAsync(markId)`, or a `NewMarkId` factory the bundled `<LexicalAddCommentButton>` calls.
The button originates its click in JS, which can neither block on .NET for an id (invariant
#5) nor — normally — mint one. So: with a factory, the button fires `compose`, the C# half
mints via `NewMarkId()` and calls `OpenAsync` back (one round trip; the selection survives
it); **without** one, the JS half mints a UUIDv7 locally (zero-interop open). Either way
`OnSubmit` carries the final id, so an app using the mint fallback still learns the key to
file its thread under. `HasInvokeHandler` is armed by `OnSubmit`/`OnCancel` **or** a factory
(the `compose` request rides the same gated channel); `OpenAsync` itself is C#→JS and always
works. `[data-lexical-comment-compose]` gets `data-lexical-disabled` toggled from selection
state, so an add button in a fixed toolbar is live only over a non-collapsed range.

## Highlights

`js/src/highlights.ts` + `Extensions/Highlights/`. The counterpart to marks, and the
distinction is worth holding onto: **a mark is a node the app knows the position of and
wants to keep; a highlight is a decoration the app describes by its text and throws away.**
Highlights exist for the anchor problem — "an AI reviewer quoted this sentence, find it and
light it up" — where the caller has words, not a position.

**Nothing is inserted.** Painting is the CSS Custom Highlight API
(`CSS.highlights.set(name, new Highlight(...ranges))`), which styles live DOM `Range`s from
the stylesheet. No node ⇒ no serialization surface, no schema change; no `editor.update()`
⇒ no undo step, no dirty document, and no need for the silent-update tag; no DOM mutation
⇒ nothing for the reconciler to collide with. It is also the only mechanism that spans
block boundaries *and* survives a click, which a native selection does not — and surviving
a click is the requirement, since the point is to leave suggestions up while the user
works. Consequently there is **no `LexicalTheme` key**: there is no element to hang a class
on. Styling is `::highlight(blazor-lexical-<id>)`, one rule per app-chosen id, which is how
several sets (AI vs. reviewer, spelling vs. structure) get different colours. Ids must be
valid CSS identifiers.

**Queries are stored, not ranges.** Reconciliation replaces DOM nodes, so every stored
`Range` goes stale on the next edit. A highlight therefore keeps its *query* and
re-resolves against the live DOM from a debounced update listener (~50 ms). That is also
what makes the anchor follow its text as the document is edited around it, and what makes
a highlight disappear when its words are deleted and return on undo.

**Matching normalizes whitespace on both sides** — every run of spaces, newlines, and
implied block boundaries collapses to one space (`BLOCK_TAGS` in `highlights.ts` drives the
boundary part). Without it, prose quotes would fail against a DOM carrying indentation,
inter-block newlines, and words split across text nodes by a bold run or a mark.

**Prefix/suffix disambiguate, they never gate.** Every occurrence is scored by how much
surrounding context it reproduces and the best wins *even at zero* — an anchor has to
survive edits nearby, and demanding an exact prefix+suffix match would drop the highlight
the moment a neighbouring word changed (this is the TextQuoteSelector contract). A tie on
the winning score is reported as `MatchedAmbiguously`: painted, but known to be weak.

Registration is document-global (`CSS.highlights` is keyed by name, not by element), so the
module-scope registry unions the ranges every editor instance contributed under a name.
Two editors on one page highlighting the same id both paint, and disposing one does not
blank the other.

**Selection is deliberately not here.** A `SelectTextAsync` belongs with the core selection
push (`LexicalSelectionState`), not in this extension — selection is document state, a
highlight is not.

## Suggestions

`js/src/suggestions.ts` + `Extensions/Suggestions/`. The inline "suggested edit" diff —
`<LexicalSuggestions>`, a built-in following the same C#+ESM-factory pattern as everything
else (`BuiltIn = "suggestions"`; contributes two nodes, an opt-in click channel, and
`ShowSuggestion`/`RemoveSuggestion`/`ClearSuggestions` over `invoke`). It renders a targeted
span struck through and a proposed replacement inserted after it, highlighted — Google Docs
suggestion mode, **display-only**.

**It deliberately mutates the in-memory document, and that is safe here.** The review viewer
never persists editor state (the app repaints from its own source of truth on every load), so
`ShowSuggestionAsync` resolves the target quote against the live DOM (the shared `text-quote.ts`
engine again), turns it into a Lexical selection via `dom-selection.ts`'s `$selectionFromDomRange`,
`extract()`s the covered text nodes, and replaces them. Nothing round-trips back out — see
`architecture.md`, "Read-only selection", for why this is acceptable where mutating a *saved*
document would not be.

**Two nodes, both `TextNode` subclasses so they flow inline.** `SuggestionDeletionNode` holds
the original text with the `suggestionDeletion` theme class (line-through) + `data-suggestion-id`;
it keeps the run's real format/style, so removing the suggestion restores it verbatim.
`SuggestionInsertionNode` holds the proposed text with the `suggestionInsertion` class +
`data-suggestion-id`, in **token mode** so it reads as one atomic inserted unit. Grow
`LexicalTheme` (`SuggestionDeletion`, `SuggestionInsertion`) alongside them — the extension
ships in-box, so typed theme keys are the norm (the mark/hr precedent).

**Suggestion ids are app-owned and opaque** (the marks convention). A click on either half
reports the id through `OnSuggestionClicked`, resolved from the clicked *element* like marks;
the click channel is armed only when that callback is wired. There are deliberately **no
accept/reject affordances** — the consuming app renders its own chrome and drives accept/reject
in its own store, then calls `RemoveSuggestionAsync(id)` (restores the struck text to plain
prose, drops the insertion) or `ClearSuggestionsAsync()`.

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
`hrSelected` styles — via the shared `atomic-block.ts` helper (an update listener paints
the class, rather than upstream's signals). Zero interop. Opt-in, so an `<hr>` in HTML
loaded into an editor without the extension is dropped, the same trade tables make.

## Page break

`js/src/page-break.ts` + `Extensions/PageBreak/`. A port of the Lexical playground's
`PageBreakNode`: node type `"page-break"`, DOM `<figure type="page-break">`, so documents
round-trip with other Lexical apps. Inserted by the `page-break:insert` token (toolbar
button and slash item, both gated on `HasExtension<LexicalPageBreak>()`) or `InsertAsync()`
from C#. Selection and Delete/Backspace come from the same `atomic-block.ts` helper hr uses
(the playground's React component is not ported); theme keys `pageBreak` /
`pageBreakSelected`. Zero interop; opt-in, so page-break markup loaded without the
extension is dropped.

**Top-level only.** A node transform splits any list, quote or paragraph around a nested
break; inside a table cell (a shadow root) it moves after the table. When nothing follows,
an empty paragraph is appended so the caret has somewhere to go.

**HTML.** Export adds `style="break-after: page; page-break-after: always"`, so exported
HTML paginates in print and HTML-to-PDF/Word tools without extra CSS. Import accepts
`figure[type=page-break]` and Word's clipboard form, `<br style="page-break-before:always">`
(or `break-before: page`); a plain `<br>` stays a line break. Markdown has no page-break
form, so markdown export drops them. Default CSS draws a dashed rule with a label
(`--blazor-lexical-page-break-label`) and a `@media print` rule that paginates the live
editor.

## Tab indentation

`js/src/tabindent.ts` + `Extensions/TabIndent/`. A port of upstream's
`registerTabIndentation`: Tab/Shift+Tab indent and outdent blocks, `MaxIndent` caps the
depth, and a caret mid-text still inserts a tab. No node, no theme, no interop —
behaviour only, which is the shape the contract has to accommodate.

It owns no indent handler: `MaxIndent` goes into `indent.ts`'s registry as the *fallback*
policy (element blocks only, list items included), used only while no `<LexicalIndent>` is
present. Alone it behaves as before. Tab on a node selection holding an opted-in decorator
maps to indent/outdent too, but only when `<LexicalIndent>` is present; any other node
selection lets Tab leave the editor.

**Opt-in for accessibility, not taste.** Binding Tab inside the editor takes it away from
keyboard navigation and traps focus; Lexical's own docs discourage the behaviour for that
reason. It is a component rather than an editor flag so that enabling it is a deliberate
act, and the C# doc comment says so.

## Indent

`js/src/indent.ts` + `Extensions/Indent/`. Owns indent/outdent policy for the editor: the
`indent:in` / `indent:out` tokens (`LexicalIndentButton`; the default toolbar shows both
when `HasExtension<LexicalIndent>()` and `ShowIndent`), Tab when `<LexicalTabIndent>` is
also present, and the buttons' disabled state. Adds no key binding, no node, no theme, no
interop.

- **`MaxIndent`** (`int?`): exclusive, like `LexicalTabIndent.MaxIndent` — `n` allows
  levels below `n`; null is uncapped. When both set it, this one wins.
- **`IndentListItems`** (default true): false leaves list items where they are (Lexical
  indents a list item by nesting it, which flat output can't represent). Consequence:
  `@lexical/rich-text` turns Backspace at the front of an indented block into
  `OUTDENT_CONTENT_COMMAND`, so Backspace no longer un-nests a nested list item either.
- **Reasons** (`MaxIndentReason`, `AtMarginReason`, `NotIndentableReason`): overridable
  strings; null falls back to the JS English defaults.

**One policy per editor, order-independent.** Extensions can't see each other and built-ins
register in markup order, so neither component checks for the other. Each registers a
policy in a `WeakMap<LexicalEditor, IndentState>`: `LexicalIndent`'s is authoritative,
`LexicalTabIndent`'s a fallback. One ref-counted pair of `COMMAND_PRIORITY_CRITICAL`
indent/outdent handlers reads the active policy at dispatch time.

**Handlers claim governed selections.** They walk the blocks `$handleIndentAndOutdent`
would (nearest non-inline element ancestor per node, deduplicated, `canIndent()` true) plus
opted-in decorators (`IndentableBlockNode`, see `extensions.md`) — node-selected ones, and
range-selected ones whose parent is the root or a shadow root, which upstream never reaches.
Excluded list items are dropped, the rest clamped to `[0, MaxIndent)`. They return `true`
whenever a candidate block was found, even if nothing moved (including when every block is
an excluded list item): a `false` would let the stock EDITOR-priority handlers run uncapped.
Without either component the tokens reach those stock handlers.

**Disabled state** comes from the same walk, so it can't drift from what the commands do.
Painted only by `LexicalIndent`, synchronously from update and editable listeners. Indent:
not editable, no candidate, or only excluded list items → `NotIndentableReason`; else every
movable block at the cap → `MaxIndentReason`. Outdent likewise with `AtMarginReason` at
level 0. See `js-contract.md` for the attributes. Rewriting the Blazor-rendered `title` is
tolerated because Blazor patches it only when the parameter changes; keep it to these two
controls.

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

**Dragging is top-level by default; nested drag is a JS policy.** The grip reorders
top-level blocks through Lexical node moves. A consumer opts into dragging *nested*
block-level nodes (and reparenting) by installing a `BlockDragPolicy` via `ctx.blockDrag`
from an extension — not through a C# knob, because the policy is pure logic over live
Lexical nodes (invariant #1: no per-node interop). The SDK owns hit-testing, the grip and
drop-indicator rendering, and the default move; the policy owns `source` (what drags),
`targets` (where it may land, as `{ parent, index }` gaps) and `drop` (how — defaulting to a
node move). The whole drag engine lives in `js/src/block-drag.ts`; `overlays.ts` runs it for
both paths, and "no policy" is the empty policy, so the top-level behavior is the *same
code*, not a parallel branch. Two details worth keeping: the drop target resolves purely
from the pointer's **vertical** position (the grip's X is uninformative, and an `*-outside`
drag has no X over the card — this is also why the drag listeners live on `document`, not
`root`), and hovering a rail **freezes** the hit-test (re-resolving there would collapse a
nested block back to its container as the pointer crosses onto the grip). The drop indicator
is themeable per target via the `--lexical-drop-color` custom property. See
`docs/extensions.md` (`ctx.blockDrag`) for the policy contract; harness: `harness/block-drag`.
