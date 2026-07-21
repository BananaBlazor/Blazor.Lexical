// ---------------------------------------------------------------------------
// The extension module contract — the JS half of `LexicalExtension` (C#).
//
// This file declares TYPES ONLY. It is never imported at runtime (index.ts pulls
// it in with `import type`, which erases), so it adds nothing to the bundle; the
// build copies it verbatim to wwwroot/blazor-lexical-extension.d.ts so extension
// authors can type their module against the shipped contract:
//
//   /// <reference path="../_content/Blazor.Lexical/blazor-lexical-extension.d.ts" />
//
// An extension's JS half is a separately-built ES module, served by URL (normally a
// static web asset of the extension's own RCL) and `import()`ed by the host before
// `createEditor` runs. Its DEFAULT EXPORT is a factory:
//
//   export default function (setup) {           // LexicalExtensionFactory
//     const { $createTextNode, TextNode } = setup.lexical;
//     class BadgeNode extends TextNode { ... }
//     return {
//       nodes: [BadgeNode],                     // registered with createEditor
//       register(ctx) { ...; return teardown; } // wired after createEditor
//       invoke(method, args) { ... }            // .NET → JS calls land here
//     };
//   }
//
// Two rules make this work:
//
//   * Take every Lexical binding from `setup.lexical`, never from your own
//     `import 'lexical'`. Node classes and `instanceof` checks must come from the
//     host's Lexical runtime; a second copy bundled into the extension would be a
//     different set of classes and would not register.
//   * `nodes` is read before the editor exists — that is the only window in which
//     custom nodes can be declared — so the factory must not need the editor.
// ---------------------------------------------------------------------------

import type { Klass, LexicalEditor, LexicalNode } from 'lexical';

/**
 * The stable half of an extension's environment, handed to the factory before the
 * editor exists.
 */
export interface LexicalExtensionSetup {
  /**
   * The per-instance configuration the C# extension returned from `GetOptions()`,
   * parsed from JSON. `undefined` when it returned null. Its shape is entirely the
   * extension's own.
   */
  options: unknown;

  /**
   * The host's `lexical` module namespace. Build node classes and call `$`-helpers
   * from here so the extension shares the host's Lexical runtime rather than a
   * second bundled copy.
   */
  lexical: typeof import('lexical');

  /**
   * The host's `@lexical/utils` module namespace — `mergeRegister`,
   * `$findMatchingParent`, `$getNearestNodeOfType`, and friends. Already in the host's
   * bundle, so taking it from here costs nothing and (like {@link lexical}) keeps the
   * extension on the host's runtime. `mergeRegister` is the tidy way to collapse
   * several `register(ctx)` teardowns into the one function you return.
   */
  utils: typeof import('@lexical/utils');

  /**
   * Calls the C# extension's `OnInvokeAsync(method, argsJson)` and resolves with its
   * parsed JSON result (`undefined` when it returned null).
   *
   * Opt-in, per the library's no-interop-unless-asked invariant: when the C#
   * extension does not override `OnInvokeAsync` (or reports `HasInvokeHandler` as
   * false) this **throws** instead of calling into .NET. Extensions that offer an
   * optional .NET callback should therefore check {@link canInvokeDotNet} first.
   *
   * **Never `await` this on the interactive path** — not inside `editor.update()`, a
   * command handler, or a DOM event handler (invariant #5: JS never blocks on .NET).
   * A .NET call is a round trip that may be a whole circuit away; awaiting one there
   * stalls typing. Compute in JS, then either fire {@link notifyDotNet} or await the
   * result somewhere the user is not waiting on it.
   */
  invokeDotNet<T = unknown>(method: string, ...args: unknown[]): Promise<T | undefined>;

  /**
   * Fire-and-forget notification to the C# half — the same channel as
   * {@link invokeDotNet}, with the result and any failure (including the opt-out
   * throw) swallowed. Because it never returns a promise to await, it is safe
   * anywhere, command handlers and `editor.update()` included; use it whenever you
   * are telling .NET something rather than asking it.
   */
  notifyDotNet(method: string, ...args: unknown[]): void;

  /**
   * Whether the C# extension accepts JS→.NET calls — i.e. whether
   * {@link invokeDotNet} will work. False means this instance must stay entirely
   * client-side.
   */
  canInvokeDotNet: boolean;

  /**
   * The update tag that marks an edit as **silent**: the host's content-changed push
   * skips any update carrying it, so an app-driven touch-up (re-resolving a stale
   * display name, say) never marks the document dirty. Pair it with Lexical's
   * `'history-merge'` so the edit adds no undo step either:
   *
   * ```js
   * editor.update(() => { ... }, { tag: [setup.silentUpdateTag, 'history-merge'] });
   * ```
   *
   * Reserve it for updates the user did not make — a silent user edit would be lost.
   */
  silentUpdateTag: string;
}

/** The live editor + DOM, handed to {@link LexicalExtensionModule.register}. */
export interface LexicalExtensionContext {
  /** The editor instance, already created and registered with rich-text/history. */
  editor: LexicalEditor;
  /** The editor's outer root element — the wrapper that also holds toolbar chrome. */
  root: HTMLElement;
  /** The `[data-lexical-content]` contenteditable surface Lexical is bound to. */
  content: HTMLElement;
}

/** What an extension's factory returns. Every member is optional. */
export interface LexicalExtensionModule {
  /**
   * Custom node classes to register with `createEditor`. Read once, before the
   * editor is created — nodes cannot be added later.
   */
  nodes?: ReadonlyArray<Klass<LexicalNode>>;

  /**
   * Theme fragment for the extension's own nodes, merged into the editor's
   * `EditorThemeClasses` before it is created (the only window there is). The value of
   * a key is what Lexical hands your node's `createDOM(config)` as
   * `config.theme.<key>` — usually a class name string.
   *
   * **Namespace your keys to your extension** (`badge`, `badgeSelected`) and never
   * touch a core one (`paragraph`, `heading`, `link`, …). The host wins every
   * collision — its `Theme` is applied over these fragments — so a core key here is
   * silently ignored at best and fights another extension at worst.
   */
  theme?: Record<string, unknown>;

  /**
   * Wires the extension's runtime: Lexical commands/listeners on `ctx.editor` and/or
   * DOM listeners scoped to the extension's own markers under `ctx.root`. Called once
   * after the editor and the core plugins are registered. Return a teardown function
   * to be run when the editor is disposed.
   */
  register?(ctx: LexicalExtensionContext): (() => void) | void;

  /**
   * Handles a call from the extension's C# half (`LexicalExtension.InvokeJsAsync`).
   * `args` is the parsed JSON argument array. The result is JSON-serialized back to
   * .NET. May be async.
   */
  invoke?(method: string, args: unknown[]): unknown;
}

/**
 * The default export of an extension module: a factory called once per editor with
 * that instance's {@link LexicalExtensionSetup}.
 */
export type LexicalExtensionFactory = (
  setup: LexicalExtensionSetup,
) => LexicalExtensionModule;

/** One extension descriptor passed from C#. Mirrors the C# `ExtensionDescriptorDto`. */
export interface ExtensionDescriptorDto {
  /** Stable extension id; routes calls in both directions. */
  id: string;
  /**
   * Names a module bundled into the host itself ('table' | 'mentions') — the built-in
   * tier of this same contract, resolved by a literal import() rather than by URL.
   * Absent for everything a consumer writes; wins over `moduleUrl` when both are set.
   */
  builtIn?: string | null;
  /** URL of the extension's ES module, or null/absent for a C#-only extension. */
  moduleUrl?: string | null;
  /** Opaque per-instance options from the C# `GetOptions()`. */
  options?: unknown;
  /** Whether the C# half accepts JS→.NET calls (gates `invokeDotNet`). */
  hasInvokeHandler?: boolean;
}
