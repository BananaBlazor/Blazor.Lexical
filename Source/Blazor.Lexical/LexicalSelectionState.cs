namespace Blazor.Lexical;

/// <summary>
/// A snapshot of the editor's current selection formatting, pushed from JS
/// whenever the selection or history availability changes. A toolbar uses this
/// to reflect which buttons are active and whether undo/redo are available.
/// Exposed via <see cref="LexicalEditor.SelectionState"/> and
/// <see cref="LexicalEditor.OnSelectionChanged"/>.
/// </summary>
public sealed record LexicalSelectionState
{
    /// <summary>Whether the selection is bold.</summary>
    public bool IsBold { get; init; }

    /// <summary>Whether the selection is italic.</summary>
    public bool IsItalic { get; init; }

    /// <summary>Whether the selection is underlined.</summary>
    public bool IsUnderline { get; init; }

    /// <summary>Whether the selection is struck through.</summary>
    public bool IsStrikethrough { get; init; }

    /// <summary>Whether the selection is inline code.</summary>
    public bool IsCode { get; init; }

    /// <summary>Whether the selection is subscript.</summary>
    public bool IsSubscript { get; init; }

    /// <summary>Whether the selection is superscript.</summary>
    public bool IsSuperscript { get; init; }

    /// <summary>Whether the selection is forced lowercase.</summary>
    public bool IsLowercase { get; init; }

    /// <summary>Whether the selection is forced uppercase.</summary>
    public bool IsUppercase { get; init; }

    /// <summary>The block type at the selection.</summary>
    public LexicalBlockType BlockType { get; init; }

    /// <summary>Whether the selection sits within a link.</summary>
    public bool IsLink { get; init; }

    /// <summary>The block alignment at the selection.</summary>
    public LexicalAlignment Alignment { get; init; }

    /// <summary>Whether an undo is currently available.</summary>
    public bool CanUndo { get; init; }

    /// <summary>Whether a redo is currently available.</summary>
    public bool CanRedo { get; init; }

    /// <summary>
    /// The selected text, or an empty string when the selection is collapsed (a plain
    /// caret) or is not a text range. Captured at selection time, so a custom action —
    /// a "comment on this" button in a <see cref="LexicalFloatingToolbar"/>, say — can
    /// store the quote it acts on without a round trip, and without depending on the
    /// selection surviving the click.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Returns whether the given inline text format is active on the selection.</summary>
    public bool HasFormat(LexicalTextFormat format) => format switch
    {
        LexicalTextFormat.Bold => IsBold,
        LexicalTextFormat.Italic => IsItalic,
        LexicalTextFormat.Underline => IsUnderline,
        LexicalTextFormat.Strikethrough => IsStrikethrough,
        LexicalTextFormat.Code => IsCode,
        LexicalTextFormat.Subscript => IsSubscript,
        LexicalTextFormat.Superscript => IsSuperscript,
        LexicalTextFormat.Lowercase => IsLowercase,
        LexicalTextFormat.Uppercase => IsUppercase,
        _ => false,
    };

    /// <summary>Projects the wire DTO received from JS into the public state record.</summary>
    internal static LexicalSelectionState FromDto(LexicalSelectionStateDto dto) => new()
    {
        IsBold = dto.IsBold,
        IsItalic = dto.IsItalic,
        IsUnderline = dto.IsUnderline,
        IsStrikethrough = dto.IsStrikethrough,
        IsCode = dto.IsCode,
        IsSubscript = dto.IsSubscript,
        IsSuperscript = dto.IsSuperscript,
        IsLowercase = dto.IsLowercase,
        IsUppercase = dto.IsUppercase,
        BlockType = LexicalBlockTypeExtensions.FromJsToken(dto.BlockType),
        IsLink = dto.IsLink,
        Alignment = LexicalAlignmentExtensions.FromJsToken(dto.Alignment),
        CanUndo = dto.CanUndo,
        CanRedo = dto.CanRedo,
        Text = dto.Text ?? string.Empty,
    };
}
