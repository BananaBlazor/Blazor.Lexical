import {
  createEditor,
  $getRoot,
  $getSelection,
  $isRangeSelection,
  $isElementNode,
  $insertNodes,
  $createParagraphNode,
  $createTextNode,
  FORMAT_TEXT_COMMAND,
  FORMAT_ELEMENT_COMMAND,
  UNDO_COMMAND,
  REDO_COMMAND,
  CAN_UNDO_COMMAND,
  CAN_REDO_COMMAND,
  COMMAND_PRIORITY_LOW,
  type EditorThemeClasses,
  type Klass,
  type LexicalEditor,
  type LexicalNode,
  type TextFormatType,
  type ElementFormatType,
} from 'lexical';
import {
  registerRichText,
  HeadingNode,
  QuoteNode,
  $createHeadingNode,
  $createQuoteNode,
  $isHeadingNode,
  $isQuoteNode,
  type HeadingTagType,
} from '@lexical/rich-text';
import { registerHistory, createEmptyHistoryState } from '@lexical/history';
import {
  ListNode,
  ListItemNode,
  registerList,
  $isListNode,
  INSERT_ORDERED_LIST_COMMAND,
  INSERT_UNORDERED_LIST_COMMAND,
  REMOVE_LIST_COMMAND,
} from '@lexical/list';
import { LinkNode, AutoLinkNode, $isLinkNode, $toggleLink } from '@lexical/link';
import { $generateHtmlFromNodes, $generateNodesFromDOM } from '@lexical/html';
import { $setBlocksType } from '@lexical/selection';
import { mergeRegister, $getNearestNodeOfType, $findMatchingParent } from '@lexical/utils';
// Also kept as a namespace: extensions are handed it as `setup.utils`, so they get
// mergeRegister and the $-helpers from the host's copy instead of bundling their own.
import * as lexicalUtils from '@lexical/utils';
import {
  registerFloatingToolbar,
  registerSlashMenu,
  registerBlockGutters,
  registerLinkEditor,
  OPEN_LINK_EDITOR_COMMAND,
} from './overlays';
// The mentions feature (custom nodes + typeahead + @lexical/text) ships in core: it
// measured ~4kb gzipped, so like the light overlays above (and unlike the heavy table
// chunk) it is imported statically and simply activated when an editor declares
// configs. Written as a self-contained module, so making it a lazy chunk later is a
// one-line change (swap this for a dynamic import, mirroring './table').
import * as mentionsRuntime from './mentions';
// The same static-in-core tier as mentions, for the same reason: each of these is
// well under a couple of kilobytes and needs nothing that isn't already bundled
// (toc/stats: `lexical` + `@lexical/rich-text`; marks: `@lexical/mark`, ~2kb and
// node-registering). Making any of them a lazy chunk later is a one-line change
// here — swap the static import for a literal `import()`, mirroring './table'.
import tocRuntime from './toc';
import marksRuntime from './marks';
import statsRuntime from './stats';
import hrRuntime, { INSERT_HORIZONTAL_RULE_COMMAND } from './hr';
import tabIndentRuntime from './tabindent';
// Consumer extension contract — types only (see extension.ts), so this import is
// erased and the external modules stay entirely outside our bundle.
import type {
  ExtensionDescriptorDto,
  LexicalExtensionFactory,
  LexicalExtensionModule,
  LexicalExtensionSetup,
} from './extension';

// The table feature (nodes + @lexical/table, ~90kb) is a lazily-imported chunk —
// `./table` is never imported statically, so esbuild code-splits it out of the core
// bundle. It is fetched only when an editor enables the 'table' feature (see create).
// The type is referenced ambiently so the dynamic module stays fully type-checked.
type TableModule = typeof import('./table');

// The silent-update tag: the content-changed push below skips any update carrying it,
// so an app-driven touch-up never marks the document dirty. The mention refresh is the
// built-in user; extensions reach the same tag through `setup.silentUpdateTag`.
import { SILENT_UPDATE_TAG } from './tags';

// The nodes every editor registers, so HTML/Markdown round-trips carry headings,
// quotes, lists and links — not just plain paragraphs. Everything else, the table,
// mention and horizontal-rule nodes included, arrives through the extension contract.
// Named here (rather than inline in createEditor) because the extension loader also
// needs their types, to catch an extension claiming one of them.
const CORE_NODES: ReadonlyArray<Klass<LexicalNode>> = [
  HeadingNode,
  QuoteNode,
  ListNode,
  ListItemNode,
  LinkNode,
  AutoLinkNode,
];

// Keys that must never be merged: assigning or recursing through them can mutate the
// prototype chain. Theme fragments can originate in JSON (the C# `Theme` travels as
// one), and `JSON.parse('{"__proto__": {…}}')` would otherwise leak into
// Object.prototype and affect every object on the page.
const UNSAFE_THEME_KEYS = new Set(['__proto__', 'constructor', 'prototype']);

/**
 * Recursively merges theme `source` into `target` in place, `source` winning on leaf
 * collisions. Mirrors the semantics of `deepThemeMergeInPlace` in `@lexical/extension`
 * (which is internal to that package, hence reimplemented rather than imported).
 *
 * Deep rather than `Object.assign` so that contributing one key of a nested group —
 * `{ heading: { h1 } }` over `{ heading: { h1, h2 } }` — adds to the group instead of
 * replacing it, which a shallow spread would do silently.
 */
function deepThemeMerge(target: unknown, source: unknown): unknown {
  if (
    target !== null &&
    source !== null &&
    typeof target === 'object' &&
    typeof source === 'object' &&
    !Array.isArray(source)
  ) {
    const targetObj = target as Record<string, unknown>;
    const sourceObj = source as Record<string, unknown>;
    for (const key in sourceObj) {
      if (UNSAFE_THEME_KEYS.has(key) || !Object.prototype.hasOwnProperty.call(sourceObj, key)) {
        continue;
      }
      targetObj[key] = deepThemeMerge(targetObj[key], sourceObj[key]);
    }
    return target;
  }
  return source;
}

/** A .NET object reference that can receive [JSInvokable] callbacks. */
interface DotNetObjectReference {
  invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** Which JS→.NET push channels are active. Both off ⇒ no interop at all. */
interface NotifyFlags {
  /** Push debounced plain text on change (OnContentChangedInternal). */
  content: boolean;
  /**
   * What the content push carries: the document in the subscriber's chosen format, or
   * nothing but the signal that it changed (an empty string) for subscribers that only
   * need a dirty flag. Serializing here — inside the debounce — is what keeps the
   * channel to one crossing: a host that wants HTML gets HTML, instead of plain text
   * it discards followed by a getHtml round trip.
   */
  contentPayload: 'signalOnly' | 'text' | 'html' | 'markdown' | 'stateJson';
  /** Push selection formatting state (OnSelectionChangedInternal). */
  selection: boolean;
  /**
   * Push the hovered top-level block (OnBlockHoveredInternal). Armed only when a
   * <LexicalBlockGutter> wired OnBlockHovered — the gutter is otherwise pure JS.
   * Deduped by node key, so it is one crossing per block change, not per mousemove.
   */
  blockHover: boolean;
}

/** The editor's initial content, applied inside create(). */
interface InitialContentDto {
  /** 'text' | 'html' | 'markdown' | 'stateJson'. */
  format: string;
  value: string;
}

/** Options passed from the C# component. Kept open for future feature keys. */
interface CreateOptions {
  namespace?: string;
  theme?: EditorThemeClasses;
  readOnly?: boolean;
  enableHistory?: boolean;
  /** Which .NET push channels to enable; omitted ⇒ both off (pure JS). */
  notify?: Partial<NotifyFlags>;
  /**
   * Every extension this editor runs, in load order: the library's own features
   * (`builtIn: 'table' | 'mentions'`) first, then the consumer extensions
   * (`moduleUrl`). One list, because both tiers speak the same module contract and
   * differ only in how their JS is bundled.
   */
  extensions?: ExtensionDescriptorDto[];
  /** Content to load as the editor is created; omitted ⇒ an empty document. */
  initialContent?: InitialContentDto;
}

interface Instance {
  editor: LexicalEditor;
  /** The lazily-loaded table module, present only when the 'table' feature is on. */
  table?: TableModule;
  /** The mentions runtime, present only when the editor declares mention configs. */
  mentions?: typeof mentionsRuntime;
  /** Loaded consumer extension modules, keyed by extension id (for invokeExtension). */
  extensions: Map<string, LexicalExtensionModule>;
  /** Flips the .NET push channels on/off after create (opt-in at runtime). */
  setNotify(flags: Partial<NotifyFlags>): void;
  dispose(): void;
}

const instances = new Map<string, Instance>();

const CHANGE_DEBOUNCE_MS = 200;

/**
 * Compact snapshot of the current selection's formatting, pushed to .NET so a
 * toolbar can reflect active/enabled state. Mirrors the C# `LexicalSelectionState`.
 */
interface SelectionStateDto {
  isBold: boolean;
  isItalic: boolean;
  isUnderline: boolean;
  isStrikethrough: boolean;
  isCode: boolean;
  isSubscript: boolean;
  isSuperscript: boolean;
  isLowercase: boolean;
  isUppercase: boolean;
  /** 'paragraph' | 'h1'..'h6' | 'quote' | 'bullet' | 'number'. */
  blockType: string;
  isLink: boolean;
  /** '' (unset) | 'left' | 'center' | 'right' | 'justify'. */
  alignment: string;
  canUndo: boolean;
  canRedo: boolean;
  /** The selected text; '' when the selection is collapsed or not a range. */
  text: string;
}

/** Creates the block-level node for a `setBlockType` target token. */
function createBlockNode(type: string) {
  switch (type) {
    case 'h1':
    case 'h2':
    case 'h3':
    case 'h4':
    case 'h5':
    case 'h6':
      return $createHeadingNode(type as HeadingTagType);
    case 'quote':
      return $createQuoteNode();
    default:
      return $createParagraphNode();
  }
}

/**
 * Reads the current selection's formatting into a {@link SelectionStateDto}.
 * Must run inside an editor read/update context. `canUndo`/`canRedo` are tracked
 * separately via the history command listeners and passed in.
 */
function readSelectionState(canUndo: boolean, canRedo: boolean): SelectionStateDto {
  const state: SelectionStateDto = {
    isBold: false,
    isItalic: false,
    isUnderline: false,
    isStrikethrough: false,
    isCode: false,
    isSubscript: false,
    isSuperscript: false,
    isLowercase: false,
    isUppercase: false,
    blockType: 'paragraph',
    isLink: false,
    alignment: '',
    canUndo,
    canRedo,
    text: '',
  };

  const selection = $getSelection();
  if (!$isRangeSelection(selection)) {
    return state;
  }

  state.isBold = selection.hasFormat('bold');
  state.isItalic = selection.hasFormat('italic');
  state.isUnderline = selection.hasFormat('underline');
  state.isStrikethrough = selection.hasFormat('strikethrough');
  state.isCode = selection.hasFormat('code');
  state.isSubscript = selection.hasFormat('subscript');
  state.isSuperscript = selection.hasFormat('superscript');
  state.isLowercase = selection.hasFormat('lowercase');
  state.isUppercase = selection.hasFormat('uppercase');

  const anchorNode = selection.anchor.getNode();
  const element =
    anchorNode.getKey() === 'root' ? anchorNode : anchorNode.getTopLevelElementOrThrow();

  if ($isListNode(element)) {
    const parentList = $getNearestNodeOfType(anchorNode, ListNode);
    const listType = (parentList ?? element).getListType();
    state.blockType = listType === 'number' ? 'number' : 'bullet';
  } else if ($isHeadingNode(element)) {
    state.blockType = element.getTag();
  } else if ($isQuoteNode(element)) {
    state.blockType = 'quote';
  } else {
    state.blockType = 'paragraph';
  }

  // `getFormatType()` returns '' when the block has no explicit alignment.
  state.alignment = $isElementNode(element) ? element.getFormatType() : '';

  const linkNode = $findMatchingParent(anchorNode, $isLinkNode);
  state.isLink = linkNode !== null || $isLinkNode(anchorNode);

  // The selected text, so an app button (a "comment on this" affordance) can store the
  // quote it acts on. Skipped while collapsed — that is the typing case, and this runs
  // on every update — so caret movement costs nothing extra.
  state.text = selection.isCollapsed() ? '' : selection.getTextContent();

  return state;
}

// ---------------------------------------------------------------------------
// Editor-level command helpers. Shared by the exported functions (opt-in C#
// calls) and the delegated toolbar dispatcher, so both paths run identical code.
// ---------------------------------------------------------------------------

/** Converts the blocks at the selection to a heading/quote/paragraph token. */
function applyBlockType(editor: LexicalEditor, type: string): void {
  editor.update(() => {
    const selection = $getSelection();
    if (!$isRangeSelection(selection)) {
      return;
    }
    // Unwrap any list first so $setBlocksType has plain blocks to convert.
    const anchorNode = selection.anchor.getNode();
    const inList =
      $getNearestNodeOfType(anchorNode, ListNode) !== null ||
      (anchorNode.getKey() !== 'root' && $isListNode(anchorNode.getTopLevelElementOrThrow()));
    if (inList) {
      editor.dispatchCommand(REMOVE_LIST_COMMAND, undefined);
    }
    $setBlocksType(selection, () => createBlockNode(type));
  });
}

/** Clears any active inline text formats over the current selection. */
function clearFormattingOnEditor(editor: LexicalEditor): void {
  editor.update(() => {
    const selection = $getSelection();
    if (!$isRangeSelection(selection)) {
      return;
    }
    const formats: TextFormatType[] = [
      'bold',
      'italic',
      'underline',
      'strikethrough',
      'code',
      'subscript',
      'superscript',
    ];
    for (const format of formats) {
      if (selection.hasFormat(format)) {
        selection.formatText(format);
      }
    }
  });
}

/** Wraps/updates (non-empty url) or unwraps (null) a link over the selection. */
function toggleLinkOnEditor(editor: LexicalEditor, url: string | null): void {
  editor.update(() => {
    $toggleLink(url ? url : null);
  });
}

// ---------------------------------------------------------------------------
// Toolbar wiring. Blazor renders buttons tagged data-lexical-command; JS owns
// the click→command dispatch and the active/disabled DOM state. No interop.
// ---------------------------------------------------------------------------

/** Whether `format` (a data-lexical-command arg) is active in `state`. */
function stateHasFormat(state: SelectionStateDto, format: string): boolean {
  switch (format) {
    case 'bold':
      return state.isBold;
    case 'italic':
      return state.isItalic;
    case 'underline':
      return state.isUnderline;
    case 'strikethrough':
      return state.isStrikethrough;
    case 'code':
      return state.isCode;
    case 'subscript':
      return state.isSubscript;
    case 'superscript':
      return state.isSuperscript;
    case 'lowercase':
      return state.isLowercase;
    case 'uppercase':
      return state.isUppercase;
    default:
      return false;
  }
}

/**
 * Whether a `data-lexical-command` token is "active" for the given state, or
 * null when the command has no active concept (history, clear-formatting).
 */
function isCommandActive(token: string, state: SelectionStateDto): boolean | null {
  const [type, arg] = token.split(':');
  switch (type) {
    case 'format':
      return stateHasFormat(state, arg);
    case 'block':
      return arg === 'select' ? null : state.blockType === arg;
    case 'list':
      if (arg === 'bullet') return state.blockType === 'bullet';
      if (arg === 'number') return state.blockType === 'number';
      return null;
    case 'align':
      return state.alignment === arg;
    case 'link':
      return state.isLink;
    default:
      return null;
  }
}

function setElDisabled(el: Element, disabled: boolean): void {
  el.toggleAttribute('data-lexical-disabled', disabled);
  if (disabled) {
    el.setAttribute('aria-disabled', 'true');
  } else {
    el.removeAttribute('aria-disabled');
  }
}

/** Reflects the selection state onto the toolbar DOM under `root`. No interop. */
function updateToolbarDom(root: HTMLElement, state: SelectionStateDto): void {
  root.querySelectorAll('[data-lexical-command]').forEach((el) => {
    const token = el.getAttribute('data-lexical-command')!;
    const active = isCommandActive(token, state);
    if (active !== null) {
      el.toggleAttribute('data-lexical-active', active);
    }
  });

  root
    .querySelectorAll('[data-lexical-command="history:undo"]')
    .forEach((el) => setElDisabled(el, !state.canUndo));
  root
    .querySelectorAll('[data-lexical-command="history:redo"]')
    .forEach((el) => setElDisabled(el, !state.canRedo));

  const select = root.querySelector<HTMLSelectElement>('[data-lexical-command="block:select"]');
  if (select && Array.from(select.options).some((o) => o.value === state.blockType)) {
    select.value = state.blockType;
  }
}

/**
 * Runs a `data-lexical-command` token against the editor. `table` tokens need the
 * lazily-loaded {@link TableModule}; when it isn't loaded (the editor didn't opt into
 * the table feature) a `table:` token is simply a no-op.
 */
function runCommandToken(
  editor: LexicalEditor,
  token: string,
  tableModule?: TableModule,
): void {
  const [type, arg] = token.split(':');
  switch (type) {
    case 'format':
      editor.dispatchCommand(FORMAT_TEXT_COMMAND, arg as TextFormatType);
      break;
    case 'align':
      editor.dispatchCommand(FORMAT_ELEMENT_COMMAND, (arg ?? '') as ElementFormatType);
      break;
    case 'block':
      applyBlockType(editor, arg);
      break;
    case 'list': {
      if (arg === 'remove') {
        editor.dispatchCommand(REMOVE_LIST_COMMAND, undefined);
        break;
      }
      // Toggle: clicking the active list type removes it, matching a toolbar toggle.
      const current = editor.getEditorState().read(() => readSelectionState(false, false).blockType);
      const target = arg === 'number' ? 'number' : 'bullet';
      if (current === target) {
        editor.dispatchCommand(REMOVE_LIST_COMMAND, undefined);
      } else {
        editor.dispatchCommand(
          arg === 'number' ? INSERT_ORDERED_LIST_COMMAND : INSERT_UNORDERED_LIST_COMMAND,
          undefined,
        );
      }
      break;
    }
    case 'history':
      editor.dispatchCommand(arg === 'redo' ? REDO_COMMAND : UNDO_COMMAND, undefined);
      break;
    case 'link': {
      if (arg === 'remove') {
        toggleLinkOnEditor(editor, null);
        break;
      }
      // link:toggle (playground-style): unwrap when the selection is already in a
      // link, otherwise insert a placeholder link and ask the floating link editor
      // (if the host placed one) to open on it so the URL can be typed.
      const isLink = editor.getEditorState().read(() => {
        const selection = $getSelection();
        if (!$isRangeSelection(selection)) {
          return false;
        }
        const node = selection.anchor.getNode();
        return $isLinkNode(node) || $findMatchingParent(node, $isLinkNode) !== null;
      });
      if (isLink) {
        toggleLinkOnEditor(editor, null);
      } else {
        toggleLinkOnEditor(editor, 'https://');
        editor.dispatchCommand(OPEN_LINK_EDITOR_COMMAND, undefined);
      }
      break;
    }
    case 'table': {
      // arg is an optional "RxC" size (e.g. "3x4"); default 3×3. The toolbar
      // picker inserts custom sizes directly, so "table:insert" here just means
      // the default (used by the slash menu item). No-op without the table chunk.
      if (!tableModule) {
        break;
      }
      const match = /^(\d+)x(\d+)$/.exec(arg ?? '');
      const rows = match ? Number(match[1]) : 3;
      const cols = match ? Number(match[2]) : 3;
      tableModule.insertTableWithDimensions(editor, rows, cols);
      break;
    }
    case 'hr':
      // No module handle needed, unlike `table:`: with the horizontal-rule extension
      // not declared nothing has registered a handler and the dispatch is a no-op.
      editor.dispatchCommand(INSERT_HORIZONTAL_RULE_COMMAND, undefined);
      break;
    case 'clear-formatting':
      clearFormattingOnEditor(editor);
      break;
  }
}

/**
 * Builds the `invokeDotNet` an extension's JS half is handed. Gated on the C# side's
 * opt-in (`HasInvokeHandler`): without it this throws rather than calling into .NET,
 * so an extension whose consumer wired no handler performs zero interop. Arguments
 * and the result cross as JSON, matching the C# `OnInvokeAsync(method, argsJson)`.
 */
function makeInvokeDotNet(
  dotNetRef: DotNetObjectReference,
  desc: ExtensionDescriptorDto,
): LexicalExtensionSetup['invokeDotNet'] {
  return async function invokeDotNet<T = unknown>(
    method: string,
    ...args: unknown[]
  ): Promise<T | undefined> {
    if (desc.hasInvokeHandler !== true) {
      throw new Error(
        `[Blazor.Lexical] extension '${desc.id}' has no .NET invoke handler; ` +
          'override LexicalExtension.OnInvokeAsync to opt in.',
      );
    }
    const json = await dotNetRef.invokeMethodAsync(
      'InvokeExtensionAsync',
      desc.id,
      method,
      JSON.stringify(args),
    );
    return typeof json === 'string' && json.length > 0 ? (JSON.parse(json) as T) : undefined;
  };
}

/**
 * Builds the `notifyDotNet` an extension's JS half is handed: the same call as
 * `invokeDotNet`, minus the promise. Because it returns nothing to await and swallows
 * every failure — the opt-out throw included — it is safe on the interactive path,
 * which `invokeDotNet` is not (invariant #5: JS never blocks on .NET). Use it to tell
 * .NET something rather than to ask it.
 */
function makeNotifyDotNet(
  invokeDotNet: LexicalExtensionSetup['invokeDotNet'],
): LexicalExtensionSetup['notifyDotNet'] {
  return function notifyDotNet(method: string, ...args: unknown[]): void {
    try {
      void invokeDotNet(method, ...args).catch(() => {});
    } catch {
      // Not opted in (invokeDotNet throws synchronously). Fire-and-forget means the
      // caller asked not to care, so staying silent is the contract.
    }
  };
}

// --- Content application -------------------------------------------------
// Shared by the set*/get* exports and by create()'s initial-content step, so a
// document loads through exactly the same parse path whether it arrives as an
// Initial* parameter or a later Set*Async call.

/**
 * `editor.update` options. Initial content passes `discrete` so the state is
 * committed before create() registers history and the update listener — without it
 * the commit lands in the next microtask, i.e. *after* registration, and the
 * preloaded document would become an undoable step and a spurious content push.
 */
const DISCRETE = { discrete: true } as const;

/** Replaces the document with a single paragraph holding `text`. */
function applyText(editor: LexicalEditor, text: string, options?: typeof DISCRETE): void {
  editor.update(() => {
    const root = $getRoot();
    root.clear();
    const paragraph = $createParagraphNode();
    if (text) {
      paragraph.append($createTextNode(text));
    }
    root.append(paragraph);
  }, options);
}

/** Replaces the document with nodes parsed from an HTML string. */
function applyHtml(editor: LexicalEditor, html: string, options?: typeof DISCRETE): void {
  editor.update(() => {
    const dom = new DOMParser().parseFromString(html, 'text/html');
    const nodes = $generateNodesFromDOM(editor, dom);
    const root = $getRoot();
    root.clear();
    root.select();
    $insertNodes(nodes);
  }, options);
}

/** Restores the document from a canonical editor-state JSON string. */
function applyEditorStateJson(editor: LexicalEditor, json: string): void {
  editor.setEditorState(editor.parseEditorState(json));
}

/**
 * Serializes the document for the content push, in the format the subscriber declared.
 * Async only because Markdown lives in a lazily-loaded chunk — that import resolves
 * once and is cached by the module system, so it is not a per-tick cost.
 */
async function readContentPayload(
  editor: LexicalEditor,
  payload: NotifyFlags['contentPayload'],
): Promise<string> {
  switch (payload) {
    case 'signalOnly':
      return '';
    case 'html':
      return editor.getEditorState().read(() => $generateHtmlFromNodes(editor, null));
    case 'stateJson':
      return JSON.stringify(editor.getEditorState().toJSON());
    case 'markdown': {
      const md = await import('./markdown');
      return md.toMarkdown(editor);
    }
    default:
      return editor.getEditorState().read(() => $getRoot().getTextContent());
  }
}

/**
 * Loads the host's initial content. Called from create() before history is
 * registered, so the loaded document is the undo baseline rather than a step the
 * user can undo away — and before the update listener exists, so preloading never
 * fires the content channel. An unknown format is logged and skipped: bad initial
 * content must not cost the host its editor.
 */
async function applyInitialContent(
  editor: LexicalEditor,
  initial: InitialContentDto,
): Promise<void> {
  switch (initial.format) {
    case 'text':
      applyText(editor, initial.value, DISCRETE);
      return;
    case 'html':
      applyHtml(editor, initial.value, DISCRETE);
      return;
    case 'stateJson':
      // setEditorState commits the state itself, so no discrete flag is involved.
      applyEditorStateJson(editor, initial.value);
      return;
    case 'markdown': {
      // Same lazily-loaded chunk get/setMarkdown use — only editors that actually
      // start from Markdown pay for it.
      const md = await import('./markdown');
      md.fromMarkdown(editor, initial.value, DISCRETE);
      return;
    }
    default:
      console.error(`[Blazor.Lexical] unknown initial content format '${initial.format}'`);
  }
}

/**
 * Creates a Lexical editor inside the `root` wrapper (binding Lexical to the
 * `[data-lexical-content]` child), wires the delegated toolbar dispatcher, and
 * keeps toolbar/placeholder DOM state in sync. .NET is notified only for the
 * channels enabled in `options.notify` — with none enabled there is no interop.
 */
export async function create(
  instanceId: string,
  root: HTMLElement,
  dotNetRef: DotNetObjectReference,
  options: CreateOptions = {},
): Promise<void> {
  if (instances.has(instanceId)) {
    dispose(instanceId);
  }

  const contentEl = root.querySelector<HTMLElement>('[data-lexical-content]');
  if (!contentEl) {
    console.error('[Blazor.Lexical] no [data-lexical-content] element under root');
    return;
  }

  const notify: NotifyFlags = {
    content: options.notify?.content ?? false,
    contentPayload: options.notify?.contentPayload ?? 'text',
    selection: options.notify?.selection ?? false,
    blockHover: options.notify?.blockHover ?? false,
  };

  // Every feature and extension this editor runs, loaded through one contract before
  // createEditor — the only window in which custom node classes can be declared.
  // Built-ins resolve to a module in our own bundle, consumer extensions to a
  // separately-built ESM fetched by URL; from here down nothing else tells them apart.
  //
  // The two built-in modules are also kept by name, because a handful of core
  // call-sites need the *feature*, not the extension: the `table:` command token, the
  // insertTable/getMentions/refreshMention exports.
  let tableModule: TableModule | undefined;
  let mentionsModule: typeof mentionsRuntime | undefined;
  const extensionModules = new Map<string, LexicalExtensionModule>();
  const extensionNodes: Array<Klass<LexicalNode>> = [];
  const extensionTheme: Record<string, unknown> = {};
  // Bookkeeping for the three ways two extensions can collide. Each is an authoring
  // error we can name precisely here, and each would otherwise surface as something
  // far less obvious — a duplicate node type throws inside createEditor and takes the
  // whole editor down, and a duplicate theme key just silently wins.
  const claimedNames = new Map<string, string>();
  const claimedNodeTypes = new Map<string, string>(CORE_NODES.map((k) => [k.getType(), 'core']));
  const claimedThemeKeys = new Map<string, string>();
  const declaredConflicts: Array<{ owner: string; conflictsWith: ReadonlyArray<string> }> = [];
  const descriptors = (options.extensions ?? []).filter((d) => d.builtIn || d.moduleUrl);
  if (descriptors.length > 0) {
    // Hand extensions the host's own Lexical runtime: their node classes must extend
    // the same classes the editor registers, which a copy bundled into the extension
    // would not be. `lexical` is already in the bundle, so this resolves locally.
    const lexicalRuntime = await import('lexical');
    for (const desc of descriptors) {
      try {
        let factory: LexicalExtensionFactory | undefined;
        if (desc.builtIn === 'table') {
          // The only reference to './table' is dynamic and literal, which is what lets
          // esbuild split @lexical/table (~90kb) into its own chunk — editors that
          // never enable tables never download it.
          tableModule = await import('./table');
          factory = tableModule.default;
        } else if (desc.builtIn === 'mentions') {
          // Statically imported (it is ~4kb), so this costs no extra fetch.
          mentionsModule = mentionsRuntime;
          factory = mentionsRuntime.default;
        } else if (desc.builtIn === 'toc') {
          factory = tocRuntime;
        } else if (desc.builtIn === 'marks') {
          factory = marksRuntime;
        } else if (desc.builtIn === 'stats') {
          factory = statsRuntime;
        } else if (desc.builtIn === 'hr') {
          factory = hrRuntime;
        } else if (desc.builtIn === 'tabIndent') {
          factory = tabIndentRuntime;
        } else if (desc.builtIn) {
          console.error(`[Blazor.Lexical] unknown built-in extension '${desc.builtIn}'`);
          continue;
        } else {
          // A runtime value, so esbuild leaves this import() untouched (no chunk, no
          // bundling attempt). Resolve against the document base, not this module's
          // URL — a bare "./_content/X/y.mjs" is written the way Blazor asset paths
          // are, and a raw import() would resolve it relative to _content/Blazor.Lexical/.
          const moduleUrl = new URL(desc.moduleUrl!, document.baseURI).href;
          const imported: unknown = await import(/* @vite-ignore */ moduleUrl);
          factory = (imported as { default?: LexicalExtensionFactory }).default;
        }
        if (typeof factory !== 'function') {
          console.error(
            `[Blazor.Lexical] extension module '${desc.moduleUrl}' has no default export factory`,
          );
          continue;
        }
        const invokeDotNet = makeInvokeDotNet(dotNetRef, desc);
        const setup: LexicalExtensionSetup = {
          options: desc.options,
          lexical: lexicalRuntime,
          utils: lexicalUtils,
          invokeDotNet,
          notifyDotNet: makeNotifyDotNet(invokeDotNet),
          canInvokeDotNet: desc.hasInvokeHandler === true,
          silentUpdateTag: SILENT_UPDATE_TAG,
        };
        const module = factory(setup);
        const label = module.name ?? desc.builtIn ?? desc.moduleUrl ?? desc.id;

        // --- Collision checks, before anything of this module is accepted. ---
        // Each rejects the *later* module whole rather than merging half of it: a
        // module whose nodes were dropped would be live but broken, which is worse
        // than absent. Upstream (@lexical/extension) refuses to build the editor at
        // all in these cases; we log and skip, because a bad extension must never
        // take the editor down with it.

        if (module.name !== undefined && claimedNames.has(module.name)) {
          console.error(
            `[Blazor.Lexical] extension '${label}' skipped: the name '${module.name}' is ` +
              `already used by '${claimedNames.get(module.name)}'. Extension names must be unique.`,
          );
          continue;
        }

        const conflict = declaredConflicts.find(
          (d) => module.name !== undefined && d.conflictsWith.includes(module.name),
        );
        if (conflict !== undefined) {
          console.error(
            `[Blazor.Lexical] extension '${label}' skipped: '${conflict.owner}' declares it ` +
              `as conflicting.`,
          );
          continue;
        }
        const conflictsWithLoaded = (module.conflictsWith ?? []).filter((n) => claimedNames.has(n));
        if (conflictsWithLoaded.length > 0) {
          console.error(
            `[Blazor.Lexical] extension '${label}' skipped: it declares a conflict with ` +
              `already-loaded ${conflictsWithLoaded.map((n) => `'${n}'`).join(', ')}.`,
          );
          continue;
        }

        // `nodes` may be a thunk, matching Lexical's own `nodes` field.
        const nodes = typeof module.nodes === 'function' ? module.nodes() : (module.nodes ?? []);
        const duplicateType = nodes
          .map((klass) => klass.getType())
          .find((type) => claimedNodeTypes.has(type));
        if (duplicateType !== undefined) {
          console.error(
            `[Blazor.Lexical] extension '${label}' skipped: node type '${duplicateType}' is ` +
              `already registered by '${claimedNodeTypes.get(duplicateType)}'. Two node classes ` +
              `cannot share a getType().`,
          );
          continue;
        }

        // --- Accepted: claim its names, nodes and theme keys. ---
        if (module.name !== undefined) {
          claimedNames.set(module.name, label);
        }
        if (module.conflictsWith !== undefined) {
          declaredConflicts.push({ owner: label, conflictsWith: module.conflictsWith });
        }
        for (const klass of nodes) {
          claimedNodeTypes.set(klass.getType(), label);
        }
        extensionModules.set(desc.id, module);
        extensionNodes.push(...nodes);

        // Theme fragments for the extension's own nodes, merged deeply and in
        // declaration order — the host overrides them all below. Two extensions
        // claiming the same key is warned about rather than skipped: unlike a node
        // type it is only a styling conflict, and the module is otherwise fine.
        for (const key of Object.keys(module.theme ?? {})) {
          const owner = claimedThemeKeys.get(key);
          if (owner !== undefined) {
            console.warn(
              `[Blazor.Lexical] extensions '${owner}' and '${label}' both define the theme key ` +
                `'${key}'; '${label}' wins. Namespace theme keys to your extension.`,
            );
          }
          claimedThemeKeys.set(key, label);
        }
        deepThemeMerge(extensionTheme, module.theme);
      } catch (error) {
        // A broken extension must not take the editor down with it.
        console.error(
          `[Blazor.Lexical] failed to load extension '${desc.builtIn ?? desc.moduleUrl}'`,
          error,
        );
      }
    }
  }

  const editor = createEditor({
    namespace: options.namespace ?? 'Blazor.Lexical',
    // Extension theme fragments first, the host's theme over them: an extension names
    // the classes for its own nodes, and the host always gets the last word — which is
    // also why extensions are told to namespace their keys rather than touch core ones.
    // Deep, so a host theme that sets `heading.h1` overrides that one key instead of
    // replacing the whole heading group.
    theme: deepThemeMerge(extensionTheme, options.theme ?? {}) as EditorThemeClasses,
    // Register the rich-text, list, and link nodes so HTML/Markdown round-trips
    // carry headings, quotes, lists, and links — not just plain paragraphs.
    // Everything else — the table and mention nodes included — arrives through the
    // extension contract above.
    nodes: [...CORE_NODES, ...extensionNodes],
    onError: (error) => {
      // Surface Lexical internal errors to the browser console rather than
      // swallowing them; there is no .NET channel for these in v1.
      console.error('[Blazor.Lexical]', error);
    },
  });

  editor.setRootElement(contentEl);
  editor.setEditable(!options.readOnly);

  const cleanups: Array<() => void> = [];
  cleanups.push(registerRichText(editor));
  cleanups.push(registerList(editor));

  // Initial content, before history exists (so it is the undo baseline, not an undoable
  // step) and before the update listener is registered (so preloading never pushes the
  // content channel or marks a fresh document dirty). Because it lands here rather than
  // in a post-OnReady round trip, it is also painted with the first frame — no flash of
  // an empty editor.
  if (options.initialContent) {
    await applyInitialContent(editor, options.initialContent);
  }

  if (options.enableHistory ?? true) {
    cleanups.push(registerHistory(editor, createEmptyHistoryState(), 300));
  }

  // Delegated toolbar dispatch. mousedown preventDefault keeps the editor's
  // selection intact when a format button is clicked; a single click/change
  // listener per root covers every button regardless of Blazor re-renders.
  const onMouseDown = (e: Event) => {
    if ((e.target as HTMLElement).closest('button[data-lexical-command]')) {
      e.preventDefault();
    }
  };
  const onClick = (e: Event) => {
    const el = (e.target as HTMLElement).closest<HTMLElement>('button[data-lexical-command]');
    if (!el || el.hasAttribute('data-lexical-disabled')) {
      return;
    }
    runCommandToken(editor, el.getAttribute('data-lexical-command')!, tableModule);
    editor.focus();
  };
  const onChange = (e: Event) => {
    const el = (e.target as HTMLElement).closest<HTMLSelectElement>(
      'select[data-lexical-command="block:select"]',
    );
    if (!el) {
      return;
    }
    // The block selector carries the list options inline (playground-style), so
    // route the list values through the list command; everything else is a block.
    const value = el.value;
    const token =
      value === 'bullet' || value === 'number' ? `list:${value}` : `block:${value}`;
    runCommandToken(editor, token);
    editor.focus();
  };
  root.addEventListener('mousedown', onMouseDown);
  root.addEventListener('click', onClick);
  root.addEventListener('change', onChange);
  cleanups.push(() => {
    root.removeEventListener('mousedown', onMouseDown);
    root.removeEventListener('click', onClick);
    root.removeEventListener('change', onChange);
  });

  // Optional in-editor overlays. Each is Blazor-authored markup that JS only
  // positions/drives; a marker element's mere presence (the host placed the
  // component) opts the behavior in. Their buttons are ordinary
  // data-lexical-command markup, so the delegated dispatch above runs them —
  // the overlays add no interop of their own.
  const floatingToolbarEl = root.querySelector<HTMLElement>('[data-lexical-floating-toolbar]');
  if (floatingToolbarEl) {
    cleanups.push(registerFloatingToolbar(editor, root, contentEl, floatingToolbarEl));
  }
  const slashMenuEl = root.querySelector<HTMLElement>('[data-lexical-slash-menu]');
  if (slashMenuEl) {
    cleanups.push(registerSlashMenu(editor, root, contentEl, slashMenuEl));
  }
  // Every block gutter at once (querySelectorAll, not querySelector): a host may place
  // several — a left rail with the grip and "+", a right rail with its own actions — and
  // they share one hover hit-test, one drop line, and one delegated drag/click pair.
  const blockGutterEls = Array.from(
    root.querySelectorAll<HTMLElement>('[data-lexical-block-gutter]'),
  );
  if (blockGutterEls.length > 0) {
    cleanups.push(
      registerBlockGutters(editor, root, contentEl, blockGutterEls, (block) => {
        // The one overlay with an (opt-in) push: the host's OnBlockHovered. The flag is
        // read at fire time so setNotifications can arm it after create().
        if (notify.blockHover) {
          dotNetRef
            .invokeMethodAsync('OnBlockHoveredInternal', block === null ? null : JSON.stringify(block))
            .catch(() => {});
        }
      }),
    );
  }
  const linkEditorEl = root.querySelector<HTMLElement>('[data-lexical-link-editor]');
  if (linkEditorEl) {
    cleanups.push(registerLinkEditor(editor, root, contentEl, linkEditorEl));
  }
  // Extensions: now that the editor and the core plugins exist, let each loaded module
  // wire its own commands/listeners and keep its teardown. This is where the table
  // runtime + its overlays and the mentions typeahead get wired too — they are
  // extensions like any other. Extensions never extend the core command-token switch;
  // they own their markers and handlers.
  for (const [id, module] of extensionModules) {
    if (!module.register) {
      continue;
    }
    try {
      const teardown = module.register({ editor, root, content: contentEl });
      if (typeof teardown === 'function') {
        cleanups.push(teardown);
      }
    } catch (error) {
      console.error(`[Blazor.Lexical] extension '${id}' failed to register`, error);
    }
  }

  // Selection/history state: always drives toolbar + placeholder DOM (no
  // interop); pushes to .NET only for enabled channels, de-duped.
  let canUndo = false;
  let canRedo = false;
  let lastSelectionJson = '';
  let contentDebounce: ReturnType<typeof setTimeout> | undefined;

  const refreshToolbar = () => {
    const state = editor.getEditorState().read(() => readSelectionState(canUndo, canRedo));
    updateToolbarDom(root, state);
    if (notify.selection) {
      const json = JSON.stringify(state);
      if (json !== lastSelectionJson) {
        lastSelectionJson = json;
        dotNetRef.invokeMethodAsync('OnSelectionChangedInternal', json).catch(() => {});
      }
    }
  };

  cleanups.push(
    mergeRegister(
      editor.registerUpdateListener(({ editorState, tags }) => {
        // Placeholder empty-state (DOM only, independent of the content channel).
        contentEl.toggleAttribute(
          'data-lexical-empty',
          editorState.read(() => $getRoot().getTextContent() === ''),
        );
        refreshToolbar();
        // A silent update (an app-driven touch-up, e.g. a mention refresh) must not
        // push the content channel — otherwise opening a document with stale names
        // would mark it dirty before the user typed anything.
        if (notify.content && !tags.has(SILENT_UPDATE_TAG)) {
          if (contentDebounce !== undefined) {
            clearTimeout(contentDebounce);
          }
          contentDebounce = setTimeout(() => {
            // Serialize in the subscriber's declared format, then make the single
            // crossing. The mode is read at fire time, so setNotifications can change
            // it between ticks.
            void readContentPayload(editor, notify.contentPayload)
              .then((payload) =>
                dotNetRef.invokeMethodAsync('OnContentChangedInternal', payload),
              )
              .catch(() => {});
          }, CHANGE_DEBOUNCE_MS);
        }
      }),
      editor.registerCommand(
        CAN_UNDO_COMMAND,
        (payload) => {
          canUndo = payload;
          refreshToolbar();
          return false;
        },
        COMMAND_PRIORITY_LOW,
      ),
      editor.registerCommand(
        CAN_REDO_COMMAND,
        (payload) => {
          canRedo = payload;
          refreshToolbar();
          return false;
        },
        COMMAND_PRIORITY_LOW,
      ),
    ),
  );

  // Initial paint of placeholder + toolbar state before any user interaction.
  contentEl.toggleAttribute(
    'data-lexical-empty',
    editor.getEditorState().read(() => $getRoot().getTextContent() === ''),
  );
  refreshToolbar();

  instances.set(instanceId, {
    editor,
    table: tableModule,
    mentions: mentionsModule,
    extensions: extensionModules,
    setNotify(flags) {
      if (flags.content !== undefined) {
        notify.content = flags.content;
      }
      if (flags.contentPayload !== undefined) {
        notify.contentPayload = flags.contentPayload;
      }
      if (flags.blockHover !== undefined) {
        notify.blockHover = flags.blockHover;
      }
      if (flags.selection !== undefined) {
        notify.selection = flags.selection;
        // Force the next push (state may have changed while unsubscribed).
        lastSelectionJson = '';
        refreshToolbar();
      }
    },
    dispose() {
      if (contentDebounce !== undefined) {
        clearTimeout(contentDebounce);
      }
      for (const cleanup of cleanups) {
        cleanup();
      }
      editor.setRootElement(null);
    },
  });
}

/**
 * Calls an extension module's `invoke` handler — the .NET→JS direction of the
 * extension channel (`LexicalExtension.InvokeJsAsync`). Arguments arrive as a JSON
 * array string and the result goes back as JSON, so no shape assumptions cross the
 * boundary. Returns null when the editor, the extension, or its handler is absent.
 */
export async function invokeExtension(
  instanceId: string,
  extensionId: string,
  method: string,
  argsJson: string,
): Promise<string | null> {
  const module = instances.get(instanceId)?.extensions.get(extensionId);
  if (!module?.invoke) {
    return null;
  }
  const parsed: unknown = argsJson ? JSON.parse(argsJson) : [];
  const result = await module.invoke(method, Array.isArray(parsed) ? parsed : [parsed]);
  return result === undefined ? null : JSON.stringify(result);
}

/** Enables/disables the .NET push channels for an existing editor. */
export function setNotifications(
  instanceId: string,
  notify: Partial<NotifyFlags>,
): void {
  instances.get(instanceId)?.setNotify(notify);
}

/** Reads the current plain-text content of the editor. */
export function getText(instanceId: string): string {
  const instance = instances.get(instanceId);
  if (!instance) {
    return '';
  }
  return instance.editor
    .getEditorState()
    .read(() => $getRoot().getTextContent());
}

/** Replaces the editor content with a single paragraph holding `text`. */
export function setText(instanceId: string, text: string): void {
  const instance = instances.get(instanceId);
  if (!instance) {
    return;
  }
  applyText(instance.editor, text);
}

/** Inserts `text` at the current selection, replacing any selected range. */
export function insertText(instanceId: string, text: string): void {
  const instance = instances.get(instanceId);
  if (!instance) {
    return;
  }
  instance.editor.update(() => {
    const selection = $getSelection();
    if ($isRangeSelection(selection)) {
      selection.insertText(text);
    }
  });
}

/** Serializes the editor content to an HTML string. */
export function getHtml(instanceId: string): string {
  const instance = instances.get(instanceId);
  if (!instance) {
    return '';
  }
  return instance.editor
    .getEditorState()
    .read(() => $generateHtmlFromNodes(instance.editor, null));
}

/** Replaces the editor content with nodes parsed from an HTML string. */
export function setHtml(instanceId: string, html: string): void {
  const instance = instances.get(instanceId);
  if (!instance) {
    return;
  }
  applyHtml(instance.editor, html);
}

/**
 * Serializes the editor content to a Markdown string. The @lexical/markdown
 * transformers live in a lazily-loaded chunk (see markdown.ts), fetched on first
 * use so editors that never touch Markdown don't download them.
 */
export async function getMarkdown(instanceId: string): Promise<string> {
  const instance = instances.get(instanceId);
  if (!instance) {
    return '';
  }
  const md = await import('./markdown');
  return md.toMarkdown(instance.editor);
}

/**
 * Replaces the editor content with nodes parsed from a Markdown string. The
 * @lexical/markdown transformers live in a lazily-loaded chunk (see markdown.ts),
 * fetched on first use.
 */
export async function setMarkdown(instanceId: string, markdown: string): Promise<void> {
  const instance = instances.get(instanceId);
  if (!instance) {
    return;
  }
  const md = await import('./markdown');
  md.fromMarkdown(instance.editor, markdown);
}

/** Serializes the full editor state to its canonical JSON string. */
export function getEditorStateJson(instanceId: string): string {
  const instance = instances.get(instanceId);
  if (!instance) {
    return '';
  }
  return JSON.stringify(instance.editor.getEditorState().toJSON());
}

/** Restores the editor from a canonical editor-state JSON string. */
export function setEditorStateJson(instanceId: string, json: string): void {
  const instance = instances.get(instanceId);
  if (!instance) {
    return;
  }
  applyEditorStateJson(instance.editor, json);
}

/** Sets whether the editor is editable. */
export function setEditable(instanceId: string, editable: boolean): void {
  instances.get(instanceId)?.editor.setEditable(editable);
}

/**
 * Inserts a table of `rows`×`columns` at the current selection. `includeHeaders`
 * toggles a bold header row (the default matches the toolbar picker).
 */
export function insertTable(
  instanceId: string,
  rows: number,
  columns: number,
  includeHeaders: boolean,
): void {
  const instance = instances.get(instanceId);
  // No-op unless the editor opted into the table feature (EnableTables), which is
  // what lazy-loads instance.table.
  if (instance?.table) {
    instance.table.insertTableWithDimensions(
      instance.editor,
      rows,
      columns,
      includeHeaders ? { rows: true, columns: false } : false,
    );
  }
}

/** Converts the blocks at the current selection into a bulleted list. */
export function insertUnorderedList(instanceId: string): void {
  instances
    .get(instanceId)
    ?.editor.dispatchCommand(INSERT_UNORDERED_LIST_COMMAND, undefined);
}

/** Converts the blocks at the current selection into a numbered list. */
export function insertOrderedList(instanceId: string): void {
  instances
    .get(instanceId)
    ?.editor.dispatchCommand(INSERT_ORDERED_LIST_COMMAND, undefined);
}

/** Converts the list items at the current selection back into paragraphs. */
export function removeList(instanceId: string): void {
  instances.get(instanceId)?.editor.dispatchCommand(REMOVE_LIST_COMMAND, undefined);
}

/**
 * Toggles a link over the current selection. A non-empty `url` wraps the
 * selection in (or updates) a link; an empty/null `url` unwraps it.
 */
export function toggleLink(instanceId: string, url: string | null): void {
  const instance = instances.get(instanceId);
  if (instance) {
    toggleLinkOnEditor(instance.editor, url);
  }
}

/**
 * Toggles an inline text format (bold, italic, underline, strikethrough, code,
 * subscript, superscript) over the current selection.
 */
export function formatText(instanceId: string, format: string): void {
  instances
    .get(instanceId)
    ?.editor.dispatchCommand(FORMAT_TEXT_COMMAND, format as TextFormatType);
}

/**
 * Sets the block alignment (left, center, right, justify; '' clears it) for the
 * blocks at the current selection.
 */
export function formatAlignment(instanceId: string, alignment: string): void {
  instances
    .get(instanceId)
    ?.editor.dispatchCommand(FORMAT_ELEMENT_COMMAND, alignment as ElementFormatType);
}

/**
 * Converts the blocks at the current selection to a block type: 'paragraph',
 * 'quote', or a heading 'h1'..'h6'. Lists are handled by the dedicated
 * insert/remove list functions, not here.
 */
export function setBlockType(instanceId: string, type: string): void {
  const instance = instances.get(instanceId);
  if (instance) {
    applyBlockType(instance.editor, type);
  }
}

/** Undoes the last change (no-op when history is disabled or empty). */
export function undo(instanceId: string): void {
  instances.get(instanceId)?.editor.dispatchCommand(UNDO_COMMAND, undefined);
}

/** Redoes the last undone change. */
export function redo(instanceId: string): void {
  instances.get(instanceId)?.editor.dispatchCommand(REDO_COMMAND, undefined);
}

/** Clears any active inline text formats over the current selection. */
export function clearFormatting(instanceId: string): void {
  const instance = instances.get(instanceId);
  if (instance) {
    clearFormattingOnEditor(instance.editor);
  }
}

/** Returns keyboard focus to the editor (e.g. after a toolbar button click). */
export function focus(instanceId: string): void {
  instances.get(instanceId)?.editor.focus();
}

/**
 * Returns a JSON array snapshotting every mention reference node — each with its
 * node key, config id, initiator, app-owned value, current text, and url — so the
 * host can decide which to re-resolve. Empty when the editor has no mentions.
 */
export function getMentions(instanceId: string): string {
  const instance = instances.get(instanceId);
  if (!instance?.mentions) {
    return '[]';
  }
  return JSON.stringify(instance.mentions.collectMentions(instance.editor));
}

/**
 * Silently updates one mention's display text (and optional url) by node key. The
 * edit adds no undo step and does not fire the content-changed channel.
 */
export function refreshMention(
  instanceId: string,
  nodeKey: string,
  text: string,
  url: string | null,
): void {
  const instance = instances.get(instanceId);
  instance?.mentions?.refreshMention(instance.editor, nodeKey, text, url);
}

/**
 * Silently updates every mention matching (configId, value) and returns the count.
 * Same no-undo / no-notify semantics as {@link refreshMention}.
 */
export function refreshMentionsByValue(
  instanceId: string,
  configId: string,
  value: string,
  text: string,
  url: string | null,
): number {
  const instance = instances.get(instanceId);
  if (!instance?.mentions) {
    return 0;
  }
  return instance.mentions.refreshMentionsByValue(instance.editor, configId, value, text, url);
}

/** Tears down the editor and removes it from the registry. */
export function dispose(instanceId: string): void {
  const instance = instances.get(instanceId);
  if (!instance) {
    return;
  }
  instance.dispose();
  instances.delete(instanceId);
}
