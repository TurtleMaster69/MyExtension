using System;
using System.Collections.Generic;
using System.Linq;

namespace Telescope.Finders
{
    /// <summary>
    /// A Telescope finder that lists the <b>definition locations</b> of the symbol at the
    /// caret in the active document (host-side Roslyn gather via DeclaringSyntaxReferences).
    /// Selecting an entry opens the hit file in the editor and jumps the caret to the
    /// definition line.
    ///
    /// <para/>
    /// The finder is hermetic-testable: its ONLY data source and action are the injected
    /// gatherer (<see cref="Func{TResult}"/>) and opener (<see cref="Action{T}"/>), supplied
    /// by the host (the VS package), so there is no DTE path inside the finder itself.
    ///
    /// <para/>
    /// <b>Threading:</b> <see cref="GetCandidates"/> and <see cref="OnSelected"/> run on the
    /// UI thread (asserted); the injected gatherer/opener are the host's UI-thread calls.
    /// </summary>
    public sealed class DefinitionFinder : SymbolFinderBase
    {
        public override string Name => "Definition";

        /// <param name="gatherer">Returns the definition hits for the symbol at the caret (host-side Roslyn call).</param>
        /// <param name="opener">Opens a hit's file at its line (host-side DTE call).</param>
        public DefinitionFinder(Func<IReadOnlyList<DefinitionHit>> gatherer, Action<DefinitionHit> opener)
            : base(gatherer, opener)
        {
        }

        protected override string GatherCountLiteral => "definitions gathered";

        protected override string OpenedLiteral => "opened definition:";

        protected override string OpenErrorNoun => "definition";

        protected override string GatherErrorPrefix => "definitions gather failed";

        // Finder-side deterministic ordering (contract X5, rev 1): the injected gatherer
        // may return hits in any order — the FINDER owns OrderBy(FilePath).ThenBy(LineNumber)
        // so the determinism contract is hermetically testable
        // (Run_DefinitionFinder_UnorderedGatherOrderedDeterministically).
        protected override IReadOnlyList<DefinitionHit> OrderHits(IReadOnlyList<DefinitionHit> hits)
            => hits.OrderBy(h => h.FilePath, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(h => h.LineNumber)
                   .ToList();
    }
}
