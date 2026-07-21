// Consumer extension used only by the harness's "extension hardening" section.
//
// One module, several deliberately-colliding personalities selected by `variant`.
// Each records the fact that it actually loaded by appending its variant name to
// `data-harness-loaded` on the editor root, so a test can assert which of a
// colliding pair the host accepted and which it skipped.

/**
 * Builds a TextNode subclass claiming `type`. Two different classes claiming the
 * same type is the collision the host has to catch before createEditor throws.
 */
function makeNode(lexical, type) {
  const { TextNode } = lexical;
  return class ColliderNode extends TextNode {
    static getType() {
      return type;
    }
    static clone(node) {
      return new ColliderNode(node.__text, node.__key);
    }
    static importJSON(json) {
      return new ColliderNode(json.text).updateFromJSON(json);
    }
  };
}

const VARIANTS = {
  // Two modules claiming the same name: the second must be skipped.
  'name-a': (lexical) => ({ name: 'harness/duplicate' }),
  'name-b': (lexical) => ({ name: 'harness/duplicate' }),

  // Two different node classes claiming the same getType(): the second must be
  // skipped, because letting both through throws inside createEditor.
  'node-a': (lexical) => ({
    name: 'harness/node-a',
    nodes: [makeNode(lexical, 'harness-collide')],
  }),
  'node-b': (lexical) => ({
    name: 'harness/node-b',
    // A thunk, which also exercises the `nodes` form Lexical's own extensions use.
    nodes: () => [makeNode(lexical, 'harness-collide')],
  }),

  // A declared conflict: the first names the second, so the second is skipped.
  'conflict-a': (lexical) => ({
    name: 'harness/conflict-a',
    conflictsWith: ['harness/conflict-b'],
  }),
  'conflict-b': (lexical) => ({ name: 'harness/conflict-b' }),
};

export default function colliderExtension(setup) {
  const variant = setup.options?.variant ?? 'name-a';
  const build = VARIANTS[variant];
  if (build === undefined) {
    throw new Error(`harness-collider: unknown variant '${variant}'`);
  }

  return {
    ...build(setup.lexical),
    register(ctx) {
      const previous = ctx.root.getAttribute('data-harness-loaded');
      ctx.root.setAttribute(
        'data-harness-loaded',
        previous === null || previous === '' ? variant : `${previous} ${variant}`,
      );
      return () => ctx.root.removeAttribute('data-harness-loaded');
    },
  };
}
