using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Exercises the JS -> .NET callback path: the debounced update listener calls
/// OnContentChangedInternal, which raises the OnContentChanged EventCallback.
/// </summary>
public class CallbackTests : HarnessTestBase
{
    public CallbackTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/callbacks";

    protected override string ReadySelector =>
        "#editor-payload-html[data-lexical-editor='true']";

    [Fact]
    public async Task Typing_RaisesOnContentChanged_WithText()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "callback fired");

        await Expect(page.Locator("#change-log")).ToHaveTextAsync("callback fired");
    }

    [Fact]
    public async Task SetText_AlsoRaisesOnContentChanged()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-set");

        await Expect(page.Locator("#change-log")).ToHaveTextAsync("Set via C#");
    }

    [Fact]
    public async Task ChangeCallback_IsDebounced_NotOncePerKeystroke()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-main", "abcdefghij"); // 10 keystrokes
        await Expect(page.Locator("#change-log")).ToHaveTextAsync("abcdefghij");

        // Debounced: far fewer callbacks than keystrokes.
        var countText = await page.Locator("#change-count").TextContentAsync();
        var count = int.Parse(countText!);
        Assert.InRange(count, 1, 5);
    }

    /// <summary>
    /// SignalOnly keeps the channel but drops the payload: the subscriber learns that
    /// the document changed without the document crossing the bridge on every tick.
    /// </summary>
    [Fact]
    public async Task SignalOnlyPayload_FiresTheCallback_WithNoText()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-signal", "not shipped");

        // The callback fired…
        await Expect(page.Locator("#signal-count")).Not.ToHaveTextAsync("0");
        // …carrying an empty argument rather than the text just typed.
        await Expect(page.Locator("#signal-arg-length")).ToHaveTextAsync("0");
    }

    /// <summary>
    /// Declaring a format is what keeps the channel to one crossing: the push already
    /// carries HTML, so a handler that wants HTML never follows up with a GetHtmlAsync.
    /// </summary>
    [Fact]
    public async Task HtmlPayload_PushesSerializedHtml_WithItsFormat()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-payload-html", "pushed as html");

        // The format rides along on the value…
        await Expect(page.Locator("#payload-html-format")).ToHaveTextAsync("Html");
        // …and the payload is markup, not the plain text the default mode would send.
        await Expect(page.Locator("#payload-html-text")).ToContainTextAsync("<p");
        await Expect(page.Locator("#payload-html-text")).ToContainTextAsync("pushed as html");
    }

    /// <summary>There is no document to cache in SignalOnly mode, so the cache stays null.</summary>
    [Fact]
    public async Task SignalOnlyPayload_LeavesLastContentNull()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-signal", "not shipped");
        await Expect(page.Locator("#signal-count")).Not.ToHaveTextAsync("0");

        await page.ClickAsync("#btn-signal-last");
        await Expect(page.Locator("#signal-last")).ToHaveTextAsync("(null)");
    }

    /// <summary>
    /// The cheap read for hosts already on the content channel: the last pushed
    /// document — format included — with no interop call of its own.
    /// </summary>
    [Fact]
    public async Task LastContent_HoldsTheMostRecentPush()
    {
        var page = await OpenAsync();

        await TypeAsync(page, "#editor-notify", "cached text");
        await Expect(page.Locator("#notify-change")).ToHaveTextAsync("cached text");

        await page.ClickAsync("#btn-notify-last");
        await Expect(page.Locator("#notify-last")).ToHaveTextAsync("Text|cached text");
    }
}
