using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of the range→mark hydration API (L2):
/// <see cref="Blazor.Lexical.LexicalMarks.WrapTextQuoteInMarkAsync"/>, which repaints a stored
/// annotation as a mark from its text quote alone — no live selection. Drives the same
/// read-only review page the L1 tests use, since re-hydrating annotations after a load is the
/// review use case.
/// </summary>
public class MarkHydrationTests : HarnessTestBase
{
    public MarkHydrationTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/readonly-annotate";

    protected override string ReadySelector =>
        "#editor-readonly-annotate[data-lexical-editor='true']";

    private const string Content = "#editor-readonly-annotate";

    [Fact] // a stored quote becomes a mark with no user selection
    public async Task HydrateQuote_WrapsTheMatch_AfterLoad()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-ro-hydrate");

        await Expect(page.Locator("#ro-hydrate-result")).ToHaveTextAsync("True");
        var mark = page.Locator($"{Content} mark");
        await Expect(mark).ToHaveCountAsync(1);
        await Expect(mark).ToHaveTextAsync("risky sections");
    }

    [Fact] // an orphaned anchor (quote not in the document) reports false and paints nothing
    public async Task HydrateMissingQuote_ReturnsFalse()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-ro-hydrate-missing");

        await Expect(page.Locator("#ro-hydrate-result")).ToHaveTextAsync("False");
        await Expect(page.Locator($"{Content} mark")).ToHaveCountAsync(0);
    }

    [Fact] // ScrollToMarkAsync and SetActiveMarkAsync work on a hydrated (not user-wrapped) mark
    public async Task Scroll_And_Activate_WorkOnHydratedMark()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-ro-hydrate");
        await Expect(page.Locator($"{Content} mark")).ToHaveCountAsync(1);

        await page.ClickAsync("#btn-ro-scroll");
        await Expect(page.Locator("#ro-scroll-result")).ToHaveTextAsync("True");

        await page.ClickAsync("#btn-ro-active");
        await Expect(page.Locator($"{Content} mark[data-lexical-mark-active]"))
            .ToHaveCountAsync(1);
    }
}
