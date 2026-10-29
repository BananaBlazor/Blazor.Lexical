// Consumer extension used only by the atomic-block and indent harness pages.
//
// A block DecoratorNode built from `setup.lexical`, made atomic through
// `setup.primitives.atomicBlock`. `variant` selects the node:
//   'indentable' — type `harness-block`, implements IndentableBlockNode and opts in;
//   'plain'      — type `harness-block-plain`, has the methods but canIndent() is false.
//
// Test hooks it writes, all under the editor:
//   - each block's DOM: `data-indent` (the node's own level, reflected by createDOM/updateDOM);
//   - every block element and decorator: `data-harness-indent`, mirrored from the editor
//     state after each update, so a test reads levels without moving focus;
//   - ctx.root: `data-harness-clicks`, the hit count of the node's own LOW-priority
//     CLICK_COMMAND handler (registered after the atomic helper's).
// Insert path: a button inside the editor carrying `data-harness-block-insert="<variant>"`.

/// <reference path="./_content/Blazor.Lexical/blazor-lexical-extension.d.ts" />

const SELECTED_CLASS = 'harness-block--selected';

export default function harnessBlockExtension(setup) {
  const variant = setup.options?.variant ?? 'indentable';
  if (variant !== 'indentable' && variant !== 'plain') {
    throw new Error(`harness-block-decorator: unknown variant '${variant}'`);
  }
  const type = variant === 'indentable' ? 'harness-block' : 'harness-block-plain';
  const {
    $getRoot,
    $isDecoratorNode,
    $isElementNode,
    CLICK_COMMAND,
    COMMAND_PRIORITY_LOW,
    DecoratorNode,
  } = setup.lexical;
  const { $insertNodeToNearestRoot, mergeRegister } = setup.utils;

  class HarnessBlockNode extends DecoratorNode {
    static getType() {
      return type;
    }

    static clone(node) {
      return new HarnessBlockNode(node.__label, node.__indent, node.__key);
    }

    static importJSON(json) {
      return new HarnessBlockNode().updateFromJSON(json);
    }

    constructor(label = '', indent = 0, key) {
      super(key);
      this.__label = label;
      this.__indent = indent;
    }

    updateFromJSON(json) {
      const node = super.updateFromJSON(json).getWritable();
      node.__label = json.label ?? '';
      node.__indent = json.indent ?? 0;
      return node;
    }

    exportJSON() {
      return { ...super.exportJSON(), label: this.getLatest().__label, indent: this.getIndent() };
    }

    exportDOM() {
      const element = document.createElement('div');
      element.setAttribute('data-harness-block', variant);
      element.setAttribute('data-indent', String(this.getIndent()));
      element.textContent = this.getLatest().__label;
      return { element };
    }

    createDOM() {
      const dom = document.createElement('div');
      dom.className = `harness-block harness-block--${variant}`;
      dom.contentEditable = 'false';
      dom.setAttribute('data-harness-block', variant);
      dom.textContent = this.__label;
      reflectIndent(dom, this.__indent);
      return dom;
    }

    updateDOM(prev, dom) {
      if (prev.__indent !== this.__indent) {
        reflectIndent(dom, this.__indent);
      }
      return false;
    }

    decorate() {
      return null;
    }

    isInline() {
      return false;
    }

    canIndent() {
      return variant === 'indentable';
    }

    getIndent() {
      return this.getLatest().__indent;
    }

    setIndent(indent) {
      const node = this.getWritable();
      node.__indent = indent;
      return node;
    }
  }

  function reflectIndent(dom, indent) {
    dom.setAttribute('data-indent', String(indent));
    dom.style.paddingInlineStart = indent ? `${indent * 2}em` : '';
  }

  const isBlock = (node) => node instanceof HarnessBlockNode;

  return {
    name: `harness/block-${variant}`,
    nodes: [HarnessBlockNode],

    register(ctx) {
      const { editor, root } = ctx;

      // Mirrors every block's level onto its DOM as data-harness-indent.
      const mirror = () => {
        editor.getEditorState().read(() => {
          const visit = (node) => {
            const isElementBlock = $isElementNode(node) && !node.isInline();
            if (isElementBlock || ($isDecoratorNode(node) && typeof node.getIndent === 'function')) {
              editor.getElementByKey(node.getKey())
                ?.setAttribute('data-harness-indent', String(node.getIndent()));
            }
            if ($isElementNode(node)) {
              node.getChildren().forEach(visit);
            }
          };
          $getRoot().getChildren().forEach(visit);
        });
      };

      const onMouseDown = (e) => {
        if (e.target.closest(`[data-harness-block-insert="${variant}"]`)) {
          e.preventDefault();
        }
      };
      const onClick = (e) => {
        if (e.target.closest(`[data-harness-block-insert="${variant}"]`)) {
          editor.update(() => {
            $insertNodeToNearestRoot(new HarnessBlockNode('inserted'));
          });
          editor.focus();
        }
      };
      root.addEventListener('mousedown', onMouseDown);
      root.addEventListener('click', onClick);

      const teardown = mergeRegister(
        setup.primitives.atomicBlock.register(editor, isBlock, SELECTED_CLASS),
        // Registered after the helper, at the same priority: it only sees clicks the
        // helper declined.
        editor.registerCommand(
          CLICK_COMMAND,
          () => {
            const hits = Number(root.getAttribute('data-harness-clicks') ?? '0');
            root.setAttribute('data-harness-clicks', String(hits + 1));
            return false;
          },
          COMMAND_PRIORITY_LOW,
        ),
        editor.registerUpdateListener(mirror),
        () => root.removeEventListener('mousedown', onMouseDown),
        () => root.removeEventListener('click', onClick),
      );
      mirror();
      return teardown;
    },
  };
}
