# The extension contract

Read this before changing `Extensions/LexicalExtension.cs`, the extension plumbing in
`LexicalEditor.razor.cs`, or the loading loop in `js/src/index.ts`.

**Everything that is not the editor is an extension of it.** One contract; built-in vs.
external differs only in how the JS is bundled (built-in = compiled into our bundle via a
literal `import()` chunk or static-in-core; external = separately-built ESM `import()`ed by
URL). There is **no privileged tier** — the built-ins get nothing a consumer can't. The
only extra is `internal virtual string? BuiltIn`, resolved by a closed literal-`import()`
switch; it's internal precisely so it isn't a seam, and it buys bundling, not access. If a
first-party feature needs something the public contract can't express, widen the public
contract.

- **C#**: `LexicalExtension` (`Extensions/LexicalExtension.cs`) — abstract `ComponentBase`;
  grabs the cascaded editor, registers in `OnInitialized` / unregisters in `Dispose`,
  carries a per-instance `ExtensionId` routing calls both ways. Subclasses declare
  `ModuleUrl`, opaque `GetOptions()`, and opt into interop by overriding `OnInvokeAsync`;
  `InvokeJsAsync` is the reverse; `OnEditorReadyAsync()` is the first safe moment to call
  (fanned out after `create()` in registration order, each wrapped in the same isolation
  idiom a broken extension gets at load). Both directions speak **JSON strings**.
- **Editor plumbing** (`LexicalEditor.razor.cs`): `_extensions` +
  `RegisterExtension`/`UnregisterExtension`, `_builtIns` (materialized lazily by
  `AllExtensions()` from registered `<LexicalMention>` configs), the `_createdExtensions`
  snapshot + `HasExtension<T>()` + `ExtensionsChanged`, the single `Extensions` descriptor
  array on `LexicalCreateOptions`, the id-routed `[JSInvokable] InvokeExtensionAsync`, and
  `InvokeExtensionJsAsync`. `AllExtensions()` yields built-ins first so their nodes keep
  their place in `nodes[]`. There is **one** JS→.NET extension entry point and **one**
  `DotNetObjectReference`.
- **Theme fragments**: a module may return `theme: { … }`, merged pre-`createEditor` with
  the **host winning**. Merging is **deep** (`deepThemeMerge` in `index.ts`, mirroring the
  semantics of `deepThemeMergeInPlace` in `@lexical/extension`, which that package does not
  export — hence reimplemented, prototype-pollution guard included), so a host theme setting
  `heading.h1` overrides that key instead of replacing the group. Keys are namespaced to the
  extension (`badge`, never `paragraph`); an extension↔extension key clash is **warned**
  about (host↔extension is silent — host winning is the design). Typed `LexicalTheme` stays
  **core-only**, except where the extension ships in-box (`Mark`, `Hr`, …).
- **Upstream vocabulary**: `name`, `conflictsWith` and a thunk-or-array `nodes` are the same
  fields, with the same meanings, as Lexical's own `defineExtension` — so an extension
  written against `@lexical/extension` ports mechanically. See `architecture.md`, "Why we
  borrow from `@lexical/extension` but don't build on it", for what we deliberately did not
  take and why.
- **Collision handling**: the loader rejects a module whole — it never registers half of one
  — on a duplicate `name`, a `conflictsWith` match in either direction, or a node whose
  `getType()` is already claimed by a core node or an earlier extension. That last one is
  the load-bearing check: two classes sharing a type throws *inside* `createEditor` and
  would otherwise take the editor down. Upstream refuses to build the editor in all three
  cases; **we log and skip the later module**, per the invariant below.
- **`setup` is the whole environment**: `options`, `lexical` and `utils` (the host's own
  module namespaces), `invokeDotNet`/`notifyDotNet`/`canInvokeDotNet`, `silentUpdateTag`,
  and `primitives` (the ghost/entity-commit building blocks — see below). Anything an
  extension would otherwise import or hardcode belongs here.
- **Single-instance by default**: `RegisterExtension` **throws** on a second instance of a
  type unless it overrides `AllowMultiple`. Stricter than the mentions dedupe (instance
  identity only) — a duplicate would register nodes and listeners twice, so it fails loudly.
- **Interop opt-in**: the descriptor's `HasInvokeHandler` defaults to "does the concrete
  type override `OnInvokeAsync`", narrowed further by subclasses to their own
  `HasDelegate`. False ⇒ JS's `invokeDotNet` throws, and `canInvokeDotNet` says so. The
  override test is a **delegate bind** —
  `((Func<string, string, Task<string?>>)OnInvokeAsync).Method.DeclaringType` — resolving
  through the vtable. Do **not** use `GetMethod(...)?.DeclaringType`: under trimming the
  name lookup returns null and the gate fails *open*, reporting a handler that isn't there.
- **Loading** — **one loop** in `create()`, pre-`createEditor`, over `options.extensions`.
  `builtIn` resolves through the closed switch of literal imports (`'table'` → `await
  import('./table')`, which is what lets esbuild split the chunk; `'mentions'` → the
  statically-imported runtime, no extra fetch). Otherwise `moduleUrl` is resolved against
  `document.baseURI` (a bare `./_content/X/y.mjs` is written the way Blazor asset paths
  are; a raw `import()` would resolve it relative to `_content/Blazor.Lexical/`) and
  `import()`ed — and because that URL is a **runtime variable**, esbuild emits the
  `import()` untouched. From there both tiers are indistinguishable: the factory gets
  `{ options, lexical, invokeDotNet, canInvokeDotNet }`, its `nodes` are spread into
  `createEditor`'s `nodes[]`, and after core plugins each module's `register(ctx)` runs with
  its teardown joining `cleanups`. A broken extension is logged and skipped — it never takes
  the editor down. `create()` keeps the built-in module *namespaces* by name (`tableModule`
  / `mentionsModule`) for the few core call-sites that want the feature and not the
  extension: the `table:` token and the `insertTable` / `getMentions` / `refreshMention*`
  exports.
- **`setup.lexical` is the host's own Lexical namespace** (`await import('lexical')`,
  loaded only when extensions exist). Load-bearing: an extension bundling its own copy
  would subclass *different* classes and the editor would refuse its nodes. It also lets an
  extension ship hand-written ESM with no bundler.
- **Commands stay closed**: extensions don't extend the `runCommandToken` switch. They
  register their own commands/listeners under their own markers (the badge sample uses
  `data-lexical-badge-insert`), or use the `@onclick` escape hatch (invariant 4).
- **CSS is a manual `<link>`** — not auto-injected, same as the core stylesheet. Document
  it for consumers.
- **Reference implementation**: `Samples/Extensions.Badge` — custom node, client-side
  insert, one opt-in JS→.NET callback, two .NET→JS calls, as hand-written ESM. The harness
  pairs `#editor-badge-notify` with `#editor-badge-quiet` (same extension, no callback) to
  prove the opt-in gate, mirroring `.harness-format` / `.harness-notify`. Covered by
  `Tests/Integration/ExtensionTests.cs` and `Tests/UnitTests/LexicalExtensionTests.cs`.
- **Not done, on purpose**: no DI/builder hook (`AddLexicalBlazor` stays options-only —
  extensions are per-editor and configured in markup), and no rescan for extensions added
  after `create()` (same limitation as overlays).

## `ctx.blockLayout` — per-block positioning

`register(ctx)` gets `blockLayout` alongside `editor`/`root`/`content`. It exists for the
gap between the built-in block gutter and an app's own per-block UI: `<LexicalBlockGutter>`
is deliberately **hover-only and singular** (one rail chasing the pointer — the grace
window exists because it's ephemeral), so persistent, many-at-once, app-styled,
app-stateful indicators are the *app's* domain. What the SDK owed it was not another rail
but the two pieces of geometry the gutter already had privately, so an app can build its
own extension without re-deriving them.

Named `blockLayout`, **not** `gutter`: the four gutter anchors are today's only spots, not
the concept. The concept is "position something relative to a block", and a future spot
(`'above'`/`'below'`) should be an additive union member, not a second namespace beside a
misleadingly-named `gutter`.

- **`blocks()`** — every top-level block in document order (`{ key, element, index, type }`),
  read fresh each call. Not cached; call it each time you reposition.
- **`anchor(spot, sizePx, consumedPx?)`** — the root-relative CSS `left` for an element of
  `sizePx` at `spot` (`left-inside` | `left-outside` | `right-inside` | `right-outside`),
  honoring the same content-padding / inside-clamp / outside-hangs-off-the-card math the
  built-in gutter uses. `consumedPx` stacks a second item further out. The axis is implied
  by `spot`.
- **`onBlocksChanged(callback)`** — fires after a structural/content change (coalesced to
  one call per animation frame) and on window resize; returns a teardown. **Not** wired to
  scroll on purpose: a block and `root` share one scroll container, so a block's offset
  from `root` is scroll-invariant — only reflow moves it.

The math has one owner: `js/src/block-layout.ts` (`listTopLevelBlocks` / `computeBlockAnchor`),
which `overlays.ts` also runs on, so the gutter and an app extension can never drift.
Worked example: `Samples/Extensions.GutterMarkers` (speaker tabs, a star toggle, a
changed-indicator bar — all its own DOM, styling and state).

## `setup.primitives` — ghost completion & entity commit

`setup.primitives` carries two app-agnostic building blocks the SDK owns because they are
easy to get subtly wrong per-app, both aimed at the **typed reference field** pattern — an
editable region whose content is conceptually a reference to an entity (a city, a tag, a
person) rather than free text. Unlike `lexical`/`utils` (namespaces to call), these are
stateless factories, so one `primitives` object is shared across every extension on the
page: `ghost.attach` takes the editor as an argument, `entityCommit.create` is editor-free.

### `primitives.ghost` — inline ghost completion

`ghost.attach(editor, root, read)` paints a muted "rest of the word" hint **after** the
caret (`chi│cken stock`), the way an autocomplete suggestion reads. `read` runs in the
editor's read context on every selection/content change and returns the `GhostSession`
(`{ anchorKey, text }`) to show, or `null` to hide. Returns a teardown that removes the
overlay and every listener.

The load-bearing invariant is that the ghost is **visible but never real**. The overlay
`<span>` is parented to `root` — *outside* the `[data-lexical-content]` contenteditable —
which is what structurally guarantees it never enters the document, the state JSON, the
history/undo stack, or `getTextContent()`. It is CSS `pointer-events: none` /
`user-select: none` even when visible (like the placeholder), so it never intercepts a
click or lands in a copy. Accepting a completion is the extension's own edit
(`editor.update()` to insert the real text); the primitive only renders and positions the
hint (at the caret's right edge, from the live selection rect).

### `primitives.entityCommit` — the commit lifecycle

`entityCommit.create(opts)` returns a controller for one field's `buffer → resolve →
commit → create-if-missing` state machine. It is DOM-free and editor-free — pure logic
over a candidate list — so wiring it to the editor (reading the text as the query,
inserting on commit) is the extension's job. It generalizes the built-in mentions (insert
a token for an *existing* entity) to a field that *is* the reference, and adds the
create-if-missing path mentions lacks.

- **`setQuery(q)`** stores the query and resets the active match to the first;
  **`current()`** returns `{ query, best, alternates }` recomputed from the live
  candidates (default match: case-insensitive prefix on `text`; empty query ⇒ no matches).
- **`cycle()`** advances the active match (wrapping) over the matches, so `current().best`
  and a subsequent `commit()` follow the cycled selection — the mechanism behind a
  dropdown-less "press ↓ to try the next match".
- **`commit()`**: empty query, or no match with no `createIfMissing`, is a **no-op**; a
  match fires `onCommit(best, { created: false, provisional: false })`; no match **with**
  `createIfMissing` is **optimistic** — `onCommit` fires *immediately* with a provisional
  entity (`{ created: true, provisional: true }`, minted id `provisional:N`), then
  `createIfMissing(query)` is awaited and `onResolved(provisionalId, realEntity)` fires.
  The interactive path never awaits.
- **Rejection**: a `createIfMissing` that rejects fires `onError(query, error)` and
  **leaves the provisional in place** — a field the user has moved on from, half-rolled-
  back, is worse than a provisional that never resolved. Surface it, don't roll back.
- **`dispose()`** marks the field disposed; a late `createIfMissing` settlement after that
  fires neither `onResolved` nor `onError`.

Worked example: `Samples/Extensions.ReferenceFields` (a city-tag field: `par` ghosts
`par│is`, ↓ cycles Paris→Parma, Tab/Enter accepts, an unknown city creates optimistically),
demoed at the website's `/reference-fields` page and driven by
`/harness/reference-fields` in `Tests/Integration/ReferenceFieldsTests.cs`.

## Adding a push channel

One flag on `notify` derived from `.HasDelegate` (mirrored in `setNotifications`); one
crossing per event; debounce at the source (`CHANGE_DEBOUNCE_MS` in `index.ts`); shape the
payload so a subscriber never needs an immediate follow-up call. Extensions never ride core
channels — in particular, do not let an extension arm the content channel, which carries one
host-chosen format. The `ContentChanged` event is a mirror of an already-made crossing,
raised only where the host's `OnContentChanged` invoke already is.
