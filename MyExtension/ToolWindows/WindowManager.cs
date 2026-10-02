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
        /// The COM/visual-tree walk that computes whether the current tool window's WPF content holds
        /// keyboard focus. Runs only on focus-change events (M1), not per key-down. UI thread only.
        /// </summary>
        private bool ComputeTextInputSurfaceFocused()
        {
            if (!IsToolWindow || CurrentWindow == null)
            {
                return false;
            }
    
            try
            {
                CurrentWindow.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object docViewObj);
                if (!(docViewObj is System.Windows.FrameworkElement frameContent))
                {
                    return false;
                }
    
                var focused = System.Windows.Input.Keyboard.FocusedElement as System.Windows.DependencyObject;
                if (focused == null)
                {
                    return false;
                }
    
                for (var current = focused; current != null; current = TextMotionHelper.GetParent(current))
                {
                    if (ReferenceEquals(current, frameContent))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
    
            return false;
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
    
            _monitorSelection.AdviseSelectionEvents(
                new SelectionEvents(this),
                out _selectionEventsCookie);
    
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
            return registered.TryGetValue(type, out var registeredController)
                ? registeredController
                : DefaultControllerFor(type);
        }

        private IToolWindowController? GetController(ToolWindowType type)
        {
            // Return a stable per-type controller so mode is remembered per window type. R20: the
            // per-type DEFAULT instances are cached in an INSTANCE-scoped dictionary (populated on
            // miss) so two GetController calls for the same type return the SAME instance.
            if (_controllers.TryGetValue(type, out var registered))
            {
                return registered;
            }
            if (_defaultControllers.TryGetValue(type, out var cached))
            {
                return cached;
            }
            var created = DefaultControllerFor(type);
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
            return GeneralToolWindowController.IsTextInputType(type)
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
                return;
            }
            int hr = CurrentWindow.GetProperty((int)__VSFPROPID.VSFPROPID_Type, out object value);
            if (hr == VSConstants.S_OK && value != null && (__WindowFrameTypeFlags)(int)value == __WindowFrameTypeFlags.WINDOWFRAMETYPE_Tool)
            {
                _isToolWindow = true;
                CurrentWindow.GetGuidProperty(
                        (int)__VSFPROPID.VSFPROPID_GuidPersistenceSlot,
                        out Guid guid);
                if (guid != Guid.Empty)
                {
                    _type = ToolWindowTypeResolver.FromGuid(guid);
                }
                else
                {
                    _type = ToolWindowType.Unknown;
                }
                _isTextInputType = GeneralToolWindowController.IsTextInputType(_type);
                _textInputSurfaceFocused = ComputeTextInputSurfaceFocused();
    
            }
            else
            {
                _isToolWindow = false;
                _type = ToolWindowType.Unknown;
                _isTextInputType = GeneralToolWindowController.IsTextInputType(_type);
                // n9: not a tool window — ComputeTextInputSurfaceFocused() would return false
                // immediately (its first guard is !IsToolWindow), so skip the COM/visual-tree walk.
                _textInputSurfaceFocused = false;
            }
        }
        public void Dispose()
        {
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
