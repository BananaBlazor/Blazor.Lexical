using Blazor.Lexical;

namespace Tests.UnitTests;

/// <summary>
/// The enum↔token helpers are part of the public SDK: a custom toolbar button writes
/// <c>data-lexical-command="format:{LexicalTextFormat.Bold.ToJsToken()}"</c> rather
/// than hardcoding the string. These lock the tokens to the ones the JS glue's command
/// switch accepts (documented in Source/Blazor.Lexical/CLAUDE.md) — a rename on either side
/// would break custom chrome silently otherwise.
/// </summary>
public class LexicalTokenTests
{
    [Theory]
    [InlineData(LexicalTextFormat.Bold, "bold")]
    [InlineData(LexicalTextFormat.Italic, "italic")]
    [InlineData(LexicalTextFormat.Underline, "underline")]
    [InlineData(LexicalTextFormat.Strikethrough, "strikethrough")]
    [InlineData(LexicalTextFormat.Code, "code")]
    [InlineData(LexicalTextFormat.Subscript, "subscript")]
    [InlineData(LexicalTextFormat.Superscript, "superscript")]
    [InlineData(LexicalTextFormat.Lowercase, "lowercase")]
    [InlineData(LexicalTextFormat.Uppercase, "uppercase")]
    public void TextFormat_RoundTripsThroughItsToken(LexicalTextFormat format, string token)
    {
        Assert.Equal(token, format.ToJsToken());
        Assert.Equal(format, LexicalTextFormatExtensions.FromJsToken(token));
    }

    /// <summary>Unlike block/alignment, there is no "no format" member to fall back to.</summary>
    [Fact]
    public void TextFormat_FromUnknownToken_IsNull()
    {
        Assert.Null(LexicalTextFormatExtensions.FromJsToken("bolder"));
        Assert.Null(LexicalTextFormatExtensions.FromJsToken(null));
    }

    [Theory]
    [InlineData(LexicalBlockType.Paragraph, "paragraph")]
    [InlineData(LexicalBlockType.Heading1, "h1")]
    [InlineData(LexicalBlockType.Heading6, "h6")]
    [InlineData(LexicalBlockType.Quote, "quote")]
    public void BlockType_RoundTripsThroughItsToken(LexicalBlockType type, string token)
    {
        Assert.Equal(token, type.ToJsToken());
        Assert.Equal(type, LexicalBlockTypeExtensions.FromJsToken(token));
    }

    /// <summary>Lists ride the <c>list:</c> command, so they have no block token.</summary>
    [Fact]
    public void BlockType_Lists_HaveNoBlockToken_ButStillParse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LexicalBlockType.BulletList.ToJsToken());
        Assert.Equal(LexicalBlockType.BulletList, LexicalBlockTypeExtensions.FromJsToken("bullet"));
        Assert.Equal(LexicalBlockType.NumberList, LexicalBlockTypeExtensions.FromJsToken("number"));
    }

    [Fact]
    public void BlockType_FromUnknownToken_FallsBackToParagraph() =>
        Assert.Equal(LexicalBlockType.Paragraph, LexicalBlockTypeExtensions.FromJsToken("h7"));

    [Theory]
    [InlineData(LexicalAlignment.Left, "left")]
    [InlineData(LexicalAlignment.Center, "center")]
    [InlineData(LexicalAlignment.Right, "right")]
    [InlineData(LexicalAlignment.Justify, "justify")]
    public void Alignment_RoundTripsThroughItsToken(LexicalAlignment alignment, string token)
    {
        Assert.Equal(token, alignment.ToJsToken());
        Assert.Equal(alignment, LexicalAlignmentExtensions.FromJsToken(token));
    }

    /// <summary>The empty token is how "clear the alignment" crosses the boundary.</summary>
    [Fact]
    public void Alignment_None_IsTheEmptyToken()
    {
        Assert.Equal("", LexicalAlignment.None.ToJsToken());
        Assert.Equal(LexicalAlignment.None, LexicalAlignmentExtensions.FromJsToken(""));
    }
}
