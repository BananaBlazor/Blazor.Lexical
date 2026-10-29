// ---------------------------------------------------------------------------
// Tab indentation: Tab / Shift+Tab indent and outdent block elements.
//
// `registerTabIndentation` below is ported from `@lexical/extension` (Meta
// Platforms, MIT), minus its signal-typed `maxIndent`/`$canIndent` parameters,
// which exist upstream only to serve the reactive extension wrapper. It is a
// port rather than an import for the same reason as the horizontal rule: that
// package's barrel is not tree-shakeable under our `--splitting` build, so
// importing one function drags LexicalBuilder and every bundled Extension along
// with it. See hr.ts and docs/architecture.md.
//
// Like the TOC and stats extensions this contributes no node and no theme; it is
// a behavior-only module, which is a shape the extension contract accommodates.
//
// This ships as an opt-in component rather than an editor default on purpose:
// binding Tab inside the editor takes it away from keyboard navigation, so an
// editor the consumer did not ask to behave that way must not.
// ---------------------------------------------------------------------------

import { $getNearestBlockElementAncestorOrThrow, mergeRegister } from '@lexical/utils';
import {
  $createRangeSelection,
  $getSelection,
  $isBlockElementNode,
  $isRangeSelection,
  $normalizeSelection__EXPERIMENTAL,
  COMMAND_PRIORITY_EDITOR,
  INDENT_CONTENT_COMMAND,
  INSERT_TAB_COMMAND,
  KEY_TAB_COMMAND,
  OUTDENT_CONTENT_COMMAND,
  type LexicalCommand,
  type LexicalEditor,
  type RangeSelection,
} from 'lexical';
import type { LexicalExtensionModule, LexicalExtensionSetup } from './extension';
import { $isIndentableNodeSelection, registerIndentPolicy } from './indent';

/**
 * Whether Tab should indent the block rather than insert a tab character:
 * true when the selection spans indentable blocks, or sits at a block's start.
 */
function $indentOverTab(selection: RangeSelection): boolean {
  const nodes = selection.getNodes();
  const canIndentBlockNodes = nodes.filter(
    (node) => $isBlockElementNode(node) && node.canIndent(),
  );
  if (canIndentBlockNodes.length > 0) {
    return true;
  }

  const { anchor, focus } = selection;
  const first = focus.isBefore(anchor) ? focus : anchor;
  const firstBlock = $getNearestBlockElementAncestorOrThrow(first.getNode());
  if (firstBlock.canIndent()) {
    const firstBlockKey = firstBlock.getKey();
    let selectionAtStart = $createRangeSelection();
    selectionAtStart.anchor.set(firstBlockKey, 0, 'element');
    selectionAtStart.focus.set(firstBlockKey, 0, 'element');
    selectionAtStart = $normalizeSelection__EXPERIMENTAL(selectionAtStart);
    if (selectionAtStart.anchor.is(first)) {
      return true;
    }
  }
  return false;
}

/**
 * Registers the Tab/Shift+Tab handler, plus `maxIndent` as the fallback indent policy
 * (see indent.ts). Returns the teardown.
 */
function registerTabIndentation(editor: LexicalEditor, maxIndent?: number): () => void {
  return mergeRegister(
    editor.registerCommand<KeyboardEvent>(
      KEY_TAB_COMMAND,
      (event) => {
        const selection = $getSelection();
        let command: LexicalCommand<void>;
        if ($isRangeSelection(selection) && !$indentOverTab(selection)) {
          command = INSERT_TAB_COMMAND;
        } else if ($isRangeSelection(selection) || $isIndentableNodeSelection(editor, selection)) {
          command = event.shiftKey ? OUTDENT_CONTENT_COMMAND : INDENT_CONTENT_COMMAND;
        } else {
          return false;
        }
        event.preventDefault();
        return editor.dispatchCommand(command, undefined);
      },
      COMMAND_PRIORITY_EDITOR,
    ),
    registerIndentPolicy(editor, { maxIndent, indentListItems: true }, false),
  );
}

/** The tab-indent extension's options payload, mirrored from C# `TabIndentExtensionOptionsDto`. */
export interface TabIndentOptionsDto {
  /** Maximum indent depth, or null/absent for no cap. */
  maxIndent?: number | null;
}

/**
 * The tab-indentation feature as a `LexicalExtensionFactory` — register-only, and
 * silent in both directions.
 */
export default function tabIndentExtension(
  setup: LexicalExtensionSetup,
): LexicalExtensionModule {
  const raw = setup.options as TabIndentOptionsDto | undefined;
  const maxIndent = raw?.maxIndent ?? undefined;

  return {
    name: 'blazor-lexical/tab-indent',
    register: ({ editor }) => registerTabIndentation(editor, maxIndent),
  };
}
