using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Telescope;

namespace MyExtension
{
    /// <summary>
    /// The single open-finder entry point: resolves DTE + the main-window rect and opens the
    /// named Telescope finder. Replaces the six near-identical open-finder methods.
    /// </summary>
    internal sealed class TelescopeLauncher
    {
        private readonly AsyncPackage _package;
        private readonly TelescopeController _telescope;

        public TelescopeLauncher(AsyncPackage package, TelescopeController telescope)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _telescope = telescope ?? throw new ArgumentNullException(nameof(telescope));
        }

        /// <summary>Maps the built-in telescope action names to the finder names they open.</summary>
        internal static readonly IReadOnlyDictionary<string, string> FinderNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["telescope"] = "Files",
                ["telescope-issues"] = "Issues",
                ["telescope-references"] = "References",
                ["telescope-implementation"] = "Implementation",
                ["telescope-grep"] = "Grep",
            };

        public void Open(string finderName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = VsServices.Dte(_package);
                var centerRect = NativeMethods.GetWindowRect(dte.MainWindow.HWnd);
                _telescope.Open(finderName, centerRect, dte.MainWindow.HWnd);
            }
            catch (Exception ex)
            {
                NeoVisualLog.Debug($"{Telescope.DiagnosticLog.NeoVisual}Failed to open Telescope {finderName}: {ex.Message}");
            }
        }
    }
}
