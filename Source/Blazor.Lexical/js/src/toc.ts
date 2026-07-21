// ---------------------------------------------------------------------------
// Table of contents: a live heading outline of the document.
//
// The editor already holds the heading DOM, so it is far better placed than the
// host to build an outline: this module watches the document, derives a slug per
// heading, stamps it onto the heading's DOM element as an `id`, and (optionally)
// renders a nested <ol> into a host-supplied element outside the editor.
//
// Two things make it cheap and safe:
//
//   * The editor state is NEVER mutated. Anchors are set with setAttribute on the
//     element Lexical already rendered, outside editor.update() — no dirty node, no
//     undo step, no serialization impact. A document authored with this extension
//     serializes byte-identically to one authored without it. The accepted cost is
//     that anchors are content-derived: renaming a heading changes its slug and
//     invalidates saved '#fragment' links.
//   * A signature gate (level|slug|text joined over every heading) short-circuits the
//     tick when nothing outline-relevant changed, so ordinary typing inside a
//     paragraph costs one string compare and nothing else.
//
// Like mentions, this is the built-in bundling tier of the ordinary extension
// contract: index.ts imports it statically (it needs only `lexical` and
// `@lexical/rich-text`, both already in core, so it adds well under a kilobyte and
// buys no extra fetch) and the descriptor loop treats it exactly like a consumer
// extension. Pushing the model to .NET is opt-in — with no OnTocChanged delegate the
// C# half reports no invoke handler and this module never calls back.
// ---------------------------------------------------------------------------

import { $getNodeByKey, $getRoot, type LexicalEditor } from 'lexical';
import { $isHeadingNode } from '@lexical/rich-text';
import type { LexicalExtensionModule, LexicalExtensionSetup } from './extension';

/** How long to coalesce document changes before rebuilding the outline. */
const TOC_DEBOUNCE_MS = 150;

/** The TOC extension's options payload, mirrored from C# `TocExtensionOptionsDto`. */
export interface TocOptionsDto {
  /** CSS selector of the element the outline is rendered into; null ⇒ no rendering. */
  targetSelector?: string | null;
  /** Shallowest heading level to include (1 = h1). */
  minLevel: number;
  /** Deepest heading level to include (3 = h3). */
  maxLevel: number;
  /** Prepended to every slug, so two editors on one page can't collide. */
  anchorPrefix?: string | null;
  /** Highlight the item for the heading currently at the top of the scroll container. */
  scrollSpy: boolean;
  /** Use smooth scrolling when an item is clicked. */
  smoothScroll: boolean;
  /** Place the caret in the heading when an item is clicked. */
  focusOnClick: boolean;
}

/** One outline entry, mirrored to the C# `LexicalTocEntry`. */
interface TocEntryDto {
  anchorId: string;
  level: number;
  text: string;
  children: TocEntryDto[];
}

/** A heading as collected from the document, before nesting. */
interface FlatHeading {
  /** The heading node's key — used to reach its DOM element. */
  key: string;
  /** 1..6. */
  level: number;
  text: string;
  slug: string;
}

/**
 * Slugifies heading text the way a fragment link wants it: lowercase, every run of
 * non-alphanumerics collapsed to a single dash, dashes trimmed. Empty text (a blank
 * heading) becomes 'section' so every heading always has an anchor.
 */
function slugify(text: string): string {
  const slug = text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return slug.length > 0 ? slug : 'section';
}

/** Reads the in-range top-level headings, assigning deduped slugs. */
function collectHeadings(editor: LexicalEditor, options: TocOptionsDto): FlatHeading[] {
  const prefix = options.anchorPrefix ?? '';
  const used = new Map<string, number>();
  return editor.getEditorState().read(() => {
    const headings: FlatHeading[] = [];
    for (const child of $getRoot().getChildren()) {
      if (!$isHeadingNode(child)) {
        continue;
      }
      const level = Number(child.getTag().slice(1));
      if (level < options.minLevel || level > options.maxLevel) {
        continue;
      }
      const text = child.getTextContent();
      // Dedupe within the document: the second "Overview" becomes overview-2.
      const base = `${prefix}${slugify(text)}`;
      const seen = (used.get(base) ?? 0) + 1;
      used.set(base, seen);
      headings.push({
        key: child.getKey(),
        level,
        text,
        slug: seen === 1 ? base : `${base}-${seen}`,
      });
    }
    return headings;
  });
}

/**
 * Folds the flat heading list into a tree by level: a deeper heading nests under the
 * last shallower one. A level jump (h1 → h3) is tolerated rather than normalized —
 * the outline mirrors the document, warts and all.
 */
function nest(headings: FlatHeading[]): TocEntryDto[] {
  const roots: TocEntryDto[] = [];
  const stack: Array<{ level: number; entry: TocEntryDto }> = [];
  for (const heading of headings) {
    const entry: TocEntryDto = {
      anchorId: heading.slug,
      level: heading.level,
      text: heading.text,
      children: [],
    };
    while (stack.length > 0 && stack[stack.length - 1].level >= heading.level) {
      stack.pop();
    }
    if (stack.length === 0) {
      roots.push(entry);
    } else {
      stack[stack.length - 1].entry.children.push(entry);
    }
    stack.push({ level: heading.level, entry });
  }
  return roots;
}

/** The nearest ancestor of `el` that scrolls vertically, or the document element. */
function scrollParent(el: HTMLElement): HTMLElement {
  for (let node = el.parentElement; node !== null; node = node.parentElement) {
    const overflowY = getComputedStyle(node).overflowY;
    if ((overflowY === 'auto' || overflowY === 'scroll') && node.scrollHeight > node.clientHeight) {
      return node;
    }
  }
  return document.documentElement;
}

/**
 * Wires the outline runtime: a debounced update listener that collects, slugs,
 * stamps, renders, and (when opted in) pushes. Returns a teardown that clears the
 * timer, the scroll listener, the rendered target, and every stamped id.
 */
export function registerToc(
  editor: LexicalEditor,
  contentEl: HTMLElement,
  options: TocOptionsDto,
  setup: LexicalExtensionSetup,
): { teardown: () => void; getEntries: () => TocEntryDto[]; scrollTo: (anchorId: string) => boolean } {
  let headings: FlatHeading[] = [];
  let entries: TocEntryDto[] = [];
  let signature = '';
  let stampedKeys: string[] = [];
  let debounce: ReturnType<typeof setTimeout> | undefined;
  let spyFrame = 0;

  /** The element the outline is rendered into, re-queried every render. */
  const target = (): HTMLElement | null =>
    options.targetSelector ? document.querySelector<HTMLElement>(options.targetSelector) : null;

  /** Scrolls the heading carrying `anchorId` into view; false when it isn't there. */
  const scrollTo = (anchorId: string): boolean => {
    const heading = headings.find((h) => h.slug === anchorId);
    const el = heading === undefined ? null : editor.getElementByKey(heading.key);
    if (el === null) {
      return false;
    }
    el.scrollIntoView({ behavior: options.smoothScroll ? 'smooth' : 'auto', block: 'start' });
    if (options.focusOnClick && heading !== undefined) {
      editor.update(() => {
        const node = $getNodeByKey(heading.key);
        node?.selectEnd();
      });
    }
    return true;
  };

  const onItemClick = (e: Event): void => {
    const link = (e.target as HTMLElement).closest<HTMLElement>('[data-lexical-toc-item]');
    if (link === null) {
      return;
    }
    e.preventDefault();
    scrollTo(link.getAttribute('data-lexical-toc-anchor') ?? '');
  };

  /** Rebuilds the target element's nested list from `entries`. */
  const render = (): void => {
    const targetEl = target();
    if (targetEl === null) {
      return;
    }
    const build = (items: TocEntryDto[]): HTMLElement => {
      const list = document.createElement('ol');
      list.className = 'blazor-lexical__toc';
      for (const item of items) {
        const li = document.createElement('li');
        const link = document.createElement('a');
        link.href = `#${item.anchorId}`;
        link.textContent = item.text;
        link.setAttribute('data-lexical-toc-item', '');
        link.setAttribute('data-lexical-toc-anchor', item.anchorId);
        link.setAttribute('data-lexical-toc-level', String(item.level));
        li.appendChild(link);
        if (item.children.length > 0) {
          li.appendChild(build(item.children));
        }
        list.appendChild(li);
      }
      return list;
    };
    // The host may have re-rendered the target, so the listener is (re)attached here
    // rather than once at registration.
    targetEl.removeEventListener('click', onItemClick);
    targetEl.replaceChildren(build(entries));
    targetEl.addEventListener('click', onItemClick);
  };

  /** Stamps `id` + the marker onto each heading element, clearing the ones that left. */
  const stamp = (): void => {
    const keys = new Set(headings.map((h) => h.key));
    for (const key of stampedKeys) {
      if (!keys.has(key)) {
        const el = editor.getElementByKey(key);
        el?.removeAttribute('id');
        el?.removeAttribute('data-lexical-toc-anchor');
      }
    }
    for (const heading of headings) {
      const el = editor.getElementByKey(heading.key);
      if (el !== null) {
        el.setAttribute('id', heading.slug);
        el.setAttribute('data-lexical-toc-anchor', heading.slug);
      }
    }
    stampedKeys = [...keys];
  };

  const rebuild = (): void => {
    const collected = collectHeadings(editor, options);
    const next = collected.map((h) => `${h.level}|${h.slug}|${h.text}`).join('\n');
    if (next === signature) {
      return;
    }
    signature = next;
    headings = collected;
    entries = nest(collected);
    stamp();
    render();
    if (setup.canInvokeDotNet) {
      setup.notifyDotNet('toc', JSON.stringify(entries));
    }
  };

  const unregisterUpdate = editor.registerUpdateListener(() => {
    if (debounce !== undefined) {
      clearTimeout(debounce);
    }
    debounce = setTimeout(rebuild, TOC_DEBOUNCE_MS);
  });

  // Scrollspy: mark the last heading whose top has passed the container's top edge.
  const container = options.scrollSpy && options.targetSelector ? scrollParent(contentEl) : null;
  const onScroll = (): void => {
    if (spyFrame !== 0) {
      return;
    }
    spyFrame = requestAnimationFrame(() => {
      spyFrame = 0;
      const targetEl = target();
      if (targetEl === null || container === null) {
        return;
      }
      const edge =
        container === document.documentElement ? 0 : container.getBoundingClientRect().top;
      let activeId: string | null = null;
      for (const heading of headings) {
        const el = editor.getElementByKey(heading.key);
        if (el !== null && el.getBoundingClientRect().top <= edge + 1) {
          activeId = heading.slug;
        }
      }
      targetEl.querySelectorAll<HTMLElement>('[data-lexical-toc-item]').forEach((link) => {
        link.toggleAttribute(
          'data-lexical-toc-active',
          link.getAttribute('data-lexical-toc-anchor') === activeId,
        );
      });
    });
  };
  const scrollSource: EventTarget | null =
    container === null ? null : container === document.documentElement ? window : container;
  scrollSource?.addEventListener('scroll', onScroll, { passive: true });

  // First pass now: initial content is already loaded by the time register() runs.
  rebuild();

  return {
    getEntries: () => entries,
    scrollTo,
    teardown: () => {
      unregisterUpdate();
      if (debounce !== undefined) {
        clearTimeout(debounce);
      }
      if (spyFrame !== 0) {
        cancelAnimationFrame(spyFrame);
      }
      scrollSource?.removeEventListener('scroll', onScroll);
      for (const key of stampedKeys) {
        const el = editor.getElementByKey(key);
        el?.removeAttribute('id');
        el?.removeAttribute('data-lexical-toc-anchor');
      }
      const targetEl = target();
      if (targetEl !== null) {
        targetEl.removeEventListener('click', onItemClick);
        targetEl.replaceChildren();
      }
    },
  };
}

/**
 * The table-of-contents feature as a `LexicalExtensionFactory` — register-only, no
 * custom nodes, no state mutation. See the module header for why the anchors are
 * DOM-only and why the signature gate matters.
 */
export default function tocExtension(setup: LexicalExtensionSetup): LexicalExtensionModule {
  const raw = setup.options as Partial<TocOptionsDto> | undefined;
  const options: TocOptionsDto = {
    targetSelector: raw?.targetSelector ?? null,
    minLevel: raw?.minLevel ?? 1,
    maxLevel: raw?.maxLevel ?? 3,
    anchorPrefix: raw?.anchorPrefix ?? null,
    scrollSpy: raw?.scrollSpy ?? true,
    smoothScroll: raw?.smoothScroll ?? true,
    focusOnClick: raw?.focusOnClick ?? false,
  };

  let runtime: ReturnType<typeof registerToc> | null = null;

  return {
    register: ({ editor, content }) => {
      runtime = registerToc(editor, content, options, setup);
      return () => {
        runtime?.teardown();
        runtime = null;
      };
    },
    invoke: (method, args) => {
      switch (method) {
        case 'get':
          return runtime?.getEntries() ?? [];
        case 'scrollTo':
          return runtime?.scrollTo(String(args[0] ?? '')) ?? false;
        default:
          return undefined;
      }
    },
  };
}
