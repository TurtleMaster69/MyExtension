using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Telescope.Logging;

namespace Telescope.Finders
{
    /// <summary>
    /// Shared skeleton for a Telescope finder: owns the UI-thread assert, the try/catch gather,
    /// the hit→entry mapping, and the open-with-error-handling flow. Subclasses supply the hit
    /// model, the gather, the entry mapping, and the open action.
    /// </summary>
    public abstract class FinderBase<THit> : IFinder where THit : IFileLocation
    {
        public abstract string Name { get; }

        public virtual bool IsQueryDriven => false;

        protected abstract IReadOnlyList<THit> GatherHits();

        protected abstract FinderEntry ToEntry(THit hit);

        protected abstract void OpenHit(THit hit);

        public virtual IReadOnlyList<FinderEntry> GetCandidates(string query = "")
        {
            AssertUiThread();

            IReadOnlyList<THit> hits;
            try
            {
                hits = GatherHits() ?? Array.Empty<THit>();
            }
            catch (Exception ex)
            {
                TelescopeLog.Log(GatherErrorLiteral(ex));
                hits = Array.Empty<THit>();
            }

            return hits.Select(ToEntry).ToList();
        }

        public virtual Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "")
            => Task.FromResult(GetCandidates(query));

        public void OnSelected(FinderEntry entry)
        {
            if (entry.Payload is not THit hit)
            {
                return;
            }

            AssertUiThread();

            try
            {
                OpenHit(hit);
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"open {OpenErrorNoun} failed: {ex.Message}");
            }
        }

        protected void AssertUiThread()
        {
            if (ThreadHelper.JoinableTaskContext != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
            }
        }

        protected virtual string GatherErrorLiteral(Exception ex) => $"{GetType().Name} failed to enumerate: {ex.Message}";

        protected virtual string OpenErrorNoun => "item";
    }
}
