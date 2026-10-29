using System.Text.RegularExpressions;
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
///   config with no provider — pure-JS hashtag highlighting, zero interop;
/// - <c>#editor-mention-readonly</c>: a read-only editor with an "@" provider config,
///   which <c>StartMentionAsync</c> must refuse;
/// - <c>#editor-mention-button</c> / <c>#editor-mention-button-none</c>:
///   <c>LexicalMentionButton</c>s with and without mention configs;
/// - <c>#editor-mention-spaces</c>: an "@" config with <c>AllowSpacesInQuery</c>, also
///   used for atomic-mention selection and editing;
/// - <c>#editor-mention-clip</c>: an "@" editor inside a short <c>overflow: hidden</c>
///   container, which the picker must escape;
/// - <c>#editor-mention-markdown</c> / <c>#editor-mention-markdown-initial</c>: "#"
///   configs with a <c>MarkdownUrlPrefix</c> — mentions round-trip through Markdown.
/// </summary>
public class MentionTests : HarnessTestBase
{
    public MentionTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/mentions";

    protected override string ReadySelector =>
        "#editor-mention-markdown-initial[data-lexical-editor='true']";

    private const string PeopleEditor = "#editor-mention-people";
    private const string Menu = ".harness-mention-people .blazor-lexical__mention-menu";

    [Fact] // typing "@lu" queries the provider and shows a candidate with its secondary line
    public async Task Typeahead_ShowsCandidate_WithSecondary()
    {
        var page = await OpenAsync();
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
        var page = await OpenAsync();
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
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "@le");
        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Menu)).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(editor).ToContainTextAsync("@le");
        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveCountAsync(0);
    }

    [Fact] // the open picker is a WAI-ARIA combobox popup driven from the focused content element
    public async Task Typeahead_ExposesComboboxSemantics_WhileOpen()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);
        var menu = page.Locator(Menu);
        var rows = menu.Locator("[data-lexical-mention-item]");
        const string menuId = "editor-mention-people-mention-menu";

        // "a" matches three candidates.
        await TypeAsync(page, PeopleEditor, "@a");
        await Expect(rows).ToHaveCountAsync(3);

        await Expect(menu).ToHaveIdAsync(menuId);
        await Expect(menu).ToHaveAttributeAsync("role", "listbox");
        await Expect(editor).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(editor).ToHaveAttributeAsync("aria-controls", menuId);
        await Expect(editor).ToHaveAttributeAsync("aria-autocomplete", "list");
        await Expect(editor).ToHaveAttributeAsync("aria-activedescendant", $"{menuId}-option-0");
        for (var i = 0; i < 3; i++)
        {
            await Expect(rows.Nth(i)).ToHaveAttributeAsync("role", "option");
            await Expect(rows.Nth(i)).ToHaveIdAsync($"{menuId}-option-{i}");
            await Expect(rows.Nth(i)).ToHaveAttributeAsync("aria-selected", i == 0 ? "true" : "false");
        }

        // Arrowing moves the active descendant and aria-selected; focus never leaves the text.
        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(editor).ToHaveAttributeAsync("aria-activedescendant", $"{menuId}-option-1");
        await Expect(rows.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(rows.Nth(0)).ToHaveAttributeAsync("aria-selected", "false");
        await Expect(editor).ToBeFocusedAsync();
    }

    [Fact] // closing the picker collapses the combobox and drops the active descendant
    public async Task Typeahead_Close_CollapsesCombobox()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "@le");
        await Expect(editor).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(editor).ToHaveAttributeAsync("aria-activedescendant", new Regex("-option-0$"));

        await page.Keyboard.PressAsync("Escape");
        await Expect(editor).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Null(await editor.GetAttributeAsync("aria-activedescendant"));
        await Expect(editor).ToBeFocusedAsync();
    }

    [Fact] // ArrowDown moves the highlight to the second candidate before committing
    public async Task Typeahead_ArrowDown_SelectsSecondCandidate()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);
        var menu = page.Locator(Menu);

        // "a" matches Luke SkywAlker, LeiA OrgAna, HAn Solo — three candidates.
        await TypeAsync(page, PeopleEditor, "@a");
        await Expect(menu.Locator("[data-lexical-mention-item]")).ToHaveCountAsync(3);

        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");

        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveTextAsync("Leia Organa");
    }

    [Fact] // ArrowUp from the first candidate wraps to the last and highlights it
    public async Task Typeahead_ArrowUp_WrapsToLastCandidate()
    {
        var page = await OpenAsync();
        var rows = page.Locator(Menu).Locator("[data-lexical-mention-item]");

        await TypeAsync(page, PeopleEditor, "@a");
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.Nth(0)).ToHaveAttributeAsync("data-lexical-mention-active", "");

        await page.Keyboard.PressAsync("ArrowUp");
        await Expect(rows.Nth(2)).ToHaveAttributeAsync("data-lexical-mention-active", "");
        await Expect(rows.Nth(0)).Not.ToHaveAttributeAsync("data-lexical-mention-active", "");
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator(PeopleEditor).Locator("span[data-lexical-mention]")).ToHaveTextAsync("Han Solo");
    }

    [Fact] // freeform "#" highlights typed tags live with no provider and no interop
    public async Task Freeform_HighlightsHashtag_WithZeroInterop()
    {
        var page = await OpenAsync();
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
        var page = await OpenAsync();

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
        var page = await OpenAsync();
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
        var page = await OpenAsync();
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

    // --- Spaces in the query (#editor-mention-spaces): AllowSpacesInQuery ---

    [Fact] // with the flag, "@luke sky" keeps the picker open and commits the full-name match
    public async Task AllowSpacesInQuery_KeepsSessionOpenAcrossASpace()
    {
        var page = await OpenAsync();
        var editor = page.Locator(SpacesEditor);
        var menu = page.Locator(SpacesMenu);

        await TypeAsync(page, SpacesEditor, "@luke sky");

        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(menu.Locator("[data-lexical-mention-item]")).ToHaveTextAsync(new Regex("Luke Skywalker"));
        await page.Keyboard.PressAsync("Enter");

        var mention = editor.Locator("span[data-lexical-mention]");
        await Expect(mention).ToHaveTextAsync("Luke Skywalker");
        // The whole "@luke sky" run was replaced, not just "@luke".
        await Expect(editor).ToHaveTextAsync(new Regex(@"^\s*Luke Skywalker\s*$"));
    }

    private const string SpacesEditor = "#editor-mention-spaces";
    private const string SpacesMenu = ".harness-mention-spaces .blazor-lexical__mention-menu";

    [Theory] // even with the flag, a double space, Escape, or ←/→ ends the session
    [InlineData("Space")]
    [InlineData("Escape")]
    [InlineData("ArrowLeft")]
    [InlineData("ArrowRight")]
    public async Task AllowSpacesInQuery_DismissKeys_ClosePicker(string key)
    {
        var page = await OpenAsync();
        var menu = page.Locator(SpacesMenu);

        await TypeAsync(page, SpacesEditor, key == "Space" ? "@luke " : "@luke");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Keyboard.PressAsync(key);
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        // Stays closed: the debounced query of the dismissed trigger must not reopen it.
        await page.WaitForTimeoutAsync(500);
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");
    }

    [Fact] // typing on after a dismissal keeps that trigger closed; a new trigger opens again
    public async Task Dismissal_HoldsForTheTrigger_UntilANewOneStarts()
    {
        var page = await OpenAsync();
        var menu = page.Locator(SpacesMenu);

        await TypeAsync(page, SpacesEditor, "@lu");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Escape");

        // The query still matches (and grows) — the dismissed trigger must not reopen.
        await page.Keyboard.TypeAsync("ke sky");
        await page.WaitForTimeoutAsync(500);
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");

        // A double space ends that trigger; a fresh one opens the picker again.
        await page.Keyboard.TypeAsync("  @le");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(menu.Locator("[data-lexical-mention-item]")).ToHaveTextAsync(new Regex("Leia Organa"));
    }

    [Fact] // without the flag, the space after "@luke" ends the session and closes the picker
    public async Task WithoutAllowSpacesInQuery_SpaceClosesThePicker()
    {
        var page = await OpenAsync();
        var menu = page.Locator(Menu);

        await TypeAsync(page, PeopleEditor, "@luke");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");

        await page.Keyboard.TypeAsync(" sky");
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");
    }

    // --- Markdown round-trip (#editor-mention-markdown): MarkdownUrlPrefix ---

    private const string MarkdownEditor = "#editor-mention-markdown";
    private const string MarkdownMenu = ".harness-mention-markdown .blazor-lexical__mention-menu";
    private const string RequestId = "6f1c2a9e-0b7d-4e5a-9c3f-2d8e1a4b5c6d";
    private const string RequestLink = $"[#12 · Budget](#req:{RequestId})";

    private static async Task SetMarkdownAsync(IPage page, string markdown)
    {
        await page.FillAsync("#mention-markdown-input", markdown);
        await page.ClickAsync("#btn-mention-markdown-set");
    }

    private static async Task<string> GetMarkdownAsync(IPage page)
    {
        var output = page.Locator("#mention-markdown-output");
        await page.ClickAsync("#btn-mention-markdown-get");
        await Expect(output).Not.ToBeEmptyAsync();
        return await output.InnerTextAsync();
    }

    [Fact] // a prefixed mention exports as a link and imports back as a mention of the live config
    public async Task Mention_SurvivesMarkdownRoundTrip()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator(MarkdownEditor);
        var mention = editor.Locator("span[data-lexical-mention]");
        var list = page.Locator("#mention-markdown-list");

        await TypeAsync(page, MarkdownEditor, "#bu");
        await Expect(page.Locator(MarkdownMenu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Enter");
        await Expect(mention).ToHaveCountAsync(1);

        // The inserted mention's config id, to compare against the re-imported one.
        await page.ClickAsync("#btn-mention-markdown-list");
        await Expect(list).Not.ToBeEmptyAsync();
        var configId = (await list.InnerTextAsync()).Split('|')[^1];

        Assert.StartsWith(RequestLink, await GetMarkdownAsync(page));

        await SetMarkdownAsync(page, $"See {RequestLink} and [x](https://example.com)");

        await Expect(mention).ToHaveCountAsync(1);
        await Expect(mention).ToHaveTextAsync("#12 · Budget");
        await Expect(mention).ToHaveAttributeAsync("data-lexical-mention-url", $"#req:{RequestId}");
        // An unprefixed link in the same text stays an ordinary link.
        await Expect(editor.Locator("a[href='https://example.com']")).ToHaveTextAsync("x");

        await page.ClickAsync("#btn-mention-markdown-list");
        await Expect(list).ToHaveTextAsync($"#12 · Budget|{RequestId}|#|#req:{RequestId}|{configId}");
        Assert.Empty(errors);
    }

    [Fact] // brackets and format characters in a mention's label are escaped on export and restored on import
    public async Task Mention_MarkdownRoundTrip_EscapesBracketsInLabel()
    {
        var page = await OpenAsync();
        var mention = page.Locator(MarkdownEditor).Locator("span[data-lexical-mention]");
        var link = $@"[Q\[3\] \*draft\*](#req:{RequestId})";

        await SetMarkdownAsync(page, link);
        await Expect(mention).ToHaveCountAsync(1);
        await Expect(mention).ToHaveTextAsync("Q[3] *draft*");

        Assert.Equal(link, await GetMarkdownAsync(page));
    }

    // a formatted mention keeps its format and the Markdown stays balanced
    [Theory]
    [InlineData("**see {0} now**", "strong")] // format spans the mention and its siblings
    [InlineData("**{0}** now", "strong")]     // the mention opens and closes the tag itself
    [InlineData("see *{0}*", "em")]
    public async Task Mention_MarkdownRoundTrip_KeepsSurroundingFormat(string template, string tag)
    {
        var page = await OpenAsync();
        var markdown = string.Format(template, RequestLink);

        await SetMarkdownAsync(page, markdown);
        // A formatted mention renders as its format element (<strong>/<em>), not <span>.
        var mention = page.Locator(MarkdownEditor).Locator($"{tag}[data-lexical-mention]");
        await Expect(mention).ToHaveTextAsync("#12 · Budget");

        Assert.Equal(markdown, await GetMarkdownAsync(page));
    }

    [Fact] // beside a prefixed config, an unprefixed config's mention exports as plain text, not a link
    public async Task Mention_FromUnprefixedConfig_ExportsAsPlainText()
    {
        var page = await OpenAsync();
        var mention = page.Locator(MarkdownEditor).Locator("span[data-lexical-mention]");

        await TypeAsync(page, MarkdownEditor, "@lu");
        await Expect(page.Locator(MarkdownMenu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Enter");
        await Expect(mention).ToHaveAttributeAsync("data-lexical-mention-url", "/u/luke");

        var markdown = await GetMarkdownAsync(page);
        Assert.Contains("Luke Skywalker", markdown);
        Assert.DoesNotContain("/u/luke", markdown);
    }

    [Fact] // Markdown initial content is applied before extensions register, yet still maps mentions
    public async Task Mention_ImportsFromMarkdownInitialContent()
    {
        var page = await OpenAsync();
        var mention = page.Locator("#editor-mention-markdown-initial span[data-lexical-mention]");

        await Expect(mention).ToHaveCountAsync(1);
        await Expect(mention).ToHaveTextAsync("#12 · Budget");
        await Expect(mention).ToHaveAttributeAsync("data-lexical-mention-url", $"#req:{RequestId}");
    }

    [Fact] // without MarkdownUrlPrefix a matching link stays a plain link
    public async Task Mention_WithoutPrefix_LeavesMarkdownLinksAlone()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);
        var moduleUrl = Fx.BaseUrl + "_content/Blazor.Lexical/blazor-lexical.mjs";

        var markdown = await page.EvaluateAsync<string>(
            @"async ([url, id, md]) => {
                const mod = await import(url);
                await mod.setMarkdown(id, md);
                return mod.getMarkdown(id);
            }",
            new[] { moduleUrl, "editor-mention-people", RequestLink });

        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveCountAsync(0);
        await Expect(editor.Locator($"a[href='#req:{RequestId}']")).ToHaveTextAsync("#12 · Budget");
        Assert.Equal(RequestLink, markdown);
    }

    // --- StartMentionAsync (#editor-mention-people): a session started from a button ---

    [Fact] // a never-focused editor gets the trigger, focus and the picker, as a notifying edit
    public async Task StartMention_InEmptyEditor_OpensPicker()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator(PeopleEditor);
        var menu = page.Locator(Menu);

        await page.ClickAsync("#btn-mention-start");

        await Expect(page.Locator("#mention-start-result")).ToHaveTextAsync("started");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
        // The bare trigger queries with an empty string, which the fake provider matches to everyone.
        await Expect(menu.Locator("[data-lexical-mention-item]")).ToHaveCountAsync(3);
        await Expect(editor).ToBeFocusedAsync();
        Assert.Equal("@", await TextContentAsync(editor));
        // Unlike a silent refresh, the insert reaches the content channel.
        await Expect(page.Locator("#mention-change-count")).Not.ToHaveTextAsync("0");
        Assert.Empty(errors);
    }

    [Fact] // the trigger goes in at the caret, not at the end, so the button keeps the caret in place
    public async Task StartMention_MidText_InsertsAtTheCaret()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "hello world");
        for (var i = 0; i < " world".Length; i++)
        {
            await page.Keyboard.PressAsync("ArrowLeft");
        }
        await page.ClickAsync("#btn-mention-start");

        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        Assert.Equal("hello @ world", await TextContentAsync(editor));
    }

    [Fact] // a selected range is replaced, and the space check reads what precedes the range
    public async Task StartMention_ReplacesSelectedRange()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "hello world");
        for (var i = 0; i < "world".Length; i++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowLeft");
        }
        await page.ClickAsync("#btn-mention-start");

        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        Assert.Equal("hello @", await TextContentAsync(editor));
    }

    [Fact] // at the start of a new block nothing precedes the caret, so no space is borrowed from the block above
    public async Task StartMention_AtBlockStart_AddsNoSpace()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "hi");
        await page.Keyboard.PressAsync("Enter");
        await page.ClickAsync("#btn-mention-start");

        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        Assert.Equal("@", await TextContentAsync(editor.Locator("p").Nth(1)));
    }

    [Theory] // a separating space is added only when the caret follows a non-space character
    [InlineData("hi", "hi @")]
    [InlineData("hi ", "hi @")]
    public async Task StartMention_AfterText_SeparatesTheTrigger(string typed, string expected)
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, typed);
        await page.ClickAsync("#btn-mention-start");

        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        Assert.Equal(expected, await TextContentAsync(editor));
    }

    [Fact] // a started session behaves like a typed one: Enter commits the highlighted candidate
    public async Task StartMention_ThenEnter_InsertsMention()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "hello");
        await page.ClickAsync("#btn-mention-start");
        await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.TypeAsync("le");
        await Expect(page.Locator(Menu).Locator("[data-lexical-mention-item]")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("Enter");

        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveTextAsync("Leia Organa");
        await Expect(page.Locator("#mention-selected")).ToHaveTextAsync("Leia Organa|2");
        Assert.StartsWith("hello Leia Organa", await TextContentAsync(editor));
    }

    [Fact] // an initiator no provider config uses is refused without touching the document
    public async Task StartMention_UnknownInitiator_IsRefused()
    {
        var page = await OpenAsync();
        var editor = page.Locator(PeopleEditor);

        await TypeAsync(page, PeopleEditor, "hi");
        await page.ClickAsync("#btn-mention-start-unknown");

        await Expect(page.Locator("#mention-start-result")).ToHaveTextAsync("refused");
        Assert.Equal("hi", await TextContentAsync(editor));
    }

    [Theory] // a freeform-only config (no provider) and a read-only editor are both refused untouched
    [InlineData("#btn-mention-start-hashtag", "#editor-mention-hashtag", "")]
    [InlineData("#btn-mention-start-readonly", "#editor-mention-readonly", "read only")]
    public async Task StartMention_NoProviderOrReadOnly_IsRefused(string button, string editorId, string text)
    {
        var page = await OpenAsync();

        await page.ClickAsync(button);

        await Expect(page.Locator("#mention-start-result")).ToHaveTextAsync("refused");
        Assert.Equal(text, await TextContentAsync(page.Locator(editorId)));
    }

    // --- LexicalMentionButton (#editor-mention-button): the mention:<initiator> token ---

    private const string ButtonEditor = "#editor-mention-button";
    private const string ButtonMenu = ".harness-mention-button .blazor-lexical__mention-menu";

    private static ILocator MentionButton(IPage page, string root, string initiator)
        => page.Locator($"{root} [data-lexical-command='mention:{initiator}']");

    [Fact] // the button keeps the caret: the trigger goes in mid-text and the picker opens
    public async Task MentionButton_MidText_InsertsAtTheCaret()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var editor = page.Locator(ButtonEditor);
        var button = MentionButton(page, ".harness-mention-button", "@");

        await Expect(button).ToHaveAttributeAsync("aria-label", "Mention a person");
        await TypeAsync(page, ButtonEditor, "hello world");
        for (var i = 0; i < " world".Length; i++)
        {
            await page.Keyboard.PressAsync("ArrowLeft");
        }
        await button.ClickAsync();

        await Expect(page.Locator(ButtonMenu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(editor).ToBeFocusedAsync();
        Assert.Equal("hello @ world", await TextContentAsync(editor));
        Assert.Empty(errors);
    }

    [Fact] // a button-started session commits a candidate exactly like a typed one
    public async Task MentionButton_ThenEnter_InsertsMention()
    {
        var page = await OpenAsync();
        var editor = page.Locator(ButtonEditor);

        await TypeAsync(page, ButtonEditor, "hi");
        await MentionButton(page, ".harness-mention-button", "@").ClickAsync();
        await Expect(page.Locator(ButtonMenu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.TypeAsync("le");
        await Expect(page.Locator(ButtonMenu).Locator("[data-lexical-mention-item]")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("Enter");

        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveTextAsync("Leia Organa");
    }

    [Fact] // the token argument is everything after the first ':', so ':' itself is a usable initiator
    public async Task MentionButton_InitiatorWithColon_StartsSession()
    {
        var page = await OpenAsync();
        var button = MentionButton(page, ".harness-mention-button", ":");

        // Default content and label come from the initiator.
        await Expect(button).ToHaveTextAsync(":");
        await Expect(button).ToHaveAttributeAsync("aria-label", "Insert :");
        await button.ClickAsync();

        await Expect(page.Locator(ButtonMenu).Locator("[data-lexical-mention-item]")).ToHaveCountAsync(3);
        Assert.Equal(":", await TextContentAsync(page.Locator(ButtonEditor)));
    }

    [Theory] // no config, a provider-less config, or no mentions at all: disabled, and a click is inert
    [InlineData(".harness-mention-button", "!", ButtonEditor)]
    [InlineData(".harness-mention-hashtag", "#", "#editor-mention-hashtag")]
    [InlineData(".harness-mention-button-none", "@", "#editor-mention-button-none")]
    public async Task MentionButton_WithoutProviderConfig_IsDisabledAndInert(
        string root, string initiator, string editorId)
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var button = MentionButton(page, root, initiator);

        await Expect(button).ToHaveAttributeAsync("data-lexical-disabled", "");
        await Expect(button).ToHaveAttributeAsync("aria-disabled", "true");
        // Forced: Playwright won't click an aria-disabled control, but a user's click still lands.
        await button.ClickAsync(new() { Force = true });

        Assert.Equal("", await TextContentAsync(page.Locator(editorId)));
        Assert.Empty(errors);
    }

    [Fact] // read-only disables the button; making the editor editable again re-enables it
    public async Task MentionButton_ReadOnly_DisablesUntilEditable()
    {
        var page = await OpenAsync();
        var button = MentionButton(page, ".harness-mention-button", "@");

        await Expect(button).Not.ToHaveAttributeAsync("data-lexical-disabled", "");
        await page.ClickAsync("#btn-mention-button-toggle-readonly");
        await Expect(button).ToHaveAttributeAsync("data-lexical-disabled", "");
        await page.ClickAsync("#btn-mention-button-toggle-readonly");
        await Expect(button).Not.ToHaveAttributeAsync("data-lexical-disabled", "");
    }

    [Fact] // a node selection has no caret, so the button disables until the caret returns
    public async Task MentionButton_NodeSelection_DisablesUntilCaretReturns()
    {
        var page = await OpenAsync();
        var button = MentionButton(page, ".harness-mention-button", "@");

        await TypeAsync(page, ButtonEditor, "above");
        await page.ClickAsync("#btn-mention-button-hr");
        await page.Locator($"{ButtonEditor} hr").ClickAsync();
        await Expect(button).ToHaveAttributeAsync("data-lexical-disabled", "");

        await page.Locator($"{ButtonEditor} p").First.ClickAsync();
        await Expect(button).Not.ToHaveAttributeAsync("data-lexical-disabled", "");
    }

    // Raw textContent: Playwright's text assertions collapse whitespace, hiding a doubled space.
    private static Task<string> TextContentAsync(ILocator locator)
        => locator.EvaluateAsync<string>("el => el.textContent");

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
        var page = await OpenAsync();
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
        var page = await OpenAsync();
        var menu = page.Locator(SlowMenu);

        await TypeAsync(page, SlowEditor, "@lu");
        await Expect(menu).ToHaveAttributeAsync("data-lexical-mention-loading", "");

        // The harness config's QueryTimeout is 600ms; the provider takes 5s.
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(menu).Not.ToHaveAttributeAsync("data-lexical-mention-loading", "");
    }

    // --- Atomic mentions (#editor-mention-spaces): a committed mention is one inline object ---

    /// <summary>Types <paramref name="prefix"/> then "@luke" and commits "Luke Skywalker".</summary>
    private static async Task<ILocator> InsertLukeAsync(IPage page, string prefix = "")
    {
        await TypeAsync(page, SpacesEditor, prefix + "@luke");
        await Expect(page.Locator(SpacesMenu)).ToHaveAttributeAsync("data-lexical-visible", "");
        await page.Keyboard.PressAsync("Enter");
        var mention = page.Locator(SpacesEditor).Locator("span[data-lexical-mention]");
        await Expect(mention).ToHaveTextAsync("Luke Skywalker");
        return mention;
    }

    /// <summary>Clicks the middle of the mention, which selects it whole.</summary>
    private static async Task SelectByClickAsync(ILocator mention)
    {
        await mention.ClickAsync();
        await Expect(mention).ToHaveAttributeAsync("data-lexical-mention-selected", "");
    }

    private static Task<string> DomSelectionTextAsync(IPage page)
        => page.EvaluateAsync<string>("() => window.getSelection().toString()");

    [Fact] // a click inside a committed mention selects it whole and never reopens the picker
    public async Task Mention_Click_SelectsWhole_WithoutReopeningPicker()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var mention = await InsertLukeAsync(page);

        await SelectByClickAsync(mention);

        Assert.Equal("Luke Skywalker", await DomSelectionTextAsync(page));
        // Past the query debounce: the picker stays closed.
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator(SpacesMenu)).Not.ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(mention).ToHaveCountAsync(1);
        Assert.Empty(errors);
    }

    [Fact] // typing over a selected mention replaces it with plain text
    public async Task SelectedMention_Typing_ReplacesIt()
    {
        var page = await OpenAsync();
        var mention = await InsertLukeAsync(page);
        await SelectByClickAsync(mention);

        await page.Keyboard.TypeAsync("x");

        await Expect(mention).ToHaveCountAsync(0);
        Assert.Equal("x ", await TextContentAsync(page.Locator(SpacesEditor)));
    }

    [Fact] // Backspace removes a selected mention whole, and undo brings it back as a mention
    public async Task SelectedMention_Backspace_RemovesWhole_AndUndoRestores()
    {
        var page = await OpenAsync();
        var mention = await InsertLukeAsync(page);
        await SelectByClickAsync(mention);

        await page.Keyboard.PressAsync("Backspace");
        await Expect(mention).ToHaveCountAsync(0);
        Assert.Equal(" ", await TextContentAsync(page.Locator(SpacesEditor)));

        await page.Keyboard.PressAsync("Control+z");
        await Expect(mention).ToHaveCountAsync(1);
        await Expect(mention).ToHaveTextAsync("Luke Skywalker");
    }

    [Theory] // an arrow collapses a selected mention to that side, with the caret outside it
    [InlineData("ArrowLeft", "aLuke Skywalker ")]
    [InlineData("ArrowRight", "Luke Skywalkera ")]
    public async Task SelectedMention_Arrow_CollapsesOutside(string key, string expected)
    {
        var page = await OpenAsync();
        var mention = await InsertLukeAsync(page);
        await SelectByClickAsync(mention);

        await page.Keyboard.PressAsync(key);
        await Expect(mention).Not.ToHaveAttributeAsync("data-lexical-mention-selected", "");
        await page.Keyboard.TypeAsync("a");

        // The typed character landed beside the mention, which is intact.
        await Expect(mention).ToHaveTextAsync("Luke Skywalker");
        Assert.Equal(expected, await TextContentAsync(page.Locator(SpacesEditor)));
    }

    [Fact] // from the text after a mention, ← steps over the whole mention
    public async Task CaretAfterMention_ArrowLeft_StepsOverIt()
    {
        var page = await OpenAsync();
        var mention = await InsertLukeAsync(page, "hi ");

        // Past the trailing space, then over the mention in one step.
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.TypeAsync("z");

        await Expect(mention).ToHaveTextAsync("Luke Skywalker");
        Assert.Equal("hi zLuke Skywalker ", await TextContentAsync(page.Locator(SpacesEditor)));
    }

    [Fact] // Backspace toward a mention removes it whole
    public async Task CaretAfterMention_Backspace_RemovesWhole()
    {
        var page = await OpenAsync();
        var mention = await InsertLukeAsync(page, "hi ");

        // The trailing space, then the whole mention.
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.PressAsync("Backspace");

        await Expect(mention).ToHaveCountAsync(0);
        Assert.Equal("hi ", await TextContentAsync(page.Locator(SpacesEditor)));
    }

    [Fact] // Shift+→ into a mention extends over all of it
    public async Task ShiftArrow_IntoMention_ExtendsOverWhole()
    {
        var page = await OpenAsync();
        var mention = await InsertLukeAsync(page, "hi ");

        await page.Keyboard.PressAsync("Home");
        for (var i = 0; i < "hi ".Length + 1; i++)
        {
            await page.Keyboard.PressAsync("Shift+ArrowRight");
        }

        Assert.Equal("hi Luke Skywalker", await DomSelectionTextAsync(page));
        await Expect(mention).ToHaveAttributeAsync("data-lexical-mention-selected", "");
    }

    [Fact] // a drag from plain text into the middle of a mention expands to cover it
    public async Task DragIntoMention_ExpandsToCoverIt()
    {
        var page = await OpenAsync();
        var mention = await InsertLukeAsync(page, "hi ");

        var paragraph = await page.Locator(SpacesEditor).Locator("p").First.BoundingBoxAsync();
        var target = await mention.BoundingBoxAsync();
        Assert.NotNull(paragraph);
        Assert.NotNull(target);
        await page.Mouse.MoveAsync(paragraph.X + 1, paragraph.Y + paragraph.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(target.X + target.Width / 2, target.Y + target.Height / 2, new() { Steps = 5 });
        await page.Mouse.UpAsync();

        await Expect(mention).ToHaveAttributeAsync("data-lexical-mention-selected", "");
        Assert.Equal("hi Luke Skywalker", await DomSelectionTextAsync(page));
    }

    [Fact] // a document saved while mentions were segmented imports them as atomic tokens
    public async Task SegmentedMentionJson_ImportsAsToken()
    {
        var page = await OpenAsync();
        var editor = page.Locator(SpacesEditor);
        var moduleUrl = Fx.BaseUrl + "_content/Blazor.Lexical/blazor-lexical.mjs";
        await InsertLukeAsync(page, "hi ");

        var json = await page.EvaluateAsync<string>(
            "async ([url, id]) => (await import(url)).getEditorStateJson(id)",
            new[] { moduleUrl, "editor-mention-spaces" });
        var legacy = Regex.Replace(
            json, "(\"mode\":)\"token\"(,[^{}]*\"type\":\"mention\")", "$1\"segmented\"$2");
        Assert.Contains("\"segmented\"", legacy);

        var reimported = await page.EvaluateAsync<string>(
            @"async ([url, id, state]) => {
                const mod = await import(url);
                mod.setEditorStateJson(id, state);
                return mod.getEditorStateJson(id);
            }",
            new[] { moduleUrl, "editor-mention-spaces", legacy });
        Assert.DoesNotContain("\"segmented\"", reimported);

        // Backspace from the end still removes the mention whole. Focusing a selection-less
        // editor puts the caret at the document end.
        await page.EvaluateAsync(
            "async ([url, id]) => (await import(url)).focus(id)",
            new[] { moduleUrl, "editor-mention-spaces" });
        await Expect(editor).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.PressAsync("Backspace");
        await Expect(editor.Locator("span[data-lexical-mention]")).ToHaveCountAsync(0);
        Assert.Equal("hi ", await TextContentAsync(editor));
    }

    // --- Clipping (#editor-mention-clip): the picker escapes an overflow:hidden container ---

    private const string ClipEditor = "#editor-mention-clip";
    private const string ClipMenu = ".harness-mention-clip .blazor-lexical__mention-menu";

    /// <summary>Scrolls the clip container to <paramref name="block"/> and opens a three-row picker.</summary>
    private static async Task<(LocatorBoundingBoxResult Menu, LocatorBoundingBoxResult Container)>
        OpenClippedPickerAsync(IPage page, string block)
    {
        await page.Locator($"{ClipEditor}[data-lexical-editor='true']").WaitForAsync();
        var container = page.Locator("#mention-clip-container");
        await container.EvaluateAsync("(el, block) => el.scrollIntoView({ block })", block);

        // Focus without Playwright's own scroll-into-view, which would move the page.
        await page.Locator(ClipEditor).FocusAsync();
        await page.Keyboard.TypeAsync("@a");
        var menu = page.Locator(ClipMenu);
        await Expect(menu).ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(menu.Locator("[data-lexical-mention-item]")).ToHaveCountAsync(3);

        var menuBox = await menu.BoundingBoxAsync();
        var containerBox = await container.BoundingBoxAsync();
        Assert.NotNull(menuBox);
        Assert.NotNull(containerBox);
        return (menuBox, containerBox);
    }

    private static void AssertInsideViewport(IPage page, LocatorBoundingBoxResult box)
    {
        var viewport = page.ViewportSize!;
        Assert.InRange(box.X, 0, viewport.Width - box.Width);
        Assert.InRange(box.Y, 0, viewport.Height - box.Height);
    }

    [Fact] // the open picker is taller than its overflow:hidden container and fully on screen
    public async Task Picker_EscapesOverflowHiddenContainer()
    {
        var page = await OpenAsync();

        var (menu, container) = await OpenClippedPickerAsync(page, "start");

        Assert.True(menu.Height > container.Height, $"menu {menu.Height}px vs container {container.Height}px");
        AssertInsideViewport(page, menu);
        // Room below: the picker opens below the caret.
        Assert.True(menu.Y > container.Y);
        await AssertPaintedOutsideAsync(page.Locator(ClipMenu), page.Locator("#mention-clip-container"));
    }

    [Fact] // with no room below the caret, the picker flips above it
    public async Task Picker_FlipsAbove_WhenNoRoomBelow()
    {
        var page = await OpenAsync();

        var (menu, container) = await OpenClippedPickerAsync(page, "end");

        AssertInsideViewport(page, menu);
        Assert.True(menu.Y + menu.Height <= container.Y + container.Height / 2,
            $"menu bottom {menu.Y + menu.Height} vs container middle {container.Y + container.Height / 2}");
    }
}
