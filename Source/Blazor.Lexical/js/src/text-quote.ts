// ---------------------------------------------------------------------------
// TextQuoteSelector resolution: finding a span of text described by its CONTENT
// (a quote, optionally with the words either side) in the live editor DOM, and
// turning the match into a DOM Range.
//
// This is the anchor engine shared by two features that both start from "words,
// not a position": highlights.ts paints the resolved Range with the CSS Custom
// Highlight API, and marks.ts (wrapQuote) turns it into a real MarkNode to hydrate
// a stored annotation. It lives in its own module so neither owns the other — the
// matching, the whitespace normalization, and the W3C prefix/suffix disambiguation
// all have one home.
//
// The design notes that used to sit in highlights.ts apply here: matching
// normalizes whitespace on BOTH sides (a prose quote matches a DOM carrying
// indentation, inter-block newlines, and words split by a bold run or a mark), and
// prefix/suffix DISAMBIGUATE rather than gate (the best-scoring occurrence wins even
// at zero, so an anchor survives edits to the words around it).
// ---------------------------------------------------------------------------

/**
 * Elements after which a boundary counts as whitespace when flattening the editor
 * to text, so a quote never accidentally matches across a paragraph break by having
 * its last and first words run together ("…the end.The next…").
 */
const BLOCK_TAGS = new Set([
  'ADDRESS', 'ARTICLE', 'ASIDE', 'BLOCKQUOTE', 'BR', 'DD', 'DIV', 'DL', 'DT',
  'FIGCAPTION', 'FIGURE', 'FOOTER', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'HEADER',
  'HR', 'LI', 'MAIN', 'NAV', 'OL', 'P', 'PRE', 'SECTION', 'TABLE', 'TD', 'TH',
  'TR', 'UL',
]);

/** How an anchor resolved. Mirrors the C# `LexicalTextAnchorResult`. */
export enum AnchorResult {
  NotFound = 0,
  Matched = 1,
  MatchedAmbiguously = 2,
}

/** Where one character of the flattened text came from. */
interface CharSource {
  node: Text;
  offset: number;
}

/** The editor's visible text, whitespace-normalized, with a map back to the DOM. */
export interface FlatText {
  text: string;
  sources: CharSource[];
}

/** A W3C-style text-quote anchor. Mirrors the C# `LexicalTextQuote`. */
export interface Quote {
  exact: string;
  prefix?: string | null;
  suffix?: string | null;
}

export interface Resolved {
  result: AnchorResult;
  range: Range | null;
}

/** Collapses whitespace runs the same way {@link flatten} does. */
export function normalize(value: string): string {
  return value.replace(/\s+/g, ' ').trim();
}

/**
 * Flattens `root` to a single string in which every run of whitespace — inside a
 * text node, or implied by a block boundary — is one space, and leading/trailing
 * whitespace is gone. `sources[i]` is where `text[i]` lives in the DOM, which is
 * what lets a match become a Range.
 *
 * Normalizing matters more than it sounds: the quote an AI reviewer hands back is
 * prose ("the quick brown fox"), while the DOM it has to match holds indentation,
 * newlines between blocks, and a word split across two text nodes by a mark or a
 * bold run. Matching raw text content would fail on all three.
 */
export function flatten(root: HTMLElement): FlatText {
  const chars: string[] = [];
  const sources: CharSource[] = [];
  // A pending separator is emitted lazily, in front of the next real character —
  // which is how trailing whitespace and runs both collapse for free.
  let pendingSpace = false;

  const walk = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      const text = node as Text;
      const value = text.data;
      for (let i = 0; i < value.length; i++) {
        const ch = value[i];
        if (ch === undefined || /\s/.test(ch)) {
          pendingSpace = true;
          continue;
        }
        if (pendingSpace && chars.length > 0) {
          // The synthetic space maps to the character it precedes: it is only ever
          // read as a range boundary, and that boundary is the same either way.
          chars.push(' ');
          sources.push({ node: text, offset: i });
        }
        pendingSpace = false;
        chars.push(ch);
        sources.push({ node: text, offset: i });
      }
      return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) {
      return;
    }
    const element = node as Element;
    const isBlock = BLOCK_TAGS.has(element.tagName);
    if (isBlock) {
      pendingSpace = true;
    }
    // Indexed rather than for-of: NodeList is only iterable under lib dom.iterable.
    for (let i = 0; i < element.childNodes.length; i++) {
      walk(element.childNodes[i]!);
    }
    if (isBlock) {
      pendingSpace = true;
    }
  };

  walk(root);
  return { text: chars.join(''), sources };
}

/** Turns a `[start, start+length)` span of the flattened text into a live DOM Range. */
export function toRange(flat: FlatText, start: number, length: number): Range | null {
  const first = flat.sources[start];
  const last = flat.sources[start + length - 1];
  if (first === undefined || last === undefined) {
    return null;
  }
  const range = document.createRange();
  range.setStart(first.node, first.offset);
  range.setEnd(last.node, last.offset + 1);
  return range;
}

/** Every index at which `needle` occurs in `haystack` (non-overlapping). */
export function occurrences(haystack: string, needle: string): number[] {
  const found: number[] = [];
  if (needle.length === 0) {
    return found;
  }
  for (let at = haystack.indexOf(needle); at !== -1;) {
    found.push(at);
    at = haystack.indexOf(needle, at + needle.length);
  }
  return found;
}

/**
 * Finds the occurrence of `quote.exact` that `prefix`/`suffix` point at.
 *
 * Context is used to DISAMBIGUATE, not to gate: every occurrence is scored by how
 * much of the surrounding context it reproduces, and the best-scoring one wins
 * even when that score is zero. That is deliberate, and it is what the
 * TextQuoteSelector spec is for — an anchor has to survive the document being
 * edited around it, and demanding an exact prefix+suffix match would drop the
 * highlight the moment a neighbouring word changed. When the winning score is a
 * tie between several occurrences the caller is told (`MatchedAmbiguously`): the
 * first one is used, but the anchor is known to be weak.
 */
export function resolve(flat: FlatText, quote: Quote): Resolved {
  const exact = normalize(quote.exact);
  const at = occurrences(flat.text, exact);
  if (at.length === 0) {
    return { result: AnchorResult.NotFound, range: null };
  }

  const prefix = normalize(quote.prefix ?? '');
  const suffix = normalize(quote.suffix ?? '');
  let best = at[0]!;
  let bestScore = -1;
  let tied = false;
  for (const start of at) {
    let score = 0;
    if (prefix.length > 0 && flat.text.slice(0, start).trimEnd().endsWith(prefix)) {
      score++;
    }
    if (
      suffix.length > 0
      && flat.text.slice(start + exact.length).trimStart().startsWith(suffix)
    ) {
      score++;
    }
    if (score > bestScore) {
      bestScore = score;
      best = start;
      tied = false;
    } else if (score === bestScore) {
      tied = true;
    }
  }

  const range = toRange(flat, best, exact.length);
  if (range === null) {
    return { result: AnchorResult.NotFound, range: null };
  }
  return {
    result: tied ? AnchorResult.MatchedAmbiguously : AnchorResult.Matched,
    range,
  };
}
