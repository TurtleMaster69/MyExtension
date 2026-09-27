namespace Telescope
{
    /// <summary>
    /// A single live-grep hit: the target file, the 1-based line number, and the matching line's
    /// text. Carried as a <c>FinderEntry.Payload</c> so <see cref="GrepFinder.OnSelected"/> can
    /// open the file at the line and the overlay preview can jump the caret to it.
    /// </summary>
    public sealed class GrepHit
    {
        public GrepHit(string filePath, int lineNumber, string lineText)
        {
            FilePath = filePath ?? string.Empty;
            LineNumber = lineNumber;
            LineText = lineText ?? string.Empty;
        }

        public string FilePath { get; }
        public int LineNumber { get; }
        public string LineText { get; }
    }
}