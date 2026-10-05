namespace MyExtension.ToolWindows
{
    /// <summary>
    /// Pure decision helper for the tool-window type probe (C7): whether a
    /// <c>GetGuidProperty</c> HRESULT is a failure that must be logged instead of silently
    /// defaulting <c>_type</c> to <c>Unknown</c>. Mirrors the Win32 <c>FAILED</c> macro — any
    /// negative HRESULT is a failure.
    /// </summary>
    public static class WindowTypeProbe
    {
        public static bool ShouldLogFailure(int hr) => hr < 0;
    }
}
