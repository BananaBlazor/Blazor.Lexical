using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;

namespace Tests.Integration;

/// <summary>
/// Boots Tests.Integration.Host in-process on a real Kestrel port (so a
/// real browser can reach it, including the Blazor Server WebSocket circuit)
/// and launches a headless browser. Shared across the whole integration suite.
/// </summary>
public sealed class HarnessFixture : IAsyncLifetime
{
    private const string DefaultChrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private RealServerFactory _factory = default!;
    private IPlaywright _playwright = default!;

    public IBrowser Browser { get; private set; } = default!;

    /// <summary>Base URL of the running host, e.g. http://127.0.0.1:49xxx/.</summary>
    public string BaseUrl { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        _factory = new RealServerFactory();
        // Accessing the server triggers CreateHost, which starts real Kestrel.
        _ = _factory.Server;
        BaseUrl = _factory.ServerAddress;

        _playwright = await Playwright.CreateAsync();

        var launchOptions = new BrowserTypeLaunchOptions { Headless = true };
        var chromePath = Environment.GetEnvironmentVariable("LEXICAL_TEST_CHROME");
        if (string.IsNullOrEmpty(chromePath) && File.Exists(DefaultChrome))
        {
            chromePath = DefaultChrome;
        }
        if (!string.IsNullOrEmpty(chromePath))
        {
            launchOptions.ExecutablePath = chromePath;
        }

        Browser = await _playwright.Chromium.LaunchAsync(launchOptions);
    }

    /// <summary>Opens the interop harness page in a fresh browser context.</summary>
    public async Task<IPage> OpenHarnessAsync()
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(BaseUrl + "interop-harness", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        // Wait until Lexical has taken over the main editor.
        await page.Locator("#editor-main[data-lexical-editor='true']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        return page;
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }
        _playwright?.Dispose();
        _factory?.Dispose();
    }

    /// <summary>
    /// WebApplicationFactory variant that hosts on a real Kestrel port instead
    /// of the in-memory TestServer. Pattern from the ASP.NET Core docs.
    /// </summary>
    private sealed class RealServerFactory : WebApplicationFactory<Program>
    {
        private IHost? _kestrelHost;

        public string ServerAddress
        {
            get
            {
                _ = Server; // ensure CreateHost has run
                return ClientOptions.BaseAddress.ToString();
            }
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Build the in-memory TestServer host first (what the base type expects).
            var testHost = builder.Build();

            // Rebuild using real Kestrel on an OS-assigned port.
            builder.ConfigureWebHost(webHost => webHost
                .UseKestrel()
                .UseUrls("http://127.0.0.1:0"));

            _kestrelHost = builder.Build();
            _kestrelHost.Start();

            var addresses = _kestrelHost.Services
                .GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!;
            ClientOptions.BaseAddress = addresses.Addresses.Select(a => new Uri(a)).Last();

            testHost.Start();
            return testHost;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _kestrelHost?.Dispose();
            }
        }
    }
}

[CollectionDefinition("harness")]
public sealed class HarnessCollection : ICollectionFixture<HarnessFixture>
{
}
