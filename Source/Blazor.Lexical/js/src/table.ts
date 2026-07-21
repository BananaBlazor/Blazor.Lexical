// ---------------------------------------------------------------------------
// Table support: the Lexical table runtime wiring, the in-cell action menu
// overlay, and the toolbar's grid-size insert picker.
//
// This whole module is a lazily-loaded chunk: index.ts only ever `import()`s it
// (never statically), so esbuild code-splits @lexical/table (~90kb) out of the core
// bundle. create() loads it only when the editor enables the 'table' feature.
//
// It is loaded THROUGH THE EXTENSION CONTRACT: the default export below is a
// `LexicalExtensionFactory`, exactly like a consumer extension's, and create() runs it
// in the same descriptor loop. Only the bundling differs — a literal import() into our
// own chunk here, a runtime URL there. The named exports beside it stay for the core
// call-sites that are not extension concerns (the `table:` command token, the
// `insertTable` export).
//
// The two `register*` overlays follow the same split as overlays.ts: Blazor
// renders the markup (the action-menu trigger + dropdown, the picker grid) as
// ChildContent inside the editor root; this module owns only the behaviour —
// positioning, show/hide, and running the table operations against the model.
// Each activates only when its marker element is present under the root (the
// host opted in by placing the component) and returns a teardown. Like the link
// editor's local buttons, the menu/picker controls are handled here directly
// (not via data-lexical-command), so they add no JS→.NET interop of their own.
// ---------------------------------------------------------------------------

import {
  $getSelection,
  $isRangeSelection,
  type LexicalEditor,
} from 'lexical';
import {
  TableNode,
  TableRowNode,
  TableCellNode,
  registerTablePlugin,
  registerTableSelectionObserver,
  $getTableCellNodeFromLexicalNode,
  $getTableNodeFromLexicalNodeOrThrow,
  $getTableRowIndexFromTableCellNode,
  $getTableColumnIndexFromTableCellNode,
  $insertTableRowAtSelection,
  $insertTableColumnAtSelection,
  $deleteTableRowAtSelection,
  $deleteTableColumnAtSelection,
  $setTableRowIsHeader,
  $setTableColumnIsHeader,
  TableCellHeaderStates,
  INSERT_TABLE_COMMAND,
  type InsertTableCommandPayloadHeaders,
} from '@lexical/table';
import type { LexicalExtensionModule } from './extension';

// A freshly inserted table gets a bold top row but no header column, matching
// the playground's common default. Shared by the picker and the C# insertTable.
const DEFAULT_HEADERS: InsertTableCommandPayloadHeaders = { rows: true, columns: false };

/**
 * The table node classes to register in `createEditor`'s `nodes[]`. Exported from
 * this lazily-loaded module so the ~90kb of `@lexical/table` (and these nodes) stay
 * out of the core bundle until an editor opts into the table feature.
 */
export const tableNodes = [TableNode, TableRowNode, TableCellNode];

/** Positions `el` absolutely (relative to `root`) at a viewport rect's corner. */
function positionAt(el: HTMLElement, top: number, left: number, root: HTMLElement): void {
  const rootRect = root.getBoundingClientRect();
  el.style.top = `${top - rootRect.top}px`;
  el.style.left = `${left - rootRect.left}px`;
}

/**
 * Wires the Lexical table runtime: the table command/transform plugin (which
 * owns INSERT_TABLE_COMMAND and keeps table structure valid) plus the selection
 * observer (cell drag-selection, keyboard navigation, resize handlers). Called by
 * create() when the table feature is enabled, so tables behave correctly (including
 * paste and serialization) even with no table editor overlay present.
 */
export function registerTable(editor: LexicalEditor): () => void {
  const unregisterPlugin = registerTablePlugin(editor);
  const unregisterObserver = registerTableSelectionObserver(editor);
  return () => {
    unregisterObserver();
    unregisterPlugin();
  };
}

/** Dispatches INSERT_TABLE_COMMAND with the given dimensions (clamped ≥ 1). */
export function insertTableWithDimensions(
  editor: LexicalEditor,
  rows: number,
  columns: number,
  includeHeaders: InsertTableCommandPayloadHeaders = DEFAULT_HEADERS,
): void {
  editor.dispatchCommand(INSERT_TABLE_COMMAND, {
    rows: String(Math.max(1, Math.floor(rows))),
    columns: String(Math.max(1, Math.floor(columns))),
    includeHeaders,
  });
}

// ---------------------------------------------------------------------------
// In-cell action menu
// ---------------------------------------------------------------------------

/** Key of the table cell the caret currently sits in, or null. */
function currentCellKey(editor: LexicalEditor): string | null {
  return editor.getEditorState().read(() => {
    const selection = $getSelection();
    if (!$isRangeSelection(selection)) {
      return null;
    }
    const cell = $getTableCellNodeFromLexicalNode(selection.anchor.getNode());
    return cell === null ? null : cell.getKey();
  });
}

/**
 * Drives a `[data-lexical-table-menu]` overlay, mirroring the playground's table
 * action menu: while the caret sits in a table cell the trigger appears in that
 * cell's top-right corner; clicking it opens a dropdown of row/column/header/
 * delete operations. Each operation runs against the current cell selection (kept
 * intact because the popup's mousedown is prevented, like the link editor) and
 * then closes the menu. All local — no JS→.NET interop.
 */
export function registerTableActionMenu(
  editor: LexicalEditor,
  root: HTMLElement,
  contentEl: HTMLElement,
  menuEl: HTMLElement,
): () => void {
  const triggerEl = menuEl.querySelector<HTMLElement>('[data-lexical-table-trigger]');
  const dropdownEl = menuEl.querySelector<HTMLElement>('[data-lexical-table-dropdown]');

  let activeKey: string | null = null;

  const closeDropdown = () => {
    menuEl.removeAttribute('data-lexical-table-open');
    dropdownEl?.setAttribute('hidden', '');
  };
  const openDropdown = () => {
    menuEl.setAttribute('data-lexical-table-open', '');
    dropdownEl?.removeAttribute('hidden');
  };

  const hide = () => {
    menuEl.removeAttribute('data-lexical-visible');
    closeDropdown();
    activeKey = null;
  };

  // Reflect the current selection: show the trigger in the caret's cell, or hide
  // when the caret leaves every table.
  const update = () => {
    const key = currentCellKey(editor);
    const sel = window.getSelection();
    if (
      key === null ||
      !sel ||
      sel.rangeCount === 0 ||
      !sel.anchorNode ||
      !contentEl.contains(sel.anchorNode)
    ) {
      hide();
      return;
    }
    if (key !== activeKey) {
      // Caret moved to a different cell — collapse any open dropdown.
      closeDropdown();
    }
    activeKey = key;
    const cellEl = editor.getElementByKey(key);
    if (cellEl === null) {
      hide();
      return;
    }
    const rect = cellEl.getBoundingClientRect();
    const triggerWidth = triggerEl?.offsetWidth ?? 20;
    positionAt(menuEl, rect.top + 2, rect.right - triggerWidth - 2, root);
    menuEl.setAttribute('data-lexical-visible', '');
  };

  const runAction = (action: string) => {
    editor.update(() => {
      const selection = $getSelection();
      if (!$isRangeSelection(selection)) {
        return;
      }
      const cell = $getTableCellNodeFromLexicalNode(selection.anchor.getNode());
      if (cell === null) {
        return;
      }
      switch (action) {
        case 'row-above':
          $insertTableRowAtSelection(false);
          break;
        case 'row-below':
          $insertTableRowAtSelection(true);
          break;
        case 'col-left':
          $insertTableColumnAtSelection(false);
          break;
        case 'col-right':
          $insertTableColumnAtSelection(true);
          break;
        case 'row-delete':
          $deleteTableRowAtSelection();
          break;
        case 'col-delete':
          $deleteTableColumnAtSelection();
          break;
        case 'row-header': {
          const table = $getTableNodeFromLexicalNodeOrThrow(cell);
          const rowIndex = $getTableRowIndexFromTableCellNode(cell);
          $setTableRowIsHeader(table, rowIndex, !cell.hasHeaderState(TableCellHeaderStates.ROW));
          break;
        }
        case 'col-header': {
          const table = $getTableNodeFromLexicalNodeOrThrow(cell);
          const colIndex = $getTableColumnIndexFromTableCellNode(cell);
          $setTableColumnIsHeader(table, colIndex, !cell.hasHeaderState(TableCellHeaderStates.COLUMN));
          break;
        }
        case 'table-delete': {
          const table = $getTableNodeFromLexicalNodeOrThrow(cell);
          table.selectPrevious();
          table.remove();
          break;
        }
      }
    });
    editor.focus();
  };

  // Keep the editor's cell selection intact when clicking the menu (the row/col
  // operations act on that selection), just like the link editor's popup.
  const onMouseDown = (e: MouseEvent) => e.preventDefault();
  menuEl.addEventListener('mousedown', onMouseDown);

  const onClick = (e: Event) => {
    const target = e.target as HTMLElement;
    if (target.closest('[data-lexical-table-trigger]') !== null) {
      if (menuEl.hasAttribute('data-lexical-table-open')) {
        closeDropdown();
      } else {
        openDropdown();
      }
      return;
    }
    const actionEl = target.closest<HTMLElement>('[data-lexical-table-action]');
    if (actionEl !== null) {
      runAction(actionEl.getAttribute('data-lexical-table-action')!);
      closeDropdown();
    }
  };
  menuEl.addEventListener('click', onClick);

  // Clicking anywhere outside the menu collapses an open dropdown.
  const onDocMouseDown = (e: MouseEvent) => {
    if (!menuEl.contains(e.target as Node)) {
      closeDropdown();
    }
  };
  document.addEventListener('mousedown', onDocMouseDown);

  const onChange = () => update();
  document.addEventListener('selectionchange', onChange);
  window.addEventListener('scroll', onChange, true);
  window.addEventListener('resize', onChange);
  const unregister = editor.registerUpdateListener(() => update());

  return () => {
    menuEl.removeEventListener('mousedown', onMouseDown);
    menuEl.removeEventListener('click', onClick);
    document.removeEventListener('mousedown', onDocMouseDown);
    document.removeEventListener('selectionchange', onChange);
    window.removeEventListener('scroll', onChange, true);
    window.removeEventListener('resize', onChange);
    unregister();
    hide();
  };
}

// ---------------------------------------------------------------------------
// Grid-size insert picker
// ---------------------------------------------------------------------------

/**
 * Turns a `[data-lexical-table-picker]` element (a toolbar "Insert ▾" trigger +
 * a grid popover) into a hover-to-size table inserter, cloning the Notion/Docs
 * grid picker: the trigger opens the grid, moving over a cell highlights the R×C
 * span (and updates the label), and clicking inserts a table of that size. The
 * popover is CSS-anchored below the trigger, so this only toggles it and tracks
 * the hovered span. No JS→.NET interop.
 */
export function registerTablePicker(
  editor: LexicalEditor,
  pickerEl: HTMLElement,
): () => void {
  const triggerEl = pickerEl.querySelector<HTMLElement>('[data-lexical-table-picker-trigger]');
  const popoverEl = pickerEl.querySelector<HTMLElement>('[data-lexical-table-picker-popover]');
  const labelEl = pickerEl.querySelector<HTMLElement>('[data-lexical-table-picker-label]');
  const cells = Array.from(
    pickerEl.querySelectorAll<HTMLElement>('[data-lexical-table-grid-cell]'),
  );

  const cellCoords = (el: HTMLElement) => ({
    row: Number(el.getAttribute('data-row')),
    col: Number(el.getAttribute('data-col')),
  });

  const highlight = (rows: number, cols: number) => {
    for (const cell of cells) {
      const { row, col } = cellCoords(cell);
      cell.toggleAttribute('data-lexical-active', row <= rows && col <= cols);
    }
    if (labelEl !== null) {
      labelEl.textContent = rows > 0 && cols > 0 ? `${rows} × ${cols}` : 'Cancel';
    }
  };

  const open = () => {
    highlight(0, 0);
    pickerEl.setAttribute('data-lexical-table-open', '');
    popoverEl?.removeAttribute('hidden');
  };
  const close = () => {
    pickerEl.removeAttribute('data-lexical-table-open');
    popoverEl?.setAttribute('hidden', '');
  };

  // Preserve the editor selection while interacting with the picker so the
  // inserted table lands where the caret was.
  const onMouseDown = (e: MouseEvent) => e.preventDefault();
  pickerEl.addEventListener('mousedown', onMouseDown);

  const onOver = (e: Event) => {
    const cell = (e.target as HTMLElement).closest<HTMLElement>('[data-lexical-table-grid-cell]');
    if (cell === null) {
      return;
    }
    const { row, col } = cellCoords(cell);
    highlight(row, col);
  };
  pickerEl.addEventListener('mouseover', onOver);

  const onClick = (e: Event) => {
    const target = e.target as HTMLElement;
    if (target.closest('[data-lexical-table-picker-trigger]') !== null) {
      if (pickerEl.hasAttribute('data-lexical-table-open')) {
        close();
      } else {
        open();
      }
      return;
    }
    const cell = target.closest<HTMLElement>('[data-lexical-table-grid-cell]');
    if (cell !== null) {
      const { row, col } = cellCoords(cell);
      insertTableWithDimensions(editor, row, col);
      close();
      editor.focus();
    }
  };
  pickerEl.addEventListener('click', onClick);

  // Click outside closes the popover.
  const onDocMouseDown = (e: MouseEvent) => {
    if (!pickerEl.contains(e.target as Node)) {
      close();
    }
  };
  document.addEventListener('mousedown', onDocMouseDown);

  return () => {
    pickerEl.removeEventListener('mousedown', onMouseDown);
    pickerEl.removeEventListener('mouseover', onOver);
    pickerEl.removeEventListener('click', onClick);
    document.removeEventListener('mousedown', onDocMouseDown);
    close();
  };
}


// ---------------------------------------------------------------------------
// The extension module
// ---------------------------------------------------------------------------

/**
 * The table feature as a `LexicalExtensionFactory` — the built-in tier of the same
 * contract consumer extensions use. It declares the table nodes (read before
 * `createEditor`, the only window in which nodes can be added) and, once the editor
 * exists, wires the Lexical table runtime plus whichever table overlays the host
 * placed. No `invoke` handler: the table feature is entirely client-side.
 */
export default function tableExtension(): LexicalExtensionModule {
  return {
    nodes: tableNodes,
    register({ editor, root, content }) {
      const cleanups = [registerTable(editor)];

      // Same marker-presence opt-in as the overlays in overlays.ts: the behaviour is
      // wired only when the host rendered the component.
      const menuEl = root.querySelector<HTMLElement>('[data-lexical-table-menu]');
      if (menuEl) {
        cleanups.push(registerTableActionMenu(editor, root, content, menuEl));
      }
      const pickerEl = root.querySelector<HTMLElement>('[data-lexical-table-picker]');
      if (pickerEl) {
        cleanups.push(registerTablePicker(editor, pickerEl));
      }

      return () => {
        for (const cleanup of cleanups) {
          cleanup();
        }
      };
    },
  };
}
