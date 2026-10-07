namespace MyExtension.Adornments
{
    /// <summary>
    /// Pure, dependency-free state model for the block caret (m57/BP-D6) — the
    /// <c>OverlayKeyHandler</c>/<c>TextMotionNavigator</c> pattern: the WPF-coupled
    /// <see cref="BlockCaretAdornment"/> delegates its desired/rendered tracking here so the
    /// focus-loss/regain contract is unit-testable without a view or an adornment layer.
    ///
    /// <para/>
    /// Two states are tracked separately: <see cref="DesiredActive"/> (what the caller asked for —
    /// a normal-mode editor view wants a block caret) and <see cref="RenderedActive"/> (what is
    /// actually drawn). A focus loss clears only the RENDERED state (the native line caret shows);
    /// the DESIRED state survives, and a focus regain restores the rendered state to the desired
    /// value. <see cref="ApplyRendered"/> is a no-op when the value is unchanged.
    /// </summary>
    internal sealed class BlockCaretState
    {
        /// <summary>True while a block caret SHOULD be drawn (the DESIRED state).</summary>
        public bool DesiredActive { get; set; }

        /// <summary>True while a block caret IS drawn (the RENDERED state).</summary>
        public bool RenderedActive { get; private set; }

        /// <summary>Applies the rendered state; a no-op when the value is unchanged.</summary>
        public void ApplyRendered(bool value)
        {
            if (RenderedActive == value)
            {
                return;
            }
            RenderedActive = value;
        }

        /// <summary>Focus loss: clear the rendered state (the desired state stays).</summary>
        public void OnLostFocus() => ApplyRendered(false);

        /// <summary>Focus regain: restore the rendered state to the desired value.</summary>
        public void OnGotFocus() => ApplyRendered(DesiredActive);
    }
}
