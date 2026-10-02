namespace Telescope.Finders
{
    /// <summary>
    /// A plain file hit (the file-finder's hit model): the file path and a line number (0 for a
    /// plain file open). Carried as a <c>FinderEntry.Payload</c> so the finder can open the file.
    /// </summary>
    public sealed class FileHit : FileLocation
    {
        public FileHit(string filePath, int lineNumber) : base(filePath, lineNumber) { }
    }
}
