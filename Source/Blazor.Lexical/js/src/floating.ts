// ---------------------------------------------------------------------------
// Viewport-anchored floating placement for top-layer overlays.
//
// An overlay positioned absolutely inside the editor root is clipped by any ancestor
// with `overflow` set (a scrolling pane, a card with `overflow: hidden`). An overlay
// shown as a `popover="manual"` renders in the top layer instead, so nothing clips it;
// this module places such an element in viewport coordinates (`position: fixed`).
// The mention picker uses it; the slash menu and link editor (overlays.ts) still use
// root-relative placement.
// ---------------------------------------------------------------------------

/** Gap between the anchor and the overlay. */
const GAP_PX = 4;
/** Minimum space between the overlay and the viewport edge. */
const VIEWPORT_MARGIN_PX = 8;

/**
 * The DOM caret's rect when the caret is inside `within`, or null when it is elsewhere,
 * absent, or has no layout (a collapsed range in an empty line reports an all-zero rect).
 */
export function caretRect(within: HTMLElement): DOMRect | null {
  const selection = window.getSelection();
  if (!selection || selection.rangeCount === 0 || !within.contains(selection.anchorNode)) {
    return null;
  }
  const rect = selection.getRangeAt(0).getBoundingClientRect();
  return rect.top === 0 && rect.left === 0 && rect.width === 0 && rect.height === 0
    ? null
    : rect;
}

/**
 * Whether the caret at `rect` is actually painted inside `within`: a hit test, so a caret
 * outside the viewport, clipped by a scrolling or `overflow: hidden` ancestor, or covered
 * by other content counts as hidden.
 */
export function isCaretVisible(rect: DOMRect, within: HTMLElement): boolean {
  const hit = document.elementFromPoint(rect.left, rect.top + rect.height / 2);
  return hit !== null && within.contains(hit);
}

/**
 * Places a `position: fixed` element below `anchor`, flipping above it when the space
 * below is too small for the element and there is more space above. The element's
 * height is capped to the space in the chosen direction (on top of its own CSS
 * `max-height`), and its left edge is clamped into the viewport.
 */
export function placeFixedNear(el: HTMLElement, anchor: DOMRect): void {
  const viewportWidth = document.documentElement.clientWidth;
  const viewportHeight = document.documentElement.clientHeight;

  // Measure at the CSS max-height, not a cap left from an earlier placement.
  el.style.maxHeight = '';
  const height = el.offsetHeight;
  const below = viewportHeight - anchor.bottom - GAP_PX - VIEWPORT_MARGIN_PX;
  const above = anchor.top - GAP_PX - VIEWPORT_MARGIN_PX;
  const flip = height > below && above > below;
  const available = Math.max(flip ? above : below, 0);
  if (height > available) {
    el.style.maxHeight = `${available}px`;
  }
  const placedHeight = Math.min(height, available);

  const top = flip ? anchor.top - GAP_PX - placedHeight : anchor.bottom + GAP_PX;
  const maxLeft = viewportWidth - el.offsetWidth - VIEWPORT_MARGIN_PX;
  const left = Math.max(Math.min(anchor.left, maxLeft), VIEWPORT_MARGIN_PX);
  el.style.top = `${top}px`;
  el.style.left = `${left}px`;
}
