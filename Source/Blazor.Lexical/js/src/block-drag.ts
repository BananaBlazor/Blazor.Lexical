// ---------------------------------------------------------------------------
// The block-gutter drag engine — the policy-aware half of the drag-to-reorder the
// gutter (overlays.ts) offers. block-layout.ts owns "where does a decoration anchored
// to a block go"; this owns "what is draggable, where may it land, and how does it
// move". Both are pure editor/DOM logic with no knowledge of rails, hover grace, or
// the grip markup — overlays.ts wires those to these functions.
//
// A consumer opts into dragging *nested* block-level nodes (a <li>, a cell, a column)
// by installing a BlockDragPolicy through ctx.blockDrag (index.ts). With no policy the
// defaults here reproduce the historic top-level-only behavior exactly, so overlays.ts
// runs one code path for both: "no policy" is just the empty policy `{}`.
// ---------------------------------------------------------------------------

import {
  $getNearestNodeFromDOMNode,
  $getRoot,
  $isElementNode,
  type LexicalEditor,
  type LexicalNode,
} from 'lexical';
import type { BlockDragGap, BlockDragPolicy } from './extension';
import type { HoveredBlock } from './overlays';

/** The top-level block element under `clientY`, or null — a Y-only scan of the root's
 *  direct children, the historic (policy-free) hit-test. Shared with overlays.ts. */
export function blockElementAt(contentEl: HTMLElement, clientY: number): HTMLElement | null {
  for (const child of Array.from(contentEl.children) as HTMLElement[]) {
    const rect = child.getBoundingClientRect();
    if (clientY >= rect.top && clientY <= rect.bottom) {
      return child;
    }
  }
  return null;
}

/**
 * The policy-aware hover resolution: the block the grip should attach to at `(x, y)`.
 * The deepest element under the pointer (falling back to the top-level block by `y` when
 * the pointer is over a rail rather than the text) resolves to its nearest Lexical node,
 * which `policy.source` maps to the draggable node — a nested child, when the policy says
 * so. Returns that node's element, key and index among its parent's children.
 *
 * Runs its own `editor.read`. Only called when `policy.source` is defined; the default
 * (top-level) hover keeps overlays.ts's own Y-scan path untouched.
 */
export function resolveActiveBlock(
  editor: LexicalEditor,
  contentEl: HTMLElement,
  x: number,
  y: number,
  policy: BlockDragPolicy,
): HoveredBlock | null {
  let seed = document.elementFromPoint(x, y) as HTMLElement | null;
  if (seed === null || !contentEl.contains(seed)) {
    // The pointer is off the text — over a rail, say, on the way to the grip. Re-probe the
    // *middle of the text column* at the same Y so a nested block still resolves (a plain
    // top-level scan would collapse a `<li>` back to its whole `<ul>`).
    const rect = contentEl.getBoundingClientRect();
    seed = document.elementFromPoint(rect.left + rect.width / 2, y) as HTMLElement | null;
  }
  if (seed === null || !contentEl.contains(seed)) {
    return null;
  }
  const seedEl = seed;
  return editor.read(() => {
    const node = $getNearestNodeFromDOMNode(seedEl);
    if (node === null) {
      return null;
    }
    const source = policy.source
      ? policy.source({ element: seedEl, node })
      : node.getTopLevelElement();
    if (source === null) {
      return null;
    }
    const key = source.getKey();
    const element = editor.getElementByKey(key);
    if (element === null) {
      return null;
    }
    const parent = source.getParent();
    const index =
      parent === null ? 0 : parent.getChildren().findIndex((c) => c.getKey() === key);
    return { element, key, index };
  });
}

/** The default drop gaps: before and after every top-level block. Must run in an editor
 *  read/update context (it reads the tree). */
export function defaultTargets(): BlockDragGap[] {
  const root = $getRoot();
  const count = root.getChildrenSize();
  const gaps: BlockDragGap[] = [];
  for (let index = 0; index <= count; index++) {
    gaps.push({ parent: root, index });
  }
  return gaps;
}

/** The default move: place `dragged` at `gap.index` among `gap.parent`'s children, as a
 *  single reparenting node move (Lexical moves an attached node rather than cloning it).
 *  Must run inside `editor.update()`. */
export function defaultDrop(dragged: LexicalNode, gap: BlockDragGap): void {
  const parent = gap.parent;
  if (!$isElementNode(parent)) {
    return;
  }
  const children = parent.getChildren();
  if (gap.index >= children.length) {
    parent.append(dragged);
  } else if (children[gap.index].getKey() !== dragged.getKey()) {
    children[gap.index].insertBefore(dragged);
  }
}

/** Client-space geometry of a drop gap: where the indicator line sits, spanning the gap
 *  parent's content box so a nested gap draws indented inside its container. `parentEl` is
 *  returned so the caller can read a per-target `--lexical-drop-color` off it. Must run in
 *  an editor read/update context. */
export interface GapGeometry {
  top: number;
  left: number;
  width: number;
  parentEl: HTMLElement;
}

export function gapGeometry(editor: LexicalEditor, gap: BlockDragGap): GapGeometry | null {
  const parentEl = editor.getElementByKey(gap.parent.getKey());
  if (parentEl === null) {
    return null;
  }
  const style = getComputedStyle(parentEl);
  const padLeft = parseFloat(style.paddingLeft) || 0;
  const padRight = parseFloat(style.paddingRight) || 0;
  const pRect = parentEl.getBoundingClientRect();
  const left = pRect.left + padLeft;
  const width = pRect.width - padLeft - padRight;

  const children = $isElementNode(gap.parent) ? gap.parent.getChildren() : [];
  let top: number;
  if (gap.index < children.length) {
    const childEl = editor.getElementByKey(children[gap.index].getKey());
    top = childEl === null ? pRect.bottom : childEl.getBoundingClientRect().top;
  } else if (children.length > 0) {
    const lastEl = editor.getElementByKey(children[children.length - 1].getKey());
    top = lastEl === null ? pRect.bottom : lastEl.getBoundingClientRect().bottom;
  } else {
    top = pRect.top + (parseFloat(style.paddingTop) || 0);
  }
  return { top, left, width, parentEl };
}

/**
 * Pick the gap whose boundary is vertically nearest `y`. Resolution is purely vertical, on
 * purpose: the grip sits in a side rail, so the pointer's X carries no information about
 * which (possibly indented) container is meant, and a drag travelling in an `*-outside`
 * rail has no meaningful X over the card at all (requirement: resolve from vertical offset
 * whether or not the cursor is over the editor). Each gap has a distinct Y, so a nested
 * `<li>` boundary naturally beats its enclosing `<ul>` boundary when the pointer is by the
 * item; ties (exact Y matches) fall to the policy's target order.
 */
function nearestGapIndex(geos: (GapGeometry | null)[], y: number): number {
  let best = -1;
  let bestDist = Infinity;
  geos.forEach((geo, i) => {
    if (geo === null) {
      return;
    }
    const dist = Math.abs(y - geo.top);
    if (dist < bestDist) {
      bestDist = dist;
      best = i;
    }
  });
  return best;
}

/** The gap `dragged` would land in at pointer height `y`, with its geometry for the
 *  indicator. `policy` may be the empty object (no hooks) — then all defaults apply, i.e.
 *  today's top-level reorder. Must run in an editor read/update context. */
export function resolveDropGap(
  editor: LexicalEditor,
  dragged: LexicalNode,
  y: number,
  policy: BlockDragPolicy,
): { gap: BlockDragGap; geometry: GapGeometry } | null {
  const gaps = policy.targets ? policy.targets(dragged) : defaultTargets();
  if (gaps.length === 0) {
    return null;
  }
  const geos = gaps.map((gap) => gapGeometry(editor, gap));
  const index = nearestGapIndex(geos, y);
  const geometry = index < 0 ? null : geos[index];
  return geometry === null ? null : { gap: gaps[index], geometry };
}

/** Apply the chosen gap through the policy's `drop` (or the default node move). Must run
 *  inside `editor.update()`. */
export function applyDrop(dragged: LexicalNode, gap: BlockDragGap, policy: BlockDragPolicy): void {
  (policy.drop ?? defaultDrop)(dragged, gap);
}
