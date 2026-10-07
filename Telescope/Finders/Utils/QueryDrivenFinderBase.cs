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
    /// Shared skeleton for the query-driven finders (Grep + Fzf — m33/BP-6): owns the ctor, the
    /// <see cref="ContentCache"/> seam, <c>EnumerateFiles</c> (with a virtual enumerate-failure
    /// literal), <c>ToEntry</c> (the <c>{fileName}:{line}: {lineText}</c> display), <c>OpenHit</c>
    /// (with a virtual open literal), the whitespace-query short-circuit (PINNED to
    /// <c>IsNullOrWhiteSpace</c>), <c>EnsureSolutionCache</c>, and the async gather skeleton (the
    /// <c>Task.Run</c> shape). The derived classes provide ONLY: <c>Name</c>, the async matcher
    /// hook (<see cref="MatchAsync"/>), and the log literals.
    ///
    /// <para/>
    /// <b>Accessibility (adjudicated):</b> the base is <c>public</c> because the derived finders
    /// (<see cref="GrepFinder"/>/<see cref="FzfFinder"/>) are <c>public</c> — an <c>internal</c>
    /// base would be an inconsistent-accessibility error. It is only used internally + via
    /// InternalsVisibleTo tests; a visibility adjustment, not a contract change.
    /// </summary>
    public abstract class QueryDrivenFinderBase<THit> : FinderBase<THit> where THit : IFileLocation
    {
        private readonly Func<DTE> _dteFactory;
        private readonly ProjectFileCache _fileCache;
        private readonly FileContentCache _contentCache;
        private string? _cachedSolutionName;

        // Hermetic-test seams: when set, candidate gathering and opening bypass DTE entirely.
        private readonly Func<IReadOnlyList<string>>? _testEnumerate;
        private readonly Action<THit>? _testOpener;

        // The ctor is internal (not protected): the base is public but its parameter types
        // (ProjectFileCache/FileContentCache) are internal, and the derived finders live in the
        // same assembly.
        internal QueryDrivenFinderBase(
            Func<DTE> dteFactory,
            ProjectFileCache fileCache,
            FileContentCache contentCache,
            Func<IReadOnlyList<string>>? testEnumerate = null,
            Action<THit>? testOpener = null)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            _fileCache = fileCache ?? throw new ArgumentNullException(nameof(fileCache));
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _testEnumerate = testEnumerate;
            _testOpener = testOpener;
        }

        /// <summary>BP-D3 (m53): the shared content cache (the reflection-free seam).</summary>
        internal FileContentCache ContentCache => _contentCache;

        /// <summary>The shared content cache (the matcher hook reads through it). Internal (not
        /// protected) because <see cref="FileContentCache"/> is internal and the base is public.</summary>
        internal FileContentCache Cache => _contentCache;

        /// <summary>The <c>grep hits=</c>/<c>fzf hits=</c> gather-summary literal.</summary>
        protected abstract string HitsLiteral { get; }

        /// <summary>The <c>opened grep: file=</c>/<c>opened fzf: file=</c> open literal.</summary>
        protected abstract string OpenLiteral { get; }

        /// <summary>The <c>GrepFinder failed to enumerate: </c>/<c>FzfFinder failed to enumerate: </c> literal.</summary>
        protected abstract string EnumerateFailureLiteral { get; }

        /// <summary>The async matcher hook — the ONLY behavioral difference between the derived finders.</summary>
        protected abstract Task<IReadOnlyList<THit>> MatchAsync(IReadOnlyList<string> files, string query, CancellationToken token);

        /// <summary>The line text for the <c>{fileName}:{line}: {lineText}</c> display.</summary>
        protected virtual string LineTextOf(THit hit) => string.Empty;

        protected override IReadOnlyList<THit> GatherHits() => throw new NotSupportedException($"{GetType().Name} is query-driven; call GetCandidatesAsync(query)");

        public override IReadOnlyList<FinderEntry> GetCandidates(string query)
        {
            // M4b (BP-3): the sync entry point must NOT block the UI thread on a non-empty query
            // (the old GetAwaiter().GetResult() UI-thread block). An empty query returns empty; a
            // non-empty query throws (mirrors FzfFinder.GetCandidates).
            if (string.IsNullOrEmpty(query))
            {
                return Array.Empty<FinderEntry>();
            }
            throw new NotSupportedException($"{GetType().Name} is query-driven; call GetCandidatesAsync(query)");
        }

        public override async Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "", CancellationToken cancellationToken = default)
        {
            // m33 (BP-6): the whitespace-query short-circuit is PINNED to IsNullOrWhiteSpace — a
            // whitespace query returns 0 entries with NO hits log line.
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<FinderEntry>();
            }

            IReadOnlyList<string> files = EnumerateFiles();

            IReadOnlyList<THit> hits;
            try
            {
                // M4a (BP-2): the whole gather runs inside ONE Task.Run (off the UI thread).
                hits = await Task.Run(() => MatchAsync(files, query, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // m9 (BP-5): a cancelled gather returns silently — no spurious failure line.
                return Array.Empty<FinderEntry>();
            }

            TelescopeLog.Log($"{HitsLiteral}{hits.Count}");
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
                TelescopeLog.Log($"{EnumerateFailureLiteral}{ex.Message}");
                return Array.Empty<string>();
            }
        }

        protected override FinderEntry ToEntry(THit hit)
        {
            string display = $"{Path.GetFileName(hit.FilePath)}:{hit.LineNumber}: {LineTextOf(hit)}";
            return new FinderEntry(display, hit);
        }

        protected override void OpenHit(THit hit)
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
                TelescopeLog.Log($"{OpenLiteral}{path} line={line}");
            });
        }
    }
}
