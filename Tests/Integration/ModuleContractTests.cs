using System.Linq;
using Microsoft.Playwright;

namespace Tests.Integration;

/// <summary>
/// Guards the exact surface of the JS glue module that Blazor calls into. If a
/// refactor (or a Lexical npm bump that forces one) renames, drops, or adds an
/// exported function, this test fails — a single authoritative list of every
/// Blazor→JS touchpoint. Behavioral coverage of each lives in the other suites;
/// this one nails the names down.
/// </summary>
[Collection("harness")]
public class ModuleContractTests
{
    private readonly HarnessFixture _fx;

    public ModuleContractTests(HarnessFixture fx) => _fx = fx;

    /// <summary>
    /// The complete set of function exports LexicalEditor.razor.cs invokes on the
    /// module. Keep in lockstep with the C# InvokeAsync/InvokeVoidAsync call sites.
    /// </summary>
    private static readonly string[] ExpectedExports =
    [
        "create",
        "getText",
        "setText",
        "insertText",
        "getHtml",
        "setHtml",
        "getMarkdown",
        "setMarkdown",
        "getEditorStateJson",
        "setEditorStateJson",
        "setEditable",
        "insertTable",
        "insertUnorderedList",
        "insertOrderedList",
        "removeList",
        "toggleLink",
        "formatText",
        "formatAlignment",
        "setBlockType",
        "undo",
        "redo",
        "clearFormatting",
        "focus",
        "getMentions",
        "refreshMention",
        "refreshMentionsByValue",
        "invokeExtension",
        "setNotifications",
        "dispose",
    ];

    [Fact]
    public async Task Module_ExportsExactlyTheExpectedFunctions()
    {
        var page = await _fx.OpenHarnessAsync();
        var moduleUrl = _fx.BaseUrl + "_content/Blazor.Lexical/blazor-lexical.mjs";

        // Import the shipped bundle in the page and list its function exports.
        var actualExports = await page.EvaluateAsync<string[]>(
            @"async (url) => {
                const mod = await import(url);
                return Object.keys(mod).filter((k) => typeof mod[k] === 'function').sort();
            }",
            moduleUrl);

        Assert.Equal(ExpectedExports.OrderBy(n => n), actualExports);
    }

    [Fact]
    public async Task CoreBundle_ExcludesTableCode_UntilLazilyLoaded()
    {
        var page = await _fx.OpenHarnessAsync();
        var baseAssets = _fx.BaseUrl + "_content/Blazor.Lexical/";

        // Fetch the entry bundle and everything it *statically* imports (the shared
        // core chunk) — i.e. the modules every editor eagerly downloads — but not the
        // dynamic import() targets. @lexical/table serializes cells with the node type
        // "tablecell", which must appear only in the lazily-loaded table chunk.
        var eagerSources = await page.EvaluateAsync<string[]>(
            @"async (base) => {
                const seen = [];
                const visited = new Set();
                const walk = async (url) => {
                    if (visited.has(url)) return;
                    visited.add(url);
                    const text = await (await fetch(url)).text();
                    seen.push(text);
                    // Follow static imports ('from ""./x""') only — never import('...').
                    for (const m of text.matchAll(/from\s*['""](\.\/[^'""]+)['""]/g)) {
                        await walk(base + m[1].slice(2));
                    }
                };
                await walk(base + 'blazor-lexical.mjs');
                return seen;
            }",
            baseAssets);

        Assert.NotEmpty(eagerSources);
        Assert.All(eagerSources, src => Assert.DoesNotContain("tablecell", src));
    }

    [Fact]
    public async Task CoreBundle_ExcludesMarkdownCode_UntilLazilyLoaded()
    {
        var page = await _fx.OpenHarnessAsync();
        var baseAssets = _fx.BaseUrl + "_content/Blazor.Lexical/";

        // Fetch the entry bundle and everything it *statically* imports (the shared
        // core chunk) — the modules every editor eagerly downloads — but not the
        // dynamic import() targets. @lexical/markdown (and the @lexical/code-core it
        // pulls in for fenced code blocks) is loaded only on the first getMarkdown/
        // setMarkdown call, so the "code-highlight" token (a @lexical/code-core class
        // name) must appear only in the lazily-loaded markdown chunk.
        var eagerSources = await page.EvaluateAsync<string[]>(
            @"async (base) => {
                const seen = [];
                const visited = new Set();
                const walk = async (url) => {
                    if (visited.has(url)) return;
                    visited.add(url);
                    const text = await (await fetch(url)).text();
                    seen.push(text);
                    // Follow static imports ('from ""./x""') only — never import('...').
                    for (const m of text.matchAll(/from\s*['""](\.\/[^'""]+)['""]/g)) {
                        await walk(base + m[1].slice(2));
                    }
                };
                await walk(base + 'blazor-lexical.mjs');
                return seen;
            }",
            baseAssets);

        Assert.NotEmpty(eagerSources);
        Assert.All(eagerSources, src => Assert.DoesNotContain("code-highlight", src));
    }
}
