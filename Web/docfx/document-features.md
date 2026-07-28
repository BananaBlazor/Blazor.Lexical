# Document features

Four components for the things a document editor needs beyond formatting: an outline,
highlights you own, a word count, and a place to hang per-block actions. Each is opt-in
child markup, and each follows the library's rule that **nothing calls into .NET unless
you wire a callback**.

## Table of contents

`<LexicalToc />` gives every heading an `id` derived from its text — so `#background`
links work — and produces a live outline of the document.

It surfaces that outline two ways, and they are independent:

```razor
<nav id="outline"></nav>

<LexicalEditor Theme="LexicalTheme.Default">
    <LexicalToolbar />
    <LexicalToc TargetSelector="#outline" MaxLevel="3" />
</LexicalEditor>
```

With just a `TargetSelector`, JS builds a nested `<ol class="blazor-lexical__toc">` inside
that element and handles clicks itself (scrolling the heading into view). Nothing crosses
into .NET.

Wire `OnTocChanged` instead — or as well — to receive the tree in C# and render it
yourself. `<LexicalTocList />` does that for you, emitting the same markup (and therefore
using the same stylesheet) as the JS renderer:

```razor
<LexicalTocList Entries="_outline" OnItemClick="Jump" />

<LexicalEditor>
    <LexicalToc @ref="_toc" OnTocChanged="e => _outline = e" />
</LexicalEditor>

@code {
    private LexicalToc? _toc;
    private IReadOnlyList<LexicalTocEntry> _outline = [];

    private Task Jump(LexicalTocEntry entry) => _toc!.ScrollToAnchorAsync(entry.AnchorId);
}
```

`LexicalTocList` takes *data*, not an editor, so it can live anywhere on the page — a
sidebar, a drawer, a different component entirely.

### What to know about the anchors

- **The document is never modified.** Anchors are written onto the rendered HTML, not into
  the editor state, so they add no undo step and a document authored with this extension
  saves byte-for-byte identically to one authored without it.
- **They follow the heading text.** Renaming "Background" to "Prior work" changes its
  anchor from `#background` to `#prior-work`, and any link saved against the old one stops
  resolving. If you need durable anchors, key them to something you own (a
  [mark](#marks), say) rather than to the heading.
- **Element ids are page-global.** Two editors on one page need distinct `AnchorPrefix`
  values, or their headings will collide with each other (and with your own markup).
- Duplicate headings within one document are deduped automatically (`#overview`,
  `#overview-2`).
- The `TargetSelector` element must exist when the editor is created — the extension
  resolves it once, and there is no rescan.

## Marks

`<LexicalMarks />` attaches **your own ids** to spans of text. That is the whole feature,
and it is the foundation for comments, annotations, suggestions and search highlighting:
the library never generates an id, never interprets one, and hands back exactly what you
passed in.

```razor
<LexicalEditor Theme="LexicalTheme.Default">
    <LexicalToolbar />
    <LexicalMarks @ref="_marks"
                  OnMarkClicked="ShowThread"
                  OnActiveMarksChanged="ids => _atCaret = ids" />
</LexicalEditor>

<button @onclick="AddComment">Comment on selection</button>

@code {
    private LexicalMarks? _marks;
    private IReadOnlyList<string> _atCaret = [];

    private async Task AddComment()
    {
        var thread = await _threads.CreateAsync();       // your id, your model
        await _marks!.WrapSelectionInMarkAsync(thread.Id);
    }

    private void ShowThread(IReadOnlyList<string> ids) => _open = ids;
}
```

The API, all of it driven from C#:

| Method | Does |
|---|---|
| `WrapSelectionInMarkAsync(id)` | Marks the current selection; `false` if there was none |
| `WrapTextQuoteInMarkAsync(id, quote)` | Marks a stored text quote with no live selection |
| `RemoveMarkAsync(id, silent)` | Unwraps the mark, returning how many nodes it cleared |
| `GetMarkIdsAsync()` | Every mark id in the document, in document order |
| `GetMarkIdsAtSelectionAsync()` | The ids covering the caret/selection |
| `SetActiveMarkAsync(id)` | Highlights one mark (`null` clears) — visual only |
| `ScrollToMarkAsync(id)` | Scrolls the first span carrying that id into view |

### Marks overlap

A marked span carries an id *array*, not a single id. Marking text that already sits
inside another mark splits the span and **merges** the id sets rather than replacing
anything — so a passage can belong to two comment threads at once. That is why every
method above is plural: `GetMarkIdsAtSelectionAsync` can legitimately return several ids,
and a span carrying more than one gets the `MarkOverlap` theme class in addition to `Mark`,
so you can make the overlap visible.

`SetActiveMarkAsync` is decoration only: it toggles a DOM attribute and touches neither the
document nor the undo stack. `RemoveMarkAsync(id, silent: true)` is for cleanup *your app*
performs (a resolved thread, a cleared search) — it adds no undo step and does not raise
`OnContentChanged`, so the user is never asked to undo something they did not do.

### Read-only review, and re-hydrating annotations

Marks work in a **read-only** editor (`ReadOnly="true"`) — the review experience, where a
reviewer selects text to annotate but never edits. Selecting text and `WrapSelectionInMarkAsync`
both work; so does the comment composer over that selection.

The library persists nothing, so an app that wants annotations to *survive a reload* stores its
own anchor data (a text quote, as with highlights) and repaints on load. `WrapTextQuoteInMarkAsync(id, quote)`
is the re-hydration path: it finds the quote in the freshly-loaded document and wraps it in a
mark, with no user selection involved. A hydrated mark is a normal mark —
`ScrollToMarkAsync`, `SetActiveMarkAsync`, and the click callback all work on it.

### The comment composer

Marks give you the *storage* for a comment; `<LexicalCommentComposer />` gives you the
*input*. It is the one piece you would not want to hand-roll — the Lexical playground's
floating comment box, with the selection-rect positioning, the "keep the target highlighted
while I type" decoration, and the fix for the textarea stealing the editor's selection all
handled for you. It **builds on marks**, so place it alongside a `<LexicalMarks />`:

```razor
<LexicalEditor Theme="LexicalTheme.Default">
    <LexicalMarks OnMarkClicked="ShowThread" />
    <LexicalCommentComposer NewMarkId="() => Guid.NewGuid().ToString()"
                            OnSubmit="SaveComment" />
    <LexicalFloatingToolbar>
        <LexicalFormatButton Format="LexicalTextFormat.Bold" />
        <LexicalAddCommentButton />   @* the 💬 affordance over a selection *@
    </LexicalFloatingToolbar>
</LexicalEditor>

@code {
    private void SaveComment(CommentComposition c) =>
        _threads.Add(c.MarkId, c.Text);      // your id, your model
}
```

Select text and press **💬** (the `<LexicalAddCommentButton>`, shown over the selection) —
or call `await _composer.OpenAsync(yourId)` from anywhere. A textarea floats at the
selection, the target stays lit while you type, and confirming (the button, or
<kbd>⌘</kbd>/<kbd>Ctrl</kbd>+<kbd>Enter</kbd>) wraps the span in a mark and raises
`OnSubmit` with `CommentComposition(MarkId, Text)`. <kbd>Esc</kbd> or a click away cancels
(`OnCancel`).

**Mark ids stay yours.** Supply one per open to `OpenAsync(markId)`, or a `NewMarkId`
factory the add-comment button calls. `NewMarkId` is optional — omit it and the composer
mints a UUIDv7, still handed back through `OnSubmit`, so you always learn the id to file
your thread under. As with everything else here, interop is opt-in: `OpenAsync` always
works, and the `OnSubmit`/`OnCancel` callbacks are the only JS→.NET traffic, armed only when
you wire them.

| Member | Does |
|---|---|
| `OpenAsync(markId)` | Opens over the current selection; `false` if it is collapsed/empty |
| `NewMarkId` | Optional `Func<string>` the add-comment button calls; a UUIDv7 otherwise |
| `OnSubmit` | `CommentComposition(MarkId, Text)` after the span is wrapped |
| `OnCancel` | The box was dismissed without wrapping |
| `ChildContent` | A custom form to float in place of the built-in textarea |
| `OnOpened` | `CommentComposerOpened(MarkId, SelectedText)` when the box opens |
| `ConfirmAsync()` / `CancelAsync()` | Drive the box from C# (your form's own buttons) |
| `Placeholder`, `CloseOnClickAway` | Textarea placeholder; whether a click away cancels |

**Need more than a textarea?** Supply `ChildContent` and the composer floats *your* form at
the selection instead of the built-in textarea — a review form with a department, a severity,
a body, a suggested-edit field. Your form's own buttons call `ConfirmAsync()` (wraps the span
and raises `OnSubmit`, so you still get the mark id) and `CancelAsync()`; `OnOpened` tells you
when the box appears and over what text so you can reset your fields. The built-in textarea is
unchanged if you leave `ChildContent` unset.

There is a [worked demo](../comments) — an editor with a live comment sidebar you can click
back into.

## Highlights

`<LexicalHighlights />` lights up text you can only describe by its **content**. Where a
mark needs to know where the span is, a highlight goes and finds it:

```razor
<LexicalEditor Theme="LexicalTheme.Default">
    <LexicalHighlights @ref="_highlights" />
</LexicalEditor>

@code {
    private LexicalHighlights? _highlights;

    private async Task ShowSuggestion(AiComment comment)
    {
        var result = await _highlights!.HighlightTextAsync(
            new LexicalTextQuote(comment.Quote, comment.Prefix, comment.Suffix),
            highlightId: "ai");

        if (result == LexicalTextAnchorResult.NotFound) { comment.Orphaned = true; }
    }
}
```

| Method | Does |
|---|---|
| `HighlightTextAsync(quote, id, scroll)` | Finds the quote and paints it; reports how well it anchored |
| `HighlightTextAsync(text, id, scroll)` | The same, for when you have only the words |
| `HighlightAllAsync(text, id)` | Paints *every* occurrence, returning the count — find-all |
| `ClearHighlightsAsync(id)` | Clears that set, or all of them with `null` |
| `ScrollToHighlightAsync(id)` | Scrolls the first one into view; `false` if nothing is anchored |

### Highlights are not in the document

Nothing is inserted. Highlights are painted with the browser's CSS Custom Highlight API, so
they add no node, serialize into nothing, create no undo step and never mark the document
dirty. Unlike a selection they survive the user clicking elsewhere and can span several
blocks — which is what makes them usable for review UI the user keeps working alongside.

Reach for **marks** when the annotation must survive a save; reach for **highlights** when
it must not.

### Anchoring, and what `Prefix`/`Suffix` are for

`LexicalTextQuote` is the W3C `TextQuoteSelector` shape. Whitespace is normalized before
matching, so a quote written as prose matches a document whose words are split across
paragraphs, bold runs or marks.

Context **disambiguates; it never rejects.** Every occurrence of `Exact` is scored by how
much of the surrounding context it reproduces and the best one wins, even when nothing
matched — an anchor has to survive the document being edited around it. So the verdict has
three values, and the third is the useful one:

| Result | Means |
|---|---|
| `Matched` | One occurrence was the clear best match |
| `MatchedAmbiguously` | Painted, but several occurrences fit equally well — a weak anchor |
| `NotFound` | The text is not in the document; nothing was painted |

Highlights then **follow their text**: the anchor is re-resolved after every edit, so a
highlight stays on its sentence as the paragraph above it grows, vanishes if the quoted
words are deleted, and comes back on undo.

### Colours are per id

Each `highlightId` paints under the CSS highlight name `blazor-lexical-<id>`, so several
sets coexist and are styled independently:

```css
::highlight(blazor-lexical-ai)       { background-color: #fef08a; }
::highlight(blazor-lexical-reviewer) { background-color: #bfdbfe; }
```

The bundled stylesheet styles only the default id. There is no `LexicalTheme` key here —
a highlight is not a node, so there is no element to put a class on. Ids must be valid CSS
identifiers.

## Suggestions

`<LexicalSuggestions />` renders inline **suggested edits** — the "reviewer with a pen" diff.
Given a span of existing text and a proposed replacement, it strikes the original through and
inserts the proposal after it, highlighted (Google Docs suggestion mode, but display-only).

```razor
<LexicalEditor ReadOnly="true" InitialContent="@_doc" Theme="LexicalTheme.Default">
    <LexicalSuggestions @ref="_suggestions" OnSuggestionClicked="OpenReviewCard" />
</LexicalEditor>

@code {
    private LexicalSuggestions? _suggestions;

    private async Task Paint(SuggestedEdit e) =>
        await _suggestions!.ShowSuggestionAsync(
            e.Id, new LexicalTextQuote(e.Quote, e.Prefix, e.Suffix), e.Proposed);

    private void OpenReviewCard(IReadOnlyList<string> ids) => /* your accept/reject chrome */;
}
```

It is **display-only**: it mutates the in-memory document to paint the diff, which is safe
because the viewer never saves — you load content and repaint from your own store on every
load. There are deliberately **no accept/reject buttons** built in; you render your own chrome
(a review card, a sidebar) and, when the reviewer decides, call `RemoveSuggestionAsync(id)`
(restores the struck text to plain prose and drops the proposal) or `ClearSuggestionsAsync()`,
then repaint from your store.

| Member | Does |
|---|---|
| `ShowSuggestionAsync(id, target, proposedText)` | Paints the diff; `false` if the target is not found |
| `RemoveSuggestionAsync(id)` | Removes one suggestion, restoring the original text |
| `ClearSuggestionsAsync()` | Removes every suggestion |
| `OnSuggestionClicked` | The app-owned id(s) when the reviewer clicks either half |

Suggestion ids are yours, exactly like mark ids — the library never mints or interprets one.
Style the two halves with the `LexicalTheme.SuggestionDeletion` and `SuggestionInsertion`
classes (the bundled stylesheet styles both by default). Everything works with
`ReadOnly="true"`, which is the point.

## Document statistics

`<LexicalStats />` counts words, characters, and paragraphs, and estimates reading time.

```razor
<LexicalEditor>
    <LexicalStats TargetSelector="#count"
                  Template="{words} words · {readingMinutes} min read" />
</LexicalEditor>
<p id="count"></p>
```

The tokens are `{characters}`, `{charactersNoSpaces}`, `{words}`, `{paragraphs}` and
`{readingMinutes}`. Written that way the counter is entirely client-side. Subscribe to
`OnStatsChanged` to receive a `LexicalDocumentStats` in C# instead (or as well), or call
`GetStatsAsync()` on demand.

## Block gutters

`<LexicalBlockGutter>` is a rail that floats beside whichever top-level block the pointer
is over. Like `<LexicalToolbar>`, it is a **container**: you fill it with items, and each
item is added on its own.

Three come in the box:

| Item | Does | Interop |
|---|---|---|
| `<LexicalDragHandle />` | Drag the block to reorder it | none — pure JS |
| `<LexicalAddBlockButton />` | Insert a paragraph below and open the slash menu | none — pure JS |
| `<LexicalGutterButton>` | Runs your C#, handed the block it is beside | opt-in |

So the Lexical playground's left rail is something you compose, not a black box — and you
can put an app-specific rail on the other margin at the same time:

```razor
<LexicalEditor>
    @* the playground's left rail *@
    <LexicalBlockGutter Position="LexicalGutterPosition.LeftInside">
        <LexicalAddBlockButton />
        <LexicalDragHandle />
    </LexicalBlockGutter>

    @* your own rail, on the right *@
    <LexicalBlockGutter>
        <LexicalGutterButton OnClick="Comment" Title="Comment on this block">💬</LexicalGutterButton>
        <LexicalGutterButton OnClick="Pin" Title="Pin">📌</LexicalGutterButton>
    </LexicalBlockGutter>
</LexicalEditor>

@code {
    private Task Comment(LexicalBlockRef? block) =>
        block is null ? Task.CompletedTask
                      : _threads.CreateForBlockAsync(block.Index, block.TextPreview);
}
```

Take only the grip and none of the "+", or two grips in different rails, or none of the
built-ins at all — the rail does not care what is in it.

`Side` picks the margin (`Right` by default). Several rails per editor are fine; ones on
the same side stack outward from the text in declaration order. The container carries
`data-lexical-block-index` / `data-lexical-block-type` / `data-lexical-block-key`, so you
can style a rail against the kind of block it is beside.

A `<LexicalGutterButton>` is handed the block directly, so the common case needs no
`@ref` and no bookkeeping. Any plain `<button @onclick>` works too — read
`HoveredBlock` off the gutter for context, and wire `OnBlockHovered` to arm it. Placing a
`LexicalGutterButton` arms the hover channel by itself, since a per-block button that
can't tell which block it's on would be useless.

> [!WARNING]
> `LexicalBlockRef.NodeKey` is **ephemeral**. Node keys are assigned per editor instance
> and are not serialized, so one does not survive a save/reload or a
> `SetEditorStateJsonAsync` round trip. Use `Index`, or a mark id you own, for anything
> you persist.

See the [API Reference](xref:Blazor.Lexical) for the full parameter set of each component.
