using System.Text.Json.Nodes;

namespace Tests.Integration.Host.Components;

/// <summary>
/// Builds Lexical editor-state JSON for harness seeds that HTML or Markdown cannot express,
/// such as the consumer block decorators from <c>harness-block-decorator.mjs</c>.
/// </summary>
public static class HarnessState
{
    /// <summary>A whole editor state whose root holds <paramref name="children"/>.</summary>
    public static string Document(params JsonObject[] children) =>
        new JsonObject { ["root"] = Element("root", children) }.ToJsonString();

    /// <summary>A paragraph holding one text node.</summary>
    public static JsonObject Paragraph(string text, int indent = 0) =>
        Element("paragraph", [Text(text)], indent, new() { ["textFormat"] = 0, ["textStyle"] = "" });

    /// <summary>A harness block decorator of node type <paramref name="type"/>.</summary>
    public static JsonObject Block(string type, string label, int indent = 0) =>
        new() { ["type"] = type, ["version"] = 1, ["label"] = label, ["indent"] = indent };

    /// <summary>A bullet list of <paramref name="items"/>.</summary>
    public static JsonObject BulletList(params JsonObject[] items) =>
        Element("list", items, extra: new() { ["listType"] = "bullet", ["start"] = 1, ["tag"] = "ul" });

    /// <summary>A list item holding <paramref name="children"/>.</summary>
    public static JsonObject ListItem(int value, params JsonObject[] children) =>
        Element("listitem", children, extra: new() { ["value"] = value });

    /// <summary>A plain text node.</summary>
    public static JsonObject Text(string text) => new()
    {
        ["detail"] = 0, ["format"] = 0, ["mode"] = "normal", ["style"] = "",
        ["text"] = text, ["type"] = "text", ["version"] = 1,
    };

    private static JsonObject Element(
        string type, JsonObject[] children, int indent = 0, JsonObject? extra = null)
    {
        var node = new JsonObject
        {
            ["children"] = new JsonArray([.. children]),
            ["direction"] = "ltr",
            ["format"] = "",
            ["indent"] = indent,
            ["type"] = type,
            ["version"] = 1,
        };
        foreach (var (key, value) in extra ?? [])
        {
            node[key] = value?.DeepClone();
        }
        return node;
    }
}
