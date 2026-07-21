using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// Makes <c>Tab</c> and <c>Shift</c>+<c>Tab</c> indent and outdent blocks inside a
/// <see cref="LexicalEditor"/> — list items, paragraphs, and anything else Lexical
/// considers indentable. Nest <c>&lt;LexicalTabIndent /&gt;</c> inside the editor.
/// </summary>
/// <remarks>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalTabIndent MaxIndent="5" /&gt;
/// &lt;/LexicalEditor&gt;
/// </code>
/// <para>
/// <b>This is opt-in for an accessibility reason, not merely a stylistic one.</b>
/// Binding <c>Tab</c> inside the editor takes it away from keyboard navigation: a
/// keyboard-only user who tabs into the editor can no longer tab out of it, so focus is
/// effectively trapped. Lexical's own documentation discourages the behavior for exactly
/// this reason. Add this extension when the editing gain is worth that cost — a
/// nested-outline editor, say — and leave it off otherwise. When you do enable it,
/// make sure some other affordance moves focus out of the editor.
/// </para>
/// <para>
/// Where the caret sits mid-text rather than at a block boundary, <c>Tab</c> inserts a
/// tab character instead of indenting, matching Lexical's own behavior.
/// </para>
/// <para>
/// Entirely client-side: this extension performs no interop in either direction.
/// </para>
/// </remarks>
public sealed class LexicalTabIndent : LexicalExtension
{
    internal override string? BuiltIn => "tabIndent";

    /// <summary>
    /// Maximum indent depth. <c>null</c> (the default) leaves indentation uncapped;
    /// a value of <c>n</c> allows indent levels below <c>n</c>.
    /// </summary>
    [Parameter] public int? MaxIndent { get; set; }

    /// <inheritdoc />
    protected override object? GetOptions() => JsonSerializer.SerializeToElement(
        new TabIndentExtensionOptionsDto { MaxIndent = MaxIndent },
        LexicalJsonSerializerContext.Default.TabIndentExtensionOptionsDto);
}
