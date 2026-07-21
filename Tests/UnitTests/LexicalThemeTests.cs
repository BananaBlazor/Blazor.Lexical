using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Blazor.Lexical;

namespace Tests.UnitTests;

/// <summary>
/// Locks the <see cref="LexicalTheme"/> wire shape: property names must map to
/// Lexical's <c>EditorThemeClasses</c> keys (camelCase, nested), and unset keys
/// must be omitted. These options mirror the source-generated context the editor
/// uses (camelCase + ignore-null), so a rename here would desync the real payload.
/// </summary>
public class LexicalThemeTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static JsonNode Serialize(LexicalTheme theme) =>
        JsonNode.Parse(JsonSerializer.Serialize(theme, Options))!;

    [Fact]
    public void Serialization_MapsToLexicalThemeKeys()
    {
        var theme = new LexicalTheme
        {
            Paragraph = "p",
            Quote = "q",
            Link = "a",
            Heading = { H1 = "h1", H3 = "h3" },
            List = { Ul = "ul", Ol = "ol", Listitem = "li", Nested = { Listitem = "nli" } },
            Text = { Bold = "b", UnderlineStrikethrough = "us" },
            Mention = "m",
            MentionHighlight = "mh",
            Mark = "mk",
            MarkOverlap = "mko",
        };

        var json = Serialize(theme);

        Assert.Equal("p", (string?)json["paragraph"]);
        Assert.Equal("q", (string?)json["quote"]);
        Assert.Equal("a", (string?)json["link"]);
        Assert.Equal("m", (string?)json["mention"]);
        Assert.Equal("mh", (string?)json["mentionHighlight"]);
        // MarkNode reads config.theme.mark / .markOverlap — the keys @lexical/mark uses.
        Assert.Equal("mk", (string?)json["mark"]);
        Assert.Equal("mko", (string?)json["markOverlap"]);
        Assert.Equal("h1", (string?)json["heading"]!["h1"]);
        Assert.Equal("h3", (string?)json["heading"]!["h3"]);
        Assert.Equal("ul", (string?)json["list"]!["ul"]);
        Assert.Equal("ol", (string?)json["list"]!["ol"]);
        Assert.Equal("li", (string?)json["list"]!["listitem"]);
        Assert.Equal("nli", (string?)json["list"]!["nested"]!["listitem"]);
        Assert.Equal("b", (string?)json["text"]!["bold"]);
        Assert.Equal("us", (string?)json["text"]!["underlineStrikethrough"]);
    }

    [Fact]
    public void Serialization_OmitsUnsetKeys()
    {
        var theme = new LexicalTheme { Text = { Bold = "b" } };

        var json = Serialize(theme);

        // Only the key that was set is present.
        Assert.Equal("b", (string?)json["text"]!["bold"]);
        Assert.Null(json["text"]!["italic"]);
        // Top-level string leaves left unset are omitted entirely.
        Assert.Null(json["paragraph"]);
        Assert.Null(json["link"]);
    }

    [Fact]
    public void Default_UsesTheClassNamesStyledByTheBundledCss()
    {
        var theme = LexicalTheme.Default;

        Assert.Equal("blazor-lexical__paragraph", theme.Paragraph);
        Assert.Equal("blazor-lexical__quote", theme.Quote);
        Assert.Equal("blazor-lexical__link", theme.Link);
        Assert.Equal("blazor-lexical__mention", theme.Mention);
        Assert.Equal("blazor-lexical__mention-highlight", theme.MentionHighlight);
        Assert.Equal("blazor-lexical__mark", theme.Mark);
        Assert.Equal("blazor-lexical__mark--overlap", theme.MarkOverlap);
        Assert.Equal("blazor-lexical__h1", theme.Heading.H1);
        Assert.Equal("blazor-lexical__h6", theme.Heading.H6);
        Assert.Equal("blazor-lexical__ul", theme.List.Ul);
        Assert.Equal("blazor-lexical__ol", theme.List.Ol);
        Assert.Equal("blazor-lexical__li", theme.List.Listitem);
        Assert.Equal("blazor-lexical__nested-li", theme.List.Nested.Listitem);
        Assert.Equal("blazor-lexical__text-bold", theme.Text.Bold);
        Assert.Equal("blazor-lexical__text-superscript", theme.Text.Superscript);
    }
}
