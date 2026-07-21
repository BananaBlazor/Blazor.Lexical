using System.Text.Json;
using Blazor.Lexical;

namespace Tests.UnitTests;

/// <summary>
/// Locks the wire shape of the outline and the statistics snapshot. Both arrive as JSON
/// built by hand in TypeScript, so the only thing keeping the two halves aligned is that
/// these exact camelCase names parse — including the recursive <c>children</c> arm, which
/// is the one part a source-generated context could plausibly get wrong.
/// </summary>
public class LexicalTocEntryTests
{
    [Fact]
    public void Toc_json_round_trips_through_the_source_generated_context()
    {
        // Shaped exactly as js/src/toc.ts emits it.
        const string json = """
            [
              {"anchorId":"intro","level":1,"text":"Intro","children":[
                {"anchorId":"background","level":2,"text":"Background","children":[]},
                {"anchorId":"background-2","level":2,"text":"Background","children":[]}
              ]}
            ]
            """;

        var entries = JsonSerializer.Deserialize(
            json, LexicalJsonSerializerContext.Default.LexicalTocEntryArray);

        var root = Assert.Single(entries!);
        Assert.Equal("intro", root.AnchorId);
        Assert.Equal(1, root.Level);
        Assert.Equal("Intro", root.Text);
        Assert.Equal(2, root.Children.Count);
        // The dedupe suffix is part of the anchor, not decoration on the text.
        Assert.Equal("background", root.Children[0].AnchorId);
        Assert.Equal("background-2", root.Children[1].AnchorId);
        Assert.Equal("Background", root.Children[1].Text);
        Assert.Empty(root.Children[0].Children);
    }

    [Fact]
    public void Stats_json_round_trips_through_the_source_generated_context()
    {
        const string json = """
            {"characters":12,"charactersNoSpaces":10,"words":2,"paragraphs":1,"readingMinutes":1}
            """;

        var stats = JsonSerializer.Deserialize(
            json, LexicalJsonSerializerContext.Default.LexicalDocumentStats);

        Assert.Equal(new LexicalDocumentStats(12, 10, 2, 1, 1), stats);
    }

    [Fact]
    public void Block_ref_json_round_trips_through_the_source_generated_context()
    {
        const string json = """
            {"nodeKey":"7","index":2,"blockType":"h2","textPreview":"Background"}
            """;

        var block = JsonSerializer.Deserialize(
            json, LexicalJsonSerializerContext.Default.LexicalBlockRef);

        Assert.Equal(new LexicalBlockRef("7", 2, "h2", "Background"), block);
    }
}
