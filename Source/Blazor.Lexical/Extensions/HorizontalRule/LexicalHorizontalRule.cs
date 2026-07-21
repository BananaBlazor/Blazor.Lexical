namespace Blazor.Lexical;

/// <summary>
/// Adds the horizontal rule (thematic break) node to a <see cref="LexicalEditor"/>.
/// Nest <c>&lt;LexicalHorizontalRule /&gt;</c> inside the editor; rules are then inserted
/// by any control carrying <c>data-lexical-command="hr:insert"</c> — the toolbar button
/// and the slash-menu item both do — or from C# with <see cref="InsertAsync"/>.
/// </summary>
/// <remarks>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalToolbar /&gt;
///   &lt;LexicalHorizontalRule /&gt;
/// &lt;/LexicalEditor&gt;
/// </code>
/// <para>
/// Clicking a rule selects it, so Delete or Backspace removes it; the selected rule
/// carries the class named by <see cref="LexicalTheme.HrSelected"/>.
/// </para>
/// <para>
/// The node is registered only when this component is present, which is what keeps it
/// out of editors that do not want it. The consequence is that an <c>&lt;hr&gt;</c> in
/// HTML loaded into an editor *without* this extension is dropped rather than
/// preserved — the same trade the table extension makes.
/// </para>
/// <para>
/// Entirely client-side: this extension performs no interop in either direction beyond
/// the <see cref="InsertAsync"/> call it offers.
/// </para>
/// </remarks>
public sealed class LexicalHorizontalRule : LexicalExtension
{
    internal override string? BuiltIn => "hr";

    /// <summary>
    /// Inserts a horizontal rule at the current selection — the C# equivalent of the
    /// <c>hr:insert</c> command token. No-op before the editor is created.
    /// </summary>
    public Task InsertAsync() => InvokeJsAsync("insert");
}
