
using System;
using System.Collections.Generic;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CardinalNavigation
{
    class UtilityMethods
    {
        /// <summary>
        /// root automation model
        /// </summary>
        /// <param name="package"></param>
        /// <returns></returns>
        public static DTE GetDTE(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            System.IServiceProvider serviceProvider = package as System.IServiceProvider;
            return (DTE)serviceProvider.GetService(typeof(DTE));
        }


        /// <summary>
        /// more involved window functionality than provided by the DTE 
        /// </summary>
        /// <param name="package"></param>
        /// <returns></returns>
        public static IVsUIShell GetIVsUIShell(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            System.IServiceProvider serviceProvider = package as System.IServiceProvider;
            return (IVsUIShell)serviceProvider.GetService(typeof(SVsUIShell));
        }


        /// <summary>
        /// returns a list to LinkedWindows
        /// </summary>
        /// <param name="windows"></param>
        /// <returns></returns>
        public static List<EnvDTE.Window> GetWindowsList(EnvDTE.Windows windows)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            List<EnvDTE.Window> windowsList = new List<Window>();
            foreach (var window in windows)
            {
                windowsList.Add((Window)window);
            }
            return windowsList;
        }

        /// <summary>
        /// converts enumerable to list--needed due to lack of full enumerator support.
        /// </summary>
        /// <param name="windows"></param>
        /// <returns></returns>
        public static List<EnvDTE.Window> GetLinkedWindowsList(EnvDTE.Window parentWindow, List<Window> allWindows)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // note: not all windows linked to a parent are in parent.LinkedWindows; we'll pair
            //       them manually.
            List<EnvDTE.Window> linkedWindows = new List<EnvDTE.Window>();

            if (parentWindow == null)
            {
                // No parent to anchor linked windows around; return empty so navigation degrades
                // to a no-op instead of throwing into the keyboard hook.
                System.Diagnostics.Debug.WriteLine("[NeoVisual] No parent window for active window; skipping window linking.");
                return linkedWindows;
            }

            foreach (var window in allWindows)
            {
                var eachWindowParentWindow = window?.LinkedWindowFrame;

                if (UtilityMethods.CompareWindows(eachWindowParentWindow, parentWindow))
                {
                    linkedWindows.Add(window);
                }
            }

            return linkedWindows;
        }


        /// <summary>
        /// special comparison function for EnvDTE.Window
        /// this is needed because some windows (e.g. properties) seem not to
        /// compare against eachother correctly from the IVsShell interface and
        /// the DTE.
        /// </summary>
        /// <param name="lhs"></param>
        /// <param name="rhs"></param>
        /// <returns></returns>
        public static bool CompareWindows(EnvDTE.Window lhs, EnvDTE.Window rhs)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (lhs == rhs)
            {
                return true;
            }

            if (lhs == null && rhs != null || lhs != null && rhs == null)
            {
                return false;
            }

            // properties window props differ when from activeWindow; if this is fixed, lhs == rhs should suffice. 
            if (lhs.Caption == rhs.Caption &&
                (
                (lhs.Type == vsWindowType.vsWindowTypeToolWindow && rhs.Type == vsWindowType.vsWindowTypeProperties) ||
                (lhs.Type == vsWindowType.vsWindowTypeProperties && rhs.Type == vsWindowType.vsWindowTypeToolWindow)
                )
                )
            {
                return true;
            }

            return false;

        }

    }
}

