using MyExtension.Input;
using MyExtension.Navigation;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;

namespace MyExtension.ToolWindows
{
    public sealed class WindowManager : IDisposable
    {
        private readonly IVsMonitorSelection _monitorSelection;
        private uint _selectionEventsCookie;
    
        // Cached IVsUIShell frame enumeration, invalidated on focus-change events so navigation
        // reuses the enumeration across keystrokes instead of rebuilding it per Ctrl+H/J/K/L.
        private List<WindowFrameAdapter>? _cachedAdapters;
        private bool _adaptersDirty = true;
    
        // The active tool window's controller. Specific controllers can be registered here; any
        // unregistered type falls back to a PER-TYPE default instance (see ResolveController), so
        // mode is remembered per window type and never leaks across windows (m22).
        private readonly Dictionary<ToolWindowType, IToolWindowController> _controllers = new();

        // R20: instance-scoped cache of the per-type DEFAULT controllers, populated on miss in
        // GetController. NOT static (static mutable state is the R40 class of issue) — the cache
        // lives on the WindowManager instance so two GetController calls for the same type return
        // the SAME instance ("mode remembered per type" no longer depends on package init eagerly
        // registering every enum value).
        private readonly Dictionary<ToolWindowType, IToolWindowController> _defaultControllers = new();

        // m14 (BP-13): fired at the end of OnWindowFocusChanged so subscribers (the package's
        // SolutionExplorerController invalidation) can react to ANY focus change — the CLICK case
        // the mode-change-only invalidation missed.
        public event Action? FocusChanged;
    
        public IVsWindowFrame? CurrentWindow { get; private set; }
    
        // The frame-derived tool-window state, set only in OnWindowFocusChanged. Exposed through the
        // sentinel-aware public members below so the test-only stale-focus fault can be injected.
        private bool _isToolWindow;
        private ToolWindowType _type;
        private bool _isTextInputType;
    
        // M1: the focused-surface fact (whether the current tool window genuinely holds WPF keyboard
        // focus) is cached on focus-change events, NOT re-evaluated per key-down. The COM
        // GetProperty(VSFPROPID_DocView) + Keyboard.FocusedElement + visual-tree walk runs only in
        // OnWindowFocusChanged (alongside _isTextInputType); the per-key getter reads this cached bool.
        private bool _textInputSurfaceFocused;

        // C3: the focused-text-box-in-current-tool-window fact is cached on focus-change events (the
        // M1 pattern) — the COM GetProperty(VSFPROPID_DocView) + visual-tree walk runs only in
        // OnWindowFocusChanged, not per shift+key routed to a tool window.
        private bool _focusedTextBoxInCurrentToolWindow;

        // n6 (BP-23): the single seam through which the merged focus walk resolves the focused WPF
        // text box (the SolutionExplorerController m5 pattern) — a test replaces it with a fake box
        // so the merged walk is testable without a real visual tree.
        private Func<System.Windows.Controls.TextBox?> _findFocusedTextBox = TextMotionHelper.FindFocusedTextBox;
    
        // Test-only fault injection: when the harness creates a 'stale-toolwindow' sentinel file under
        // NEOVISUAL_LOG_DIR, report the Solution Explorer frame as current even when it is not — the
        // stale-frame + editor-focused state the leak was observed in. Absent in normal user runs
        // (the env var is unset -> null path -> no file stat).
        private static readonly string? TestStaleSentinelPath = BuildTestSentinelPath();
    
        private static string? BuildTestSentinelPath()
        {
            string? dir = Environment.GetEnvironmentVariable("NEOVISUAL_LOG_DIR");
            return string.IsNullOrEmpty(dir) ? null : System.IO.Path.Combine(dir, "stale-toolwindow");
        }
    
        private readonly StaleToolWindowSentinel _sentinel = new(TestStaleSentinelPath);
    
        private bool IsTestStaleInjected() => _sentinel.IsStale;
    
        /// <summary>
        /// Re-stats the sentinel file (once per key-down, from <see cref="InputHandler.IsKeyOfInterest"/>)
        /// and logs the positive diagnostic on the false→true transition so the harness can assert the
        /// fault is active. Exactly one line per activation; re-arms on removal.
        /// </summary>
        public void RefreshStaleSentinel()
        {
            if (_sentinel.Refresh() && _sentinel.IsStale)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}stale-toolwindow sentinel active");
            }
        }
    
        public bool IsToolWindow => _isToolWindow || IsTestStaleInjected();
    
        public ToolWindowType Type => IsTestStaleInjected() ? ToolWindowType.SolutionExplorer : _type;
    
        /// <summary>
        /// Whether the current tool-window type is a text-input surface, cached on focus change.
        /// Sentinel-aware: a forced stale Solution Explorer frame must never classify as text-input
        /// (SolutionExplorer is never text-input), keeping the FocusGuard veto correct under the fault.
        /// </summary>
        public bool IsTextInputType => IsTestStaleInjected() ? false : _isTextInputType;
    
        /// <summary>
        /// True when the current tool window genuinely holds WPF keyboard focus. Cached on focus-change
        /// events (M1) — the per-key getter reads the cached bool instead of performing the COM
        /// <c>GetProperty(VSFPROPID_DocView)</c> + <c>Keyboard.FocusedElement</c> + visual-tree walk on
        /// every key-down. Sentinel-aware: a forced stale Solution Explorer frame never reports a
        /// text-input surface as focused.
        /// </summary>
        public bool TextInputSurfaceFocused => IsTestStaleInjected() ? false : _textInputSurfaceFocused;

        /// <summary>
        /// m60 (BP-D13): test-only seam — forces the frame-derived tool-window state (the fields
        /// the SEID_WindowFrame selection event normally sets) so the InputHandler routing tests
        /// are hermetic. No production behavior change.
        /// </summary>
        internal void SetToolWindowStateForTest(bool isToolWindow, ToolWindowType type, bool isTextInputType = false, bool textInputSurfaceFocused = false)
        {
            _isToolWindow = isToolWindow;
            _type = type;
            _isTextInputType = isTextInputType;
            _textInputSurfaceFocused = textInputSurfaceFocused;
        }

        /// <summary>
        /// M5 (BP-2): invalidates the cached text-input-surface flag when the main editor gains
        /// focus. The flag is cached on focus-change events only (M1), so a stale Command Window
        /// frame could otherwise claim keyboard ownership over a focused editor; the
        /// <see cref="VimModeTracker.MainEditorFocused"/> event (keyed on the focused view's
        /// document identity) drives this invalidation.
        /// </summary>
        internal void InvalidateTextInputSurfaceFocused()
        {
            _textInputSurfaceFocused = false;
            _focusedTextBoxInCurrentToolWindow = false;
        }
    
        /// <summary>
        /// n6 (BP-23): the merged COM/visual-tree walk that computes BOTH the text-input-surface
        /// fact and the focused-text-box-in-current-tool-window fact in ONE pass — reads
        /// <c>GetProperty(VSFPROPID_DocView)</c> ONCE and does one visual-tree walk (previously two
        /// separate walks per focus change). Runs only on focus-change events (M1/C3), not per
        /// key-down. UI thread only. The focused box is resolved through the
        /// <see cref="_findFocusedTextBox"/> seam (testable without a real visual tree).
        /// </summary>
        private void ComputeFocusedSurfaceState(out bool textInputSurfaceFocused, out bool focusedTextBoxInCurrentToolWindow)
        {
            textInputSurfaceFocused = false;
            focusedTextBoxInCurrentToolWindow = false;
            if (!IsToolWindow || CurrentWindow == null)
            {
                return;
            }
            try
            {
                CurrentWindow.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object docViewObj);
                if (docViewObj is System.Windows.FrameworkElement frameContent)
                {
                    // Primary: the focused element / box is a descendant of the current tool
                    // window's DocView content (the same walk the two old methods used).
                    var focused = System.Windows.Input.Keyboard.FocusedElement as System.Windows.DependencyObject;
                    if (focused != null)
                    {
                        textInputSurfaceFocused = IsDescendantOf(focused, frameContent);
                    }
                    var box = _findFocusedTextBox();
                    if (box != null)
                    {
                        focusedTextBoxInCurrentToolWindow = IsDescendantOf(box, frameContent);
                    }
                }
                else
                {
                    // The DocView is a COM object (not a WPF FrameworkElement), so the visual-tree
                    // walk can never match it — yet its focused surface is an IWpfTextView. For a
                    // text-input tool window a focused editor view IS that window's own surface, so
                    // it owns the keyboard. Navigation tool windows (Solution Explorer) are not
                    // text-input, so the editor-veto is unaffected.
                    textInputSurfaceFocused = IsTextInputType
                        && System.Windows.Input.Keyboard.FocusedElement is Microsoft.VisualStudio.Text.Editor.IWpfTextView;

                    // Fallback for the box: scope by the focused box's own top-level window — the VS
                    // main window has Owner == null; a modal dialog's Window has an Owner (the main
                    // window), so a modal rename/move dialog's TextBox is NOT exempted (R10 preserved).
                    var box = _findFocusedTextBox();
                    if (box != null)
                    {
                        var top = FindTopLevelWindow(box);
                        focusedTextBoxInCurrentToolWindow = top != null && top.Owner == null;
                    }
                }
            }
            catch
            {
                // both flags stay false
            }
        }

        /// <summary>
        /// A10: the shared "walk up to the DocView content" loop — true when
        /// <paramref name="start"/> is <paramref name="frameContent"/> or a descendant of it in
        /// the WPF logical/visual tree. Used by <see cref="ComputeFocusedSurfaceState"/>
        /// (previously duplicated inline).
        /// </summary>
        private static bool IsDescendantOf(System.Windows.DependencyObject start, System.Windows.FrameworkElement frameContent)
        {
            for (var current = start; current != null; current = TextMotionHelper.GetParent(current))
            {
                if (ReferenceEquals(current, frameContent))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when the focused WPF TextBox belongs to the CURRENT tool window. C3: cached on
        /// focus-change events (the M1 pattern) — the per-key getter reads the cached bool instead
        /// of performing the COM <c>GetProperty(VSFPROPID_DocView)</c> + visual-tree walk on every
        /// shift+key routed to a tool window. Sentinel-aware: a forced stale Solution Explorer
        /// frame never reports a text box as focused.
        /// </summary>
        public bool IsFocusedTextBoxInCurrentToolWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return IsTestStaleInjected() ? false : _focusedTextBoxInCurrentToolWindow;
        }

        /// <summary>
        /// The top-level WPF <see cref="System.Windows.Window"/> hosting <paramref name="child"/>,
        /// or null. Used by <see cref="ComputeFocusedSurfaceState"/>'s COM-object fallback: the box
        /// must be hosted in the VS main window (Owner == null), NOT a separate modal dialog (whose
        /// Window has an Owner) — scopes the D4 shift-gate exemption to the current tool window's
        /// own search box, preserving R10.
        /// </summary>
        private static System.Windows.Window? FindTopLevelWindow(System.Windows.DependencyObject child)
        {
            for (var current = child; current != null; current = TextMotionHelper.GetParent(current))
            {
                if (current is System.Windows.Window window)
                {
                    return window;
                }
            }
            return null;
        }

        /// <summary>
        /// The controller driving the currently focused tool window, or null when focus is not in a
        /// tool window. Re-evaluated from <see cref="Type"/> on every access (cheap dictionary lookup).
        /// </summary>
        public IToolWindowController? CurrentController =>
            IsToolWindow ? GetController(Type) : null;
    
        public WindowManager(IVsMonitorSelection monitorSelection)
        {
            _monitorSelection = monitorSelection;
    
            // m17 (BP-16): check the AdviseSelectionEvents HRESULT — a failure leaves
            // _selectionEventsCookie = 0 and no focus-change events ever fire (stale
            // _isToolWindow/_type/_textInputSurfaceFocused). Log the failure (the C7 `window type
            // probe failed` precedent) instead of silently discarding it; Dispose already guards != 0.
            int hr = _monitorSelection.AdviseSelectionEvents(
                new SelectionEvents(this),
                out _selectionEventsCookie);
            if (hr < 0)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}selection events advise failed: 0x{hr:X8}");
                _selectionEventsCookie = 0;
            }
    
            // Initialize the current window (and its classification) once so InputHandler has
            // correct state immediately, not only after the first focus-change event.
            RefreshCurrentWindow();
            OnWindowFocusChanged();
        }
    
        /// <summary>
        /// Registers a controller for a tool-window type, overriding the default. Called on the UI
        /// thread (e.g. from package init or by specific tool-window integrations).
        /// </summary>
        public void RegisterController(IToolWindowController controller)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            _controllers[controller.Type] = controller;
        }
    
        /// <summary>
        /// Resolves the controller for a tool-window type: the registered controller when present,
        /// else a PER-TYPE default instance (never a shared one — m22). Pure static factory — no VS
        /// API.
        /// </summary>
        public static IToolWindowController? ResolveController(
            IReadOnlyDictionary<ToolWindowType, IToolWindowController> registered, ToolWindowType type)
        {
            // n13: inline the registered→default logic without the throwaway defaults dictionary —
            // each call resolves a fresh per-type default (stateless, no caching).
            if (registered.TryGetValue(type, out var c))
            {
                return c;
            }
            return DefaultControllerFor(type);
        }

        internal IToolWindowController? GetController(ToolWindowType type)
        {
            // m21 (BP-20): GetController delegates to ResolveController for the decision (registered
            // wins, else the per-type default), then applies the instance cache so two calls for the
            // same type return the SAME instance ("mode remembered per type" — R20: the per-type
            // DEFAULT instances are cached in an INSTANCE-scoped dictionary, populated on miss).
            if (_defaultControllers.TryGetValue(type, out var cached))
            {
                return cached;
            }
            var created = ResolveController(_controllers, type);
            if (created != null)
            {
                _defaultControllers[type] = created;
            }
            return created;
        }
    
        /// <summary>
        /// The default controller for a tool-window type, or null when the type has no default
        /// (SolutionExplorer is driven by the specialized SolutionExplorerController registered by the
        /// package; Unknown has no window to drive). Pure static factory — no VS API.
        /// </summary>
        public static IToolWindowController? DefaultControllerFor(ToolWindowType type)
        {
            if (type == ToolWindowType.SolutionExplorer || type == ToolWindowType.Unknown)
            {
                return null;
            }
            return ToolWindowTypeResolver.IsTextInputType(type)
                ? new TextInputToolWindowController(type)
                : new GeneralToolWindowController(type);
        }
    
        /// <summary>
        /// Returns the cached frame enumeration, re-enumerating only when it is dirty (a focus
        /// change) or not yet built. Rects are refreshed lazily per navigation via
        /// <see cref="WindowFrameAdapter.Rect"/>.
        /// </summary>
        internal List<WindowFrameAdapter> GetWindowAdapters(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cachedAdapters == null || _adaptersDirty)
            {
                _cachedAdapters = WindowFrameAdapter.Enumerate(package);
                _adaptersDirty = false;
            }
            return _cachedAdapters;
        }
    
        private void RefreshCurrentWindow()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            _monitorSelection.GetCurrentElementValue(
                (uint)VSConstants.VSSELELEMID.SEID_WindowFrame,
                out object value);
    
            CurrentWindow = value as IVsWindowFrame;
        }
    
        private void OnWindowFocusChanged()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            _adaptersDirty = true;
            RefreshCurrentWindow();
            if (CurrentWindow == null)
            {
                // R7: reset the stale frame-derived state before the null-return so a null frame
                // does not leave _isToolWindow/_type/_isTextInputType/_textInputSurfaceFocused
                // from the previous frame.
                _isToolWindow = false;
                _type = ToolWindowType.Unknown;
                _isTextInputType = false;
                _textInputSurfaceFocused = false;
                _focusedTextBoxInCurrentToolWindow = false;
                // m14 (BP-13): a focus change still fires the event (the click case).
                FocusChanged?.Invoke();
                return;
            }
            // m22 (BP-21): the frame-derived state computation runs inside the IVsSelectionEvents
            // COM callback — a disposed frame's GetProperty/GetGuidProperty throws out of the
            // callback. Wrap it and reset the frame-derived state on failure.
            try
            {
                int hr = CurrentWindow.GetProperty((int)__VSFPROPID.VSFPROPID_Type, out object value);
                // N25: guard the cast — a non-int VSFPROPID_Type value must not throw
                // InvalidCastException out of the IVsSelectionEvents callback.
                if (hr == VSConstants.S_OK && value is int typeValue && (__WindowFrameTypeFlags)typeValue == __WindowFrameTypeFlags.WINDOWFRAMETYPE_Tool)
                {
                    _isToolWindow = true;
                    int guidHr = CurrentWindow.GetGuidProperty(
                            (int)__VSFPROPID.VSFPROPID_GuidPersistenceSlot,
                            out Guid guid);
                    // C7: the GetGuidProperty HRESULT was discarded -> silent _type = Unknown on COM
                    // failure. Check it and log the failure (the n19 `window rect unavailable`
                    // precedent) instead of silently defaulting. n14: the one-line ShouldLogFailure
                    // wrapper is inlined — any negative HRESULT is a failure (the Win32 FAILED macro).
                    if (guidHr < 0)
                    {
                        Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}window type probe failed: 0x{guidHr:X8}");
                        _type = ToolWindowType.Unknown;
                    }
                    else if (guid != Guid.Empty)
                    {
                        _type = ToolWindowTypeResolver.FromGuid(guid);
                    }
                    else
                    {
                        _type = ToolWindowType.Unknown;
                    }
                    _isTextInputType = ToolWindowTypeResolver.IsTextInputType(_type);
                    // n6 (BP-23): the merged single walk computes both surface facts (one DocView read).
                    ComputeFocusedSurfaceState(out bool textInputSurfaceFocused, out bool focusedTextBoxInCurrentToolWindow);
                    _textInputSurfaceFocused = textInputSurfaceFocused;
                    _focusedTextBoxInCurrentToolWindow = focusedTextBoxInCurrentToolWindow;
                }
                else
                {
                    _isToolWindow = false;
                    _type = ToolWindowType.Unknown;
                    _isTextInputType = ToolWindowTypeResolver.IsTextInputType(_type);
                    // n9: not a tool window — the merged walk would return false immediately (its
                    // first guard is !IsToolWindow), so skip the COM/visual-tree walk.
                    _textInputSurfaceFocused = false;
                    _focusedTextBoxInCurrentToolWindow = false;
                }
            }
            catch
            {
                // m22 (BP-21): a disposed frame threw out of the COM callback — reset the
                // frame-derived state so a stale frame never drives routing.
                _isToolWindow = false;
                _type = ToolWindowType.Unknown;
                _isTextInputType = false;
                _textInputSurfaceFocused = false;
                _focusedTextBoxInCurrentToolWindow = false;
            }
            // m14 (BP-13): fired at the end of every focus change (the click case).
            FocusChanged?.Invoke();
        }
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_selectionEventsCookie != 0)
            {
                _monitorSelection.UnadviseSelectionEvents(
                    _selectionEventsCookie);
    
                _selectionEventsCookie = 0;
            }
        }
    
        private sealed class SelectionEvents : IVsSelectionEvents
        {
            private readonly WindowManager _owner;
    
            public SelectionEvents(WindowManager owner)
            {
                _owner = owner;
            }
    
            public int OnElementValueChanged(
                uint elementid,
                object oldValue,
                object newValue)
            {
                if (elementid ==
                    (uint)VSConstants.VSSELELEMID.SEID_WindowFrame)
                {
                    _owner.OnWindowFocusChanged();
                }
    
                return VSConstants.S_OK;
            }
    
            public int OnSelectionChanged(
                IVsHierarchy pHierOld,
                uint itemidOld,
                IVsMultiItemSelect pMISOld,
                ISelectionContainer pSCOld,
                IVsHierarchy pHierNew,
                uint itemidNew,
                IVsMultiItemSelect pMISNew,
                ISelectionContainer pSCNew)
            {
                return VSConstants.S_OK;
            }
    
            public int OnCmdUIContextChanged(
                uint dwCmdUICookie,
                int fActive)
            {
                return VSConstants.S_OK;
            }
        }
    }
}
