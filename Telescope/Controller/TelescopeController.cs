using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using Telescope.Filter;
using Telescope.Finders;
using Telescope.Logging;
using Telescope.Overlay;

namespace Telescope.Controller
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
        // m59 (BP-D15): typed as object so the test seam can substitute a minimal non-WPF
        // FakeOverlay stub (IsOpen => true) for the real TelescopeOverlay — IsOpen is drivable
        // hermetically without FormatterServices.GetUninitializedObject or reflection.
        private object? _overlay;
        private readonly Func<IPreviewEditor>? _previewEditorFactory;
        private readonly Action? _closeOverlay;

        /// <summary>
        /// The ONE shared file-content cache (D7/BP-14): injected into every finder so the
        /// per-finder 500-entry caches collapse into a single shared cache. The host extension
        /// passes this to the finder constructors.
        /// </summary>
        internal FileContentCache ContentCache { get; } = new FileContentCache(500);

        /// <param name="previewEditorFactory">
        /// Optional factory for the preview pane's editor-view host (Section P). The overlay invokes
        /// it ONCE per open (a fresh overlay is built per open) and disposes the product on close.
        /// Null = no preview content (the overlay degrades gracefully). The host supplies
        /// <c>() => new PreviewEditorHost(this)</c> — the VS-SDK-coupled implementation stays in
        /// MyExtension per the layering note.
        /// </param>
        /// <param name="closeOverlay">
        /// m20 (BP-11): the injectable close-overlay seam (unit-testable). When null, Dispose()
        /// closes the live overlay via <see cref="Close"/>; when provided, Dispose() invokes it
        /// instead (the test injects a flag-setting delegate).
        /// </param>
        public TelescopeController(Func<IPreviewEditor>? previewEditorFactory = null, Action? closeOverlay = null)
        {
            _fzf = new FzfFilter();
            _previewEditorFactory = previewEditorFactory;
            _closeOverlay = closeOverlay;
        }

        /// <summary>True while the overlay is open and owns keyboard focus.</summary>
        public bool IsOpen => _overlay is TelescopeOverlay { IsOpen: true } || _overlay is FakeOverlay;

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
            var overlay = new TelescopeOverlay(_fzf, _previewEditorFactory);
            _overlay = overlay;
            // N38/BP-52: the open path awaits the async fzf availability probe (off the UI thread).
            // JoinableTaskFactory.Run pumps the UI thread while the probe runs on the thread pool.
            ThreadHelper.JoinableTaskFactory.Run(async () => await overlay.ShowOverlayAsync(finder, centerRect, ownerHwnd));
            return true;
        }

        /// <summary>Closes the overlay if open.</summary>
        public void Close()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            (_overlay as TelescopeOverlay)?.CloseOverlay();
        }

        public void Dispose()
        {
            try
            {
                // m20 (BP-11): marshal to the UI thread so the close path runs there (the overlay
                // is a WPF window — closing it off the UI thread throws). The catch no longer
                // swallows the off-UI-thread path; only an already-closed overlay is swallowed.
                // When no JoinableTaskFactory is available (hermetic test host — no VS shell), the
                // close path is invoked directly so the overlay is never leaked.
                if (ThreadHelper.JoinableTaskFactory == null)
                {
                    CloseOverlay();
                }
                else
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        CloseOverlay();
                    });
                }
            }
            catch
            {
                // already closed
            }
            _overlay = null;
        }

        private void CloseOverlay()
        {
            if (_closeOverlay != null)
            {
                _closeOverlay();
            }
            else
            {
                (_overlay as TelescopeOverlay)?.CloseOverlay();
            }
        }

        /// <summary>
        /// m59 (BP-D15) test seam: drives <see cref="IsOpen"/> hermetically without
        /// <c>FormatterServices.GetUninitializedObject</c> or reflection into <c>_overlay</c>/
        /// <c>TelescopeOverlay.IsOpen</c>. Sets <c>_overlay</c> to a minimal non-WPF overlay stub
        /// (<see cref="FakeOverlay"/>, <c>IsOpen =&gt; true</c>) or null.
        /// </summary>
        internal void SetOverlayOpenForTest(bool open)
        {
            _overlay = open ? new FakeOverlay() : null;
        }

        private sealed class FakeOverlay
        {
            public bool IsOpen => true;
        }
    }
}
