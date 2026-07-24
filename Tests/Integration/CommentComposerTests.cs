using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalCommentComposer&gt;</c> — the floating in-editor
/// comment input. <c>#editor-comments</c> pairs it with a <c>NewMarkId</c> factory and
/// both callbacks; <c>#editor-comments-mint</c> supplies no factory, so the composer mints
/// a UUIDv7 the app still learns through <c>OnSubmit</c>.
/// </summary>
/// <remarks>
/// Selector convention (as in <see cref="LinkEditorTests"/>): the editor <c>Id</c> sits on
/// the contenteditable, so <c>#editor-comments</c> holds the document (and the mark nodes),
/// while the composer box, its input/buttons, and the add-comment buttons are overlays under
/// the editor's <c>CssClass</c> root (<c>.harness-comments</c>).
/// </remarks>
public class CommentComposerTests : HarnessTestBase
{
    public CommentComposerTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/comments";

    protected override string ReadySelector =>
        "#editor-comments-mint[data-lexical-editor='true']";

    private const string Content = "#editor-comments";
    private const string Root = ".harness-comments";
    private const string MintContent = "#editor-comments-mint";
    private const string MintRoot = ".harness-comments-mint";

    /// <summary>
    /// Selects <paramref name="length"/> characters from <paramref name="start"/> on the
    /// content element's first line, driven from the keyboard so Lexical adopts the
    /// selection (a synthetic DOM Range is not something it wraps).
    /// </summary>
    private static async Task SelectRangeAsync(IPage page, string content, int start, int length)
    {
        await page.ClickAsync(content);
        await page.Keyboard.PressAsync("Home");
        for (var i = 0; i < start; i++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }
        for (var i = 0; i < length; i++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }
    }

    [Fact] // the whole flow: add-comment button → type → confirm wraps a mark + raises OnSubmit
    public async Task AddCommentButton_WrapsAMark_AndRaisesOnSubmit_WithTheFactoryId()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Content, "hello comment world");
        await SelectRangeAsync(page, Content, 6, 7); // "comment"

        // The add-comment button lives in the floating toolbar, shown over the selection.
        await page.ClickAsync($"{Root} .blazor-lexical__floating-toolbar [data-lexical-comment-compose]");

        var input = page.Locator($"{Root} [data-lexical-comment-input]");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync("looks good");
        await page.ClickAsync($"{Root} [data-lexical-comment-confirm]");

        // The commented span became a <mark> (in the content) carrying the factory's id.
        var mark = page.Locator($"{Content} mark");
        await Expect(mark).ToHaveCountAsync(1);
        await Expect(mark).ToHaveTextAsync("comment");

        await Expect(page.Locator("#comment-submit-id")).ToHaveTextAsync("comment-1");
        await Expect(page.Locator("#comment-submit-text")).ToHaveTextAsync("looks good");
        await Expect(page.Locator("#comment-submit-count")).ToHaveTextAsync("1");
    }

    [Fact] // Ctrl+Enter is the keyboard confirm
    public async Task CtrlEnter_ConfirmsTheComment()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Content, "hello comment world");
        await SelectRangeAsync(page, Content, 6, 7);
        await page.ClickAsync($"{Root} .blazor-lexical__floating-toolbar [data-lexical-comment-compose]");

        var input = page.Locator($"{Root} [data-lexical-comment-input]");
        await input.FillAsync("via keyboard");
        await input.PressAsync("Control+Enter");

        await Expect(page.Locator($"{Content} mark")).ToHaveCountAsync(1);
        await Expect(page.Locator("#comment-submit-text")).ToHaveTextAsync("via keyboard");
    }

    [Fact] // Escape cancels: OnCancel fires and nothing is wrapped
    public async Task Escape_Cancels_WithoutWrapping()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Content, "hello comment world");
        await SelectRangeAsync(page, Content, 6, 7);
        await page.ClickAsync($"{Root} .blazor-lexical__floating-toolbar [data-lexical-comment-compose]");

        var input = page.Locator($"{Root} [data-lexical-comment-input]");
        await Expect(input).ToBeVisibleAsync();
        await input.PressAsync("Escape");

        await Expect(page.Locator($"{Root} [data-lexical-comment-composer]"))
            .Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(page.Locator($"{Content} mark")).ToHaveCountAsync(0);
        await Expect(page.Locator("#comment-cancel-count")).ToHaveTextAsync("1");
        await Expect(page.Locator("#comment-submit-count")).ToHaveTextAsync("0");
    }

    [Fact] // OpenAsync(markId) opens over the current selection and wraps with that id
    public async Task OpenAsync_UsesTheSuppliedMarkId()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Content, "hello comment world");
        await SelectRangeAsync(page, Content, 6, 7);

        await page.ClickAsync("#btn-comment-open");

        var input = page.Locator($"{Root} [data-lexical-comment-input]");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync("api opened");
        await page.ClickAsync($"{Root} [data-lexical-comment-confirm]");

        await Expect(page.Locator($"{Content} mark")).ToHaveCountAsync(1);
        await Expect(page.Locator("#comment-submit-id")).ToHaveTextAsync("open-id");
    }

    [Fact] // with no factory the composer mints a UUIDv7, reported back through OnSubmit
    public async Task NoFactory_MintsAUuidV7_AndReportsItThroughOnSubmit()
    {
        var page = await OpenAsync();

        await TypeAsync(page, MintContent, "hello comment world");
        await SelectRangeAsync(page, MintContent, 6, 7);
        await page.ClickAsync($"{MintRoot} .blazor-lexical__floating-toolbar [data-lexical-comment-compose]");

        var input = page.Locator($"{MintRoot} [data-lexical-comment-input]");
        await input.FillAsync("minted");
        await page.ClickAsync($"{MintRoot} [data-lexical-comment-confirm]");

        await Expect(page.Locator($"{MintContent} mark")).ToHaveCountAsync(1);
        // A canonical UUIDv7: version nibble 7, variant nibble 8/9/a/b.
        await Expect(page.Locator("#comment-mint-id")).ToHaveTextAsync(
            new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"));
    }

    [Fact] // the fixed-toolbar add button is disabled while the selection is collapsed
    public async Task AddCommentButton_IsDisabled_WhenSelectionIsCollapsed()
    {
        var page = await OpenAsync();
        var button = page.Locator($"{Root} .comment-add-fixed");

        // Collapsed caret (just typed): nothing to comment on.
        await TypeAsync(page, Content, "hello comment world");
        await Expect(button).ToHaveAttributeAsync("data-lexical-disabled", "");

        // A real selection enables it.
        await SelectRangeAsync(page, Content, 6, 7);
        await Expect(button).Not.ToHaveAttributeAsync("data-lexical-disabled", "");
    }

    [Fact] // the target stays visible while composing — the CSS Custom Highlight is registered
    public async Task ComposeHighlight_IsPainted_WhileTheBoxIsOpen()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Content, "hello comment world");
        await SelectRangeAsync(page, Content, 6, 7);
        await page.ClickAsync($"{Root} .blazor-lexical__floating-toolbar [data-lexical-comment-compose]");
        await Expect(page.Locator($"{Root} [data-lexical-comment-input]")).ToBeVisibleAsync();

        var painted = await page.EvaluateAsync<bool>(
            "() => CSS.highlights.has('blazor-lexical-comment-compose')");
        Assert.True(painted);

        // Cancelling clears it.
        await page.Keyboard.PressAsync("Escape");
        var cleared = await page.EvaluateAsync<bool>(
            "() => CSS.highlights.has('blazor-lexical-comment-compose')");
        Assert.False(cleared);
    }

    [Fact] // invariant #1: OpenAsync is C#→JS, so composing raises no unexpected console errors
    public async Task Composer_OpensAndWraps_WithNoConsoleErrors()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, MintContent, "hello comment world");
        await SelectRangeAsync(page, MintContent, 6, 7);
        await page.ClickAsync($"{MintRoot} .blazor-lexical__floating-toolbar [data-lexical-comment-compose]");
        await page.Locator($"{MintRoot} [data-lexical-comment-input]").FillAsync("x");
        await page.ClickAsync($"{MintRoot} [data-lexical-comment-confirm]");

        await Expect(page.Locator($"{MintContent} mark")).ToHaveCountAsync(1);
        Assert.Empty(errors);
    }
}
