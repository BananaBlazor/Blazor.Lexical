// ---------------------------------------------------------------------------
// Indent policy: capped indent/outdent with exclusions, shared per editor by
// `LexicalIndent` and `LexicalTabIndent`, plus the disabled state of the
// `indent:in` / `indent:out` toolbar controls.
//
// Extensions cannot see each other and built-ins register in markup order, so the
// two components do not coordinate directly. Each registers a policy here instead:
// `LexicalIndent`'s is authoritative, `LexicalTabIndent`'s a fallback used only while
// no authoritative one exists. One pair of CRITICAL-priority handlers per editor reads
// the active policy at dispatch time, so declaration order does not matter.
//
// The handlers claim the command (return true) whenever the selection holds a block
// the policy governs, even when nothing moves: a `false` would let @lexical/rich-text's
// stock EDITOR-priority handlers run and bypass the cap and the exclusions.
//
// Block decorators opt in by implementing canIndent/getIndent/setIndent (extension.ts
// `IndentableBlockNode`); only the authoritative policy indents them.
// ---------------------------------------------------------------------------

import { $isListItemNode, $isListNode } from '@lexical/list';
import { $findMatchingParent, mergeRegister } from '@lexical/utils';
import {
  $getSelection,
  $isDecoratorNode,
  $isElementNode,
  $isNodeSelection,
  $isRangeSelection,
  $isRootOrShadowRoot,
  COMMAND_PRIORITY_CRITICAL,
  COMMAND_PRIORITY_LOW,
  INDENT_CONTENT_COMMAND,
  KEY_BACKSPACE_COMMAND,
  OUTDENT_CONTENT_COMMAND,
  type BaseSelection,
  type DecoratorNode,
  type ElementNode,
  type LexicalEditor,
  type LexicalNode,
} from 'lexical';
import type { IndentableBlockNode, LexicalExtensionModule, LexicalExtensionSetup } from './extension';
import { setElDisabled } from './toolbar-dom';

/** The rules the indent/outdent handlers apply. */
export interface IndentPolicy {
  /** Levels below this are allowed; undefined means no cap. */
  maxIndent?: number;
  /** When false, list items are never indented or outdented. */
  indentListItems: boolean;
}

interface IndentState {
  authoritative: IndentPolicy | null;
  fallback: IndentPolicy | null;
  refs: number;
  unregisterHandlers: () => void;
}

const states = new WeakMap<LexicalEditor, IndentState>();

type IndentableDecorator = DecoratorNode<unknown> & IndentableBlockNode;
type IndentableBlock = ElementNode | IndentableDecorator;

/** A block decorator that implements {@link IndentableBlockNode} and opts in. */
function $isIndentableDecorator(node: LexicalNode | null | undefined): node is IndentableDecorator {
  if (!$isDecoratorNode(node) || node.isInline()) {
    return false;
  }
  const candidate = node as Partial<IndentableBlockNode>;
  return (
    typeof candidate.canIndent === 'function' &&
    typeof candidate.getIndent === 'function' &&
    typeof candidate.setIndent === 'function' &&
    candidate.canIndent()
  );
}

/** The policy the handlers apply now: the authoritative one, else the fallback. */
function activePolicy(editor: LexicalEditor): { policy: IndentPolicy; authoritative: boolean } | null {
  const state = states.get(editor);
  if (state?.authoritative) {
    return { policy: state.authoritative, authoritative: true };
  }
  return state?.fallback ? { policy: state.fallback, authoritative: false } : null;
}

/**
 * The blocks an indent/outdent of `selection` visits: for a range selection, the
 * nearest non-inline element ancestor of every node (as `$handleIndentAndOutdent`
 * walks them) plus, with `decorators`, opted-in decorators whose parent is the root
 * or a shadow root (`$findMatchingParent` stops at the root, so upstream never reaches
 * those; a decorator inside a list item is covered by its containing block). For a
 * node selection, the opted-in decorators in it.
 */
function $collectBlocks(selection: BaseSelection | null, decorators: boolean): IndentableBlock[] {
  const blocks: IndentableBlock[] = [];
  if ($isNodeSelection(selection)) {
    if (decorators) {
      blocks.push(...selection.getNodes().filter($isIndentableDecorator));
    }
    return blocks;
  }
  if (!$isRangeSelection(selection)) {
    return blocks;
  }
  const seen = new Set<string>();
  const add = (block: IndentableBlock): void => {
    if (!seen.has(block.getKey())) {
      seen.add(block.getKey());
      blocks.push(block);
    }
  };
  for (const node of selection.getNodes()) {
    if (decorators && $isIndentableDecorator(node) && $isRootOrShadowRoot(node.getParent())) {
      add(node);
      continue;
    }
    const block = $findMatchingParent(
      node,
      (parent): parent is ElementNode => $isElementNode(parent) && !parent.isInline(),
    );
    if (block !== null && block.canIndent()) {
      add(block);
    }
  }
  return blocks;
}

/** The blocks of the current selection the policy governs, and which of them may move. */
function $governedBlocks(
  policy: IndentPolicy,
  authoritative: boolean,
): { governed: boolean; movable: IndentableBlock[] } {
  const blocks = $collectBlocks($getSelection(), authoritative);
  return {
    governed: blocks.length > 0,
    movable: policy.indentListItems ? blocks : blocks.filter((b) => !$isListItemNode(b)),
  };
}

function $canIndentBlock(block: IndentableBlock, policy: IndentPolicy): boolean {
  return policy.maxIndent === undefined || block.getIndent() + 1 < policy.maxIndent;
}

function $canOutdentBlock(block: IndentableBlock): boolean {
  return block.getIndent() > 0;
}

function registerHandlers(editor: LexicalEditor): () => void {
  const handler = (outdent: boolean) => (): boolean => {
    const active = activePolicy(editor);
    if (active === null) {
      return false;
    }
    const { governed, movable } = $governedBlocks(active.policy, active.authoritative);
    for (const block of movable) {
      if (outdent) {
        if ($canOutdentBlock(block)) {
          block.setIndent(block.getIndent() - 1);
        }
      } else if ($canIndentBlock(block, active.policy)) {
        block.setIndent(block.getIndent() + 1);
      }
    }
    return governed;
  };
  return mergeRegister(
    editor.registerCommand(INDENT_CONTENT_COMMAND, handler(false), COMMAND_PRIORITY_CRITICAL),
    editor.registerCommand(OUTDENT_CONTENT_COMMAND, handler(true), COMMAND_PRIORITY_CRITICAL),
    // @lexical/list outdents a nested item on Backspace at its front by calling
    // $handleOutdent directly, not through OUTDENT_CONTENT_COMMAND. Claim that key first
    // when list items are excluded, as the outdent handler would.
    editor.registerCommand(
      KEY_BACKSPACE_COMMAND,
      (event) => {
        const active = activePolicy(editor);
        if (active === null || active.policy.indentListItems || !$isAtFrontOfNestedListItem()) {
          return false;
        }
        event.preventDefault();
        return true;
      },
      COMMAND_PRIORITY_LOW,
    ),
  );
}

/**
 * Whether the selection is a caret at the front of a nested list item: the case
 * @lexical/list's Backspace handler turns into an outdent (`ListItemNode.collapseAtStart`).
 */
function $isAtFrontOfNestedListItem(): boolean {
  const selection = $getSelection();
  if (!$isRangeSelection(selection) || !selection.isCollapsed() || selection.anchor.offset !== 0) {
    return false;
  }
  let node: LexicalNode | null = selection.anchor.getNode();
  while (node !== null && !$isListItemNode(node)) {
    if (node.getPreviousSibling() !== null) {
      return false;
    }
    node = node.getParent();
  }
  return (
    $isListItemNode(node) &&
    !$isListNode(node.getFirstChild()) &&
    $isListItemNode(node.getParent()?.getParent())
  );
}

/**
 * Installs `policy` for `editor` (`authoritative` for `LexicalIndent`, otherwise the
 * `LexicalTabIndent` fallback) and the shared handlers, ref-counted. Returns the teardown.
 */
export function registerIndentPolicy(
  editor: LexicalEditor,
  policy: IndentPolicy,
  authoritative: boolean,
): () => void {
  let state = states.get(editor);
  if (state === undefined) {
    state = { authoritative: null, fallback: null, refs: 0, unregisterHandlers: () => {} };
    states.set(editor, state);
  }
  if (state.refs++ === 0) {
    state.unregisterHandlers = registerHandlers(editor);
  }
  if (authoritative) {
    state.authoritative = policy;
  } else {
    state.fallback = policy;
  }
  const owned = state;
  return () => {
    if (authoritative) {
      owned.authoritative = null;
    } else {
      owned.fallback = null;
    }
    if (--owned.refs === 0) {
      owned.unregisterHandlers();
      states.delete(editor);
    }
  };
}

/**
 * Whether Tab should map to indent/outdent for a node selection: true when an
 * authoritative policy exists and the selection holds an opted-in decorator.
 */
export function $isIndentableNodeSelection(
  editor: LexicalEditor,
  selection: BaseSelection | null,
): boolean {
  return (
    states.get(editor)?.authoritative != null &&
    $isNodeSelection(selection) &&
    selection.getNodes().some($isIndentableDecorator)
  );
}

/** The `indent` extension's options payload, mirrored from C# `IndentExtensionOptionsDto`. */
export interface IndentOptionsDto {
  maxIndent?: number | null;
  indentListItems?: boolean | null;
  maxIndentReason?: string | null;
  atMarginReason?: string | null;
  notIndentableReason?: string | null;
}

const INDENT_SELECTOR = '[data-lexical-command="indent:in"]';
const OUTDENT_SELECTOR = '[data-lexical-command="indent:out"]';

/**
 * Applies `reason` (or `null` to enable) to a control: `data-lexical-disabled` carries
 * the reason, and `title`/`aria-label` show it, with the originals kept in
 * `data-lexical-title`/`data-lexical-aria-label` and restored on re-enable.
 */
function paintControl(el: Element, reason: string | null): void {
  const disabled = reason !== null;
  if (disabled && !el.hasAttribute('data-lexical-title')) {
    el.setAttribute('data-lexical-title', el.getAttribute('title') ?? '');
    el.setAttribute('data-lexical-aria-label', el.getAttribute('aria-label') ?? '');
  }
  setElDisabled(el, disabled, reason ?? '');
  if (disabled) {
    el.setAttribute('title', reason);
    el.setAttribute('aria-label', reason);
  } else if (el.hasAttribute('data-lexical-title')) {
    restoreAttribute(el, 'title', 'data-lexical-title');
    restoreAttribute(el, 'aria-label', 'data-lexical-aria-label');
  }
}

function restoreAttribute(el: Element, name: string, stash: string): void {
  const value = el.getAttribute(stash);
  if (value) {
    el.setAttribute(name, value);
  } else {
    el.removeAttribute(name);
  }
  el.removeAttribute(stash);
}

/**
 * The indent extension as a `LexicalExtensionFactory` — register-only, and silent in
 * both directions.
 */
export default function indentExtension(setup: LexicalExtensionSetup): LexicalExtensionModule {
  const raw = setup.options as IndentOptionsDto | undefined;
  const policy: IndentPolicy = {
    maxIndent: raw?.maxIndent ?? undefined,
    indentListItems: raw?.indentListItems ?? true,
  };
  const reasons = {
    maxIndent: raw?.maxIndentReason ?? 'Maximum indent reached',
    atMargin: raw?.atMarginReason ?? 'Already at the left margin',
    notIndentable: raw?.notIndentableReason ?? 'Indenting does not apply to this selection',
  };

  return {
    name: 'blazor-lexical/indent',
    register: ({ editor, root }) => {
      // Synchronous, like core's refreshToolbar: an async paint would race the
      // assertions (and the user) reading the toolbar right after a keystroke.
      const refresh = (): void => {
        const [indentReason, outdentReason] = editor.getEditorState().read(() => {
          if (!editor.isEditable()) {
            return [reasons.notIndentable, reasons.notIndentable];
          }
          const { governed, movable } = $governedBlocks(policy, true);
          if (!governed || movable.length === 0) {
            return [reasons.notIndentable, reasons.notIndentable];
          }
          return [
            movable.some((b) => $canIndentBlock(b, policy)) ? null : reasons.maxIndent,
            movable.some($canOutdentBlock) ? null : reasons.atMargin,
          ];
        });
        root.querySelectorAll(INDENT_SELECTOR).forEach((el) => paintControl(el, indentReason));
        root.querySelectorAll(OUTDENT_SELECTOR).forEach((el) => paintControl(el, outdentReason));
      };

      const unregister = mergeRegister(
        registerIndentPolicy(editor, policy, true),
        editor.registerUpdateListener(refresh),
        editor.registerEditableListener(refresh),
      );
      refresh();
      return () => {
        unregister();
        root
          .querySelectorAll(`${INDENT_SELECTOR}, ${OUTDENT_SELECTOR}`)
          .forEach((el) => paintControl(el, null));
      };
    },
  };
}
