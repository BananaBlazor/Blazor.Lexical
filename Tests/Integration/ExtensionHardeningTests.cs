using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// Proves the extension loader fails <i>soft</i>: each harness editor here nests a pair
/// of colliding modules, and in every case the second is skipped with a console error
/// while the editor stays alive and typeable.
/// </summary>
/// <remarks>
/// Lexical's own <c>@lexical/extension</c> refuses to build the editor at all on these
/// collisions. We diverge deliberately — a broken extension must never take the editor
/// down with it — so these tests assert both halves: the offender is gone, and the
/// editor still works.
/// </remarks>
public class ExtensionHardeningTests : HarnessTestBase
{
    public ExtensionHardeningTests(HarnessFixture fx) : base(fx) { }

    // The loaded-marker is written onto the editor ROOT, which carries the CssClass;
    // the Id sits on the [data-lexical-content] surface inside it.

    [Fact] // two modules claiming one name: the second is skipped
    public async Task DuplicateName_SkipsTheSecondModule()
    {
        var page = await Fx.OpenHarnessAsync();
        // The collisions are logged during create(), so the listener has to be in place
        // before the page loads.
        var errors = await CaptureErrorsFromLoadAsync(page);

        await Expect(page.Locator(".harness-collide-name")).ToHaveAttributeAsync(
            "data-harness-loaded", "name-a");

        Assert.Contains(errors, e => e.Contains("harness/duplicate") && e.Contains("skipped"));
    }

    [Fact] // two node classes claiming one getType(): the second is skipped
    public async Task DuplicateNodeType_SkipsTheSecondModule()
    {
        var page = await Fx.OpenHarnessAsync();
        // The collisions are logged during create(), so the listener has to be in place
        // before the page loads.
        var errors = await CaptureErrorsFromLoadAsync(page);

        await Expect(page.Locator(".harness-collide-nodetype")).ToHaveAttributeAsync(
            "data-harness-loaded", "node-a");

        Assert.Contains(
            errors,
            e => e.Contains("harness-collide") && e.Contains("already registered"));
    }

    [Fact] // an explicitly declared conflict is honoured
    public async Task DeclaredConflict_SkipsTheConflictingModule()
    {
        var page = await Fx.OpenHarnessAsync();
        // The collisions are logged during create(), so the listener has to be in place
        // before the page loads.
        var errors = await CaptureErrorsFromLoadAsync(page);

        await Expect(page.Locator(".harness-collide-declared")).ToHaveAttributeAsync(
            "data-harness-loaded", "conflict-a");

        Assert.Contains(errors, e => e.Contains("conflicting"));
    }

    [Theory] // the point of failing soft: a collision costs an extension, not the editor
    [InlineData("#editor-collide-name")]
    [InlineData("#editor-collide-nodetype")]
    [InlineData("#editor-collide-declared")]
    public async Task ACollision_LeavesTheEditorAliveAndTypeable(string editor)
    {
        var page = await Fx.OpenHarnessAsync();

        await TypeAsync(page, editor, "still works");

        await Expect(page.Locator(editor)).ToContainTextAsync("still works");
    }
}
