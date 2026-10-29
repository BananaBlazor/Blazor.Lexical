// ---------------------------------------------------------------------------
// Page break: a block-level marker that forces a new page when the document is
// printed or converted (HTML-to-PDF, Word).
//
// The node below ports the Lexical playground's `PageBreakNode` (Meta Platforms,
// MIT), keeping its `"page-break"` node type and `<figure type="page-break">` DOM so
// documents interoperate with other Lexical apps. The playground's React decorator
// component is not ported: selection and delete come from the shared atomic-block
// helper, as for the horizontal rule.
//
// As with hr.ts, INSERT_PAGE_BREAK_COMMAND is a distinct object from the
// playground's, so code holding that command will not reach our handler.
// ---------------------------------------------------------------------------

import { $findMatchingParent, $insertNodeToNearestRoot, addClassNamesToElement } from '@lexical/utils';
import {
  $create,
  $createParagraphNode,
  $getDocument,
  $getSelection,
  $isElementNode,
  $isRangeSelection,
  $isRootNode,
  $isRootOrShadowRoot,
  $splitNode,
  COMMAND_PRIORITY_EDITOR,
  createCommand,
  DecoratorNode,
  type DOMConversionMap,
  type DOMConversionOutput,
  type DOMExportOutput,
  type EditorConfig,
  type ElementNode,
  type LexicalCommand,
  type LexicalEditor,
  type LexicalNode,
  type SerializedLexicalNode,
} from 'lexical';
import { registerAtomicBlockSelection } from './atomic-block';
import type { LexicalExtensionModule, LexicalExtensionSetup } from './extension';

/** The serialized form of a {@link PageBreakNode} — no fields beyond the base. */
export type SerializedPageBreakNode = SerializedLexicalNode;

/** Inserts a {@link PageBreakNode} at the current selection. */
export const INSERT_PAGE_BREAK_COMMAND: LexicalCommand<void> = createCommand(
  'INSERT_PAGE_BREAK_COMMAND',
);

// Word puts a page break on the clipboard as `<br style="page-break-before:always">`.
const WORD_PAGE_BREAK_STYLE = /(?:^|;)\s*(?:page-break-before\s*:\s*always|break-before\s*:\s*page)\b/i;

/**
 * A forced page break, rendered as `<figure type="page-break">`.
 *
 * Ported from the Lexical playground's `PageBreakNode` (Copyright (c) Meta
 * Platforms, Inc. and affiliates; MIT). The type string and DOM shape are kept
 * identical so documents round-trip with other Lexical apps.
 */
export class PageBreakNode extends DecoratorNode<unknown> {
  static getType(): string {
    return 'page-break';
  }

  static clone(node: PageBreakNode): PageBreakNode {
    return new PageBreakNode(node.__key);
  }

  static importJSON(serializedNode: SerializedPageBreakNode): PageBreakNode {
    return $createPageBreakNode().updateFromJSON(serializedNode);
  }

  static importDOM(): DOMConversionMap | null {
    return {
      figure: (domNode: HTMLElement) =>
        domNode.getAttribute('type') === PageBreakNode.getType()
          ? { conversion: $convertPageBreakElement, priority: 3 }
          : null,
      // Outranks the default `br` → LineBreakNode only when the style matches.
      br: (domNode: HTMLElement) =>
        WORD_PAGE_BREAK_STYLE.test(domNode.getAttribute('style') ?? '')
          ? { conversion: $convertPageBreakElement, priority: 1 }
          : null,
    };
  }

  exportDOM(): DOMExportOutput {
    const element = $getDocument().createElement('figure');
    element.setAttribute('type', PageBreakNode.getType());
    // Paginates the exported HTML when printed or converted, with no extra CSS.
    element.setAttribute('style', 'break-after: page; page-break-after: always');
    return { element };
  }

  createDOM(config: EditorConfig): HTMLElement {
    const element = $getDocument().createElement('figure');
    element.setAttribute('type', PageBreakNode.getType());
    element.contentEditable = 'false';
    addClassNamesToElement(element, config.theme.pageBreak as string | undefined);
    return element;
  }

  getTextContent(): string {
    return '\n';
  }

  isInline(): false {
    return false;
  }

  updateDOM(): boolean {
    return false;
  }

  decorate(): null {
    return null;
  }
}

function $convertPageBreakElement(): DOMConversionOutput {
  return { node: $createPageBreakNode() };
}

/** Creates a {@link PageBreakNode}. */
export function $createPageBreakNode(): PageBreakNode {
  return $create(PageBreakNode);
}

/** True when `node` is a {@link PageBreakNode}, narrowing its type. */
export function $isPageBreakNode(node: LexicalNode | null | undefined): node is PageBreakNode {
  return node instanceof PageBreakNode;
}

/** True when `element` holds no leaf at any depth — a husk left behind by a split. */
function $isHollow(element: ElementNode): boolean {
  return element.getChildren().every((child) => $isElementNode(child) && $isHollow(child));
}

/** True inside a shadow root other than the document root (a table cell). */
function $isInShadowRoot(node: LexicalNode): boolean {
  return $findMatchingParent(node, (n) => $isRootOrShadowRoot(n) && !$isRootNode(n)) !== null;
}

/** The ancestor of `node` (or `node` itself) that is a direct child of the root. */
function $getRootChild(node: LexicalNode): LexicalNode {
  let top = node;
  while (!$isRootNode(top.getParentOrThrow())) {
    top = top.getParentOrThrow();
  }
  return top;
}

/**
 * Keeps a page break a direct child of the root. Inside lists, quotes and paragraphs
 * the ancestors are split around it; inside a shadow root (a table cell), where
 * splitting would tear the table, it moves after the top-level block instead. A
 * paragraph is appended when nothing follows, so the caret has somewhere to go.
 */
function $hoistToRoot(node: PageBreakNode): void {
  const parent = node.getParent();
  if (parent === null || $isRootNode(parent)) {
    return;
  }

  if ($isInShadowRoot(node)) {
    $getRootChild(parent).insertAfter(node);
  } else {
    const [left, right] = $splitNode(parent, node.getIndexWithinParent());
    right.insertBefore(node);
    for (const half of [left, right]) {
      if (half !== null && $isHollow(half)) {
        half.remove();
      }
    }
  }

  if (node.getNextSibling() === null) {
    node.insertAfter($createParagraphNode());
  }
}

/**
 * The page-break feature as a `LexicalExtensionFactory`. Contributes the node and its
 * theme keys, and performs no interop in either direction — the C# half's only call
 * is the `insert` escape hatch below.
 */
export default function pageBreakExtension(setup: LexicalExtensionSetup): LexicalExtensionModule {
  const { mergeRegister } = setup.utils;
  let currentEditor: LexicalEditor | undefined;

  return {
    name: 'blazor-lexical/page-break',
    nodes: [PageBreakNode],
    theme: {
      pageBreak: 'blazor-lexical__page-break',
      pageBreakSelected: 'blazor-lexical__page-break--selected',
    },
    register: ({ editor }) => {
      currentEditor = editor;
      const selectedClass =
        (editor._config.theme.pageBreakSelected as string | undefined) ??
        'blazor-lexical__page-break--selected';

      return mergeRegister(
        editor.registerCommand(
          INSERT_PAGE_BREAK_COMMAND,
          () => {
            const node = $createPageBreakNode();
            const selection = $getSelection();
            const anchor = $isRangeSelection(selection) ? selection.anchor.getNode() : null;
            if (anchor !== null && $isInShadowRoot(anchor)) {
              // In a table cell, go straight after the table: inserting at the caret
              // would split the cell's paragraph before the transform moves the break.
              $getRootChild(anchor).insertAfter(node);
            } else {
              $insertNodeToNearestRoot(node);
            }
            if (node.getNextSibling() === null) {
              node.insertAfter($createParagraphNode());
            }
            node.selectNext(0, 0);
            return true;
          },
          COMMAND_PRIORITY_EDITOR,
        ),
        editor.registerNodeTransform(PageBreakNode, $hoistToRoot),
        registerAtomicBlockSelection(editor, $isPageBreakNode, selectedClass),
        () => {
          currentEditor = undefined;
        },
      );
    },
    invoke: (method) => {
      if (method === 'insert' && currentEditor !== undefined) {
        currentEditor.dispatchCommand(INSERT_PAGE_BREAK_COMMAND, undefined);
      }
      return undefined;
    },
  };
}
