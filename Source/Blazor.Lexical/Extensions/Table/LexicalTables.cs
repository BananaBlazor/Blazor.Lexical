namespace Blazor.Lexical;

/// <summary>
/// Adds the table feature to a <see cref="LexicalEditor"/>: nest
/// <c>&lt;LexicalTables /&gt;</c> inside the editor and it lazily loads the
/// <c>@lexical/table</c> chunk, registers the table nodes, and activates the
/// <see cref="LexicalTableButton"/> insert picker, the <c>/table</c> slash item, the
/// <see cref="LexicalTableEditor"/> action menu, and
/// <see cref="LexicalEditor.InsertTableAsync"/>. Editors that do not declare it never
/// download that chunk (~90&#160;kb).
/// </summary>
/// <remarks>
/// <para>
/// It is an ordinary extension, authored in markup like any other — it just lives on the
/// built-in bundling tier, so its JS is a literal <c>import('./table')</c> inside our own
/// bundle rather than a URL fetched at runtime.
/// </para>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalToolbar /&gt;
///   &lt;LexicalTables /&gt;
///   &lt;LexicalTableEditor /&gt;
/// &lt;/LexicalEditor&gt;
/// </code>
/// <para>
/// Entirely client-side: no <c>OnInvokeAsync</c> override, so the descriptor reports no
/// invoke handler and the table module can never call into .NET (invariant #1).
/// </para>
/// </remarks>
public sealed class LexicalTables : LexicalExtension
{
    internal override string? BuiltIn => "table";
}
