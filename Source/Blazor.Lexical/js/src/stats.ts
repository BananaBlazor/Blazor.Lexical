// ---------------------------------------------------------------------------
// Document statistics: word/character counts, paragraph count, reading time.
//
// The smallest of the built-in extensions and the same dual surface as the TOC:
// with a `targetSelector` it writes a formatted line into a host-supplied element
// entirely client-side (zero interop), and with an `OnStatsChanged` delegate on the
// C# side it additionally pushes the numbers to .NET. No nodes, no commands, and
// the editor state is only ever read.
//
// Like the TOC it runs on its own debounced timer rather than riding the core
// content channel (see docs/extensions.md, "Adding a push channel"), and skips the
// tick entirely when the computed tuple is unchanged.
// ---------------------------------------------------------------------------

import { $getRoot, type LexicalEditor } from 'lexical';
import type { LexicalExtensionModule, LexicalExtensionSetup } from './extension';

/** How long to coalesce document changes before recomputing. */
const STATS_DEBOUNCE_MS = 150;

/** The stats extension's options payload, mirrored from C# `StatsExtensionOptionsDto`. */
export interface StatsOptionsDto {
  /** CSS selector of the element the formatted line is written into; null ⇒ none. */
  targetSelector?: string | null;
  /** Template for that line; `{words}`, `{characters}`, … are substituted. */
  template?: string | null;
  /** Reading speed used for `readingMinutes`. */
  wordsPerMinute: number;
}

/** The computed numbers, mirrored to the C# `LexicalDocumentStats`. */
interface DocumentStatsDto {
  characters: number;
  charactersNoSpaces: number;
  words: number;
  paragraphs: number;
  readingMinutes: number;
}

/** Computes the statistics for the editor's current document. */
function computeStats(editor: LexicalEditor, wordsPerMinute: number): DocumentStatsDto {
  return editor.getEditorState().read(() => {
    const root = $getRoot();
    const text = root.getTextContent();
    // Unicode-aware: split on any whitespace run, then drop the empty edges an empty
    // or whitespace-only document produces.
    const words = text.split(/\s+/u).filter((word) => word.length > 0).length;
    const paragraphs = root
      .getChildren()
      .filter((child) => child.getTextContent().trim().length > 0).length;
    return {
      characters: text.length,
      charactersNoSpaces: text.replace(/\s/gu, '').length,
      words,
      paragraphs,
      readingMinutes: wordsPerMinute > 0 ? Math.ceil(words / wordsPerMinute) : 0,
    };
  });
}

/** Substitutes the `{token}` placeholders in `template` with the computed numbers. */
function format(template: string, stats: DocumentStatsDto): string {
  return template.replace(/\{(\w+)\}/g, (match, token: string) =>
    token in stats ? String(stats[token as keyof DocumentStatsDto]) : match,
  );
}

/**
 * The document-statistics feature as a `LexicalExtensionFactory` — register-only,
 * read-only, and silent unless the host asked for either surface.
 */
export default function statsExtension(setup: LexicalExtensionSetup): LexicalExtensionModule {
  const raw = setup.options as Partial<StatsOptionsDto> | undefined;
  const targetSelector = raw?.targetSelector ?? null;
  const template = raw?.template ?? '{words} words';
  const wordsPerMinute = raw?.wordsPerMinute ?? 200;

  let current: DocumentStatsDto = {
    characters: 0,
    charactersNoSpaces: 0,
    words: 0,
    paragraphs: 0,
    readingMinutes: 0,
  };
  let signature = '';

  return {
    register: ({ editor }) => {
      let debounce: ReturnType<typeof setTimeout> | undefined;

      const rebuild = (): void => {
        const stats = computeStats(editor, wordsPerMinute);
        const next = JSON.stringify(stats);
        if (next === signature) {
          return;
        }
        signature = next;
        current = stats;
        if (targetSelector !== null) {
          // Re-queried each tick: the host may have re-rendered the target.
          const targetEl = document.querySelector<HTMLElement>(targetSelector);
          if (targetEl !== null) {
            targetEl.textContent = format(template, stats);
          }
        }
        if (setup.canInvokeDotNet) {
          setup.notifyDotNet('stats', next);
        }
      };

      const unregister = editor.registerUpdateListener(() => {
        if (debounce !== undefined) {
          clearTimeout(debounce);
        }
        debounce = setTimeout(rebuild, STATS_DEBOUNCE_MS);
      });
      rebuild();

      return () => {
        unregister();
        if (debounce !== undefined) {
          clearTimeout(debounce);
        }
      };
    },
    invoke: (method) => (method === 'get' ? current : undefined),
  };
}
