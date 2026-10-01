using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MyExtension.ToolWindows
{
    /// <summary>
    /// Shared mode-state owner for the tool-window controllers. Holds the tool-window type and the
    /// normal/input-mode flag, and centralizes the Enter/ExitInputMode flip so every controller
    /// behaves identically; subclasses hook the mode change via <see cref="OnModeChanged"/> (e.g. to
    /// restyle the caret) and implement the per-window key routing.
    /// </summary>
    internal abstract class ToolWindowControllerBase : IToolWindowController
    {
        protected readonly ToolWindowType _type;
        protected bool _isInputMode;

        protected ToolWindowControllerBase(ToolWindowType type)
        {
            _type = type;
        }

        public ToolWindowType Type => _type;

        public bool IsInputMode => _isInputMode;

        public virtual void EnterInputMode()
        {
            _isInputMode = true;
            OnModeChanged();
        }

        public virtual void ExitInputMode()
        {
            _isInputMode = false;
            OnModeChanged();
        }

        protected virtual void OnModeChanged() { }

        /// <summary>Shared vim text-motion action wiring for the focused text surface (search box /
        /// text-input window): routes the key through <see cref="TextMotionHelper.TryMoveFocusedSurface"/>.</summary>
        protected Func<bool> TextMotion(Keys key) => () => TextMotionHelper.TryMoveFocusedSurface(key, ref _isInputMode);

        public abstract bool TryMove(Keys key);

        public abstract IReadOnlyCollection<Keys> ActionKeys { get; }
    }
}
