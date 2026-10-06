using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telescope.Filter;
using Telescope.Logging;

namespace Telescope.Finders
{
    /// <summary>
    /// A Telescope finder that fuzzy-matches the solution's project-file <b>contents</b> for the
    /// typed query using fzf (query-driven, per-file <c>fzf --filter</c>). Each row shows
    /// <c>{fileName}:{line}: {lineText}</c>. Selecting an entry opens the hit file in the editor
    /// and jumps the caret to the hit line. When fzf is unavailable the finder falls back to a
    /// literal case-insensitive substring scan (never the full unfiltered list).
    ///
    /// <para/>
    /// <b>Threading:</b> the DTE enumeration path asserts the UI thread; the fzf subprocess itself
    /// runs off-thread inside <see cref="IFzfEngine"/>. <see cref="OnSelected"/> touches DTE and
    /// therefore must run on the UI thread.
    /// </summary>
    public sealed class FzfFinder : FinderBase<FzfHit>
    {
        /// <summary>Total-hit cap: a query that matches everything must not stall the UI.</summary>
        private const int HitCap = 200;

        private readonly Func<DTE> _dteFactory;
        private readonly ProjectFileCache _fileCache;
        private readonly FileContentCache _contentCache;
        private readonly IFzfEngine _fzf;
        private string? _cachedSolutionName;

        // Hermetic-test seams: when set, candidate gathering and opening bypass DTE entirely.
        private readonly Func<IReadOnlyList<string>>? _testEnumerate;
        private readonly Action<FzfHit>? _testOpener;

        public override string Name => "Fzf";

        public override bool IsQueryDriven => true;

        /// <param name="dteFactory">Returns the top-level DTE automation object (see <see cref="FileFinder"/>).</param>
        /// <param name="fileCache">Shared project-file enumeration cache (amortizes the per-query solution walk).</param>
        /// <param name="fzf">The fzf availability + filter engine.</param>
        /// <param name="contentCache">Shared file-content cache (D7/BP-14 — ONE instance injected from the controller; m8/BP-14 makes it a REQUIRED param so a finder can never silently revert to its own cache).</param>
        internal FzfFinder(Func<DTE> dteFactory, ProjectFileCache fileCache, IFzfEngine fzf, FileContentCache contentCache)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            _fileCache = fileCache ?? throw new ArgumentNullException(nameof(fileCache));
            _fzf = fzf ?? throw new ArgumentNullException(nameof(fzf));
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
        }

        /// <summary>Test-only constructor: scans the given files' content for the query and reports opens without DTE.</summary>
        internal FzfFinder(Func<IReadOnlyList<string>> fileSource, Action<FzfHit> opener, IFzfEngine fzf)
            : this(new ProjectFileCache(), fileSource, opener, fzf)
        {
        }

        /// <summary>Test-only constructor: routes the enumerate delegate through the shared cache.</summary>
        internal FzfFinder(ProjectFileCache cache, Func<IReadOnlyList<string>> enumerate, Action<FzfHit> opener, IFzfEngine fzf)
        {
            _fileCache = cache ?? throw new ArgumentNullException(nameof(cache));
            _testEnumerate = enumerate;
            _testOpener = opener;
            _fzf = fzf ?? throw new ArgumentNullException(nameof(fzf));
            _dteFactory = () => null!;
            _contentCache = new FileContentCache(500);
        }

        /// <summary>Test-only constructor: routes the enumerate delegate through the shared content cache (D7/BP-14).</summary>
        internal FzfFinder(FileContentCache contentCache, Func<IReadOnlyList<string>> enumerate, Action<FzfHit> opener, IFzfEngine fzf)
        {
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _testEnumerate = enumerate;
            _testOpener = opener;
            _fzf = fzf ?? throw new ArgumentNullException(nameof(fzf));
            _dteFactory = () => null!;
            _fileCache = new ProjectFileCache();
        }

        protected override IReadOnlyList<FzfHit> GatherHits() => throw new NotSupportedException("FzfFinder is query-driven; call GetCandidatesAsync(query)");

        public override IReadOnlyList<FinderEntry> GetCandidates(string query)
        {
            // Empty query -> deterministic empty initial state (NO gather log, so the finder-open
            // line emits only the generic "open finder=Fzf candidates=0"). This is also the
            // overlay-open path, so warm the shared content cache once here so the per-query
            // gather hits a warm cache instead of re-reading every file on the UI thread.
            if (string.IsNullOrEmpty(query))
            {
                WarmContentCache();
                return Array.Empty<FinderEntry>();
            }

            // A sync method cannot await fzf; the overlay-open path always passes an empty query,
            // so this branch is dead at open and exists only to fail loudly on a future non-empty
            // sync call instead of silently returning the wrong (unfiltered) list.
            throw new NotSupportedException("FzfFinder is query-driven; call GetCandidatesAsync(query)");
        }

        public override async Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "", CancellationToken cancellationToken = default)
        {
            // The overlay calls this on every prompt change, including a cleared prompt: an empty
            // query must NOT spawn fzf. Delegate to the sync short-circuit (warm cache + empty
            // list, no gather log, no fzf spawn).
            if (string.IsNullOrEmpty(query))
            {
                return GetCandidates(query);
            }

            var hits = new List<FzfHit>();

            bool available = await _fzf.IsAvailableAsync();
            if (!available)
            {
                TelescopeLog.Log("fzf unavailable — literal fallback");
            }

            IReadOnlyList<string> files = EnumerateFiles();

            foreach (string path in files)
            {
                // M2 (BP-3): a cancelled gather stops spawning fzf subprocesses (the overlay's
                // _filterCts is cancelled on every query change + close).
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                if (hits.Count >= HitCap)
                {
                    break;
                }

                string[] lines;
                try
                {
                    lines = _contentCache.GetLines(path);
                }
                catch
                {
                    // unreadable/binary file — skip
                    continue;
                }

                if (available)
                {
                    IReadOnlyList<string> matched = await _fzf.FilterAsync(lines, query, cancellationToken);
                    foreach (int ln in FzfLineMapper.Map(lines, matched))
                    {
                        if (hits.Count >= HitCap)
                        {
                            break;
                        }
                        hits.Add(new FzfHit(path, ln, lines[ln - 1]));
                    }
                }
                else
                {
                    foreach (int ln in LiteralLineScanner.Scan(lines, query, HitCap - hits.Count))
                    {
                        hits.Add(new FzfHit(path, ln, lines[ln - 1]));
                    }
                }
            }

            TelescopeLog.Log($"fzf hits={hits.Count}");
            return hits.Select(ToEntry).ToList();
        }

        /// <summary>
        /// Enumerates the solution's project files (test seam or DTE). Synchronous so the UI-thread
        /// assert lives outside the async gather (VSTHRD109 forbids throwing in an async method).
        /// </summary>
        private IReadOnlyList<string> EnumerateFiles()
        {
            if (_testEnumerate != null)
            {
                // Hermetic test path: no VS thread affinity; the shared cache serves the enumerate
                // delegate once across queries.
                return _fileCache.Get(_testEnumerate);
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                DTE dte = _dteFactory();
                if (dte?.Solution != null)
                {
                    // m7 (BP-13): the ONE shared solution-invalidation helper.
                    ProjectFileCache.EnsureSolutionCache(_fileCache, ref _cachedSolutionName, dte?.Solution?.FullName);
                    return _fileCache.Get(() => ProjectFiles.Enumerate(dte));
                }
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"FzfFinder failed to enumerate: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// One-time pre-read of the solution's project files into the shared content cache when the
        /// Fzf overlay opens, so the per-query gather hits a warm cache. Best-effort: unreadable
        /// files are skipped. Runs on the UI thread (the overlay-open path).
        /// </summary>
        private void WarmContentCache()
        {
            if (_testEnumerate != null)
            {
                foreach (string path in _fileCache.Get(_testEnumerate))
                {
                    WarmFile(path);
                }
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                DTE dte = _dteFactory();
                if (dte?.Solution != null)
                {
                    // m7 (BP-13): the ONE shared solution-invalidation helper.
                    ProjectFileCache.EnsureSolutionCache(_fileCache, ref _cachedSolutionName, dte?.Solution?.FullName);
                    foreach (string path in _fileCache.Get(() => ProjectFiles.Enumerate(dte)))
                    {
                        WarmFile(path);
                    }
                }
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"FzfFinder failed to enumerate: {ex.Message}");
            }
        }

        private void WarmFile(string path)
        {
            try
            {
                _contentCache.GetLines(path);
            }
            catch
            {
                // unreadable/binary file — skip
            }
        }

        protected override FinderEntry ToEntry(FzfHit hit)
        {
            string display = $"{Path.GetFileName(hit.FilePath)}:{hit.LineNumber}: {hit.LineText}";
            return new FinderEntry(display, hit);
        }

        protected override void OpenHit(FzfHit hit)
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
                TelescopeLog.Log($"opened fzf: file={path} line={line}");
            });
        }

        protected override string OpenErrorNoun => "fzf";
    }
}
