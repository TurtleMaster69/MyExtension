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
    sealed class WindowAdapter
    {
        private IVsWindowFrame _frame;
        private EnvDTE.Window _dte;
        private readonly IVsWindowFrame4? _frame4;

        public WindowAdapter(IVsWindowFrame frame, EnvDTE.Window dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _frame = frame;
            _dte = dte;
            _frame4 = frame as IVsWindowFrame4;
        }

        /// <summary>
        /// The window's on-screen rect, refreshed on every access. The engine snapshots all
        /// rects in a single pass per navigation (see <see cref="NavigationSnapshot"/>), so
        /// this is read once per window per navigation.
        /// </summary>
        public RectCoordinate Rect => RefreshRect();

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
        public static List<WindowAdapter> Enumerate(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IVsUIShell uiShell = UtilityMethods.GetIVsUIShell(package);
            List<WindowAdapter> adapters = new List<WindowAdapter>();

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
        public static WindowAdapter? FindActive(EnvDTE.Window activeWindow, IEnumerable<WindowAdapter> windows)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (activeWindow == null)
            {
                return null;
            }
            return windows?.FirstOrDefault(a => UtilityMethods.CompareWindows(activeWindow, a.DteWindow));
        }

        /// <summary>
        /// Returns the adapters linked to the active window's parent frame.
        /// </summary>
        public static IEnumerable<WindowAdapter> LinkedTo(EnvDTE.Window activeWindow, IEnumerable<WindowAdapter> windows)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            List<EnvDTE.Window> dteWindows = windows.Select(w => w.DteWindow).ToList();
            List<EnvDTE.Window> parentWindows = UtilityMethods.GetLinkedWindowsList(activeWindow.LinkedWindowFrame, dteWindows);
            return windows.Where(a => parentWindows.Any(p => UtilityMethods.CompareWindows(p, a.DteWindow)));
        }

        private static IEnumerable<WindowAdapter> ExtractFrames(IEnumWindowFrames frames)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var frame = new IVsWindowFrame[1];
            int ok = VSConstants.S_OK;
            while (ok == VSConstants.S_OK)
            {
                uint fetched;
                ok = frames.Next(1, frame, out fetched);
                ErrorHandler.ThrowOnFailure(ok);
                if (fetched == 1)
                {
                    yield return new WindowAdapter(frame[0], VsShellUtilities.GetWindowObject(frame[0]));
                }
            }
        }

        private RectCoordinate RefreshRect()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return TryGetScreenRect(_frame4) ?? RectCoordinate.Empty;
        }

        /// <summary>
        /// Reads the frame's on-screen rect via the IVsWindowFrame4 interface, or null when the
        /// frame does not conform (the old cast path threw InvalidCastException).
        /// </summary>
        internal static RectCoordinate? TryGetScreenRect(IVsWindowFrame4? frame4)
        {
            if (frame4 == null)
            {
                return null;
            }
            frame4.GetWindowScreenRect(out int left, out int top, out int width, out int height);
            return new RectCoordinate(left, top, width, height);
        }
    }
}
