# Toolbar & Overlays

The editor is a single `<LexicalEditor>` with **optional, opt-in** child markup. In its
bare state it performs **zero** JS→.NET interop — typing and formatting are entirely
client-side, and Blazor reads content only when you ask (`GetTextAsync()` and friends).
A push channel to .NET turns on only when you attach a callback such as
`OnContentChanged`.

## Toolbar

`<LexicalToolbar>` is placed as a child of the editor. Its built-in buttons are markup
tagged with a command token; a single delegated listener in JS dispatches the Lexical
command and owns each button's active/disabled state — a built-in button click never
round-trips to .NET.

```razor
<LexicalEditor OnContentChanged="OnChanged">
    <LexicalToolbar />
</LexicalEditor>
```

To keep the default controls and simply *add* to them, use `StartContent` /
`EndContent` — the usual home for an extension's own button:

```razor
<LexicalToolbar>
    <EndContent>
        <BadgeButton />
    </EndContent>
</LexicalToolbar>
```

`ChildContent`, by contrast, **replaces** the default set. Compose your own toolbar
from the primitives and groups when that is what you want:
`LexicalCommandButton`, `LexicalFormatButton`, `LexicalHistoryButton`,
`LexicalListButton`, `LexicalBlockButton`, `LexicalAlignButton`, and the groups
`LexicalBasicFormatting`, `LexicalHistoryGroup`, `LexicalListGroup`,
`LexicalBlockSelect`, `LexicalHyperlink`, and `LexicalTableButton` (an insert grid
picker).

## In-editor overlays

Add any of these as children; JS positions the Blazor-authored markup and reuses the
same command dispatch as the toolbar — the overlays add no interop of their own.

- `<LexicalFloatingToolbar />` — appears above a non-empty text selection.
- `<LexicalSlashMenu>` — a typeahead list opened by typing `/` at the start of a line;
  populate it with `<LexicalSlashItem>` entries.
- `<LexicalBlockGutter>` — a per-block hover rail. Fill it with `<LexicalDragHandle />`
  (drag to reorder), `<LexicalAddBlockButton />` ("+"), and/or your own
  `<LexicalGutterButton>`; several rails per editor are fine. See
  [Document features](document-features.md).
- `<LexicalLinkEditor />` — a floating link editor popup.
- `<LexicalTableEditor />` — an in-cell action menu (requires `<LexicalTables />`).

The floating toolbar, slash menu, link editor, comment composer and mention picker render
above the page (as top-layer popovers anchored to the text). An editor inside a scrolling
pane, a short card or an `overflow: hidden` container still shows them in full, and you
don't need to leave room for them in your layout. They still take their styling from the
`blazor-lexical__*` classes.

## The `data-lexical-command` contract

Buttons declare a command token that JS interprets. The built-in tokens:

- `format:{bold|italic|underline|strikethrough|code|subscript|superscript|lowercase|uppercase}`
- `block:{paragraph|h1..h6|quote}`
- `list:{bullet|number|remove}`
- `history:{undo|redo}`
- `align:{left|center|right|justify}`
- `link:{toggle|remove}`
- `table:insert[:RxC]` (e.g. `table:3x4`)
- `hr:insert` — inserts a horizontal rule. Does nothing unless the editor nests
  `<LexicalHorizontalRule />`.
- `page-break:insert` — inserts a page break. Does nothing unless the editor nests
  `<LexicalPageBreak />`. Nesting it also adds a toolbar button (hide it with
  `<LexicalToolbar ShowPageBreak="false" />`; the default is `true`) and a "Page break"
  slash item.
- `clear-formatting`

The enums model these tokens, so custom chrome doesn't have to spell them out — use
`ToJsToken()` (and `FromJsToken()` to go back) on `LexicalTextFormat`,
`LexicalBlockType`, and `LexicalAlignment`:

```razor
<button type="button"
        data-lexical-command="@($"format:{LexicalTextFormat.Bold.ToJsToken()}")">
    Bold
</button>
```

## The opt-in escape hatch

When you need real C# logic (save, export, insert a computed value), use an ordinary
Blazor `<button @onclick>` in the toolbar, or a `<LexicalSlashItem OnSelect="...">` in
the slash menu. These reach the editor through the cascaded `LexicalEditor` and
round-trip by design — that is the only expected interop for toolbar/menu actions.

```razor
<LexicalSlashItem Label="Insert today's date"
                  Keywords="date today"
                  OnSelect="InsertDate" />
```

## Tables and mentions

- **Tables** are an extension, nested like any other child: add `<LexicalTables />` and
  the `@lexical/table` chunk is downloaded only then; editors that don't declare it never
  pay for it. Declaring it is also what lights up the toolbar's insert picker, the
  `/table` slash item, `<LexicalTableEditor />`, and `InsertTableAsync` — chrome you
  can ask about yourself with `editor.HasExtension<LexicalTables>()`.

  ```razor
  <LexicalEditor>
      <LexicalToolbar />
      <LexicalTables />
      <LexicalTableEditor />
  </LexicalEditor>
  ```

- **Mentions** are configured by nesting one or more `<LexicalMention>` children (each
  with its own initiator character, colour, optional provider, and freeform flag). A
  config with no `Provider` does zero JS→.NET calls; only a provider config queries .NET.
  A committed mention behaves as one inline object: a click selects it whole, ←/→ step
  over it, typing over it replaces it, and Backspace or Delete removes it whole. The
  picker renders above the page (a top-layer popover anchored to the caret), so an
  editor inside a scrolling pane or an `overflow: hidden` container still shows the whole
  list; it opens above the caret when there is no room below. Style the selected state
  with `.blazor-lexical__mention[data-lexical-mention-selected]`. Mentions keep their id and url in HTML and editor-state JSON. For Markdown, set
  `MarkdownUrlPrefix` and give each item a `Url` of that prefix followed by its id (no
  spaces or parentheses): the mention is written as a plain `[text](url)` link, and on load
  any link whose url starts with the prefix becomes a mention again, with the rest of the
  url as its id.

  ```razor
  <LexicalMention Initiator="#" MarkdownUrlPrefix="#ticket:" Provider="SearchTickets" />
  @* "[#12 · Budget](#ticket:42)" loads as a mention with id "42" *@
  ```

  To start a mention from the toolbar, add a `<LexicalMentionButton>` per initiator. It
  inserts the initiator at the caret and opens the picker, as if typed, with no .NET
  round trip. It is disabled while the editor is read-only or when no `<LexicalMention>`
  with a `Provider` uses its initiator. Its content defaults to the initiator; pass your own
  icon instead:

  ```razor
  <LexicalEditor>
      <LexicalToolbar>
          <EndContent>
              <LexicalMentionButton Initiator="@@" Title="Mention a person">
                  <i class="icon-at"></i>
              </LexicalMentionButton>
              <LexicalMentionButton Initiator="#" Title="Reference a ticket">
                  <i class="icon-hash"></i>
              </LexicalMentionButton>
          </EndContent>
      </LexicalToolbar>
      <LexicalMention Initiator="@@" Provider="SearchPeople" />
      <LexicalMention Initiator="#" Provider="SearchTickets" />
  </LexicalEditor>
  ```

  `editor.StartMentionAsync("@")` does the same from code.

See the [API Reference](xref:Blazor.Lexical) for the full parameter set of each
component.
