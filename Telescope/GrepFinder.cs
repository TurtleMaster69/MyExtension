using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Telescope
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

        // Hermetic-test seams: when set, candidate gathering and opening bypass DTE entirely.
        private readonly Func<IReadOnlyList<string>>? _testFileSource;
        private readonly Action<GrepHit>? _testOpener;

        public override string Name => "Grep";

        public override bool IsQueryDriven => true;

        /// <param name="dteFactory">Returns the top-level DTE automation object (see <see cref="FileFinder"/>).</param>
        public GrepFinder(Func<DTE> dteFactory)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
        }

        /// <summary>Test-only constructor: scans the given files' content for the query and reports opens without DTE.</summary>
        internal GrepFinder(Func<IReadOnlyList<string>> fileSource, Action<GrepHit> opener)
        {
            _testFileSource = fileSource;
            _testOpener = opener;
            _dteFactory = () => null!;
        }

        protected override IReadOnlyList<GrepHit> GatherHits() => Array.Empty<GrepHit>();

        public override IReadOnlyList<FinderEntry> GetCandidates(string query)
        {
            // Empty query -> deterministic empty initial state (NO gather log, so the finder-open
            // line emits only the generic "open finder=Grep candidates=0").
            if (string.IsNullOrEmpty(query))
            {
                return Array.Empty<FinderEntry>();
            }

            var hits = new List<GrepHit>();

            if (_testFileSource != null)
            {
                // Hermetic test path: no VS thread affinity.
                foreach (string path in _testFileSource())
                {
                    ScanFile(path, query, hits);
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
                    foreach (string path in ProjectFiles.Enumerate(dte))
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
                NeoVisualLog.Debug($"{Telescope.DiagnosticLog.Telescope}GrepFinder failed to enumerate: {ex.Message}");
            }

            TelescopeLog.Log($"grep hits={hits.Count}");
            return hits.Select(ToEntry).ToList();
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

            if (!File.Exists(hit.FilePath))
            {
                return;
            }

            DTE dte = _dteFactory();
            DteFileOpener.OpenAtLine(dte, hit.FilePath, hit.LineNumber);
            TelescopeLog.Log($"opened grep: file={hit.FilePath} line={hit.LineNumber}");
        }

        protected override string OpenErrorNoun => "grep";

        private static void ScanFile(string path, string query, List<GrepHit> hits)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
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
