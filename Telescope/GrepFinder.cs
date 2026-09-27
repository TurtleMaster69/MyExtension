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
    public sealed class GrepFinder : IFinder, IQueryFinder
    {
        /// <summary>Total-hit cap: a query that matches everything must not stall the UI.</summary>
        private const int HitCap = 200;

        private readonly Func<DTE> _dteFactory;

        // Hermetic-test seams: when set, candidate gathering and opening bypass DTE entirely.
        private readonly Func<IReadOnlyList<string>>? _testFileSource;
        private readonly Action<GrepHit>? _testOpener;

        public string Name => "Grep";

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

        public IReadOnlyList<FinderEntry> GetCandidates()
        {
            return GetCandidates(string.Empty);
        }

        public IReadOnlyList<FinderEntry> GetCandidates(string query)
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
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}grep hits={hits.Count}");
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
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.Telescope}GrepFinder failed to enumerate: {ex.Message}");
            }

            NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}grep hits={hits.Count}");
            return hits.Select(ToEntry).ToList();
        }

        public void OnSelected(FinderEntry entry)
        {
            if (entry.Payload is not GrepHit hit)
            {
                return;
            }

            if (_testOpener != null)
            {
                // Hermetic test path: no VS thread affinity.
                _testOpener(hit);
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (!File.Exists(hit.FilePath))
                {
                    return;
                }

                DTE dte = _dteFactory();
                dte.ItemOperations.OpenFile(hit.FilePath);
                GotoLine(dte, hit.LineNumber);
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}opened grep: file={hit.FilePath} line={hit.LineNumber}");
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}open grep failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.Telescope}GrepFinder failed to open '{entry.Display}': {ex.Message}");
            }
        }

        /// <summary>Jumps the active document's selection to the given 1-based line.</summary>
        private static void GotoLine(DTE dte, int line)
        {
            try
            {
                var doc = dte.ActiveDocument;
                if (doc?.Selection is TextSelection selection && line > 0)
                {
                    selection.GotoLine(line, false);
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}goto line={line}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.Telescope}goto line failed: {ex.Message}");
            }
        }

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

        private static FinderEntry ToEntry(GrepHit hit)
        {
            string display = $"{Path.GetFileName(hit.FilePath)}:{hit.LineNumber}: {hit.LineText}";
            return new FinderEntry(display, hit);
        }
    }
}