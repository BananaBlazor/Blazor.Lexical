using Blazor.Lexical;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.UnitTests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLexicalBlazor_RegistersOptions_WithDefaults()
    {
        var provider = new ServiceCollection()
            .AddLexicalBlazor()
            .BuildServiceProvider();

        var options = provider.GetRequiredService<LexicalOptions>();

        Assert.Equal("./_content/Blazor.Lexical/blazor-lexical.mjs", options.ModuleUrl);
        Assert.Null(options.DefaultTheme);
    }

    [Fact]
    public void AddLexicalBlazor_AppliesConfigureCallback()
    {
        var theme = new { paragraph = "p" };

        var provider = new ServiceCollection()
            .AddLexicalBlazor(o =>
            {
                o.ModuleUrl = "https://cdn.example/lexical.mjs";
                o.DefaultTheme = theme;
            })
            .BuildServiceProvider();

        var options = provider.GetRequiredService<LexicalOptions>();

        Assert.Equal("https://cdn.example/lexical.mjs", options.ModuleUrl);
        Assert.Same(theme, options.DefaultTheme);
    }

    [Fact]
    public void AddLexicalBlazor_RegistersOptions_AsSingleton()
    {
        var provider = new ServiceCollection()
            .AddLexicalBlazor()
            .BuildServiceProvider();

        var first = provider.GetRequiredService<LexicalOptions>();
        var second = provider.GetRequiredService<LexicalOptions>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddLexicalBlazor_ReturnsSameServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddLexicalBlazor();

        Assert.Same(services, returned);
    }
}
