using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of the generalized comment composer (L3): a custom
/// <c>ChildContent</c> form floated at the selection, with <c>OnOpened</c> and the C#-driven
/// <see cref="Blazor.Lexical.LexicalCommentComposer.ConfirmAsync"/> /
/// <see cref="Blazor.Lexical.LexicalCommentComposer.CancelAsync"/> replacing the built-in
/// textarea and buttons.
/// </summary>
public class CommentFormTests : HarnessTestBase
{
    public CommentFormTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/comment-form";

    protected override string ReadySelector =>
        "#editor-comment-form[data-lexical-editor='true']";

    private const string Content = "#editor-comment-form";
    private const string Root = ".harness-comment-form";

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

    private async Task OpenComposerAsync(IPage page)
    {
        await TypeAsync(page, Content, "hello formed world");
        await SelectRangeAsync(page, Content, 6, 6); // "formed"
        await page.ClickAsync($"{Root} .blazor-lexical__floating-toolbar [data-lexical-comment-compose]");
        await Expect(page.Locator($"{Root} #form-body")).ToBeVisibleAsync();
    }

    [Fact] // OnOpened carries the mark id and the selected text, so the form can populate itself
    public async Task Opened_CarriesMarkIdAndSelectedText()
    {
        var page = await OpenAsync();

        await OpenComposerAsync(page);

        await Expect(page.Locator("#form-opened-id")).ToHaveTextAsync("form-comment-1");
        await Expect(page.Locator("#form-opened-text")).ToHaveTextAsync("formed");
    }

    [Fact] // the form's own Save button drives ConfirmAsync: the span is wrapped and OnSubmit fires
    public async Task ConfirmAsync_WrapsMark_AndRaisesOnSubmit()
    {
        var page = await OpenAsync();

        await OpenComposerAsync(page);
        await page.Locator($"{Root} #form-body").FillAsync("needs a rewrite");
        await page.ClickAsync($"{Root} #form-confirm");

        await Expect(page.Locator("#form-confirm-result")).ToHaveTextAsync("True");
        var mark = page.Locator($"{Content} mark");
        await Expect(mark).ToHaveCountAsync(1);
        await Expect(mark).ToHaveTextAsync("formed");
        await Expect(page.Locator("#form-submit-id")).ToHaveTextAsync("form-comment-1");
        await Expect(page.Locator("#form-submit-count")).ToHaveTextAsync("1");
    }

    [Fact] // the form's own Discard button drives CancelAsync: nothing is wrapped
    public async Task CancelAsync_DismissesWithoutWrapping()
    {
        var page = await OpenAsync();

        await OpenComposerAsync(page);
        await page.ClickAsync($"{Root} #form-cancel");

        await Expect(page.Locator($"{Root} [data-lexical-comment-composer]"))
            .Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(page.Locator($"{Content} mark")).ToHaveCountAsync(0);
        await Expect(page.Locator("#form-cancel-count")).ToHaveTextAsync("1");
    }
}
