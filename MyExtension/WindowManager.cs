using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using MyExtension;
using System;

public sealed class WindowManager : IDisposable
{
    private readonly IVsMonitorSelection _monitorSelection;
    private uint _selectionEventsCookie;

    public IVsWindowFrame? CurrentWindow { get; private set; }
    public bool IsToolWindow { get; private set; }
    public ToolWindowType Type { get; private set; }

    public WindowManager(IVsMonitorSelection monitorSelection)
    {
        _monitorSelection = monitorSelection;

        _monitorSelection.AdviseSelectionEvents(
            new SelectionEvents(this),
            out _selectionEventsCookie);

        // Initialize the current window once.
        UpdateCurrentWindow();
    }

    private void UpdateCurrentWindow()
    {
        _monitorSelection.GetCurrentElementValue(
            (uint)VSConstants.VSSELELEMID.SEID_WindowFrame,
            out object value);

        CurrentWindow = value as IVsWindowFrame;
    }

    private void OnWindowFocusChanged()
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        UpdateCurrentWindow();
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