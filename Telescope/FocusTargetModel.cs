namespace Telescope
{
    /// <summary>
    /// Where the overlay's keyboard focus currently lives: the results list (default, where
    /// j/k select) or the file preview (where h/l/j/k/w/b/e/gg/G navigate the code read-only).
    /// The member names <c>List</c>/<c>Preview</c> produce the exact <c>ToString()</c> tokens the
    /// <c>[Telescope] focus target=List|Preview</c> diagnostic contract needs.
    /// </summary>
    internal enum FocusTarget
    {
        List,
        Preview,
    }

    /// <summary>
    /// The outcome of feeding a key to <see cref="FocusTargetModel.Handle"/>: whether the key was
    /// consumed by the focus-target state machine (<see cref="Handled"/>) or should fall through
    /// to the overlay's normal key handling (<see cref="None"/>).
    /// </summary>
    internal enum FocusTargetAction
    {
        None,
        Handled,
    }

    /// <summary>
    /// Dependency-free state machine for the overlay's focus target (results list vs file
    /// preview): Ctrl+L moves to the preview, Ctrl+H returns to the list, and Escape in the
    /// preview returns to the list. Extracted from <see cref="TelescopeOverlay"/> so the
    /// focus-target transitions can be unit-tested without a WPF window or Visual Studio.
    /// </summary>
    internal sealed class FocusTargetModel
    {
        public FocusTarget Current { get; private set; } = FocusTarget.List;

        public void Reset() => Current = FocusTarget.List;

        public FocusTargetAction Handle(OverlayKey key)
        {
            switch (key)
            {
                case OverlayKey.CtrlL:
                    Current = FocusTarget.Preview;
                    return FocusTargetAction.Handled;
                case OverlayKey.CtrlH:
                    Current = FocusTarget.List;
                    return FocusTargetAction.Handled;
                case OverlayKey.Escape when Current == FocusTarget.Preview:
                    Current = FocusTarget.List;
                    return FocusTargetAction.Handled;
                default:
                    return FocusTargetAction.None;
            }
        }
    }
}
