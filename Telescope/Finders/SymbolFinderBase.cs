using System;
using System.Collections.Generic;
using System.IO;
using Telescope.Logging;

namespace Telescope.Finders
{
    /// <summary>
    /// Shared base for the symbol-at-caret finders (<see cref="DefinitionFinder"/> /
    /// <see cref="ImplementationFinder"/>): owns the injected gatherer/opener ctor pattern,
    /// the shared display contract ({Kind} {SymbolName} — {file}:{line}), and the
    /// gather/open/count logging. Subclasses supply the finder-specific literals and may
    /// override <see cref="OrderHits"/> for finder-side deterministic ordering.
    /// </summary>
    public abstract class SymbolFinderBase : FinderBase<DefinitionHit>
    {
        private readonly Func<IReadOnlyList<DefinitionHit>> _gatherer;
        private readonly Action<DefinitionHit> _opener;

        protected SymbolFinderBase(Func<IReadOnlyList<DefinitionHit>> gatherer, Action<DefinitionHit> opener)
        {
            _gatherer = gatherer ?? throw new ArgumentNullException(nameof(gatherer));
            _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        }

        /// <summary>The gather-count literal prefix, e.g. "definitions gathered".</summary>
        protected abstract string GatherCountLiteral { get; }

        /// <summary>The open-outcome literal prefix, e.g. "opened definition:".</summary>
        protected abstract string OpenedLiteral { get; }

        /// <summary>The open-error noun, e.g. "definition".</summary>
        protected override abstract string OpenErrorNoun { get; }

        /// <summary>The gather-error literal prefix, e.g. "definitions gather failed".</summary>
        protected abstract string GatherErrorPrefix { get; }

        /// <summary>Finder-side deterministic ordering hook (identity by default).</summary>
        protected virtual IReadOnlyList<DefinitionHit> OrderHits(IReadOnlyList<DefinitionHit> hits) => hits;

        protected override IReadOnlyList<DefinitionHit> GatherHits()
        {
            IReadOnlyList<DefinitionHit> hits = OrderHits(_gatherer() ?? Array.Empty<DefinitionHit>());
            TelescopeLog.Log($"{GatherCountLiteral} count={hits.Count}");
            return hits;
        }

        protected override FinderEntry ToEntry(DefinitionHit hit)
        {
            string display = $"{hit.Kind} {hit.SymbolName} — {Path.GetFileName(hit.FilePath)}:{hit.LineNumber}";
            return new FinderEntry(display, hit);
        }

        protected override void OpenHit(DefinitionHit hit)
        {
            _opener(hit);
            TelescopeLog.Log($"{OpenedLiteral} file={hit.FilePath} line={hit.LineNumber}");
        }

        protected override string GatherErrorLiteral(Exception ex) => $"{GatherErrorPrefix}: {ex.Message}";
    }
}
