# Blazor.Lexical (RCL) — library internals

The shipped library: a Blazor wrapper around [Lexical](https://github.com/facebook/lexical).
Root `AGENTS.md` covers repo layout and build/test.

## Architectural invariants — do not regress these

Hard constraints. Violating one needs an explicit, deliberate decision.

1. **No JS→.NET calls unless opted in.** With no `OnContentChanged`/`OnSelectionChanged`
   delegate, JS performs **zero** interop. Channels are derived from `.HasDelegate` into
   `notify.{content,selection}`; `setNotifications` flips them if a callback is added
   later. Never add a "just in case someone's listening" push.
2. **Built-in controls do as much as possible JS-side.** Built-in toolbar buttons are
   markup tagged `data-lexical-command="…"` with **no `@onclick`**. One delegated listener
   per editor root dispatches the command and owns active/disabled state via JS-set
   `data-lexical-active` / `data-lexical-disabled`.
3. **The editor owns the DOM state Blazor doesn't need.** Placeholder visibility is JS/CSS
   only. Blazor never sets `data-lexical-*`; JS never sets `class` on those elements — that
   separation keeps them from clobbering each other across re-renders.
4. **Real C# actions are the opt-in escape hatch.** Anything needing C# logic is a normal
   `<button @onclick>` reaching the editor via the cascaded `LexicalEditor`. That is the
   *only* expected interop for toolbar actions.
5. **JS never blocks on .NET.** No `await` of a .NET call inside `editor.update()`, a
   command handler, or a DOM event handler. Core pushes are fire-and-forget.
6. **Toolbar is a child of the editor.** The editor renders `ChildContent` inside its root
   (so the delegated listener catches the buttons) and cascades `this` (`IsFixed`). Do
   **not** reintroduce a sibling `Editor="@_ref"` toolbar — it forces an `@ref` ordering
   hazard and breaks the `IsFixed` cascade.
7. **Binding is `InitialContent` (in, once) + `OnContentChanged` (out, opt-in) + the
   `Get*Async`/`Set*Async` pairs (on demand)** — never `@bind-Value`. Don't "fix" this.

## Reference — read the relevant file before changing that area

| Area | File |
|---|---|
| Command tokens, overlay markers, bundling/chunks, update tags, initial-content ordering | `docs/js-contract.md` |
| Extension contract, editor plumbing, `setup`, adding a push channel | `docs/extensions.md` |
| Built-in features: tables, mentions, TOC, marks, highlights, stats, horizontal rule, tab indent, block gutter | `docs/features.md` |
| *Why* the invariants above exist — read when a rule looks arbitrary | `docs/architecture.md` |

## Public API surface

- **Public means "part of the SDK."** Everything else is `internal`/`private`: the interop
  wire models and their `JsonSerializerContext` (`LexicalInterop.cs`) and
  `LexicalToolbar.BlockTypeLabel` are internal by design. The deliberate exception is
  `ToJsToken()`/`FromJsToken()` on `LexicalTextFormat`/`LexicalBlockType`/
  `LexicalAlignment` — **public**, so custom `data-lexical-command` buttons don't hardcode
  strings the enums already model. Their internal companions (`IsList`, `ToSelectValue`,
  `LexicalContentFormat.ToJsToken`) stay internal. When something must be `public` for the
  runtime but not for consumers — the `[JSInvokable]` callbacks — hide it with
  `[EditorBrowsable(EditorBrowsableState.Never)]` and say so in its doc comment.
- **Every public member carries a `///` doc comment.** `GenerateDocumentationFile` +
  repo-wide `TreatWarningsAsErrors` make a missing one a build error (CS1591). The doc file
  is what gives *consuming* IDEs hover tooltips. `/// <inheritdoc />` suffices for framework
  lifecycle overrides. Razor `[Parameter]` doc comments ship too.
- Interop payloads serialize reflection-free via `LexicalJsonSerializerContext` (camelCase,
  ignore-null) — add `[JsonSerializable]` for any new wire type.

## Folder shape & namespaces

`LexicalEditor.razor` + `.razor.cs` sit at the project root with the config/wire types
(`LexicalOptions`, `LexicalTheme`, enums, `LexicalContent`, `LexicalInterop.cs`);
**every other component lives under `Extensions/`**, sub-foldered by feature
(`Mentions/`, `Table/`, `Toolbar/`, `Overlays/`) — the "one feature contract" idea made
physical.

**Namespaces stay flat.** Everything is `Blazor.Lexical` regardless of folder, so a consumer
types `Lexical` and IntelliSense offers the whole SDK off one `@using`. Razor files pin it
with `@namespace Blazor.Lexical` (a root `_Imports.razor` won't do: `@namespace` there is a
*base* that subfolders append to); the `.csproj` sets `NoWarn=IDE0130` for C# files. Keep
both when adding files.

## Versioning

`Major.<Lexical-minor>.Serial`. The one source of truth is `js/package-lock.json`; the
`.csproj` parses the resolved `lexical` version at evaluation time and derives *everything*
from it — the version's middle component, the `LexicalPackage.LexicalVersion` const, the
assembly metadata, the package description/tags/release notes, and the packed README footer.
**Never hand-edit any of these** — change the pin and rebuild.

- **Major** is locked to `0` in `Version.props`; bumping it is human-only. Scripts/CI must
  not touch it.
- **Serial** is our revision within a Lexical-minor line. Bump per release.
- Bump Lexical with `Scripts/update-lexical.ps1 -Version x.y.z` (re-pins every `@lexical/*`
  in lockstep, rebuilds the bundle, resets the serial to 0 on a minor change).
- **Publish gate**: `VerifyLexicalAlignment` runs `Scripts/lexical-version.mjs` on every
  build and before `pack`, failing if any `lexical`/`@lexical/*` is pinned with a range
  operator, out of lockstep, or out of sync between `package.json` and the lockfile.
