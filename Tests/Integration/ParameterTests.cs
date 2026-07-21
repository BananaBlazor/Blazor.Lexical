using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Exercises how component parameters flow across the interop boundary:
/// Placeholder, CssClass, Theme, ReadOnly, EnableHistory.
/// </summary>
public class ParameterTests : HarnessTestBase
{
    public ParameterTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/core";

    protected override string ReadySelector =>
        "#editor-disposable[data-lexical-editor='true']";

    [Fact] // Placeholder — driven purely by JS/CSS (data-lexical-empty + data-placeholder), no interop
    public async Task Placeholder_ShowsWhenEmpty_HidesAfterTyping()
    {
        var page = await OpenAsync();
        var content = page.Locator("#editor-main");

        // The placeholder text rides on the content element and the empty-state flag
        // is present while the editor has no text.
        await Expect(content).ToHaveAttributeAsync("data-placeholder", "Type here…");
        await Expect(content).ToHaveAttributeAsync("data-lexical-empty", "");

        await TypeAsync(page, "#editor-main", "x");
        await Expect(content).Not.ToHaveAttributeAsync("data-lexical-empty", "");
    }

    [Fact] // CssClass
    public async Task CssClass_AppliedToWrapper()
    {
        var page = await OpenAsync();

        // The main editor's contenteditable lives inside a wrapper carrying the class.
        await Expect(page.Locator(".blazor-lexical.harness-main #editor-main")).ToHaveCountAsync(1);
    }

    [Fact] // Theme -> options.theme -> Lexical applies class to nodes
    public async Task Theme_AppliedToParagraphNodes()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "themed");

        await Expect(page.Locator("#editor-main p")).ToHaveClassAsync(new Regex("harness-paragraph"));
    }

    [Fact] // ReadOnly (options.readOnly at create)
    public async Task ReadOnly_CreatedNonEditable_RejectsTyping()
    {
        var page = await OpenAsync();
        var readonlyEditor = page.Locator("#editor-readonly");

        await Expect(readonlyEditor).ToHaveAttributeAsync("contenteditable", "false");

        await readonlyEditor.ClickAsync();
        await page.Keyboard.TypeAsync("should not appear");

        await Expect(readonlyEditor).ToHaveTextAsync("");
    }

    [Fact] // EnableHistory = true -> undo reverts
    public async Task History_Enabled_UndoReverts()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "first ");
        // Separate history entry, then a second edit.
        await page.WaitForTimeoutAsync(400);
        await page.Keyboard.TypeAsync("second");
        await page.ClickAsync("#btn-get");
        await Expect(page.Locator("#get-result")).ToHaveTextAsync("first second");

        await page.Locator("#editor-main").ClickAsync();
        await page.Keyboard.PressAsync("Control+z");

        await page.ClickAsync("#btn-get");
        // After undo the text is shorter than the full "first second".
        await Expect(page.Locator("#get-result")).Not.ToHaveTextAsync("first second");
    }

    [Fact] // EnableHistory = false -> undo does nothing
    public async Task History_Disabled_UndoDoesNotRevert()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-nohistory", "keep this");
        await page.Keyboard.PressAsync("Control+z");

        await Expect(page.Locator("#editor-nohistory")).ToHaveTextAsync("keep this");
    }
}
