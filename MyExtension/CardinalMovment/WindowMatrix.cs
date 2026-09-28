using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CardinalNavigation
{

    class WindowMatrix
    {

        private List<WindowAdapter> m_ActiveWindows;

        private WindowAdapter m_activeWindow;

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
                this.CheckDte(package);

                DTE dteService = MyExtension.VsServices.Dte(package);

                _settings = NavigationSettings.FromSystemDpi();

                // The active window is sourced from WindowManager's cached frame instead of re-deriving
                // it from DTE.ActiveWindow — WindowManager already tracks focus via selection events.
                EnvDTE.Window activeWindow = currentFrame != null
                    ? VsShellUtilities.GetWindowObject(currentFrame)
                    : dteService.ActiveWindow;

                m_ActiveWindows = WindowAdapter.LinkedTo(activeWindow, adapters).
                    ToList();

                if (activeWindow == null)
                {
                    // No active window to anchor navigation around; degrade to a no-op rather than
                    // throwing (navigation should never crash the hook). m_activeWindow stays null;
                    // NavigateInDirection guards against it.
                    m_ActiveWindows = new List<WindowAdapter>();
                    m_activeWindow = null!;
                    return;
                }

                // If the active window can't be paired to an adapter (possible when the window list
                // is mid-change), degrade to a no-op rather than throwing.
                m_activeWindow = WindowAdapter.FindActive(activeWindow, m_ActiveWindows) ?? null!;
            }
            catch (Exception ex)
            {
                Telescope.NeoVisualLog.Debug(
                    $"{Telescope.DiagnosticLog.NeoVisual}Window matrix initialization failed: {ex.Message}\n{ex.StackTrace}");
                m_ActiveWindows = new List<WindowAdapter>();
                m_activeWindow = null!;
            }
        }

        private void CheckDte(AsyncPackage package)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                DTE myDTE = MyExtension.VsServices.Dte(package);
            }
            catch (Exception ex)
            {
                Telescope.NeoVisualLog.Debug(
                    $"{Telescope.DiagnosticLog.NeoVisual}Unable to get DTE for window navigation: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// swap the current active window for the one found here
        /// or do nothing if none is found.
        /// </summary>
        /// <param name="direction"></param>
        public void NavigateInDirection(char direction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Guard order matters: m_activeWindow may be null, so test it before dereferencing it.
            if (m_activeWindow == null || m_ActiveWindows.Count == 0 || m_activeWindow.AutoHides())
            {
                return;
            }
            try
            {
                Direction dir = ToDirection(direction);
                RectCoordinate active = m_activeWindow.Rect;
                List<RectCoordinate> candidates = m_ActiveWindows.Select(w => w.Rect).ToList();
                int? target = WindowNavigationEngine.SelectTarget(active, candidates, dir, _settings);
                if (target.HasValue)
                {
                    m_ActiveWindows[target.Value].Activate();
                }
            }
            catch (Exception ex)
            {
                // Navigation is best-effort: never throw into the keyboard hook or pop a modal
                // dialog mid-typing. Log and move on.
                Telescope.NeoVisualLog.Debug(
                    $"{Telescope.DiagnosticLog.NeoVisual}Window navigation failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static Direction ToDirection(char direction)
        {
            if (direction == CardinalNavigationConstants.UP) return Direction.Up;
            if (direction == CardinalNavigationConstants.DOWN) return Direction.Down;
            if (direction == CardinalNavigationConstants.LEFT) return Direction.Left;
            if (direction == CardinalNavigationConstants.RIGHT) return Direction.Right;
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

    }
}
