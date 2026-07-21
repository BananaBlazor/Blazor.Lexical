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
/// The two expensive things the whole suite shares: Tests.Integration.Host running on a
/// real Kestrel port (so a real browser can reach it, including the Blazor Server
/// WebSocket circuit) and a headless browser.
/// </summary>
/// <remarks>
/// Deliberately a process-wide singleton rather than an xunit collection fixture. Test
/// classes each own a <see cref="HarnessFixture"/> so they sit in <i>separate</i>
/// collections and run in parallel, but they must not each pay for a host and a browser.
/// <see cref="HarnessTestFramework"/> shuts the singleton down once the assembly is done.
/// </remarks>
internal sealed class HarnessServer
{
    private const string DefaultChrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static HarnessServer? _instance;

    private RealServerFactory _factory = default!;
    private IPlaywright _playwright = default!;

    public IBrowser Browser { get; private set; } = default!;

    /// <summary>Base URL of the running host, e.g. http://127.0.0.1:49xxx/.</summary>
    public string BaseUrl { get; private set; } = default!;

    /// <summary>Starts the host and browser on first call; hands back the same pair after.</summary>
    public static async Task<HarnessServer> GetAsync()
    {
        // The fast path: once started, every parallel class reads the same instance.
        if (_instance is { } started)
        {
            return started;
        }

        await Gate.WaitAsync();
        try
        {
            if (_instance is null)
            {
                var server = new HarnessServer();
                await server.StartAsync();
                _instance = server;
            }
            return _instance;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Tears the pair down. Called once, at assembly teardown.</summary>
    public static async Task ShutdownAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_instance is { } server)
            {
                _instance = null;
                await server.StopAsync();
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task StartAsync()
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

    private async Task StopAsync()
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
