// ---------------------------------------------------------------------------
// Bridging the browser's own selection into a Lexical RangeSelection.
//
// Lexical only syncs `$getSelection()` from the DOM while the editor is EDITABLE.
// A read-only editor — the review experience, where a reviewer selects text to
// comment on or annotate but never types — leaves `$getSelection()` null, so any
// feature that reads the Lexical selection (mark-wrapping, the comment composer)
// silently finds nothing to act on. These helpers reconstruct a RangeSelection
// from `window.getSelection()` instead, using Lexical's own DOM→node point
// resolution (`$createRangeSelectionFromDom`), so those features work read-only.
//
// Both must run where the ACTIVE EDITOR is set (inside `editor.read()` or
// `editor.update()`), because resolving DOM nodes to node keys needs it — the same
// reason marks.ts uses `editor.read` for its click hit-test.
// ---------------------------------------------------------------------------

import {
  $createRangeSelectionFromDom,
  $isRangeSelection,
  type LexicalEditor,
  type RangeSelection,
} from 'lexical';

/**
 * A RangeSelection reconstructed from the live browser selection, or null when there
 * is none, it is collapsed, or it does not resolve to editor text. For the read-only
 * path where `$getSelection()` is null; callers gate on `!editor.isEditable()` so an
 * editable editor keeps using Lexical's own synced selection.
 */
export function $rangeSelectionFromDom(editor: LexicalEditor): RangeSelection | null {
  const selection = $createRangeSelectionFromDom(window.getSelection(), editor);
  return $isRangeSelection(selection) && !selection.isCollapsed() ? selection : null;
}

/**
 * A RangeSelection resolved from an arbitrary DOM {@link Range} — for hydrating a mark
 * from a text-quote match, where the caller has a resolved DOM Range but no user
 * selection. Lexical only resolves points from a DOM `Selection`, so the range is
 * briefly installed as the window selection to feed
 * {@link $createRangeSelectionFromDom}, then the user's own selection is restored so
 * nothing visible moves. Returns null when the range does not resolve to a non-collapsed
 * text span.
 */
export function $selectionFromDomRange(
  editor: LexicalEditor,
  range: Range,
): RangeSelection | null {
  const domSelection = window.getSelection();
  if (domSelection === null) {
    return null;
  }
  // Snapshot the user's selection: we only borrow the DOM selection to let Lexical
  // resolve DOM points to node keys, and must not leave the caret moved.
  const saved: Range[] = [];
  for (let i = 0; i < domSelection.rangeCount; i++) {
    saved.push(domSelection.getRangeAt(i));
  }
  domSelection.removeAllRanges();
  domSelection.addRange(range);
  const selection = $createRangeSelectionFromDom(domSelection, editor);
  domSelection.removeAllRanges();
  for (const r of saved) {
    domSelection.addRange(r);
  }
  return $isRangeSelection(selection) && !selection.isCollapsed() ? selection : null;
}
