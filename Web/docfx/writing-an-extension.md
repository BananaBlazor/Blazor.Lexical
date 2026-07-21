# Writing an extension

An **extension** lets you add your own Lexical nodes, commands and plugins to
`<LexicalEditor>` — and your own two-way channel to C# — without forking the library.
Extensions live in *your* code: an app-local component, or your own NuGet package.

An extension has two halves:

| Half | What it is |
|------|------------|
| **C#** | A component deriving from `LexicalExtension`, nested inside `<LexicalEditor>`. |
| **JS** | A separately-built ES module, served by URL, `import()`ed by the editor at runtime. |

The library's own features are built the same way — the only difference is bundling:
`table` and `mentions` ship inside the library's bundle, while an extension's module is
fetched from its own URL. There is no privileged "internal" extension tier.

The worked example below is the `Samples.Extensions.Badge` sample
(`Samples/Extensions.Badge` in the repository): a custom "badge" node, a button that
inserts one, and an optional click callback to C#.

## 1. The C# half

```razor
@namespace My.Extensions
@inherits LexicalExtension

<button type="button" data-my-badge-insert>Insert badge</button>

@code {
    [Parameter] public string Label { get; set; } = "Badge";

    // Wiring this is what opts the extension into JS→.NET calls.
    [Parameter] public EventCallback<string> OnBadgeClicked { get; set; }

    protected override string? ModuleUrl => "./_content/My.Extensions/badge.mjs";

    // Opaque per-instance config, handed to the JS factory.
    protected override object? GetOptions() => new { label = Label };

    // Narrow the opt-in gate to the consumer's actual wiring.
    protected override bool HasInvokeHandler => OnBadgeClicked.HasDelegate;

    protected override async Task<string?> OnInvokeAsync(string method, string argsJson)
    {
        if (method == "badgeClicked")
        {
            using var doc = JsonDocument.Parse(argsJson);
            await OnBadgeClicked.InvokeAsync(doc.RootElement[0].GetString());
        }
        return null; // the JSON result handed back to JS
    }

    // The reverse direction: call your own JS half.
    public Task InsertBadgeAsync(string text) =>
        InvokeJsAsync("insert", JsonSerializer.Serialize(new[] { text }));
}
```

Use it exactly like any other editor child:

```razor
<LexicalEditor>
    <LexicalToolbar />
    <BadgeExtension Label="Draft" OnBadgeClicked="Handle" />
</LexicalEditor>
```

Members you can override:

| Member | Purpose |
|--------|---------|
| `ModuleUrl` | URL of the JS half; `null` for a C#-only extension. **Required.** |
| `GetOptions()` | Per-instance config passed to the JS factory (opaque JSON). |
| `OnInvokeAsync(method, argsJson)` | Handles calls *from* your JS half. Overriding it is the interop opt-in. |
| `HasInvokeHandler` | Narrows that opt-in further (e.g. to `Callback.HasDelegate`). |
| `AllowMultiple` | `false` by default — a second instance of the same extension type in one editor throws. Override to `true` for genuinely multi-instance extensions. |
| `OnEditorReadyAsync()` | Called once after the editor exists and every module is loaded — the first moment `InvokeJsAsync` reaches a live JS half. |
| `InvokeJsAsync(method, argsJson)` | Calls *into* your JS half; returns its result as JSON. |

`OnEditorReadyAsync` is the start-up hook. Before it, the JS editor does not exist and
`InvokeJsAsync` silently no-ops, so anything you want to push into your module at load
belongs here rather than in `OnInitialized`:

```csharp
protected override Task OnEditorReadyAsync() =>
    InvokeJsAsync("seed", JsonSerializer.Serialize(new[] { _pendingText }));
```

Extensions are called in registration order and each call is isolated — one that throws
is logged to the console and skipped, never taking the editor or its siblings down.

Both directions speak JSON strings, so no assumption about your payload shapes leaks
into the library (and nothing forces reflection-based serialization on you).

## 2. The JS half

The module's **default export is a factory**. It is called once per editor, *before*
the underlying Lexical editor is created — which is the only window in which custom
nodes can be declared.

```js
export default function badgeExtension(setup) {
  const { TextNode, $applyNodeReplacement, $getSelection, $isRangeSelection } = setup.lexical;
  const label = setup.options?.label ?? 'Badge';
  let editor;

  class BadgeNode extends TextNode {
    static getType() { return 'badge'; }
    static clone(node) { return new BadgeNode(node.__text, node.__key); }
    static importJSON(json) { return $createBadgeNode(json.text).updateFromJSON(json); }
    createDOM(config) {
      const dom = super.createDOM(config);
      dom.setAttribute('data-badge', '');
      return dom;
    }
  }

  const $createBadgeNode = (text) => $applyNodeReplacement(new BadgeNode(text));

  return {
    nodes: [BadgeNode],                 // registered with createEditor

    register(ctx) {                     // called after the editor exists
      editor = ctx.editor;
      const onClick = (e) => {
        if (e.target.closest('[data-my-badge-insert]')) { /* insert */ }
        const badge = e.target.closest('[data-badge]');
        if (badge && setup.canInvokeDotNet) {
          setup.invokeDotNet('badgeClicked', badge.textContent);
        }
      };
      ctx.root.addEventListener('click', onClick);
      return () => ctx.root.removeEventListener('click', onClick);  // teardown
    },

    invoke(method, args) {              // calls from C# land here
      if (method === 'insert') { /* ... */ }
    },
  };
}
```

Two rules that are easy to get wrong:

- **Take every Lexical binding from `setup.lexical`, never from your own
  `import 'lexical'`.** Your node classes must extend the *host's* classes; a second
  copy of Lexical bundled into your module produces different classes, and the editor
  will not register them. This is also why an extension needs no bundler at all — the
  badge sample is hand-written ESM with no build step.
- **`nodes` is read before the editor exists.** Your factory must not need `ctx.editor`;
  anything that does belongs in `register`.

`register` may also register Lexical commands and listeners on `ctx.editor`, and own
DOM listeners scoped to your own markers under `ctx.root`. Extensions do **not** extend
the built-in `data-lexical-command` token set — use your own attribute (as above) or the
ordinary `@onclick` escape hatch.

When `register` wires more than one thing, collapse the teardowns with `mergeRegister`
from `setup.utils` — the host's `@lexical/utils`, handed over the same way its `lexical`
is, so there is nothing to install and no second copy to bundle:

```js
register(ctx) {
  const { mergeRegister } = setup.utils;
  ctx.root.addEventListener('click', onClick);
  return mergeRegister(
    ctx.editor.registerUpdateListener(onUpdate),
    ctx.editor.registerCommand(MY_COMMAND, handler, 0),
    () => ctx.root.removeEventListener('click', onClick),
  );
}
```

Types for the whole contract ship with the library:

```
_content/Blazor.Lexical/blazor-lexical-extension.d.ts
```

## 3. Interop is opt-in

The library's core promise — an editor that performs **zero** JS→.NET calls unless you
ask for them — extends to extensions. An extension that does not override
`OnInvokeAsync` (or reports `HasInvokeHandler` as `false`) has `setup.canInvokeDotNet`
set to `false`, and calling `setup.invokeDotNet` throws rather than reaching .NET. Check
the flag when your callback is optional.

**Never `await` a .NET call on the interactive path.** JS never blocks on .NET — not
inside `editor.update()`, a command handler, or a DOM event handler. A round trip to
.NET may be a whole circuit away; awaiting one there stalls typing. When you are
*telling* .NET something rather than asking it, use `setup.notifyDotNet(method, ...args)`
— the same channel with the promise (and every failure, including the opt-out throw)
swallowed, so it is safe to call anywhere. Reserve `invokeDotNet` for the places you
genuinely need the answer, and keep the user off the critical path while you wait — see
§3b.

## 3a. Don't ride the editor's content channel

`OnContentChanged` is the *host's* channel — one subscriber, one payload, chosen by the
host. An extension that watched it would learn only that something changed, in someone
else's format, and would then have to call back for what it actually wanted: two
crossings, on every keystroke burst, for a job the JS half can do locally.

Instead, register your own listener in `register(ctx)` on `ctx.editor` (Lexical's
`registerUpdateListener`, or a command/node listener scoped to your own nodes), compute
exactly what you need in JS, and make **one** call through `setup.invokeDotNet` — only
when your C# half is actually subscribed (see below). That is the shape the badge
sample follows.

If your extension really has **no JS half** and still needs to see the document, the
editor exposes a multicast `ContentChanged` event you can subscribe to from the cascaded
editor:

```csharp
protected override void OnInitialized()
{
    base.OnInitialized();                 // registers with the editor
    if (Editor is not null) { Editor.ContentChanged += OnContent; }
}
```

Be clear about what that gives you, because it is deliberately not a channel of your
own: it fans out something that already crossed, so it adds no interop — and therefore
it is **silent unless the host armed the channel**. With no `OnContentChanged` delegate
on the editor nothing is pushed at all, and when there is one you receive the *host's*
chosen `ContentPayload` format, not one you asked for. Treat it as an observation of the
host's traffic, and unsubscribe in `Dispose(bool)`.

## 3c. Silent updates

An edit your extension makes on the app's behalf — not the user's — should not mark the
document dirty or land in the undo stack. Tag it:

```js
ctx.editor.update(() => { /* re-resolve a stale label */ },
                  { tag: [setup.silentUpdateTag, 'history-merge'] });
```

`setup.silentUpdateTag` is the tag the host's content-changed push skips (never hardcode
the string), and Lexical's own `'history-merge'` folds the edit into the previous history
entry instead of pushing a new undoable step. The pair is what lets the built-in mention
refresh update stale names on load and leave the document clean. Reserve it for updates
the user did not make — a silently-tagged *user* edit would never reach the host.

## 3d. Theming your nodes

Return a `theme` fragment from your factory and it is merged into the editor's
`EditorThemeClasses` before the editor is created:

```js
return {
  nodes: [BadgeNode],
  theme: { badge: 'blazor-lexical-badge' },
  // …
};
```

Your node then reads it in `createDOM(config)` — `config.theme.badge` — rather than
hardcoding a class, which is what lets a host restyle your node without forking it.

**Namespace your keys to your extension** (`badge`, `badgeSelected`) and never touch a
core one (`paragraph`, `heading`, `link`, …). The host wins every collision — its
`Theme` is applied *over* the fragments — so a core key is silently ignored at best and
fights another extension at worst. The typed C# `LexicalTheme` stays core-only;
extension keys never enter it.

## 3b. Slow data must degrade gracefully

If your extension fetches anything — from .NET or elsewhere — the UI has to say so, and
it has to give up eventually. A host's data source can be slow, and "nothing happened"
is indistinguishable from "broken".

The built-in mention picker is the reference implementation: the moment a provider query
goes out, the menu is shown (empty) carrying `data-lexical-mention-loading`, which the
bundled stylesheet paints as a spinner. A per-config `QueryTimeout` (default 5 seconds)
drops a query that never answers, closes the menu, and warns to the console — so a hung
provider can't strand the session. Late responses are ignored rather than applied.

Do the same in your own overlays: a state attribute on your marker element while a
request is in flight (style it yourself), a timeout that ends the wait, and a stale-
response guard so an answer that arrives after the user moved on is discarded.

Where the affordance is a state attribute rather than text, there is no string to
localize — worth copying.

## 4. Shipping it

- **JS: nothing for the consumer to do.** Put `badge.mjs` in your RCL's `wwwroot`; it is
  served at `_content/<YourAssembly>/badge.mjs`, and the editor `import()`s it at
  runtime from the `ModuleUrl` you declare (resolved against the app's `<base href>`).
  No `<script>` tag, no `App.razor` edit.
- **CSS: the consumer adds a `<link>`**, exactly as they already do for the core
  stylesheet. Document it in your readme:

  ```html
  <link rel="stylesheet" href="_content/My.Extensions/badge.css" />
  ```

- **Toolbar placement:** if your extension renders a button, tell consumers to drop it
  in the toolbar's `EndContent` — that keeps the default controls and appends yours:

  ```razor
  <LexicalToolbar>
      <EndContent>
          <BadgeExtension Label="Draft" />
      </EndContent>
  </LexicalToolbar>
  ```

- **Optional:** a consumer may warm a heavy extension module with
  `<link rel="modulepreload" href="_content/My.Extensions/badge.mjs">`. Pure perf
  tuning — never required.

- **Pin the runtime with an ordinary range.** `Blazor.Lexical`'s version is
  `Major.<Lexical-minor>.Serial`, so the middle component *is* the Lexical minor your
  nodes are compiled against. That makes the dependency expressible without any extra
  metadata:

  ```xml
  <PackageReference Include="Blazor.Lexical" Version="[0.48.0, 0.49.0)" />
  ```

  Serial bumps inside that line are yours for free; a Lexical minor bump is a version
  your extension has to be rebuilt and re-tested against, and the range says so. At
  runtime, `LexicalPackage.LexicalVersion` is the diagnostic — read it if you want to
  log or assert which Lexical you actually got.

## Timing and limits

Extensions declared in markup register during their `OnInitialized`, which runs before
the editor's first `OnAfterRenderAsync` — that is what guarantees the node-declaration
window. Like overlays, extensions are collected once when the editor is created:
adding one to an already-created editor has no effect until that editor is recreated.
