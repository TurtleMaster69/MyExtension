using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using MyExtension;
using System;
using System.Collections.Generic;

public sealed class WindowManager : IDisposable
{
    private readonly IVsMonitorSelection _monitorSelection;
    private uint _selectionEventsCookie;

    // The active tool window's controller. Specific controllers can be registered here; any
    // unregistered type falls back to a shared GeneralToolWindowController, giving hjkl + an
    // i/Esc normal-input mode to every tool window by default.
    private readonly Dictionary<ToolWindowType, IToolWindowController> _controllers = new();
    private readonly GeneralToolWindowController _defaultController = new(ToolWindowType.Unknown);

    public IVsWindowFrame? CurrentWindow { get; private set; }
    public bool IsToolWindow { get; private set; }
    public ToolWindowType Type { get; private set; }

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
        // Return a stable per-type general controller so mode is remembered per window type.
        if (_controllers.TryGetValue(type, out var registered))
        {
            return registered;
        }

        if (type == ToolWindowType.Unknown)
        {
            return _defaultController;
        }

        var general = new GeneralToolWindowController(type);
        _controllers[type] = general;
        return general;
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
        RefreshCurrentWindow();
        if (CurrentWindow == null) { return; }
        CurrentWindow.GetProperty((int)__VSFPROPID.VSFPROPID_Type, out object value);
        if ((__WindowFrameTypeFlags)(int)value == __WindowFrameTypeFlags.WINDOWFRAMETYPE_Tool)
        {
            IsToolWindow = true;
            CurrentWindow.GetGuidProperty(
                    (int)__VSFPROPID.VSFPROPID_GuidPersistenceSlot,
                    out Guid guid);
            if (guid != null)
            {
                Type = ToolWindowTypeResolver.FromGuid(guid);
            }
            else
            {
                Type = ToolWindowType.Unknown;
            }

        }
        else
        {
            IsToolWindow = false;
            Type = ToolWindowType.Unknown;
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