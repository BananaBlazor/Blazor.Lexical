// ---------------------------------------------------------------------------
// Mentions: a configurable, multi-instance write-ahead (typeahead) feature plus
// freeform hashtag-style highlighting.
//
// A host declares one or more mention configs (each an initiator char, a colour,
// an optional .NET provider, and a freeform flag). This module contributes two
// custom nodes and wires the runtime:
//
//   * MentionNode          — a segmented (atomic) reference token inserted when a
//                            suggestion is confirmed; carries an app-owned id
//                            (value) + optional url so the host can re-resolve it
//                            later. Serializable for HTML/JSON round-trips.
//   * MentionHighlightNode  — a text-entity node used for freeform highlighting
//                            (the playground's hashtag behaviour), wrapped/unwrapped
//                            live as the user types via registerLexicalTextEntity.
//
// The typeahead machinery mirrors the slash menu (overlays.ts `registerSlashMenu`):
// a trigger regex read on every update, a floating list, ↑/↓/Enter/Tab/Escape via a
// capture-phase keydown. It differs in two ways the feature demands: the list is
// built from data the .NET provider returns (not static DOM), and the menu element
// is created here rather than authored in Blazor (its rows are inherently dynamic).
//
// Interop is strictly opt-in: only configs with a provider query .NET, and only for
// the query. Freeform-only configs are pure JS. Refresh updates carry a tag so they
// neither push the content-changed channel nor add an undo step (see index.ts).
//
// Like the table chunk, this module is loaded THROUGH THE EXTENSION CONTRACT — the
// default export is a `LexicalExtensionFactory` create() runs in the same descriptor
// loop as a consumer extension's, and the two .NET calls below go out over the
// extension channel's `invokeDotNet` rather than over callbacks of their own. The one
// asymmetry is bundling: mentions measured ~4kb gzipped, so index.ts imports it
// statically and the factory simply goes unused when no editor declares configs.
//
// The whole feature is ONE extension owning ALL of an editor's configs, not one per
// config: the freeform highlighter is a single shared text-entity matcher spanning
// every initiator (the reverse transform of registerLexicalTextEntity would otherwise
// unwrap another config's tokens), so per-config instances would fight each other.
//
// This module is written so it can live in the core bundle (static import) or be
// code-split into a lazy chunk (dynamic import), decided by bundle-size measurement;
// either way index.ts is the only switch that changes.
// ---------------------------------------------------------------------------

import {
  $applyNodeReplacement,
  $createTextNode,
  $getNodeByKey,
  $getSelection,
  $isRangeSelection,
  $isTextNode,
  $nodesOfType,
  TextNode,
  type DOMConversionMap,
  type DOMConversionOutput,
  type DOMExportOutput,
  type EditorConfig,
  type LexicalEditor,
  type NodeKey,
  type SerializedTextNode,
  type Spread,
} from 'lexical';
import { addClassNamesToElement } from '@lexical/utils';
import { registerLexicalTextEntity, type EntityMatch } from '@lexical/text';
import type {
  LexicalExtensionModule,
  LexicalExtensionSetup,
} from './extension';

// The tags carried by refresh edits (see refreshMention below): SILENT_UPDATE_TAG
// suppresses the content-changed push index.ts would otherwise make, HISTORY_MERGE_TAG
// keeps the edit out of the undo stack — together, refreshing a stale name in a freshly
// opened document leaves it clean. They live in their own module (tags.ts) so this one
// and index.ts share the constants without importing each other.
import { HISTORY_MERGE_TAG, SILENT_UPDATE_TAG } from './tags';

/** The mentions extension's options payload, mirrored from C# `MentionsExtensionOptionsDto`. */
export interface MentionsOptionsDto {
  configs?: MentionConfigDto[];
}

/** One mention configuration, mirrored from the C# `MentionConfigDto`. */
export interface MentionConfigDto {
  /** Stable config id; routes provider queries and is stored on inserted nodes. */
  id: string;
  /** The trigger character (e.g. '@', '#', '!'). */
  initiator: string;
  /** CSS colour applied to the inserted/highlighted node (inline var). */
  color: string;
  /** When true, matching `<initiator>token` text is highlighted live. */
  freeform: boolean;
  /** When true, typing the initiator queries .NET for suggestions. */
  hasProvider: boolean;
  /** When true, a confirmed selection notifies .NET (the `selected` invoke). */
  notifySelected: boolean;
  /**
   * How long (ms) to wait for a provider response before giving up on the query and
   * closing the session. 0 (or absent) disables the timeout.
   */
  queryTimeoutMs: number;
}

/** A candidate returned by a .NET provider, mirrored from the C# `MentionItem`. */
interface MentionItemDto {
  id: string;
  text: string;
  url?: string | null;
  secondary?: string | null;
}

/** A reference-node snapshot returned by {@link collectMentions}. */
interface MentionRefDto {
  nodeKey: string;
  configId: string;
  initiator: string;
  value: string;
  text: string;
  url: string | null;
}

// ---------------------------------------------------------------------------
// MentionNode — the atomic reference token
// ---------------------------------------------------------------------------

type SerializedMentionNode = Spread<
  {
    configId: string;
    trigger: string;
    value: string;
    url: string | null;
    color: string;
  },
  SerializedTextNode
>;

/** Reflects the app-owned url + colour onto a mention's DOM element. */
function applyMentionAttributes(dom: HTMLElement, url: string | null, color: string): void {
  dom.style.setProperty('--blazor-lexical-mention-color', color);
  if (url) {
    dom.setAttribute('data-lexical-mention-url', url);
  } else {
    dom.removeAttribute('data-lexical-mention-url');
  }
}

/**
 * An atomic, styled reference token inserted when a suggestion is confirmed. Extends
 * TextNode in segmented mode so it behaves as one non-editable unit. It carries the
 * app-owned `value` (an opaque id, the key for later re-resolution) and an optional
 * `url`, both of which survive HTML/JSON round-trips. The display text is the node's
 * own text; `refreshMention` can update it (and the url) later without re-typing.
 */
export class MentionNode extends TextNode {
  __configId: string;
  __trigger: string;
  __value: string;
  __url: string | null;
  __color: string;

  static getType(): string {
    return 'mention';
  }

  static clone(node: MentionNode): MentionNode {
    return new MentionNode(
      node.__configId,
      node.__trigger,
      node.__value,
      node.__url,
      node.__color,
      node.__text,
      node.__key,
    );
  }

  constructor(
    configId: string,
    trigger: string,
    value: string,
    url: string | null,
    color: string,
    text: string,
    key?: NodeKey,
  ) {
    super(text, key);
    this.__configId = configId;
    this.__trigger = trigger;
    this.__value = value;
    this.__url = url;
    this.__color = color;
  }

  createDOM(config: EditorConfig): HTMLElement {
    const dom = super.createDOM(config);
    addClassNamesToElement(dom, config.theme.mention);
    dom.setAttribute('data-lexical-mention', 'true');
    applyMentionAttributes(dom, this.__url, this.__color);
    return dom;
  }

  updateDOM(prevNode: this, dom: HTMLElement, config: EditorConfig): boolean {
    const updated = super.updateDOM(prevNode, dom, config);
    if (prevNode.__url !== this.__url || prevNode.__color !== this.__color) {
      applyMentionAttributes(dom, this.__url, this.__color);
    }
    return updated;
  }

  static importJSON(serializedNode: SerializedMentionNode): MentionNode {
    return $createMentionNode(
      serializedNode.configId,
      serializedNode.trigger,
      serializedNode.value,
      serializedNode.url,
      serializedNode.color,
      serializedNode.text,
    ).updateFromJSON(serializedNode);
  }

  exportJSON(): SerializedMentionNode {
    return {
      ...super.exportJSON(),
      configId: this.__configId,
      trigger: this.__trigger,
      value: this.__value,
      url: this.__url,
      color: this.__color,
    };
  }

  exportDOM(): DOMExportOutput {
    const element = document.createElement('span');
    element.setAttribute('data-lexical-mention', 'true');
    element.setAttribute('data-lexical-mention-config', this.__configId);
    element.setAttribute('data-lexical-mention-trigger', this.__trigger);
    element.setAttribute('data-lexical-mention-value', this.__value);
    element.setAttribute('data-lexical-mention-color', this.__color);
    if (this.__url) {
      element.setAttribute('data-lexical-mention-url', this.__url);
    }
    element.textContent = this.__text;
    return { element };
  }

  static importDOM(): DOMConversionMap | null {
    return {
      span: (domNode: HTMLElement) => {
        if (!domNode.hasAttribute('data-lexical-mention')) {
          return null;
        }
        return { conversion: $convertMentionElement, priority: 1 };
      },
    };
  }

  /** Whether this token carries a link. */
  hasUrl(): boolean {
    return this.getLatest().__url !== null;
  }

  /** Updates the link url (null clears it), returning the writable node. */
  setUrl(url: string | null): this {
    const self = this.getWritable();
    self.__url = url;
    return self;
  }

  /** The app-owned opaque id used to re-resolve this reference later. */
  getValue(): string {
    return this.getLatest().__value;
  }

  /** The stable config id this reference was created from. */
  getConfigId(): string {
    return this.getLatest().__configId;
  }

  /** The trigger character this reference was created from. */
  getTrigger(): string {
    return this.getLatest().__trigger;
  }
}

function $convertMentionElement(domNode: HTMLElement): DOMConversionOutput {
  const configId = domNode.getAttribute('data-lexical-mention-config') ?? '';
  const trigger = domNode.getAttribute('data-lexical-mention-trigger') ?? '';
  const value = domNode.getAttribute('data-lexical-mention-value') ?? '';
  const url = domNode.getAttribute('data-lexical-mention-url');
  const color = domNode.getAttribute('data-lexical-mention-color') ?? '';
  const text = domNode.textContent ?? '';
  return { node: $createMentionNode(configId, trigger, value, url, color, text) };
}

/** Creates a segmented, directionless {@link MentionNode}. */
export function $createMentionNode(
  configId: string,
  trigger: string,
  value: string,
  url: string | null,
  color: string,
  text: string,
): MentionNode {
  const node = new MentionNode(configId, trigger, value, url, color, text);
  node.setMode('segmented').toggleDirectionless();
  return $applyNodeReplacement(node);
}

// ---------------------------------------------------------------------------
// MentionHighlightNode — the freeform (hashtag-style) highlight
// ---------------------------------------------------------------------------

type SerializedMentionHighlightNode = Spread<
  { trigger: string; color: string },
  SerializedTextNode
>;

/**
 * A styled, editable text-entity node used for freeform highlighting: as the user
 * types `<initiator>token`, {@link registerLexicalTextEntity} wraps the run into this
 * node and unwraps it when it stops matching (exactly like the playground's hashtag).
 * Unlike {@link MentionNode} it stays plain editable text and carries no payload.
 */
export class MentionHighlightNode extends TextNode {
  __trigger: string;
  __color: string;

  static getType(): string {
    return 'mention-highlight';
  }

  static clone(node: MentionHighlightNode): MentionHighlightNode {
    return new MentionHighlightNode(node.__trigger, node.__color, node.__text, node.__key);
  }

  constructor(trigger: string, color: string, text: string, key?: NodeKey) {
    super(text, key);
    this.__trigger = trigger;
    this.__color = color;
  }

  createDOM(config: EditorConfig): HTMLElement {
    const dom = super.createDOM(config);
    addClassNamesToElement(dom, config.theme.mentionHighlight);
    dom.setAttribute('data-lexical-mention-highlight', 'true');
    dom.style.setProperty('--blazor-lexical-mention-color', this.__color);
    return dom;
  }

  updateDOM(prevNode: this, dom: HTMLElement, config: EditorConfig): boolean {
    const updated = super.updateDOM(prevNode, dom, config);
    if (prevNode.__color !== this.__color) {
      dom.style.setProperty('--blazor-lexical-mention-color', this.__color);
    }
    return updated;
  }

  static importJSON(serializedNode: SerializedMentionHighlightNode): MentionHighlightNode {
    return $createMentionHighlightNode(
      serializedNode.trigger,
      serializedNode.color,
      serializedNode.text,
    ).updateFromJSON(serializedNode);
  }

  exportJSON(): SerializedMentionHighlightNode {
    return {
      ...super.exportJSON(),
      trigger: this.__trigger,
      color: this.__color,
    };
  }

  exportDOM(): DOMExportOutput {
    const element = document.createElement('span');
    element.setAttribute('data-lexical-mention-highlight', 'true');
    element.setAttribute('data-lexical-mention-trigger', this.__trigger);
    element.setAttribute('data-lexical-mention-color', this.__color);
    element.textContent = this.__text;
    return { element };
  }

  static importDOM(): DOMConversionMap | null {
    return {
      span: (domNode: HTMLElement) => {
        if (!domNode.hasAttribute('data-lexical-mention-highlight')) {
          return null;
        }
        return { conversion: $convertMentionHighlightElement, priority: 1 };
      },
    };
  }

  isTextEntity(): true {
    return true;
  }

  canInsertTextBefore(): boolean {
    return false;
  }
}

function $convertMentionHighlightElement(domNode: HTMLElement): DOMConversionOutput {
  const trigger = domNode.getAttribute('data-lexical-mention-trigger') ?? '';
  const color = domNode.getAttribute('data-lexical-mention-color') ?? '';
  const text = domNode.textContent ?? '';
  return { node: $createMentionHighlightNode(trigger, color, text) };
}

/** Creates a {@link MentionHighlightNode}. */
export function $createMentionHighlightNode(
  trigger: string,
  color: string,
  text: string,
): MentionHighlightNode {
  return $applyNodeReplacement(new MentionHighlightNode(trigger, color, text));
}

/** The custom node classes to register in `createEditor`'s `nodes[]`. */
export const mentionNodes = [MentionNode, MentionHighlightNode];

// ---------------------------------------------------------------------------
// Trigger / entity matching
// ---------------------------------------------------------------------------

/** Escapes a string for literal use inside a RegExp. */
function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * A trigger regex for the typeahead: the initiator at line start or after
 * whitespace, then a run of word characters (the query, possibly empty). Any other
 * character — a space included — ends the match and closes the session.
 */
function makeTriggerRegex(initiator: string): RegExp {
  return new RegExp(`(?:^|\\s)${escapeRegExp(initiator)}([\\w-]*)$`);
}

/**
 * A combined entity matcher over every freeform initiator, returning the first
 * `<initiator>token` run (initiator kept, boundary excluded via lookbehind). One
 * matcher shared by all freeform configs keeps registerLexicalTextEntity's single
 * reverse-transform from unwrapping a node whose initiator belongs to another config.
 */
function makeEntityMatcher(initiators: string[]): (text: string) => EntityMatch | null {
  // Longest initiators first so multi-char triggers win over single-char prefixes.
  const alternation = [...initiators]
    .sort((a, b) => b.length - a.length)
    .map(escapeRegExp)
    .join('|');
  const regex = new RegExp(`(?:^|(?<=\\s))(?:${alternation})[\\w-]+`, 'u');
  return (text: string): EntityMatch | null => {
    const match = regex.exec(text);
    if (match === null) {
      return null;
    }
    return { start: match.index, end: match.index + match[0].length };
  };
}

// ---------------------------------------------------------------------------
// getMentions / refresh helpers (app-driven dynamic updates)
// ---------------------------------------------------------------------------

/** Snapshots every {@link MentionNode} in the document (read-only). */
export function collectMentions(editor: LexicalEditor): MentionRefDto[] {
  return editor.getEditorState().read(() =>
    $nodesOfType(MentionNode).map((node) => ({
      nodeKey: node.getKey(),
      configId: node.__configId,
      initiator: node.__trigger,
      value: node.__value,
      text: node.getTextContent(),
      url: node.__url,
    })),
  );
}

/**
 * Silently updates one mention's display text (and optional url) by node key. The
 * edit is tagged so it neither pushes the content-changed channel nor adds an undo
 * step — opening a document and refreshing stale names never marks it dirty.
 */
export function refreshMention(
  editor: LexicalEditor,
  nodeKey: string,
  text: string,
  url: string | null,
): void {
  editor.update(
    () => {
      const node = $getNodeByKey(nodeKey);
      if (node instanceof MentionNode) {
        node.setTextContent(text);
        node.setUrl(url);
      }
    },
    { tag: [SILENT_UPDATE_TAG, HISTORY_MERGE_TAG] },
  );
}

/**
 * Silently updates every mention matching (configId, value) — a contact renamed once
 * updates all its occurrences. Returns how many nodes changed. Same tag/history
 * semantics as {@link refreshMention}.
 */
export function refreshMentionsByValue(
  editor: LexicalEditor,
  configId: string,
  value: string,
  text: string,
  url: string | null,
): number {
  let count = 0;
  editor.update(
    () => {
      for (const node of $nodesOfType(MentionNode)) {
        if (node.__configId === configId && node.__value === value) {
          node.setTextContent(text);
          node.setUrl(url);
          count++;
        }
      }
    },
    { tag: [SILENT_UPDATE_TAG, HISTORY_MERGE_TAG] },
  );
  return count;
}

// ---------------------------------------------------------------------------
// registerMentions — the runtime wiring
// ---------------------------------------------------------------------------

/** Positions `el` absolutely (relative to `root`) just below `rect`'s bottom-left. */
function positionBelow(el: HTMLElement, rect: DOMRect, root: HTMLElement): void {
  const rootRect = root.getBoundingClientRect();
  el.style.top = `${rect.bottom - rootRect.top + 4}px`;
  el.style.left = `${Math.max(rect.left - rootRect.left, 0)}px`;
}

/** A `<initiator>query` trigger found at the collapsed caret. */
interface MentionMatch {
  config: MentionConfigDto;
  nodeKey: string;
  /** Offset of the initiator within the text node. */
  triggerOffset: number;
  /** Offset of the caret (end of the query) within the text node. */
  caretOffset: number;
  /** The text typed after the initiator. */
  query: string;
}

/**
 * Wires the mentions runtime for the given configs: the freeform highlight transform
 * (for configs with `freeform`), and the typeahead menu (for configs with a
 * provider). Creates and owns the floating menu element under `root`. Returns a
 * teardown that removes every listener and the menu.
 */
export function registerMentions(
  editor: LexicalEditor,
  root: HTMLElement,
  contentEl: HTMLElement,
  configs: MentionConfigDto[],
  invokeDotNet: LexicalExtensionSetup['invokeDotNet'],
): () => void {
  const cleanups: Array<() => void> = [];

  // --- Freeform highlight transform (one shared entity over all freeform inits) ---
  const freeformConfigs = configs.filter((c) => c.freeform);
  if (freeformConfigs.length > 0) {
    const byInitiator = new Map(freeformConfigs.map((c) => [c.initiator, c]));
    const getMatch = makeEntityMatcher(freeformConfigs.map((c) => c.initiator));
    const createNode = (textNode: TextNode): MentionHighlightNode => {
      const text = textNode.getTextContent();
      // Match the config by the longest initiator that prefixes the matched run.
      const config =
        [...byInitiator.entries()]
          .sort((a, b) => b[0].length - a[0].length)
          .find(([initiator]) => text.startsWith(initiator))?.[1] ?? freeformConfigs[0];
      return $createMentionHighlightNode(config.initiator, config.color, text);
    };
    for (const teardown of registerLexicalTextEntity(
      editor,
      getMatch,
      MentionHighlightNode,
      createNode,
    )) {
      cleanups.push(teardown);
    }
  }

  // --- Typeahead menu (configs with a provider) ---
  const providerConfigs = configs.filter((c) => c.hasProvider);
  if (providerConfigs.length === 0) {
    return () => cleanups.forEach((fn) => fn());
  }

  const triggers = new Map(providerConfigs.map((c) => [c.id, makeTriggerRegex(c.initiator)]));

  // The floating menu is created here (not authored in Blazor) because its rows are
  // built from provider data. It lives under root so it positions with the caret.
  const menuEl = document.createElement('div');
  menuEl.className = 'blazor-lexical__mention-menu';
  menuEl.setAttribute('data-lexical-mention-menu', '');
  menuEl.setAttribute('role', 'listbox');
  root.appendChild(menuEl);

  let session: MentionMatch | null = null;
  let items: MentionItemDto[] = [];
  let activeIndex = 0;
  let requestSeq = 0;
  let queryDebounce: ReturnType<typeof setTimeout> | undefined;
  let queryTimeout: ReturnType<typeof setTimeout> | undefined;

  const QUERY_DEBOUNCE_MS = 150;

  /**
   * Marks the menu as waiting on a provider. A host's data source can be slow — the
   * menu is shown (empty) with `data-lexical-mention-loading` as soon as the query is
   * dispatched, so the user sees the picker working instead of nothing at all. This is
   * the reference for the "slow data must degrade gracefully" rule extensions follow.
   */
  const setLoading = (loading: boolean): void => {
    menuEl.toggleAttribute('data-lexical-mention-loading', loading);
  };

  /** Clears the in-flight query's debounce and timeout timers. */
  const clearQueryTimers = (): void => {
    if (queryDebounce !== undefined) {
      clearTimeout(queryDebounce);
      queryDebounce = undefined;
    }
    if (queryTimeout !== undefined) {
      clearTimeout(queryTimeout);
      queryTimeout = undefined;
    }
  };

  /** Reads the caret for the first provider trigger. Runs in a read context. */
  const readMentionMatch = (): MentionMatch | null => {
    const selection = $getSelection();
    if (!$isRangeSelection(selection) || !selection.isCollapsed()) {
      return null;
    }
    const node = selection.anchor.getNode();
    if (!$isTextNode(node)) {
      return null;
    }
    const caretOffset = selection.anchor.offset;
    const before = node.getTextContent().slice(0, caretOffset);
    for (const config of providerConfigs) {
      const match = before.match(triggers.get(config.id)!);
      if (match !== null) {
        const query = match[1];
        return {
          config,
          nodeKey: node.getKey(),
          triggerOffset: caretOffset - query.length - config.initiator.length,
          caretOffset,
          query,
        };
      }
    }
    return null;
  };

  const close = (): void => {
    session = null;
    items = [];
    clearQueryTimers();
    setLoading(false);
    menuEl.removeAttribute('data-lexical-visible');
  };

  /** Shows the menu at the caret. Returns false when there is no caret to anchor to. */
  const showMenuAtCaret = (): boolean => {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) {
      return false;
    }
    menuEl.setAttribute('data-lexical-visible', '');
    positionBelow(menuEl, sel.getRangeAt(0).getBoundingClientRect(), root);
    return true;
  };

  const setActive = (index: number): void => {
    activeIndex = index;
    menuEl.querySelectorAll<HTMLElement>('[data-lexical-mention-item]').forEach((el, i) => {
      el.toggleAttribute('data-lexical-mention-active', i === index);
    });
  };

  const renderItems = (): void => {
    menuEl.replaceChildren();
    items.forEach((item, i) => {
      const row = document.createElement('button');
      row.type = 'button';
      row.className = 'blazor-lexical__mention-item';
      row.setAttribute('data-lexical-mention-item', '');
      row.setAttribute('data-index', String(i));
      row.setAttribute('role', 'option');

      const label = document.createElement('span');
      label.className = 'blazor-lexical__mention-item-label';
      label.textContent = item.text;
      row.appendChild(label);

      if (item.secondary) {
        const secondary = document.createElement('span');
        secondary.className = 'blazor-lexical__mention-item-secondary';
        secondary.textContent = item.secondary;
        row.appendChild(secondary);
      }
      menuEl.appendChild(row);
    });
    setActive(items.length > 0 ? 0 : -1);
  };

  /** Replaces the `<initiator>query` run with a mention node for `item`. */
  const commit = (item: MentionItemDto): void => {
    const active = session;
    if (active === null) {
      return;
    }
    editor.update(() => {
      const match = readMentionMatch();
      if (match === null || match.config.id !== active.config.id) {
        return;
      }
      const node = $getNodeByKey(match.nodeKey);
      if (!$isTextNode(node)) {
        return;
      }
      const mentionNode = $createMentionNode(
        active.config.id,
        active.config.initiator,
        item.id,
        item.url ?? null,
        active.config.color,
        item.text,
      );
      const range = node.select(match.triggerOffset, match.caretOffset);
      range.insertNodes([mentionNode]);
      // A trailing space so typing continues as normal after the token.
      const space = $createTextNode(' ');
      mentionNode.insertAfter(space);
      space.select(1, 1);
    });
    if (active.config.notifySelected) {
      invokeDotNet('selected', active.config.id, item.id, item.text, item.url ?? null).catch(
        () => {},
      );
    }
    close();
    editor.focus();
  };

  /** Debounced provider query for the current session's query. */
  const queryProvider = (match: MentionMatch): void => {
    clearQueryTimers();
    const seq = ++requestSeq;
    queryDebounce = setTimeout(() => {
      queryDebounce = undefined;

      // Show the (empty) menu in its loading state the moment the query goes out, so a
      // slow provider reads as "working" rather than as nothing happening.
      items = [];
      menuEl.replaceChildren();
      setLoading(true);
      showMenuAtCaret();

      // Soft timeout: a hung provider must not strand the session forever. Bumping the
      // sequence makes the eventual response stale, so it is ignored if it ever lands.
      const timeoutMs = match.config.queryTimeoutMs;
      if (timeoutMs > 0) {
        queryTimeout = setTimeout(() => {
          queryTimeout = undefined;
          if (seq !== requestSeq) {
            return;
          }
          requestSeq++;
          console.warn(
            `[Blazor.Lexical] mention provider for '${match.config.initiator}' did not ` +
              `respond within ${timeoutMs}ms; closing the picker.`,
          );
          close();
        }, timeoutMs);
      }

      // The extension channel parses the .NET side's JSON result for us, so the
      // candidates arrive already shaped.
      invokeDotNet<MentionItemDto[]>('resolve', match.config.id, match.query)
        .then((resolved) => {
          // Ignore stale responses and responses after the session moved on.
          if (seq !== requestSeq || session === null || session.config.id !== match.config.id) {
            return;
          }
          clearQueryTimers();
          setLoading(false);
          items = resolved ?? [];
          if (items.length === 0) {
            close();
            return;
          }
          renderItems();
          showMenuAtCaret();
        })
        .catch(() => close());
    }, QUERY_DEBOUNCE_MS);
  };

  // Re-read on every editor change: (re)open while a trigger exists, query when the
  // fragment changes, close once the trigger is gone.
  cleanups.push(
    editor.registerUpdateListener(() => {
      const match = editor.getEditorState().read(readMentionMatch);
      if (match === null) {
        if (session !== null) {
          close();
        }
        return;
      }
      const changed =
        session === null ||
        session.config.id !== match.config.id ||
        session.query !== match.query;
      session = match;
      if (changed) {
        queryProvider(match);
      }
    }),
  );

  // Capture-phase keydown so the menu wins arrows/enter over the editor while open.
  const onKeyDown = (e: KeyboardEvent): void => {
    if (session === null) {
      return;
    }
    // Escape must dismiss the picker even while it is still loading — the one key
    // that has to work when the provider is the slow part.
    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      close();
      return;
    }
    if (items.length === 0) {
      return;
    }
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        e.stopPropagation();
        setActive((activeIndex + 1) % items.length);
        break;
      case 'ArrowUp':
        e.preventDefault();
        e.stopPropagation();
        setActive((activeIndex - 1 + items.length) % items.length);
        break;
      case 'Enter':
      case 'Tab':
        e.preventDefault();
        e.stopPropagation();
        if (activeIndex >= 0) {
          commit(items[activeIndex]);
        }
        break;
    }
  };
  contentEl.addEventListener('keydown', onKeyDown, true);

  // Mouse selection: a click on a row commits it (mousedown preventDefault keeps the
  // editor selection so commit's re-read finds the trigger).
  const onMenuMouseDown = (e: Event): void => e.preventDefault();
  const onMenuClick = (e: Event): void => {
    const row = (e.target as HTMLElement).closest<HTMLElement>('[data-lexical-mention-item]');
    if (row === null) {
      return;
    }
    const index = Number(row.getAttribute('data-index'));
    if (index >= 0 && index < items.length) {
      commit(items[index]);
    }
  };
  menuEl.addEventListener('mousedown', onMenuMouseDown);
  menuEl.addEventListener('click', onMenuClick);

  cleanups.push(() => {
    contentEl.removeEventListener('keydown', onKeyDown, true);
    menuEl.removeEventListener('mousedown', onMenuMouseDown);
    menuEl.removeEventListener('click', onMenuClick);
    clearQueryTimers();
    menuEl.remove();
  });

  return () => cleanups.forEach((fn) => fn());
}

// ---------------------------------------------------------------------------
// The extension module
// ---------------------------------------------------------------------------

/**
 * The mentions feature as a `LexicalExtensionFactory` — the built-in tier of the same
 * contract consumer extensions use. It declares the two mention nodes (read before
 * `createEditor`) and, once the editor exists, wires the freeform highlighter and/or
 * the provider-driven typeahead for every config the editor declared.
 *
 * `setup.invokeDotNet` is the feature's only .NET channel, and it is gated on the C#
 * side exactly like a consumer extension's: an editor whose configs are all freeform
 * reports no invoke handler, so this half can never call in.
 */
export default function mentionsExtension(
  setup: LexicalExtensionSetup,
): LexicalExtensionModule {
  const configs = (setup.options as MentionsOptionsDto | undefined)?.configs ?? [];
  return {
    name: 'blazor-lexical/mentions',
    nodes: mentionNodes,
    register: ({ editor, root, content }) =>
      registerMentions(editor, root, content, configs, setup.invokeDotNet),
  };
}
