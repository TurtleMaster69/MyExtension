using EnvDTE;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telescope.Logging;

namespace Telescope.Finders
{
    /// <summary>
    /// A Telescope finder that live-greps the solution's project files for the typed query
    /// (query-driven, case-insensitive substring — NOT fzf-filtered). Each row shows
    /// <c>{fileName}:{line}: {lineText}</c>. Selecting an entry opens the hit file in the editor
    /// and jumps the caret to the hit line.
    ///
    /// <para/>
    /// <b>Threading:</b> <see cref="GetCandidatesAsync"/> keeps the DTE enumeration on the UI
    /// thread and runs the pure per-file scan off-thread (M1/BP-1); <see cref="OnSelected"/> touches
    /// DTE and therefore must run on the UI thread.
    /// </summary>
    public sealed class GrepFinder : QueryDrivenFinderBase<GrepHit>
    {
        public override string Name => "Grep";

        public override bool IsQueryDriven => true;

        /// <param name="dteFactory">Returns the top-level DTE automation object (see <see cref="FileFinder"/>).</param>
        /// <param name="fileCache">Shared project-file enumeration cache (amortizes the per-query solution walk).</param>
        /// <param name="contentCache">Shared file-content cache (D7/BP-14 — ONE instance injected from the controller; m8/BP-14 makes it a REQUIRED param so a finder can never silently revert to its own cache).</param>
        /// <param name="testEnumerate">Hermetic-test seam: when set, candidate gathering bypasses DTE entirely (the shared cache serves the delegate once across queries).</param>
        /// <param name="testOpener">Hermetic-test seam: when set, opening bypasses DTE entirely.</param>
        internal GrepFinder(
            Func<DTE> dteFactory,
            ProjectFileCache fileCache,
            FileContentCache contentCache,
            Func<IReadOnlyList<string>>? testEnumerate = null,
            Action<GrepHit>? testOpener = null)
            : base(dteFactory, fileCache, contentCache, testEnumerate, testOpener)
        {
        }

        protected override string HitsLiteral => "grep hits=";

        protected override string OpenLiteral => "opened grep: file=";

        protected override string EnumerateFailureLiteral => "GrepFinder failed to enumerate: ";

        protected override string LineTextOf(GrepHit hit) => hit.LineText;

        protected override string OpenErrorNoun => "grep";

        protected override async Task<IReadOnlyList<GrepHit>> MatchAsync(IReadOnlyList<string> files, string query, CancellationToken token)
        {
            var hits = new List<GrepHit>();
            foreach (string path in files)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                ScanFile(path, query, hits, Cache);
                if (hits.Count >= FinderConstants.HitCap)
                {
                    break;
                }
            }
            return hits;
        }

        /// <summary>
        /// Pure per-file scan loop (D5/BP-10): no instance state, so it can run on a background
        /// task while the DTE enumeration stays on the UI thread. The caller supplies the content
        /// cache (the shared instance).
        /// </summary>
        internal static void ScanFile(string path, string query, List<GrepHit> hits, FileContentCache cache)
        {
            try
            {
                string[] lines = cache.GetLines(path);
                foreach (int ln in LiteralLineScanner.Scan(lines, query, FinderConstants.HitCap - hits.Count))
                {
                    hits.Add(new GrepHit(path, ln, lines[ln - 1]));
                }
            }
            catch
            {
                // unreadable/binary file — skip
            }
        }
    }
}
