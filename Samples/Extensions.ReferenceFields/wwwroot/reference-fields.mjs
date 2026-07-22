// ---------------------------------------------------------------------------
// The JS half of <ReferenceFieldsExtension> — the worked example for
// setup.primitives (ghost completion + entity commit).
//
// No bundler, no `import 'lexical'`: an extension takes everything it needs from
// the host through `setup`. This one registers no nodes; it treats the editor as a
// single "typed reference field" whose whole text content is the query, and wires
// the two SDK primitives to it:
//
//   * setup.primitives.entityCommit — the buffer→resolve→commit→create-if-missing
//     state machine. The field text drives setQuery on every edit; Tab/Enter
//     commits; ↓ cycles the active match. A city the pool doesn't contain drives
//     the optimistic create (createIfMissing below simulates a slow backend).
//   * setup.primitives.ghost — the muted "rest of the word" hint after the caret.
//     `read` returns the best match's suffix; the primitive paints it in an overlay
//     OUTSIDE the contenteditable, so it never enters the document/state/history.
//
// Types ship alongside the library:
//   _content/Blazor.Lexical/blazor-lexical-extension.d.ts
// ---------------------------------------------------------------------------

/**
 * @param {import('../../../Source/Blazor.Lexical/js/src/extension').LexicalExtensionSetup} setup
 * @returns {import('../../../Source/Blazor.Lexical/js/src/extension').LexicalExtensionModule}
 */
export default function referenceFieldsExtension(setup) {
  const { $getRoot, $getSelection, $isRangeSelection, $createParagraphNode, $createTextNode } =
    setup.lexical;

  const candidates = (setup.options && setup.options.candidates) || [];

  /** Simulates a slow backend minting a brand-new city — the create-if-missing path. */
  const createCity = (query) =>
    new Promise((resolve) => {
      setTimeout(() => {
        const id = `city-${query.trim().toLowerCase().replace(/\s+/g, '-')}`;
        resolve({ id, text: query.trim() });
      }, 600);
    });

  let editor;
  let root;
  let controller;
  // The ghost's teardown while it is attached, or null while it is off — the toggle
  // demonstrates calling it.
  let ghostTeardown = null;
  let read;

  /** The field's plain text, read from the current editor state. */
  const fieldText = () => editor.getEditorState().read(() => $getRoot().getTextContent());

  /** Replaces the whole field with `text`, caret at the end. */
  const setFieldText = (text) => {
    editor.update(() => {
      const rootNode = $getRoot();
      rootNode.clear();
      const paragraph = $createParagraphNode();
      const node = $createTextNode(text);
      paragraph.append(node);
      rootNode.append(paragraph);
      node.selectEnd();
    });
  };

  /**
   * Accepts the current completion. A match completes the field to its text and commits
   * it; no match with text falls through to entityCommit's create-if-missing; empty is a
   * no-op. setQuery is set synchronously first so commit() is deterministic rather than
   * racing the async update listener.
   */
  const accept = () => {
    const { best } = controller.current();
    if (best !== null) {
      controller.setQuery(best.text);
      setFieldText(best.text);
    }
    controller.commit();
  };

  return {
    name: 'samples/reference-fields',

    register(ctx) {
      editor = ctx.editor;
      root = ctx.root;

      controller = setup.primitives.entityCommit.create({
        candidates: () => candidates,
        createIfMissing: createCity,
        onCommit: (entity, info) => {
          // Fire-and-forget to .NET; safe even when no callback is wired (it no-ops).
          setup.notifyDotNet('committed', entity.id, entity.text, info.created, info.provisional);
        },
        onResolved: (provisionalId, entity) => {
          setup.notifyDotNet('resolved', provisionalId, entity.id);
        },
        onError: (query, error) => {
          console.error(`[Samples.Extensions.ReferenceFields] create failed for '${query}'`, error);
        },
      });

      // Keep the query in lockstep with the field text on every edit.
      const stopUpdates = editor.registerUpdateListener(() => {
        controller.setQuery(fieldText());
      });

      // The ghost reads the best match's remaining suffix at the caret.
      read = () => {
        const { query, best } = controller.current();
        if (best === null) {
          return null;
        }
        // Guard the slice: best is a prefix match, so the suffix is best.text past the
        // query length (best.text's own casing preserved — "par" + "is").
        if (!best.text.toLowerCase().startsWith(query.toLowerCase())) {
          return null;
        }
        const suffix = best.text.slice(query.length);
        if (suffix.length === 0) {
          return null;
        }
        const selection = $getSelection();
        if (!$isRangeSelection(selection) || !selection.isCollapsed()) {
          return null;
        }
        return { anchorKey: selection.anchor.key, text: suffix };
      };
      ghostTeardown = setup.primitives.ghost.attach(editor, root, read);

      // Tab/Enter accept, ↓ cycles the active match. Capture phase so we win over the
      // editor; a synthetic selectionchange nudges the ghost to re-point after a cycle
      // (cycle() changes no editor state, so nothing else would repaint it).
      const onKeyDown = (e) => {
        if (e.key === 'Tab' || e.key === 'Enter') {
          e.preventDefault();
          e.stopPropagation();
          accept();
        } else if (e.key === 'ArrowDown') {
          e.preventDefault();
          e.stopPropagation();
          controller.cycle();
          document.dispatchEvent(new Event('selectionchange'));
        }
      };
      ctx.content.addEventListener('keydown', onKeyDown, true);

      return () => {
        stopUpdates();
        ctx.content.removeEventListener('keydown', onKeyDown, true);
        if (ghostTeardown) {
          ghostTeardown();
          ghostTeardown = null;
        }
        controller.dispose();
      };
    },

    invoke(method, args) {
      switch (method) {
        case 'setGhost': {
          const enabled = args[0] === true;
          if (enabled && ghostTeardown === null) {
            ghostTeardown = setup.primitives.ghost.attach(editor, root, read);
          } else if (!enabled && ghostTeardown !== null) {
            ghostTeardown();
            ghostTeardown = null;
          }
          return enabled;
        }
        case 'inspect': {
          const { query, best, alternates } = controller.current();
          return {
            query,
            best: best ? best.text : '',
            alternates: alternates.map((a) => a.text),
          };
        }
        default:
          console.error(`[Samples.Extensions.ReferenceFields] unknown method '${method}'`);
          return null;
      }
    },
  };
}
