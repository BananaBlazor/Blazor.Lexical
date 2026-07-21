using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Covers the mentions feature on the two harness editors:
/// - <c>#editor-mention-people</c> (.harness-mention-people): an "@" config with a fake
///   provider + OnSelected + a content callback — exercises the typeahead, insertion,
///   getMentions, and the silent-refresh guarantee (a refresh must not fire the content
///   channel, so opening a doc and updating stale names never marks it dirty);
/// - <c>#editor-mention-hashtag</c> (.harness-mention-hashtag): a freeform-only "#"
///   config with no provider — pure-JS hashtag highlighting, zero interop.
/// </summary>
public class MentionTests : HarnessTestBase
{
    public MentionTests(HarnessFixture fx) : base(fx) { }

    private const string PeopleEditor = "#editor-mention-people";
    private const string Menu = ".harness-mention-people .blazor-lexical__mention-menu";

    [Fact] // typing "@lu" queries the provider and shows a candidate with its secondary line
    public async Task Typeahead_ShowsCandidate_WithSecondary()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);
        var menu = page.Locator(Menu);

        await TypeAsync(page, PeopleEditor, "@lu");

        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
        var rows = menu.Locator("[data-lexical-mention-item]");
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows.First).ToContainTextAsync("Luke Skywalker");
        await Expect(menu.Locator(".blazor-lexical__mention-item-secondary")).ToHaveTextAsync("Jedi");
        Assert.Empty(errors);
    }

    [Fact] // pressing Enter inserts the atomic reference node and fires OnSelected
    public async Task Typeahead_Enter_InsertsMention_AndNotifiesSelected()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "@lu");
        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Enter");

        var mention = editor.Locator("span[data-lexical-mention]");
        await Expect(mention).ToHaveCountAsync(1);
        await Expect(mention).ToHaveTextAsync("Luke Skywalker");
        await Expect(mention).ToHaveAttributeAsync("data-lexical-mention-url", "/u/luke");
        // The "@lu" trigger text is gone (replaced by the token).
        await Expect(editor).Not.ToContainTextAsync("@lu");
        // OnSelected (the opt-in escape hatch) reported the pick.
        await Expect(page.Locator("#mention-selected")).ToHaveTextAsync("Luke Skywalker|1");
        // The menu closed.
        await Expect(page.Locator(Menu)).Not.ToHaveAttributeAsync("data-lexical-visible", "");
    }

    [Fact] // Escape dismisses the menu and leaves the typed text in place (no freeform here)
    public async Task Typeahead_Escape_LeavesTypedText()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "@le");
        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Menu)).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(editor).ToContainTextAsync("@le");
        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveCountAsync(0);
    }

    [Fact] // ArrowDown moves the highlight to the second candidate before committing
    public async Task Typeahead_ArrowDown_SelectsSecondCandidate()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator(PeopleEditor);
        var menu = page.Locator(Menu);

        // "a" matches Luke SkywAlker, LeiA OrgAna, HAn Solo — three candidates.
        await TypeAsync(page, PeopleEditor, "@a");
        await Expect(menu.Locator("[data-lexical-mention-item]")).ToHaveCountAsync(3);

        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");

        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveTextAsync("Leia Organa");
    }

    [Fact] // freeform "#" highlights typed tags live with no provider and no interop
    public async Task Freeform_HighlightsHashtag_WithZeroInterop()
    {
        var page = await Fx.OpenHarnessAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator("#editor-mention-hashtag");

        await TypeAsync(page, "#editor-mention-hashtag", "hello #world done");

        var highlight = editor.Locator("span[data-lexical-mention-highlight]");
        await Expect(highlight).ToHaveCountAsync(1);
        await Expect(highlight).ToHaveTextAsync("#world");
        Assert.Empty(errors);
    }

    [Fact] // getMentions enumerates inserted references with their app-owned value
    public async Task GetMentions_ListsInsertedReference()
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, PeopleEditor, "@lu");
        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(PeopleEditor).Locator("span[data-lexical-mention]")).ToHaveCountAsync(1);

        await page.ClickAsync("#btn-mention-list");
        await Expect(page.Locator("#mention-list")).ToHaveTextAsync("Luke Skywalker|1|@");
    }

    [Fact] // a refresh updates the rendered text but does NOT fire the content channel
    public async Task Refresh_UpdatesText_WithoutDirtyingDocument()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator(PeopleEditor);
        var mention = editor.Locator("span[data-lexical-mention]");

        await TypeAsync(page, PeopleEditor, "@lu");
        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Enter");
        await Expect(mention).ToHaveTextAsync("Luke Skywalker");

        // Let the debounced content-change pushes from typing/insertion all land, then
        // snapshot the count. A silent refresh must not move it.
        await page.WaitForTimeoutAsync(500);
        var beforeCount = await page.Locator("#mention-change-count").TextContentAsync();

        await page.ClickAsync("#btn-mention-refresh");

        await Expect(mention).ToHaveTextAsync("Luke S. (renamed)");
        await Expect(mention).ToHaveAttributeAsync("data-lexical-mention-url", "/u/luke-renamed");
        await Expect(page.Locator("#mention-refresh-count")).ToHaveTextAsync("1");

        // Give any (erroneous) content push time to arrive, then assert it never did.
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#mention-change-count")).ToHaveTextAsync(beforeCount ?? "");
    }

    [Fact] // a mention survives an editor-state JSON round-trip (registered node + value)
    public async Task Mention_SurvivesJsonRoundTrip()
    {
        var page = await Fx.OpenHarnessAsync();
        var editor = page.Locator(PeopleEditor);
        var moduleUrl = Fx.BaseUrl + "_content/Blazor.Lexical/blazor-lexical.mjs";

        await TypeAsync(page, PeopleEditor, "@lu");
        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Enter");
        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveCountAsync(1);

        // Capture state JSON, clear the editor, then restore — the mention node (with its
        // app-owned value) must come back, proving the custom node serializes and is
        // registered in the editor's node list.
        var json = await page.EvaluateAsync<string>(
            @"async ([url, id]) => {
                const mod = await import(url);
                return mod.getEditorStateJson(id);
            }",
            new[] { moduleUrl, "editor-mention-people" });

        Assert.Contains("\"type\":\"mention\"", json);
        Assert.Contains("\"value\":\"1\"", json);

        await page.EvaluateAsync(
            @"async ([url, id]) => {
                const mod = await import(url);
                mod.setText(id, '');
            }",
            new[] { moduleUrl, "editor-mention-people" });
        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveCountAsync(0);

        await page.EvaluateAsync(
            @"async ([url, id, state]) => {
                const mod = await import(url);
                mod.setEditorStateJson(id, state);
            }",
            new[] { moduleUrl, "editor-mention-people", json });

        var restored = editor.Locator("span[data-lexical-mention]");
        await Expect(restored).ToHaveCountAsync(1);
        await Expect(restored).ToHaveTextAsync("Luke Skywalker");
    }

    // --- Slow provider (#editor-mention-slow): the graceful-degradation contract ---

    private const string SlowEditor = "#editor-mention-slow";
    private const string SlowMenu = ".harness-mention-slow .blazor-lexical__mention-menu";

    /// <summary>
    /// A host's data source can be slow, so the picker shows itself as soon as the
    /// query goes out rather than only once results arrive — otherwise a slow provider
    /// is indistinguishable from a broken trigger.
    /// </summary>
    [Fact]
    public async Task SlowProvider_MenuShowsLoadingWhileTheQueryIsInFlight()
    {
        var page = await Fx.OpenHarnessAsync();
        var menu = page.Locator(SlowMenu);

        await TypeAsync(page, SlowEditor, "@lu");

        await Expect(menu).ToHaveAttributeAsync("data-lexical-mention-loading", "");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
    }

    /// <summary>
    /// …and a provider that never answers must not strand the session: the soft timeout
    /// drops the query and closes the menu, so the next keystroke starts clean.
    /// </summary>
    [Fact]
    public async Task SlowProvider_ClosesTheMenu_WhenTheQueryTimesOut()
    {
        var page = await Fx.OpenHarnessAsync();
        var menu = page.Locator(SlowMenu);

        await TypeAsync(page, SlowEditor, "@lu");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-mention-loading", "");

        // The harness config's QueryTimeout is 600ms; the provider takes 5s.
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-mention-loading", "");
    }
}
