// ---------------------------------------------------------------------------
// Horizontal rule: a thematic break as a block-level node.
//
// The node below is ported from `HorizontalRuleExtension` in `@lexical/extension`
// (Meta Platforms, MIT — see LICENSE note on the class). Keeping upstream's
// `"horizontalrule"` node type and `<hr>` DOM conversion verbatim is the point:
// a document we produce deserializes in any other Lexical app, and one produced
// elsewhere loads here.
//
// It is a port rather than an import because importing anything from
// `@lexical/extension` pulls in its whole barrel: under our `--splitting` build
// esbuild does not tree-shake it, so LexicalBuilder, ExtensionRep and every
// bundled Extension survive into a shared chunk — measured at ~25kb to reuse one
// node class. (Note this is NOT an argument about `@preact/signals-core`, which
// is already in our bundle: @lexical/rich-text, history, list, link and html all
// depend on @lexical/extension transitively. The weight here is the barrel.)
// Selection and delete come from the shared atomic-block helper, which replaces
// upstream's signal-driven handling (signal-driven only to serve its reactive wrapper).
//
// The one thing a port cannot preserve is *command identity*: our
// INSERT_HORIZONTAL_RULE_COMMAND is a distinct object from upstream's, so code
// holding upstream's command will not reach our handler (the node and its
// serialized form still interoperate). See docs/architecture.md.
// ---------------------------------------------------------------------------

import { $insertNodeToNearestRoot, addClassNamesToElement } from '@lexical/utils';
import {
  $create,
  $getDocument,
  COMMAND_PRIORITY_EDITOR,
  createCommand,
  DecoratorNode,
  type DOMConversionMap,
  type DOMConversionOutput,
  type DOMExportOutput,
  type EditorConfig,
  type LexicalCommand,
  type LexicalEditor,
  type LexicalNode,
  type SerializedLexicalNode,
} from 'lexical';
import { registerAtomicBlockSelection } from './atomic-block';
import type { LexicalExtensionModule, LexicalExtensionSetup } from './extension';

/** The serialized form of a {@link HorizontalRuleNode} — no fields beyond the base. */
export type SerializedHorizontalRuleNode = SerializedLexicalNode;

/** Inserts a {@link HorizontalRuleNode} at the current selection. */
export const INSERT_HORIZONTAL_RULE_COMMAND: LexicalCommand<void> = createCommand(
  'INSERT_HORIZONTAL_RULE_COMMAND',
);

/**
 * A thematic break, rendered as `<hr>`.
 *
 * Ported from `@lexical/extension`'s `HorizontalRuleNode` (Copyright (c) Meta
 * Platforms, Inc. and affiliates; MIT). The type string, DOM import/export and
 * theme key are kept identical so documents round-trip with upstream Lexical.
 */
export class HorizontalRuleNode extends DecoratorNode<unknown> {
  static getType(): string {
    return 'horizontalrule';
  }

  static clone(node: HorizontalRuleNode): HorizontalRuleNode {
    return new HorizontalRuleNode(node.__key);
  }

  static importJSON(serializedNode: SerializedHorizontalRuleNode): HorizontalRuleNode {
    return $createHorizontalRuleNode().updateFromJSON(serializedNode);
  }

  static importDOM(): DOMConversionMap | null {
    return { hr: () => ({ conversion: $convertHorizontalRuleElement, priority: 0 }) };
  }

  exportDOM(): DOMExportOutput {
    return { element: $getDocument().createElement('hr') };
  }

  createDOM(config: EditorConfig): HTMLElement {
    const element = $getDocument().createElement('hr');
    addClassNamesToElement(element, config.theme.hr);
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
}

function $convertHorizontalRuleElement(): DOMConversionOutput {
  return { node: $createHorizontalRuleNode() };
}

/** Creates a {@link HorizontalRuleNode}. */
export function $createHorizontalRuleNode(): HorizontalRuleNode {
  return $create(HorizontalRuleNode);
}

/** True when `node` is a {@link HorizontalRuleNode}, narrowing its type. */
export function $isHorizontalRuleNode(
  node: LexicalNode | null | undefined,
): node is HorizontalRuleNode {
  return node instanceof HorizontalRuleNode;
}

/**
 * The horizontal-rule feature as a `LexicalExtensionFactory`. Contributes the node
 * and its theme keys, and performs no interop in either direction — the C# half's
 * only call is the `insert` escape hatch below.
 */
export default function hrExtension(setup: LexicalExtensionSetup): LexicalExtensionModule {
  const { mergeRegister } = setup.utils;
  let currentEditor: LexicalEditor | undefined;

  return {
    name: 'blazor-lexical/hr',
    nodes: [HorizontalRuleNode],
    // Upstream's key names (`config.theme.hr`, `theme.hrSelected`), so a theme
    // written for another Lexical app maps over unchanged.
    theme: {
      hr: 'blazor-lexical__hr',
      hrSelected: 'blazor-lexical__hr--selected',
    },
    register: ({ editor }) => {
      currentEditor = editor;
      const selectedClass =
        (editor._config.theme.hrSelected as string | undefined) ?? 'blazor-lexical__hr--selected';

      return mergeRegister(
        editor.registerCommand(
          INSERT_HORIZONTAL_RULE_COMMAND,
          () => {
            $insertNodeToNearestRoot($createHorizontalRuleNode());
            return true;
          },
          COMMAND_PRIORITY_EDITOR,
        ),
        registerAtomicBlockSelection(editor, $isHorizontalRuleNode, selectedClass),
        () => {
          currentEditor = undefined;
        },
      );
    },
    invoke: (method) => {
      if (method === 'insert' && currentEditor !== undefined) {
        currentEditor.dispatchCommand(INSERT_HORIZONTAL_RULE_COMMAND, undefined);
      }
      return undefined;
    },
  };
}
