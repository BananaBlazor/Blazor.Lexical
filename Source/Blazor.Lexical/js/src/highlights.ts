// ---------------------------------------------------------------------------
// Highlights: transient, app-driven decoration of text found BY ITS CONTENT —
// the anchor half of "an AI left a comment on this sentence".
//
// Marks (marks.ts) and highlights answer two different questions. A mark is a
// NODE: the app already knows which span it means, the id lives in the document,
// it survives a round trip through JSON. A highlight starts from text the app can
// only describe — a quote, possibly with the words either side of it, exactly the
// W3C TextQuoteSelector shape — and paints it without touching the document at
// all.
//
// That "without touching the document" is the whole design. The painting is the
// CSS Custom Highlight API: `CSS.highlights.set(name, new Highlight(...ranges))`
// styles live DOM Ranges from the stylesheet, so there is
//   * no node, so no serialization surface and no schema change;
//   * no `editor.update()`, so no undo step, no dirty document, and no need for
//     the silent-update tag;
//   * no DOM mutation, so nothing for Lexical's reconciler to collide with.
// It is also the only mechanism that spans block boundaries and survives a click,
// which a native selection does not — and surviving a click is the requirement,
// since the point is to leave suggestions visible while the user works.
//
// Ranges DO go stale: Lexical re-creates DOM nodes on reconciliation. So a
// highlight stores its QUERY, not its ranges, and re-resolves against the live
// DOM after every update (debounced). A quote that the user edits away simply
// stops matching and its highlight disappears; undo brings it back.
//
// Registration is document-global (CSS.highlights is keyed by name, not by
// element), so the registry below unions the ranges every editor instance has
// contributed under a given name. Two editors on one page highlighting the same
// id therefore both paint, and disposing one does not blank the other.
//
// Bundling: no Lexical imports beyond types and nothing from @lexical/*, so this
// module is a few hundred bytes on the core bundle — index.ts imports it
// statically, like marks and mentions.
// ---------------------------------------------------------------------------

import type { LexicalEditor } from 'lexical';
import type { LexicalExtensionModule, LexicalExtensionSetup } from './extension';
import {
  AnchorResult,
  flatten,
  normalize,
  occurrences,
  resolve,
  toRange,
  type Quote,
} from './text-quote';

/** How long to coalesce editor updates before re-resolving every highlight. */
const REPAINT_DEBOUNCE_MS = 50;

/** The `::highlight()` name for a given app-facing highlight id. */
const CSS_NAME_PREFIX = 'blazor-lexical-';

/** The CSS Custom Highlight API, or null where the browser does not have it. */
interface HighlightApi {
  create: (ranges: Range[]) => object;
  registry: {
    set: (name: string, highlight: object) => void;
    delete: (name: string) => void;
  };
}

function getHighlightApi(): HighlightApi | null {
  const global = globalThis as unknown as {
    Highlight?: new (...ranges: Range[]) => object;
    CSS?: { highlights?: HighlightApi['registry'] };
  };
  const ctor = global.Highlight;
  const registry = global.CSS?.highlights;
  if (typeof ctor !== 'function' || registry === undefined) {
    return null;
  }
  return { create: (ranges) => new ctor(...ranges), registry };
}

// --- The document-global registry -----------------------------------------
//
// CSS.highlights is per-document, so contributions are pooled by name across
// every editor instance and re-published whenever any of them changes.

/** name → (contributor token → its ranges). */
const contributions = new Map<string, Map<object, Range[]>>();

/** Re-publishes `name` from every contributor, unregistering it when empty. */
function publish(name: string): void {
  const api = getHighlightApi();
  if (api === null) {
    return;
  }
  const ranges: Range[] = [];
  for (const contributed of contributions.get(name)?.values() ?? []) {
    ranges.push(...contributed);
  }
  if (ranges.length === 0) {
    api.registry.delete(name);
    return;
  }
  api.registry.set(name, api.create(ranges));
}

/** Records (or, with an empty list, withdraws) one contributor's ranges. */
function contribute(name: string, token: object, ranges: Range[]): void {
  let byToken = contributions.get(name);
  if (byToken === undefined) {
    if (ranges.length === 0) {
      return;
    }
    byToken = new Map();
    contributions.set(name, byToken);
  }
  if (ranges.length === 0) {
    byToken.delete(token);
    if (byToken.size === 0) {
      contributions.delete(name);
    }
  } else {
    byToken.set(token, ranges);
  }
  publish(name);
}

// --- The extension ---------------------------------------------------------

/** What a highlight id is currently asking for, kept so it can be re-resolved. */
type Query =
  | { kind: 'quote'; quote: Quote }
  | { kind: 'all'; text: string };

/**
 * The highlights feature as a `LexicalExtensionFactory`. It contributes no nodes and
 * no theme — there is nothing in the document to theme — and performs no JS→.NET
 * calls at all: every entry point is the host calling in over `invoke`.
 */
export default function highlightsExtension(
  _setup: LexicalExtensionSetup,
): LexicalExtensionModule {
  let editorRef: LexicalEditor | null = null;
  let contentRef: HTMLElement | null = null;
  // Identifies this editor's contributions in the document-global registry.
  const token = {};
  // highlight id → the query that produced it. Queries, not ranges: ranges go
  // stale every time Lexical reconciles, queries do not.
  const queries = new Map<string, Query>();

  const cssName = (id: string): string => CSS_NAME_PREFIX + id;

  /** Re-resolves one id against the live DOM and republishes it. */
  const repaint = (id: string): Range[] => {
    const content = contentRef;
    const query = queries.get(id);
    if (content === null || query === undefined) {
      return [];
    }
    const flat = flatten(content);
    const ranges: Range[] = [];
    if (query.kind === 'quote') {
      const { range } = resolve(flat, query.quote);
      if (range !== null) {
        ranges.push(range);
      }
    } else {
      const needle = normalize(query.text);
      for (const start of occurrences(flat.text, needle)) {
        const range = toRange(flat, start, needle.length);
        if (range !== null) {
          ranges.push(range);
        }
      }
    }
    contribute(cssName(id), token, ranges);
    return ranges;
  };

  const repaintAll = (): void => {
    for (const id of queries.keys()) {
      repaint(id);
    }
  };

  /** Drops one id (or, with null, all of them) from the registry. */
  const clear = (id: string | null): void => {
    for (const existing of [...queries.keys()]) {
      if (id === null || existing === id) {
        queries.delete(existing);
        contribute(cssName(existing), token, []);
      }
    }
  };

  const scrollTo = (range: Range | null): void => {
    // Text nodes have no layout box of their own, so scroll the element holding it.
    const target = range?.startContainer.parentElement;
    target?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  };

  return {
    name: 'blazor-lexical/highlights',
    register: ({ editor, content }) => {
      editorRef = editor;
      contentRef = content;
      let debounce: ReturnType<typeof setTimeout> | undefined;

      const unregister = editor.registerUpdateListener(() => {
        // Reconciliation replaces DOM nodes, so every stored Range is now pointing
        // at detached text. Re-resolve from the queries instead of trying to
        // salvage them — that is also what lets a highlight follow its quote as the
        // text around it is edited.
        if (queries.size === 0) {
          return;
        }
        if (debounce !== undefined) {
          clearTimeout(debounce);
        }
        debounce = setTimeout(repaintAll, REPAINT_DEBOUNCE_MS);
      });

      return () => {
        if (debounce !== undefined) {
          clearTimeout(debounce);
        }
        unregister();
        clear(null);
        editorRef = null;
        contentRef = null;
      };
    },
    invoke: (method, args) => {
      if (editorRef === null || contentRef === null) {
        return undefined;
      }
      switch (method) {
        case 'highlight': {
          const request = (args[0] ?? {}) as Quote & { id?: string; scroll?: boolean };
          const id = String(request.id ?? '');
          queries.set(id, { kind: 'quote', quote: request });
          const ranges = repaint(id);
          if (ranges.length === 0) {
            queries.delete(id);
            return AnchorResult.NotFound;
          }
          if (request.scroll === true) {
            scrollTo(ranges[0] ?? null);
          }
          // Re-resolve rather than trust `repaint`'s ranges for the verdict: the
          // ambiguity signal is the caller's cue that this anchor may drift.
          return resolve(flatten(contentRef), request).result;
        }

        case 'highlightAll': {
          const request = (args[0] ?? {}) as { id?: string; exact?: string };
          const id = String(request.id ?? '');
          queries.set(id, { kind: 'all', text: String(request.exact ?? '') });
          const count = repaint(id).length;
          if (count === 0) {
            queries.delete(id);
          }
          return count;
        }

        case 'clear': {
          const id = String(args[0] ?? '');
          clear(id.length > 0 ? id : null);
          return true;
        }

        case 'scrollTo': {
          const id = String(args[0] ?? '');
          const ranges = repaint(id);
          if (ranges.length === 0) {
            return false;
          }
          scrollTo(ranges[0] ?? null);
          return true;
        }

        default:
          return undefined;
      }
    },
  };
}
