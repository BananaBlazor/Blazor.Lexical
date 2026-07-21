using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Exercises the list and link JS touchpoints Blazor calls: insertUnorderedList,
/// insertOrderedList, removeList, and toggleLink (set/remove). Also round-trips
/// list/link Markdown so a Lexical npm bump that breaks the @lexical/list or
/// @lexical/link node registration or their Markdown transformers is caught.
/// </summary>
public class ListAndLinkTests : HarnessTestBase
{
    private const string LinkUrl = "https://example.com/";

    public ListAndLinkTests(HarnessFixture fx) : base(fx) { }

    /// <summary>Types into an editor and selects all of it, so the following
    /// command applies to a real, non-empty selection.</summary>
    private static async Task TypeAndSelectAllAsync(IPage page, string selector, string text)
    {
        await TypeAsync(page, selector, text);
        await page.Keyboard.PressAsync("Control+A");
    }

    [Fact] // insertUnorderedList
    public async Task InsertUnorderedList_WrapsSelectionInBulletedList()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator("#editor-lists");

        await TypeAndSelectAllAsync(page, "#editor-lists", "list item text");
        await page.ClickAsync("#btn-ul");

        await Expect(editor.Locator("ul > li")).ToHaveCountAsync(1);
        await Expect(editor.Locator("ul > li")).ToHaveTextAsync("list item text");
        Assert.Empty(errors);
    }

    [Fact] // insertOrderedList
    public async Task InsertOrderedList_WrapsSelectionInNumberedList()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator("#editor-lists");

        await TypeAndSelectAllAsync(page, "#editor-lists", "numbered text");
        await page.ClickAsync("#btn-ol");

        await Expect(editor.Locator("ol > li")).ToHaveCountAsync(1);
        await Expect(editor.Locator("ol > li")).ToHaveTextAsync("numbered text");
        Assert.Empty(errors);
    }

    [Fact] // removeList
    public async Task RemoveList_ConvertsListBackToParagraph()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator("#editor-lists");

        await TypeAndSelectAllAsync(page, "#editor-lists", "toggle me");
        await page.ClickAsync("#btn-ul");
        await Expect(editor.Locator("ul")).ToHaveCountAsync(1);

        // Re-select the list content, then remove the list formatting.
        await page.ClickAsync("#editor-lists");
        await page.Keyboard.PressAsync("Control+A");
        await page.ClickAsync("#btn-list-remove");

        await Expect(editor.Locator("ul")).ToHaveCountAsync(0);
        await Expect(editor.Locator("ol")).ToHaveCountAsync(0);
        await Expect(editor.Locator("p")).ToContainTextAsync("toggle me");
    }

    [Fact] // setMarkdown + getMarkdown of a list -> exercises @lexical/list nodes
    public async Task ListMarkdown_RoundTrips()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator("#editor-lists");

        await page.ClickAsync("#btn-list-md"); // setMarkdown("- one\n- two\n- three")

        // Markdown import produced a real bulleted list in the DOM.
        await Expect(editor.Locator("ul > li")).ToHaveCountAsync(3);
        await Expect(editor.Locator("ul > li").Nth(0)).ToHaveTextAsync("one");
        await Expect(editor.Locator("ul > li").Nth(2)).ToHaveTextAsync("three");

        // ...and Markdown export renders it back as bullets.
        await page.ClickAsync("#btn-list-get-md");
        var result = page.Locator("#list-md-result");
        await Expect(result).ToContainTextAsync("- one");
        await Expect(result).ToContainTextAsync("- three");
    }

    [Fact] // toggleLink (set)
    public async Task SetLink_WrapsSelectionInAnchor()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator("#editor-links");

        await TypeAndSelectAllAsync(page, "#editor-links", "clicky");
        await page.ClickAsync("#btn-link-set");

        var anchor = editor.Locator("a");
        await Expect(anchor).ToHaveCountAsync(1);
        await Expect(anchor).ToHaveAttributeAsync("href", LinkUrl);
        await Expect(anchor).ToHaveTextAsync("clicky");
        Assert.Empty(errors);
    }

    [Fact] // toggleLink (remove)
    public async Task RemoveLink_UnwrapsAnchor()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator("#editor-links");

        await TypeAndSelectAllAsync(page, "#editor-links", "clicky");
        await page.ClickAsync("#btn-link-set");
        await Expect(editor.Locator("a")).ToHaveCountAsync(1);

        // Re-select the linked text, then unwrap it.
        await page.ClickAsync("#editor-links");
        await page.Keyboard.PressAsync("Control+A");
        await page.ClickAsync("#btn-link-remove");

        await Expect(editor.Locator("a")).ToHaveCountAsync(0);
        await Expect(editor).ToContainTextAsync("clicky");
    }

    [Fact] // setMarkdown + getMarkdown of a link -> exercises @lexical/link node
    public async Task LinkMarkdown_RoundTrips()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator("#editor-links");

        await page.ClickAsync("#btn-link-md"); // setMarkdown("see [Example](https://example.com/) now")

        var anchor = editor.Locator("a");
        await Expect(anchor).ToHaveCountAsync(1);
        await Expect(anchor).ToHaveAttributeAsync("href", LinkUrl);
        await Expect(anchor).ToHaveTextAsync("Example");

        await page.ClickAsync("#btn-link-get-md");
        await Expect(page.Locator("#link-md-result")).ToContainTextAsync($"[Example]({LinkUrl})");
    }
}
