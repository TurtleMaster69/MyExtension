using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using MyExtension.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyExtension.Package
{
    /// <summary>
    /// VS-coupled gatherer for the severity-filtered diagnostics navigation (Gap 3): reads the
    /// VS Error List through the exact DTE2 API the Telescope CodeIssuesFinder established
    /// (Telescope/Finders/CodeIssuesFinder.cs:170-205), filters to ONE file + ONE severity, and
    /// returns DiagnosticEntry rows under the gather contract DiagnosticNavigator relies on:
    /// 1. severity filter — ErrorItem.ErrorLevel (vsBuildErrorLevel): errors =
    ///    vsBuildErrorLevelHigh, warnings = vsBuildErrorLevelMedium (messages/Low excluded);
    /// 2. file filter — ErrorItem.FileName equals the requested path (OrdinalIgnoreCase);
    /// 3. entries with Line &lt;= 0 dropped;
    /// 4. sorted ascending by Line (stable), duplicate lines collapsed to the FIRST Error List
    ///    item — repeated navigation never re-lands on the same line.
    /// Per-item read failures are skipped (the CodeIssuesFinder discipline); an outer read
    /// failure PROPAGATES so the caller logs <c>diagnostic-nav failed: {msg}</c>.
    /// <para/>
    /// <b>Threading:</b> UI thread only (DTE/COM).
    /// <para/>
    /// <b>m1 (BP-7):</b> the cache is INSTANCE-scoped (the static R40 state is gone) and
    /// invalidated on build-done / document-saved via an UNHOOKED COM subscription (the m2
    /// pattern — <see cref="HookEvents"/> subscribes, <see cref="Dispose"/> unhooks). The pure
    /// <see cref="ErrorListCacheDecision"/> TTL seam stays.
    /// </summary>
    internal sealed class ErrorListGatherer : IDisposable
    {
        // A3: DTE ErrorItems has NO version counter, so a count-keyed cache is weak (same count,
        // different items after a build). Use a short-TTL cache keyed on (file, severity) with the
        // freshness decision in the pure ErrorListCacheDecision helper — consecutive ],e/[,e/],w/[,w
        // presses reuse the scan instead of re-enumerating the whole Error List per press.
        private const long CacheTtlMs = 2000;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private string? _cacheKey;
        private long _cacheStampMs;
        private List<DiagnosticEntry>? _cache;

        // m1 (BP-7): the build-done/document-saved invalidation subscription (the m2 COM-event
        // lifecycle pattern — held references so the connection points are not GC'd, unhooked in
        // Dispose). Build events live on BuildEvents (via dte.Events.BuildEvents), NOT
        // SolutionEvents (plan correction, 2026-10-06).
        private BuildEvents? _buildEvents;
        private DocumentEvents? _documentEvents;
        private bool _eventsHooked;

        public ErrorListGatherer()
        {
        }

        /// <summary>
        /// Subscribes to <c>SolutionEvents.OnBuildDone</c> + <c>DocumentEvents.DocumentSaved</c>
        /// and invalidates the cache on each (a build or a save can change the Error List). The m2
        /// pattern: idempotent, best-effort (a failure leaves the TTL cache serving), and the
        /// subscription is unhooked in <see cref="Dispose"/>. UI thread only.
        /// </summary>
        public void HookEvents(Func<DTE?> dteFactory)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_eventsHooked)
            {
                return;
            }
            try
            {
                DTE? dte = dteFactory();
                if (dte == null)
                {
                    return;
                }
                _buildEvents = dte.Events.BuildEvents;
                _buildEvents.OnBuildDone += OnBuildDone;
                _documentEvents = dte.Events.DocumentEvents;
                _documentEvents.DocumentSaved += OnDocumentSaved;
                _eventsHooked = true;
            }
            catch
            {
                // best-effort — the TTL cache still serves
            }
        }

        /// <summary>Clears the instance cache (a build-done/document-saved event forces a fresh
        /// gather — m1/BP-7).</summary>
        public void Invalidate()
        {
            _cache = null;
            _cacheKey = null;
            _cacheStampMs = 0;
        }

        public List<DiagnosticEntry> Gather(DTE dte, string filePath, bool severityError)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string key = (severityError ? "e:" : "w:") + filePath;
            if (_cache != null
                && string.Equals(_cacheKey, key, StringComparison.Ordinal)
                && ErrorListCacheDecision.IsFresh(Clock.ElapsedMilliseconds - _cacheStampMs, CacheTtlMs))
            {
                return _cache;
            }

            var entries = new List<DiagnosticEntry>();
            var dte2 = dte as DTE2;
            ErrorItems? items = dte2?.ToolWindows.ErrorList.ErrorItems;
            if (items == null)
            {
                return entries;
            }

            var wanted = severityError
                ? vsBuildErrorLevel.vsBuildErrorLevelHigh
                : vsBuildErrorLevel.vsBuildErrorLevelMedium;

            int count = items.Count;
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    ErrorItem item = items.Item(i);
                    if (item.ErrorLevel != wanted)
                    {
                        continue;
                    }
                    string fileName = item.FileName ?? string.Empty;
                    if (fileName.Length == 0 ||
                        !string.Equals(fileName, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (item.Line <= 0)
                    {
                        continue;
                    }
                    entries.Add(new DiagnosticEntry(fileName, item.Line));
                }
                catch
                {
                    // skip an item that can't be read (same discipline as CodeIssuesFinder)
                }
            }

            var result = entries
                .OrderBy(e => e.Line)
                .GroupBy(e => e.Line)
                .Select(g => g.First())
                .ToList();
            _cache = result;
            _cacheKey = key;
            _cacheStampMs = Clock.ElapsedMilliseconds;
            return result;
        }

        public void Dispose()
        {
            if (_buildEvents != null)
            {
                try { _buildEvents.OnBuildDone -= OnBuildDone; } catch { /* already unhooked */ }
                _buildEvents = null;
            }
            if (_documentEvents != null)
            {
                try { _documentEvents.DocumentSaved -= OnDocumentSaved; } catch { /* already unhooked */ }
                _documentEvents = null;
            }
            _eventsHooked = false;
        }

        private void OnBuildDone(vsBuildScope scope, vsBuildAction action) => Invalidate();

        private void OnDocumentSaved(Document document) => Invalidate();
    }
}
