using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MyExtension.Package
{
    /// <summary>
    /// The recent-files MRU source behind RecentFilesFinder's
    /// <c>Func&lt;IReadOnlyList&lt;string&gt;&gt;</c> seam. Two-tier sourcing:
    ///
    /// <para/>1. REFLECTION PROBE (best-effort): <c>DTE.RecentFiles</c> is NOT in the installed
    /// EnvDTE interop (envdte.dll 17.14.40260 type-forwards to Microsoft.VisualStudio.Interop
    /// with zero Recent types), so the COM property is reached via <see cref="Type.InvokeMember"/>
    /// on the live DTE object, and each item's <c>Name</c>/<c>Path</c> the same way. Any
    /// failure degrades to tier 2.
    /// <para/>2. SESSION MRU (the guaranteed floor): the extension's own MRU, fed by
    /// <c>DocumentEvents.DocumentOpened</c> — knows only files opened since extension load.
    ///
    /// <para/><b>Threading:</b> all entry points run on the UI thread (DTE + the automation
    /// events are main-thread; the finder's base asserts it too).
    /// <b>Failure discipline:</b> the probe failure logs
    /// <c>[Telescope] recent files probe unavailable: {msg}</c> ONCE per gatherer instance
    /// (best-effort — the session MRU floor serves); a gather that throws anyway is caught by
    /// FinderBase and logged as <c>[Telescope] recent files gather failed: {msg}</c>.
    /// </summary>
    internal sealed class RecentFilesGatherer
    {
        private readonly Func<DTE?> _dteFactory;
        private readonly List<string> _sessionMru = new List<string>();
        private DocumentEvents? _documentEvents;   // HOLD the reference: a GC'd COM connection point drops the subscription
        private bool _eventsHooked;
        private bool _probeFailureLogged;          // REV 2: the probe failure logs ONCE per gatherer

        public RecentFilesGatherer(Func<DTE?> dteFactory)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            HookSessionEvents();   // REV 2: EAGER — the session MRU is maintained from construction time
        }

        /// <summary>The seam method: the MRU paths, most-recent-first (unfiltered — the finder owns the policy).</summary>
        public IReadOnlyList<string> Gather()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            HookSessionEvents();   // idempotent RETRY only — a no-op once the eager hookup succeeded
            IReadOnlyList<string> probed;
            try
            {
                probed = ProbeRecentFiles();
            }
            catch (Exception ex)
            {
                // REV 2: best-effort by design, but OBSERVABLE — logged ONCE per gatherer instance
                // (the M18 one-time-fallback precedent). Run 181's silence proved a silent probe is
                // undebuggable.
                probed = Array.Empty<string>();
                if (!_probeFailureLogged)
                {
                    _probeFailureLogged = true;
                    Telescope.Logging.TelescopeLog.Log($"recent files probe unavailable: {ex.Message}");
                }
            }

            // REV 2 MERGE: the probe's MRU first (VS's most-recent-first), then the session floor's
            // additions (files opened since extension load that the probe's MRU lacks), each list's
            // internal order preserved. The finder's Distinct → File.Exists → Take(200) chain
            // (BP-2, unchanged) is the final dedupe/filter/cap policy.
            var merged = new List<string>(probed);
            var seen = new HashSet<string>(probed, StringComparer.OrdinalIgnoreCase);
            foreach (string p in _sessionMru)
            {
                if (seen.Add(p))
                {
                    merged.Add(p);
                }
            }
            return merged;
        }

        private IReadOnlyList<string> ProbeRecentFiles()
        {
            DTE? dte = _dteFactory();
            if (dte == null)
            {
                return Array.Empty<string>();
            }

            // Late-bound IDispatch on the live COM object — the compile-time interop type has
            // no RecentFiles member, but the live object exposes it.
            object recentFiles = dte.GetType().InvokeMember("RecentFiles",
                BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public, null, dte, null);
            if (recentFiles == null || recentFiles is not System.Collections.IEnumerable enumerable)
            {
                return Array.Empty<string>();
            }

            var paths = new List<string>();
            foreach (object item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                string name = InvokeString(item, "Name");
                string path = InvokeString(item, "Path");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                {
                    continue;
                }

                paths.Add(Path.Combine(path, name));
            }

            return paths;   // the COM collection is VS's MRU order (most-recent-first)
        }

        private static string InvokeString(object target, string member)
        {
            object? value = target.GetType().InvokeMember(member,
                BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public, null, target, null);
            return value as string ?? string.Empty;
        }

        private void HookSessionEvents()
        {
            ThreadHelper.ThrowIfNotOnUIThread();   // REV 2: touches dte.Events (a VS API)
            if (_eventsHooked)
            {
                return;
            }

            try
            {
                DTE? dte = _dteFactory();
                if (dte == null)
                {
                    return;
                }

                _documentEvents = dte.Events.DocumentEvents;   // hold the reference (see field doc)
                _documentEvents.DocumentOpened += OnDocumentOpened;
                _eventsHooked = true;
            }
            catch
            {
                // The session MRU is best-effort too; the probe may still serve.
            }
        }

        private void OnDocumentOpened(Document document)
        {
            // Move-to-front dedupe (VS canonicalizes FullName casing; List.Remove is ordinal —
            // acceptable, the same source produces the same string).
            string path = document.FullName;
            _sessionMru.Remove(path);
            _sessionMru.Insert(0, path);
        }
    }
}
