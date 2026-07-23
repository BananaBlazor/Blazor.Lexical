// Consumer extension used only by the harness's "nested block drag" section.
//
// It installs a ctx.blockDrag policy that makes individual list items draggable and
// reparentable — the worked shape of the nested-drag seam — taking every Lexical binding
// from `setup`, never its own import. The policy is pure logic over live nodes: source
// resolves the nearest <li>, targets yields the sibling gaps inside its list plus the
// document's top-level gaps (so an item can also be dragged out of the list), and drop is
// left to the SDK's default node move.

/// <reference path="./_content/Blazor.Lexical/blazor-lexical-extension.d.ts" />

export default function nestedDragExtension(setup) {
  const { $getRoot } = setup.lexical;
  const { $findMatchingParent } = setup.utils;
  const isListItem = (node) => node.getType() === 'listitem';

  return {
    name: 'harness/nested-drag',

    register(ctx) {
      // A load marker so a test can assert the policy actually installed.
      ctx.root.setAttribute('data-nested-drag', 'on');
      return ctx.blockDrag.setPolicy({
        // The nearest list item is the draggable; anything else falls back to its
        // top-level block, so paragraphs still drag flat.
        source({ node }) {
          const li = isListItem(node) ? node : $findMatchingParent(node, isListItem);
          return li ?? node.getTopLevelElement();
        },

        // Gaps between the dragged item's siblings (reorder within the list), plus the
        // document's top-level gaps (drag the item out to the top level).
        targets(dragged) {
          const gaps = [];
          const list = dragged.getParent();
          if (list !== null) {
            for (let i = 0; i <= list.getChildrenSize(); i++) {
              gaps.push({ parent: list, index: i });
            }
          }
          const root = $getRoot();
          for (let i = 0; i <= root.getChildrenSize(); i++) {
            gaps.push({ parent: root, index: i });
          }
          return gaps;
        },

        // drop omitted — the SDK's default node move (reparent to parent/index) is correct.
      });
    },
  };
}
