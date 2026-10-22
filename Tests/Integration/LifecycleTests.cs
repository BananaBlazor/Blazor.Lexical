using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// An editor the host removes while it is still starting up — during the module import
/// or during JS <c>create()</c> — must go quietly: no exception (which on Server kills
/// the circuit and shows the error UI), no console error, and no <c>OnReady</c>.
/// </summary>
public class LifecycleTests : HarnessTestBase
{
    public LifecycleTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/lifecycle";

    protected override string ReadySelector =>
        "#editor-lifecycle[data-lexical-editor='true']";

    [Fact] // removed as soon as startup begins: disposal lands during the module import
    public async Task RemovedDuringImport_IsSilent_AndNeverReady()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        for (var i = 1; i <= 5; i++)
        {
            await FlashAsync(page, delayMs: 0, expectedCount: i);
        }
        await SettleAsync(page);

        await Expect(page.Locator("#flash-ready-count")).ToHaveTextAsync("0");
        await AssertCircuitHealthyAsync(page, errors, expectedCount: 6);
    }

    [Fact] // removed a little later each time, so some removals land during JS create()
    public async Task RemovedAtVaryingPointsInStartup_IsSilent()
    {
        var page = await OpenAsync();
        var errors = CaptureErrors(page);

        int[] delays = [5, 15, 30, 60, 100, 200, 400];
        for (var i = 0; i < delays.Length; i++)
        {
            await FlashAsync(page, delays[i], expectedCount: i + 1);
        }
        await SettleAsync(page);

        await AssertCircuitHealthyAsync(page, errors, expectedCount: delays.Length + 1);
    }

    /// <summary>Mounts the flash editor and waits until the host has removed it again.</summary>
    private static async Task FlashAsync(IPage page, int delayMs, int expectedCount)
    {
        await page.FillAsync("#flash-delay", delayMs.ToString());
        await page.ClickAsync("#btn-flash");
        await Expect(page.Locator("#flash-count")).ToHaveTextAsync(expectedCount.ToString());
        await Expect(page.Locator("#editor-flash")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Removal is immediate, but the deferred teardown runs only once the in-flight
    /// import/create round trips settle — give any late failure time to surface.
    /// </summary>
    private static Task SettleAsync(IPage page) => page.WaitForTimeoutAsync(1000);

    private static async Task AssertCircuitHealthyAsync(
        IPage page, List<string> errors, int expectedCount)
    {
        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.Empty(errors);

        // One more round trip proves the circuit is still alive and rendering.
        await FlashAsync(page, delayMs: 0, expectedCount);
    }
}
