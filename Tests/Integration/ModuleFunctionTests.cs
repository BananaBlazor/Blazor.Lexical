using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Exercises the JS module functions Blazor calls: create, getText, setText,
/// setEditable, dispose.
/// </summary>
public class ModuleFunctionTests : HarnessTestBase
{
    public ModuleFunctionTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/core";

    protected override string ReadySelector =>
        "#editor-disposable[data-lexical-editor='true']";

    [Fact] // create
    public async Task Create_MountsEditableLexicalSurface()
    {
        var page = await OpenAsync();

        await Expect(page.Locator("#editor-main")).ToHaveAttributeAsync("data-lexical-editor", "true");
        await Expect(page.Locator("#editor-main")).ToHaveAttributeAsync("contenteditable", "true");
    }

    [Fact] // getText
    public async Task GetText_ReturnsCurrentContent()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "hello getText");
        await page.ClickAsync("#btn-get");

        await Expect(page.Locator("#get-result")).ToHaveTextAsync("hello getText");
    }

    [Fact] // setText
    public async Task SetText_ReplacesDomContent_AndGetTextReflectsIt()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "will be replaced");
        await page.ClickAsync("#btn-set");

        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("Set via C#");

        await page.ClickAsync("#btn-get");
        await Expect(page.Locator("#get-result")).ToHaveTextAsync("Set via C#");
    }

    [Fact] // setEditable
    public async Task SetEditable_TogglesContentEditable_ViaReadOnlyParam()
    {
        var page = await OpenAsync();
        var toggle = page.Locator("#editor-toggle");

        await Expect(toggle).ToHaveAttributeAsync("contenteditable", "true");

        await page.ClickAsync("#btn-toggle");
        await Expect(toggle).ToHaveAttributeAsync("contenteditable", "false");

        await page.ClickAsync("#btn-toggle");
        await Expect(toggle).ToHaveAttributeAsync("contenteditable", "true");
    }

    [Fact] // dispose
    public async Task Dispose_RemovesEditor_WithoutErrors_AndCanReAdd()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await Expect(page.Locator("#editor-disposable")).ToHaveCountAsync(1);

        await page.ClickAsync("#btn-remove");
        await Expect(page.Locator("#editor-disposable")).ToHaveCountAsync(0);

        // Re-adding creates a fresh working instance.
        await page.ClickAsync("#btn-readd");
        await Expect(page.Locator("#editor-disposable[data-lexical-editor='true']")).ToHaveCountAsync(1);

        Assert.Empty(errors);
    }

    [Fact] // create keys instances by Id
    public async Task InstancesAreKeyedById_NoCrossTalk()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-set"); // main <- "Set via C#"
        await TypeAsync(page, "#editor-nohistory", "different content");

        await page.ClickAsync("#btn-get"); // reads main by its Id
        await Expect(page.Locator("#get-result")).ToHaveTextAsync("Set via C#");
    }

    [Fact] // getHtml
    public async Task GetHtml_SerializesContentToHtml()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "hello html");
        await page.ClickAsync("#btn-get-html");

        var result = page.Locator("#html-result");
        await Expect(result).ToContainTextAsync("hello html");
        await Expect(result).ToContainTextAsync("<p");
    }

    [Fact] // setHtml
    public async Task SetHtml_ReplacesContentFromHtml()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "will be replaced");
        await page.ClickAsync("#btn-set-html");

        var editor = page.Locator("#editor-main");
        await Expect(editor).ToContainTextAsync("HTML heading");
        await Expect(editor).ToContainTextAsync("bold");
        await Expect(editor.Locator("h1")).ToHaveTextAsync("HTML heading");
        await Expect(editor.Locator("strong")).ToHaveTextAsync("bold");
    }

    [Fact] // getMarkdown
    public async Task GetMarkdown_SerializesContentToMarkdown()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "hello markdown");
        await page.ClickAsync("#btn-get-markdown");

        await Expect(page.Locator("#markdown-result")).ToContainTextAsync("hello markdown");
    }

    [Fact] // setMarkdown
    public async Task SetMarkdown_ReplacesContentFromMarkdown()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "will be replaced");
        await page.ClickAsync("#btn-set-markdown");

        var editor = page.Locator("#editor-main");
        await Expect(editor.Locator("h1")).ToHaveTextAsync("Markdown heading");
        await Expect(editor.Locator("strong")).ToHaveTextAsync("bold");
    }

    [Fact] // getEditorStateJson
    public async Task GetEditorStateJson_SerializesCanonicalState()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "json state");
        await page.ClickAsync("#btn-get-json");

        var result = page.Locator("#json-result");
        await Expect(result).ToContainTextAsync("\"root\"");
        await Expect(result).ToContainTextAsync("json state");
    }

    [Fact] // setEditorStateJson (round-trips with getEditorStateJson)
    public async Task SetEditorStateJson_RestoresCapturedState()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "restore me");
        await page.ClickAsync("#btn-get-json"); // capture current state

        // Overwrite the editor with different content...
        await page.ClickAsync("#btn-set");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("Set via C#");

        // ...then restore the captured JSON state.
        await page.ClickAsync("#btn-set-json");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("restore me");
    }

    [Fact] // setEditorStateJson(silent): app-driven content, so no echo and no undo step
    public async Task SilentSetEditorStateJson_DoesNotRaiseTheContentChannel()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "restore me");
        await page.ClickAsync("#btn-get-json"); // capture current state

        await page.ClickAsync("#btn-set");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("Set via C#");

        // Let the overwrite's own (non-silent) push settle before taking the baseline.
        await page.WaitForTimeoutAsync(400);
        var before = await page.Locator("#change-count").TextContentAsync();

        await page.ClickAsync("#btn-set-json-silent");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("restore me");

        // Well past the content debounce: the count must not have moved.
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#change-count")).ToHaveTextAsync(before!);

        // The same restore without `silent` does push — so the assertion above is the
        // silence talking, not a channel that never fires on this path.
        await page.ClickAsync("#btn-set");
        await page.ClickAsync("#btn-set-json");
        await Expect(page.Locator("#change-count")).Not.ToHaveTextAsync(before!);
    }

    [Fact] // a silent restore merges into history: undo skips it, landing on the overwrite
    public async Task SilentSetEditorStateJson_AddsNoUndoStep()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "restore me");
        await page.ClickAsync("#btn-get-json");

        await page.ClickAsync("#btn-set");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("Set via C#");

        await page.ClickAsync("#btn-set-json-silent");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("restore me");

        // The silent apply is not its own undoable step, so undo goes past it to the
        // state before the overwrite rather than back to "Set via C#".
        await page.Locator("#editor-main").ClickAsync();
        await page.Keyboard.PressAsync("Control+z");
        await Expect(page.Locator("#editor-main")).Not.ToHaveTextAsync("Set via C#");

        // Same sequence without `silent` — proving undo is live here and that the
        // difference above is the missing history entry, not a no-op undo.
        await page.ClickAsync("#btn-set");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("Set via C#");
        await page.ClickAsync("#btn-set-json");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("restore me");

        await page.Locator("#editor-main").ClickAsync();
        await page.Keyboard.PressAsync("Control+z");
        await Expect(page.Locator("#editor-main")).ToHaveTextAsync("Set via C#");
    }
}
