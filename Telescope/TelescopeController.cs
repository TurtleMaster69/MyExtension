using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;

namespace Telescope
{
    /// <summary>
    /// The reusable, extension-facing surface for Telescope. Owns a single overlay instance and
    /// the set of registered finders. The host extension calls <see cref="Open"/> with a finder
    /// name to show the picker, and reads <see cref="IsOpen"/> so its global keyboard hook can
    /// pass keys through while the overlay owns focus.
    ///
    /// <para/>
    /// <b>This is the intended integration point:</b> the VSIX only needs to construct a
    /// <see cref="TelescopeController"/>, register finders, and call <see cref="Open"/> from a
    /// command or key binding. Everything else (fzf filtering, the overlay, key handling) lives
    /// here in the Telescope library.
    /// </summary>
    public sealed class TelescopeController : IDisposable
    {
        private readonly FzfFilter _fzf;
        private readonly Dictionary<string, IFinder> _finders = new(StringComparer.OrdinalIgnoreCase);
        private TelescopeOverlay? _overlay;

        public TelescopeController()
        {
            _fzf = new FzfFilter();
        }

        /// <summary>True while the overlay is open and owns keyboard focus.</summary>
        public bool IsOpen => _overlay is { IsOpen: true };

        /// <summary>Registers a finder under its name so it can be opened by name.</summary>
        public void RegisterFinder(IFinder finder)
        {
            if (finder == null)
            {
                throw new ArgumentNullException(nameof(finder));
            }
            _finders[finder.Name] = finder;
        }

        /// <summary>
        /// Opens the named finder in the overlay. <paramref name="centerRect"/> (optional, in
        /// screen pixels) is the area to center over — pass the VS main-window rect.
        /// <paramref name="ownerHwnd"/> (optional) is the HWND of the host window (the VS main
        /// window) that should own the overlay, so it reliably activates and captures keyboard
        /// focus when shown. Returns false if the finder is unknown. Must run on the UI thread.
        /// </summary>
        public bool Open(string finderName, System.Drawing.Rectangle? centerRect = null, IntPtr ownerHwnd = default)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!_finders.TryGetValue(finderName, out var finder))
            {
                TelescopeLog.Log($"Unknown finder '{finderName}'.");
                return false;
            }

            if (IsOpen)
            {
                return true; // already showing
            }

            // Build a fresh overlay every open: a WPF Window cannot be shown again after Close(),
            // so reusing the cached instance would throw InvalidOperationException on a second
            // open. The abandoned closed window is simply garbage-collected.
            _overlay = new TelescopeOverlay(_fzf);
            _overlay.ShowOverlay(finder, centerRect, ownerHwnd);
            return true;
        }

        /// <summary>Closes the overlay if open.</summary>
        public void Close()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _overlay?.CloseOverlay();
        }

        public void Dispose()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _overlay?.CloseOverlay();
            }
            catch
            {
                // not on UI thread or already closed — nothing more to do
            }
            _overlay = null;
        }
    }
}
