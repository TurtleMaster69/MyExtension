using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MyExtension
{
    /// <summary>
    /// Controller for text-input tool windows (Command Window, Find and Replace, Immediate Window,
    /// ...): surfaces the user can type into. Starts in <b>insert mode</b> so typing works
    /// immediately; after <c>Esc</c> it enters a vim-style <b>normal mode</b> with caret motions
    /// over the text box:
    ///
    /// <list type="bullet">
    /// <item><c>h</c>/<c>l</c> — character left/right</item>
    /// <item><c>w</c>/<c>b</c>/<c>e</c> — next/previous word start, end of word</item>
    /// <item><c>a</c> — insert after the caret</item>
    /// <item><c>A</c> — insert at the end of the line</item>
    /// <item><c>I</c> — insert at the start of the line</item>
    /// <item><c>i</c> — insert at the caret (the generic <see cref="InputHandler"/> handler)</item>
    /// </list>
    ///
    /// The caret is drawn <b>block</b> in normal mode and <b>line</b> in insert mode (WPF text
    /// boxes only). Motions act on the focused text box — a WPF <see cref="System.Windows.Controls.TextBox"/>
    /// via <c>Keyboard.FocusedElement</c>, or a WinForms <see cref="System.Windows.Forms.TextBoxBase"/>
    /// in legacy tool windows via <c>GetFocus</c>/<c>Control.FromHandle</c>. When the focused
    /// control is not a text box, <c>h</c>/<c>l</c> fall back to injected arrow keys. The motion
    /// pipeline itself lives in <see cref="TextMotionHelper"/>.
    ///
    /// <para/>
    /// <b>Threading:</b> all members are called on the UI thread only (same thread as the hook), so
    /// reading <c>Keyboard.FocusedElement</c>/<c>Keyboard.Modifiers</c> and mutating the caret is
    /// safe.
    /// </summary>
    internal sealed class TextInputToolWindowController : ToolWindowControllerBase
    {
        private readonly Dictionary<Keys, Func<bool>> _actions;

        public TextInputToolWindowController(ToolWindowType type) : base(type)
        {
            // Text-input surfaces default to insert mode so the user can type immediately.
            _isInputMode = true;
            _actions = new Dictionary<Keys, Func<bool>>
            {
                [Keys.W] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.W, ref _isInputMode),
                [Keys.B] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.B, ref _isInputMode),
                [Keys.E] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.E, ref _isInputMode),
                [Keys.A] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.A, ref _isInputMode),
                [Keys.H] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.H, ref _isInputMode),
                [Keys.L] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.L, ref _isInputMode),
            };
        }

        protected override void OnModeChanged() => TextMotionHelper.StyleFocusedSurface(_isInputMode);

        /// <summary>The non-hjkl keys routed to normal mode (w/b/e/a/h/l are vim text motions).</summary>
        public override IReadOnlyCollection<Keys> ActionKeys => _actions.Keys;

        public override bool TryMove(Keys key)
        {
            return _actions.TryGetValue(key, out var action) && action();
        }
    }
}
