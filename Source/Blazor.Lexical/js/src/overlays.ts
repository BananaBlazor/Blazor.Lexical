// ---------------------------------------------------------------------------
// In-editor floating overlays: the floating format toolbar, the "/" slash menu,
// and the per-block hover rails (block gutters) that carry the drag grip, the "+"
// add-block button, and whatever else the host puts in them.
//
// These follow the same split of responsibilities as the fixed toolbar
// (see index.ts): Blazor renders the *markup* (buttons tagged
// `data-lexical-command`, menu items, the handle) as ChildContent inside the
// editor root; this module owns only the *behaviour* Blazor can't do from C#
// without chatty interop — positioning, show/hide, typeahead filtering, and
// drag mechanics. Because the markup lives inside the root, the editor's
// existing delegated click listener (`runCommandToken`) dispatches the commands
// and `updateToolbarDom` reflects active state onto the buttons — so an overlay
// button "just works" with zero extra wiring and, crucially, zero JS→.NET calls.
//
// Each `register*` function activates only when its marker element is present
// under the root (the host opted in by placing the component), and returns a
// teardown that `create()` pushes onto its cleanup list.
// ---------------------------------------------------------------------------

import {
  $getSelection,
  $isRangeSelection,
  $isTextNode,
  $getNodeByKey,
  $getNearestNodeFromDOMNode,
  $createParagraphNode,
  createCommand,
  COMMAND_PRIORITY_LOW,
  type LexicalCommand,
  type LexicalEditor,
} from 'lexical';
import { $isLinkNode } from '@lexical/link';
import { $findMatchingParent } from '@lexical/utils';

// Matches the Lexical playground's `setFloatingElemPosition` gaps.
const FLOATING_VERTICAL_GAP = 10;
const FLOATING_HORIZONTAL_OFFSET = 5;

/**
 * Positions the floating toolbar over a selection, cloning the playground's
 * behavior: the toolbar's left aligns to the selection's left edge (offset a few
 * px), is clamped so it never sits farther left than the first text column — the
 * left gutter is reserved for the `+`/drag handle — nor past the content's right
 * edge, and flips below the selection when there's no room above. Positioned
 * absolutely relative to `root`; `contentEl` supplies the text bounds.
 */
function positionFloatingToolbar(
  el: HTMLElement,
  targetRect: DOMRect,
  root: HTMLElement,
  contentEl: HTMLElement,
): void {
  const rootRect = root.getBoundingClientRect();
  const bounds = contentEl.getBoundingClientRect();
  const elRect = el.getBoundingClientRect();
  // The first text column is inside the content's left padding (the gutter).
  const paddingLeft = parseFloat(getComputedStyle(contentEl).paddingLeft) || 0;
  const textLeft = bounds.left + paddingLeft;

  let top = targetRect.top - elRect.height - FLOATING_VERTICAL_GAP;
  if (top < bounds.top) {
    top = targetRect.bottom + FLOATING_VERTICAL_GAP;
  }

  let left = targetRect.left - FLOATING_HORIZONTAL_OFFSET;
  if (left + elRect.width > bounds.right) {
    left = bounds.right - elRect.width;
  }
  if (left < textLeft) {
    left = textLeft;
  }

  el.style.top = `${top - rootRect.top}px`;
  el.style.left = `${left - rootRect.left}px`;
}

/** Positions `el` absolutely (relative to `root`) just below `rect`'s bottom-left. */
function positionBelow(el: HTMLElement, rect: DOMRect, root: HTMLElement): void {
  const rootRect = root.getBoundingClientRect();
  el.style.top = `${rect.bottom - rootRect.top + 4}px`;
  el.style.left = `${Math.max(rect.left - rootRect.left, 0)}px`;
}

// ---------------------------------------------------------------------------
// Floating format toolbar
// ---------------------------------------------------------------------------

/**
 * Reveals `toolbarEl` above the current text selection whenever a non-empty
 * range is selected inside this editor, and hides it otherwise. The toolbar's
 * buttons are ordinary `data-lexical-command` buttons, so the delegated root
 * listener handles their clicks and active state — this only positions the box.
 */
export function registerFloatingToolbar(
  editor: LexicalEditor,
  root: HTMLElement,
  contentEl: HTMLElement,
  toolbarEl: HTMLElement,
): () => void {
  const update = () => {
    const sel = window.getSelection();
    // Show only for a real, non-collapsed selection that lives in *this*
    // editor's content (the document `selectionchange` event is global, so we
    // must guard against selections in other editors on the page).
    if (
      !sel ||
      sel.rangeCount === 0 ||
      sel.isCollapsed ||
      !sel.anchorNode ||
      !contentEl.contains(sel.anchorNode)
    ) {
      toolbarEl.removeAttribute('data-lexical-visible');
      return;
    }
    const rect = sel.getRangeAt(0).getBoundingClientRect();
    if (rect.width === 0 && rect.height === 0) {
      toolbarEl.removeAttribute('data-lexical-visible');
      return;
    }
    toolbarEl.setAttribute('data-lexical-visible', '');
    positionFloatingToolbar(toolbarEl, rect, root, contentEl);
  };

  // `selectionchange` covers mouse-drag selection; the editor update listener
  // covers programmatic changes; scroll/resize keep the box glued to the text.
  const onChange = () => update();
  document.addEventListener('selectionchange', onChange);
  window.addEventListener('scroll', onChange, true);
  window.addEventListener('resize', onChange);
  const unregister = editor.registerUpdateListener(() => update());

  return () => {
    document.removeEventListener('selectionchange', onChange);
    window.removeEventListener('scroll', onChange, true);
    window.removeEventListener('resize', onChange);
    unregister();
    toolbarEl.removeAttribute('data-lexical-visible');
  };
}

// ---------------------------------------------------------------------------
// Slash command menu
// ---------------------------------------------------------------------------

/** Describes a `/query` typeahead trigger found at the collapsed caret. */
interface SlashMatch {
  /** Key of the text node holding the trigger. */
  nodeKey: string;
  /** Offset of the leading `/` within that node. */
  slashOffset: number;
  /** Offset of the caret within that node (end of the query). */
  caretOffset: number;
  /** The text typed after the `/`, used to filter the menu. */
  query: string;
}

// A "/" is a trigger only at the start of a block or after whitespace, so URLs
// and inline slashes don't pop the menu. The query is word characters/hyphen —
// enough for block names, and any other char (space included) closes the menu.
const SLASH_TRIGGER = /(?:^|\s)\/([\w-]*)$/;

/**
 * Reads the current selection for a slash trigger. Must run inside an editor
 * read/update context. Returns `null` when the caret isn't sitting just after a
 * `/query` run in a text node.
 */
function readSlashMatch(): SlashMatch | null {
  const selection = $getSelection();
  if (!$isRangeSelection(selection) || !selection.isCollapsed()) {
    return null;
  }
  const node = selection.anchor.getNode();
  if (!$isTextNode(node)) {
    return null;
  }
  const caretOffset = selection.anchor.offset;
  const before = node.getTextContent().slice(0, caretOffset);
  const match = before.match(SLASH_TRIGGER);
  if (match === null) {
    return null;
  }
  const query = match[1];
  return {
    nodeKey: node.getKey(),
    slashOffset: caretOffset - query.length - 1,
    caretOffset,
    query,
  };
}

/**
 * Turns a `[data-lexical-slash-menu]` element into a typeahead: typing "/" opens
 * it, further characters filter the `[data-lexical-slash-item]` buttons, and
 * ↑/↓/Enter/Tab/Escape drive it. Selecting an item deletes the `/query` text and
 * then lets the item's `data-lexical-command` run through the normal delegated
 * dispatch, so a slash item and a toolbar button share one code path.
 */
export function registerSlashMenu(
  editor: LexicalEditor,
  root: HTMLElement,
  contentEl: HTMLElement,
  menuEl: HTMLElement,
): () => void {
  const items = Array.from(menuEl.querySelectorAll<HTMLElement>('[data-lexical-slash-item]'));
  let open = false;
  let visible: HTMLElement[] = [];
  let activeIndex = 0;

  const setActive = (index: number) => {
    activeIndex = index;
    visible.forEach((el, i) => el.toggleAttribute('data-lexical-slash-active', i === index));
  };

  const filter = (query: string) => {
    const q = query.toLowerCase();
    visible = [];
    for (const item of items) {
      const haystack =
        `${item.getAttribute('data-lexical-slash-keywords') ?? ''} ${item.textContent ?? ''}`.toLowerCase();
      const matches = q === '' || haystack.includes(q);
      item.toggleAttribute('hidden', !matches);
      if (matches) {
        visible.push(item);
      }
    }
    setActive(visible.length > 0 ? 0 : -1);
  };

  const close = () => {
    open = false;
    menuEl.removeAttribute('data-lexical-visible');
  };

  /** Deletes the `/query` trigger text, leaving the caret where the `/` was. */
  const removeTrigger = () => {
    editor.update(() => {
      const match = readSlashMatch();
      if (match === null) {
        return;
      }
      const node = $getNodeByKey(match.nodeKey);
      if ($isTextNode(node)) {
        node.select(match.slashOffset, match.caretOffset).insertText('');
      }
    });
  };

  // React to every editor change: open/refresh while a trigger exists, close
  // once it's gone. Typing, deleting, and caret moves all fire this.
  const unregister = editor.registerUpdateListener(() => {
    const match = editor.getEditorState().read(readSlashMatch);
    const sel = window.getSelection();
    if (match === null || !sel || sel.rangeCount === 0) {
      if (open) {
        close();
      }
      return;
    }
    open = true;
    filter(match.query);
    if (visible.length === 0) {
      close();
      return;
    }
    menuEl.setAttribute('data-lexical-visible', '');
    positionBelow(menuEl, sel.getRangeAt(0).getBoundingClientRect(), root);
  });

  // Capture-phase click on the menu: strip the trigger text first, then let the
  // event bubble on to the root's delegated listener which runs the command.
  const onMenuClick = (e: Event) => {
    if ((e.target as HTMLElement).closest('[data-lexical-slash-item]') === null) {
      return;
    }
    removeTrigger();
    close();
  };
  menuEl.addEventListener('click', onMenuClick, true);

  // Capture-phase keydown so the menu wins arrow/enter over the editor while open.
  const onKeyDown = (e: KeyboardEvent) => {
    if (!open || visible.length === 0) {
      return;
    }
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        e.stopPropagation();
        setActive((activeIndex + 1) % visible.length);
        break;
      case 'ArrowUp':
        e.preventDefault();
        e.stopPropagation();
        setActive((activeIndex - 1 + visible.length) % visible.length);
        break;
      case 'Enter':
      case 'Tab': {
        e.preventDefault();
        e.stopPropagation();
        // A synthetic click reuses onMenuClick (trigger removal) + the root's
        // delegated dispatch (command), exactly like a real mouse click.
        visible[activeIndex]?.click();
        editor.focus();
        break;
      }
      case 'Escape':
        e.preventDefault();
        e.stopPropagation();
        close();
        break;
    }
  };
  contentEl.addEventListener('keydown', onKeyDown, true);

  return () => {
    unregister();
    menuEl.removeEventListener('click', onMenuClick, true);
    contentEl.removeEventListener('keydown', onKeyDown, true);
    close();
  };
}

// ---------------------------------------------------------------------------
// Block gutters — the per-block hover rails (drag grip, "+", host content)
// ---------------------------------------------------------------------------

/** Returns the top-level block element under `clientY`, or null. */
function blockElementAt(contentEl: HTMLElement, clientY: number): HTMLElement | null {
  for (const child of Array.from(contentEl.children) as HTMLElement[]) {
    const rect = child.getBoundingClientRect();
    if (clientY >= rect.top && clientY <= rect.bottom) {
      return child;
    }
  }
  return null;
}

/** The block under the pointer, as reported to a {@link trackHoveredBlock} subscriber. */
export interface HoveredBlock {
  /** The block's DOM element (a direct child of the content surface). */
  element: HTMLElement;
  /** The top-level node's key, or null when the DOM node resolved to nothing. */
  key: string | null;
  /** The block's zero-based position among the content surface's children. */
  index: number;
}

/**
 * Tracks which top-level block the pointer is over and reports every change to
 * `onHover` (null when the pointer leaves the content band or the root entirely).
 *
 * Split out from the rail engine so the hit-test and the `editor.read` key resolution
 * have exactly one owner: {@link registerBlockGutters} builds on it, and anything else
 * that needs "which block is the pointer over" should too rather than re-deriving it.
 */
export function trackHoveredBlock(
  editor: LexicalEditor,
  root: HTMLElement,
  contentEl: HTMLElement,
  onHover: (block: HoveredBlock | null) => void,
): () => void {
  // `editor.read` (not `getEditorState().read`) so the *active editor* is set —
  // $getNearestNodeFromDOMNode needs it to resolve a DOM node's key. Use the
  // non-throwing getTopLevelElement so hovering a non-block (→ root) is a no-op.
  const keyForBlockElement = (blockEl: HTMLElement): string | null =>
    editor.read(() => {
      const node = $getNearestNodeFromDOMNode(blockEl);
      const top = node === null ? null : node.getTopLevelElement();
      return top === null ? null : top.getKey();
    });

  const onMouseMove = (e: MouseEvent): void => {
    const contentRect = contentEl.getBoundingClientRect();
    if (e.clientY < contentRect.top || e.clientY > contentRect.bottom) {
      onHover(null);
      return;
    }
    const element = blockElementAt(contentEl, e.clientY);
    if (element === null) {
      onHover(null);
      return;
    }
    onHover({
      element,
      key: keyForBlockElement(element),
      index: Array.prototype.indexOf.call(contentEl.children, element),
    });
  };
  const onMouseLeave = (): void => onHover(null);

  root.addEventListener('mousemove', onMouseMove);
  root.addEventListener('mouseleave', onMouseLeave);
  return () => {
    root.removeEventListener('mousemove', onMouseMove);
    root.removeEventListener('mouseleave', onMouseLeave);
  };
}

/** The hovered block, as reported to the .NET half of the block gutter. */
export interface BlockRefDto {
  nodeKey: string;
  index: number;
  blockType: string;
  textPreview: string;
}

/** How much of the block's text rides along in the hover push. */
const BLOCK_PREVIEW_CHARS = 80;

/** Gap (px) between the anchor edge and a rail, and between stacked rails. */
const GUTTER_GAP = 4;

/**
 * How long a rail stays up after the pointer leaves the editor before it hides.
 *
 * This is what makes a rail *reachable*. Rails are absolutely positioned children of the
 * root, but the pixels between the text and a rail — the gutter gap, and the whole page
 * outside the card for an `Outside` rail — do not belong to the root, so travelling to a
 * rail fires the root's `mouseleave`. Hiding on that event immediately (a rail is then
 * `pointer-events: none`) makes the rail's own buttons impossible to click: it vanishes
 * mid-journey. The grace window lets the pointer arrive, at which point the rail's
 * `mouseenter` cancels the hide.
 */
const GUTTER_HIDE_GRACE_MS = 400;

/** Where a rail sits, mirrored from the C# `LexicalGutterPosition`. */
type GutterPosition = 'left-inside' | 'left-outside' | 'right-inside' | 'right-outside';

/**
 * Drives every `[data-lexical-block-gutter]` rail in the editor: the per-block hover
 * gutters that hold the drag grip, the "+" add-block button, and whatever else the
 * host put in them.
 *
 * ONE registration for ALL rails, rather than one per rail, because everything here is
 * genuinely shared: a single hover hit-test (running it per rail would repeat the same
 * `editor.read` N times per mousemove), a single drop-line indicator, and one delegated
 * drag/click listener pair on the root. Rails differ only in which side they sit on and
 * what the host put inside them.
 *
 * Rails are positioned outward from the content edge in declaration order, so several on
 * the same side stack instead of overlapping. Behaviour lives here rather than in the
 * items because the items are just markup: `[data-lexical-drag-grip]` reorders its block
 * (through Lexical node moves, so history and serialization stay correct),
 * `[data-lexical-add-block]` inserts a paragraph below and types "/" to pop the slash
 * menu, and anything else in a rail is the host's own Blazor `@onclick` markup.
 *
 * `onHover` is the single opt-in .NET push (`notify.blockHover`), deduped by node key so
 * it fires once per *block* rather than once per mousemove.
 */
export function registerBlockGutters(
  editor: LexicalEditor,
  root: HTMLElement,
  contentEl: HTMLElement,
  gutterEls: HTMLElement[],
  onHover: (block: BlockRefDto | null) => void,
): () => void {
  // The block currently under the pointer (what every rail acts on).
  let hoveredKey: string | null = null;
  // The block being dragged, captured on dragstart.
  let draggedKey: string | null = null;
  // True while the pointer is over any rail — see scheduleHide.
  let pointerInGutter = false;
  let lastPushedKey: string | null = null;
  let hideTimer: ReturnType<typeof setTimeout> | undefined;

  // A JS-owned horizontal indicator showing where a dropped block will land. One per
  // editor, not one per rail: it marks a position in the document, not in a gutter.
  const dropLine = document.createElement('div');
  dropLine.className = 'blazor-lexical__drop-line';
  dropLine.setAttribute('data-lexical-drop-line', '');
  root.appendChild(dropLine);

  const positionOf = (el: HTMLElement): GutterPosition => {
    const raw = el.getAttribute('data-lexical-block-gutter-position');
    return raw === 'left-inside' || raw === 'left-outside' || raw === 'right-outside'
      ? raw
      : 'right-inside';
  };

  const cancelHide = (): void => {
    if (hideTimer !== undefined) {
      clearTimeout(hideTimer);
      hideTimer = undefined;
    }
  };

  /**
   * Hides every rail after {@link GUTTER_HIDE_GRACE_MS}, unless something cancels it
   * first — the pointer arriving on a rail, or moving back onto a block.
   */
  const scheduleHide = (): void => {
    cancelHide();
    hideTimer = setTimeout(() => {
      hideTimer = undefined;
      if (pointerInGutter) {
        return;
      }
      for (const el of gutterEls) {
        el.removeAttribute('data-lexical-visible');
      }
    }, GUTTER_HIDE_GRACE_MS);
  };

  const hideDropLine = (): void => dropLine.removeAttribute('data-lexical-visible');

  const showDropLine = (blockEl: HTMLElement, after: boolean): void => {
    const rootRect = root.getBoundingClientRect();
    const contentRect = contentEl.getBoundingClientRect();
    const rect = blockEl.getBoundingClientRect();
    dropLine.style.left = `${contentRect.left - rootRect.left}px`;
    dropLine.style.width = `${contentRect.width}px`;
    dropLine.style.top = `${(after ? rect.bottom : rect.top) - rootRect.top}px`;
    dropLine.setAttribute('data-lexical-visible', '');
  };

  /**
   * Positions every rail beside `block`, stacking same-position rails outward.
   *
   * Each position names the edge a rail hangs off:
   *
   * - `*-inside` anchors to the **text column** — the content box inset by its own
   *   padding, i.e. the empty margin `.blazor-lexical__content` already reserves. An
   *   inside rail is clamped to the card, so one wider than that reserved band slides
   *   over the text rather than escaping the editor.
   * - `*-outside` anchors to the **card edge** and hangs off it, into the page.
   *
   * Either way, staying reachable is the grace window's job (see
   * {@link GUTTER_HIDE_GRACE_MS}), not geometry's — which is precisely what makes
   * `outside` safe to offer at all.
   */
  const layout = (block: HoveredBlock): void => {
    const rootRect = root.getBoundingClientRect();
    const contentRect = contentEl.getBoundingClientRect();
    const rect = block.element.getBoundingClientRect();
    const style = getComputedStyle(contentEl);
    // The four anchor edges, root-relative.
    const anchors: Record<GutterPosition, number> = {
      'left-inside': contentRect.left + parseFloat(style.paddingLeft) - rootRect.left,
      'left-outside': 0,
      'right-inside': contentRect.right - parseFloat(style.paddingRight) - rootRect.left,
      'right-outside': rootRect.width,
    };
    // Distance already consumed at each position by rails placed earlier.
    const consumed: Record<GutterPosition, number> = {
      'left-inside': 0,
      'left-outside': 0,
      'right-inside': 0,
      'right-outside': 0,
    };
    const blockType = block.element.tagName.toLowerCase();

    for (const el of gutterEls) {
      const position = positionOf(el);
      const isLeft = position === 'left-inside' || position === 'left-outside';
      // The fractional width, not offsetWidth: that rounds to an integer, and the clamp
      // below compares it against fractional rects — half a pixel of rounding is enough
      // to put a rail back over the card's edge. Both are measurable here because a
      // hidden rail uses visibility, not display, so it still has a layout box.
      const width = el.getBoundingClientRect().width;
      const offset = consumed[position] + GUTTER_GAP;
      const ideal = isLeft ? anchors[position] - offset - width : anchors[position] + offset;

      el.style.top = `${rect.top - rootRect.top}px`;
      el.style.left = `${
        position.endsWith('-inside')
          ? Math.max(0, Math.min(ideal, rootRect.width - width))
          : ideal
      }px`;
      consumed[position] += width + GUTTER_GAP;

      el.setAttribute('data-lexical-visible', '');
      // Context for the host's markup: styleable in CSS, readable from JS, and the same
      // values the .NET push carries.
      el.setAttribute('data-lexical-block-key', block.key ?? '');
      el.setAttribute('data-lexical-block-index', String(block.index));
      el.setAttribute('data-lexical-block-type', blockType);
    }
  };

  const untrack = trackHoveredBlock(editor, root, contentEl, (block) => {
    if (block === null) {
      // Deferred, not immediate: the pointer may simply be on its way to a rail. See
      // GUTTER_HIDE_GRACE_MS. hoveredKey is deliberately left as-is too — hiding is a
      // visual state, and an in-flight drag still needs the key it started from.
      scheduleHide();
      return;
    }
    cancelHide();
    hoveredKey = block.key;
    layout(block);

    if (block.key === lastPushedKey) {
      return;
    }
    lastPushedKey = block.key;
    onHover({
      nodeKey: block.key ?? '',
      index: block.index,
      blockType: block.element.tagName.toLowerCase(),
      textPreview: (block.element.textContent ?? '').slice(0, BLOCK_PREVIEW_CHARS),
    });
  });

  // Drag-to-reorder. A grip element carries draggable="true" (set in markup); it can sit
  // in any rail, so this is delegated from the root rather than bound per gutter.
  const onDragStart = (e: DragEvent) => {
    if ((e.target as HTMLElement).closest('[data-lexical-drag-grip]') === null) {
      return;
    }
    draggedKey = hoveredKey;
    if (e.dataTransfer !== null) {
      e.dataTransfer.effectAllowed = 'move';
      // Firefox requires data for a drag to start.
      e.dataTransfer.setData('text/plain', '');
    }
  };
  const onDragOver = (e: DragEvent) => {
    if (draggedKey === null) {
      return;
    }
    e.preventDefault();
    const blockEl = blockElementAt(contentEl, e.clientY);
    if (blockEl === null) {
      return;
    }
    const rect = blockEl.getBoundingClientRect();
    showDropLine(blockEl, e.clientY > rect.top + rect.height / 2);
  };
  const onDrop = (e: DragEvent) => {
    if (draggedKey === null) {
      return;
    }
    e.preventDefault();
    const blockEl = blockElementAt(contentEl, e.clientY);
    const rect = blockEl?.getBoundingClientRect();
    const after = blockEl !== null && rect !== undefined && e.clientY > rect.top + rect.height / 2;
    editor.update(() => {
      const dragged = draggedKey === null ? null : $getNodeByKey(draggedKey);
      const targetNode = blockEl === null ? null : $getNearestNodeFromDOMNode(blockEl);
      const target = targetNode === null ? null : targetNode.getTopLevelElement();
      if (dragged === null || target === null) {
        return;
      }
      if (target.getKey() === dragged.getKey()) {
        return;
      }
      if (after) {
        target.insertAfter(dragged);
      } else {
        target.insertBefore(dragged);
      }
    });
    draggedKey = null;
    hideDropLine();
  };
  const onDragEnd = () => {
    draggedKey = null;
    hideDropLine();
  };
  root.addEventListener('dragstart', onDragStart);
  root.addEventListener('dragover', onDragOver);
  root.addEventListener('drop', onDrop);
  root.addEventListener('dragend', onDragEnd);

  // "+" inserts a fresh paragraph below the hovered block and types "/" so the slash
  // menu (if the host added one) opens on the new line. Delegated for the same reason
  // the drag is: the button may live in any rail.
  const onAddClick = (e: Event) => {
    if ((e.target as HTMLElement).closest('[data-lexical-add-block]') === null) {
      return;
    }
    e.preventDefault();
    const key = hoveredKey;
    if (key === null) {
      return;
    }
    editor.update(() => {
      const node = $getNodeByKey(key);
      if (node === null) {
        return;
      }
      const paragraph = $createParagraphNode();
      node.getTopLevelElementOrThrow().insertAfter(paragraph);
      const selection = paragraph.select();
      selection.insertText('/');
    });
    editor.focus();
  };
  root.addEventListener('click', onAddClick);

  // Arriving on a rail is what cancels the pending hide — the other half of the grace
  // window. Leaving one restarts it, so a rail does not linger once the pointer is gone.
  const onGutterEnter = (): void => {
    pointerInGutter = true;
    cancelHide();
  };
  const onGutterLeave = (): void => {
    pointerInGutter = false;
    scheduleHide();
  };
  for (const el of gutterEls) {
    el.addEventListener('mouseenter', onGutterEnter);
    el.addEventListener('mouseleave', onGutterLeave);
  }

  return () => {
    untrack();
    cancelHide();
    root.removeEventListener('dragstart', onDragStart);
    root.removeEventListener('dragover', onDragOver);
    root.removeEventListener('drop', onDrop);
    root.removeEventListener('dragend', onDragEnd);
    root.removeEventListener('click', onAddClick);
    for (const el of gutterEls) {
      el.removeEventListener('mouseenter', onGutterEnter);
      el.removeEventListener('mouseleave', onGutterLeave);
      el.removeAttribute('data-lexical-visible');
    }
    dropLine.remove();
  };
}

// ---------------------------------------------------------------------------
// Floating link editor
// ---------------------------------------------------------------------------

/**
 * Dispatched by the toolbar link button (index.ts `runCommandToken`) right after
 * it inserts a fresh placeholder link, asking the floating link editor — if the
 * host placed one — to open in edit mode on that new link. Defined here and
 * imported by index.ts so the button and the overlay stay decoupled: no editor
 * ⇒ the command is simply unhandled and the placeholder link stands.
 */
export const OPEN_LINK_EDITOR_COMMAND: LexicalCommand<void> = createCommand('OPEN_LINK_EDITOR');

/**
 * Reads the link node the selection sits in (or spans) and its URL, or null when
 * there is none. Scans the anchor/focus and every selected node's ancestry, so it
 * works whether the caret is inside a link's text or the selection is anchored on
 * the enclosing block (e.g. right after wrapping a full-line selection). Must run
 * in a read context.
 */
function readLinkAtSelection(): { key: string; url: string } | null {
  const selection = $getSelection();
  if (!$isRangeSelection(selection)) {
    return null;
  }
  const candidates = [selection.anchor.getNode(), selection.focus.getNode(), ...selection.getNodes()];
  for (const node of candidates) {
    const link = $isLinkNode(node) ? node : $findMatchingParent(node, $isLinkNode);
    if ($isLinkNode(link)) {
      return { key: link.getKey(), url: link.getURL() };
    }
  }
  return null;
}

/**
 * Drives a `[data-lexical-link-editor]` popup, mirroring the Lexical playground's
 * FloatingLinkEditor: while the caret sits in a link it shows a *preview* (the URL
 * as a clickable link plus edit/remove buttons); the toolbar link button (via
 * {@link OPEN_LINK_EDITOR_COMMAND}) or the edit button switches it to an *edit*
 * form (a URL input with confirm/cancel). Confirm updates the link's URL in place
 * (empty unwraps it); the remove button is an ordinary `data-lexical-command`, so
 * the delegated dispatch handles it. All local — no JS→.NET interop.
 */
export function registerLinkEditor(
  editor: LexicalEditor,
  root: HTMLElement,
  contentEl: HTMLElement,
  editorEl: HTMLElement,
): () => void {
  const viewEl = editorEl.querySelector<HTMLElement>('[data-lexical-link-view]');
  const formEl = editorEl.querySelector<HTMLElement>('[data-lexical-link-edit-form]');
  const anchorEl = editorEl.querySelector<HTMLAnchorElement>('[data-lexical-link-preview]');
  const inputEl = editorEl.querySelector<HTMLInputElement>('[data-lexical-link-input]');

  let editMode = false;
  // Set by OPEN_LINK_EDITOR_COMMAND (the toolbar link button) so that the next
  // update which sees the freshly inserted link opens in edit mode rather than
  // preview. A flag — not a direct call — because editor.update() commits
  // asynchronously, so the link isn't yet readable when the command fires.
  let pendingEdit = false;
  let activeKey: string | null = null;

  const show = () => editorEl.setAttribute('data-lexical-visible', '');
  const hide = () => {
    editorEl.removeAttribute('data-lexical-visible');
    editMode = false;
    activeKey = null;
  };

  // Glue the popup below the link's rendered element (falls back to no-op until
  // the element exists), matching the other overlays' root-relative positioning.
  const position = () => {
    const el = activeKey === null ? null : editor.getElementByKey(activeKey);
    if (el !== null) {
      positionBelow(editorEl, el.getBoundingClientRect(), root);
    }
  };

  const setMode = (edit: boolean) => {
    viewEl?.toggleAttribute('hidden', edit);
    formEl?.toggleAttribute('hidden', !edit);
  };

  const showPreview = (url: string) => {
    if (anchorEl !== null) {
      anchorEl.href = url;
      anchorEl.textContent = url;
    }
    editMode = false;
    setMode(false);
    show();
    position();
  };

  const enterEdit = (url: string) => {
    editMode = true;
    if (inputEl !== null) {
      inputEl.value = url;
    }
    setMode(true);
    show();
    position();
    // Focus after the current click/dispatch settles, so the editor's own
    // focus() call in the delegated click handler doesn't steal it straight back.
    requestAnimationFrame(() => {
      inputEl?.focus();
      inputEl?.select();
    });
  };

  // Reflect the current selection: preview the link under the caret, or hide when
  // the caret leaves it. Frozen while editing so the popup doesn't flicker/close
  // as focus moves to the input.
  const update = () => {
    if (editMode) {
      return;
    }
    const link = editor.getEditorState().read(readLinkAtSelection);
    const sel = window.getSelection();
    if (
      link === null ||
      !sel ||
      sel.rangeCount === 0 ||
      !sel.anchorNode ||
      !contentEl.contains(sel.anchorNode)
    ) {
      hide();
      return;
    }
    activeKey = link.key;
    if (pendingEdit) {
      pendingEdit = false;
      enterEdit(link.url);
    } else {
      showPreview(link.url);
    }
  };

  const currentUrl = (): string =>
    activeKey === null
      ? ''
      : editor.getEditorState().read(() => {
          const node = $getNodeByKey(activeKey!);
          return $isLinkNode(node) ? node.getURL() : '';
        });

  const confirm = () => {
    const url = (inputEl?.value ?? '').trim();
    const key = activeKey;
    editor.update(() => {
      const node = key === null ? null : $getNodeByKey(key);
      if (!$isLinkNode(node)) {
        return;
      }
      if (url === '') {
        // Unwrap: promote the link's children in place, then drop the link.
        for (const child of node.getChildren()) {
          node.insertBefore(child);
        }
        node.remove();
      } else {
        node.setURL(url);
      }
    });
    editMode = false;
    setMode(false);
    editor.focus();
    // Re-evaluate against the restored selection: preview the updated link or hide.
    update();
  };

  const cancel = () => {
    editMode = false;
    setMode(false);
    editor.focus();
    update();
  };

  // Keep the editor selection intact when clicking the popup's own buttons/anchor
  // (the input still needs to receive focus, so let its mousedown through).
  const onMouseDown = (e: MouseEvent) => {
    if ((e.target as HTMLElement).closest('input') === null) {
      e.preventDefault();
    }
  };
  editorEl.addEventListener('mousedown', onMouseDown);

  const onClick = (e: Event) => {
    const target = e.target as HTMLElement;
    if (target.closest('[data-lexical-link-edit]') !== null) {
      enterEdit(currentUrl());
    } else if (target.closest('[data-lexical-link-confirm]') !== null) {
      confirm();
    } else if (target.closest('[data-lexical-link-cancel]') !== null) {
      cancel();
    }
  };
  editorEl.addEventListener('click', onClick);

  const onInputKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      confirm();
    } else if (e.key === 'Escape') {
      e.preventDefault();
      cancel();
    }
  };
  inputEl?.addEventListener('keydown', onInputKeyDown);

  // Clicking anywhere outside the popup while editing cancels the edit.
  const onDocMouseDown = (e: MouseEvent) => {
    if (editMode && !editorEl.contains(e.target as Node)) {
      cancel();
    }
  };
  document.addEventListener('mousedown', onDocMouseDown);

  const openCmd = editor.registerCommand(
    OPEN_LINK_EDITOR_COMMAND,
    () => {
      // Arm edit mode for the pending insert, and — if the link already committed
      // (synchronous update path) — open on it immediately.
      pendingEdit = true;
      const link = editor.getEditorState().read(readLinkAtSelection);
      if (link !== null) {
        pendingEdit = false;
        activeKey = link.key;
        enterEdit(link.url);
      }
      return false;
    },
    COMMAND_PRIORITY_LOW,
  );

  const onChange = () => update();
  document.addEventListener('selectionchange', onChange);
  window.addEventListener('scroll', onChange, true);
  window.addEventListener('resize', onChange);
  const unregister = editor.registerUpdateListener(() => update());

  return () => {
    editorEl.removeEventListener('mousedown', onMouseDown);
    editorEl.removeEventListener('click', onClick);
    inputEl?.removeEventListener('keydown', onInputKeyDown);
    document.removeEventListener('mousedown', onDocMouseDown);
    document.removeEventListener('selectionchange', onChange);
    window.removeEventListener('scroll', onChange, true);
    window.removeEventListener('resize', onChange);
    openCmd();
    unregister();
    hide();
  };
}
