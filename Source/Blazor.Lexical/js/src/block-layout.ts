// ---------------------------------------------------------------------------
// Per-block positioning primitives — the two pieces of math the built-in block
// gutter (overlays.ts) needs and, deliberately, the two pieces an app building
// its own persistent per-block decorations needs too.
//
// This module is pure editor/DOM geometry: it knows nothing about hover, rails,
// grace windows, or drag. `overlays.ts` builds all of that on top of it, and the
// same two functions are surfaced to extensions as `ctx.blockLayout` (index.ts),
// so the gutter and an app-authored extension share one source of truth for
// "what are the blocks" and "where does something anchored to a block go".
// ---------------------------------------------------------------------------

import { $getNearestNodeFromDOMNode, type LexicalEditor } from 'lexical';
import type { LexicalBlockAnchor, LexicalBlockInfo } from './extension';

/** Gap (px) between an anchor edge and an element placed against it, and between
 *  stacked elements sharing the same spot. */
export const GUTTER_GAP = 4;

/**
 * Resolves the top-level node key for a content-surface child element, or null when
 * the DOM node maps to no Lexical node (a non-block, or the root itself). Runs its own
 * `editor.read` — not `getEditorState().read` — because `$getNearestNodeFromDOMNode`
 * needs the *active editor* set to resolve a DOM node's key. The single owner of this
 * resolution: anything wanting "which block is this element" should call here.
 */
export function keyForBlockElement(editor: LexicalEditor, blockEl: HTMLElement): string | null {
  return editor.read(() => {
    const node = $getNearestNodeFromDOMNode(blockEl);
    const top = node === null ? null : node.getTopLevelElement();
    return top === null ? null : top.getKey();
  });
}

/**
 * Every top-level block in the editor, in document order, each paired with its DOM
 * element, its index among the content surface's children, and its lowercased tag
 * name (the same `blockType` convention the hover push uses). A live snapshot read
 * fresh from the current DOM/editor state — not cached. Children that resolve to no
 * Lexical node are skipped, so every entry has a real node key.
 */
export function listTopLevelBlocks(
  editor: LexicalEditor,
  contentEl: HTMLElement,
): LexicalBlockInfo[] {
  const children = Array.from(contentEl.children) as HTMLElement[];
  // One read spanning every child rather than one per child: the active-editor
  // resolution is the same for all of them.
  return editor.read(() => {
    const blocks: LexicalBlockInfo[] = [];
    children.forEach((element, index) => {
      const node = $getNearestNodeFromDOMNode(element);
      const top = node === null ? null : node.getTopLevelElement();
      if (top === null) {
        return;
      }
      blocks.push({
        key: top.getKey(),
        element,
        index,
        type: element.tagName.toLowerCase(),
      });
    });
    return blocks;
  });
}

/**
 * The root-relative CSS offset for an element of `sizePx` anchored at `spot`, honoring
 * the same content-padding / inside-clamp / outside-hangs-off-the-card math the
 * built-in block gutter uses:
 *
 * - `*-inside` anchors to the **text column** — the content box inset by its own
 *   padding, the empty margin `.blazor-lexical__content` reserves — and is clamped to
 *   the card, so an element wider than that band slides over the text rather than
 *   escaping the editor.
 * - `*-outside` anchors to the **card edge** and hangs off it, into the page, unclamped.
 *
 * `consumedPx` is the accumulated size of anything already stacked at this spot for this
 * block, so a second item lands further out than the first.
 *
 * For today's horizontal spots this is a CSS `left`; the axis is implied by `spot`, so a
 * future vertical spot (`'above'`/`'below'`) becomes an added `case` returning a `top`
 * rather than a new parameter.
 */
export function computeBlockAnchor(
  root: HTMLElement,
  contentEl: HTMLElement,
  spot: LexicalBlockAnchor,
  sizePx: number,
  consumedPx = 0,
): number {
  const rootRect = root.getBoundingClientRect();
  const contentRect = contentEl.getBoundingClientRect();
  const style = getComputedStyle(contentEl);
  const offset = consumedPx + GUTTER_GAP;

  switch (spot) {
    case 'left-inside':
    case 'left-outside': {
      // Left spots grow leftward, so the element's own size counts against the edge.
      const edge =
        spot === 'left-inside'
          ? contentRect.left + parseFloat(style.paddingLeft) - rootRect.left
          : 0;
      const ideal = edge - offset - sizePx;
      // Inside clamps to the card; outside is free to hang into the page.
      return spot === 'left-inside' ? Math.max(0, Math.min(ideal, rootRect.width - sizePx)) : ideal;
    }
    case 'right-inside':
    case 'right-outside': {
      const edge =
        spot === 'right-inside'
          ? contentRect.right - parseFloat(style.paddingRight) - rootRect.left
          : rootRect.width;
      const ideal = edge + offset;
      return spot === 'right-inside' ? Math.max(0, Math.min(ideal, rootRect.width - sizePx)) : ideal;
    }
  }
}
