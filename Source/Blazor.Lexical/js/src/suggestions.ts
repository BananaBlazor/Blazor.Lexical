// ---------------------------------------------------------------------------
// Suggestions: inline "suggested edit" rendering — the reviewer-with-a-pen diff.
//
// A suggestion targets a span of existing text (described by a text quote, the same
// TextQuoteSelector anchor marks and highlights use) and proposes a replacement. It
// renders the target STRUCK THROUGH and the proposed text inserted right after it,
// highlighted — Google Docs "suggestion mode", but display-only. There are no
// accept/reject affordances here: the consuming app renders its own chrome and drives
// accept/reject in its own store. The library only paints the diff and reports clicks.
//
// This is display-only and DELIBERATELY MUTATES the in-memory document. The viewer
// never persists editor state — the consuming app repaints from its own source of
// truth on every load — so turning a run of text into deletion+insertion nodes is
// safe: nothing round-trips back out. That is also why both nodes live only long
// enough to be shown and are removed wholesale by id (RemoveSuggestion) or in bulk
// (ClearSuggestions).
//
// Two custom nodes, both TextNode subclasses so they sit inline with the prose:
//   * SuggestionDeletionNode — the original text, class `suggestionDeletion`
//     (line-through) + data-suggestion-id. It keeps the original text/format/style so
//     removing the suggestion restores the run exactly.
//   * SuggestionInsertionNode — the proposed text, class `suggestionInsertion` +
//     data-suggestion-id, in TOKEN mode so it reads as one atomic inserted unit.
// Both carry the app-owned suggestion id (opaque, never minted here), so a click on
// either half reports that id.
// ---------------------------------------------------------------------------

import {
  $applyNodeReplacement,
  $createTextNode,
  $getNearestNodeFromDOMNode,
  $getSelection,
  $isRangeSelection,
  $isTextNode,
  $nodesOfType,
  $setSelection,
  CLICK_COMMAND,
  COMMAND_PRIORITY_LOW,
  TextNode,
  type EditorConfig,
  type LexicalEditor,
  type LexicalNode,
  type NodeKey,
  type SerializedTextNode,
  type Spread,
} from 'lexical';
import { $findMatchingParent, addClassNamesToElement } from '@lexical/utils';
import { $selectionFromDomRange } from './dom-selection';
import { flatten, resolve, type Quote } from './text-quote';
import type { LexicalExtensionModule, LexicalExtensionSetup } from './extension';

/** The suggestion id carried by both node kinds, threaded through JSON. */
type SerializedSuggestionNode = Spread<{ suggestionId: string }, SerializedTextNode>;

/**
 * The struck-through original text of a suggested edit. A TextNode subclass so it flows
 * with the prose; the line-through is the `suggestionDeletion` theme class, not a format
 * bit, so the node keeps the run's real format/style and a removal restores it verbatim.
 */
class SuggestionDeletionNode extends TextNode {
  __suggestionId: string;

  constructor(text: string, suggestionId: string, key?: NodeKey) {
    super(text, key);
    this.__suggestionId = suggestionId;
  }

  static getType(): string {
    return 'suggestion-deletion';
  }

  static clone(node: SuggestionDeletionNode): SuggestionDeletionNode {
    return new SuggestionDeletionNode(node.__text, node.__suggestionId, node.__key);
  }

  static importJSON(serialized: SerializedSuggestionNode): SuggestionDeletionNode {
    return $createSuggestionDeletionNode(serialized.text, serialized.suggestionId).updateFromJSON(
      serialized,
    );
  }

  exportJSON(): SerializedSuggestionNode {
    return { ...super.exportJSON(), suggestionId: this.getSuggestionId() };
  }

  getSuggestionId(): string {
    return this.getLatest().__suggestionId;
  }

  createDOM(config: EditorConfig): HTMLElement {
    const dom = super.createDOM(config);
    addClassNamesToElement(dom, config.theme.suggestionDeletion);
    dom.setAttribute('data-suggestion-id', this.__suggestionId);
    return dom;
  }
}

/**
 * The proposed replacement text of a suggested edit, inserted after the deletion and
 * highlighted (`suggestionInsertion` theme class). Token mode makes it atomic — it reads
 * as one inserted unit rather than editable prose, which suits the read-only review view.
 */
class SuggestionInsertionNode extends TextNode {
  __suggestionId: string;

  constructor(text: string, suggestionId: string, key?: NodeKey) {
    super(text, key);
    this.__suggestionId = suggestionId;
  }

  static getType(): string {
    return 'suggestion-insertion';
  }

  static clone(node: SuggestionInsertionNode): SuggestionInsertionNode {
    return new SuggestionInsertionNode(node.__text, node.__suggestionId, node.__key);
  }

  static importJSON(serialized: SerializedSuggestionNode): SuggestionInsertionNode {
    return $createSuggestionInsertionNode(serialized.text, serialized.suggestionId).updateFromJSON(
      serialized,
    );
  }

  exportJSON(): SerializedSuggestionNode {
    return { ...super.exportJSON(), suggestionId: this.getSuggestionId() };
  }

  getSuggestionId(): string {
    return this.getLatest().__suggestionId;
  }

  createDOM(config: EditorConfig): HTMLElement {
    const dom = super.createDOM(config);
    addClassNamesToElement(dom, config.theme.suggestionInsertion);
    dom.setAttribute('data-suggestion-id', this.__suggestionId);
    return dom;
  }
}

function $createSuggestionDeletionNode(text: string, suggestionId: string): SuggestionDeletionNode {
  return $applyNodeReplacement(new SuggestionDeletionNode(text, suggestionId));
}

function $createSuggestionInsertionNode(
  text: string,
  suggestionId: string,
): SuggestionInsertionNode {
  const node = new SuggestionInsertionNode(text, suggestionId);
  // Token mode: the inserted proposal behaves as one atomic unit.
  node.setMode('token');
  return $applyNodeReplacement(node);
}

function $isSuggestionNode(
  node: LexicalNode | null,
): node is SuggestionDeletionNode | SuggestionInsertionNode {
  return node instanceof SuggestionDeletionNode || node instanceof SuggestionInsertionNode;
}

/**
 * The suggestions feature as a `LexicalExtensionFactory`. Contributes the two suggestion
 * nodes and their theme defaults, and an opt-in click channel (armed only when the C# half
 * wired OnSuggestionClicked). Everything the host drives — show/remove/clear — comes in
 * over the extension `invoke` surface.
 */
export default function suggestionsExtension(
  setup: LexicalExtensionSetup,
): LexicalExtensionModule {
  let editorRef: LexicalEditor | null = null;
  let contentRef: HTMLElement | null = null;

  return {
    name: 'blazor-lexical/suggestions',
    nodes: [SuggestionDeletionNode, SuggestionInsertionNode],
    // Defaults for the nodes' theme keys; the host's typed LexicalTheme is merged over
    // these (host wins), so setting SuggestionInsertion/SuggestionDeletion overrides them.
    theme: {
      suggestionInsertion: 'blazor-lexical__suggestion-insertion',
      suggestionDeletion: 'blazor-lexical__suggestion-deletion',
    },
    register: ({ editor, content }) => {
      editorRef = editor;
      contentRef = content;

      // A click on either half of a suggestion reports its id — resolved from the clicked
      // ELEMENT (like marks), not the selection, which has not moved to the click yet.
      // Returns false so the click keeps travelling; this observes, it never swallows.
      const unregister = editor.registerCommand(
        CLICK_COMMAND,
        (event: MouseEvent) => {
          if (!setup.canInvokeDotNet) {
            return false;
          }
          const target = event.target;
          if (!(target instanceof Node)) {
            return false;
          }
          const ids = editor.read(() => {
            const node = $getNearestNodeFromDOMNode(target);
            const suggestion = $isSuggestionNode(node)
              ? node
              : node === null
                ? null
                : $findMatchingParent(node, $isSuggestionNode);
            return $isSuggestionNode(suggestion) ? [suggestion.getSuggestionId()] : [];
          });
          if (ids.length > 0) {
            setup.notifyDotNet('clicked', JSON.stringify(ids));
          }
          return false;
        },
        COMMAND_PRIORITY_LOW,
      );

      return () => {
        unregister();
        editorRef = null;
        contentRef = null;
      };
    },
    invoke: (method, args) => {
      const editor = editorRef;
      if (editor === null) {
        return undefined;
      }
      switch (method) {
        case 'show': {
          const suggestionId = String(args[0] ?? '');
          const quote = (args[1] ?? {}) as Quote;
          const proposedText = String(args[2] ?? '');
          const content = contentRef;
          if (content === null) {
            return false;
          }
          let shown = false;
          editor.update(() => {
            const { range } = resolve(flatten(content), quote);
            if (range === null) {
              return;
            }
            const selection = $selectionFromDomRange(editor, range);
            if (selection === null) {
              return;
            }
            $setSelection(selection);
            const active = $getSelection();
            if (!$isRangeSelection(active)) {
              return;
            }
            // extract() splits the boundary text nodes and returns the covered nodes.
            const covered = active.extract().filter($isTextNode);
            let last: TextNode | null = null;
            for (const node of covered) {
              if ($isSuggestionNode(node)) {
                // Already part of a suggestion — leave it, but track it as an anchor.
                last = node;
                continue;
              }
              const deletion = $createSuggestionDeletionNode(node.getTextContent(), suggestionId);
              deletion.setFormat(node.getFormat());
              deletion.setStyle(node.getStyle());
              node.replace(deletion);
              last = deletion;
            }
            if (last === null) {
              return;
            }
            last.insertAfter($createSuggestionInsertionNode(proposedText, suggestionId));
            shown = true;
          }, { discrete: true });
          return shown;
        }

        case 'remove': {
          const suggestionId = String(args[0] ?? '');
          return removeSuggestions(editor, suggestionId);
        }

        case 'clear':
          return removeSuggestions(editor, null);

        default:
          return undefined;
      }
    },
  };
}

/**
 * Removes the suggestion `id` (or, with null, every suggestion): each deletion node is
 * restored to a plain TextNode carrying its original text/format/style, and each insertion
 * node is dropped — leaving the document exactly as it was before the suggestion. Returns
 * how many suggestion nodes were cleared.
 */
function removeSuggestions(editor: LexicalEditor, id: string | null): number {
  let removed = 0;
  editor.update(() => {
    // Snapshot first: replacing/removing mutates the sets we are walking.
    for (const node of [...$nodesOfType(SuggestionDeletionNode)]) {
      if (id !== null && node.getSuggestionId() !== id) {
        continue;
      }
      const original = $createTextNode(node.getTextContent());
      original.setFormat(node.getFormat());
      original.setStyle(node.getStyle());
      node.replace(original);
      removed++;
    }
    for (const node of [...$nodesOfType(SuggestionInsertionNode)]) {
      if (id !== null && node.getSuggestionId() !== id) {
        continue;
      }
      node.remove();
      removed++;
    }
  }, { discrete: true });
  return removed;
}
