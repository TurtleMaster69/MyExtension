using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Telescope.Logging;

namespace Telescope.Finders
{
    /// <summary>
    /// A Telescope finder that lists the recently-opened files (the VS MRU, most-recent-first).
    /// Hermetic-testable: the ONLY data source and action are the injected gatherer
    /// (<see cref="Func{TResult}"/>, the MRU paths) and the opener (<see cref="Action{T}"/>,
    /// a plain file open — no line jump).
    ///
    /// <para/>
    /// Policy owned HERE (pure, unit-testable): existing files only (<see cref="File.Exists"/>),
    /// capped at <see cref="FinderConstants.HitCap"/>, the gatherer's most-recent-first order preserved (never
    /// re-sorted). Sourcing (the DTE reflection probe + the session MRU) lives in the host —
    /// MyExtension/Package/Utils/RecentFilesGatherer.cs.
    ///
    /// <para/>
    /// <b>Threading:</b> <see cref="FinderBase{THit}.GetCandidates"/> and
    /// <see cref="FinderBase{THit}.OnSelected"/> run on the UI thread (asserted by the base).
    /// m37 (BP-15): produces <see cref="FileHit"/> payloads (RecentFileHit is merged into FileHit).
    /// </summary>
    public sealed class RecentFilesFinder : FinderBase<FileHit>
    {
        private readonly Func<IReadOnlyList<string>> _gatherer;
        private readonly Action<string> _opener;

        public override string Name => "Recent";

        /// <param name="gatherer">Returns the MRU file paths, most-recent-first (host-side).</param>
        /// <param name="opener">Opens a file path in the editor (host-side DTE call).</param>
        public RecentFilesFinder(Func<IReadOnlyList<string>> gatherer, Action<string> opener)
        {
            _gatherer = gatherer ?? throw new ArgumentNullException(nameof(gatherer));
            _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        }

        protected override IReadOnlyList<FileHit> GatherHits()
        {
            IReadOnlyList<string> paths = _gatherer() ?? Array.Empty<string>();
            IReadOnlyList<FileHit> hits = paths
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .Take(FinderConstants.HitCap)
                .Select(p => new FileHit(p, 0))
                .ToList();

            TelescopeLog.Log($"recent files gathered count={hits.Count}");
            return hits;
        }

        protected override FinderEntry ToEntry(FileHit hit)
        {
            // The Files finder's display: the bare file name; the directory is the Dir
            // column's cell (the Recent column set).
            return new FinderEntry(Path.GetFileName(hit.FilePath), hit);
        }

        protected override void OpenHit(FileHit hit)
        {
            _opener(hit.FilePath);
            // Finder-owned open log — REUSES the Files finder's literal byte-exactly (M-M7).
            // The host opener must NOT log it (single-stamp discipline).
            TelescopeLog.Log($"opened file: {hit.FilePath}");
        }

        protected override string OpenErrorNoun => "file";

        protected override string GatherErrorLiteral(Exception ex) => $"recent files gather failed: {ex.Message}";
    }
}
