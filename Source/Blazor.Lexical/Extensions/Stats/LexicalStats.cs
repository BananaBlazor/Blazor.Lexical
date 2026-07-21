using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// Adds live document statistics — word/character counts, paragraphs, reading time — to
/// a <see cref="LexicalEditor"/>. Nest <c>&lt;LexicalStats /&gt;</c> inside the editor
/// and either point it at an element to write into (<see cref="TargetSelector"/>), or
/// subscribe to <see cref="OnStatsChanged"/>, or both.
/// </summary>
/// <remarks>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalStats TargetSelector="#count" Template="{words} words · {readingMinutes} min read" /&gt;
/// &lt;/LexicalEditor&gt;
/// &lt;p id="count"&gt;&lt;/p&gt;
/// </code>
/// <para>
/// The target surface is entirely client-side: JS computes the numbers and writes the
/// formatted line, so a word counter costs no interop at all. The .NET push is the opt-in
/// half — with no <see cref="OnStatsChanged"/> delegate this extension never calls back.
/// Either way the document is only ever read.
/// </para>
/// </remarks>
public sealed class LexicalStats : LexicalExtension
{
    internal override string? BuiltIn => "stats";

    /// <summary>
    /// CSS selector of an element whose text is replaced with the formatted
    /// <see cref="Template"/> on every change. Leave unset to receive the numbers in .NET
    /// only. The element must exist when the editor is created (no rescan).
    /// </summary>
    [Parameter] public string? TargetSelector { get; set; }

    /// <summary>
    /// The line written into <see cref="TargetSelector"/>. Tokens <c>{characters}</c>,
    /// <c>{charactersNoSpaces}</c>, <c>{words}</c>, <c>{paragraphs}</c> and
    /// <c>{readingMinutes}</c> are substituted; anything else is left alone. Defaults to
    /// <c>"{words} words"</c>. Ignored without a <see cref="TargetSelector"/>.
    /// </summary>
    [Parameter] public string Template { get; set; } = "{words} words";

    /// <summary>
    /// Reading speed used for <see cref="LexicalDocumentStats.ReadingMinutes"/>. Defaults
    /// to 200, the usual prose figure.
    /// </summary>
    [Parameter] public int WordsPerMinute { get; set; } = 200;

    /// <summary>
    /// Fires (debounced) when any of the numbers change. Wiring it is what enables the
    /// .NET push; a change that leaves every number identical never fires.
    /// </summary>
    [Parameter] public EventCallback<LexicalDocumentStats> OnStatsChanged { get; set; }

    /// <summary>
    /// The most recent snapshot, or <see cref="LexicalDocumentStats.Empty"/> before the
    /// first push. Only populated while <see cref="OnStatsChanged"/> is wired.
    /// </summary>
    public LexicalDocumentStats Stats { get; private set; } = LexicalDocumentStats.Empty;

    /// <inheritdoc />
    protected override bool HasInvokeHandler => OnStatsChanged.HasDelegate;

    /// <inheritdoc />
    protected override object? GetOptions() => JsonSerializer.SerializeToElement(
        new StatsExtensionOptionsDto
        {
            TargetSelector = TargetSelector,
            Template = Template,
            WordsPerMinute = WordsPerMinute,
        },
        LexicalJsonSerializerContext.Default.StatsExtensionOptionsDto);

    /// <summary>
    /// Reads the current statistics on demand — the pull counterpart of
    /// <see cref="OnStatsChanged"/>. <see cref="LexicalDocumentStats.Empty"/> before the
    /// editor is created.
    /// </summary>
    public async Task<LexicalDocumentStats> GetStatsAsync() =>
        Deserialize(await InvokeJsAsync("get"));

    /// <summary>Receives the statistics push from the JS half.</summary>
    protected override async Task<string?> OnInvokeAsync(string method, string argsJson)
    {
        if (method != "stats")
        {
            return null;
        }

        string? statsJson;
        using (var document = JsonDocument.Parse(argsJson))
        {
            var args = document.RootElement;
            if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() == 0)
            {
                return null;
            }
            statsJson = args[0].GetString();
        }

        Stats = Deserialize(statsJson);
        await OnStatsChanged.InvokeAsync(Stats);
        return null;
    }

    /// <summary>Parses a JSON statistics object, tolerating null/empty.</summary>
    private static LexicalDocumentStats Deserialize(string? json) =>
        string.IsNullOrEmpty(json)
            ? LexicalDocumentStats.Empty
            : JsonSerializer.Deserialize(
                json, LexicalJsonSerializerContext.Default.LexicalDocumentStats)
                ?? LexicalDocumentStats.Empty;
}
