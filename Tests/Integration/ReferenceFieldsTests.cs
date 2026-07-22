using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Covers the SDK's two app-agnostic JS primitives (<c>setup.primitives</c>) end to end,
/// through the reference-fields worked example (Samples/Extensions.ReferenceFields) — the
/// sample IS the vehicle that consumes the primitives, so the assertions read its DOM and
/// its commit/resolve callbacks:
/// <list type="bullet">
/// <item><b>Ghost completion</b> — the muted suffix renders after the caret
/// (<c>[data-lexical-ghost]</c>) and, crucially, is never part of the document / state JSON
/// / history.</item>
/// <item><b>Entity commit</b> — prefix matching, <c>cycle()</c> over alternates, Tab/Enter
/// accept, the optimistic create + resolve swap, and the empty-commit no-op.</item>
/// </list>
/// </summary>
public class ReferenceFieldsTests : HarnessTestBase
{
    public ReferenceFieldsTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/reference-fields";

    protected override string ReadySelector =>
        "#editor-reference[data-lexical-editor='true']";

    private const string Editor = "#editor-reference";
    // The ghost overlay lives under the editor's outer root (which carries the CssClass),
    // not under the content element that holds the Id — that placement outside the
    // contenteditable is the whole invariant.
    private const string Ghost = ".harness-reference [data-lexical-ghost]";

    [Fact] // typing "par" ghosts in the rest of "Paris" after the caret
    public async Task Ghost_RendersSuffix_AfterTheCaret()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);
        var ghost = page.Locator(Ghost);

        await TypeAsync(page, Editor, "par");

        await Expect(ghost).ToHaveAttributeAsync("data-lexical-visible", "");
        await Expect(ghost).ToHaveTextAsync("is");
        Assert.Empty(errors);
    }

    [Fact] // invariant: the ghost suffix is in neither getTextContent() nor the state JSON
    public async Task Ghost_IsNeverInTheDocumentOrState()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Editor, "par");
        await Expect(page.Locator(Ghost)).ToHaveTextAsync("is");

        await page.ClickAsync("#btn-ref-read");

        // The field holds exactly what was typed — the ghost added nothing.
        await Expect(page.Locator("#ref-doc-text")).ToHaveTextAsync("par");
        // "is" / "Paris" never leaked into the serialized state either.
        await Expect(page.Locator("#ref-state-json")).Not.ToContainTextAsync("Paris");
        await Expect(page.Locator("#ref-state-json")).Not.ToContainTextAsync("paris");
    }

    [Fact] // the ghost adds no undo step: one Ctrl+Z removes the typed text wholesale
    public async Task Ghost_AddsNoUndoStep()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Editor, "par");
        await Expect(page.Locator(Ghost)).ToHaveTextAsync("is");

        await page.Keyboard.PressAsync("Control+z");

        await page.ClickAsync("#btn-ref-read");
        // The typing undoes in a single step down to empty — the ghost was never a step,
        // and its "is" certainly never became real through undo.
        await Expect(page.Locator("#ref-doc-text")).ToHaveTextAsync("");
    }

    [Fact] // Enter accepts the best match: the field is completed and the entity committed
    public async Task Accept_CompletesField_AndCommitsEntity()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Editor, "par");
        await Expect(page.Locator(Ghost)).ToHaveTextAsync("is");
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator(Editor)).ToContainTextAsync("Paris");
        await Expect(page.Locator("#ref-committed-id")).ToHaveTextAsync("paris");
        await Expect(page.Locator("#ref-committed-text")).ToHaveTextAsync("Paris");
        await Expect(page.Locator("#ref-committed-created")).ToHaveTextAsync("False");
        await Expect(page.Locator("#ref-committed-provisional")).ToHaveTextAsync("False");
        // The ghost is gone once the field matches its entity exactly (empty suffix).
        await Expect(page.Locator(Ghost)).Not.ToHaveAttributeAsync("data-lexical-visible", "");
    }

    [Fact] // ArrowDown cycles the active match (Paris -> Parma), re-pointing the ghost
    public async Task Cycle_AdvancesAlternates()
    {
        var page = await OpenAsync();
        var ghost = page.Locator(Ghost);

        await TypeAsync(page, Editor, "par");
        await Expect(ghost).ToHaveTextAsync("is"); // Paris

        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(ghost).ToHaveTextAsync("ma"); // Parma

        // The inspect readout agrees: Parma is now best, Paris the alternate.
        await page.ClickAsync("#btn-ref-inspect");
        await Expect(page.Locator("#ref-best")).ToHaveTextAsync("Parma");
        await Expect(page.Locator("#ref-alternates")).ToHaveTextAsync("Paris");
    }

    [Fact] // a city not in the pool commits optimistically, then resolves to a real id
    public async Task Create_IsOptimistic_ThenResolves()
    {
        var page = await OpenAsync();

        await TypeAsync(page, Editor, "Quito");
        await page.Keyboard.PressAsync("Enter");

        // The optimistic commit fires immediately with a provisional id and both flags set.
        await Expect(page.Locator("#ref-committed-text")).ToHaveTextAsync("Quito");
        await Expect(page.Locator("#ref-committed-created")).ToHaveTextAsync("True");
        await Expect(page.Locator("#ref-committed-provisional")).ToHaveTextAsync("True");
        await Expect(page.Locator("#ref-committed-id")).ToContainTextAsync("provisional:");

        // Then the simulated backend resolves and the real id is swapped in.
        await Expect(page.Locator("#ref-resolved-real")).ToHaveTextAsync("city-quito");
        await Expect(page.Locator("#ref-resolved-provisional")).ToContainTextAsync("provisional:");
    }

    [Fact] // committing an empty field is a no-op — nothing fires
    public async Task EmptyCommit_IsNoOp()
    {
        var page = await OpenAsync();

        await page.ClickAsync(Editor);
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("#ref-commit-count")).ToHaveTextAsync("0");
        await Expect(page.Locator("#ref-committed-id")).ToHaveTextAsync("");
    }

    [Fact] // the ghost toggle demonstrates attach's returned teardown (element removed / re-added)
    public async Task GhostToggle_TearsDownAndReattaches()
    {
        var page = await OpenAsync();
        var ghost = page.Locator(Ghost);

        await TypeAsync(page, Editor, "par");
        await Expect(ghost).ToHaveTextAsync("is");

        // Off runs the teardown: the overlay element is removed from the DOM entirely.
        await page.ClickAsync("#btn-ref-ghost-off");
        await Expect(ghost).ToHaveCountAsync(0);

        // On re-attaches: the overlay element exists again.
        await page.ClickAsync("#btn-ref-ghost-on");
        await Expect(ghost).ToHaveCountAsync(1);
    }
}
