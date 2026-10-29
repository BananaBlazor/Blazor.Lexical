using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// Owns indent and outdent for a <see cref="LexicalEditor"/>: a cap, the blocks they apply
/// to, and the disabled state of the indent toolbar buttons. Nest
/// <c>&lt;LexicalIndent /&gt;</c> inside the editor; the default
/// <see cref="LexicalToolbar"/> then shows Outdent/Indent buttons, and any control carrying
/// <c>data-lexical-command="indent:in"</c> or <c>"indent:out"</c> follows the same rules.
/// </summary>
/// <remarks>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalToolbar /&gt;
///   &lt;LexicalIndent MaxIndent="4" IndentListItems="false" /&gt;
///   &lt;LexicalTabIndent /&gt;
/// &lt;/LexicalEditor&gt;
/// </code>
/// <para>
/// One policy applies per editor, whatever the declaration order: with
/// <see cref="LexicalTabIndent"/> also present, <c>Tab</c>/<c>Shift</c>+<c>Tab</c> follow
/// this component's cap and exclusions, and its <see cref="LexicalTabIndent.MaxIndent"/>
/// is ignored. <c>Tab</c> binding still needs <see cref="LexicalTabIndent"/>; this
/// component adds no key bindings.
/// </para>
/// <para>
/// An indent button is disabled when it would do nothing: every selected block is at the
/// cap or the left margin, nothing in the selection can be indented, or the editor is
/// read-only. The control's <c>data-lexical-disabled</c> attribute, <c>title</c> and
/// <c>aria-label</c> carry the reason; the original title returns when it re-enables.
/// </para>
/// <para>
/// A custom block decorator node can opt into indenting by implementing
/// <c>canIndent</c>, <c>getIndent</c> and <c>setIndent</c> (see
/// <c>IndentableBlockNode</c> in <c>blazor-lexical-extension.d.ts</c>). Only this
/// component indents decorators.
/// </para>
/// <para>
/// Entirely client-side: this extension performs no interop in either direction.
/// </para>
/// </remarks>
public sealed class LexicalIndent : LexicalExtension
{
    internal override string? BuiltIn => "indent";

    /// <summary>
    /// Maximum indent depth. <c>null</c> (the default) leaves indentation uncapped; a value
    /// of <c>n</c> allows indent levels below <c>n</c>, as on
    /// <see cref="LexicalTabIndent.MaxIndent"/>.
    /// </summary>
    [Parameter] public int? MaxIndent { get; set; }

    /// <summary>
    /// Whether list items can be indented and outdented (default <c>true</c>). Lexical
    /// indents a list item by nesting it, which flat output formats cannot represent; set
    /// <c>false</c> to keep lists flat. Backspace at the start of an already-nested list
    /// item then no longer outdents it either.
    /// </summary>
    [Parameter] public bool IndentListItems { get; set; } = true;

    /// <summary>Disabled reason when every selected block is at the cap. Default "Maximum indent reached".</summary>
    [Parameter] public string? MaxIndentReason { get; set; }

    /// <summary>Disabled reason when every selected block is at the left margin. Default "Already at the left margin".</summary>
    [Parameter] public string? AtMarginReason { get; set; }

    /// <summary>
    /// Disabled reason when nothing in the selection can be indented, or the editor is
    /// read-only. Default "Indenting does not apply to this selection".
    /// </summary>
    [Parameter] public string? NotIndentableReason { get; set; }

    /// <inheritdoc />
    protected override object? GetOptions() => JsonSerializer.SerializeToElement(
        new IndentExtensionOptionsDto
        {
            MaxIndent = MaxIndent,
            IndentListItems = IndentListItems,
            MaxIndentReason = MaxIndentReason,
            AtMarginReason = AtMarginReason,
            NotIndentableReason = NotIndentableReason,
        },
        LexicalJsonSerializerContext.Default.IndentExtensionOptionsDto);
}
