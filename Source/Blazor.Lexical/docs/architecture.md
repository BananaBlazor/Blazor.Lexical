# Architecture

This page explains *why* the library is shaped the way it is. The enforceable form of
these rules lives in `../AGENTS.md`; this is the reasoning behind them. Read it before
changing the interop surface, adding a push channel, or reworking a feature — and update
it when a rule changes.

Consumer-facing usage docs are a separate thing, published from `Web/docfx/`.

## The core principle: pay-for-play interop

Blazor's weak spot in a rich-text editor is the bridge. On Blazor Server every
JS→.NET call is a network round trip; on WebAssembly it's cheap but not free. An editor
that pushes state to .NET on every keystroke is unusable on Server and wasteful
everywhere.

So the library's central rule is that **the bare editor performs zero JS→.NET interop**.
An editor with no callbacks attached does all typing, formatting, undo, and toolbar
state entirely client-side. Blazor learns the content only when it asks
(`GetTextAsync()` and friends). Every push channel turns on individually, and only
because you attached the callback that consumes it.

Five consequences follow, and they're the constraints the codebase is built around:

1. **No JS→.NET calls unless opted in.** A channel is armed from the `.HasDelegate` of
   the callback that consumes it, never speculatively.
2. **Built-in controls do as much as possible JS-side.** Toolbar buttons are markup
   tagged `data-lexical-command`; one delegated listener per editor root dispatches the
   Lexical command and owns active/disabled state. A built-in button click never
   round-trips to .NET.
3. **The editor owns the DOM state Blazor doesn't need.** Placeholder visibility and
   toolbar active state are JS/CSS only, driven by `data-lexical-*` attributes. Blazor
   never sets those attributes; JS never sets `class` on those elements. That separation
   is what stops the two from clobbering each other across re-renders.
4. **Real C# actions are the opt-in escape hatch.** A save or export button is an
   ordinary `<button @onclick>` in the toolbar. It reaches the editor through the
   cascaded `LexicalEditor` and round-trips by design — that's the only expected interop
   for toolbar actions.
5. **JS never blocks on .NET.** Nothing `await`s a .NET call inside `editor.update()`, a
   command handler, or a DOM event handler — anywhere a user is waiting on the result.
   Pushes are fire-and-forget. This is the runtime twin of rule 1: that one governs
   *whether* JS may call .NET, this one governs *where*.

## Why `@ref` and not `@bind-Value`

The most common first reaction is that the editor should be a two-way-bound input like
any other form control. It isn't, deliberately.

The editor is a **document** component, not a value input. Its source of truth is
Lexical's JS `EditorState`. The C# string you see is a *projection* into one of four
formats — text, HTML, Markdown, or state-JSON, modelled by `LexicalContent`. Two-way
binding would have to pick one of them, and then on every external set it would re-parse
the whole document (destroying selection, composition, and undo history), need
echo-suppression to avoid feedback loops, and ship the full document across the bridge
continuously — exactly the flood rule 1 exists to prevent.

Binding is instead **decomposed into halves, each pay-for-play**:

- **In (once):** `InitialContent` takes a `LexicalContent` (format + text), applied
  inside the JS `create()` call. No `@ref`, no ready round-trip, no empty first paint —
  and it lands before history is registered, so it becomes the undo baseline.
- **Out (live, opt-in):** `OnContentChanged` fires debounced, carrying a `LexicalContent`
  in the format `ContentPayload` names (`Text`/`Html`/`Markdown`/`EditorStateJson`), or
  `SignalOnly` for a bare dirty signal. Serializing JS-side inside the debounce keeps
  this to **one crossing** — ask for HTML and you get HTML, rather than a plain-text push
  you discard plus a follow-up fetch. `LastContent` caches the most recent push for free
  reads.
- **Both (on demand):** the `Get*Async` / `Set*Async` pairs, in whichever format the
  caller wants, for when you genuinely need the whole document.

Between them, most applications need no `@ref` at all. A form-integration adapter — an
`InputBase<string>` wrapper with a declared format and debounce, built on these
primitives — is a plausible future addition. It would be built *on* this split, not
instead of it.

## Adding a push channel

There are three: `content`, `selection`, and `blockHover`. Every JS→.NET channel has the
same shape. If you're contributing a new one (focus/blur is the obvious next candidate),
follow it:

1. **One flag on `notify`,** derived from the `.HasDelegate` of the callback that
   consumes it. `setNotifications` mirrors it so a late subscription still arms the
   channel.
2. **One crossing per event.** The args carry everything the consumer needs. Never "the
   event fired, now everyone fetches" — that turns one channel into N round trips.
3. **Debounce at the source** when the event can burst.
4. **Size *and shape* the payload deliberately.** `ContentPayload` is the precedent: it
   spans a bare signal through every format a consumer might want, all serialized JS-side
   inside the debounce. The test for a new channel: *could a plausible subscriber take
   this payload and immediately call back for something else?* If so, let it declare what
   it wants instead — a push the consumer must follow with a pull is two crossings where
   one would do.
5. **`blockHover` is why the third channel exists at all.** `<LexicalBlockGutter>` is an
   *overlay*, not a `LexicalExtension`, so it has no extension channel of its own to push
   over. That is the bar for adding one: not "this feature wants to talk to .NET" (an
   extension already can), but "this thing is not an extension and cannot". It still obeys
   every rule above: armed only by `OnBlockHovered.HasDelegate` or the presence of a
   `LexicalGutterButton`, deduped by node key so it is one crossing per *block* rather
   than per mousemove, and carrying the whole `LexicalBlockRef` so no follow-up pull is
   needed. Several rails share that single crossing — the editor fans it out — rather
   than each opening a channel.
6. **Extensions don't ride these channels.** An extension owns its own JS-side listener
   and its own opt-in `invokeDotNet`, so it computes exactly what it needs in JS and makes
   one crossing. The content channel in particular carries **one host-chosen format**, so
   widening it to serve an extension would either stick that extension with the host's
   format or force the channel to multiply.

   The one concession is a **mirror, not a channel**: the editor exposes
   `event Action<LexicalContent>? ContentChanged`, raised only where the host's own
   `OnContentChanged` is already invoked. It fans out something that already crossed the
   bridge, so it costs no new interop — and a C#-only extension that subscribes gets the
   host's format or nothing.

## Everything that is not the editor is an extension

`LexicalEditor` owns the Lexical instance, the core plugins, the delegated command
dispatch, and the push channels. Every *feature* — tables, mentions, and anything you
write — is a module loaded through one contract. That's why the entire non-editor surface
lives under `Extensions/`.

There is **one contract**. Built-in and external extensions differ only in how the JS is
bundled:

| Tier | JS bundling | Examples |
|------|-------------|----------|
| Built-in | compiled into our bundle — a literal `import()` chunk, or static-in-core | `table`, `mentions` |
| External | separately-built ESM, `import()`ed at runtime by URL | the badge sample, third-party |

There is deliberately **no privileged extension tier**. The built-ins prove it: they get
nothing a consumer extension cannot get. `LexicalTables` is public and authored in markup
exactly like yours would be; both built-ins are ordinary `LexicalExtension` subclasses
riding the same descriptor list and the same id-routed invoke path, and their JS halves
are plain extension factories run by the same loop. The only thing they have that you
don't is an internal `BuiltIn` name resolved by a closed switch of literal imports — and
that buys bundling, not access.

If a first-party feature ever needs something the public contract can't express, that's a
signal to widen the public contract.

The mechanics are in [extensions.md](extensions.md); the consumer-facing guide is
`Web/docfx/writing-an-extension.md`. The design points worth knowing here:

- **Both directions speak JSON strings,** so no consumer payload shape crosses interop as
  a typed object, and nothing forces reflection on you.
- **`setup.lexical` is the host's own Lexical namespace.** This is load-bearing: an
  extension bundling its own copy of `lexical` would subclass *different* classes and the
  editor would refuse its nodes. It's also what lets an extension ship hand-written ESM
  with no bundler at all.
- **Theme fragments merge with the host winning collisions.** Namespace your keys to your
  extension. The typed C# `LexicalTheme` stays core-only.
- **Single-instance by default.** Registering a second instance of the same extension type
  throws unless the type opts into `AllowMultiple` — a duplicated extension would register
  its nodes and listeners twice, so it fails loudly instead of subtly.
- **Commands stay closed.** Extensions don't extend the built-in command tokens; they
  register their own Lexical commands and their own delegated DOM listeners under their
  own markers, or use the `@onclick` escape hatch.
- **A broken extension is logged and skipped.** It never takes the editor down.

## Why we borrow from `@lexical/extension` but don't build on it

Lexical ships its own extension system — `defineExtension` + `buildEditorFromExtensions`
in `@lexical/extension`, with a dependency graph, config merging, conflict detection and
`@preact/signals-core` reactivity. Our contract predates using it and turned out to be a
near-subset, arrived at independently. We deliberately do not adopt the builder, and the
reasons are worth keeping because the question recurs:

- **It would cost the no-toolchain property.** `Samples/Extensions.Badge` is hand-written
  ESM: no npm, no bundler, no `import 'lexical'` — everything arrives through `setup`.
  Requiring `defineExtension` means the symbol has to be resolvable at author time, which
  is strictly more ceremony than returning an object literal.
- **It solves none of the hard part.** Most of `extension.ts` is the .NET bridge —
  `invokeDotNet`/`notifyDotNet`/`canInvokeDotNet`, the `HasInvokeHandler` gate, `invoke`,
  `silentUpdateTag`, per-instance dispose. `@lexical/extension` has nothing to say about
  any of it; it would sit underneath and we would still write all of it.
- **`buildEditorFromExtensions` wants to own editor creation,** and our ordering inside
  `create()` is load-bearing (see [js-contract.md](js-contract.md), "Initial content
  ordering").
- **Our factory is a factory for a reason.** `(setup) => module`, not a static object,
  because `setup` carries per-instance interop handles a module-level constant cannot.

What we *do* take is vocabulary and parts. Where our contract already had a concept,
it now uses upstream's name and meaning — `name`, `conflictsWith`, `nodes` accepting a
thunk — so an extension written against `@lexical/extension` ports mechanically instead
of being rewritten, and a Lexical author reads our contract as a dialect rather than an
invention. Theme fragments merge with upstream `deepThemeMergeInPlace` semantics
(reimplemented, since that helper is internal to the package). The horizontal-rule node
and the tab-indentation handler are ports of upstream's, keeping the `"horizontalrule"`
node type and the `hr`/`hrSelected` theme keys so documents and themes interoperate.

**One divergence is deliberate**: upstream *throws* on a name clash or a declared
conflict and refuses to build the editor. We log and skip the offending module, because
"a broken extension never takes the editor down" outranks it here.

Two practical notes for anyone revisiting this:

- `@preact/signals-core` is **already in our bundle**, transitively — `@lexical/rich-text`,
  `history`, `list`, `link` and `html` all depend on `@lexical/extension`. Its weight is
  not an argument for or against anything we do.
- Importing from `@lexical/extension` directly *is* costly for a different reason: under
  our `--splitting` esbuild build its barrel does not tree-shake, so one symbol drags
  `LexicalBuilder`, `ExtensionRep` and every bundled Extension into a shared chunk
  (measured ~25kb). That is why `hr.ts` and `tabindent.ts` port ~170 lines instead of
  importing them. The cost of the port is losing *command identity* — our
  `INSERT_HORIZONTAL_RULE_COMMAND` is a different object from upstream's — while node
  type and serialization still interoperate.

## Lazy loading: how tables stay out of the bundle

Tables are a **lazily-loaded chunk**, declared in markup like any other extension via
`<LexicalTables />`. There's no `EnableTables` parameter — the editor has no table-shaped
knob at all, which is the authoring surface matching the claim that everything not the
editor is an extension of it.

The mechanism: the only reference to the table module is a dynamic `import()`, so esbuild
code-splits `@lexical/table` (~90 kb) out of the core bundle. Editors that don't declare
it never download it.

Chrome that depends on it — the toolbar's insert picker, the `/table` slash item — asks
`editor.HasExtension<LexicalTables>()`, so it never renders as a dead control. That
answers from the extension set frozen when the editor was created, not the live registry:
an extension added later isn't loaded and mustn't light chrome up.

Mentions, by contrast, is ~4 kb gzipped and ships statically in core. Lazy loading is a
size decision, not a tier.

## Mentions: graceful degradation as a reference

Mentions is the reference implementation of the rule extensions are told to follow about
slow data. Its picker shows itself *immediately* — empty, in a loading state — the moment
a provider query is dispatched, so a slow data source reads as "working" rather than as a
dead trigger. The default styling paints a spinner, a language-free affordance with no
string to localize. A per-config query timeout drops a query that never answers, a
sequence guard makes a late response inert, and `Escape` closes the menu even while it's
still loading.

Two other design points generalize:

- **The editor never resolves display text on load.** It renders the stored text
  immediately rather than firing per-node interop on open. Hosts re-resolve on their own
  schedule via `GetMentionsAsync` and the `RefreshMention*` methods.
- **Those refreshes are silent.** They're applied with a silent-update tag so they add no
  undo step and raise no content-changed push. Opening a document and refreshing stale
  display names never marks it dirty. The tag is generic rather than mentions-specific,
  and extensions reach it as `setup.silentUpdateTag` — reserve it for edits the user
  didn't make, since a silently-tagged *user* edit would never reach the host.

## Initial content ordering

`InitialContent` is applied inside `create()` — after the core plugins, but *before*
history registration and the update listener, committed discretely. That ordering is what
makes a preloaded document paint in the first frame, sit outside the undo stack, and
raise no spurious content push. All three depend on it.

## Versioning

The NuGet version is **`Major.<Lexical-minor>.Serial`** — Lexical `0.48.0` gives
`0.48.<serial>`. The middle component tells you which Lexical minor a package wraps.

There's one source of truth: the committed `package-lock.json`. The build parses the
resolved `lexical` version from it and derives everything else — the package version, the
`LexicalPackage.LexicalVersion` constant, assembly metadata, and the package description
and tags. A publish gate fails the build if any `@lexical/*` package is pinned with a
range operator, out of lockstep, or out of sync between `package.json` and the lockfile,
so a package can never ship claiming a Lexical version it doesn't bundle.
