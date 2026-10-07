using System;
using System.Collections.Generic;
using ImplementationHit = Telescope.Finders.DefinitionHit;

namespace Telescope.Finders
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
    public sealed class ImplementationFinder : SymbolFinderBase
    {
        public override string Name => "Implementation";

        /// <param name="gatherer">Returns the implementation hits for the symbol at the caret (host-side Roslyn call).</param>
        /// <param name="opener">Opens a hit's file at its line (host-side DTE call).</param>
        public ImplementationFinder(Func<IReadOnlyList<ImplementationHit>> gatherer, Action<ImplementationHit> opener)
            : base(gatherer, opener)
        {
        }

        protected override string GatherCountLiteral => "implementations gathered";

        protected override string OpenedLiteral => "opened implementation:";

        protected override string OpenErrorNoun => "implementation";

        protected override string GatherErrorPrefix => "implementations gather failed";
    }
}
