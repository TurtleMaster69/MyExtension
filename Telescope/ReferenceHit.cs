namespace Telescope
{
    /// <summary>
    /// A single reference to the caret symbol, as reported by VS's Roslyn find-references
    /// engine: the target file, the 1-based line and column, whether the site is a write
    /// (assignment) or a read, the symbol's name, and the source line for display. Carried as a
    /// <c>FinderEntry.Payload</c> so <see cref="ReferencesFinder.OnSelected"/> can open the file
    /// at the line and the overlay preview can jump the caret to it.
    /// </summary>
    public sealed class ReferenceHit : FileLocation
    {
        public ReferenceHit(string filePath, int lineNumber, int column, bool isWrite, string symbol, string lineText)
            : base(filePath, lineNumber)
        {
            Column = column;
            IsWrite = isWrite;
            Symbol = symbol ?? string.Empty;
            LineText = lineText ?? string.Empty;
        }

        public int Column { get; }
        public bool IsWrite { get; }
        public string Symbol { get; }
        public string LineText { get; }
    }
}