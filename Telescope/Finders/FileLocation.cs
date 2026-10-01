namespace Telescope.Finders
{
    /// <summary>
    /// Base class for finder hit models: owns the null-coalescing <see cref="FilePath"/> and the
    /// <see cref="LineNumber"/>. Subclasses add their display/action-specific fields.
    /// </summary>
    public abstract class FileLocation : IFileLocation
    {
        protected FileLocation(string filePath, int lineNumber)
        {
            FilePath = filePath ?? string.Empty;
            LineNumber = lineNumber;
        }

        public string FilePath { get; }
        public int LineNumber { get; }
    }
}
