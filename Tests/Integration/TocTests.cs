using static Microsoft.Playwright.Assertions;
using Microsoft.Playwright;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalToc /&gt;</c>, driven as a pair so the opt-in
/// invariant is proved rather than assumed:
/// <list type="bullet">
/// <item><c>#editor-toc-notify</c> wires <c>OnTocChanged</c> <i>and</i> a render target,
/// so both surfaces run.</item>
/// <item><c>#editor-toc-quiet</c> renders into its target with no callback: anchors and
/// list appear, zero JS→.NET calls.</item>
/// </list>
/// </summary>
public class TocTests : HarnessTestBase
{
    public TocTests(HarnessFixture fx) : base(fx) { }

    private const string NotifyEditor = "#editor-toc-notify";
    private const string QuietEditor = "#editor-toc-quiet";

    /// <summary>Types a two-level outline: one h1 with two h2s beneath it.</summary>
    private static async Task TypeOutlineAsync(IPage page, string editor)
    {
        // Type each line first, THEN convert it: the block type is applied at the
        // selection, and a freshly created editor has no caret until something is typed
        // into it.
        await TypeAsync(page, editor, "Intro");
        await SetBlockAsync(page, editor, "h1");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("Background");
        await SetBlockAsync(page, editor, "h2");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("Method");
        await SetBlockAsync(page, editor, "h2");
    }

    /// <summary>Converts the current line via the JS block-type touchpoint.</summary>
    private static Task SetBlockAsync(IPage page, string editor, string tag) =>
        page.EvaluateAsync(
            @"async ([id, tag]) => {
                const mod = await import('/_content/Blazor.Lexical/blazor-lexical.mjs');
                mod.setBlockType(id, tag);
            }",
            new[] { editor.TrimStart('#'), tag });

    [Fact] // headings get ids stamped onto the rendered DOM — the point of the feature
    public async Task Toc_StampsAnchorIdsOntoHeadings()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeOutlineAsync(page, QuietEditor);

        // The prefix is per-editor, which is how two editors on one page stay distinct.
        await Expect(page.Locator($"{QuietEditor} h1#q-intro")).ToHaveCountAsync(1);
        await Expect(page.Locator($"{QuietEditor} h2#q-background")).ToHaveCountAsync(1);
        await Expect(page.Locator($"{QuietEditor} h2#q-method")).ToHaveCountAsync(1);
    }

    [Fact] // the anchors are DOM-only: they must not appear in the serialized document
    public async Task Toc_DoesNotChangeTheSerializedDocument()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeOutlineAsync(page, QuietEditor);

        var json = await page.EvaluateAsync<string>(
            @"async () => {
                const mod = await import('/_content/Blazor.Lexical/blazor-lexical.mjs');
                return mod.getEditorStateJson('editor-toc-quiet');
            }");

        Assert.Contains("Intro", json);
        Assert.DoesNotContain("q-intro", json);
        Assert.DoesNotContain("anchor", json);
    }

    [Fact] // the JS renderer builds a nested list into the host's element
    public async Task Toc_RendersANestedListIntoTheTarget()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeOutlineAsync(page, QuietEditor);

        var target = page.Locator("#toc-target-quiet");
        // One top-level item (Intro) with a nested list holding the two h2s.
        await Expect(target.Locator("> ol > li")).ToHaveCountAsync(1);
        await Expect(target.Locator("> ol > li > ol > li")).ToHaveCountAsync(2);
        await Expect(target.Locator("a[data-lexical-toc-anchor='q-background']"))
            .ToHaveTextAsync("Background");
    }

    [Fact] // invariant #1: the quiet editor's outline runs with zero interop
    public async Task Toc_DoesNoInterop_WhenNoCallbackIsWired()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);

        await TypeOutlineAsync(page, QuietEditor);
        await Expect(page.Locator("#toc-target-quiet a")).ToHaveCountAsync(3);

        // The notify editor was never touched, so nothing can have pushed. Had the quiet
        // extension called invokeDotNet it would have thrown and logged.
        await Expect(page.Locator("#toc-push-count")).ToHaveTextAsync("0");
        Assert.Empty(errors);
    }

    [Fact] // the opt-in half: the tree reaches C#, nesting intact
    public async Task Toc_PushesTheTree_WhenOnTocChangedIsWired()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeOutlineAsync(page, NotifyEditor);

        await Expect(page.Locator("#toc-count")).ToHaveTextAsync("1");
        // Depth-first, so the levels prove the nesting rather than just the membership.
        await Expect(page.Locator("#toc-flat"))
            .ToHaveTextAsync("1:n-intro,2:n-background,2:n-method");
    }

    [Fact] // the same model rendered by Blazor, from the pushed tree
    public async Task TocList_RendersThePushedModel()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeOutlineAsync(page, NotifyEditor);
        await Expect(page.Locator("#toc-count")).ToHaveTextAsync("1");

        var list = page.Locator(".harness-toc-list");
        await Expect(list.Locator("> li")).ToHaveCountAsync(1);
        await Expect(list.Locator("> li > ol > li")).ToHaveCountAsync(2);
        await Expect(list.Locator("a[href='#n-background']")).ToHaveTextAsync("Background");
    }

    [Fact] // typing that leaves the outline alone must not push at all (the signature gate)
    public async Task Toc_DoesNotPush_WhenOnlyBodyTextChanges()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeOutlineAsync(page, NotifyEditor);
        await Expect(page.Locator("#toc-count")).ToHaveTextAsync("1");
        var pushes = await page.Locator("#toc-push-count").TextContentAsync();

        // A new paragraph of body text: no heading changed, so no push.
        await page.ClickAsync(NotifyEditor);
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await SetBlockAsync(page, NotifyEditor, "paragraph");
        await page.Keyboard.TypeAsync("some body text that is not a heading");

        await Expect(page.Locator("#editor-toc-notify p")).ToHaveCountAsync(1);
        await Expect(page.Locator("#toc-push-count")).ToHaveTextAsync(pushes!);
    }

    [Fact] // the pull API returns the same tree the push carries
    public async Task GetTocAsync_ReturnsTheCurrentOutline()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeOutlineAsync(page, NotifyEditor);
        await Expect(page.Locator("#toc-count")).ToHaveTextAsync("1");
        await page.ClickAsync("#btn-toc-get");

        await Expect(page.Locator("#toc-get-result"))
            .ToHaveTextAsync("n-intro,n-background,n-method");
    }

    [Fact] // scrolling to a known anchor resolves; the return value says so
    public async Task ScrollToAnchorAsync_ResolvesAKnownAnchor()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeOutlineAsync(page, NotifyEditor);
        await Expect(page.Locator("#toc-count")).ToHaveTextAsync("1");
        await page.ClickAsync("#btn-toc-scroll");

        await Expect(page.Locator("#toc-scroll-result")).ToHaveTextAsync("True");
    }

    [Fact] // clicking a rendered item scrolls its heading into view, without navigating
    public async Task Toc_ItemClick_ScrollsWithoutNavigating()
    {
        var page = await Fx.OpenHarnessAsync();
        var url = page.Url;

        await TypeOutlineAsync(page, QuietEditor);
        await page.ClickAsync("#toc-target-quiet a[data-lexical-toc-anchor='q-method']");

        // preventDefault: the fragment must not end up in the address bar.
        Assert.Equal(url, page.Url);
        var inView = await page.Locator($"{QuietEditor} h2#q-method").EvaluateAsync<bool>(
            @"el => {
                const r = el.getBoundingClientRect();
                return r.top >= 0 && r.bottom <= window.innerHeight;
            }");
        Assert.True(inView);
    }
}
