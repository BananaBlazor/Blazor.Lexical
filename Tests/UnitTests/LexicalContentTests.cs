using Blazor.Lexical;

namespace Tests.UnitTests;

/// <summary>
/// Locks the shape of the content value the editor's <c>InitialContent</c> parameter
/// takes. The point of the type is that a document and the format it is written in
/// travel together, so "which format won" can never be ambiguous — these assert the
/// factories tag their format and keep the text verbatim. Loading each format into a
/// real editor is covered end-to-end by Tests.Integration's <c>ParameterTests</c>.
/// </summary>
public class LexicalContentTests
{
    [Fact]
    public void Factories_TagTheMatchingFormat()
    {
        Assert.Equal(LexicalContentFormat.Text, LexicalContent.FromText("x").Format);
        Assert.Equal(LexicalContentFormat.Html, LexicalContent.FromHtml("x").Format);
        Assert.Equal(LexicalContentFormat.Markdown, LexicalContent.FromMarkdown("x").Format);
        Assert.Equal(
            LexicalContentFormat.EditorStateJson,
            LexicalContent.FromEditorStateJson("x").Format);
    }

    [Fact]
    public void Factories_KeepTheTextVerbatim()
    {
        const string html = "<p>Hello <strong>world</strong></p>";
        Assert.Equal(html, LexicalContent.FromHtml(html).Text);
    }

    [Fact]
    public void Factories_RejectNullText()
    {
        Assert.Throws<ArgumentNullException>(() => LexicalContent.FromHtml(null!));
        Assert.Throws<ArgumentNullException>(() => LexicalContent.FromText(null!));
        Assert.Throws<ArgumentNullException>(() => LexicalContent.FromMarkdown(null!));
        Assert.Throws<ArgumentNullException>(() => LexicalContent.FromEditorStateJson(null!));
    }

    /// <summary>Value semantics: same format + same text is the same content.</summary>
    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(LexicalContent.FromHtml("<p>a</p>"), LexicalContent.FromHtml("<p>a</p>"));
        Assert.NotEqual(LexicalContent.FromHtml("a"), LexicalContent.FromText("a"));
    }
}
