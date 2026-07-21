using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalStats /&gt;</c>. <c>#editor-stats-quiet</c>
/// writes its count into the page with no callback (zero interop);
/// <c>#editor-stats-notify</c> additionally pushes the numbers to C#.
/// </summary>
public class StatsTests : HarnessTestBase
{
    public StatsTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/stats";

    protected override string ReadySelector =>
        "#editor-stats-quiet[data-lexical-editor='true']";

    [Fact] // the target surface is entirely client-side
    public async Task Stats_WriteTheTemplateIntoTheTarget_WithNoInterop()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, "#editor-stats-quiet", "one two three");

        await Expect(page.Locator("#stats-target-quiet")).ToHaveTextAsync("3 words");
        Assert.Empty(errors);
    }

    [Fact] // every token in the template is substituted
    public async Task Stats_SubstituteEveryTemplateToken()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-stats-notify", "one two");

        // "{words}w/{characters}c" — 2 words, 7 characters including the space.
        await Expect(page.Locator("#stats-target-notify")).ToHaveTextAsync("2w/7c");
    }

    [Fact] // the opt-in push carries the same numbers the target shows
    public async Task Stats_PushTheSnapshot_WhenOnStatsChangedIsWired()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-stats-notify", "one two three four five");

        // words|characters|paragraphs|readingMinutes — one paragraph, ceil(5/200) = 1.
        await Expect(page.Locator("#stats-push")).ToHaveTextAsync("5|23|1|1");
    }

    [Fact] // the pull API answers without a subscription
    public async Task GetStatsAsync_ReturnsTheCurrentNumbers()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-stats-notify", "alpha beta");
        await Expect(page.Locator("#stats-target-notify")).ToHaveTextAsync("2w/10c");

        await page.ClickAsync("#btn-stats-get");

        // words|charactersNoSpaces — the space is excluded from the second figure.
        await Expect(page.Locator("#stats-get-result")).ToHaveTextAsync("2|9");
    }

    [Fact] // an empty document is zero words, not one
    public async Task Stats_CountAnEmptyDocumentAsZeroWords()
    {
        var page = await OpenAsync();

        await Expect(page.Locator("#stats-target-quiet")).ToHaveTextAsync("0 words");
    }
}
