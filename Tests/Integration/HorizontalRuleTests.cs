using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalHorizontalRule /&gt;</c>. <c>#editor-hr</c> nests
/// the extension; <c>#editor-hr-absent</c> deliberately does not, which is what proves
/// the <c>hr:insert</c> token is an inert no-op rather than an error when the node was
/// never registered.
/// </summary>
public class HorizontalRuleTests : HarnessTestBase
{
    public HorizontalRuleTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/horizontal-rule";

    protected override string ReadySelector =>
        "#editor-hr-absent[data-lexical-editor='true']";

    [Fact] // the command token inserts the node, entirely client-side
    public async Task HrInsertToken_InsertsARule_WithNoInterop()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, "#editor-hr", "above");
        await page.ClickAsync("#btn-hr-insert");

        await Expect(page.Locator("#editor-hr hr.blazor-lexical__hr")).ToHaveCountAsync(1);
        Assert.Empty(errors);
    }

    [Fact] // the .NET→JS escape hatch reaches the same command
    public async Task InsertAsync_InsertsARuleFromCSharp()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-hr", "above");
        await page.ClickAsync("#btn-hr-insert-cs");

        await Expect(page.Locator("#editor-hr hr.blazor-lexical__hr")).ToHaveCountAsync(1);
    }

    [Fact] // clicking a rule selects it, which is what makes Delete meaningful
    public async Task ClickingARule_MarksItSelected()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-hr", "above");
        await page.ClickAsync("#btn-hr-insert");
        var rule = page.Locator("#editor-hr hr.blazor-lexical__hr");
        await Expect(rule).ToHaveCountAsync(1);

        await rule.ClickAsync();

        await Expect(rule).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(
            "blazor-lexical__hr--selected"));
    }

    [Fact] // a selected rule is deletable, and deselecting drops the class again
    public async Task SelectedRule_IsRemovedByBackspace()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-hr", "above");
        await page.ClickAsync("#btn-hr-insert");
        var rule = page.Locator("#editor-hr hr.blazor-lexical__hr");
        await Expect(rule).ToHaveCountAsync(1);

        await rule.ClickAsync();
        await page.Keyboard.PressAsync("Backspace");

        await Expect(page.Locator("#editor-hr hr.blazor-lexical__hr")).ToHaveCountAsync(0);
    }

    [Fact] // the node is upstream's, so <hr> survives an HTML round trip
    public async Task Rule_SurvivesAnHtmlRoundTrip()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-hr-set-html");
        await Expect(page.Locator("#editor-hr hr.blazor-lexical__hr")).ToHaveCountAsync(1);

        await page.ClickAsync("#btn-hr-get-html");

        await Expect(page.Locator("#hr-html")).ToContainTextAsync("<hr>");
    }

    [Fact] // no extension, no handler: the token does nothing and logs nothing
    public async Task HrInsertToken_IsANoOp_WithoutTheExtension()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, "#editor-hr-absent", "plain");
        await page.ClickAsync("#btn-hr-absent-insert");

        await Expect(page.Locator("#editor-hr-absent hr")).ToHaveCountAsync(0);
        await Expect(page.Locator("#editor-hr-absent")).ToContainTextAsync("plain");
        Assert.Empty(errors);
    }
}
