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

  /**
   * App-agnostic building blocks the SDK owns because they are easy to get subtly
   * wrong per-app. Unlike {@link lexical}/{@link utils} (which hand you a namespace to
   * call), these are stateless factories: {@link GhostCompletionApi.attach} takes the
   * editor as an argument and {@link EntityCommitApi.create} is editor-free, so one
   * `primitives` object is shared across every extension on the page. See
   * {@link LexicalPrimitives}.
   */
  primitives: LexicalPrimitives;
}

/**
 * The SDK-owned primitives handed to every extension via {@link LexicalExtensionSetup.primitives}.
 * Each generalizes a pattern the built-ins already solve internally but consumers keep
 * re-implementing: an inline completion hint that must never touch the document, and the
 * buffer→resolve→commit lifecycle of a field whose whole content is a reference to an entity.
 */
export interface LexicalPrimitives {
  /** Inline ghost-completion overlay. See {@link GhostCompletionApi}. */
  ghost: GhostCompletionApi;
  /** The entity-commit state machine. See {@link EntityCommitApi}. */
  entityCommit: EntityCommitApi;
}

/**
 * One reading of what the ghost should show, returned by the `read` callback an
 * extension hands {@link GhostCompletionApi.attach}. `null` from that callback hides the
 * ghost.
 */
export interface GhostSession {
  /** Key of the text node the caret sits in — the ghost's anchor. */
  anchorKey: string;
  /** The muted suffix to paint after the caret (e.g. `ken stock` for `chic│`). */
  text: string;
}

/**
 * Inline ghost completion: muted "rest of the word" text shown *after* the caret
 * (`chi│cken stock` with the suffix dimmed), the way an autocomplete hint reads.
 *
 * The hard invariant is that the ghost is **visible but not real**. `attach` paints it
 * into an overlay element parented to `root` — *outside* the `[data-lexical-content]`
 * contenteditable — so it can never enter the document, the state JSON, the history/undo
 * stack, or `getTextContent()`. Accepting a completion is the extension's own job (insert
 * the real text through `editor.update()` like any edit); the primitive only renders and
 * positions the hint.
 */
export interface GhostCompletionApi {
  /**
   * Starts painting a ghost for `editor`. `read` is run inside the editor's read context
   * on every selection/content change and must return the {@link GhostSession} to show
   * (or `null` to hide). The overlay is created under `root` and positioned at the
   * caret's right edge. Returns a teardown that removes the overlay and every listener.
   */
  attach(
    editor: LexicalEditor,
    root: HTMLElement,
    read: () => GhostSession | null,
  ): () => void;
}

/**
 * One selectable entity in an {@link EntityCommitApi} field: a stable `id` and the `text`
 * that both displays it and is matched against the query. Extensions extend this with
 * their own fields (`T extends EntityCandidate`).
 */
export interface EntityCandidate {
  /** Stable identifier — what a commit ultimately resolves to. */
  id: string;
  /** Display/match text (the query is prefix-matched against this by default). */
  text: string;
}

/** The snapshot {@link EntityCommitController.current} returns for the live query. */
export interface EntityCommitState<T extends EntityCandidate> {
  /** The query as last set via {@link EntityCommitController.setQuery}. */
  query: string;
  /** The active match (the one a `commit()` would take), or `null` for none. */
  best: T | null;
  /** The other matches, in candidate order — what {@link EntityCommitController.cycle} steps through. */
  alternates: T[];
}

/** Configuration for one {@link EntityCommitApi.create} field. */
export interface EntityCommitOptions<T extends EntityCandidate> {
  /** The current candidate pool. Read fresh on every query, so it may change over time. */
  candidates: () => T[];
  /**
   * Whether `candidate` matches `query`. Defaults to a case-insensitive prefix test on
   * `candidate.text`.
   */
  match?: (query: string, candidate: T) => boolean;
  /**
   * Mints a brand-new entity when the query matches nothing — the create-if-missing path.
   * Absent ⇒ a no-match commit is simply a no-op. See {@link commit} on
   * {@link EntityCommitController} for the optimistic lifecycle this drives.
   */
  createIfMissing?: (query: string) => Promise<T>;
  /**
   * Fired synchronously when a commit resolves to an entity. `created` is true for the
   * create-if-missing path; `provisional` is true only for the optimistic placeholder
   * fired *before* `createIfMissing` resolves — a later {@link onResolved} swaps it for
   * the real entity.
   */
  onCommit: (entity: T, info: { created: boolean; provisional: boolean }) => void;
  /**
   * Fired when a `createIfMissing` promise resolves, carrying the provisional id first
   * handed to {@link onCommit} and the real entity to swap in. Never fired after
   * {@link EntityCommitController.dispose}.
   */
  onResolved?: (provisionalId: string, entity: T) => void;
  /**
   * Fired when a `createIfMissing` promise rejects. The provisional entity is
   * **deliberately left in place** — a half-committed field the user has moved on from is
   * worse than a provisional that never resolved — so use this to surface the failure,
   * not to roll back. Never fired after {@link EntityCommitController.dispose}.
   */
  onError?: (query: string, error: unknown) => void;
}

/** Drives one entity-commit field. Created by {@link EntityCommitApi.create}. */
export interface EntityCommitController<T extends EntityCandidate> {
  /** Sets the query (typically the field's current text) and resets the active match to the first. */
  setQuery(query: string): void;
  /** The current {@link EntityCommitState} — matches recomputed from the live candidates. */
  current(): EntityCommitState<T>;
  /**
   * Advances the active match (wrapping) over the current matches, so `current().best` and
   * a subsequent {@link commit} follow the cycled selection — the mechanism behind a
   * dropdown-less "press ↓ to try the next match".
   */
  cycle(): void;
  /**
   * Commits the active match:
   * - empty query, or no match and no `createIfMissing` ⇒ **no-op**;
   * - a match ⇒ `onCommit(match, { created: false, provisional: false })`;
   * - no match **with** `createIfMissing` ⇒ **optimistic**: `onCommit` fires immediately
   *   with a provisional entity (`{ created: true, provisional: true }`), then
   *   `createIfMissing(query)` is awaited and `onResolved` (or `onError`) fires. The
   *   interactive path never awaits.
   */
  commit(): void;
  /** Tears the field down; late `createIfMissing` settlements after this are ignored. */
  dispose(): void;
}

/**
 * The entity-commit lifecycle: the `buffer → resolve → commit → create-if-missing` state
 * machine behind a **typed reference field** — an editable region whose whole content is
 * conceptually a reference to an entity (a city, a tag, a person) rather than free text.
 *
 * It generalizes the built-in mentions (which insert a token for an *existing* entity) to
 * a field that *is* the reference, and adds the create-if-missing path mentions lacks. It
 * is DOM-free and editor-free — pure logic over a candidate list — so the same primitive
 * drives a field, a chip, or anything else; wiring it to the editor is the extension's job.
 */
export interface EntityCommitApi {
  /** Builds a controller for one field from its {@link EntityCommitOptions}. */
  create<T extends EntityCandidate>(opts: EntityCommitOptions<T>): EntityCommitController<T>;
}

/**
 * Where something can be anchored relative to a block. Today, the four margins a
 * `<LexicalBlockGutter>` rail can occupy; the union is the extension point for future
 * spots (e.g. `'above' | 'below'`) without changing {@link LexicalBlockLayout.anchor}.
 */
export type LexicalBlockAnchor =
  | 'left-inside'
  | 'left-outside'
  | 'right-inside'
  | 'right-outside';

/** One top-level block, as reported by {@link LexicalBlockLayout.blocks}. */
export interface LexicalBlockInfo {
  /** The top-level node's key. */
  key: string;
  /** The block's DOM element (a direct child of the content surface). */
  element: HTMLElement;
  /** Zero-based position among the content surface's children. */
  index: number;
  /** `tagName.toLowerCase()`, the same convention as the hover push's `blockType`. */
  type: string;
}

/**
 * Per-block positioning: the two pieces of geometry the built-in block gutter uses,
 * surfaced so an extension can place its own persistent, app-styled, app-stateful
 * decorations beside blocks. Positioning only — the decorations, their styling, their
 * click handling and any persistence are the extension's own.
 */
export interface LexicalBlockLayout {
  /**
   * Every top-level block in document order. A live snapshot — call it fresh each time
   * you reposition, don't cache it.
   */
  blocks(): LexicalBlockInfo[];

  /**
   * Root-relative CSS offset for an element of `sizePx` at `spot`, honoring the same
   * content-padding / inside-clamp / outside-hangs-off-the-card math the built-in block
   * gutter uses. Pass `consumedPx` (the accumulated size of anything already stacked at
   * this spot for this block) to place a further item outward. For the current
   * horizontal spots this is a CSS `left`; the axis is implied by `spot`.
   */
  anchor(spot: LexicalBlockAnchor, sizePx: number, consumedPx?: number): number;

  /**
   * Runs `callback` after a structural/content change (coalesced to one call per
   * animation frame) and on window resize — the two things that can move a block.
   * Deliberately not wired to scroll: a block and the root share one scroll container,
   * so a block's position relative to root is scroll-invariant; only reflow moves it.
   * Returns a teardown.
   */
  onBlocksChanged(callback: () => void): () => void;
}

/** The live editor + DOM, handed to {@link LexicalExtensionModule.register}. */
export interface LexicalExtensionContext {
  /** The editor instance, already created and registered with rich-text/history. */
  editor: LexicalEditor;
  /** The editor's outer root element — the wrapper that also holds toolbar chrome. */
  root: HTMLElement;
  /** The `[data-lexical-content]` contenteditable surface Lexical is bound to. */
  content: HTMLElement;
  /** Per-block positioning primitives — see {@link LexicalBlockLayout}. */
  blockLayout: LexicalBlockLayout;
}

/** What an extension's factory returns. Every member is optional. */
export interface LexicalExtensionModule {
  /**
   * A unique name for this module, conventionally namespaced to its author
   * (`acme/badge`; the built-ins use `blazor-lexical/…`). Optional, but naming your
   * module is what lets the host report a collision against something other than a
   * URL — and what makes {@link conflictsWith} usable by anyone else.
   *
   * Two loaded modules claiming the same name is an authoring error: the second is
   * logged and skipped.
   *
   * Same field, same meaning as `name` on Lexical's own `defineExtension`.
   */
  name?: string;

  /**
   * Names of modules that must not be loaded alongside this one — for extensions
   * whose functionality overlaps destructively (two that both bind the same key, or
   * both claim the same node type).
   *
   * Same field, same meaning as `conflictsWith` on Lexical's own `defineExtension`,
   * with one deliberate difference: upstream refuses to build the editor at all,
   * whereas here the *later* of the two modules is logged and skipped and the editor
   * comes up regardless. A broken extension never takes the editor down.
   */
  conflictsWith?: ReadonlyArray<string>;

  /**
   * Custom node classes to register with `createEditor`. Read once, before the
   * editor is created — nodes cannot be added later.
   *
   * May be a thunk (`() => [MyNode]`) as well as a plain array, matching Lexical's
   * own `nodes` field, so a node list copied from an upstream extension works as-is.
   *
   * A node whose `getType()` is already claimed — by a core node or by an earlier
   * extension — is a collision the editor cannot resolve, so the offending module is
   * logged and skipped rather than left to fail inside `createEditor`.
   */
  nodes?: ReadonlyArray<Klass<LexicalNode>> | (() => ReadonlyArray<Klass<LexicalNode>>);

  /**
   * Theme fragment for the extension's own nodes, merged into the editor's
   * `EditorThemeClasses` before it is created (the only window there is). The value of
   * a key is what Lexical hands your node's `createDOM(config)` as
   * `config.theme.<key>` — usually a class name string.
   *
   * **Namespace your keys to your extension** (`badge`, `badgeSelected`) and never
   * touch a core one (`paragraph`, `heading`, `link`, …). The host wins every
   * collision — its `Theme` is applied over these fragments — so a core key here is
   * silently ignored at best and fights another extension at worst. A key already
   * claimed by *another extension* is warned about, since neither of them can be the
   * intended owner.
   *
   * Fragments are merged **deeply**, so contributing `{ heading: { h7: '…' } }` adds
   * to a nested group rather than replacing it.
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
