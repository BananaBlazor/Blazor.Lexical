// ---------------------------------------------------------------------------
// Ghost completion: muted "rest of the word" text painted AFTER the caret
// (`chi│cken stock`), the way an inline autocomplete hint reads.
//
// The whole design is the placement. The ghost is a single <span> parented to
// `root` — the editor's outer wrapper — and NOT to the `[data-lexical-content]`
// contenteditable Lexical is bound to. Living outside the editable surface is
// what structurally guarantees the invariant that makes this safe to offer at
// all: the ghost is *visible but not real*. It is not a Lexical node, so it never
// enters the document, the state JSON, the history/undo stack, or
// getTextContent(); it is not inside the contenteditable, so the browser never
// treats it as text and Lexical's reconciler never sees it.
//
// Positioning reuses the overlay quartet the floating toolbar runs on
// (selectionchange + capture-scroll + resize + registerUpdateListener), but
// anchors to the caret's RIGHT edge rather than below a selection: a ghost sits
// where the next character would go. The caret rect comes from
// getSelection().getRangeAt(0).getBoundingClientRect() — a collapsed caret yields
// a zero-width rect at the caret, the same call the mention picker anchors to.
//
// Bundling: no @lexical/* imports, only `lexical` types, so this is a few hundred
// bytes on the core bundle — index.ts imports it statically, like mentions.
// ---------------------------------------------------------------------------

import type { LexicalEditor } from 'lexical';
import type { GhostCompletionApi, GhostSession } from './extension';

/**
 * Builds the shared {@link GhostCompletionApi}. Stateless: every field lives in the
 * closure that {@link GhostCompletionApi.attach} opens, so one instance is safely shared
 * across all extensions (see index.ts `primitives`).
 */
export function createGhostApi(): GhostCompletionApi {
  return {
    attach(editor: LexicalEditor, root: HTMLElement, read: () => GhostSession | null): () => void {
      // The overlay lives under root, outside the contenteditable — the placement that
      // keeps the ghost out of the document/state/history entirely. Starts hidden.
      const ghost = document.createElement('span');
      ghost.className = 'blazor-lexical__ghost';
      ghost.setAttribute('data-lexical-ghost', '');
      root.appendChild(ghost);

      // The content surface, so we can confirm the selection is really in *this* editor
      // before painting (the document selectionchange event is global).
      const contentEl = root.querySelector<HTMLElement>('[data-lexical-content]');

      const hide = (): void => {
        ghost.removeAttribute('data-lexical-visible');
      };

      const update = (): void => {
        const session = editor.getEditorState().read(read);
        if (session === null || session.text === '') {
          hide();
          return;
        }
        const sel = window.getSelection();
        // Only paint for a collapsed caret that lives in this editor's content.
        if (
          !sel ||
          sel.rangeCount === 0 ||
          !sel.isCollapsed ||
          !sel.anchorNode ||
          contentEl === null ||
          !contentEl.contains(sel.anchorNode)
        ) {
          hide();
          return;
        }
        const caretRect = sel.getRangeAt(0).getBoundingClientRect();
        const rootRect = root.getBoundingClientRect();
        ghost.textContent = session.text;
        // The ghost lives under root, not the content element, so it inherits neither the
        // content's font metrics nor its line-height. Copy the font from the content and
        // pin the ghost's line box to the caret rect's height, so its baseline coincides
        // with the real text rather than dropping below it.
        if (contentEl !== null) {
          const cs = getComputedStyle(contentEl);
          ghost.style.fontFamily = cs.fontFamily;
          ghost.style.fontSize = cs.fontSize;
          ghost.style.fontWeight = cs.fontWeight;
          ghost.style.fontStyle = cs.fontStyle;
          ghost.style.letterSpacing = cs.letterSpacing;
        }
        ghost.style.lineHeight = `${caretRect.height}px`;
        // The caret's right edge — a ghost sits where the next character would go, not
        // below the selection like the floating toolbar.
        ghost.style.left = `${caretRect.right - rootRect.left}px`;
        ghost.style.top = `${caretRect.top - rootRect.top}px`;
        ghost.setAttribute('data-lexical-visible', '');
      };

      // The floating-toolbar reposition quartet: programmatic changes, mouse-drag caret
      // moves, and scroll/resize all have to re-place (or hide) the ghost.
      const onChange = (): void => update();
      const unregister = editor.registerUpdateListener(onChange);
      document.addEventListener('selectionchange', onChange);
      window.addEventListener('scroll', onChange, true);
      window.addEventListener('resize', onChange);

      update();

      return () => {
        unregister();
        document.removeEventListener('selectionchange', onChange);
        window.removeEventListener('scroll', onChange, true);
        window.removeEventListener('resize', onChange);
        ghost.remove();
      };
    },
  };
}
