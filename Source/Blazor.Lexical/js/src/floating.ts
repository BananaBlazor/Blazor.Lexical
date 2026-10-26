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

/** Gap between the anchor and the overlay, and between the overlay and the viewport edge. */
const GAP_PX = 4;
const VIEWPORT_MARGIN_PX = 8;

/**
 * The collapsed DOM caret's rect, or null when there is no caret or it has no layout
 * (a collapsed range in an empty line reports an all-zero rect).
 */
export function caretRect(): DOMRect | null {
  const selection = window.getSelection();
  if (!selection || selection.rangeCount === 0) {
    return null;
  }
  const rect = selection.getRangeAt(0).getBoundingClientRect();
  return rect.top === 0 && rect.left === 0 && rect.width === 0 && rect.height === 0
    ? null
    : rect;
}

/** Whether `rect` lies (at least partly) inside the viewport and inside `within`'s box. */
export function isRectVisible(rect: DOMRect, within: HTMLElement): boolean {
  const bounds = within.getBoundingClientRect();
  const viewportHeight = document.documentElement.clientHeight;
  return (
    rect.bottom >= Math.max(bounds.top, 0) &&
    rect.top <= Math.min(bounds.bottom, viewportHeight)
  );
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
