using System.Linq;
using System.Text.Json;

namespace Blazor.Lexical;

/// <summary>
/// The mentions feature, expressed on the same contract a consumer extension uses —
/// on the built-in bundling tier, so its JS is the statically-imported
/// <c>js/src/mentions.ts</c> rather than a URL fetched at runtime. Created by
/// <see cref="LexicalEditor"/> when the host nested at least one
/// <see cref="LexicalMention"/>; never authored in markup, hence <c>internal</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>one</b> extension owning <b>all</b> the editor's configs, not one per
/// <see cref="LexicalMention"/>: freeform highlighting runs through a single shared
/// text-entity matcher spanning every initiator, so per-config instances would register
/// competing transforms that unwrap each other's tokens.
/// </para>
/// <para>
/// It holds the editor's live config list rather than a copy, so configs registered
/// after this instance is constructed are still described when the editor is created.
/// </para>
/// </remarks>
internal sealed class LexicalMentionExtension(IReadOnlyList<LexicalMention> configs)
    : LexicalExtension
{
    internal override string? BuiltIn => "mentions";

    /// <summary>
    /// Interop stays opt-in per config (invariant #1): with every config purely freeform
    /// — no provider, no selection callback — the whole feature runs client-side and JS
    /// is told it may not call back at all.
    /// </summary>
    protected override bool HasInvokeHandler =>
        configs.Any(config => config.HasProvider || config.NotifiesSelected);

    /// <inheritdoc />
    protected override object? GetOptions() => JsonSerializer.SerializeToElement(
        new MentionsExtensionOptionsDto { Configs = [.. configs.Select(c => c.ToDto())] },
        LexicalJsonSerializerContext.Default.MentionsExtensionOptionsDto);

    /// <summary>
    /// Routes the mentions runtime's two calls — <c>resolve</c> (query a config's
    /// provider) and <c>selected</c> (a suggestion was confirmed) — to the config named
    /// by the first argument. Both arrive as the extension channel's JSON argument array.
    /// </summary>
    protected override async Task<string?> OnInvokeAsync(string method, string argsJson)
    {
        using var document = JsonDocument.Parse(argsJson);
        var args = document.RootElement;
        if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() == 0)
        {
            return null;
        }

        var config = configs.FirstOrDefault(c => c.ConfigId == args[0].GetString());
        if (config is null)
        {
            return method == "resolve" ? "[]" : null;
        }

        switch (method)
        {
            case "resolve":
            {
                var items = (await config.ResolveAsync(ArgString(args, 1))).ToArray();
                return JsonSerializer.Serialize(
                    items, LexicalJsonSerializerContext.Default.MentionItemArray);
            }

            case "selected":
            {
                // The url stays nullable end-to-end — "no link" is not the empty string.
                var selected = new LexicalMentionSelected(
                    config.ConfigId, ArgString(args, 1), ArgString(args, 2), ArgStringOrNull(args, 3));
                await config.NotifySelectedAsync(selected);
                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>Reads argument <paramref name="index"/> as a string, tolerating a short array or a null.</summary>
    private static string ArgString(JsonElement args, int index) =>
        ArgStringOrNull(args, index) ?? string.Empty;

    /// <summary>As <see cref="ArgString"/>, but keeps "absent" distinct from "empty".</summary>
    private static string? ArgStringOrNull(JsonElement args, int index) =>
        index < args.GetArrayLength() && args[index].ValueKind == JsonValueKind.String
            ? args[index].GetString()
            : null;
}
