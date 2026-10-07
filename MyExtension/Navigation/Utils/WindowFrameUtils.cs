
using System;
using System.Collections.Generic;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace MyExtension.Navigation
{
    static class WindowFrameUtils
    {
        /// <summary>
        /// more involved window functionality than provided by the DTE 
        /// </summary>
        /// <param name="package"></param>
        /// <returns></returns>
        public static IVsUIShell? GetIVsUIShell(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            System.IServiceProvider serviceProvider = package as System.IServiceProvider;
            if (serviceProvider == null)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}IVsUIShell unavailable: package is not an IServiceProvider.");
                return null;
            }
            IVsUIShell uiShell = serviceProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell == null)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}IVsUIShell unavailable: SVsUIShell service returned null.");
                return null;
            }
            return uiShell;
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
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}No parent window for active window; skipping window linking.");
                return linkedWindows;
            }

            foreach (var window in allWindows)
            {
                // M6 (BP-12): a stale/disconnected RCW must not propagate through LinkedTo ->
                // BuildActiveWindows -> the ctor catch and degrade ALL navigation to a no-op. Each
                // window's LinkedWindowFrame read + the CompareWindows Caption/Type reads are
                // isolated — a throwing window is skipped, the rest survive.
                try
                {
                    var eachWindowParentWindow = window?.LinkedWindowFrame;

                    if (WindowFrameUtils.CompareWindows(eachWindowParentWindow, parentWindow))
                    {
                        linkedWindows.Add(window);
                    }
                }
                catch
                {
                    // stale/disconnected window — skip it
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
            if (lhs.Caption == rhs.Caption && MatchesPropertiesQuirk(lhs.Type, rhs.Type))
            {
                return true;
            }

            return false;

        }

        /// <summary>
        /// N7/N14: the Properties-window quirk — a ToolWindow and a Properties window with the same
        /// caption are treated as the same window (their IVsShell/DTE identities differ). Single
        /// source so <see cref="CompareWindows"/> and <see cref="WindowFrameAdapter.LinkedTo"/>
        /// cannot diverge. m11 (BP-14): the unused <c>caption</c> parameter is dropped.
        /// </summary>
        public static bool MatchesPropertiesQuirk(vsWindowType type, vsWindowType otherType)
        {
            return (type == vsWindowType.vsWindowTypeToolWindow && otherType == vsWindowType.vsWindowTypeProperties) ||
                   (type == vsWindowType.vsWindowTypeProperties && otherType == vsWindowType.vsWindowTypeToolWindow);
        }

    }
}

