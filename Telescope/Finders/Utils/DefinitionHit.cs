namespace Telescope.Finders
{
    /// <summary>
    /// A single definition location of the caret symbol: the target file, the 1-based
    /// declaring line, the symbol's name, and its kind (e.g. <c>Class</c>/<c>Method</c>/
    /// <c>Property</c>) for the display row. Carried as a <c>FinderEntry.Payload</c> so
    /// <see cref="DefinitionFinder.OnSelected"/> can open the file at the line and the
    /// overlay preview can jump the caret to it (the shared <see cref="IFileLocation"/>
    /// dispatch — no overlay changes needed).
    /// </summary>
    public sealed class DefinitionHit : FileLocation
    {
        public DefinitionHit(string filePath, int lineNumber, string symbolName, string kind)
            : base(filePath, lineNumber)
        {
            SymbolName = symbolName ?? string.Empty;
            Kind = kind ?? string.Empty;
        }

        public string SymbolName { get; }
        public string Kind { get; }
    }
}
