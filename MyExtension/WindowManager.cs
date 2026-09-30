using CardinalNavigation;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;

namespace MyExtension
{
public sealed class WindowManager : IDisposable
{
    private readonly IVsMonitorSelection _monitorSelection;
    private uint _selectionEventsCookie;

    // Cached IVsUIShell frame enumeration, invalidated on focus-change events so navigation
    // reuses the enumeration across keystrokes instead of rebuilding it per Ctrl+H/J/K/L.
    private List<WindowAdapter>? _cachedAdapters;
    private bool _adaptersDirty = true;

    // The active tool window's controller. Specific controllers can be registered here; any
    // unregistered type falls back to a shared GeneralToolWindowController, giving hjkl + an
    // i/Esc normal-input mode to every tool window by default.
    private readonly Dictionary<ToolWindowType, IToolWindowController> _controllers = new();
    private readonly GeneralToolWindowController _defaultController = new(ToolWindowType.Unknown);

    public IVsWindowFrame? CurrentWindow { get; private set; }

    // The frame-derived tool-window state, set only in OnWindowFocusChanged. Exposed through the
    // sentinel-aware public members below so the test-only stale-focus fault can be injected.
    private bool _isToolWindow;
    private ToolWindowType _type;
    private bool _isTextInputType;

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
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}stale-toolwindow sentinel active");
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
    /// True when the current tool window genuinely holds WPF keyboard focus: the focused element
    /// (or one of its visual/logical ancestors) is inside the tool-window frame's WPF content.
    /// The frame-derived tool-window flag can lag behind real WPF focus, so this is the raw
    /// "the tool window owns the keyboard" fact the FocusGuard ANDs with the text-input type.
    /// UI thread only.
    /// </summary>
    public bool TextInputSurfaceFocused
    {
        get
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
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

                for (var current = focused; current != null; current = GetParent(current))
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
    }

    private static System.Windows.DependencyObject? GetParent(System.Windows.DependencyObject child)
    {
        try
        {
            var visual = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (visual != null)
            {
                return visual;
            }
        }
        catch
        {
            // not a visual — try the logical tree
        }
        try
        {
            return System.Windows.LogicalTreeHelper.GetParent(child);
        }
        catch
        {
            return null;
        }
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
        _isTextInputType = GeneralToolWindowController.IsTextInputType(_type);
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

    private IToolWindowController GetController(ToolWindowType type)
    {
        // Return a stable per-type controller so mode is remembered per window type.
        return _controllers.TryGetValue(type, out var registered) ? registered : _defaultController;
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
    /// <see cref="WindowAdapter.Rect"/>.
    /// </summary>
    internal List<WindowAdapter> GetWindowAdapters(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_cachedAdapters == null || _adaptersDirty)
        {
            _cachedAdapters = WindowAdapter.Enumerate(package);
            _adaptersDirty = false;
        }
        return _cachedAdapters;
    }

    private void RefreshCurrentWindow()
    {
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
        if (CurrentWindow == null) { return; }
        CurrentWindow.GetProperty((int)__VSFPROPID.VSFPROPID_Type, out object value);
        if ((__WindowFrameTypeFlags)(int)value == __WindowFrameTypeFlags.WINDOWFRAMETYPE_Tool)
        {
            _isToolWindow = true;
            CurrentWindow.GetGuidProperty(
                    (int)__VSFPROPID.VSFPROPID_GuidPersistenceSlot,
                    out Guid guid);
            if (guid != null)
            {
                _type = ToolWindowTypeResolver.FromGuid(guid);
            }
            else
            {
                _type = ToolWindowType.Unknown;
            }
            _isTextInputType = GeneralToolWindowController.IsTextInputType(_type);

        }
        else
        {
            _isToolWindow = false;
            _type = ToolWindowType.Unknown;
            _isTextInputType = GeneralToolWindowController.IsTextInputType(_type);
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