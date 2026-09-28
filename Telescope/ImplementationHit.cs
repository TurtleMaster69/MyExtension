namespace Telescope
{
    /// <summary>
    /// A single implementation/override of the caret symbol, as reported by VS's Roslyn
    /// find-implementations engine: the target file, the 1-based declaring line, the symbol's
    /// name, and its kind (e.g. <c>Class</c>/<c>Method</c>/<c>Property</c>) for the display row.
    /// Carried as a <c>FinderEntry.Payload</c> so <see cref="ImplementationFinder.OnSelected"/>
    /// can open the file at the line and the overlay preview can jump the caret to it.
    /// </summary>
    public sealed class ImplementationHit : FileLocation
    {
        public ImplementationHit(string filePath, int lineNumber, string symbolName, string kind)
            : base(filePath, lineNumber)
        {
            SymbolName = symbolName ?? string.Empty;
            Kind = kind ?? string.Empty;
        }

        public string SymbolName { get; }
        public string Kind { get; }
    }
}