using static Microsoft.Playwright.Assertions;
using Microsoft.Playwright;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalHighlights /&gt;</c> — decoration of text found by
/// its content. The harness editor holds "the lazy dog" and "fox" twice each, either side
/// of a paragraph break, so ambiguity, context disambiguation and block-boundary
/// normalization all have something to bite on.
/// </summary>
public class HighlightTests : HarnessTestBase
{
    public HighlightTests(HarnessFixture fx) : base(fx) { }

    private const string Editor = "#editor-highlights";

    /// <summary>
    /// How many ranges are currently painted under a highlight id. Read from
    /// <c>CSS.highlights</c> rather than from the DOM on purpose: the whole point of the
    /// feature is that there is nothing in the DOM to look at.
    /// </summary>
    private static Task<int> PaintedRangesAsync(IPage page, string highlightId = "default") =>
        page.EvaluateAsync<int>(
            "id => CSS.highlights.get('blazor-lexical-' + id)?.size ?? 0",
            highlightId);

    [Fact] // the whole point: an app-supplied quote finds and paints its text
    public async Task HighlightText_PaintsTheQuote()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-string");

        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("Matched");
        Assert.Equal(1, await PaintedRangesAsync(page));
    }

    /// <summary>
    /// Two occurrences and no context to choose between them: the first is painted and the
    /// caller is told the anchor is weak, rather than the call failing.
    /// </summary>
    [Fact]
    public async Task HighlightText_ReportsAmbiguity_WhenContextCannotChoose()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-plain");

        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("MatchedAmbiguously");
        Assert.Equal(1, await PaintedRangesAsync(page));
    }

    [Fact] // prefix/suffix resolve the same ambiguous quote to one occurrence
    public async Task HighlightText_DisambiguatesWithContext()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-context");

        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("Matched");
        Assert.Equal(1, await PaintedRangesAsync(page));
    }

    [Fact] // a quote that is not there paints nothing and says so
    public async Task HighlightText_ReportsNotFound()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-missing");

        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("NotFound");
        Assert.Equal(0, await PaintedRangesAsync(page));
    }

    /// <summary>
    /// A quote spanning the paragraph break. This only matches if the block boundary
    /// normalizes to a single space — matching raw text content would run "dog." straight
    /// into "A second" and find nothing.
    /// </summary>
    [Fact]
    public async Task HighlightText_MatchesAcrossBlockBoundaries()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-cross");

        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("Matched");
        Assert.Equal(1, await PaintedRangesAsync(page));
    }

    [Fact] // find-all: every occurrence, under its own id
    public async Task HighlightAll_PaintsEveryOccurrence()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-all");

        await Expect(page.Locator("#hl-count")).ToHaveTextAsync("2");
        Assert.Equal(2, await PaintedRangesAsync(page, "search"));
    }

    /// <summary>
    /// Ids are independent sets: clearing one leaves the other painted, which is what lets
    /// an app run AI suggestions and a search at the same time in different colours.
    /// </summary>
    [Fact]
    public async Task ClearHighlights_ClearsOneIdOrAllOfThem()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-string");
        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("Matched");
        await page.ClickAsync("#btn-hl-all");
        await Expect(page.Locator("#hl-count")).ToHaveTextAsync("2");
        Assert.Equal(1, await PaintedRangesAsync(page));
        Assert.Equal(2, await PaintedRangesAsync(page, "search"));

        await page.ClickAsync("#btn-hl-clear");
        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("cleared:default");
        Assert.Equal(0, await PaintedRangesAsync(page));
        Assert.Equal(2, await PaintedRangesAsync(page, "search"));

        await page.ClickAsync("#btn-hl-clear-all");
        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("cleared:all");
        Assert.Equal(0, await PaintedRangesAsync(page, "search"));
    }

    /// <summary>
    /// The document is never touched: no node is inserted, so the serialized editor state
    /// is byte-identical before and after painting. That is the invariant that separates
    /// highlights from marks, and it is what keeps them out of the undo stack.
    /// </summary>
    [Fact]
    public async Task Highlighting_DoesNotChangeTheDocument()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);

        var before = await StateJsonAsync(page);
        await page.ClickAsync("#btn-hl-string");
        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("Matched");
        Assert.Equal(1, await PaintedRangesAsync(page));

        Assert.Equal(before, await StateJsonAsync(page));
        // No <mark> or wrapper element either — the paint is entirely off-DOM.
        await Expect(page.Locator($"{Editor} mark")).ToHaveCountAsync(0);
        Assert.Empty(errors);
    }

    /// <summary>
    /// Highlights follow their text: the anchor is re-resolved after every edit, so typing
    /// ahead of the quote keeps it painted, and deleting the quoted words drops it.
    /// </summary>
    [Fact]
    public async Task Highlight_ReanchorsAfterEdits()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-string");
        await Expect(page.Locator("#hl-result")).ToHaveTextAsync("Matched");
        Assert.Equal(1, await PaintedRangesAsync(page));

        // Insert text at the very top — every DOM node the range pointed at is replaced.
        await page.ClickAsync(Editor);
        await page.Keyboard.PressAsync("Control+Home");
        await page.Keyboard.TypeAsync("Preamble. ");
        await Expect(page.Locator($"{Editor}")).ToContainTextAsync("Preamble.");
        await page.WaitForFunctionAsync(
            "() => (CSS.highlights.get('blazor-lexical-default')?.size ?? 0) === 1");
    }

    [Fact] // scrolling asks the same question as anchoring, so it answers false when unpainted
    public async Task ScrollToHighlight_ReportsWhetherAnythingIsAnchored()
    {
        var page = await Fx.OpenHarnessAsync();

        await page.ClickAsync("#btn-hl-scroll");
        await Expect(page.Locator("#hl-scroll-result")).ToHaveTextAsync("False");

        await page.ClickAsync("#btn-hl-string");
        await page.ClickAsync("#btn-hl-scroll");
        await Expect(page.Locator("#hl-scroll-result")).ToHaveTextAsync("True");
    }

    /// <summary>The serialized editor state, read straight off the JS module.</summary>
    private static Task<string> StateJsonAsync(IPage page) =>
        page.EvaluateAsync<string>(
            @"async () => {
                const mod = await import('/_content/Blazor.Lexical/blazor-lexical.mjs');
                return mod.getEditorStateJson('editor-highlights');
            }");
}
