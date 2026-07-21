using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Covers the in-editor overlays on <c>#editor-overlays</c> (.harness-overlays), which
/// has NO callbacks wired — proving the floating toolbar, slash menu, and drag handle
/// are entirely client-side (zero JS→.NET interop), just like the built-in toolbar:
/// - the floating toolbar appears over a selection and formats via data-lexical-command;
/// - the slash menu opens on "/", filters, and converts the block on Enter;
/// - the drag handle appears on hover and its "+" inserts a block and opens the menu.
/// (Native HTML5 drag-reorder isn't simulated here — Playwright can't drive real
/// dataTransfer drags reliably — so that path is verified by hand.)
/// </summary>
public class OverlayTests : HarnessTestBase
{
    public OverlayTests(HarnessFixture fx) : base(fx) { }

    [Fact] // floating toolbar shows over a selection and bolds it — no callbacks wired
    public async Task FloatingToolbar_AppearsOnSelection_AndBolds()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator("#editor-overlays");
        var toolbar = page.Locator(".harness-overlays .blazor-lexical__floating-toolbar");

        await TypeAsync(page, "#editor-overlays", "float me");
        await Expect(toolbar).Not.ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Keyboard.PressAsync("Control+A");
        await Expect(toolbar).ToHaveAttributeAsync("data-lexical-visible", "");

        await toolbar.Locator("[aria-label='Bold']").ClickAsync();
        await Expect(editor.Locator("strong")).ToHaveCountAsync(1);
        await Expect(editor.Locator("strong")).ToHaveTextAsync("float me");
        Assert.Empty(errors);
    }

    [Fact] // the hover toolbar exposes the playground's transform buttons (uppercase here)
    public async Task FloatingToolbar_AppliesUppercaseTransform()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator("#editor-overlays");
        var toolbar = page.Locator(".harness-overlays .blazor-lexical__floating-toolbar");

        await TypeAsync(page, "#editor-overlays", "make me big");
        await page.Keyboard.PressAsync("Control+A");
        await Expect(toolbar).ToHaveAttributeAsync("data-lexical-visible", "");

        await toolbar.Locator("[aria-label='Uppercase']").ClickAsync();

        var transformed = editor.Locator(".blazor-lexical__text-uppercase");
        await Expect(transformed).ToHaveCountAsync(1);
        await Expect(transformed).ToHaveTextAsync("make me big");
        Assert.Empty(errors);
    }

    [Fact] // the toolbar's left edge never crosses into the gutter (first text column)
    public async Task FloatingToolbar_LeftEdge_StaysInTextColumn()
    {
        var page = await Fx.OpenHarnessAsync();
        var toolbar = page.Locator(".harness-overlays .blazor-lexical__floating-toolbar");

        // A selection starting at column 0 is where the left clamp actually bites.
        await TypeAsync(page, "#editor-overlays", "clamp my left edge to the text column");
        await page.Keyboard.PressAsync("Control+A");
        await Expect(toolbar).ToHaveAttributeAsync("data-lexical-visible", "");

        // toolbar.left must be >= the content's first text column (left + padding-left).
        var slack = await page.EvaluateAsync<double>(@"() => {
            const root = document.querySelector('.harness-overlays');
            const content = root.querySelector('[data-lexical-content]');
            const bar = root.querySelector('.blazor-lexical__floating-toolbar');
            const padLeft = parseFloat(getComputedStyle(content).paddingLeft) || 0;
            const textLeft = content.getBoundingClientRect().left + padLeft;
            return bar.getBoundingClientRect().left - textLeft; // >= 0 means in-column
        }");

        Assert.True(slack >= -1.0, $"toolbar left crossed into the gutter by {-slack}px");
    }

    [Fact] // typing "/h1" then Enter converts the line to a heading and removes the trigger
    public async Task SlashMenu_ConvertsLineToHeading()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator("#editor-overlays");
        var menu = page.Locator(".harness-overlays .blazor-lexical__slash-menu");

        await page.ClickAsync("#editor-overlays");
        await page.Keyboard.TypeAsync("/h1");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Keyboard.PressAsync("Enter");
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(editor.Locator("h1")).ToHaveCountAsync(1);
        // The "/h1" trigger text must be gone (the heading is empty).
        await Expect(editor).Not.ToContainTextAsync("/h1");
    }

    [Fact] // Escape closes the slash menu without changing the block
    public async Task SlashMenu_EscapeCloses()
    {
        var page = await Fx.OpenHarnessAsync();
        var menu = page.Locator(".harness-overlays .blazor-lexical__slash-menu");

        await page.ClickAsync("#editor-overlays");
        await page.Keyboard.TypeAsync("/quote");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Keyboard.PressAsync("Escape");
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(page.Locator("#editor-overlays blockquote")).ToHaveCountAsync(0);
    }

    [Fact] // the drag handle appears in the gutter when the pointer is over a block
    public async Task DragHandle_AppearsOnHover()
    {
        var page = await Fx.OpenHarnessAsync();
        var handle = page.Locator(".harness-overlays .blazor-lexical__drag-handle");

        await TypeAsync(page, "#editor-overlays", "hover target");
        await Expect(handle).Not.ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Locator("#editor-overlays p").First.HoverAsync();
        await Expect(handle).ToHaveAttributeAsync("data-lexical-visible", "");
    }

    [Fact] // the "+" add-block button inserts a paragraph below and opens the slash menu
    public async Task AddBlockButton_InsertsParagraph_AndOpensSlashMenu()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator("#editor-overlays");
        var menu = page.Locator(".harness-overlays .blazor-lexical__slash-menu");

        await TypeAsync(page, "#editor-overlays", "first block");
        await Expect(editor.Locator("p")).ToHaveCountAsync(1);

        // Hover to bind the handle to the block, then click "+".
        await editor.Locator("p").First.HoverAsync();
        await page.Locator(".harness-overlays [data-lexical-add-block]").ClickAsync();

        await Expect(editor.Locator("p")).ToHaveCountAsync(2);
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
    }
}
