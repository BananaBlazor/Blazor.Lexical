using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalPageBreak /&gt;</c>. <c>#editor-pb</c> nests the
/// extension with a toolbar and slash menu; <c>#editor-pb-hidden</c> hides the toolbar
/// button; <c>#editor-pb-absent</c> has no extension, which proves the
/// <c>page-break:insert</c> token is an inert no-op and the markup is dropped.
/// </summary>
public class PageBreakTests : HarnessTestBase
{
    private const string Break = "figure[type='page-break']";
    private const string ToolbarButton = ".blazor-lexical__toolbar [data-lexical-command='page-break:insert']";
    private const string SlashItem = ".blazor-lexical__slash-menu [data-lexical-command='page-break:insert']";

    public PageBreakTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/page-break";

    protected override string ReadySelector =>
        "#editor-pb-absent[data-lexical-editor='true']";

    [Fact] // the toolbar button inserts a break, and the caret lands in the paragraph after it
    public async Task ToolbarButton_InsertsABreak_FollowedByAParagraph()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, "#editor-pb", "above");
        await page.ClickAsync($".harness-pb {ToolbarButton}");
        await page.Keyboard.TypeAsync("below");

        await Expect(page.Locator($"#editor-pb > {Break}.blazor-lexical__page-break")).ToHaveCountAsync(1);
        await Expect(page.Locator($"#editor-pb > {Break} + p")).ToHaveTextAsync("below");
        Assert.Empty(errors);
    }

    [Fact] // "/page" then Enter runs the same token from the slash menu
    public async Task SlashItem_InsertsABreak()
    {
        var page = await OpenAsync();
        var menu = page.Locator(".harness-pb .blazor-lexical__slash-menu");

        await page.ClickAsync("#editor-pb");
        await page.Keyboard.TypeAsync("/page");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator($"#editor-pb > {Break}")).ToHaveCountAsync(1);
        await Expect(page.Locator("#editor-pb")).Not.ToContainTextAsync("/page");
    }

    [Fact] // the .NET→JS escape hatch reaches the same command
    public async Task InsertAsync_InsertsABreakFromCSharp()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-pb", "above");
        await page.ClickAsync("#btn-pb-insert-cs");

        await Expect(page.Locator($"#editor-pb > {Break}")).ToHaveCountAsync(1);
        await Expect(page.Locator($"#editor-pb > {Break} + p")).ToHaveCountAsync(1);
    }

    [Fact] // the button and slash item exist only with the extension, and ShowPageBreak hides the button
    public async Task Controls_AppearOnlyWithTheExtension()
    {
        var page = await OpenAsync();

        await Expect(page.Locator($".harness-pb {ToolbarButton}")).ToHaveCountAsync(1);
        await Expect(page.Locator($".harness-pb {SlashItem}")).ToHaveCountAsync(1);
        await Expect(page.Locator($".harness-pb-hidden {ToolbarButton}")).ToHaveCountAsync(0);
        await Expect(page.Locator($".harness-pb-absent {ToolbarButton}")).ToHaveCountAsync(0);
        await Expect(page.Locator($".harness-pb-absent {SlashItem}")).ToHaveCountAsync(0);
    }

    [Fact] // clicking a break selects it; Backspace then removes it
    public async Task ClickingABreak_SelectsIt_AndBackspaceRemovesIt()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-pb", "above");
        await page.ClickAsync("#btn-pb-insert-cs");
        var pageBreak = page.Locator($"#editor-pb {Break}");
        await Expect(pageBreak).ToHaveCountAsync(1);

        await pageBreak.ClickAsync();
        await Expect(pageBreak).ToHaveClassAsync(new Regex("blazor-lexical__page-break--selected"));

        await page.Keyboard.PressAsync("Backspace");
        await Expect(page.Locator($"#editor-pb {Break}")).ToHaveCountAsync(0);
        await Expect(page.Locator("#editor-pb")).ToContainTextAsync("above");
    }

    [Fact] // Delete removes a selected break too
    public async Task SelectedBreak_IsRemovedByDelete()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-pb", "above");
        await page.ClickAsync("#btn-pb-insert-cs");
        var pageBreak = page.Locator($"#editor-pb {Break}");
        await pageBreak.ClickAsync();
        await Expect(pageBreak).ToHaveClassAsync(new Regex("blazor-lexical__page-break--selected"));

        await page.Keyboard.PressAsync("Delete");

        await Expect(page.Locator($"#editor-pb {Break}")).ToHaveCountAsync(0);
    }

    [Fact] // export carries the print style, and re-importing the export keeps one break
    public async Task Break_SurvivesAnHtmlRoundTrip()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-pb-set-figure");
        await Expect(page.Locator($"#editor-pb {Break}")).ToHaveCountAsync(1);

        await page.ClickAsync("#btn-pb-roundtrip");

        var html = page.Locator("#pb-html");
        await Expect(html).ToContainTextAsync("type=\"page-break\"");
        await Expect(html).ToContainTextAsync("break-after: page");
        await Expect(page.Locator($"#editor-pb {Break}")).ToHaveCountAsync(1);
        Assert.Single(Regex.Matches(await html.InnerTextAsync(), "<figure"));
    }

    [Fact] // Word's clipboard page break imports as a break, lifted out of its paragraph
    public async Task WordStyleBr_ImportsAsAPageBreak()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-pb-set-word");

        await Expect(page.Locator($"#editor-pb > {Break}")).ToHaveCountAsync(1);
        await Expect(page.Locator($"#editor-pb p {Break}")).ToHaveCountAsync(0);
        await Expect(page.Locator("#editor-pb")).ToContainTextAsync("above");
        await Expect(page.Locator("#editor-pb")).ToContainTextAsync("below");
    }

    [Fact] // a plain <br> is still a line break
    public async Task PlainBr_StaysALineBreak()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-pb-set-plain-br");

        await Expect(page.Locator("#editor-pb p br")).ToHaveCountAsync(1);
        await Expect(page.Locator($"#editor-pb {Break}")).ToHaveCountAsync(0);
    }

    [Fact] // a break inserted in a list item lands at the root, splitting the list
    public async Task InsertingInAListItem_PutsTheBreakAtRootLevel()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-pb-set-list");
        await page.Locator("#editor-pb li").First.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.ClickAsync($".harness-pb {ToolbarButton}");

        await Expect(page.Locator($"#editor-pb > {Break}")).ToHaveCountAsync(1);
        await Expect(page.Locator($"#editor-pb li {Break}, #editor-pb ul {Break}")).ToHaveCountAsync(0);
        await Expect(page.Locator("#editor-pb")).ToContainTextAsync("first");
        await Expect(page.Locator("#editor-pb")).ToContainTextAsync("second");
    }

    [Fact] // no extension, no handler: the token does nothing, and loaded markup is dropped
    public async Task WithoutTheExtension_TokenIsANoOp_AndMarkupIsDropped()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, "#editor-pb-absent", "plain");
        await page.ClickAsync("#btn-pb-absent-insert");
        await Expect(page.Locator("#editor-pb-absent")).ToContainTextAsync("plain");
        await Expect(page.Locator($"#editor-pb-absent {Break}")).ToHaveCountAsync(0);

        await page.ClickAsync("#btn-pb-absent-set-figure");
        await Expect(page.Locator("#editor-pb-absent")).ToContainTextAsync("below");
        await Expect(page.Locator($"#editor-pb-absent {Break}")).ToHaveCountAsync(0);
        Assert.Empty(errors);
    }
}
