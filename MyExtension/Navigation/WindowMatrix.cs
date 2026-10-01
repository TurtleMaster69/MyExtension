using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyExtension.Navigation
{

    sealed class WindowMatrix
    {

        private List<WindowAdapter> _activeWindows;

        private WindowAdapter _activeWindow;

        private NavigationSettings _settings;

        /// <summary>
        /// initalize windowmatrix and track windows; no filtering. The active window is taken from
        /// <paramref name="currentFrame"/> (the cached frame tracked by <see cref="WindowManager"/>)
        /// when supplied, otherwise it falls back to the DTE's active window.
        /// </summary>
        /// <param name="adapters">the cached frame enumeration from <see cref="WindowManager"/>.</param>
        /// <param name="package"></param>
        /// <param name="currentFrame">the currently focused window frame, or null to use DTE.</param>
        public WindowMatrix(List<WindowAdapter> adapters, AsyncPackage package, IVsWindowFrame? currentFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Initialization is best-effort: never let a navigation-setup failure throw into the
            // keyboard hook. On any error we degrade to an empty matrix (NavigateInDirection
            // becomes a no-op) and log.
            try
            {
                DTE? dteService = MyExtension.Package.VsServices.Dte(package);

                _settings = NavigationSettings.FromSystemDpi();

                // The active window is sourced from WindowManager's cached frame instead of re-deriving
                // it from DTE.ActiveWindow — WindowManager already tracks focus via selection events.
                EnvDTE.Window activeWindow = currentFrame != null
                    ? VsShellUtilities.GetWindowObject(currentFrame)
                    : dteService?.ActiveWindow;

                _activeWindows = WindowAdapter.LinkedTo(activeWindow, adapters).
                    ToList();

                if (activeWindow == null)
                {
                    // No active window to anchor navigation around; degrade to a no-op rather than
                    // throwing (navigation should never crash the hook). _activeWindow stays null;
                    // NavigateInDirection guards against it.
                    _activeWindows = new List<WindowAdapter>();
                    _activeWindow = null!;
                    return;
                }

                // If the active window can't be paired to an adapter (possible when the window list
                // is mid-change), degrade to a no-op rather than throwing.
                _activeWindow = WindowAdapter.FindActive(activeWindow, _activeWindows) ?? null!;
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log(
                    $"{Telescope.Logging.DiagnosticLog.NeoVisual}Window matrix initialization failed: {ex.Message}\n{ex.StackTrace}");
                _activeWindows = new List<WindowAdapter>();
                _activeWindow = null!;
            }
        }

        /// <summary>
        /// swap the current active window for the one found here
        /// or do nothing if none is found.
        /// </summary>
        /// <param name="direction"></param>
        public void NavigateInDirection(Direction direction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Guard order matters: _activeWindow may be null, so test it before dereferencing it.
            if (_activeWindow == null || _activeWindows.Count == 0)
            {
                return;
            }
            try
            {
                // AutoHides() is inside the try so a DTE window disposed/odd-frame exception is
                // caught by the per-navigation catch below instead of escaping to the hook path.
                if (_activeWindow.AutoHides())
                {
                    return;
                }
                List<RectCoordinate> rects = _activeWindows.Select(w => w.Rect).ToList();
                NavigationSnapshot? snapshot = NavigationSnapshot.Capture(rects, _activeWindows.IndexOf(_activeWindow));
                if (snapshot == null) { return; }
                int? target = WindowNavigationEngine.SelectTarget(snapshot.Active, snapshot.Candidates, direction, _settings);
                if (target.HasValue)
                {
                    _activeWindows[target.Value].Activate();
                }
            }
            catch (Exception ex)
            {
                // Navigation is best-effort: never throw into the keyboard hook or pop a modal
                // dialog mid-typing. Log and move on.
                Telescope.Logging.NeoVisualLog.Log(
                    $"{Telescope.Logging.DiagnosticLog.NeoVisual}Window navigation failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

    }
}
