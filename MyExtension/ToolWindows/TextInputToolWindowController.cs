using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MyExtension.ToolWindows
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
        public TextInputToolWindowController(ToolWindowType type) : base(type)
        {
            // N62: the _actions table + TryMove/ActionKeys lookup live in the base.
            _actions[Keys.A] = TextMotion(Keys.A);
            _actions[Keys.H] = TextMotion(Keys.H);
            _actions[Keys.L] = TextMotion(Keys.L);
            _actions[Keys.I] = TextMotion(Keys.I);
            AddTextMotionKeys(_actions);
        }

        protected override void OnModeChanged() => TextMotionHelper.StyleFocusedSurface(_isInputMode);
    }
}
