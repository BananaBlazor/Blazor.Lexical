using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Covers the consumer extension SDK end to end, through the badge reference
/// extension (Samples/Extensions.Badge) — an external ESM loaded by URL, not part
/// of our bundle. The two harness editors form the same opt-in/no-opt-in pair as
/// .harness-format / .harness-notify do for the core:
/// <list type="bullet">
/// <item><c>#editor-badge-notify</c> (.harness-badge-notify) wires
/// <c>OnBadgeClicked</c>, so the extension reports an invoke handler and clicking a
/// badge round-trips to C#; it also drives the .NET→JS direction.</item>
/// <item><c>#editor-badge-quiet</c> (.harness-badge-quiet) wires nothing: the same
/// custom node and the same client-side insert, but zero JS→.NET calls.</item>
/// </list>
/// </summary>
public class ExtensionTests : HarnessTestBase
{
    public ExtensionTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/extensions";

    protected override string ReadySelector =>
        "#editor-badge-quiet[data-lexical-editor='true']";

    private const string NotifyEditor = "#editor-badge-notify";
    private const string QuietEditor = "#editor-badge-quiet";

    /// <summary>Types in an editor, then clicks the extension's own insert button.</summary>
    private static async Task InsertBadgeAsync(IPage page, string editor, string cssClass)
    {
        await TypeAsync(page, editor, "before ");
        await page.ClickAsync($"{cssClass} [data-lexical-badge-insert]");
    }

    [Fact] // the external module loads, registers its node, and inserts it with no interop
    public async Task Extension_InsertsCustomNode_WithoutAnyCallback()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await InsertBadgeAsync(page, QuietEditor, ".harness-badge-quiet");

        var badge = page.Locator($"{QuietEditor} [data-badge]");
        await Expect(badge).ToHaveCountAsync(1);
        await Expect(badge).ToHaveTextAsync("Quiet");
        Assert.Empty(errors);
    }

    [Fact] // each instance gets its own GetOptions() payload (label differs per editor)
    public async Task Extension_ReceivesPerInstanceOptions()
    {
        var page = await OpenAsync();

        await InsertBadgeAsync(page, NotifyEditor, ".harness-badge-notify");
        await InsertBadgeAsync(page, QuietEditor, ".harness-badge-quiet");

        await Expect(page.Locator($"{NotifyEditor} [data-badge]")).ToHaveTextAsync("Draft");
        await Expect(page.Locator($"{QuietEditor} [data-badge]")).ToHaveTextAsync("Quiet");
    }

    [Fact] // the module's theme fragment is merged into the editor's theme pre-create
    public async Task Extension_ThemeFragment_StylesItsOwnNode()
    {
        var page = await OpenAsync();

        await InsertBadgeAsync(page, QuietEditor, ".harness-badge-quiet");

        // BadgeNode's createDOM reads config.theme.badge rather than hardcoding the
        // class, so the class only lands if create() merged the fragment in.
        await Expect(page.Locator($"{QuietEditor} [data-badge]"))
            .ToHaveClassAsync(new Regex(@"\bblazor-lexical-badge\b"));
    }

    [Fact] // JS→.NET: clicking a badge reaches the extension's own C# handler
    public async Task BadgeClick_InvokesDotNet_WhenHandlerIsWired()
    {
        var page = await OpenAsync();

        await InsertBadgeAsync(page, NotifyEditor, ".harness-badge-notify");
        await page.ClickAsync($"{NotifyEditor} [data-badge]");

        await Expect(page.Locator("#badge-clicked")).ToHaveTextAsync("Draft");
        await Expect(page.Locator("#badge-click-count")).ToHaveTextAsync("1");
    }

    [Fact] // invariant #1: with no handler wired the extension performs zero interop
    public async Task BadgeClick_DoesNoInterop_WhenNoHandlerIsWired()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        await InsertBadgeAsync(page, QuietEditor, ".harness-badge-quiet");
        await page.ClickAsync($"{QuietEditor} [data-badge]");

        // Nothing reached .NET. The quiet extension's descriptor reports no invoke
        // handler, so its invokeDotNet is never called — had it been, it would have
        // thrown and the module's catch would have logged a console error.
        await Expect(page.Locator("#badge-clicked")).ToHaveTextAsync("");
        await Expect(page.Locator("#badge-click-count")).ToHaveTextAsync("0");
        Assert.Empty(errors);
        // The badge itself is untouched — the client-side half works either way.
        await Expect(page.Locator($"{QuietEditor} [data-badge]")).ToHaveCountAsync(1);
    }

    [Fact] // .NET→JS: InvokeJsAsync reaches the module's invoke handler, both ways
    public async Task InvokeJs_InsertsAndCounts_ThroughTheExtensionModule()
    {
        var page = await OpenAsync();

        // Give the editor a selection to insert at, then drive it from C#.
        await TypeAsync(page, NotifyEditor, "csharp ");
        await page.ClickAsync("#btn-badge-insert-cs");
        await Expect(page.Locator($"{NotifyEditor} [data-badge]")).ToHaveTextAsync("From C#");

        // The reverse call returns a value (the badge count) back to C#.
        await page.ClickAsync("#btn-badge-count");
        await Expect(page.Locator("#badge-count")).ToHaveTextAsync("1");
    }

    [Fact] // an extension's custom node participates in the editor's own serialization
    public async Task ExtensionNode_SurvivesHtmlExport()
    {
        var page = await OpenAsync();

        await InsertBadgeAsync(page, NotifyEditor, ".harness-badge-notify");
        await page.ClickAsync("#btn-badge-html");

        await Expect(page.Locator("#badge-html-result")).ToContainTextAsync("data-badge");
        await Expect(page.Locator("#badge-html-result")).ToContainTextAsync("Draft");
    }

    [Fact] // the extension module is loaded by URL from its own RCL, not from our bundle
    public async Task ExtensionModule_IsServedByItsOwnLibrary()
    {
        var page = await OpenAsync();
        var url = Fx.BaseUrl + "_content/Samples.Extensions.Badge/badge.mjs";

        var hasDefaultFactory = await page.EvaluateAsync<bool>(
            @"async (url) => {
                const mod = await import(url);
                return typeof mod.default === 'function';
            }",
            url);

        Assert.True(hasDefaultFactory);
    }
}
