using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Covers the table feature on <c>#editor-tables</c> (.harness-tables), which has NO
/// callbacks wired — proving the whole table stack is client-side (zero JS→.NET
/// interop), like the other overlays:
/// - the toolbar's grid picker inserts a table of the hovered size;
/// - the "/table" slash item inserts a default table;
/// - the programmatic C# <c>InsertTableAsync</c> inserts a sized table with a header row;
/// - the in-cell action menu appears in the caret's cell and edits the table structure.
/// </summary>
public class TableTests : HarnessTestBase
{
    public TableTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/tables";

    protected override string ReadySelector =>
        "#editor-tables[data-lexical-editor='true']";

    private const string Editor = "#editor-tables";
    private const string Root = ".harness-tables";

    [Fact] // the programmatic C# insert makes a 2×3 table with a header row — no callbacks
    public async Task ProgrammaticInsert_MakesSizedTableWithHeaderRow()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator(Editor);

        await page.ClickAsync(Editor);
        await page.ClickAsync("#btn-insert-table");

        await Expect(editor.Locator("table")).ToHaveCountAsync(1);
        await Expect(editor.Locator("table tr")).ToHaveCountAsync(2);
        // includeHeaderRow (default) ⇒ the first row is three header cells.
        await Expect(editor.Locator("table tr").First.Locator("th")).ToHaveCountAsync(3);
        Assert.Empty(errors);
    }

    [Fact] // the toolbar grid picker inserts a table of the clicked R×C size
    public async Task ToolbarPicker_InsertsTableOfHoveredSize()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator(Editor);
        var popover = page.Locator($"{Root} [data-lexical-table-picker-popover]");

        await page.ClickAsync(Editor);
        await page.ClickAsync($"{Root} [data-lexical-table-picker-trigger]");
        await Expect(popover).Not.ToHaveAttributeAsync("hidden", "");

        // Click the 2×2 grid cell.
        await page.ClickAsync($"{Root} [data-lexical-table-grid-cell][data-row='2'][data-col='2']");

        await Expect(editor.Locator("table")).ToHaveCountAsync(1);
        await Expect(editor.Locator("table tr")).ToHaveCountAsync(2);
        await Expect(editor.Locator("table tr").First.Locator("th")).ToHaveCountAsync(2);
        await Expect(popover).ToHaveAttributeAsync("hidden", "");
        Assert.Empty(errors);
    }

    [Fact] // typing "/table" then Enter inserts a default table and removes the trigger
    public async Task SlashMenu_InsertsDefaultTable()
    {
        var page = await OpenAsync();
        var editor = page.Locator(Editor);
        var menu = page.Locator($"{Root} .blazor-lexical__slash-menu");

        await page.ClickAsync(Editor);
        await page.Keyboard.TypeAsync("/table");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Keyboard.PressAsync("Enter");
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(editor.Locator("table")).ToHaveCountAsync(1);
        // Default is 3 rows.
        await Expect(editor.Locator("table tr")).ToHaveCountAsync(3);
        await Expect(editor).Not.ToContainTextAsync("/table");
    }

    [Fact] // the action menu appears in the caret's cell and inserts a row below
    public async Task ActionMenu_InsertsRowBelow()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator(Editor);
        var menu = page.Locator($"{Root} .blazor-lexical__table-menu");

        await page.ClickAsync(Editor);
        await page.ClickAsync("#btn-insert-table"); // 2×3 table
        await Expect(editor.Locator("table tr")).ToHaveCountAsync(2);

        // Place the caret in a body cell (not the header cell the freshly-inserted
        // table's trigger already floats over); the action menu should surface there.
        await editor.Locator("table td").First.ClickAsync();
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.ClickAsync($"{Root} [data-lexical-table-trigger]");
        await page.ClickAsync($"{Root} [data-lexical-table-action='row-below']");

        await Expect(editor.Locator("table tr")).ToHaveCountAsync(3);
        Assert.Empty(errors);
    }

    [Fact] // the action menu's "Delete table" removes the whole table
    public async Task ActionMenu_DeletesTable()
    {
        var page = await OpenAsync();
        var editor = page.Locator(Editor);
        var menu = page.Locator($"{Root} .blazor-lexical__table-menu");

        await page.ClickAsync(Editor);
        await page.ClickAsync("#btn-insert-table");
        await Expect(editor.Locator("table")).ToHaveCountAsync(1);

        await editor.Locator("table td").First.ClickAsync();
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.ClickAsync($"{Root} [data-lexical-table-trigger]");
        await page.ClickAsync($"{Root} [data-lexical-table-action='table-delete']");

        await Expect(editor.Locator("table")).ToHaveCountAsync(0);
    }
}
