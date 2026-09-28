using CardinalNavigation;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MyExtension;
using System;
using System.Collections.Generic;

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

    private static bool IsTestStaleInjected() =>
        TestStaleSentinelPath != null && System.IO.File.Exists(TestStaleSentinelPath);

    public bool IsToolWindow => _isToolWindow || IsTestStaleInjected();

    public ToolWindowType Type => IsTestStaleInjected() ? ToolWindowType.SolutionExplorer : _type;

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

        }
        else
        {
            _isToolWindow = false;
            _type = ToolWindowType.Unknown;
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