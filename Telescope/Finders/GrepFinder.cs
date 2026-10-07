using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public sealed class GrepFinder : FinderBase<GrepHit>
    {
        private readonly Func<DTE> _dteFactory;
        private readonly ProjectFileCache _fileCache;
        private readonly FileContentCache _contentCache;
        private string? _cachedSolutionName;

        // Hermetic-test seams: when set, candidate gathering and opening bypass DTE entirely.
        private readonly Func<IReadOnlyList<string>>? _testEnumerate;
        private readonly Action<GrepHit>? _testOpener;

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
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            _fileCache = fileCache ?? throw new ArgumentNullException(nameof(fileCache));
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _testEnumerate = testEnumerate;
            _testOpener = testOpener;
        }

        /// <summary>BP-D3 (m53): the shared content cache (the reflection-free seam).</summary>
        internal FileContentCache ContentCache => _contentCache;

        protected override IReadOnlyList<GrepHit> GatherHits() => throw new NotSupportedException("GrepFinder is query-driven; call GetCandidates(query)");

        public override IReadOnlyList<FinderEntry> GetCandidates(string query)
        {
            // M1 (BP-1): the sync entry point delegates to the async override (test-only +
            // empty-query path; never called on the UI thread in production — the overlay uses
            // the async override).
            return GetCandidatesAsync(query).GetAwaiter().GetResult();
        }

        public override async Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "", CancellationToken cancellationToken = default)
        {
            // Empty query -> deterministic empty initial state (NO gather log, so the finder-open
            // line emits only the generic "open finder=Grep candidates=0"). M3 (BP-3): the eager
            // warm-up is dropped — the first gather populates the shared content cache lazily.
            if (string.IsNullOrEmpty(query))
            {
                return Array.Empty<FinderEntry>();
            }

            var hits = new List<GrepHit>();

            if (_testEnumerate != null)
            {
                // Hermetic test path: no VS thread affinity; the shared cache serves the enumerate
                // delegate once across queries. The token is honored in the loop (BP-1).
                foreach (string path in _fileCache.Get(_testEnumerate))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    ScanFile(path, query, hits, _contentCache);
                    if (hits.Count >= FinderConstants.HitCap)
                    {
                        break;
                    }
                }
                TelescopeLog.Log($"grep hits={hits.Count}");
                return hits.Select(ToEntry).ToList();
            }

            // M1 (BP-1): the DTE enumeration + the solution-name compare stay on the UI thread
            // (the sync helper asserts it); the per-file content scan is pure file I/O (no VS API)
            // — run it on a background task so the UI thread is not blocked by the synchronous
            // reads. The shared content cache is thread-safe (m10/BP-1), and the _queryGeneration
            // marshal-back guard in TelescopeOverlay.RefreshQueryDrivenAsync discards stale results.
            IReadOnlyList<string> files = EnumerateFiles();

            try
            {
                await Task.Run(() =>
                {
                    foreach (string path in files)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        ScanFile(path, query, hits, _contentCache);
                        if (hits.Count >= FinderConstants.HitCap)
                        {
                            break;
                        }
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"GrepFinder failed to enumerate: {ex.Message}");
            }

            TelescopeLog.Log($"grep hits={hits.Count}");
            return hits.Select(ToEntry).ToList();
        }

        /// <summary>
        /// Enumerates the solution's project files (test seam or DTE). Synchronous so the UI-thread
        /// assert lives outside the async gather (VSTHRD109 forbids throwing in an async method).
        /// </summary>
        private IReadOnlyList<string> EnumerateFiles()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                DTE dte = _dteFactory();
                if (dte?.Solution != null)
                {
                    // m7 (BP-13): the ONE shared solution-invalidation helper (replaces the
                    // duplicated compare + Invalidate block).
                    ProjectFileCache.EnsureSolutionCache(_fileCache, ref _cachedSolutionName, dte?.Solution?.FullName);
                    return _fileCache.Get(() => ProjectFiles.Enumerate(dte));
                }
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"GrepFinder failed to enumerate: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        protected override FinderEntry ToEntry(GrepHit hit)
        {
            string display = $"{Path.GetFileName(hit.FilePath)}:{hit.LineNumber}: {hit.LineText}";
            return new FinderEntry(display, hit);
        }

        protected override void OpenHit(GrepHit hit)
        {
            if (_testOpener != null)
            {
                // Hermetic test path: no VS thread affinity.
                _testOpener(hit);
                return;
            }

            HitOpener.OpenAtLine(hit, (path, line) =>
            {
                DTE dte = _dteFactory();
                DteFileOpener.OpenAtLine(dte, path, line);
                TelescopeLog.Log($"opened grep: file={path} line={line}");
            });
        }

        protected override string OpenErrorNoun => "grep";

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
