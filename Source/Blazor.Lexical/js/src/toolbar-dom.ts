// ---------------------------------------------------------------------------
// Toolbar control state shared by core and the built-ins that paint their own
// controls' disabled state (indent).
// ---------------------------------------------------------------------------

/**
 * Sets or clears `data-lexical-disabled`, mirrored to `aria-disabled`. A `reason`
 * becomes the attribute's value (CSS `[data-lexical-disabled]` and `hasAttribute`
 * match either way).
 */
export function setElDisabled(el: Element, disabled: boolean, reason = ''): void {
  if (disabled) {
    el.setAttribute('data-lexical-disabled', reason);
    el.setAttribute('aria-disabled', 'true');
  } else {
    el.removeAttribute('data-lexical-disabled');
    el.removeAttribute('aria-disabled');
  }
}
