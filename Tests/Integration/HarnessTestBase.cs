using System.Collections.Generic;
using Microsoft.Playwright;

namespace Tests.Integration;

[Collection("harness")]
public abstract class HarnessTestBase
{
    protected readonly HarnessFixture Fx;

    protected HarnessTestBase(HarnessFixture fx) => Fx = fx;

    /// <summary>Focuses a Lexical editor and types into it.</summary>
    protected static async Task TypeAsync(IPage page, string selector, string text)
    {
        await page.ClickAsync(selector);
        await page.Keyboard.TypeAsync(text);
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
