namespace Blazor.Lexical;

/// <summary>
/// Adds the page-break node to a <see cref="LexicalEditor"/>: a block that forces a new
/// page when the document is printed or converted to PDF or Word.
/// Nest <c>&lt;LexicalPageBreak /&gt;</c> inside the editor; breaks are then inserted
/// by any control carrying <c>data-lexical-command="page-break:insert"</c> — the toolbar
/// button and the slash-menu item both do — or from C# with <see cref="InsertAsync"/>.
/// </summary>
/// <remarks>
/// <code>
/// &lt;LexicalEditor&gt;
///   &lt;LexicalToolbar /&gt;
///   &lt;LexicalPageBreak /&gt;
/// &lt;/LexicalEditor&gt;
/// </code>
/// <para>
/// A page break always sits at the top level of the document: inserted inside a list or
/// quote it splits it, and inside a table cell it lands after the table. Clicking a break
/// selects it, so Delete or Backspace removes it; the selected break carries the class
/// named by <see cref="LexicalTheme.PageBreakSelected"/>.
/// </para>
/// <para>
/// Exported HTML is <c>&lt;figure type="page-break"&gt;</c> with an inline
/// <c>break-after: page</c> style, so it paginates without extra CSS. Word's
/// <c>&lt;br style="page-break-before:always"&gt;</c> imports as a page break. Markdown
/// has no page-break form, so breaks are dropped from markdown output.
/// </para>
/// <para>
/// The node is registered only when this component is present, which is what keeps it
/// out of editors that do not want it. The consequence is that page-break markup in HTML
/// loaded into an editor *without* this extension is dropped rather than preserved — the
/// same trade the table extension makes.
/// </para>
/// <para>
/// Entirely client-side: this extension performs no interop in either direction beyond
/// the <see cref="InsertAsync"/> call it offers.
/// </para>
/// </remarks>
public sealed class LexicalPageBreak : LexicalExtension
{
    internal override string? BuiltIn => "pageBreak";

    /// <summary>
    /// Inserts a page break at the current selection — the C# equivalent of the
    /// <c>page-break:insert</c> command token. No-op before the editor is created.
    /// </summary>
    public Task InsertAsync() => InvokeJsAsync("insert");
}
