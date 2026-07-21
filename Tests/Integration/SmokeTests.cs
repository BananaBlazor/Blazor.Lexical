using Microsoft.Playwright;

namespace Tests.Integration;

public class SmokeTests : HarnessTestBase
{
    public SmokeTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/core";

    protected override string ReadySelector =>
        "#editor-disposable[data-lexical-editor='true']";

    [Fact]
    public async Task Harness_MountsLexicalEditor()
    {
        var page = await OpenAsync();
        var editable = await page.GetAttributeAsync("#editor-main", "contenteditable");
        Assert.Equal("true", editable);
    }

    [Fact]
    public async Task ModuleAsset_IsServed()
    {
        var page = await OpenAsync();
        var response = await page.APIRequest.GetAsync(
            Fx.BaseUrl + "_content/Blazor.Lexical/blazor-lexical.mjs");
        Assert.Equal(200, response.Status);
    }
}
