// ---------------------------------------------------------------------------
// The JS half of <GutterMarkersExtension> — the worked example for ctx.blockLayout.
//
// No bundler, no `import 'lexical'`: an extension takes everything it needs from the
// host through `setup`/`ctx`. This one registers no nodes and does no interop — it only
// draws its own DOM beside each block, positioned with ctx.blockLayout, and owns that
// DOM's styling, click handling and (client-side) state entirely.
//
// Types ship alongside the library:
//   _content/Blazor.Lexical/blazor-lexical-extension.d.ts
// ---------------------------------------------------------------------------

// A stand-in "speaker" per block, cycled by block index. Real speaker assignment would
// be the app's own business logic; the palette is all this sample needs to demonstrate.
const SPEAKERS = [
  { initial: 'A', color: '#e5484d' },
  { initial: 'B', color: '#0091ff' },
  { initial: 'C', color: '#30a46c' },
  { initial: 'D', color: '#f5a623' },
  { initial: 'E', color: '#8e4ec6' },
  { initial: 'F', color: '#d6409f' },
];

const TAB_WIDTH = 22;
const STAR_SIZE = 20;
const BAR_WIDTH = 4;
const GAP = 4;

/**
 * @param {import('../../../Source/Blazor.Lexical/js/src/extension').LexicalExtensionSetup} _setup
 * @returns {import('../../../Source/Blazor.Lexical/js/src/extension').LexicalExtensionModule}
 */
export default function gutterMarkersExtension(_setup) {
  return {
    name: 'samples/gutter-markers',

    register(ctx) {
      const { root, editor, blockLayout } = ctx;

      // One JS-owned layer for every marker, positioned relative to root like the
      // built-in rails. pointer-events pass through except on the star (see CSS).
      const layer = document.createElement('div');
      layer.className = 'gutter-markers__layer';
      root.appendChild(layer);

      const starred = new Set(); // block keys the user starred — client-side only
      const changed = new Set(); // block keys edited this session

      // Rebuild every marker from a fresh block snapshot. Cheap enough to redo wholesale
      // on each reflow; state lives in the two Sets above, not in the DOM.
      const render = () => {
        layer.replaceChildren();
        const rootTop = root.getBoundingClientRect().top;
        for (const block of blockLayout.blocks()) {
          const rect = block.element.getBoundingClientRect();
          const top = rect.top - rootTop;
          const speaker = SPEAKERS[block.index % SPEAKERS.length];

          // Left-outside: a full-height colored speaker tab hanging off the card.
          const tab = document.createElement('div');
          tab.className = 'gutter-markers__tab';
          tab.textContent = speaker.initial;
          tab.style.top = `${top}px`;
          tab.style.height = `${rect.height}px`;
          tab.style.width = `${TAB_WIDTH}px`;
          tab.style.left = `${blockLayout.anchor('left-outside', TAB_WIDTH)}px`;
          tab.style.background = speaker.color;
          layer.appendChild(tab);

          // Right-inside: the star, then the changed bar stacked further out.
          let consumed = 0;
          const on = starred.has(block.key);
          const star = document.createElement('button');
          star.type = 'button';
          star.className = on ? 'gutter-markers__star is-on' : 'gutter-markers__star';
          star.dataset.blockKey = block.key;
          star.textContent = on ? '★' : '☆';
          star.style.top = `${top}px`;
          star.style.width = `${STAR_SIZE}px`;
          star.style.height = `${STAR_SIZE}px`;
          star.style.left = `${blockLayout.anchor('right-inside', STAR_SIZE, consumed)}px`;
          layer.appendChild(star);
          consumed += STAR_SIZE + GAP;

          if (changed.has(block.key)) {
            const bar = document.createElement('div');
            bar.className = 'gutter-markers__bar';
            bar.style.top = `${top}px`;
            bar.style.height = `${rect.height}px`;
            bar.style.width = `${BAR_WIDTH}px`;
            bar.style.left = `${blockLayout.anchor('right-inside', BAR_WIDTH, consumed)}px`;
            layer.appendChild(bar);
          }
        }
      };

      // Keep the editor selection alive across a star click, like the badge sample.
      const onMouseDown = (e) => {
        if (e.target.closest('.gutter-markers__star')) {
          e.preventDefault();
        }
      };
      // The extension's own delegated click — scoped to its own marker, never the core
      // data-lexical-command dispatch.
      const onClick = (e) => {
        const star = e.target.closest('.gutter-markers__star');
        if (!star) {
          return;
        }
        const key = star.dataset.blockKey;
        starred.has(key) ? starred.delete(key) : starred.add(key);
        render();
      };
      root.addEventListener('mousedown', onMouseDown);
      root.addEventListener('click', onClick);

      // Real logic, not decoration: mark which blocks were touched this session, then
      // re-render so their changed bar appears.
      const stopUpdates = editor.registerUpdateListener(({ dirtyElements }) => {
        for (const key of dirtyElements.keys()) {
          if (key !== 'root') {
            changed.add(key);
          }
        }
        render();
      });

      // Reposition on reflow/resize — the block gutter's geometry, reused.
      const stopReposition = blockLayout.onBlocksChanged(render);

      render();

      return () => {
        stopUpdates();
        stopReposition();
        root.removeEventListener('mousedown', onMouseDown);
        root.removeEventListener('click', onClick);
        layer.remove();
      };
    },
  };
}
