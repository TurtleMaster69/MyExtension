using EnvDTE;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telescope.Filter;
using Telescope.Logging;
using FzfHit = Telescope.Finders.GrepHit;

namespace Telescope.Finders
{
    /// <summary>
    /// A Telescope finder that fuzzy-matches the solution's project-file <b>contents</b> for the
    /// typed query using fzf (query-driven, ONE batched <c>fzf --filter</c> call across ALL files'
    /// lines — M2/BP-2). Each row shows <c>{fileName}:{line}: {lineText}</c>. Selecting an entry
    /// opens the hit file in the editor and jumps the caret to the hit line. When fzf is
    /// unavailable the finder falls back to a literal case-insensitive substring scan (never the
    /// full unfiltered list).
    ///
    /// <para/>
    /// <b>Threading:</b> the DTE enumeration path asserts the UI thread; the per-file content reads
    /// + the literal-fallback scan run off-thread (M3/BP-5); the fzf subprocess itself runs
    /// off-thread inside <see cref="IFzfEngine"/>. <see cref="OnSelected"/> touches DTE and
    /// therefore must run on the UI thread.
    ///
    /// <para/>
    /// <b>m33 (BP-6):</b> inherits the shared <see cref="QueryDrivenFinderBase{THit}"/> — the
    /// derived class provides ONLY <see cref="Name"/>, the async matcher hook
    /// (<see cref="MatchAsync"/>), and the log literals. The ctor signature is byte-identical to
    /// the pre-m33 6-arg form (the <see cref="IFzfEngine"/> is the only addition over GrepFinder).
    /// </summary>
    public sealed class FzfFinder : QueryDrivenFinderBase<FzfHit>
    {
        private readonly IFzfEngine _fzf;

        public override string Name => "Fzf";

        public override bool IsQueryDriven => true;

        /// <param name="dteFactory">Returns the top-level DTE automation object (see <see cref="FileFinder"/>).</param>
        /// <param name="fileCache">Shared project-file enumeration cache (amortizes the per-query solution walk).</param>
        /// <param name="fzf">The fzf availability + filter engine.</param>
        /// <param name="contentCache">Shared file-content cache (D7/BP-14 — ONE instance injected from the controller; m8/BP-14 makes it a REQUIRED param so a finder can never silently revert to its own cache).</param>
        /// <param name="testEnumerate">Hermetic-test seam: when set, candidate gathering bypasses DTE entirely (the shared cache serves the delegate once across queries).</param>
        /// <param name="testOpener">Hermetic-test seam: when set, opening bypasses DTE entirely.</param>
        internal FzfFinder(
            Func<DTE> dteFactory,
            ProjectFileCache fileCache,
            IFzfEngine fzf,
            FileContentCache contentCache,
            Func<IReadOnlyList<string>>? testEnumerate = null,
            Action<FzfHit>? testOpener = null)
            : base(dteFactory, fileCache, contentCache, testEnumerate, testOpener)
        {
            _fzf = fzf ?? throw new ArgumentNullException(nameof(fzf));
        }

        protected override string HitsLiteral => "fzf hits=";

        protected override string OpenLiteral => "opened fzf: file=";

        protected override string EnumerateFailureLiteral => "FzfFinder failed to enumerate: ";

        protected override string LineTextOf(FzfHit hit) => hit.LineText;

        protected override string OpenErrorNoun => "fzf";

        /// <summary>
        /// The async matcher hook (m33/BP-6): the ONLY behavioral difference from GrepFinder. Runs
        /// inside the base's ONE <c>Task.Run</c> (M4a/BP-2) — the per-file content reads,
        /// BuildCandidates, FilterAsync, MapBatched, AND the literal-fallback scan. M3 (BP-1): a
        /// null FilterAsync result (timeout/crash) is treated as failure → the literal fallback
        /// scan (never garbage).
        /// </summary>
        protected override async Task<IReadOnlyList<FzfHit>> MatchAsync(IReadOnlyList<string> files, string query, CancellationToken token)
        {
            bool available = await _fzf.IsAvailableAsync();
            if (!available)
            {
                TelescopeLog.Log("fzf unavailable — literal fallback");
            }

            var result = new List<FzfHit>();

            var filesLines = new List<(string Path, IReadOnlyList<string> Lines)>();
            foreach (string path in files)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                string[] lines;
                try
                {
                    lines = Cache.GetLines(path);
                }
                catch
                {
                    // unreadable/binary file — skip
                    continue;
                }
                filesLines.Add((path, lines));
            }

            bool useFzf = available;
            IReadOnlyList<string>? matched = null;
            if (useFzf)
            {
                // M2 (BP-2): ONE batched fzf --filter call across ALL files' lines (fzf ranks
                // globally); the boundary-aware mapper maps the ranked output back to (file, line).
                IReadOnlyList<string> candidates = FzfLineMapper.BuildCandidates(filesLines);
                matched = await _fzf.FilterAsync(candidates, query, token);
                // M3 (BP-1): a null result (timeout/crash) is a failure signal — fall back to
                // the literal scan, never map null back as matches.
                useFzf = matched != null;
            }

            if (useFzf)
            {
                foreach ((int fileIndex, int lineNumber) in FzfLineMapper.MapBatched(filesLines, matched!))
                {
                    if (result.Count >= FinderConstants.HitCap)
                    {
                        break;
                    }
                    string path = filesLines[fileIndex].Path;
                    result.Add(new FzfHit(path, lineNumber, filesLines[fileIndex].Lines[lineNumber - 1]));
                }
            }
            else
            {
                foreach ((string path, IReadOnlyList<string> lines) in filesLines)
                {
                    foreach (int ln in LiteralLineScanner.Scan(lines, query, FinderConstants.HitCap - result.Count))
                    {
                        result.Add(new FzfHit(path, ln, lines[ln - 1]));
                    }
                }
            }

            return result;
        }
    }
}
