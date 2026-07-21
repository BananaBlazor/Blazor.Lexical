using Blazor.Lexical;

namespace Tests.UnitTests;

public class LexicalOptionsTests
{
    [Fact]
    public void DefaultModuleUrl_PointsAtSelfHostedRclAsset()
    {
        var options = new LexicalOptions();

        Assert.Equal("./_content/Blazor.Lexical/blazor-lexical.mjs", options.ModuleUrl);
    }

    [Fact]
    public void DefaultModuleUrl_IsRelative_SoDynamicImportResolves()
    {
        // A bare specifier ("_content/...") throws "Failed to resolve module
        // specifier" in the browser; the leading "./" is required. Lock it in.
        var options = new LexicalOptions();

        Assert.StartsWith("./", options.ModuleUrl);
    }

    [Fact]
    public void DefaultTheme_IsNullByDefault()
    {
        var options = new LexicalOptions();

        Assert.Null(options.DefaultTheme);
    }
}
