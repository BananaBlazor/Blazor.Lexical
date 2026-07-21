// ---------------------------------------------------------------------------
// Markdown conversion: the @lexical/markdown transformers used to serialize the
// editor to a Markdown string and to parse a Markdown string back into nodes.
//
// This whole module is a lazily-loaded chunk: index.ts only ever `import()`s it
// (never statically), so esbuild code-splits @lexical/markdown (~24kb) — and the
// @lexical/code-core (~16kb) it pulls in for fenced code blocks — out of the core
// bundle. Markdown registers no nodes, so nothing here is needed at create() time;
// the chunk is fetched on demand the first time getMarkdown/setMarkdown is called.
// Editors that never convert to/from Markdown never download it.
// ---------------------------------------------------------------------------

import {
  $convertToMarkdownString,
  $convertFromMarkdownString,
  TRANSFORMERS,
} from '@lexical/markdown';
import { type LexicalEditor } from 'lexical';

/** Serializes the editor content to a Markdown string. */
export function toMarkdown(editor: LexicalEditor): string {
  return editor.getEditorState().read(() => $convertToMarkdownString(TRANSFORMERS));
}

/**
 * Replaces the editor content with nodes parsed from a Markdown string. `options`
 * is passed through to `editor.update` — create()'s initial-content step uses
 * `{ discrete: true }` so the document is committed before history is registered,
 * and a silent `setMarkdown` passes the silent + history-merge tags so an app-driven
 * load neither pushes the content channel nor lands on the undo stack.
 */
export function fromMarkdown(
  editor: LexicalEditor,
  markdown: string,
  options?: { discrete?: true; tag?: string[] },
): void {
  editor.update(() => {
    $convertFromMarkdownString(markdown, TRANSFORMERS);
  }, options);
}
