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
- `<LexicalDragHandle />` — a left-gutter grip + "insert" button on hover.
- `<LexicalLinkEditor />` — a floating link editor popup.
- `<LexicalTableEditor />` — an in-cell action menu (requires `<LexicalTables />`).

## The `data-lexical-command` contract

Buttons declare a command token that JS interprets. The built-in tokens:

- `format:{bold|italic|underline|strikethrough|code|subscript|superscript|lowercase|uppercase}`
- `block:{paragraph|h1..h6|quote}`
- `list:{bullet|number|remove}`
- `history:{undo|redo}`
- `align:{left|center|right|justify}`
- `link:{toggle|remove}`
- `table:insert[:RxC]` (e.g. `table:3x4`)
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

See the [API Reference](xref:Blazor.Lexical) for the full parameter set of each
component.
