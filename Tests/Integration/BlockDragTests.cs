using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of the block gutter's nested drag (the <c>ctx.blockDrag</c> policy
/// seam). <c>#editor-nested-drag</c> installs a policy (HarnessNestedDrag) that makes list
/// items individually draggable and reparentable; <c>#editor-flat-drag</c> installs no
/// policy, proving the default top-level reorder is unchanged.
/// <para>
/// As in <c>OverlayTests</c>, native HTML5 <c>dataTransfer</c> drags can't be driven fully
/// reliably by Playwright, so these lean on the reorder outcome and the drop indicator's
/// DOM state; the deepest interactions stay partly hand-verified.
/// </para>
/// </summary>
public class BlockDragTests : HarnessTestBase
{
    public BlockDragTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/block-drag";

    protected override string ReadySelector =>
        "#editor-flat-drag[data-lexical-editor='true']";

    private const string NestedEditor = "#editor-nested-drag";
    private const string FlatEditor = "#editor-flat-drag";

    /// <summary>Drags a grip (revealed by hovering <paramref name="fromBlock"/>) onto the top
    /// of <paramref name="toBlock"/>, via the HTML5 drag emulation OverlayTests uses.</summary>
    private static async Task DragGripAsync(
        IPage page, string gripRail, ILocator fromBlock, ILocator toBlock)
    {
        await fromBlock.HoverAsync();
        var grip = page.Locator($"{gripRail} [data-lexical-drag-grip]");
        var box = await toBlock.BoundingBoxAsync();

        await grip.HoverAsync();
        await page.Mouse.DownAsync();
        // Two moves: HTML5 drag needs a move to start the drag before the target move.
        await page.Mouse.MoveAsync(box!.X + box.Width / 2, box.Y + 2, new() { Steps = 8 });
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + 2, new() { Steps = 4 });
        await page.Mouse.UpAsync();
    }

    [Fact] // a nested <li> reorders within its list, independently of the whole <ul>
    public async Task NestedListItem_ReordersWithinList()
    {
        var page = await OpenAsync();
        // The policy extension must have registered (marks the root on load).
        await Expect(page.Locator(".harness-nested-drag[data-nested-drag='on']")).ToHaveCountAsync(1);
        var items = page.Locator($"{NestedEditor} li");
        await Expect(items).ToHaveCountAsync(3);
        await Expect(items).ToHaveTextAsync(new[] { "alpha", "beta", "gamma" });

        // Drag the second item (beta) up onto the first (alpha).
        await DragGripAsync(page, ".harness-nested-left", items.Nth(1), items.First);

        // The list is still one <ul> of three items — reordered, not reparented.
        await Expect(items).ToHaveCountAsync(3);
        await Expect(items).ToHaveTextAsync(new[] { "beta", "alpha", "gamma" });
    }

    [Fact] // the drop indicator paints at a nested gap, tinted by the target's --lexical-drop-color
    public async Task NestedDrop_ShowsThemedIndicator()
    {
        var page = await OpenAsync();
        var items = page.Locator($"{NestedEditor} li");
        await Expect(items).ToHaveCountAsync(3);
        var dropLine = page.Locator(".harness-nested-drag .blazor-lexical__drop-line");

        // Start a drag and hold it over the list (no mouse-up yet) so the indicator is live.
        await items.Nth(2).HoverAsync();
        var grip = page.Locator(".harness-nested-left [data-lexical-drag-grip]");
        var target = await items.First.BoundingBoxAsync();
        await grip.HoverAsync();
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(target!.X + target.Width / 2, target.Y + 2, new() { Steps = 8 });
        await page.Mouse.MoveAsync(target.X + target.Width / 2, target.Y + 2, new() { Steps = 4 });

        // The indicator is visible and wears the list's per-target colour, not the accent.
        await Expect(dropLine).ToHaveAttributeAsync("data-lexical-visible", "");
        var background = await dropLine.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.Equal("rgb(220, 20, 60)", background);

        await page.Mouse.UpAsync();
    }

    [Fact] // no policy ⇒ the default top-level reorder is unchanged
    public async Task FlatDrag_ReordersTopLevel_WithoutPolicy()
    {
        var page = await OpenAsync();
        var paras = page.Locator($"{FlatEditor} p");
        await Expect(paras).ToHaveTextAsync(new[] { "one", "two", "three" });

        // Drag the second paragraph up onto the first.
        await DragGripAsync(page, ".harness-flat-left", paras.Nth(1), paras.First);

        await Expect(paras).ToHaveTextAsync(new[] { "two", "one", "three" });
    }
}
