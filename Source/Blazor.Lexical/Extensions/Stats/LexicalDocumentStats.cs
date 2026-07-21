namespace Blazor.Lexical;

/// <summary>
/// A snapshot of a document's size, produced by <see cref="LexicalStats"/>.
/// </summary>
/// <param name="Characters">Total characters, whitespace included.</param>
/// <param name="CharactersNoSpaces">Characters excluding all whitespace.</param>
/// <param name="Words">
/// Words, counted as whitespace-separated runs (Unicode-aware). An empty document is 0.
/// </param>
/// <param name="Paragraphs">Top-level blocks that contain some non-whitespace text.</param>
/// <param name="ReadingMinutes">
/// <see cref="Words"/> divided by <see cref="LexicalStats.WordsPerMinute"/>, rounded up.
/// </param>
public sealed record LexicalDocumentStats(
    int Characters,
    int CharactersNoSpaces,
    int Words,
    int Paragraphs,
    int ReadingMinutes)
{
    /// <summary>An all-zero snapshot — what an empty (or not-yet-created) editor reports.</summary>
    public static LexicalDocumentStats Empty { get; } = new(0, 0, 0, 0, 0);
}
