using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of the read-only review experience (L1): selecting text in a
/// <c>ReadOnly</c> editor to annotate it. Lexical does not sync <c>$getSelection()</c> when
/// <c>editable=false</c>, so the composer and mark-wrap fall back to
/// <c>window.getSelection()</c> — this class proves that fallback end to end.
/// </summary>
/// <remarks>
/// The editor <c>Id</c> sits on the contenteditable (<c>#editor-readonly-annotate</c>, which
/// holds the mark nodes); the composer and floating toolbar are overlays under the editor's
/// <c>CssClass</c> root (<c>.harness-readonly-annotate</c>).
/// </remarks>
public class ReadOnlyAnnotateTests : HarnessTestBase
{
    public ReadOnlyAnnotateTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/readonly-annotate";

    protected override string ReadySelector =>
        "#editor-readonly-annotate[data-lexical-editor='true']";

    private const string Content = "#editor-readonly-annotate";
    private const string Root = ".harness-readonly-annotate";

    /// <summary>
    /// Selects the word <paramref name="word"/> in the read-only content by placing a real
    /// browser selection over it — exactly what a reviewer's mouse drag produces, and what the
    /// read-only fallback reads. A keyboard selection is not available here: a read-only
    /// contenteditable has no caret to extend.
    /// </summary>
    private static async Task<bool> SelectWordAsync(IPage page, string word)
    {
        return await page.EvaluateAsync<bool>(
            @"(args) => {
                const content = document.querySelector(args.content);
                if (content === null) { return false; }
                const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT);
                let node;
                while ((node = walker.nextNode())) {
                    const idx = node.data.indexOf(args.word);
                    if (idx !== -1) {
                        const range = document.createRange();
                        range.setStart(node, idx);
                        range.setEnd(node, idx + args.word.length);
                        const sel = window.getSelection();
                        sel.removeAllRanges();
                        sel.addRange(range);
                        document.dispatchEvent(new Event('selectionchange'));
                        return true;
                    }
                }
                return false;
            }",
            new { content = Content, word });
    }

    [Fact] // the floating toolbar shows over a selection even in a read-only editor
    public async Task Selection_ShowsFloatingToolbar_ReadOnly()
    {
        var page = await OpenAsync();

        Assert.True(await SelectWordAsync(page, "flagship"));

        await Expect(page.Locator($"{Root} .blazor-lexical__floating-toolbar"))
            .ToHaveAttributeAsync("data-lexical-visible", "");
    }

    [Fact] // the whole L1 flow: read-only select → toolbar → composer → confirm → mark; click reports it
    public async Task ReadOnly_AddComment_WrapsMark_AndOnMarkClickedFires()
    {
        var page = await OpenAsync();

        Assert.True(await SelectWordAsync(page, "flagship"));

        // The add-comment button in the floating toolbar opens the composer over the
        // read-only selection (the factory mints the deterministic id).
        await page.ClickAsync($"{Root} .blazor-lexical__floating-toolbar [data-lexical-comment-compose]");

        var input = page.Locator($"{Root} [data-lexical-comment-input]");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync("please revisit");
        await page.ClickAsync($"{Root} [data-lexical-comment-confirm]");

        // The read-only selection was wrapped in a mark carrying the factory id.
        var mark = page.Locator($"{Content} mark");
        await Expect(mark).ToHaveCountAsync(1);
        await Expect(mark).ToHaveTextAsync("flagship");
        await Expect(page.Locator("#ro-submit-id")).ToHaveTextAsync("comment-1");
        await Expect(page.Locator("#ro-submit-text")).ToHaveTextAsync("please revisit");

        // Clicking the mark reaches OnMarkClicked — click fires even when not editable.
        await mark.ClickAsync();
        await Expect(page.Locator("#ro-mark-clicked")).ToHaveTextAsync("comment-1");
    }

    [Fact] // WrapSelectionInMarkAsync works read-only via the window.getSelection fallback
    public async Task ReadOnly_WrapSelection_CreatesMark()
    {
        var page = await OpenAsync();

        Assert.True(await SelectWordAsync(page, "flagship"));
        await page.ClickAsync("#btn-ro-wrap");

        await Expect(page.Locator("#ro-wrap-result")).ToHaveTextAsync("True");
        await Expect(page.Locator($"{Content} mark")).ToHaveTextAsync("flagship");
    }
}
