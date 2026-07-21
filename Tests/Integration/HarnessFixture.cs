using Microsoft.Playwright;

namespace Tests.Integration;

/// <summary>
/// The per-test-class handle on the shared host and browser. It is an
/// <c>IClassFixture</c> rather than a collection fixture on purpose: that is what lets
/// each test class sit in its own collection and run in parallel with the others. The
/// expensive resources behind it are started once, by <see cref="HarnessServer"/>.
/// </summary>
public sealed class HarnessFixture : IAsyncLifetime
{
    private HarnessServer _server = default!;

    public IBrowser Browser => _server.Browser;

    /// <summary>Base URL of the running host, e.g. http://127.0.0.1:49xxx/.</summary>
    public string BaseUrl => _server.BaseUrl;

    public async Task InitializeAsync() => _server = await HarnessServer.GetAsync();

    /// <summary>
    /// Opens one harness page in a fresh browser context and waits for Lexical to take
    /// it over. The suite has one page per feature area, so a test boots only the
    /// editors it drives — <paramref name="readySelector"/> is the page's own gate.
    /// </summary>
    /// <param name="route">Route relative to the host root, e.g. <c>harness/marks</c>.</param>
    /// <param name="readySelector">
    /// A selector satisfied only once the page's editors are live, conventionally the
    /// last editor declared on it.
    /// </param>
    public async Task<IPage> OpenAsync(string route, string readySelector)
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(BaseUrl + route, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.Locator(readySelector)
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        return page;
    }

    // The shared host and browser outlive any one class; HarnessTestFramework closes them.
    public Task DisposeAsync() => Task.CompletedTask;
}
