using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;
using Microsoft.Playwright;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalMarks /&gt;</c> — the app-owned-id highlight.
/// Paired like the badge extension: <c>#editor-marks-notify</c> wires both callbacks,
/// <c>#editor-marks-quiet</c> wires neither and still works entirely from C#.
/// </summary>
public class MarkTests : HarnessTestBase
{
    public MarkTests(HarnessFixture fx) : base(fx) { }

    private const string NotifyEditor = "#editor-marks-notify";
    private const string QuietEditor = "#editor-marks-quiet";

    /// <summary>
    /// Selects <paramref name="length"/> characters starting at <paramref name="start"/>
    /// on the editor's first line. Driven from the keyboard rather than by building a DOM
    /// Range: a synthetic Range is not something Lexical adopts into its own selection, so
    /// the wrap would find nothing to act on.
    /// </summary>
    private static async Task SelectRangeAsync(IPage page, string editor, int start, int length)
    {
        await page.ClickAsync(editor);
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

    [Fact] // the whole point: a selection becomes a <mark> carrying the app's id
    public async Task WrapSelection_CreatesAMarkNode()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, QuietEditor, "hello marked world");
        await SelectRangeAsync(page, QuietEditor, 6, 6);
        await page.ClickAsync("#btn-mark-quiet-wrap");

        var mark = page.Locator($"{QuietEditor} mark");
        await Expect(mark).ToHaveCountAsync(1);
        await Expect(mark).ToHaveTextAsync("marked");
        // The theme fragment the module contributes, overridden by LexicalTheme.Default.
        await Expect(mark).ToHaveClassAsync(new Regex(@"\bblazor-lexical__mark\b"));
    }

    [Fact] // invariant #1: a decorative highlighter performs zero JS→.NET calls
    public async Task Marks_DoNoInterop_WhenNoCallbackIsWired()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);

        await TypeAsync(page, QuietEditor, "hello marked world");
        await SelectRangeAsync(page, QuietEditor, 6, 6);
        await page.ClickAsync("#btn-mark-quiet-wrap");
        await Expect(page.Locator($"{QuietEditor} mark")).ToHaveCountAsync(1);

        // Clicking inside the mark would push on the notify editor; here it must not.
        await page.ClickAsync($"{QuietEditor} mark");
        await Expect(page.Locator("#mark-clicked")).ToHaveTextAsync("");
        await Expect(page.Locator("#mark-active")).ToHaveTextAsync("");
        Assert.Empty(errors);
    }

    [Fact] // the id round-trips unchanged: the library never mints or rewrites one
    public async Task GetMarkIds_ReturnsTheAppsOwnIds()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hello marked world");
        await SelectRangeAsync(page, NotifyEditor, 6, 6);
        await page.ClickAsync("#btn-mark-wrap-a");
        await Expect(page.Locator("#mark-wrap-result")).ToHaveTextAsync("True");

        await page.ClickAsync("#btn-mark-ids");
        await Expect(page.Locator("#mark-ids")).ToHaveTextAsync("mark-a");
    }

    [Fact] // overlapping wraps split the text and MERGE the id sets rather than replacing
    public async Task OverlappingMarks_MergeTheirIds()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "one two three four");
        // "one two" then "two three" — the overlap is the word "two".
        await SelectRangeAsync(page, NotifyEditor, 0, 7);
        await page.ClickAsync("#btn-mark-wrap-a");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(1);

        // "two three" (chars 4..13) — it starts inside mark A and runs past its end, so
        // the second wrap has to split the first rather than replace it.
        await SelectRangeAsync(page, NotifyEditor, 4, 9);
        await page.ClickAsync("#btn-mark-wrap-b");

        await page.ClickAsync("#btn-mark-ids");
        await Expect(page.Locator("#mark-ids")).ToHaveTextAsync("mark-a,mark-b");

        // The overlapping span carries both ids, which is what --overlap styles.
        var overlap = page.Locator($"{NotifyEditor} mark.blazor-lexical__mark--overlap");
        await Expect(overlap).ToHaveCountAsync(1);
    }

    [Fact] // the caret's marks reach C# — several of them where marks overlap
    public async Task GetMarkIdsAtSelection_ReturnsEveryCoveringMark()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hello marked world");
        await SelectRangeAsync(page, NotifyEditor, 6, 6);
        await page.ClickAsync("#btn-mark-wrap-a");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(1);

        await page.ClickAsync($"{NotifyEditor} mark");
        await page.ClickAsync("#btn-mark-ids-sel");

        await Expect(page.Locator("#mark-ids-selection")).ToHaveTextAsync("mark-a");
    }

    [Fact] // clicking inside a mark reaches the opt-in C# handler
    public async Task MarkClick_InvokesDotNet_WhenHandlerIsWired()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hello marked world");
        await SelectRangeAsync(page, NotifyEditor, 6, 6);
        await page.ClickAsync("#btn-mark-wrap-a");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(1);

        await page.ClickAsync($"{NotifyEditor} mark");

        await Expect(page.Locator("#mark-clicked")).ToHaveTextAsync("mark-a");
    }

    [Fact] // removing unwraps the node, leaving the text behind
    public async Task RemoveMark_UnwrapsTheNode_AndKeepsTheText()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hello marked world");
        await SelectRangeAsync(page, NotifyEditor, 6, 6);
        await page.ClickAsync("#btn-mark-wrap-a");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(1);

        await page.ClickAsync("#btn-mark-remove-a");

        await Expect(page.Locator("#mark-remove-count")).ToHaveTextAsync("1");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(0);
        await Expect(page.Locator(NotifyEditor))
            .ToContainTextAsync("hello marked world");
    }

    [Fact] // a silent removal must not raise the content channel (app-driven cleanup)
    public async Task SilentRemove_DoesNotRaiseTheContentChannel()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hello marked world");
        await SelectRangeAsync(page, NotifyEditor, 6, 6);
        await page.ClickAsync("#btn-mark-wrap-a");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(1);

        // Let the wrap's own (non-silent) push settle before taking the baseline.
        await page.WaitForTimeoutAsync(400);
        var before = await page.Locator("#marks-change-count").TextContentAsync();

        await page.ClickAsync("#btn-mark-remove-a-silent");
        await Expect(page.Locator("#mark-remove-count")).ToHaveTextAsync("1");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(0);

        // Well past the content debounce: the count must not have moved.
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#marks-change-count")).ToHaveTextAsync(before!);
    }

    [Fact] // the active decoration is DOM-only — it never touches the document
    public async Task SetActiveMark_DecoratesWithoutChangingTheDocument()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hello marked world");
        await SelectRangeAsync(page, NotifyEditor, 6, 6);
        await page.ClickAsync("#btn-mark-wrap-a");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(1);

        var json = await page.EvaluateAsync<string>(
            @"async () => {
                const mod = await import('/_content/Blazor.Lexical/blazor-lexical.mjs');
                return mod.getEditorStateJson('editor-marks-notify');
            }");

        await page.ClickAsync("#btn-mark-active");
        await Expect(page.Locator($"{NotifyEditor} mark[data-lexical-mark-active]"))
            .ToHaveCountAsync(1);

        var after = await page.EvaluateAsync<string>(
            @"async () => {
                const mod = await import('/_content/Blazor.Lexical/blazor-lexical.mjs');
                return mod.getEditorStateJson('editor-marks-notify');
            }");
        Assert.Equal(json, after);
    }

    [Fact] // the mark node participates in the editor's own serialization
    public async Task MarkNode_SurvivesAnEditorStateRoundTrip()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, NotifyEditor, "hello marked world");
        await SelectRangeAsync(page, NotifyEditor, 6, 6);
        await page.ClickAsync("#btn-mark-wrap-a");
        await Expect(page.Locator($"{NotifyEditor} mark")).ToHaveCountAsync(1);

        var restored = await page.EvaluateAsync<int>(
            @"async () => {
                const mod = await import('/_content/Blazor.Lexical/blazor-lexical.mjs');
                const json = mod.getEditorStateJson('editor-marks-notify');
                mod.setEditorStateJson('editor-marks-notify', json);
                return document.querySelectorAll('#editor-marks-notify mark').length;
            }");

        Assert.Equal(1, restored);
    }
}
