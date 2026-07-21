using Microsoft.Playwright;

namespace Tests.Integration;

[Collection("harness")]
public class SmokeTests
{
    private readonly HarnessFixture _fx;

    public SmokeTests(HarnessFixture fx) => _fx = fx;

    [Fact]
    public async Task Harness_MountsLexicalEditor()
    {
        var page = await _fx.OpenHarnessAsync();
        var editable = await page.GetAttributeAsync("#editor-main", "contenteditable");
        Assert.Equal("true", editable);
    }

    [Fact]
    public async Task ModuleAsset_IsServed()
    {
        var page = await _fx.OpenHarnessAsync();
        var response = await page.APIRequest.GetAsync(
            _fx.BaseUrl + "_content/Blazor.Lexical/blazor-lexical.mjs");
        Assert.Equal(200, response.Status);
    }
}
