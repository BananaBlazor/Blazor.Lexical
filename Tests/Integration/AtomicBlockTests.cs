using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>setup.primitives.atomicBlock</c>, driven through a consumer
/// block decorator (<c>harness-block-decorator.mjs</c>) rather than a built-in, so it
/// proves the helper is reachable from the public extension surface.
/// </summary>
public class AtomicBlockTests : HarnessTestBase
{
    private const string Editor = "#editor-atomic";
    private static readonly Regex Selected = new(@"\bharness-block--selected\b");

    public AtomicBlockTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/atomic-block";

    protected override string ReadySelector => $"{Editor}[data-lexical-editor='true']";

    private static ILocator Block(IPage page, string label) =>
        page.Locator($"{Editor} .harness-block", new() { HasText = label });

    [Fact] // click selects the block and paints the caller's class
    public async Task Click_SelectsTheBlock_AndPaintsTheClass()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await Block(page, "X").ClickAsync();

        await Expect(Block(page, "X")).ToHaveClassAsync(Selected);
        await Expect(Block(page, "Y")).Not.ToHaveClassAsync(Selected);
        Assert.Empty(errors);
    }

    [Fact] // a plain click on another block moves the selection; shift-click extends it
    public async Task ShiftClick_ExtendsTheSelection()
    {
        var page = await OpenAsync();

        await Block(page, "X").ClickAsync();
        await Block(page, "Y").ClickAsync();
        await Expect(Block(page, "X")).Not.ToHaveClassAsync(Selected);
        await Expect(Block(page, "Y")).ToHaveClassAsync(Selected);

        await Block(page, "X").ClickAsync(new() { Modifiers = [KeyboardModifier.Shift] });

        await Expect(Block(page, "X")).ToHaveClassAsync(Selected);
        await Expect(Block(page, "Y")).ToHaveClassAsync(Selected);
    }

    [Fact] // Delete removes a selected block, and only the block
    public async Task Delete_RemovesTheSelectedBlock()
    {
        var page = await OpenAsync();

        await Block(page, "X").ClickAsync();
        await Expect(Block(page, "X")).ToHaveClassAsync(Selected);
        await page.Keyboard.PressAsync("Delete");

        await Expect(Block(page, "X")).ToHaveCountAsync(0);
        await Expect(Block(page, "Y")).ToHaveCountAsync(1);
        await Expect(page.Locator(Editor)).ToContainTextAsync("above");
        await Expect(page.Locator(Editor)).ToContainTextAsync("below");
    }

    [Fact] // Backspace removes a selected block, and only the block
    public async Task Backspace_RemovesTheSelectedBlock()
    {
        var page = await OpenAsync();

        await Block(page, "Y").ClickAsync();
        await Expect(Block(page, "Y")).ToHaveClassAsync(Selected);
        await page.Keyboard.PressAsync("Backspace");

        await Expect(Block(page, "Y")).ToHaveCountAsync(0);
        await Expect(Block(page, "X")).ToHaveCountAsync(1);
        await Expect(page.Locator(Editor)).ToContainTextAsync("above");
        await Expect(page.Locator(Editor)).ToContainTextAsync("below");
    }

    [Fact] // a block inserted at runtime is atomic too
    public async Task InsertedBlock_IsSelectable()
    {
        var page = await OpenAsync();

        await page.Locator($"{Editor} p", new() { HasText = "below" }).ClickAsync();
        await page.ClickAsync("#btn-atomic-insert");
        var inserted = Block(page, "inserted");
        await Expect(inserted).ToHaveCountAsync(1);

        await inserted.ClickAsync();

        await Expect(inserted).ToHaveClassAsync(Selected);
    }

    [Fact] // the helper declines clicks elsewhere, so the node's own LOW handler still runs
    public async Task OwnLowPriorityClickHandler_StillRunsForOtherTargets()
    {
        var page = await OpenAsync();
        var root = page.Locator(".harness-atomic");

        await page.Locator($"{Editor} p", new() { HasText = "above" }).ClickAsync();
        await Expect(root).ToHaveAttributeAsync("data-harness-clicks", "1");

        // The helper claims a click on its own node, so the handler behind it does not run.
        await Block(page, "X").ClickAsync();
        await Expect(Block(page, "X")).ToHaveClassAsync(Selected);
        await Expect(root).ToHaveAttributeAsync("data-harness-clicks", "1");

        await page.Locator($"{Editor} p", new() { HasText = "below" }).ClickAsync();
        await Expect(root).ToHaveAttributeAsync("data-harness-clicks", "2");
        await Expect(Block(page, "X")).Not.ToHaveClassAsync(Selected);
    }

    [Fact] // disposing the editor strips the class from DOM that outlives it
    public async Task Dispose_StripsTheSelectedClass()
    {
        var page = await OpenAsync();

        await Block(page, "X").ClickAsync();
        await Expect(Block(page, "X")).ToHaveClassAsync(Selected);
        var block = await Block(page, "X").ElementHandleAsync();

        // A synthetic click: no mousedown or focus change that could move the selection first.
        await page.Locator("#btn-atomic-remove").DispatchEventAsync("click");

        await page.WaitForFunctionAsync(
            "el => !el.isConnected && !el.classList.contains('harness-block--selected')", block);
    }
}
