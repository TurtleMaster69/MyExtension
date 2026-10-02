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
        private EnvDTE.Window _dte;
        private readonly IVsWindowFrame4? _frame4;
        private bool _loggedEmptyRect;

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
            if (_dte == null) { return; }
            _dte.Activate();
        }

        public bool AutoHides()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _dte != null && _dte.AutoHides;
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

            IEnumWindowFrames toolFramesEnum;
            ErrorHandler.ThrowOnFailure(uiShell.GetToolWindowEnum(out toolFramesEnum));
            adapters.AddRange(ExtractFrames(toolFramesEnum));

            IEnumWindowFrames documentFramesEnum;
            ErrorHandler.ThrowOnFailure(uiShell.GetDocumentWindowEnum(out documentFramesEnum));
            adapters.AddRange(ExtractFrames(documentFramesEnum));

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

            // Precompute a key set over the parent windows (m13) instead of the O(n·m)
            // CompareWindows COM reads. CompareWindows matches a window when it is reference-equal
            // to a parent, or shares a caption with a parent of the opposite Properties/ToolWindow
            // type (the Properties-window quirk), so the set is keyed by caption+type and the
            // opposite-type lookup is applied per candidate.
            HashSet<EnvDTE.Window> parentRefs = new HashSet<EnvDTE.Window>(parentWindows);
            HashSet<string> parentKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (EnvDTE.Window parent in parentWindows)
            {
                parentKeys.Add(WindowKey(parent));
            }

            return windows.Where(a =>
            {
                EnvDTE.Window w = a.DteWindow;
                if (parentRefs.Contains(w))
                {
                    return true;
                }
                if (w.Type == vsWindowType.vsWindowTypeToolWindow)
                {
                    return parentKeys.Contains(WindowKey(w.Caption, vsWindowType.vsWindowTypeProperties));
                }
                if (w.Type == vsWindowType.vsWindowTypeProperties)
                {
                    return parentKeys.Contains(WindowKey(w.Caption, vsWindowType.vsWindowTypeToolWindow));
                }
                return false;
            });
        }

        private static string WindowKey(EnvDTE.Window window) => WindowKey(window.Caption, window.Type);

        private static string WindowKey(string caption, vsWindowType type) => caption + "\u0000" + (int)type;

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
                    yield return new WindowFrameAdapter(frame[0], VsShellUtilities.GetWindowObject(frame[0]));
                }
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
            if (frame4 == null)
            {
                return null;
            }
            frame4.GetWindowScreenRect(out int left, out int top, out int width, out int height);
            return new WindowRect(left, top, width, height);
        }
    }
}
