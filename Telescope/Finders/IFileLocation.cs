namespace Telescope.Finders
{
    /// <summary>
    /// A location in a source file: the file path and the 1-based line number. Implemented by
    /// every finder hit model so the overlay preview and the finder base can treat them uniformly.
    /// </summary>
    public interface IFileLocation
    {
        string FilePath { get; }
        int LineNumber { get; }
    }
}
