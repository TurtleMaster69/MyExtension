namespace Telescope.Finders
{
    /// <summary>
    /// A single fzf fuzzy-content hit: the target file, the 1-based line number, and the matching
    /// line's text. Carried as a <c>FinderEntry.Payload</c> so <see cref="FzfFinder.OnSelected"/>
    /// can open the file at the line and the overlay preview can jump the caret to it.
    /// </summary>
    public sealed class FzfHit : FileLocation
    {
        public FzfHit(string filePath, int lineNumber, string lineText)
            : base(filePath, lineNumber)
        {
            LineText = lineText ?? string.Empty;
        }

        public string LineText { get; }
    }
}
