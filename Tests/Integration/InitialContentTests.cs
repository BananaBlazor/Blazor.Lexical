using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Covers <c>InitialContent</c> — the no-<c>@ref</c> way to preload a document. The
/// content is applied inside the JS <c>create()</c> call rather than by a post-ready
/// round trip, which is what buys the three guarantees asserted here: every format
/// parses, the editor is never painted empty first, and the preloaded document is the
/// history baseline rather than an undoable step.
/// </summary>
public class InitialContentTests : HarnessTestBase
{
    public InitialContentTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/initial-content";

    protected override string ReadySelector =>
        "#editor-initial-json[data-lexical-editor='true']";

    [Fact]
    public async Task Html_LoadsWithMarkup()
    {
        var page = await OpenAsync();

        await Expect(page.Locator("#editor-initial-html")).ToContainTextAsync("Initial html content");
        await Expect(page.Locator("#editor-initial-html strong")).ToHaveTextAsync("html");
    }

    [Fact]
    public async Task Text_LoadsAsASingleParagraph()
    {
        var page = await OpenAsync();

        await Expect(page.Locator("#editor-initial-text")).ToContainTextAsync("Initial text content");
        await Expect(page.Locator("#editor-initial-text p")).ToHaveCountAsync(1);
    }

    [Fact] // The markdown chunk is fetched inside create() before the first paint.
    public async Task Markdown_LoadsAsRichNodes()
    {
        var page = await OpenAsync();

        await Expect(page.Locator("#editor-initial-md h2"))
            .ToHaveTextAsync("Initial markdown heading");
    }

    [Fact]
    public async Task EditorStateJson_LoadsVerbatim()
    {
        var page = await OpenAsync();

        await Expect(page.Locator("#editor-initial-json"))
            .ToContainTextAsync("Initial state json content");
    }

    /// <summary>
    /// The empty-state flag drives the placeholder, so its absence is what "no flash of
    /// an empty editor" means in DOM terms: the loaded document is present in the same
    /// frame the editor first paints in.
    /// </summary>
    [Fact]
    public async Task PreloadedEditor_IsNeverMarkedEmpty()
    {
        var page = await OpenAsync();

        await Expect(page.Locator("#editor-initial-html"))
            .Not.ToHaveAttributeAsync("data-lexical-empty", "");
    }

    /// <summary>
    /// Initial content is applied before history is registered, so there is no earlier
    /// state to return to — undo must leave the document alone rather than emptying it.
    /// </summary>
    [Fact]
    public async Task Undo_DoesNotEraseInitialContent()
    {
        var page = await OpenAsync();
        var editor = page.Locator("#editor-initial-json");

        await page.ClickAsync("#btn-initial-undo");
        await Expect(editor).ToContainTextAsync("Initial state json content");

        // Also via the keyboard, which goes through the same history plugin.
        await editor.ClickAsync();
        await page.Keyboard.PressAsync("Control+z");
        await Expect(editor).ToContainTextAsync("Initial state json content");
    }

    /// <summary>
    /// The generic pair carries the format as a value, so a document read in a format
    /// chosen at runtime can be written straight back without a caller-side switch.
    /// </summary>
    [Fact]
    public async Task GetAndSetContentAsync_RoundTripTheDocument()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-content-roundtrip");

        // The format travels with the text…
        await Expect(page.Locator("#content-roundtrip"))
            .ToContainTextAsync("Markdown|Initial state json content");
        // …and writing it back leaves the document intact.
        await Expect(page.Locator("#editor-initial-json"))
            .ToContainTextAsync("Initial state json content");
    }

    [Fact] // IsReady complements OnReady: a property to read in guard clauses.
    public async Task IsReady_IsTrueOnceTheEditorExists()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-initial-ready");

        await Expect(page.Locator("#initial-ready")).ToHaveTextAsync("True");
    }
}
