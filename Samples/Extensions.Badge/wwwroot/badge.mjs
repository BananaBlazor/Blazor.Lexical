// ---------------------------------------------------------------------------
// The JS half of <BadgeExtension> — the reference implementation of the
// Blazor.Lexical extension module contract.
//
// Note what is NOT here: no bundler, no npm install, no `import 'lexical'`. An
// extension takes Lexical from the host (`setup.lexical`) so its node classes
// extend the very classes the editor registers; bundling a second copy of Lexical
// would produce different classes that the editor would refuse. That is why this
// file is plain, hand-written ESM served as a static web asset, and why the host
// import()s it by URL instead of bundling it.
//
// Types for the contract ship alongside the library:
//   _content/Blazor.Lexical/blazor-lexical-extension.d.ts
// ---------------------------------------------------------------------------

/**
 * @param {import('../../../Source/Blazor.Lexical/js/src/extension').LexicalExtensionSetup} setup
 * @returns {import('../../../Source/Blazor.Lexical/js/src/extension').LexicalExtensionModule}
 */
export default function badgeExtension(setup) {
  const {
    $applyNodeReplacement,
    $createTextNode,
    $getSelection,
    $isRangeSelection,
    $nodesOfType,
    TextNode,
  } = setup.lexical;

  // The host's @lexical/utils, handed over the same way its Lexical is — no second
  // copy to bundle, and nothing to npm-install for a build-step-free extension.
  const { addClassNamesToElement, mergeRegister } = setup.utils;

  const defaultLabel = (setup.options && setup.options.label) || 'Badge';

  /**
   * A badge: an atomic, styled token. A segmented TextNode subclass — the smallest
   * custom node that still exercises registration, DOM rendering and JSON/HTML
   * round-trips.
   */
  class BadgeNode extends TextNode {
    static getType() {
      return 'badge';
    }

    static clone(node) {
      return new BadgeNode(node.__text, node.__key);
    }

    static importJSON(serializedNode) {
      return $createBadgeNode(serializedNode.text).updateFromJSON(serializedNode);
    }

    createDOM(config) {
      const dom = super.createDOM(config);
      // The class comes from the theme, not a literal — the extension declares the
      // default below and the host can override `badge` in its own Theme.
      addClassNamesToElement(dom, config.theme.badge);
      dom.setAttribute('data-badge', '');
      return dom;
    }

    // HTML round-tripping: a custom node survives getHtml/setHtml only if it says
    // how to write itself out and how to recognise itself coming back.
    exportDOM() {
      const element = document.createElement('span');
      element.setAttribute('data-badge', '');
      element.textContent = this.__text;
      return { element };
    }

    static importDOM() {
      return {
        span: (domNode) =>
          domNode.hasAttribute('data-badge')
            ? {
                conversion: (node) => ({ node: $createBadgeNode(node.textContent ?? '') }),
                priority: 1,
              }
            : null,
      };
    }
  }

  function $createBadgeNode(text) {
    const node = new BadgeNode(text);
    // Segmented mode makes the badge behave as one unit (like a mention token).
    node.setMode('segmented');
    return $applyNodeReplacement(node);
  }

  /** The editor, captured in register() so invoke() can reach it. */
  let editor;

  function insertBadge(text) {
    if (!editor) {
      return false;
    }
    editor.update(() => {
      const selection = $getSelection();
      if (!$isRangeSelection(selection)) {
        return;
      }
      const badge = $createBadgeNode(text);
      selection.insertNodes([badge]);
      // A trailing space so typing continues normally after the atomic token.
      const space = $createTextNode(' ');
      badge.insertAfter(space);
      space.select(1, 1);
    });
    return true;
  }

  return {
    // Unique and namespaced: this is what the editor names if another extension
    // collides with this one, and what someone else's conflictsWith would target.
    name: 'samples/badge',
    // Read before createEditor — this is the only window for custom nodes.
    nodes: [BadgeNode],

    // Theme fragment for this extension's own node, merged into the editor's theme
    // before it is created. The key is namespaced to the extension ('badge', never
    // 'paragraph'), and the host wins if it declares the same key itself.
    theme: { badge: 'blazor-lexical-badge' },

    register(ctx) {
      editor = ctx.editor;

      // The extension owns its own delegated listeners under its own markers; it
      // does not extend the core data-lexical-command switch, which stays closed.
      const onMouseDown = (e) => {
        if (e.target.closest('[data-lexical-badge-insert]')) {
          // Keep the editor selection alive across the button click.
          e.preventDefault();
        }
      };

      const onClick = (e) => {
        if (e.target.closest('[data-lexical-badge-insert]')) {
          insertBadge(defaultLabel);
          ctx.editor.focus();
          return;
        }
        const badgeEl = e.target.closest('[data-badge]');
        // Opt-in interop: without a .NET handler this stays purely client-side
        // (invokeDotNet would throw), so the check is the invariant, not politeness.
        if (badgeEl && setup.canInvokeDotNet) {
          setup.invokeDotNet('badgeClicked', badgeEl.textContent ?? '').catch((error) => {
            console.error('[Samples.Extensions.Badge] badgeClicked failed', error);
          });
        }
      };

      ctx.root.addEventListener('mousedown', onMouseDown);
      ctx.root.addEventListener('click', onClick);
      // mergeRegister (from the host's @lexical/utils) collapses several teardowns
      // into the single function register() returns.
      return mergeRegister(
        () => ctx.root.removeEventListener('mousedown', onMouseDown),
        () => ctx.root.removeEventListener('click', onClick),
      );
    },

    // The .NET→JS direction: LexicalExtension.InvokeJsAsync lands here.
    invoke(method, args) {
      switch (method) {
        case 'insert':
          return insertBadge(args[0] ?? defaultLabel);
        case 'count':
          return editor
            ? editor.getEditorState().read(() => $nodesOfType(BadgeNode).length)
            : 0;
        default:
          console.error(`[Samples.Extensions.Badge] unknown method '${method}'`);
          return null;
      }
    },
  };
}
