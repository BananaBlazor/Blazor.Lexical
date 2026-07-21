using System.Collections.Generic;
using Microsoft.Playwright;

namespace Tests.Integration;

/// <summary>
/// Base for every integration test class. Deriving classes deliberately carry no
/// <c>[Collection]</c> attribute: each is its own collection, so xunit runs them in
/// parallel against the one shared host and browser.
/// </summary>
public abstract class HarnessTestBase : IClassFixture<HarnessFixture>
{
    protected readonly HarnessFixture Fx;

    protected HarnessTestBase(HarnessFixture fx) => Fx = fx;

    /// <summary>The harness page this class drives, relative to the host root.</summary>
    protected abstract string Route { get; }

    /// <summary>
    /// A selector satisfied only once <see cref="Route"/> is live — by convention the
    /// last editor declared on the page, since the editors boot in document order.
    /// </summary>
    protected abstract string ReadySelector { get; }

    /// <summary>Opens this class's harness page in a fresh browser context.</summary>
    protected Task<IPage> OpenAsync() => Fx.OpenAsync(Route, ReadySelector);

    /// <summary>Focuses a Lexical editor and types into it.</summary>
    protected static async Task TypeAsync(IPage page, string selector, string text)
    {
        await page.ClickAsync(selector);
        await page.Keyboard.TypeAsync(text);
    }

    /// <summary>
    /// Collects console errors raised while the page <i>loads</i> — including anything the
    /// editor logs during <c>create()</c>, which <see cref="CaptureErrors"/> misses because
    /// the harness has already booted by the time a test can attach.
    /// </summary>
    protected async Task<List<string>> CaptureErrorsFromLoadAsync(IPage page)
    {
        var errors = CaptureErrors(page);
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(ReadySelector)
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        return errors;
    }

    /// <summary>Collects browser console errors + page errors for the page's lifetime.</summary>
    protected static List<string> CaptureErrors(IPage page)
    {
        var errors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
            {
                errors.Add(msg.Text);
            }
        };
        page.PageError += (_, err) => errors.Add(err);
        return errors;
    }
}
