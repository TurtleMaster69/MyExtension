using System.Collections.Generic;

namespace Telescope
{
    /// <summary>
    /// A single Telescope "finder" — one mode of the fuzzy finder (e.g. find files, live grep,
    /// buffers, git files). The overlay hosts one finder at a time; the extension decides which
    /// finder to open based on the invoked binding.
    ///
    /// <para/>
    /// This is the primary extension point for adding Telescope functionality later: implement a
    /// new <see cref="IFinder"/> and register it with the controller to get a new picker.
    /// </summary>
    public interface IFinder
    {
        /// <summary>Short label shown in the prompt row, e.g. "Files".</summary>
        string Name { get; }

        /// <summary>
        /// Returns the candidate entries to filter. Called once when the finder is opened, on the
        /// UI thread (may touch VS/DTE — assert <c>ThreadHelper.ThrowIfNotOnUIThread()</c>).
        /// </summary>
        IReadOnlyList<FinderEntry> GetCandidates();

        /// <summary>
        /// Invoked when the user confirms (Enter) an entry. Runs on the UI thread and may call VS
        /// APIs. Should swallow exceptions rather than crash the overlay.
        /// </summary>
        void OnSelected(FinderEntry entry);
    }

    /// <summary>
    /// One row in the finder's results list: the display text that fzf filters plus an opaque
    /// payload carried to <see cref="IFinder.OnSelected"/>.
    /// </summary>
    public sealed class FinderEntry
    {
        public string Display { get; }
        public object? Payload { get; }

        public FinderEntry(string display, object? payload = null)
        {
            Display = display;
            Payload = payload;
        }

        public override string ToString() => Display;
    }
}
