using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Telescope
{
    /// <summary>
    /// A Telescope finder that lists the <b>implementations/overrides</b> of the symbol at the
    /// caret in the active document (interfaces → implementing types/members, abstract/virtual
    /// members → overrides, classes → derived classes), with each hit's declaring source
    /// location. Selecting an entry opens the hit file in the editor and jumps the caret to the
    /// implementation line.
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
    public sealed class ImplementationFinder : FinderBase<ImplementationHit>
    {
        private readonly Func<IReadOnlyList<ImplementationHit>> _gatherer;
        private readonly Action<ImplementationHit> _opener;

        public override string Name => "Implementation";

        /// <param name="gatherer">Returns the implementation hits for the symbol at the caret (host-side Roslyn call).</param>
        /// <param name="opener">Opens a hit's file at its line (host-side DTE call).</param>
        public ImplementationFinder(Func<IReadOnlyList<ImplementationHit>> gatherer, Action<ImplementationHit> opener)
        {
            _gatherer = gatherer ?? throw new ArgumentNullException(nameof(gatherer));
            _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        }

        protected override IReadOnlyList<ImplementationHit> GatherHits()
        {
            IReadOnlyList<ImplementationHit> hits = _gatherer() ?? Array.Empty<ImplementationHit>();
            TelescopeLog.Log($"implementations gathered count={hits.Count}");
            return hits;
        }

        protected override FinderEntry ToEntry(ImplementationHit hit)
        {
            string display = $"{hit.Kind} {hit.SymbolName} — {Path.GetFileName(hit.FilePath)}:{hit.LineNumber}";
            return new FinderEntry(display, hit);
        }

        protected override void OpenHit(ImplementationHit hit)
        {
            _opener(hit);
            TelescopeLog.Log($"opened implementation: file={hit.FilePath} line={hit.LineNumber}");
        }

        protected override string OpenErrorNoun => "implementation";

        protected override string GatherErrorLiteral(Exception ex) => $"{Telescope.DiagnosticLog.Telescope}implementations gather failed: {ex.Message}";
    }
}
