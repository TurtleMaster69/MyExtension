using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Telescope.Finders
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
        /// UI thread (may touch VS/DTE — assert <c>ThreadHelper.ThrowIfNotOnUIThread()</c>). The
        /// optional <paramref name="query"/> is used by query-driven finders (see
        /// <see cref="IsQueryDriven"/>); non-query finders ignore it.
        /// </summary>
        IReadOnlyList<FinderEntry> GetCandidates(string query = "");

        /// <summary>
        /// Async variant of <see cref="GetCandidates(string)"/> for query-driven finders whose
        /// gather is asynchronous (e.g. an fzf subprocess). The overlay awaits this on the
        /// query-driven path so a slow gather never blocks the UI thread. Abstract (NOT a default
        /// interface method — net472 rejects C# 8 default implementations). The optional
        /// <paramref name="cancellationToken"/> lets the overlay cancel an in-flight gather when
        /// the query changes or the overlay closes (M2/BP-3).
        /// </summary>
        Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "", CancellationToken cancellationToken = default);

        /// <summary>
        /// True when the finder is <b>query-driven</b>: every query change re-gathers candidates
        /// from <see cref="GetCandidates(string)"/> and renders them directly (skipping fzf — grep
        /// semantics are literal, not fuzzy). The overlay branches on this capability and applies a
        /// debounce so a full-solution scan runs after typing settles, not per keystroke.
        /// </summary>
        bool IsQueryDriven { get; }

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
