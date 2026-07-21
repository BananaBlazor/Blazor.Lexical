namespace Blazor.Lexical;

/// <summary>
/// App-wide defaults for Blazor.Lexical, configured via
/// <see cref="ServiceCollectionExtensions.AddLexicalBlazor"/>.
/// </summary>
public sealed class LexicalOptions
{
    /// <summary>
    /// URL of the ESM glue module. Defaults to the self-hosted RCL asset.
    /// Override to serve from a CDN or custom host.
    /// </summary>
    public string ModuleUrl { get; set; } = "./_content/Blazor.Lexical/blazor-lexical.mjs";

    /// <summary>
    /// Optional default theme applied to editors that do not specify their own
    /// <see cref="LexicalEditor.Theme"/>. Prefer a <see cref="LexicalTheme"/> (e.g.
    /// <c>o.DefaultTheme = LexicalTheme.Default</c>); a raw anonymous object matching
    /// Lexical's <c>EditorThemeClasses</c> shape also works as an escape hatch.
    /// </summary>
    public object? DefaultTheme { get; set; }
}
