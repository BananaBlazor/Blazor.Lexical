using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalTabIndent /&gt;</c>. <c>#editor-tab-plain</c>
/// carries no extension, which is the invariant that matters most here: rebinding Tab
/// costs a keyboard user their way out of the editor, so an editor that did not ask for
/// the behavior must not get it.
/// </summary>
public class TabIndentTests : HarnessTestBase
{
    public TabIndentTests(HarnessFixture fx) : base(fx) { }

    /// <summary>
    /// Reads the first block's indent level out of the editor state (via the harness's
    /// read button), rather than off a CSS side effect that styling could move.
    /// </summary>
    private static async Task AssertIndentAsync(IPage page, string readButton, string expected)
    {
        await page.ClickAsync(readButton);
        await Expect(page.Locator("#tab-indent")).ToHaveTextAsync(expected);
    }

    [Fact] // Tab at the start of a block indents it
    public async Task Tab_IndentsTheBlock_WithTheExtension()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, "#editor-tab-indent", "indent me");
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Tab");

        await AssertIndentAsync(page, "#btn-tab-indent-of", "1");
        Assert.Empty(errors);
    }

    [Fact] // Shift+Tab is the inverse
    public async Task ShiftTab_OutdentsTheBlock()
    {
        var page = await Fx.OpenHarnessAsync();

        // No mid-flight assertion: reading the level clicks a button, which takes focus
        // off the editor and would leave the Shift+Tab below with nothing to act on.
        await TypeAsync(page, "#editor-tab-indent", "indent me");
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Shift+Tab");

        await AssertIndentAsync(page, "#btn-tab-indent-of", "1");
    }

    [Fact] // MaxIndent="2" allows levels below 2, so indenting stops at 1
    public async Task MaxIndent_CapsTheDepth()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, "#editor-tab-capped", "capped");
        await page.Keyboard.PressAsync("Control+A");
        for (var i = 0; i < 5; i++)
        {
            await page.Keyboard.PressAsync("Tab");
        }

        await AssertIndentAsync(page, "#btn-tab-capped-of", "1");
    }

    [Fact] // the whole reason this is opt-in: Tab must stay a navigation key otherwise
    public async Task Tab_DoesNotIndent_WithoutTheExtension()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, "#editor-tab-plain", "leave me");
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Tab");

        await AssertIndentAsync(page, "#btn-tab-plain-of", "0");
    }
}
