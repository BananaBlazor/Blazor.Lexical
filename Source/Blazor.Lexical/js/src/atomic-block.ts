// ---------------------------------------------------------------------------
// Atomic block selection: the click-to-select, Delete/Backspace and selected-state
// painting shared by block-level decorator nodes that have no text of their own
// (horizontal rule, page break).
//
// Upstream drives the selected state with a signal per node key; an update listener
// does the same job here because the number of such nodes in a document is small and
// only the ones whose selected-ness actually changed are touched.
// ---------------------------------------------------------------------------

import { addClassNamesToElement, mergeRegister, removeClassNamesFromElement } from '@lexical/utils';
import {
  $createNodeSelection,
  $getNodeFromDOMNode,
  $getSelection,
  $isNodeSelection,
  $setSelection,
  CLICK_COMMAND,
  COMMAND_PRIORITY_LOW,
  isDOMNode,
  KEY_BACKSPACE_COMMAND,
  KEY_DELETE_COMMAND,
  type LexicalEditor,
  type LexicalNode,
  type NodeKey,
} from 'lexical';

/**
 * Selects `key` on its own, so the node reads as "the thing the caret is on" and
 * Delete/Backspace act on it. `extend` (shift-click) toggles it in an existing node
 * selection instead.
 */
function $selectNode(key: NodeKey, extend: boolean): void {
  const selection = $getSelection();
  const nodeSelection =
    extend && $isNodeSelection(selection) ? selection : $createNodeSelection();
  if (nodeSelection !== selection) {
    $setSelection(nodeSelection);
  }
  if (nodeSelection.has(key)) {
    nodeSelection.delete(key);
  } else {
    nodeSelection.add(key);
  }
}

/**
 * Registers click selection, Delete/Backspace removal and `selectedClass` painting
 * for the nodes `isNode` matches. Returns the teardown, which also strips any class
 * it painted, since the DOM may outlive the editor.
 */
export function registerAtomicBlockSelection(
  editor: LexicalEditor,
  isNode: (node: LexicalNode | null | undefined) => node is LexicalNode,
  selectedClass: string,
): () => void {
  const $deleteSelected = (event: KeyboardEvent | null): boolean => {
    const selection = $getSelection();
    if (!$isNodeSelection(selection)) {
      return false;
    }
    let handled = false;
    for (const node of selection.getNodes()) {
      if (isNode(node)) {
        node.remove();
        handled = true;
      }
    }
    if (handled) {
      // Otherwise the browser's own Backspace/Delete also edits the text at the caret.
      event?.preventDefault();
    }
    return handled;
  };

  let painted = new Set<NodeKey>();
  const setClass = (key: NodeKey, on: boolean): void => {
    const dom = editor.getElementByKey(key);
    if (dom !== null) {
      (on ? addClassNamesToElement : removeClassNamesFromElement)(dom, selectedClass);
    }
  };

  const paint = (): void => {
    const next = editor.getEditorState().read(() => {
      const selection = $getSelection();
      const keys = new Set<NodeKey>();
      if ($isNodeSelection(selection)) {
        for (const node of selection.getNodes()) {
          if (isNode(node)) {
            keys.add(node.getKey());
          }
        }
      }
      return keys;
    });
    for (const key of painted) {
      if (!next.has(key)) {
        setClass(key, false);
      }
    }
    for (const key of next) {
      if (!painted.has(key)) {
        setClass(key, true);
      }
    }
    painted = next;
  };

  return mergeRegister(
    editor.registerCommand(
      CLICK_COMMAND,
      (event: MouseEvent) => {
        if (!isDOMNode(event.target)) {
          return false;
        }
        const node = $getNodeFromDOMNode(event.target);
        if (!isNode(node)) {
          return false;
        }
        $selectNode(node.getKey(), event.shiftKey);
        return true;
      },
      COMMAND_PRIORITY_LOW,
    ),
    editor.registerCommand(KEY_DELETE_COMMAND, $deleteSelected, COMMAND_PRIORITY_LOW),
    editor.registerCommand(KEY_BACKSPACE_COMMAND, $deleteSelected, COMMAND_PRIORITY_LOW),
    editor.registerUpdateListener(paint),
    () => {
      for (const key of painted) {
        setClass(key, false);
      }
      painted = new Set();
    },
  );
}
