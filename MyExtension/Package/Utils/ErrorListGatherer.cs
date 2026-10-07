using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using MyExtension.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Telescope.Finders;

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
        // SolutionEvents (plan correction, 2026-10-06). m8 (BP-9): document-opened +
        // window-activated are added so a build/save/open/activate that changes the Error List
        // never serves a stale TTL cache.
        private BuildEvents? _buildEvents;
        private DocumentEvents? _documentEvents;
        private WindowEvents? _windowEvents;
        private bool _eventsHooked;

        public ErrorListGatherer()
        {
        }

        /// <summary>
        /// Subscribes to <c>SolutionEvents.OnBuildDone</c> + <c>DocumentEvents.DocumentSaved</c>
        /// (m8/BP-9: + <c>DocumentEvents.DocumentOpened</c> + <c>WindowEvents.WindowActivated</c>)
        /// and invalidates the cache on each (a build/save/open/activate can change the Error
        /// List). The m2 pattern: idempotent, best-effort (a failure leaves the TTL cache serving),
        /// and the subscription is unhooked in <see cref="Dispose"/>. UI thread only.
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
                _documentEvents.DocumentOpened += OnDocumentOpened;
                _windowEvents = dte.Events.WindowEvents;
                _windowEvents.WindowActivated += OnWindowActivated;
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

        /// <summary>
        /// m60 (BP-D13): test-only seam — the instance cache (null when empty). No production
        /// behavior change.
        /// </summary>
        internal List<DiagnosticEntry>? CacheForTest
        {
            get => _cache;
            set => _cache = value;
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

            // m33 (BP-24): the shared per-item walk (per-item try/catch — a throwing item is
            // skipped, the rest survive).
            ErrorItemsWalker.ForEach(items, item =>
            {
                if (item.ErrorLevel != wanted)
                {
                    return;
                }
                string fileName = item.FileName ?? string.Empty;
                if (fileName.Length == 0 ||
                    !string.Equals(fileName, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (item.Line <= 0)
                {
                    return;
                }
                entries.Add(new DiagnosticEntry(fileName, item.Line));
            });

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
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_buildEvents != null)
            {
                try { _buildEvents.OnBuildDone -= OnBuildDone; } catch { /* already unhooked */ }
                _buildEvents = null;
            }
            if (_documentEvents != null)
            {
                try { _documentEvents.DocumentSaved -= OnDocumentSaved; } catch { /* already unhooked */ }
                try { _documentEvents.DocumentOpened -= OnDocumentOpened; } catch { /* already unhooked */ }
                _documentEvents = null;
            }
            if (_windowEvents != null)
            {
                try { _windowEvents.WindowActivated -= OnWindowActivated; } catch { /* already unhooked */ }
                _windowEvents = null;
            }
            _eventsHooked = false;
        }

        private void OnBuildDone(vsBuildScope scope, vsBuildAction action)
            => InvalidateIf(ErrorListCacheDecision.ShouldInvalidateOnEvent("build-done"));

        private void OnDocumentSaved(Document document)
            => InvalidateIf(ErrorListCacheDecision.ShouldInvalidateOnEvent("document-saved"));

        private void OnDocumentOpened(Document document)
            => InvalidateIf(ErrorListCacheDecision.ShouldInvalidateOnEvent("document-opened"));

        private void OnWindowActivated(Window gotFocus, Window lostFocus)
            => InvalidateIf(ErrorListCacheDecision.ShouldInvalidateOnEvent("window-activated"));

        private void InvalidateIf(bool shouldInvalidate)
        {
            if (shouldInvalidate)
            {
                Invalidate();
            }
        }
    }
}
