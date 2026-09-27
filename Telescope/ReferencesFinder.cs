using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Telescope
{
    /// <summary>
    /// A Telescope finder that lists the <b>references</b> to the symbol at the caret in the
    /// active document, with each hit's read/write access (from VS's Roslyn find-references
    /// engine). Selecting an entry opens the hit file in the editor and jumps the caret to the
    /// reference line.
    ///
    /// <para/>
    /// The finder is hermetic-testable: its ONLY data source and action are the injected
    /// gatherer (<see cref="Func{TResult}"/>) and opener (<see cref="Action{T}"/>), supplied by
    /// the host (the VS package), so there is no DTE path inside the finder itself.
    ///
    /// <para/>
    /// <b>Threading:</b> <see cref="GetCandidates"/> and <see cref="OnSelected"/> run on the UI
    /// thread (asserted); the injected gatherer/opener are the host's UI-thread calls.
    /// </summary>
    public sealed class ReferencesFinder : IFinder
    {
        private readonly Func<IReadOnlyList<ReferenceHit>> _gatherer;
        private readonly Action<ReferenceHit> _opener;

        public string Name => "References";

        /// <param name="gatherer">Returns the reference hits for the symbol at the caret (host-side Roslyn call).</param>
        /// <param name="opener">Opens a hit's file at its line (host-side DTE call).</param>
        public ReferencesFinder(Func<IReadOnlyList<ReferenceHit>> gatherer, Action<ReferenceHit> opener)
        {
            _gatherer = gatherer ?? throw new ArgumentNullException(nameof(gatherer));
            _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        }

        public IReadOnlyList<FinderEntry> GetCandidates()
        {
            // UI-thread assert (IFinder contract, parity with CodeIssuesFinder). Outside VS the
            // JoinableTaskContext is uninitialized — treated as on-UI-thread, so the unit-test
            // host does not throw here.
            if (ThreadHelper.JoinableTaskContext != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
            }

            IReadOnlyList<ReferenceHit> hits;
            try
            {
                hits = _gatherer() ?? Array.Empty<ReferenceHit>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.Telescope}references gather failed: {ex.Message}");
                hits = Array.Empty<ReferenceHit>();
            }

            int reads = hits.Count(h => !h.IsWrite);
            int writes = hits.Count(h => h.IsWrite);
            NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}references gathered reads={reads} writes={writes}");
            return hits.Select(ToEntry).ToList();
        }

        public void OnSelected(FinderEntry entry)
        {
            // UI-thread assert (IFinder contract, parity with CodeIssuesFinder). Outside VS the
            // JoinableTaskContext is uninitialized — treated as on-UI-thread, so the unit-test
            // host does not throw here.
            if (ThreadHelper.JoinableTaskContext != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
            }

            if (entry.Payload is not ReferenceHit hit)
            {
                return;
            }

            try
            {
                _opener(hit);
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}opened reference: file={hit.FilePath} line={hit.LineNumber} col={hit.Column} access={(hit.IsWrite ? "write" : "read")}");
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}open reference failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.Telescope}ReferencesFinder failed to open '{entry.Display}': {ex.Message}");
            }
        }

        private static FinderEntry ToEntry(ReferenceHit hit)
        {
            string access = hit.IsWrite ? "write" : "read";
            string display = $"{hit.Symbol} ({access}) {Path.GetFileName(hit.FilePath)}:{hit.LineNumber}:{hit.Column} — {hit.LineText}";
            return new FinderEntry(display, hit);
        }
    }
}