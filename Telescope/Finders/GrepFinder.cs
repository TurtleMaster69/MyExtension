using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <b>Threading:</b> <see cref="GetCandidates(string)"/> and <see cref="OnSelected"/> touch DTE
    /// and therefore must run on the UI thread (the overlay's debounce resumes on the UI thread).
    /// </summary>
    public sealed class GrepFinder : FinderBase<GrepHit>
    {
        /// <summary>Total-hit cap: a query that matches everything must not stall the UI.</summary>
        private const int HitCap = 200;

        private readonly Func<DTE> _dteFactory;
        private readonly ProjectFileCache _fileCache;
        private readonly FileContentCache _contentCache = new FileContentCache(500);
        private string? _cachedSolutionName;

        // Hermetic-test seams: when set, candidate gathering and opening bypass DTE entirely.
        private readonly Func<IReadOnlyList<string>>? _testEnumerate;
        private readonly Action<GrepHit>? _testOpener;

        public override string Name => "Grep";

        public override bool IsQueryDriven => true;

        /// <param name="dteFactory">Returns the top-level DTE automation object (see <see cref="FileFinder"/>).</param>
        /// <param name="fileCache">Shared project-file enumeration cache (amortizes the per-query solution walk).</param>
        internal GrepFinder(Func<DTE> dteFactory, ProjectFileCache fileCache)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            _fileCache = fileCache ?? throw new ArgumentNullException(nameof(fileCache));
        }

        /// <summary>Test-only constructor: scans the given files' content for the query and reports opens without DTE.</summary>
        internal GrepFinder(Func<IReadOnlyList<string>> fileSource, Action<GrepHit> opener)
            : this(new ProjectFileCache(), fileSource, opener)
        {
        }

        /// <summary>Test-only constructor: routes the enumerate delegate through the shared cache (BP-1/M5a).</summary>
        internal GrepFinder(ProjectFileCache cache, Func<IReadOnlyList<string>> enumerate, Action<GrepHit> opener)
        {
            _fileCache = cache ?? throw new ArgumentNullException(nameof(cache));
            _testEnumerate = enumerate;
            _testOpener = opener;
            _dteFactory = () => null!;
        }

        protected override IReadOnlyList<GrepHit> GatherHits() => throw new NotSupportedException("GrepFinder is query-driven; call GetCandidates(query)");

        public override IReadOnlyList<FinderEntry> GetCandidates(string query)
        {
            // Empty query -> deterministic empty initial state (NO gather log, so the finder-open
            // line emits only the generic "open finder=Grep candidates=0"). This is also the
            // overlay-open path, so warm the shared content cache once here (N32/BP-45) so the
            // per-query scan hits a warm cache instead of re-reading every file on the UI thread.
            if (string.IsNullOrEmpty(query))
            {
                WarmContentCache();
                return Array.Empty<FinderEntry>();
            }

            var hits = new List<GrepHit>();

            if (_testEnumerate != null)
            {
                // Hermetic test path: no VS thread affinity; the shared cache serves the enumerate
                // delegate once across queries.
                foreach (string path in _fileCache.Get(_testEnumerate))
                {
                    ScanFile(path, query, hits);
                    if (hits.Count >= HitCap)
                    {
                        break;
                    }
                }
                TelescopeLog.Log($"grep hits={hits.Count}");
                return hits.Select(ToEntry).ToList();
            }

            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                DTE dte = _dteFactory();
                if (dte?.Solution != null)
                {
                    string? solutionName = dte?.Solution?.FullName;
                    if (!string.Equals(_cachedSolutionName, solutionName, StringComparison.OrdinalIgnoreCase))
                    {
                        _fileCache.Invalidate();
                        _cachedSolutionName = solutionName;
                    }
                    // M2: the per-file content scan is pure file I/O (no VS API) but the
                    // synchronous blocking is unchanged — drop the wasted Task.Run thread hop and
                    // scan inline on the UI thread.
                    IReadOnlyList<string> files = _fileCache.Get(() => ProjectFiles.Enumerate(dte));
                    foreach (string path in files)
                    {
                        ScanFile(path, query, hits);
                        if (hits.Count >= HitCap)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"GrepFinder failed to enumerate: {ex.Message}");
            }

            TelescopeLog.Log($"grep hits={hits.Count}");
            return hits.Select(ToEntry).ToList();
        }

        /// <summary>
        /// One-time pre-read of the solution's project files into the shared content cache when the
        /// Grep overlay opens (N32/BP-45), so the per-query scan hits a warm cache. Best-effort:
        /// unreadable files are skipped. Runs on the UI thread (the overlay-open path).
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
                    string? solutionName = dte?.Solution?.FullName;
                    if (!string.Equals(_cachedSolutionName, solutionName, StringComparison.OrdinalIgnoreCase))
                    {
                        _fileCache.Invalidate();
                        _cachedSolutionName = solutionName;
                    }
                    foreach (string path in _fileCache.Get(() => ProjectFiles.Enumerate(dte)))
                    {
                        WarmFile(path);
                    }
                }
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"GrepFinder failed to enumerate: {ex.Message}");
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

        private void ScanFile(string path, string query, List<GrepHit> hits)
        {
            try
            {
                string[] lines = _contentCache.GetLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (hits.Count >= HitCap)
                    {
                        break;
                    }
                    string line = lines[i];
                    if (line.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hits.Add(new GrepHit(path, i + 1, line));
                    }
                }
            }
            catch
            {
                // unreadable/binary file — skip
            }
        }
    }
}
