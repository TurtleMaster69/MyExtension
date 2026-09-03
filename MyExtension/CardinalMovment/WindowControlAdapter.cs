using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CardinalNavigation
{
    class WindowControlAdapter
    {

        private IVsFrameView m_genericWindow;

        private Window m_dteWindow;

        public Window internalWindow { get => m_dteWindow; }

        private int m_screenLeft, m_screenTop, m_screenWidth, m_screenHeight;

        public RectCoordinate coordinates
        {
            get
            {
                return this.GetScreenDisplayCoordinates();
            }
        }

        /// <summary>
        /// constructor binds an IVsWindowFrame to a Dte.Window
        /// </summary>
        /// <param name="genericWindow"></param>
        /// <param name="dteWindow"></param>
        WindowControlAdapter(IVsFrameView genericWindow, Window dteWindow)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            if (genericWindow == null || dteWindow == null)
            {
                ErrorHandler.ThrowOnFailure(VSConstants.E_FAIL);
            }

            m_genericWindow = genericWindow;
            m_dteWindow = dteWindow;

            GetWindowScreenCoordinates();
        }

        /// <summary>
        /// Returns the dimensions of a window that's being rendered on the screen.
        /// </summary>
        private void GetWindowScreenCoordinates()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            m_genericWindow.GetWindowScreenRect(out m_screenLeft, out m_screenTop, out m_screenWidth, out m_screenHeight);
        }


        /// <summary>
        /// returns the active window from an enumerable of WindowControlAdapters
        /// </summary>
        /// <param name="activeWindow"></param>
        /// <param name="windows"></param>
        /// <returns></returns>
        public static WindowControlAdapter? GetActiveWindowControlAdapter(EnvDTE.Window activeWindow, IEnumerable<WindowControlAdapter> windows)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (activeWindow == null)
            {
                return null;
            }
            try
            {
                return windows?.Where((eachWindow) =>
                {
                    Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
                    return UtilityMethods.CompareWindows(activeWindow, eachWindow.internalWindow);
                }).First();
            }
            catch (Exception ex)
            {
                if (ex is System.InvalidOperationException)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[NeoVisual] Unable to pair active window '{activeWindow.Caption}': {ex.Message}");
                    return null;
                }
                throw;
            }

        }


        /// <summary>
        ///  returns an ienumerable to the children of our selected window's parent window. 
        /// </summary>
        /// <param name="genericWindows"></param>
        /// <param name="dteWindows"></param>
        /// <param name="activeWindow"></param>
        /// <returns></returns>
        public static IEnumerable<WindowControlAdapter> GetLinkedWindowControlAdapters(
            List<IVsFrameView> genericWindows,
            List<Window> dteWindows,
            EnvDTE.Window activeWindow
            )
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                List<WindowControlAdapter> allWindows = GetWindowControlAdapters(genericWindows, dteWindows).ToList();

                List<EnvDTE.Window> parentWindows = UtilityMethods.GetLinkedWindowsList(activeWindow.LinkedWindowFrame, dteWindows);

                return allWindows.Where((eachActiveWindow) =>
                {
                    var internalWindow = eachActiveWindow.internalWindow;
                    foreach (var parentWindow in parentWindows)
                    {
                        if (UtilityMethods.CompareWindows(parentWindow, internalWindow))
                        {
                            return true;
                        }
                    }
                    return false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NeoVisual] Window linking failed (dte:{dteWindows?.Count}, frames:{genericWindows?.Count}): {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Returns an ienum to this class, bound to lists from the DTE and IVs shell api.
        /// </summary>
        /// <param name="genericWindows"></param>
        /// <param name="dteWindows"></param>
        /// <returns></returns>
        public static IEnumerable<WindowControlAdapter> GetWindowControlAdapters(List<IVsFrameView> genericWindows, List<Window> dteWindows)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // instead of pairing, we'll take straight from IVsUi. It seems more inclusive.

            foreach (var genericWindow in genericWindows)
            {
                yield return new WindowControlAdapter(genericWindow, VsShellUtilities.GetWindowObject(genericWindow));
            }
        }


        /// <summary>
        /// returns the absolute screen position and dimensions of this window
        /// </summary>
        /// <returns></returns>
        public RectCoordinate GetScreenDisplayCoordinates()
        {
            return new RectCoordinate(m_screenLeft, m_screenTop, m_screenWidth, m_screenHeight);
        }


        /// <summary>
        /// activates the given window
        /// </summary>
        public void ActivateWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            m_dteWindow.Activate();
        }

        /// <summary>
        /// returns whether the window autohides
        /// </summary>
        /// <returns></returns>
        public bool AutoHides()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return m_dteWindow.AutoHides;
        }


    }
}

