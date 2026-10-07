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
    /// </summary>
    public sealed class FzfFinder : FinderBase<FzfHit>
    {
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
        /// <param name="testEnumerate">Hermetic-test seam: when set, candidate gathering bypasses DTE entirely (the shared cache serves the delegate once across queries).</param>
        /// <param name="testOpener">Hermetic-test seam: when set, opening bypasses DTE entirely.</param>
        internal FzfFinder(
            Func<DTE> dteFactory,
            ProjectFileCache fileCache,
            IFzfEngine fzf,
            FileContentCache contentCache,
            Func<IReadOnlyList<string>>? testEnumerate = null,
            Action<FzfHit>? testOpener = null)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            _fileCache = fileCache ?? throw new ArgumentNullException(nameof(fileCache));
            _fzf = fzf ?? throw new ArgumentNullException(nameof(fzf));
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _testEnumerate = testEnumerate;
            _testOpener = testOpener;
        }

        /// <summary>BP-D3 (m53): the shared content cache (the reflection-free seam).</summary>
        internal FileContentCache ContentCache => _contentCache;

        protected override IReadOnlyList<FzfHit> GatherHits() => throw new NotSupportedException("FzfFinder is query-driven; call GetCandidatesAsync(query)");

        public override IReadOnlyList<FinderEntry> GetCandidates(string query)
        {
            // The overlay-open path calls the SYNC GetCandidates("") with an empty query (D2b):
            // it must return empty (no gather, no failure log), matching the async path's
            // empty-query short-circuit. A non-empty sync call cannot await fzf, so it fails
            // loudly instead of silently returning the wrong (unfiltered) list.
            if (string.IsNullOrEmpty(query))
            {
                return Array.Empty<FinderEntry>();
            }
            throw new NotSupportedException("FzfFinder is query-driven; call GetCandidatesAsync(query)");
        }

        public override async Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "", CancellationToken cancellationToken = default)
        {
            // The overlay calls this on every prompt change, including a cleared prompt: an empty
            // query must NOT spawn fzf. M3 (BP-3): the eager warm-up is dropped — the first gather
            // populates the shared content cache lazily.
            if (string.IsNullOrEmpty(query))
            {
                return Array.Empty<FinderEntry>();
            }

            var hits = new List<FzfHit>();

            bool available = await _fzf.IsAvailableAsync();
            if (!available)
            {
                TelescopeLog.Log("fzf unavailable — literal fallback");
            }

            IReadOnlyList<string> files = EnumerateFiles();

            // M3 (BP-5): read every file's lines off-thread (the content reads are pure file I/O).
            // The batched fzf path (BP-2) and the literal-fallback path share this block.
            var filesLines = await Task.Run(() =>
            {
                var result = new List<(string Path, IReadOnlyList<string> Lines)>();
                foreach (string path in files)
                {
                    if (cancellationToken.IsCancellationRequested)
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
                    result.Add((path, lines));
                }
                return result;
            }, cancellationToken);

            if (available)
            {
                // M2 (BP-2): ONE batched fzf --filter call across ALL files' lines (fzf ranks
                // globally); the boundary-aware mapper maps the ranked output back to (file, line).
                IReadOnlyList<string> candidates = FzfLineMapper.BuildCandidates(filesLines);
                IReadOnlyList<string> matched = await _fzf.FilterAsync(candidates, query, cancellationToken);
                foreach ((int fileIndex, int lineNumber) in FzfLineMapper.MapBatched(filesLines, matched))
                {
                    if (hits.Count >= FinderConstants.HitCap)
                    {
                        break;
                    }
                    string path = filesLines[fileIndex].Path;
                    hits.Add(new FzfHit(path, lineNumber, filesLines[fileIndex].Lines[lineNumber - 1]));
                }
            }
            else
            {
                foreach ((string path, IReadOnlyList<string> lines) in filesLines)
                {
                    foreach (int ln in LiteralLineScanner.Scan(lines.ToArray(), query, FinderConstants.HitCap - hits.Count))
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
