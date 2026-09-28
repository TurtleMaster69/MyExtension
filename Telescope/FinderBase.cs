using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Telescope
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
                NeoVisualLog.Debug(GatherErrorLiteral(ex));
                hits = Array.Empty<THit>();
            }

            return hits.Select(ToEntry).ToList();
        }

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
                NeoVisualLog.Debug($"{Telescope.DiagnosticLog.Telescope}{FinderNameForErrors} failed to open '{entry.Display}': {ex.Message}");
            }
        }

        protected void AssertUiThread()
        {
            if (ThreadHelper.JoinableTaskContext != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
            }
        }

        protected virtual string GatherErrorLiteral(Exception ex) => $"{Telescope.DiagnosticLog.Telescope}{GetType().Name} failed to enumerate: {ex.Message}";

        protected virtual string OpenErrorNoun => "item";

        protected virtual string FinderNameForErrors => GetType().Name;
    }
}
