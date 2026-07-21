using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace Tests.UnitTests;

/// <summary>
/// Guards the shape of the shipped NuGet package. The JS bundle is generated at build
/// time with content-hashed chunk names, so it is registered as a static web asset by a
/// custom target (<c>RegisterLexicalBundle</c>) rather than by the SDK's evaluation-time
/// wwwroot glob. Hooking that target one step too late once put the whole bundle in the
/// package's legacy <c>content/</c> folder instead of <c>staticwebassets/</c>: everything
/// still built, every other test still passed, and consumers got a 404 on
/// <c>_content/Blazor.Lexical/blazor-lexical.mjs</c>. Nothing but packing catches that,
/// so this test packs.
/// </summary>
public class PackageLayoutTests
{
    /// <summary>
    /// Every file in the library's wwwroot must ship under <c>staticwebassets/</c> — that
    /// is the only package folder Blazor maps to <c>_content/Blazor.Lexical/</c>.
    /// </summary>
    [Fact]
    public void Package_ships_every_wwwroot_asset_as_a_static_web_asset()
    {
        var entries = PackContents();

        var expected = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "Source", "Blazor.Lexical", "wwwroot"))
            .Select(Path.GetFileName)
            .ToList();

        // Sanity: the bundle is generated, so an empty wwwroot would make this vacuous.
        Assert.Contains("blazor-lexical.mjs", expected);
        Assert.Contains(expected, f => f!.StartsWith("blazor-lexical-chunk-", StringComparison.Ordinal));

        foreach (var file in expected)
        {
            Assert.Contains($"staticwebassets/{file}", entries);
        }
    }

    /// <summary>
    /// The same assets must not also land in <c>content/</c> or <c>contentFiles/</c>. Those
    /// are the legacy NuGet content folders — Blazor never serves them, and their presence
    /// is the signature of the assets having missed the static web asset pipeline.
    /// </summary>
    [Fact]
    public void Package_does_not_ship_wwwroot_assets_as_legacy_content()
    {
        var entries = PackContents();

        Assert.DoesNotContain(entries, e =>
            e.StartsWith("content/", StringComparison.Ordinal) ||
            e.StartsWith("contentFiles/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The XML documentation file must ride along, since it is what gives consuming IDEs
    /// hover tooltips for the public SDK.
    /// </summary>
    [Fact]
    public void Package_ships_the_xml_documentation_file()
    {
        Assert.Contains("lib/net10.0/Blazor.Lexical.xml", PackContents());
    }

    // --- packing -----------------------------------------------------------------

    private static readonly Lazy<IReadOnlyList<string>> Packed = new(Pack, isThreadSafe: true);

    /// <summary>Entry names of the freshly packed .nupkg; packed once per test run.</summary>
    private static IReadOnlyList<string> PackContents() => Packed.Value;

    private static readonly string RepoRoot = FindRepoRoot();

    private static IReadOnlyList<string> Pack()
    {
        // Pack in the configuration this test assembly was built in so the pack reuses the
        // existing build output instead of provoking a second full build (JS bundle included).
        var configuration = typeof(PackageLayoutTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

        var output = Path.Combine(Path.GetTempPath(), "Blazor.Lexical.PackGuard", Path.GetRandomFileName());
        Directory.CreateDirectory(output);

        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("pack");
            psi.ArgumentList.Add(Path.Combine("Source", "Blazor.Lexical", "Blazor.Lexical.csproj"));
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(configuration);
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(output);

            // `dotnet test` exports MSBuild's own environment; inheriting it makes the nested
            // build resolve the wrong SDK. Drop those variables for the child process only.
            foreach (var key in psi.Environment.Keys
                         .Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase) ||
                                     k.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                psi.Environment.Remove(key);
            }

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("could not start `dotnet pack`");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"`dotnet pack` failed ({process.ExitCode}):\n{stdout}\n{stderr}");

            var nupkg = Assert.Single(Directory.GetFiles(output, "*.nupkg"));
            using var archive = ZipFile.OpenRead(nupkg);
            return [.. archive.Entries.Select(e => e.FullName)];
        }
        finally
        {
            try
            {
                Directory.Delete(output, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing the run over.
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Blazor.Lexical.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("could not locate the repository root");
    }
}
