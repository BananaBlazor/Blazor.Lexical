using Microsoft.AspNetCore.Components;

namespace Blazor.Lexical;

/// <summary>
/// A formatting toolbar for a <see cref="LexicalEditor"/>. Placed as a child of the
/// editor (<c>&lt;LexicalEditor&gt;&lt;LexicalToolbar/&gt;&lt;/LexicalEditor&gt;</c>),
/// it renders a default set of controls (history, block type — which includes the
/// bulleted/numbered list options inline, playground-style — basic formatting, link,
/// and an insert-table grid picker). Each built-in control is pure markup tagged with a
/// <c>data-lexical-command</c>; the editor's delegated JS listener dispatches the
/// command and maintains active/disabled state, so nothing round-trips to .NET.
/// Toggle groups with the <c>Show*</c> parameters, add to the set with
/// <see cref="StartContent"/>/<see cref="EndContent"/>, or replace it wholesale with
/// <see cref="ChildContent"/> (compose the <c>Lexical*</c> primitives/groups, or add
/// your own <c>&lt;button @onclick&gt;</c> for real C# actions like save/export).
/// </summary>
public partial class LexicalToolbar : ComponentBase, IDisposable
{
    /// <summary>
    /// The editor this toolbar lives in, supplied by <see cref="LexicalEditor"/>'s
    /// cascade. Used to gate table chrome on the presence of a <see cref="LexicalTables"/>
    /// extension.
    /// </summary>
    [CascadingParameter] public LexicalEditor? Editor { get; set; }

    /// <summary>Extra CSS class(es) applied to the toolbar container.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Show the undo/redo group (default true).</summary>
    [Parameter] public bool ShowHistory { get; set; } = true;

    /// <summary>Show the block-type selector (default true).</summary>
    [Parameter] public bool ShowBlockType { get; set; } = true;

    /// <summary>Show the basic inline-format group (default true).</summary>
    [Parameter] public bool ShowTextFormat { get; set; } = true;

    /// <summary>
    /// Show a standalone bulleted/numbered list button group (default false). Off by
    /// default because the block-type selector already offers the list options inline
    /// (playground-style); enable this for a dedicated pair of toggle buttons instead.
    /// </summary>
    [Parameter] public bool ShowLists { get; set; }

    /// <summary>Show the link control (default true).</summary>
    [Parameter] public bool ShowLink { get; set; } = true;

    /// <summary>
    /// Show the insert-table grid picker (default true). Only rendered when the editor
    /// nests a <see cref="LexicalTables"/> extension, since the picker is inert without
    /// the table chunk loaded.
    /// </summary>
    [Parameter] public bool ShowTable { get; set; } = true;

    /// <summary>Whether the table controls should render (tables present and not hidden).</summary>
    private bool ShowTableControls => ShowTable && (Editor?.HasExtension<LexicalTables>() ?? false);

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        // The table answer only settles once the editor freezes its extension set, which
        // is after this toolbar has already rendered — and <LexicalTables/> may even be
        // declared below us. Re-render when the editor says the set changed.
        if (Editor is not null)
        {
            Editor.ExtensionsChanged += OnExtensionsChanged;
        }
    }

    private void OnExtensionsChanged() => InvokeAsync(StateHasChanged);

    /// <summary>Unsubscribes from the editor's extension-set notifications.</summary>
    public void Dispose()
    {
        if (Editor is not null)
        {
            Editor.ExtensionsChanged -= OnExtensionsChanged;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Custom toolbar content, <b>replacing</b> the entire default control set. Compose
    /// the <c>Lexical*</c> primitives/groups, and/or your own C# buttons. To keep the
    /// defaults and merely add to them, use <see cref="StartContent"/> /
    /// <see cref="EndContent"/> instead.
    /// </summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Extra content rendered <b>before</b> the toolbar body, without replacing it —
    /// the additive counterpart to <see cref="ChildContent"/>. Renders in both modes
    /// (alongside the default control set, or alongside a <see cref="ChildContent"/>
    /// replacement).
    /// </summary>
    [Parameter] public RenderFragment? StartContent { get; set; }

    /// <summary>
    /// Extra content rendered <b>after</b> the toolbar body, without replacing it —
    /// the usual place for an extension's own button
    /// (<c>&lt;LexicalToolbar&gt;&lt;EndContent&gt;&lt;BadgeButton/&gt;&lt;/EndContent&gt;&lt;/LexicalToolbar&gt;</c>).
    /// Renders in both modes; see <see cref="StartContent"/>.
    /// </summary>
    [Parameter] public RenderFragment? EndContent { get; set; }

    /// <summary>A short display label for a block type (used by the selector and buttons).</summary>
    internal static string BlockTypeLabel(LexicalBlockType type) => type switch
    {
        LexicalBlockType.Paragraph => "Normal",
        LexicalBlockType.Heading1 => "Heading 1",
        LexicalBlockType.Heading2 => "Heading 2",
        LexicalBlockType.Heading3 => "Heading 3",
        LexicalBlockType.Heading4 => "Heading 4",
        LexicalBlockType.Heading5 => "Heading 5",
        LexicalBlockType.Heading6 => "Heading 6",
        LexicalBlockType.Quote => "Quote",
        LexicalBlockType.BulletList => "Bulleted list",
        LexicalBlockType.NumberList => "Numbered list",
        _ => type.ToString(),
    };
}
