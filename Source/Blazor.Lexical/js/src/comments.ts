// ---------------------------------------------------------------------------
// Comment composer: a floating, in-editor comment input — the Lexical playground's
// CommentPlugin/CommentInputBox as a reusable primitive.
//
// The whole reason this lives in the library rather than app-side is the three
// things that are genuinely hard to reproduce over the interop boundary:
//
//   1. Selection-rect positioning — float the box at the selection, the way the
//      link editor and floating toolbar do.
//   2. The blur problem — a textarea taking focus collapses the editor's own
//      selection, so by the time the user confirms there is nothing to wrap. We
//      solve it by CAPTURING the selection (both the Lexical RangeSelection and a
//      DOM Range) the instant the box opens, and wrapping the captured one.
//   3. The "keep the target highlighted while composing" decoration — painted with
//      the CSS Custom Highlight API (like highlights.ts) so it survives the DOM
//      selection moving into the textarea, needs no node, and needs no theme key.
//
// It is built ON marks, not beside them: confirming wraps the captured range in a
// `@lexical/mark` MarkNode carrying an APP-OWNED id, exactly as <LexicalMarks> does
// (and reusing the MarkNode + overlap resolver that extension registers). So the
// composer REQUIRES a sibling <LexicalMarks>; the C# half enforces that.
//
// Interop is opt-in as ever. The .NET→JS `open` call always works (it is a call this
// side initiates). The JS→.NET pushes — `submit`, `cancel`, and the `compose` request
// the add-comment button fires when the app supplied a mark-id factory — go through
// notifyDotNet, so a composer whose consumer wired none of them stays silent. When no
// factory is supplied the id is minted here as a UUIDv7, and OnSubmit carries it so the
// app still learns the id it must store its comment under.
// ---------------------------------------------------------------------------

import {
  $getSelection,
  $isRangeSelection,
  $setSelection,
  type LexicalEditor,
  type RangeSelection,
} from 'lexical';
import { $wrapSelectionInMarkNode } from '@lexical/mark';
import { createDOMRange } from '@lexical/selection';
import type { LexicalExtensionModule, LexicalExtensionSetup } from './extension';

/** The compose-highlight's registered name — its `::highlight()` selector in the CSS. */
const COMPOSE_HIGHLIGHT = 'blazor-lexical-comment-compose';

/** Gap in px between the selection rect and the floating box. */
const COMPOSE_GAP = 8;

/**
 * The CSS Custom Highlight API, or null where the browser lacks it. Accessed through this
 * wrapper (as highlights.ts does) because the DOM lib types `CSS.highlights` as an opaque
 * `HighlightRegistry` without the `set`/`delete` this uses.
 */
interface HighlightApi {
  set: (name: string, ranges: Range[]) => void;
  delete: (name: string) => void;
}

function getHighlightApi(): HighlightApi | null {
  const global = globalThis as unknown as {
    Highlight?: new (...ranges: Range[]) => object;
    CSS?: { highlights?: { set(name: string, h: object): void; delete(name: string): void } };
  };
  const ctor = global.Highlight;
  const registry = global.CSS?.highlights;
  if (typeof ctor !== 'function' || registry === undefined) {
    return null;
  }
  return {
    set: (name, ranges) => registry.set(name, new ctor(...ranges)),
    delete: (name) => registry.delete(name),
  };
}

/** The comment composer's options payload, mirrored from C# `CommentComposerExtensionOptionsDto`. */
export interface CommentComposerOptionsDto {
  /**
   * Whether the app supplied a `NewMarkId` factory. When true the add-comment button
   * asks .NET for an id (`compose`) before opening; when false the id is minted here.
   */
  hasMarkIdFactory?: boolean;
  /** Whether a mousedown outside the open box cancels the compose. */
  closeOnClickAway?: boolean;
}

/**
 * A time-ordered UUIDv7, minted when the app supplied no id factory. RFC 9562 layout:
 * a 48-bit big-endian Unix-ms timestamp, version 7, variant 10, the rest random. Used
 * only as a fallback — an app that cares about the id's shape supplies its own factory.
 */
function uuidv7(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  const ts = Date.now();
  bytes[0] = Math.floor(ts / 2 ** 40) & 0xff;
  bytes[1] = Math.floor(ts / 2 ** 32) & 0xff;
  bytes[2] = Math.floor(ts / 2 ** 24) & 0xff;
  bytes[3] = Math.floor(ts / 2 ** 16) & 0xff;
  bytes[4] = Math.floor(ts / 2 ** 8) & 0xff;
  bytes[5] = ts & 0xff;
  bytes[6] = (bytes[6] & 0x0f) | 0x70; // version 7
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant 10
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** Whether a non-collapsed selection currently lives inside `contentEl`. */
function hasRangeIn(contentEl: HTMLElement): boolean {
  const sel = window.getSelection();
  return (
    sel !== null &&
    sel.rangeCount > 0 &&
    !sel.isCollapsed &&
    sel.anchorNode !== null &&
    contentEl.contains(sel.anchorNode)
  );
}

/**
 * The comment composer as a `LexicalExtensionFactory`. It contributes no node of its own
 * — it wraps into `@lexical/mark`'s MarkNode, which the required <LexicalMarks> registers
 * — so this is register + invoke only.
 */
export default function commentComposerExtension(
  setup: LexicalExtensionSetup,
): LexicalExtensionModule {
  const options = (setup.options ?? {}) as CommentComposerOptionsDto;
  const closeOnClickAway = options.closeOnClickAway !== false;

  let editorRef: LexicalEditor | null = null;
  // Open the floating box, called by the C# half's OpenAsync. Set in register(); until
  // then (and after teardown) there is nothing to open, so it is a no-op returning false.
  let openImpl: (markId: string) => boolean = () => false;

  return {
    name: 'blazor-lexical/comment-composer',
    register: ({ editor, root, content }) => {
      editorRef = editor;

      const boxEl = root.querySelector<HTMLElement>('[data-lexical-comment-composer]');
      const inputEl = boxEl?.querySelector<HTMLTextAreaElement>('[data-lexical-comment-input]') ?? null;
      // No box markup ⇒ nothing to drive. Still return a teardown so create()'s list is uniform.
      if (boxEl === null || inputEl === null) {
        return () => {
          editorRef = null;
        };
      }

      // The captured range, held only while the box is open. `selection` is what we wrap;
      // `domRange` is what we highlight and position against. Both are snapshotted at open
      // so the textarea taking focus (which collapses the live selection) can't lose them.
      let selection: RangeSelection | null = null;
      let domRange: Range | null = null;
      // The id the confirmed mark will carry: the app's (OpenAsync / factory) or a UUIDv7.
      let markId = '';
      let open = false;

      const paintHighlight = (range: Range): void => {
        getHighlightApi()?.set(COMPOSE_HIGHLIGHT, [range]);
      };
      const clearHighlight = (): void => {
        getHighlightApi()?.delete(COMPOSE_HIGHLIGHT);
      };

      // Root-relative placement just below the selection, clamped to the content column.
      const position = (): void => {
        if (domRange === null) {
          return;
        }
        const rootRect = root.getBoundingClientRect();
        const bounds = content.getBoundingClientRect();
        const rect = domRange.getBoundingClientRect();
        const width = boxEl.getBoundingClientRect().width;
        let left = rect.left;
        if (left + width > bounds.right) {
          left = bounds.right - width;
        }
        if (left < bounds.left) {
          left = bounds.left;
        }
        boxEl.style.top = `${rect.bottom - rootRect.top + COMPOSE_GAP}px`;
        boxEl.style.left = `${left - rootRect.left}px`;
      };

      const reset = (): void => {
        open = false;
        selection = null;
        domRange = null;
        markId = '';
        inputEl.value = '';
        boxEl.removeAttribute('data-lexical-visible');
        clearHighlight();
      };

      // Snapshot the current selection and float the box over it. Returns false — and stays
      // closed — when there is no non-collapsed range to comment on. The DOM range is built
      // from the Lexical selection (createDOMRange), NOT window.getSelection(), so OpenAsync
      // works even when called from a control outside the editor that has taken DOM focus:
      // Lexical retains its selection across that, the browser's does not.
      const openBox = (id: string): boolean => {
        const captured = editor.getEditorState().read(() => {
          const sel = $getSelection();
          if (!$isRangeSelection(sel) || sel.isCollapsed()) {
            return null;
          }
          const range = createDOMRange(
            editor,
            sel.anchor.getNode(),
            sel.anchor.offset,
            sel.focus.getNode(),
            sel.focus.offset,
          );
          return range === null ? null : { selection: sel.clone(), range };
        });
        if (captured === null) {
          return false;
        }
        selection = captured.selection;
        domRange = captured.range;
        markId = id;
        open = true;
        paintHighlight(domRange);
        boxEl.setAttribute('data-lexical-visible', '');
        position();
        // Focus after the current click/dispatch settles, so the delegated editor.focus()
        // in a button's click handler can't steal it straight back (as the link editor does).
        requestAnimationFrame(() => {
          inputEl.focus();
        });
        return true;
      };
      openImpl = openBox;

      const confirm = (): void => {
        if (!open || selection === null) {
          return;
        }
        const text = inputEl.value.trim();
        const captured = selection;
        const id = markId;
        editor.update(
          () => {
            $setSelection(captured.clone());
            const sel = $getSelection();
            if ($isRangeSelection(sel) && !sel.isCollapsed()) {
              $wrapSelectionInMarkNode(sel, sel.isBackward(), id);
            }
          },
          { discrete: true },
        );
        reset();
        editor.focus();
        setup.notifyDotNet('submit', id, text);
      };

      const cancel = (): void => {
        if (!open) {
          return;
        }
        reset();
        editor.focus();
        setup.notifyDotNet('cancel');
      };

      // Keep the editor selection intact when the box's own buttons are clicked; the
      // textarea still needs focus, so let its mousedown through (as the link editor does).
      const onBoxMouseDown = (e: MouseEvent): void => {
        if ((e.target as HTMLElement).closest('textarea, input') === null) {
          e.preventDefault();
        }
      };
      boxEl.addEventListener('mousedown', onBoxMouseDown);

      const onBoxClick = (e: Event): void => {
        const target = e.target as HTMLElement;
        if (target.closest('[data-lexical-comment-confirm]') !== null) {
          confirm();
        } else if (target.closest('[data-lexical-comment-cancel]') !== null) {
          cancel();
        }
      };
      boxEl.addEventListener('click', onBoxClick);

      // Ctrl/Cmd+Enter confirms; Escape cancels — matching the playground.
      const onInputKeyDown = (e: KeyboardEvent): void => {
        if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
          e.preventDefault();
          confirm();
        } else if (e.key === 'Escape') {
          e.preventDefault();
          cancel();
        }
      };
      inputEl.addEventListener('keydown', onInputKeyDown);

      // The add-comment button ([data-lexical-comment-compose], e.g. <LexicalAddCommentButton>).
      // mousedown preventDefault keeps the selection alive across the click (it may sit in the
      // floating toolbar, which already does this — but the button can live anywhere, so own it
      // here too). The click opens: with a factory, ask .NET for the id first; without one, mint.
      const onRootMouseDown = (e: MouseEvent): void => {
        if ((e.target as HTMLElement).closest('[data-lexical-comment-compose]') !== null) {
          e.preventDefault();
        }
      };
      const onRootClick = (e: Event): void => {
        const btn = (e.target as HTMLElement).closest('[data-lexical-comment-compose]');
        if (btn === null || btn.hasAttribute('data-lexical-disabled') || !hasRangeIn(content)) {
          return;
        }
        if (options.hasMarkIdFactory) {
          // .NET mints the id and calls back into OpenAsync — see the C# OnInvokeAsync.
          setup.notifyDotNet('compose');
        } else {
          openBox(uuidv7());
        }
      };
      root.addEventListener('mousedown', onRootMouseDown);
      root.addEventListener('click', onRootClick);

      // Cancel when the pointer goes down outside the open box (opt-out via CloseOnClickAway).
      const onDocMouseDown = (e: MouseEvent): void => {
        if (open && closeOnClickAway && !boxEl.contains(e.target as Node)) {
          cancel();
        }
      };
      document.addEventListener('mousedown', onDocMouseDown);

      // Reflect selection state onto any add-comment buttons (disabled while collapsed), and
      // keep the open box glued to its selection through scroll/reflow.
      const refresh = (): void => {
        const enabled = hasRangeIn(content);
        root
          .querySelectorAll<HTMLElement>('[data-lexical-comment-compose]')
          .forEach((el) => {
            el.toggleAttribute('data-lexical-disabled', !enabled);
            el.setAttribute('aria-disabled', String(!enabled));
          });
        if (open) {
          position();
        }
      };
      const onSelectionChange = (): void => refresh();
      const onScrollOrResize = (): void => {
        if (open) {
          position();
        }
      };
      document.addEventListener('selectionchange', onSelectionChange);
      window.addEventListener('scroll', onScrollOrResize, true);
      window.addEventListener('resize', onScrollOrResize);
      const unregisterUpdate = editor.registerUpdateListener(() => refresh());
      refresh();

      return () => {
        boxEl.removeEventListener('mousedown', onBoxMouseDown);
        boxEl.removeEventListener('click', onBoxClick);
        inputEl.removeEventListener('keydown', onInputKeyDown);
        root.removeEventListener('mousedown', onRootMouseDown);
        root.removeEventListener('click', onRootClick);
        document.removeEventListener('mousedown', onDocMouseDown);
        document.removeEventListener('selectionchange', onSelectionChange);
        window.removeEventListener('scroll', onScrollOrResize, true);
        window.removeEventListener('resize', onScrollOrResize);
        unregisterUpdate();
        reset();
        openImpl = () => false;
        editorRef = null;
      };
    },
    invoke: (method, args) => {
      if (editorRef === null) {
        return undefined;
      }
      switch (method) {
        case 'open':
          return openImpl(String(args[0] ?? ''));
        default:
          return undefined;
      }
    },
  };
}
