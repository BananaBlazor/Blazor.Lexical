using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Covers the nested toolbar and the opt-in interop model:
/// - built-in toolbar buttons format entirely in JS (no callbacks wired) and drive
///   data-lexical-active without any .NET round-trip (#editor-format / .harness-format);
/// - the opt-in OnContentChanged / OnSelectionChanged channels push only when
///   subscribed (#editor-notify / .harness-notify).
/// - the programmatic C# methods still drive the editor.
/// </summary>
public class FormattingTests : HarnessTestBase
{
    public FormattingTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/formatting";

    protected override string ReadySelector =>
        "#editor-toolbar-extra[data-lexical-editor='true']";

    private static async Task TypeAndSelectAllAsync(IPage page, string selector, string text)
    {
        await TypeAsync(page, selector, text);
        await page.Keyboard.PressAsync("Control+A");
    }

    [Fact] // toolbar Bold button works with NO callbacks wired — pure JS, zero interop
    public async Task ToolbarBoldButton_BoldsSelection_WithoutAnyCallback()
    {
        var page = await OpenAsync();
        var editor = page.Locator("#editor-format");

        await TypeAndSelectAllAsync(page, "#editor-format", "pure js bold");
        await page.ClickAsync(".harness-format [aria-label='Bold']");

        await Expect(editor.Locator("strong")).ToHaveCountAsync(1);
        await Expect(editor.Locator("strong")).ToHaveTextAsync("pure js bold");
    }

    [Fact] // active-state reflects the selection with no callback (JS sets data-lexical-active)
    public async Task ToolbarActiveState_ReflectsSelection_WithoutCallback()
    {
        var page = await OpenAsync();
        var boldButton = page.Locator(".harness-format [aria-label='Bold']");

        await TypeAndSelectAllAsync(page, "#editor-format", "active me");
        await Expect(boldButton).Not.ToHaveAttributeAsync("data-lexical-active", "");

        await boldButton.ClickAsync();
        await Expect(boldButton).ToHaveAttributeAsync("data-lexical-active", "");
    }

    [Fact] // block-type via the toolbar <select> (change dispatched in JS)
    public async Task ToolbarBlockSelect_AppliesHeading()
    {
        var page = await OpenAsync();
        var editor = page.Locator("#editor-format");

        await TypeAndSelectAllAsync(page, "#editor-format", "heading via select");
        await page.Locator(".harness-format [data-lexical-command='block:select']")
            .SelectOptionAsync("h1");

        await Expect(editor.Locator("h1")).ToHaveCountAsync(1);
        await Expect(editor.Locator("h1")).ToHaveTextAsync("heading via select");
    }

    [Fact] // the block <select> carries the list options inline (playground-style):
           // selecting "Bulleted list" makes a list, selecting "Normal" unwraps it
    public async Task ToolbarBlockSelect_AppliesAndRemovesList()
    {
        var page = await OpenAsync();
        var editor = page.Locator("#editor-format");
        var select = page.Locator(".harness-format [data-lexical-command='block:select']");

        await TypeAndSelectAllAsync(page, "#editor-format", "list item");
        await select.SelectOptionAsync("bullet");
        await Expect(editor.Locator("ul > li")).ToHaveCountAsync(1);

        await page.ClickAsync("#editor-format");
        await page.Keyboard.PressAsync("Control+A");
        await select.SelectOptionAsync("paragraph");
        await Expect(editor.Locator("ul")).ToHaveCountAsync(0);
    }

    [Fact] // programmatic C# methods still drive the same editor
    public async Task ProgrammaticMethods_FormatAndBlockAndHistory()
    {
        var page = await OpenAsync();
        var editor = page.Locator("#editor-format");

        await TypeAndSelectAllAsync(page, "#editor-format", "history text");
        await page.ClickAsync("#btn-block-h1");
        await Expect(editor.Locator("h1")).ToHaveCountAsync(1);

        await page.ClickAsync("#btn-fmt-undo");
        await Expect(editor.Locator("h1")).ToHaveCountAsync(0);

        await page.ClickAsync("#btn-fmt-redo");
        await Expect(editor.Locator("h1")).ToHaveCountAsync(1);
    }

    [Fact] // opt-in: OnContentChanged + OnSelectionChanged push only on the subscribed editor
    public async Task OptInCallbacks_PushContentAndSelection()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-notify", "hello");
        await Expect(page.Locator("#notify-change")).ToHaveTextAsync("hello");
        await Expect(page.Locator("#notify-change-count")).Not.ToHaveTextAsync("0");

        await page.Keyboard.PressAsync("Control+A");
        await page.ClickAsync(".harness-notify [aria-label='Bold']");
        await Expect(page.Locator("#notify-sel-bold")).ToHaveTextAsync("true");
    }

    /// <summary>
    /// The selection push carries the selected text, so an app action can store the quote
    /// it acts on. Collapsing the selection clears it — the caret quotes nothing.
    /// </summary>
    [Fact]
    public async Task SelectionState_CarriesTheSelectedText()
    {
        var page = await OpenAsync();

        await TypeAndSelectAllAsync(page, "#editor-notify", "quote me");
        await Expect(page.Locator("#notify-sel-text")).ToHaveTextAsync("quote me");

        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(page.Locator("#notify-sel-text")).ToHaveTextAsync("");
    }

    /// <summary>
    /// StartContent/EndContent add to the toolbar instead of replacing it — the common
    /// "the default controls plus my one button" case, which ChildContent cannot serve
    /// without re-composing every group.
    /// </summary>
    [Fact]
    public async Task ToolbarFragments_AddToTheDefaultControlSet()
    {
        var page = await OpenAsync();
        var toolbar = page.Locator(".harness-toolbar-extra .blazor-lexical__toolbar");

        await Expect(toolbar.Locator("#btn-toolbar-start")).ToHaveCountAsync(1);
        await Expect(toolbar.Locator("#btn-toolbar-end")).ToHaveCountAsync(1);
        // The defaults are still there — a default control from the middle of the set.
        await Expect(toolbar.Locator("[aria-label='Bold']")).ToHaveCountAsync(1);
    }
}
