namespace Telescope.Finders
{
    /// <summary>
    /// A recent-files finder hit model: the MRU file path, LineNumber=0 (a plain file open —
    /// the overlay's preview branch shows the file and resets the caret to the top).
    /// Deliberately NOT <see cref="FileHit"/>: the Files column catalog's getters are typed to
    /// FileHit, so a distinct type keeps the two finders' column sets type-disjoint (the Recent
    /// catalog has its own RecentFileHit-typed getters).
    /// </summary>
    public sealed class RecentFileHit : FileLocation
    {
        public RecentFileHit(string filePath) : base(filePath, 0) { }
    }
}
