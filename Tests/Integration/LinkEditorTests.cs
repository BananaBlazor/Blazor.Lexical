using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Covers the playground-style link flow on <c>#editor-linkeditor</c>
/// (.harness-linkeditor), which has NO callbacks wired — proving the toolbar link
/// button and the floating <c>LexicalLinkEditor</c> popup are entirely client-side
/// (zero JS→.NET interop):
/// - the toolbar link button inserts a placeholder link and opens the popup in edit
///   mode; confirming writes the typed URL and switches the popup to its preview;
/// - the preview's edit button re-opens the form to change the URL in place;
/// - the preview's remove button (a <c>link:remove</c> command) unwraps the link.
/// </summary>
public class LinkEditorTests : HarnessTestBase
{
    private const string Url = "https://example.com/";
    private const string OtherUrl = "https://updated.example/";

    public LinkEditorTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/link-editor";

    protected override string ReadySelector =>
        "#editor-linkeditor[data-lexical-editor='true']";

    /// <summary>Selects some text, clicks the toolbar link button, and returns once
    /// the popup is open in edit mode over the freshly inserted placeholder link.</summary>
    private static async Task InsertLinkAsync(IPage page, string text)
    {
        await TypeAsync(page, "#editor-linkeditor", text);
        await page.Keyboard.PressAsync("Control+A");
        await page.ClickAsync(".harness-linkeditor [aria-label='Insert link']");
        await Expect(page.Locator(".harness-linkeditor [data-lexical-link-input]"))
            .ToBeVisibleAsync();
    }

    [Fact] // link button → edit form → confirm writes the URL and shows the preview
    public async Task LinkButton_InsertsLink_EditsUrl_AndPreviews()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator("#editor-linkeditor");

        await InsertLinkAsync(page, "clicky");

        await page.FillAsync(".harness-linkeditor [data-lexical-link-input]", Url);
        await page.ClickAsync(".harness-linkeditor [data-lexical-link-confirm]");

        var anchor = editor.Locator("a");
        await Expect(anchor).ToHaveCountAsync(1);
        await Expect(anchor).ToHaveAttributeAsync("href", Url);
        await Expect(anchor).ToHaveTextAsync("clicky");

        // The popup flips to its preview row, showing the URL as a clickable link.
        var preview = page.Locator(".harness-linkeditor [data-lexical-link-preview]");
        await Expect(preview).ToBeVisibleAsync();
        await Expect(preview).ToHaveTextAsync(Url);
        Assert.Empty(errors);
    }

    [Fact] // the preview's edit (pencil) button re-opens the form to change the URL
    public async Task EditButton_UpdatesExistingLinkUrl()
    {
        var page = await OpenAsync();
        var editor = page.Locator("#editor-linkeditor");

        await InsertLinkAsync(page, "editme");
        await page.FillAsync(".harness-linkeditor [data-lexical-link-input]", Url);
        await page.ClickAsync(".harness-linkeditor [data-lexical-link-confirm]");
        await Expect(editor.Locator("a")).ToHaveAttributeAsync("href", Url);

        // Re-open the editor from the preview and change the URL.
        await page.ClickAsync(".harness-linkeditor [data-lexical-link-edit]");
        await Expect(page.Locator(".harness-linkeditor [data-lexical-link-input]"))
            .ToBeVisibleAsync();
        await page.FillAsync(".harness-linkeditor [data-lexical-link-input]", OtherUrl);
        await page.ClickAsync(".harness-linkeditor [data-lexical-link-confirm]");

        await Expect(editor.Locator("a")).ToHaveCountAsync(1);
        await Expect(editor.Locator("a")).ToHaveAttributeAsync("href", OtherUrl);
    }

    [Fact] // the preview's remove button (link:remove command) unwraps the link
    public async Task RemoveButton_UnwrapsLink()
    {
        var page = await OpenAsync();
        var editor = page.Locator("#editor-linkeditor");

        await InsertLinkAsync(page, "goodbye");
        await page.FillAsync(".harness-linkeditor [data-lexical-link-input]", Url);
        await page.ClickAsync(".harness-linkeditor [data-lexical-link-confirm]");
        await Expect(editor.Locator("a")).ToHaveCountAsync(1);

        await page.ClickAsync(".harness-linkeditor [data-lexical-command='link:remove']");

        await Expect(editor.Locator("a")).ToHaveCountAsync(0);
        await Expect(editor).ToContainTextAsync("goodbye");
    }
}
