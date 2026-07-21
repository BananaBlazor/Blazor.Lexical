using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalBlockGutter /&gt;</c> — the composable per-block
/// hover rail. <c>#editor-gutter-notify</c> carries <b>two</b> rails (a left one with the
/// built-in JS-driven items, a right one with host actions), which is what proves several
/// coexist; <c>#editor-gutter-quiet</c> carries one with no callback and no
/// <c>LexicalGutterButton</c>, so it reveals and its plain button works with zero
/// JS→.NET calls.
/// <para>
/// The drag/add-block behaviour of the rail's built-in items lives in
/// <c>OverlayTests</c> — it is the same machinery, just reached through the gutter now.
/// </para>
/// </summary>
public class BlockGutterTests : HarnessTestBase
{
    public BlockGutterTests(HarnessFixture fx) : base(fx) { }

    private const string NotifyEditor = "#editor-gutter-notify";
    private const string QuietEditor = "#editor-gutter-quiet";

    [Fact] // it reveals beside the hovered block
    public async Task Gutter_AppearsOnHover()
    {
        var page = await Fx.OpenHarnessAsync();
        var gutter = page.Locator(".harness-gutter-quiet .blazor-lexical__block-gutter");

        await TypeAsync(page, QuietEditor, "hover target");
        await Expect(gutter).Not.ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Locator($"{QuietEditor} p").First.HoverAsync();
        await Expect(gutter).ToHaveAttributeAsync("data-lexical-visible", "");
    }

    [Fact] // invariant #1: no callback and no typed button ⇒ no interop, button still works
    public async Task Gutter_DoesNoInterop_WhenNothingIsWired()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, QuietEditor, "hover target");
        await page.Locator($"{QuietEditor} p").First.HoverAsync();
        await page.ClickAsync("#btn-gutter-quiet-action");

        // The button is an ordinary Blazor @onclick — invariant 4's escape hatch — so it
        // fires without the hover channel being armed at all.
        await Expect(page.Locator("#gutter-quiet-action")).ToHaveTextAsync("clicked");
        await Expect(page.Locator("#gutter-hover-count")).ToHaveTextAsync("0");
        Assert.Empty(errors);
    }

    [Fact] // several rails coexist, and both reveal on the same hover
    public async Task TwoGutters_BothRevealOnHover()
    {
        var page = await Fx.OpenHarnessAsync();
        var left = page.Locator(".harness-gutter-left");
        var right = page.Locator(".harness-gutter-right");

        await TypeAsync(page, NotifyEditor, "hover target");
        await page.Locator($"{NotifyEditor} p").First.HoverAsync();

        await Expect(left).ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(right).ToHaveAttributeAsync("data-lexical-visible", "");
    }

    [Fact] // Inside sits in the card's reserved margin; Outside hangs off its edge
    public async Task Gutters_PositionAccordingToTheirDeclaredPosition()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hover target");
        var block = page.Locator($"{NotifyEditor} p").First;
        await block.HoverAsync();
        await Expect(page.Locator(".harness-gutter-left"))
            .ToHaveAttributeAsync("data-lexical-visible", "");

        var card = await page.Locator($"{NotifyEditor}").BoundingBoxAsync();
        var text = await block.BoundingBoxAsync();
        var left = await page.Locator(".harness-gutter-left").BoundingBoxAsync();
        var right = await page.Locator(".harness-gutter-right").BoundingBoxAsync();
        var textMiddle = text!.X + text.Width / 2;

        // Each rail is in its own margin. Not an exact "clear of the text" bound for the
        // inside one: a rail wider than the reserved gutter (a host button with its own
        // padding) is deliberately clamped inward rather than allowed to overflow, so a
        // pixel or two of overlap is the designed outcome.
        Assert.True(left!.X + left.Width < textMiddle,
            $"left rail ends at {left.X + left.Width}, past the text middle {textMiddle}");
        Assert.True(right!.X > textMiddle,
            $"right rail starts at {right.X}, before the text middle {textMiddle}");

        // LeftInside stays within the card…
        Assert.True(left.X >= card!.X,
            $"left rail at {left.X} escaped the card starting at {card.X}");
        // …while RightOutside deliberately hangs off it. Reachability is the grace
        // window's job, not geometry's — see GutterButton_StaysReachable_*.
        Assert.True(right.X >= card.X + card.Width,
            $"right rail at {right.X} should hang outside the card ending at {card.X + card.Width}");
    }

    // The regression the grace window exists for: the rail here is OUTSIDE the card, so
    // walking to it crosses pixels the editor root does not own and fires its mouseleave.
    // Hiding on that event directly made the rail vanish mid-journey and its buttons
    // unclickable — which is exactly what a user hit.
    [Fact] // you can travel to a rail's buttons, even one hanging outside the editor
    public async Task GutterButton_StaysReachable_WhenThePointerTravelsToIt()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hover target");
        var block = page.Locator($"{NotifyEditor} p").First;
        await block.HoverAsync();

        var text = await block.BoundingBoxAsync();
        var button = await page.Locator(".harness-gutter-typed").BoundingBoxAsync();
        var midY = text!.Y + text.Height / 2;

        // Walk the pointer from the text out across the gutter and onto the button, the
        // way a user does — no teleporting. Every step must keep the rail visible.
        await page.Mouse.MoveAsync(text.X + text.Width - 4, midY);
        await page.Mouse.MoveAsync(text.X + text.Width + 4, midY);
        await page.Mouse.MoveAsync(
            button!.X + button.Width / 2, button.Y + button.Height / 2, new() { Steps = 10 });

        await Expect(page.Locator(".harness-gutter-right"))
            .ToHaveAttributeAsync("data-lexical-visible", "");

        // And the click lands on the button it travelled to.
        await page.Mouse.DownAsync();
        await page.Mouse.UpAsync();
        await Expect(page.Locator("#gutter-typed-action")).ToHaveTextAsync("0|p");
    }

    [Fact] // the opt-in push identifies which block the pointer is on
    public async Task Gutter_PushesTheHoveredBlock_WhenOnBlockHoveredIsWired()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "first block");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("second block");

        await page.Locator($"{NotifyEditor} p").Nth(1).HoverAsync();

        // index|blockType — the second top-level block, rendered as a <p>.
        await Expect(page.Locator("#gutter-hovered")).ToHaveTextAsync("1|p");
    }

    [Fact] // the block context is stamped onto EVERY rail, not just the subscribed one
    public async Task Gutters_StampTheBlockContextOntoBothContainers()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "first block");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("second block");
        await page.Locator($"{NotifyEditor} p").Nth(1).HoverAsync();

        foreach (var rail in new[] { ".harness-gutter-left", ".harness-gutter-right" })
        {
            await Expect(page.Locator(rail)).ToHaveAttributeAsync("data-lexical-block-index", "1");
            await Expect(page.Locator(rail)).ToHaveAttributeAsync("data-lexical-block-type", "p");
        }
    }

    [Fact] // a plain @onclick in a rail reads HoveredBlock for the block it acts on
    public async Task GutterButton_ActsOnTheHoveredBlock()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "first block");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("second block");

        await page.Locator($"{NotifyEditor} p").Nth(1).HoverAsync();
        await Expect(page.Locator("#gutter-hovered")).ToHaveTextAsync("1|p");
        // Moving onto the rail itself must not hide it — otherwise the click never lands.
        await page.ClickAsync("#btn-gutter-action");

        await Expect(page.Locator("#gutter-action")).ToHaveTextAsync("1|second block");
    }

    [Fact] // <LexicalGutterButton> is handed the block directly, with no @ref bookkeeping
    public async Task TypedGutterButton_ReceivesTheHoveredBlock()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "first block");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("second block");

        await page.Locator($"{NotifyEditor} p").Nth(1).HoverAsync();
        await Expect(page.Locator("#gutter-hovered")).ToHaveTextAsync("1|p");
        await page.ClickAsync(".harness-gutter-typed");

        await Expect(page.Locator("#gutter-typed-action")).ToHaveTextAsync("1|p");
    }

    [Fact] // one crossing per block, not per mouse move (deduped by node key)
    public async Task Gutter_DoesNotPush_WhileTheHoverStaysOnOneBlock()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "a single long block of text to move across");
        var block = page.Locator($"{NotifyEditor} p").First;

        await block.HoverAsync(new() { Position = new() { X = 5, Y = 5 } });
        await Expect(page.Locator("#gutter-hover-count")).ToHaveTextAsync("1");

        await block.HoverAsync(new() { Position = new() { X = 60, Y = 5 } });
        await block.HoverAsync(new() { Position = new() { X = 120, Y = 5 } });

        await Expect(page.Locator("#gutter-hover-count")).ToHaveTextAsync("1");
    }
}
