// ---------------------------------------------------------------------------
// Entity commit: the buffer → resolve → commit → create-if-missing state machine
// behind a "typed reference field" — an editable region whose whole content is
// conceptually a reference to an entity (a city, a tag, a person) rather than
// free text.
//
// This is the piece the built-in mentions (mentions.ts) does NOT generalize:
// mentions inserts a token for an entity that already exists, whereas here the
// field IS the reference and may name something that has to be created. The logic
// that is easy to get subtly wrong per-app lives here so every consumer gets it
// right — prefix matching, alternates ordering, and above all the optimistic
// create: fire the commit immediately with a provisional id so typing never
// stalls on a round trip, then swap in the real entity when it resolves.
//
// Deliberately DOM-free and editor-free — pure logic over a candidate list. It
// takes no `editor`, touches no DOM, and performs no interop. Wiring it to a
// field (reading the text as the query, inserting on commit) is the extension's
// job; keeping it pure is what lets the same primitive drive a field, a chip, or
// a test harness unchanged. No imports beyond types ⇒ a few hundred bytes on the
// core bundle, imported statically by index.ts like mentions.
// ---------------------------------------------------------------------------

import type {
  EntityCandidate,
  EntityCommitApi,
  EntityCommitController,
  EntityCommitOptions,
  EntityCommitState,
} from './extension';

/** Case-insensitive prefix test on `text` — the default {@link EntityCommitOptions.match}. */
function defaultMatch<T extends EntityCandidate>(query: string, candidate: T): boolean {
  return candidate.text.toLowerCase().startsWith(query.toLowerCase());
}

/**
 * Builds the shared {@link EntityCommitApi}. Stateless: each field's state lives in the
 * closure {@link EntityCommitApi.create} opens, so one instance is safely shared across
 * all extensions (see index.ts `primitives`).
 */
export function createEntityCommitApi(): EntityCommitApi {
  return {
    create<T extends EntityCandidate>(
      opts: EntityCommitOptions<T>,
    ): EntityCommitController<T> {
      const match = opts.match ?? defaultMatch;
      let query = '';
      // Which of the current matches is active. setQuery resets it; cycle advances it.
      let activeIndex = 0;
      // Monotonic provisional-id source, per field, so two optimistic creates never collide.
      let provisionalSeq = 0;
      let disposed = false;

      /** The matches for the current query, in candidate order (empty query ⇒ none). */
      const matchesFor = (): T[] =>
        query === '' ? [] : opts.candidates().filter((c) => match(query, c));

      const current = (): EntityCommitState<T> => {
        const matches = matchesFor();
        // best is the active match; the rest, in order, are the alternates cycle steps
        // through. `?? null` covers an activeIndex left past the end by a shrunk list.
        const best = matches[activeIndex] ?? null;
        const alternates = matches.filter((_, i) => i !== activeIndex);
        return { query, best, alternates };
      };

      const commit = (): void => {
        if (disposed) {
          return;
        }
        const matches = matchesFor();
        const best = matches[activeIndex] ?? null;
        if (best !== null) {
          // A real, existing entity — the common path.
          opts.onCommit(best, { created: false, provisional: false });
          return;
        }
        // No match. With nothing to create, an empty or unmatched commit is a no-op.
        if (query === '' || opts.createIfMissing === undefined) {
          return;
        }
        // Create-if-missing, optimistically: hand back a provisional entity NOW so the
        // interactive path never awaits, then resolve the real one in the background.
        const provisionalId = `provisional:${++provisionalSeq}`;
        const createdQuery = query;
        const provisional = { id: provisionalId, text: createdQuery } as T;
        opts.onCommit(provisional, { created: true, provisional: true });
        opts
          .createIfMissing(createdQuery)
          .then((entity) => {
            // A resolution that lands after dispose is dropped — the field is gone.
            if (disposed) {
              return;
            }
            opts.onResolved?.(provisionalId, entity);
          })
          .catch((error: unknown) => {
            // Rejection: surface it, but LEAVE THE PROVISIONAL IN PLACE. A field the user
            // has moved on from, half-rolled-back, is worse than a provisional that never
            // resolved. Dropped after dispose, like the resolve path.
            if (disposed) {
              return;
            }
            opts.onError?.(createdQuery, error);
          });
      };

      return {
        setQuery(next: string): void {
          query = next;
          activeIndex = 0;
        },
        current,
        cycle(): void {
          const count = matchesFor().length;
          if (count > 0) {
            activeIndex = (activeIndex + 1) % count;
          }
        },
        commit,
        dispose(): void {
          disposed = true;
        },
      };
    },
  };
}
