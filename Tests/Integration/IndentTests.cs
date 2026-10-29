using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// End-to-end coverage of <c>&lt;LexicalIndent /&gt;</c>, the <c>indent:in</c>/<c>indent:out</c>
/// tokens and indentable block decorators. Levels are read from the editor state: the
/// harness decorator extension mirrors each block's level onto its DOM as
/// <c>data-harness-indent</c>, so a read does not take focus off the editor.
/// <list type="bullet">
/// <item><c>#editor-indent</c>: <c>LexicalIndent MaxIndent="3"</c> alone, opted-in and plain
/// decorators, and an opted-in decorator inside a list item.</item>
/// <item><c>#editor-indent-tab-first</c> / <c>-tab-last</c>: both components in opposite
/// orders; <c>LexicalIndent MaxIndent="2" IndentListItems="false"</c> must win.</item>
/// <item><c>#editor-indent-flat</c>: <c>IndentListItems="false"</c> over a seeded nested list.</item>
/// <item><c>#editor-indent-uncapped</c>: indent buttons with no indent extension.</item>
/// </list>
/// </summary>
public class IndentTests : HarnessTestBase
{
    private const string Main = "#editor-indent";
    private const string AtMargin = "Already at the left margin";
    private const string AtCap = "Maximum indent reached";
    private const string NotIndentable = "Indenting does not apply to this selection";
    private static readonly Regex Selected = new(@"\bharness-block--selected\b");

    public IndentTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/indent";

    protected override string ReadySelector =>
        "#editor-indent-uncapped[data-lexical-editor='true']";

    private static ILocator IndentButton(IPage page, string editor) =>
        page.Locator($".harness-{editor} [data-lexical-command='indent:in']");

    private static ILocator OutdentButton(IPage page, string editor) =>
        page.Locator($".harness-{editor} [data-lexical-command='indent:out']");

    private static ILocator Paragraph(IPage page, string editorId, string text) =>
        page.Locator($"{editorId} p", new() { HasText = text });

    private static ILocator Block(IPage page, string editorId, string label) =>
        page.Locator($"{editorId} .harness-block", new() { HasText = label });

    private static Task ExpectLevelAsync(ILocator block, int level) =>
        Expect(block).ToHaveAttributeAsync("data-harness-indent", level.ToString());

    private static async Task ExpectDisabledAsync(ILocator button, string reason)
    {
        await Expect(button).ToHaveAttributeAsync("data-lexical-disabled", reason);
        await Expect(button).ToHaveAttributeAsync("title", reason);
        await Expect(button).ToHaveAttributeAsync("aria-label", reason);
        await Expect(button).ToHaveAttributeAsync("aria-disabled", "true");
    }

    private static async Task ExpectEnabledAsync(ILocator button, string title)
    {
        await Expect(button).Not.ToHaveAttributeAsync("data-lexical-disabled", new Regex(".*"));
        await Expect(button).ToHaveAttributeAsync("title", title);
        await Expect(button).ToHaveAttributeAsync("aria-label", title);
    }

    /// <summary>Clicks into <paramref name="block"/> and moves the caret to its start.</summary>
    private static async Task CaretAtStartAsync(ILocator block)
    {
        await block.ClickAsync();
        await block.Page.Keyboard.PressAsync("Home");
    }

    // --- buttons on paragraphs -----------------------------------------------------------

    [Fact] // the default toolbar shows Outdent then Indent once LexicalIndent is nested
    public async Task Toolbar_ShowsIndentButtons_WithLexicalIndent()
    {
        var page = await OpenAsync();

        await Expect(IndentButton(page, "indent")).ToHaveCountAsync(1);
        await Expect(OutdentButton(page, "indent")).ToHaveCountAsync(1);
        await Expect(page.Locator(".harness-indent .blazor-lexical__toolbar [data-lexical-command^='indent:']"))
            .ToHaveCountAsync(2);
    }

    [Fact] // the buttons indent and outdent the caret's paragraph
    public async Task Buttons_IndentAndOutdentAParagraph()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var alpha = Paragraph(page, Main, "alpha");

        await alpha.ClickAsync();
        await IndentButton(page, "indent").ClickAsync();
        await IndentButton(page, "indent").ClickAsync();
        await ExpectLevelAsync(alpha, 2);

        await OutdentButton(page, "indent").ClickAsync();
        await ExpectLevelAsync(alpha, 1);
        await ExpectLevelAsync(Paragraph(page, Main, "beta"), 0);
        Assert.Empty(errors);
    }

    [Fact] // MaxIndent="3" allows levels below 3: Indent disables at 2 with the cap reason
    public async Task IndentButton_DisablesAtTheCap()
    {
        var page = await OpenAsync();
        var alpha = Paragraph(page, Main, "alpha");
        var indent = IndentButton(page, "indent");

        await alpha.ClickAsync();
        await ExpectEnabledAsync(indent, "Indent");
        await indent.ClickAsync();
        await indent.ClickAsync();
        await ExpectLevelAsync(alpha, 2);

        await ExpectDisabledAsync(indent, AtCap);
        await indent.ClickAsync(new() { Force = true });
        await ExpectLevelAsync(alpha, 2);
        await ExpectEnabledAsync(OutdentButton(page, "indent"), "Outdent");
    }

    [Fact] // at level 0 Outdent is disabled; it re-enables (title restored) when the selection moves
    public async Task OutdentButton_DisablesAtTheMargin_AndReEnablesOnSelectionChange()
    {
        var page = await OpenAsync();
        var outdent = OutdentButton(page, "indent");

        await Paragraph(page, Main, "alpha").ClickAsync();
        await ExpectDisabledAsync(outdent, AtMargin);

        await IndentButton(page, "indent").ClickAsync();
        await ExpectEnabledAsync(outdent, "Outdent");

        await Paragraph(page, Main, "beta").ClickAsync();
        await ExpectDisabledAsync(outdent, AtMargin);

        await Paragraph(page, Main, "alpha").ClickAsync();
        await ExpectEnabledAsync(outdent, "Outdent");
        await Expect(outdent).Not.ToHaveAttributeAsync("data-lexical-title", new Regex(".*"));
    }

    [Fact] // read-only disables both with the not-indentable reason; editable again re-enables
    public async Task ReadOnly_DisablesBothButtons()
    {
        var page = await OpenAsync();
        var indent = IndentButton(page, "indent");
        var outdent = OutdentButton(page, "indent");

        await Paragraph(page, Main, "alpha").ClickAsync();
        await ExpectEnabledAsync(indent, "Indent");

        await page.ClickAsync("#btn-indent-readonly");
        await ExpectDisabledAsync(indent, NotIndentable);
        await ExpectDisabledAsync(outdent, NotIndentable);

        await page.ClickAsync("#btn-indent-readonly");
        await Paragraph(page, Main, "alpha").ClickAsync();
        await ExpectEnabledAsync(indent, "Indent");
    }

    // --- indentable decorators -----------------------------------------------------------

    [Fact] // a node-selected opted-in decorator moves with the buttons, clamped, with disabled state
    public async Task Buttons_MoveANodeSelectedDecorator_Clamped()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var block = Block(page, Main, "A");
        var indent = IndentButton(page, "indent");
        var outdent = OutdentButton(page, "indent");

        await block.ClickAsync();
        await Expect(block).ToHaveClassAsync(Selected);
        await ExpectDisabledAsync(outdent, AtMargin);

        await indent.ClickAsync();
        await indent.ClickAsync();
        await Expect(block).ToHaveAttributeAsync("data-indent", "2");
        await ExpectDisabledAsync(indent, AtCap);

        await outdent.ClickAsync();
        await Expect(block).ToHaveAttributeAsync("data-indent", "1");
        await ExpectEnabledAsync(indent, "Indent");
        await Expect(block).ToHaveClassAsync(Selected);
        Assert.Empty(errors);
    }

    [Fact] // a decorator that does not opt in is not governed: both buttons disabled, level untouched
    public async Task PlainDecorator_IsNotIndentable()
    {
        var page = await OpenAsync();
        var block = page.Locator($"{Main} .harness-block--plain");

        await block.ClickAsync();
        await Expect(block).ToHaveClassAsync(Selected);

        await ExpectDisabledAsync(IndentButton(page, "indent"), NotIndentable);
        await ExpectDisabledAsync(OutdentButton(page, "indent"), NotIndentable);
        await Expect(block).ToHaveAttributeAsync("data-indent", "0");
    }

    [Fact] // undo restores the level, one step per indent, and keeps the node selection
    public async Task Undo_RestoresTheDecoratorLevel_AndKeepsTheSelection()
    {
        var page = await OpenAsync();
        var block = Block(page, Main, "A");
        var indent = IndentButton(page, "indent");

        await block.ClickAsync();
        await indent.ClickAsync();
        await indent.ClickAsync();
        await Expect(block).ToHaveAttributeAsync("data-indent", "2");

        await page.Keyboard.PressAsync("Control+Z");
        await Expect(block).ToHaveAttributeAsync("data-indent", "1");
        await Expect(block).ToHaveClassAsync(Selected);

        // The selection survived as a node selection: the next step acts on the block again.
        await indent.ClickAsync();
        await Expect(block).ToHaveAttributeAsync("data-indent", "2");
    }

    [Fact] // the level rides in exportJSON and comes back through updateFromJSON
    public async Task DecoratorLevel_SurvivesAStateJsonRoundTrip()
    {
        var page = await OpenAsync();
        var block = Block(page, Main, "A");

        await block.ClickAsync();
        await IndentButton(page, "indent").ClickAsync();
        await IndentButton(page, "indent").ClickAsync();
        await Expect(block).ToHaveAttributeAsync("data-indent", "2");

        await page.ClickAsync("#btn-indent-roundtrip");

        await Expect(page.Locator("#indent-roundtrip")).ToHaveTextAsync("A:2 N:0");
        await Expect(Block(page, Main, "A")).ToHaveAttributeAsync("data-indent", "2");
    }

    [Fact] // a range over paragraphs and decorators indents each governed block exactly once
    public async Task RangeSelection_IndentsParagraphsAndTopLevelDecorators_Once()
    {
        var page = await OpenAsync();

        await Paragraph(page, Main, "alpha").ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await IndentButton(page, "indent").ClickAsync();

        await ExpectLevelAsync(Paragraph(page, Main, "alpha"), 1);
        await ExpectLevelAsync(Paragraph(page, Main, "beta"), 1);
        await Expect(Block(page, Main, "A")).ToHaveAttributeAsync("data-indent", "1");
        // Not opted in: untouched.
        await Expect(page.Locator($"{Main} .harness-block--plain")).ToHaveAttributeAsync("data-indent", "0");
        // Inside a list item: the item nests once, the decorator itself stays at 0.
        await Expect(page.Locator($"{Main} ul ul li")).ToHaveCountAsync(1);
        await Expect(Block(page, Main, "N")).ToHaveAttributeAsync("data-indent", "0");
    }

    // --- both components: Tab follows LexicalIndent's policy in either order -------------

    [Theory] // LexicalIndent's MaxIndent="2" caps Tab, not LexicalTabIndent's "5"
    [InlineData("tab-first")]
    [InlineData("tab-last")]
    public async Task Tab_FollowsLexicalIndentsCap_InEitherOrder(string editor)
    {
        var page = await OpenAsync();
        var para = Paragraph(page, $"#editor-indent-{editor}", "para");

        await CaretAtStartAsync(para);
        for (var i = 0; i < 4; i++)
        {
            await page.Keyboard.PressAsync("Tab");
        }

        await ExpectLevelAsync(para, 1);
        await ExpectDisabledAsync(IndentButton(page, $"indent-{editor}"), "Two levels at most");
    }

    [Theory] // IndentListItems="false" also binds Tab, in either order
    [InlineData("tab-first")]
    [InlineData("tab-last")]
    public async Task Tab_LeavesListItemsFlat_InEitherOrder(string editor)
    {
        var page = await OpenAsync();
        var id = $"#editor-indent-{editor}";

        await CaretAtStartAsync(page.Locator($"{id} li", new() { HasText = "two" }));
        await page.Keyboard.PressAsync("Tab");

        await Expect(page.Locator($"{id} ul ul")).ToHaveCountAsync(0);
        await Expect(page.Locator($"{id} li", new() { HasText = "two" })).ToHaveTextAsync("two");
        await ExpectDisabledAsync(IndentButton(page, $"indent-{editor}"), NotIndentable);
    }

    [Theory] // Tab/Shift+Tab move a node-selected opted-in decorator, clamped
    [InlineData("tab-first")]
    [InlineData("tab-last")]
    public async Task Tab_MovesANodeSelectedDecorator(string editor)
    {
        var page = await OpenAsync();
        var block = Block(page, $"#editor-indent-{editor}", "T");

        await block.ClickAsync();
        await Expect(block).ToHaveClassAsync(Selected);
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Tab");
        await Expect(block).ToHaveAttributeAsync("data-indent", "1");

        await page.Keyboard.PressAsync("Shift+Tab");
        await Expect(block).ToHaveAttributeAsync("data-indent", "0");
        await Expect(block).ToHaveClassAsync(Selected);
    }

    // --- IndentListItems="false" ---------------------------------------------------------

    [Fact] // list items are excluded: both buttons disabled with the not-indentable reason
    public async Task ListItems_AreNotIndentable_WhenExcluded()
    {
        var page = await OpenAsync();

        // The text itself: the outer item's box also holds the nested list.
        await page.Locator("#editor-indent-flat [data-lexical-text]", new() { HasText = "outer" }).ClickAsync();

        await ExpectDisabledAsync(IndentButton(page, "indent-flat"), NotIndentable);
        await ExpectDisabledAsync(OutdentButton(page, "indent-flat"), NotIndentable);

        await Paragraph(page, "#editor-indent-flat", "after").ClickAsync();
        await ExpectEnabledAsync(IndentButton(page, "indent-flat"), "Indent");
    }

    [Fact] // Backspace at the front of a nested item no longer outdents it
    public async Task Backspace_DoesNotOutdentANestedListItem_WhenExcluded()
    {
        var page = await OpenAsync();
        var nested = page.Locator("#editor-indent-flat ul ul li");
        await Expect(nested).ToHaveTextAsync("nested");

        await CaretAtStartAsync(nested);
        await page.Keyboard.PressAsync("Backspace");

        await Expect(nested).ToHaveCountAsync(1);
        await Expect(nested).ToHaveTextAsync("nested");
        await Expect(page.Locator("#editor-indent-flat")).ToContainTextAsync("outer");

        // Elsewhere in the item Backspace still deletes.
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Backspace");
        await Expect(nested).ToHaveTextAsync("neste");
    }

    // --- no indent extension -------------------------------------------------------------

    [Fact] // without LexicalIndent the tokens reach the stock handlers: uncapped, never disabled
    public async Task Tokens_IndentUncapped_WithoutAnIndentExtension()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var indent = IndentButton(page, "indent-uncapped");

        await TypeAsync(page, "#editor-indent-uncapped", "free");
        for (var i = 0; i < 5; i++)
        {
            await indent.ClickAsync();
        }

        await ExpectLevelAsync(Paragraph(page, "#editor-indent-uncapped", "free"), 5);
        await Expect(page.Locator(".harness-indent-uncapped [data-lexical-disabled]")).ToHaveCountAsync(0);
        Assert.Empty(errors);
    }
}
