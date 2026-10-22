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
//
// Mentions round-trip as ordinary `[text](url)` links when their config sets a
// `markdownUrlPrefix`: export writes a mention whose url starts with a registered
// prefix as a link, and import turns such a link back into a MentionNode of that
// config. The import direction stays core ← chunk (this module imports from
// mentions.ts, never the reverse), so the core bundle never pulls in @lexical/markdown.
// ---------------------------------------------------------------------------

import {
  $convertToMarkdownString,
  $convertFromMarkdownString,
  TRANSFORMERS,
  type TextMatchTransformer,
  type Transformer,
} from '@lexical/markdown';
import { type LexicalEditor } from 'lexical';
import {
  $createMentionNode,
  $isMentionNode,
  getMentionConfigs,
  MentionNode,
  type MentionConfigDto,
} from './mentions';

/** Escapes a string for literal use inside a RegExp. */
function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Backslash-escapes a mention label for a link's `[...]`: `\` and brackets so the label
 * parses back intact, and the inline-format characters `*_`~` so they stay literal.
 */
function escapeLabel(label: string): string {
  return label.replace(/([\\[\]*_`~])/g, '\\$1');
}

/** A private-use character no format tag or escape can contain. */
const PLACEHOLDER = '';

/** Reverses {@link escapeLabel} (and any other CommonMark punctuation escape). */
function unescapeLabel(label: string): string {
  return label.replace(/\\([!-/:-@[-`{-~])/g, '$1');
}

/**
 * A text-match transformer mapping mentions to `[text](url)` links, in both directions
 * only for urls that start with one of `configs`' prefixes — every other link and
 * mention falls through to the stock handling. An imported mention takes the current
 * config's id and colour, so stale per-session config ids never enter the document.
 */
function mentionTransformer(configs: MentionConfigDto[]): TextMatchTransformer {
  // Longest prefix first, so a more specific prefix wins over a shorter one.
  const byPrefix = configs
    .map((config) => ({ config, prefix: config.markdownUrlPrefix! }))
    .sort((a, b) => b.prefix.length - a.prefix.length);
  const prefixes = byPrefix.map((p) => escapeRegExp(p.prefix)).join('|');
  const url = `(?:${prefixes})[^()\\s]+`;
  const pattern = `\\[((?:\\\\.|[^\\[\\]\\\\])+)\\]\\((${url})\\)`;
  // Only urls the import pattern can read back export as links; others stay plain text.
  const exportableUrl = new RegExp(`^${url}$`);

  return {
    type: 'text-match',
    dependencies: [MentionNode],
    trigger: ')',
    importRegExp: new RegExp(pattern),
    regExp: new RegExp(`${pattern}$`),
    replace: (textNode, match) => {
      const [, label, href] = match;
      const entry = byPrefix.find((p) => href.startsWith(p.prefix))!;
      const mention = $createMentionNode(
        entry.config.id,
        entry.config.initiator,
        href.slice(entry.prefix.length),
        href,
        entry.config.color,
        unescapeLabel(label),
      );
      // Keep formatting a surrounding format transformer already applied (e.g. bold).
      mention.setFormat(textNode.getFormat());
      textNode.replace(mention);
    },
    export: (node, _exportChildren, exportFormat) => {
      const href = $isMentionNode(node) ? node.getLatest().__url : null;
      if (href === null || !exportableUrl.test(href)) {
        return null;
      }
      // exportFormat opens/closes the node's format tags in step with its siblings; a
      // placeholder stands in for the link so the label is not escaped a second time.
      const link = `[${escapeLabel(node.getTextContent())}](${href})`;
      // Inside a code span the link would be literal text, so code format is dropped.
      if ((node as MentionNode).hasFormat('code')) {
        return link;
      }
      return exportFormat(node as MentionNode, PLACEHOLDER).replace(PLACEHOLDER, () => link);
    },
  };
}

/**
 * The transformers for `editor`: the stock set, preceded by the mention transformer
 * when any of its mention configs declares a Markdown url prefix. First position
 * matters — on an equal start index the import loop keeps the first transformer that
 * matched, so a prefixed link becomes a mention rather than a LinkNode.
 */
function transformersFor(editor: LexicalEditor): Transformer[] {
  const configs = getMentionConfigs(editor).filter((c) => !!c.markdownUrlPrefix);
  return configs.length > 0 ? [mentionTransformer(configs), ...TRANSFORMERS] : TRANSFORMERS;
}

/** Serializes the editor content to a Markdown string. */
export function toMarkdown(editor: LexicalEditor): string {
  const transformers = transformersFor(editor);
  return editor.getEditorState().read(() => $convertToMarkdownString(transformers));
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
  const transformers = transformersFor(editor);
  editor.update(() => {
    $convertFromMarkdownString(markdown, transformers);
  }, options);
}
