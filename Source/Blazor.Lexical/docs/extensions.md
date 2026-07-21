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
  the **host winning** (`{ ...extensionFragments, ...(options.theme ?? {}) }`). Keys are
  namespaced to the extension (`badge`, never `paragraph`). Typed `LexicalTheme` stays
  **core-only**.
- **`setup` is the whole environment**: `options`, `lexical` and `utils` (the host's own
  module namespaces), `invokeDotNet`/`notifyDotNet`/`canInvokeDotNet`, `silentUpdateTag`.
  Anything an extension would otherwise import or hardcode belongs here.
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

## Adding a push channel

One flag on `notify` derived from `.HasDelegate` (mirrored in `setNotifications`); one
crossing per event; debounce at the source (`CHANGE_DEBOUNCE_MS` in `index.ts`); shape the
payload so a subscriber never needs an immediate follow-up call. Extensions never ride core
channels — in particular, do not let an extension arm the content channel, which carries one
host-chosen format. The `ContentChanged` event is a mirror of an already-made crossing,
raised only where the host's `OnContentChanged` invoke already is.
