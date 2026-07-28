using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalSuggestions&gt;</c> (L4) — the display-only inline
/// diff. <c>#editor-suggestions-notify</c> wires <c>OnSuggestionClicked</c> (opt-in interop);
/// <c>#editor-suggestions-quiet</c> wires nothing and still renders the diff from C#. Both are
/// read-only, the review use case.
/// </summary>
public class SuggestionTests : HarnessTestBase
{
    public SuggestionTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/suggestions";

    protected override string ReadySelector =>
        "#editor-suggestions-quiet[data-lexical-editor='true']";

    private const string NotifyContent = "#editor-suggestions-notify";
    private const string QuietContent = "#editor-suggestions-quiet";
    private const string Deletion = ".blazor-lexical__suggestion-deletion";
    private const string Insertion = ".blazor-lexical__suggestion-insertion";

    [Fact] // the target renders struck-through and the proposal is inserted after it
    public async Task ShowSuggestion_RendersDeletionAndInsertion()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-sg-show");

        await Expect(page.Locator("#sg-show-result")).ToHaveTextAsync("True");

        var deletion = page.Locator($"{NotifyContent} {Deletion}");
        await Expect(deletion).ToHaveCountAsync(1);
        await Expect(deletion).ToHaveTextAsync("quick brown fox");
        await Expect(deletion).ToHaveAttributeAsync("data-suggestion-id", "sg-1");

        var insertion = page.Locator($"{NotifyContent} {Insertion}");
        await Expect(insertion).ToHaveCountAsync(1);
        await Expect(insertion).ToHaveTextAsync("swift red fox");
        await Expect(insertion).ToHaveAttributeAsync("data-suggestion-id", "sg-1");

        // The original words are gone as plain text; the surrounding text stays.
        await Expect(page.Locator(NotifyContent)).ToContainTextAsync("jumps over the lazy dog");
    }

    [Fact] // a missing target reports false and paints nothing
    public async Task ShowMissingTarget_ReturnsFalse()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-sg-show-missing");

        await Expect(page.Locator("#sg-show-result")).ToHaveTextAsync("False");
        await Expect(page.Locator($"{NotifyContent} {Deletion}")).ToHaveCountAsync(0);
        await Expect(page.Locator($"{NotifyContent} {Insertion}")).ToHaveCountAsync(0);
    }

    [Fact] // clicking either half reaches the opt-in handler with the app-owned id
    public async Task ClickSuggestion_InvokesDotNet_WhenHandlerIsWired()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-sg-show");
        await Expect(page.Locator($"{NotifyContent} {Insertion}")).ToHaveCountAsync(1);

        await page.ClickAsync($"{NotifyContent} {Insertion}");
        await Expect(page.Locator("#sg-clicked")).ToHaveTextAsync("sg-1");
    }

    [Fact] // removing a suggestion restores the struck text to plain prose
    public async Task RemoveSuggestion_RestoresTheOriginalText()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-sg-show");
        await Expect(page.Locator($"{NotifyContent} {Deletion}")).ToHaveCountAsync(1);

        await page.ClickAsync("#btn-sg-remove");

        // Deletion + insertion cleared (two nodes), original text back in place.
        await Expect(page.Locator("#sg-remove-count")).ToHaveTextAsync("2");
        await Expect(page.Locator($"{NotifyContent} {Deletion}")).ToHaveCountAsync(0);
        await Expect(page.Locator($"{NotifyContent} {Insertion}")).ToHaveCountAsync(0);
        await Expect(page.Locator(NotifyContent)).ToContainTextAsync("quick brown fox");
    }

    [Fact] // clear removes every suggestion at once
    public async Task ClearSuggestions_RemovesAll()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#btn-sg-show");
        await page.ClickAsync("#btn-sg-show2");
        await Expect(page.Locator($"{NotifyContent} {Deletion}")).ToHaveCountAsync(2);

        await page.ClickAsync("#btn-sg-clear");

        await Expect(page.Locator($"{NotifyContent} {Deletion}")).ToHaveCountAsync(0);
        await Expect(page.Locator($"{NotifyContent} {Insertion}")).ToHaveCountAsync(0);
    }

    [Fact] // invariant #1: a suggestions editor with no callback performs zero JS→.NET calls
    public async Task Suggestions_DoNoInterop_WhenNoCallbackIsWired()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await page.ClickAsync("#btn-sg-quiet-show");
        await Expect(page.Locator($"{QuietContent} {Insertion}")).ToHaveTextAsync("swift red fox");

        // Clicking a suggestion would push on the notify editor; here it must not.
        await page.ClickAsync($"{QuietContent} {Insertion}");
        await Expect(page.Locator("#sg-clicked")).ToHaveTextAsync("");
        Assert.Empty(errors);
    }
}
