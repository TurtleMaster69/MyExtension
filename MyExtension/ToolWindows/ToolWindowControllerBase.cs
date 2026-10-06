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

        // N62: the action-key table + its TryMove lookup are single-sourced here; subclasses
        // populate _actions and only override TryMove when they need custom routing.
        protected readonly Dictionary<Keys, Func<bool>> _actions = new Dictionary<Keys, Func<bool>>();

        protected ToolWindowControllerBase(ToolWindowType type)
        {
            _type = type;
            // A window's initial mode comes from whether its type is a text-input surface (m23 —
            // the base/initial-mode flow owns this so every controller starts correctly).
            _isInputMode = GeneralToolWindowController.IsTextInputType(type);
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
        /// text-input window): routes the key through <see cref="TextMotionHelper.TryMoveFocusedSurface"/>.
        /// N21: an a/A/I insert placement enters input mode through <see cref="EnterInputMode"/> so
        /// the mode-change side effects (caret restyle) fire — never by mutating the flag directly.</summary>
        protected Func<bool> TextMotion(Keys key) => () =>
        {
            bool handled = TextMotionHelper.TryMoveFocusedSurface(key, out bool enteredInputMode);
            if (handled && enteredInputMode)
            {
                EnterInputMode();
            }
            return handled;
        };

        /// <summary>
        /// m5 (BP-10): the <paramref name="focusedBox"/> overload — the caller resolves the focused
        /// WPF text box ONCE and passes it through, so the visual tree is not walked twice per
        /// routed key (the gate + the internal walk). The <see cref="TextMotionHelper.TryMoveFocusedSurface(Keys, out bool, System.Windows.Controls.TextBox?)"/>
        /// overload at TextMotionHelper.cs:90 already avoids the second walk.
        /// </summary>
        protected Func<bool> TextMotion(Keys key, System.Windows.Controls.TextBox? focusedBox) => () =>
        {
            bool handled = TextMotionHelper.TryMoveFocusedSurface(key, out bool enteredInputMode, focusedBox);
            if (handled && enteredInputMode)
            {
                EnterInputMode();
            }
            return handled;
        };

        /// <summary>
        /// Adds the shared w/b/e vim text-motion action wiring to <paramref name="actions"/> (m16 —
        /// the single source for the text-input surfaces' word motions).
        /// </summary>
        protected void AddTextMotionKeys(Dictionary<Keys, Func<bool>> actions)
        {
            actions[Keys.W] = TextMotion(Keys.W);
            actions[Keys.B] = TextMotion(Keys.B);
            actions[Keys.E] = TextMotion(Keys.E);
        }

        public virtual bool TryMove(Keys key)
        {
            return _actions.TryGetValue(key, out var action) && action();
        }

        public virtual IReadOnlyCollection<Keys> ActionKeys => _actions.Keys;
    }
}
