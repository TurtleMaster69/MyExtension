using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyExtension.Navigation
{
    /// <summary>
    /// Pairs an IVsWindowFrame (IVs shell) with its EnvDTE.Window (DTE automation) and
    /// exposes the on-screen rect lazily. Consolidates IVsFrameView + WindowControlAdapter
    /// + IVsUIWindowFrameExtractor into one type.
    /// </summary>
    sealed class WindowFrameAdapter
    {
        private readonly EnvDTE.Window _dte;
        private readonly IVsWindowFrame4? _frame4;
        // N72: session-scoped so the n19 diagnostic is logged once per session, not once per
        // adapter instance (adapters are recreated per focus change).
        private static bool _loggedEmptyRect;

        public WindowFrameAdapter(IVsWindowFrame frame, EnvDTE.Window dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = dte;
            _frame4 = frame as IVsWindowFrame4;
        }

        /// <summary>
        /// The window's on-screen rect, refreshed on every access. The engine snapshots all
        /// rects in a single pass per navigation (see <see cref="NavigationSnapshot"/>), so
        /// this is read once per window per navigation.
        /// </summary>
        public WindowRect Rect => RefreshRect();

        public EnvDTE.Window DteWindow => _dte;

        public void Activate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte.Activate();
        }

        public bool AutoHides()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _dte.AutoHides;
        }

        /// <summary>
        /// Enumerates both tool and document window frames from IVsUIShell, pairing each
        /// with its DTE window object.
        /// </summary>
        public static List<WindowFrameAdapter> Enumerate(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IVsUIShell? uiShell = WindowFrameUtils.GetIVsUIShell(package);
            if (uiShell == null)
            {
                return new List<WindowFrameAdapter>();
            }
            List<WindowFrameAdapter> adapters = new List<WindowFrameAdapter>();

            // R4: per-enumeration fault isolation — one stale frame must not abort the whole
            // enumeration (which would escape into the hook path). Each enumeration is wrapped so
            // a failure degrades to the frames collected so far, mirroring the null-uiShell path.
            try
            {
                IEnumWindowFrames toolFramesEnum;
                ErrorHandler.ThrowOnFailure(uiShell.GetToolWindowEnum(out toolFramesEnum));
                adapters.AddRange(ExtractFrames(toolFramesEnum));
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}window frame enumeration failed: {ex.Message}");
            }

            try
            {
                IEnumWindowFrames documentFramesEnum;
                ErrorHandler.ThrowOnFailure(uiShell.GetDocumentWindowEnum(out documentFramesEnum));
                adapters.AddRange(ExtractFrames(documentFramesEnum));
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}window frame enumeration failed: {ex.Message}");
            }

            return adapters;
        }

        /// <summary>
        /// Returns the adapter whose DTE window matches the active window, or null.
        /// </summary>
        public static WindowFrameAdapter? FindActive(EnvDTE.Window activeWindow, IEnumerable<WindowFrameAdapter> windows)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (activeWindow == null)
            {
                return null;
            }
            return windows?.FirstOrDefault(a => WindowFrameUtils.CompareWindows(activeWindow, a.DteWindow));
        }

        /// <summary>
        /// Returns the adapters linked to the active window's parent frame.
        /// </summary>
        public static IEnumerable<WindowFrameAdapter> LinkedTo(EnvDTE.Window activeWindow, IEnumerable<WindowFrameAdapter> windows)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            List<EnvDTE.Window> dteWindows = windows.Select(w => w.DteWindow).ToList();
            List<EnvDTE.Window> parentWindows = WindowFrameUtils.GetLinkedWindowsList(activeWindow.LinkedWindowFrame, dteWindows);

            // N7/N14: single-source the window comparison (including the Properties-window quirk)
            // in WindowFrameUtils.CompareWindows — no separate key-set strategy that can diverge.
            // This runs once per window-set change (BuildActiveWindows caches the result), not per
            // keystroke.
            return windows.Where(a => parentWindows.Any(p => WindowFrameUtils.CompareWindows(p, a.DteWindow)));
        }

        private static IEnumerable<WindowFrameAdapter> ExtractFrames(IEnumWindowFrames frames)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ExtractFramesCore(frames);
        }

        private static IEnumerable<WindowFrameAdapter> ExtractFramesCore(IEnumWindowFrames frames)
        {
            var frame = new IVsWindowFrame[1];
            int ok = VSConstants.S_OK;
            while (ok == VSConstants.S_OK)
            {
                uint fetched;
                ok = frames.Next(1, frame, out fetched);
                ErrorHandler.ThrowOnFailure(ok);
                if (fetched == 1)
                {
                    // R4: per-frame fault isolation — one stale frame's GetWindowObject must not
                    // abort the whole enumeration; skip the bad frame (log once).
                    WindowFrameAdapter? adapter = TryCreateAdapter(frame[0]);
                    if (adapter != null)
                    {
                        yield return adapter;
                    }
                }
            }
        }

        private static WindowFrameAdapter? TryCreateAdapter(IVsWindowFrame frame)
        {
            try
            {
                return new WindowFrameAdapter(frame, VsShellUtilities.GetWindowObject(frame));
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}window frame skipped: {ex.Message}");
                return null;
            }
        }

        private WindowRect RefreshRect()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            WindowRect? rect = TryGetScreenRect(_frame4);
            if (rect == null)
            {
                if (!_loggedEmptyRect)
                {
                    _loggedEmptyRect = true;
                    Telescope.Logging.NeoVisualLog.Log(
                        $"{Telescope.Logging.DiagnosticLog.NeoVisual}window rect unavailable; using empty rect");
                }
                return WindowRect.Empty;
            }
            return rect.Value;
        }

        /// <summary>
        /// Reads the frame's on-screen rect via the IVsWindowFrame4 interface, or null when the
        /// frame does not conform (the old cast path threw InvalidCastException).
        /// </summary>
        internal static WindowRect? TryGetScreenRect(IVsWindowFrame4? frame4)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (frame4 == null)
            {
                return null;
            }
            // N5: a failed GetWindowScreenRect must degrade to null so RefreshRect emits the n19
            // diagnostic and returns WindowRect.Empty (today the bool result was discarded, yielding
            // a silent empty/garbage rect without the diagnostic).
            bool ok = frame4.GetWindowScreenRect(out int left, out int top, out int width, out int height);
            if (!ok)
            {
                return null;
            }
            return new WindowRect(left, top, width, height);
        }
    }
}
