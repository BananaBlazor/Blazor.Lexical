// ---------------------------------------------------------------------------
// Lexical update tags shared across the bundle.
//
// This lives in its own module rather than in index.ts so both the producer
// (mentions.ts, and any extension via `setup.silentUpdateTag`) and the consumer
// (index.ts's update listener) can import the same constant without a cycle — and
// without a lazily-chunked feature dragging the core entry in behind it.
// ---------------------------------------------------------------------------

/**
 * Marks an update as app-driven rather than user-driven. The content-changed push in
 * index.ts skips any update carrying it, so a silent touch-up — re-resolving a stale
 * mention name on load, say — never marks the document dirty.
 *
 * Pair it with {@link HISTORY_MERGE_TAG} so such an edit adds no undo step either.
 * Reserve it for updates the user did not make: a silent *user* edit would be lost.
 */
export const SILENT_UPDATE_TAG = 'blazor-lexical-silent';

/**
 * Lexical's own tag: merge this update into the previous history entry rather than
 * pushing a new undoable step.
 */
export const HISTORY_MERGE_TAG = 'history-merge';
