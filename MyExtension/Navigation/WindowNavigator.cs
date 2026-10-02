using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyExtension.Navigation
{

    sealed class WindowNavigator
    {

        private List<WindowFrameAdapter> _activeWindows;

        private WindowFrameAdapter _activeWindow;

        private NavigationSettings _settings;

        // R32: the linked-window filter is cached across navigations, keyed by the adapters list
        // reference. WindowManager re-enumerates the adapters on focus change (a new list), so the
        // cache is invalidated exactly when the window set can change; while the adapters are stable
        // the linked filter is stable (navigation keeps the active window within the same linked
        // group), so the O(n) COM LinkedWindowFrame/Type/Caption reads in LinkedTo run once per
        // window-set change instead of per keystroke.
        private static List<WindowFrameAdapter>? _cachedLinked;
        private static IReadOnlyList<WindowFrameAdapter>? _cachedLinkedSource;
        // N11: the cache is also keyed on the active window — the adapters list reference alone is
        // not enough (the active window can change while the list reference is unchanged).
        private static EnvDTE.Window? _cachedLinkedActive;

        // N15: the active window's index in _activeWindows, resolved once at construction instead
        // of an O(n) IndexOf per navigation.
        private int _activeIndex = -1;

        /// <summary>
        /// Initialize the navigation window set and track the active window; no filtering. The active window is taken from
        /// <paramref name="currentFrame"/> (the cached frame tracked by <see cref="WindowManager"/>)
        /// when supplied, otherwise it falls back to the DTE's active window.
        /// </summary>
        /// <param name="adapters">the cached frame enumeration from <see cref="WindowManager"/>.</param>
        /// <param name="package"></param>
        /// <param name="currentFrame">the currently focused window frame, or null to use DTE.</param>
        public WindowNavigator(List<WindowFrameAdapter> adapters, AsyncPackage package, IVsWindowFrame? currentFrame)
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
                EnvDTE.Window? activeWindow = currentFrame != null
                    ? VsShellUtilities.GetWindowObject(currentFrame)
                    : dteService?.ActiveWindow;

                // BuildActiveWindows null-checks the active window BEFORE linking (m44), so a null
                // active window degrades to an empty list instead of dereferencing it in LinkedTo.
                _activeWindows = BuildActiveWindows(activeWindow, adapters);

                if (activeWindow == null)
                {
                    // No active window to anchor navigation around; degrade to a no-op rather than
                    // throwing (navigation should never crash the hook). _activeWindow stays null;
                    // NavigateInDirection guards against it.
                    _activeWindow = null!;
                    return;
                }

                // If the active window can't be paired to an adapter (possible when the window list
                // is mid-change), degrade to a no-op rather than throwing.
                _activeWindow = WindowFrameAdapter.FindActive(activeWindow, _activeWindows) ?? null!;
                // N15: resolve the active index once here (O(n)) instead of per navigation.
                _activeIndex = _activeWindow == null ? -1 : _activeWindows.IndexOf(_activeWindow);
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log(
                    $"{Telescope.Logging.DiagnosticLog.NeoVisual}Window navigator initialization failed: {ex.Message}\n{ex.StackTrace}");
                _activeWindows = new List<WindowFrameAdapter>();
                _activeWindow = null!;
                _activeIndex = -1;
            }
        }

        /// <summary>
        /// Returns the adapters linked to the active window, or an empty list when the active
        /// window is null. The null check runs BEFORE <see cref="WindowFrameAdapter.LinkedTo"/> so a
        /// null active window degrades to a no-op instead of being dereferenced (m44).
        /// </summary>
        public static List<WindowFrameAdapter> BuildActiveWindows(EnvDTE.Window? active, IReadOnlyList<WindowFrameAdapter> adapters)
        {
            if (active == null)
            {
                return new List<WindowFrameAdapter>();
            }
            if (ReferenceEquals(_cachedLinkedSource, adapters) &&
                ReferenceEquals(_cachedLinkedActive, active) &&
                _cachedLinked != null)
            {
                return _cachedLinked;
            }
            var linked = WindowFrameAdapter.LinkedTo(active, adapters).ToList();
            _cachedLinked = linked;
            _cachedLinkedSource = adapters;
            _cachedLinkedActive = active;
            return linked;
        }

        /// <summary>
        /// swap the current active window for the one found here
        /// or do nothing if none is found.
        /// </summary>
        /// <param name="direction"></param>
        public NavigationOutcome NavigateInDirection(Direction direction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Guard order matters: _activeWindow may be null, so test it before dereferencing it.
            if (_activeWindow == null || _activeWindows.Count == 0)
            {
                return NavigationOutcome.NoOp("no active window");
            }
            try
            {
                // AutoHides() is inside the try so a DTE window disposed/odd-frame exception is
                // caught by the per-navigation catch below instead of escaping to the hook path.
                if (_activeWindow.AutoHides())
                {
                    return NavigationOutcome.NoOp("active window auto-hides");
                }
                List<WindowRect> rects = new List<WindowRect>(_activeWindows.Count);
                foreach (WindowFrameAdapter w in _activeWindows)
                {
                    try
                    {
                        rects.Add(w.Rect);
                    }
                    catch (Exception)
                    {
                        // One stale frame must not kill all navigation (m46): fall back to an
                        // empty rect, which SelectTarget excludes via the !IsEmpty predicate.
                        rects.Add(WindowRect.Empty);
                    }
                }
                NavigationSnapshot? snapshot = NavigationSnapshot.Capture(rects, _activeIndex);
                if (snapshot == null) { return NavigationOutcome.NoOp("no snapshot"); }
                // N12: an empty active rect would anchor selection around (0,0,0,0); degrade to a
                // no-op with the m47 diagnostic instead.
                if (snapshot.Value.Active.IsEmpty)
                {
                    return NavigationOutcome.NoOp("active window rect unavailable");
                }
                int? target = WindowNavigationEngine.SelectTarget(snapshot.Value.Active, snapshot.Value.Candidates, direction, _settings);
                if (target.HasValue)
                {
                    _activeWindows[target.Value].Activate();
                    return NavigationOutcome.ActivatedAt(target.Value);
                }
                return NavigationOutcome.NoOp("no target in direction");
            }
            catch (Exception ex)
            {
                // Navigation is best-effort: never throw into the keyboard hook or pop a modal
                // dialog mid-typing. Log and move on.
                Telescope.Logging.NeoVisualLog.Log(
                    $"{Telescope.Logging.DiagnosticLog.NeoVisual}Window navigation failed: {ex.Message}\n{ex.StackTrace}");
                return NavigationOutcome.NoOp($"navigation failed: {ex.Message}");
            }
        }

    }

    /// <summary>
    /// The outcome of a <see cref="WindowNavigator.NavigateInDirection"/> call: whether a target window
    /// was activated (and its index) or why navigation was a no-op. Lets the caller emit the m47
    /// outcome diagnostic (<c>navigate activated index=...</c> / <c>navigate no-op: &lt;reason&gt;</c>).
    /// </summary>
    internal readonly struct NavigationOutcome
    {
        public bool Activated { get; }
        public int Index { get; }
        public string? NoOpReason { get; }

        private NavigationOutcome(bool activated, int index, string? noOpReason)
        {
            Activated = activated;
            Index = index;
            NoOpReason = noOpReason;
        }

        public static NavigationOutcome ActivatedAt(int index) => new(true, index, null);

        public static NavigationOutcome NoOp(string reason) => new(false, -1, reason);
    }
}
