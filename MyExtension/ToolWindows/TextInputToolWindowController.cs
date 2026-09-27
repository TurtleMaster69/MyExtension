using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Telescope;

namespace MyExtension
{
    /// <summary>
    /// The vim text motions available in a text-input tool window's normal mode, used by
    /// <see cref="TextInputToolWindowController"/>. Pure mapping (key + shift state -> motion) so
    /// it can be unit-tested hermetically; the motion math itself lives in the shared
    /// <see cref="TextMotionNavigator"/>.
    /// </summary>
    internal enum TextMotion
    {
        Left,
        Right,
        NextWord,
        PrevWord,
        EndWord,
        InsertAfter,
        InsertEnd,
        InsertStart,
    }

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
    /// control is not a text box, <c>h</c>/<c>l</c> fall back to injected arrow keys.
    ///
    /// <para/>
    /// <b>Threading:</b> all members are called on the UI thread only (same thread as the hook), so
    /// reading <c>Keyboard.FocusedElement</c>/<c>Keyboard.Modifiers</c> and mutating the caret is
    /// safe.
    /// </summary>
    internal sealed class TextInputToolWindowController : IToolWindowController
    {
        private readonly ToolWindowType _type;
        private bool _isInputMode;

        public TextInputToolWindowController(ToolWindowType type)
        {
            _type = type;
            // Text-input surfaces default to insert mode so the user can type immediately.
            _isInputMode = true;
        }

        public ToolWindowType Type => _type;

        public bool IsInputMode => _isInputMode;

        public void EnterInputMode()
        {
            _isInputMode = true;
            ApplyCaretStyle();
        }

        public void ExitInputMode()
        {
            _isInputMode = false;
            ApplyCaretStyle();
        }

        /// <summary>The non-hjkl keys routed to normal mode (w/b/e/a are vim text motions).</summary>
        public IReadOnlyCollection<Keys> ActionKeys { get; } =
            new List<Keys> { Keys.W, Keys.B, Keys.E, Keys.A };

        /// <summary>
        /// Maps a normal-mode key to the text motion it triggers (shift distinguishes A/a and I/i).
        /// Returns null when the key is not a text-input motion (e.g. a bare <c>i</c>, which the
        /// generic insert handler in <see cref="InputHandler"/> takes care of).
        /// </summary>
        public static TextMotion? MapMotion(Keys key, bool shift)
        {
            switch (key)
            {
                case Keys.H: return TextMotion.Left;
                case Keys.L: return TextMotion.Right;
                case Keys.W: return TextMotion.NextWord;
                case Keys.B: return TextMotion.PrevWord;
                case Keys.E: return TextMotion.EndWord;
                case Keys.A:
                    // A (Shift+a) = insert at end of line; a = insert after the caret.
                    return shift ? TextMotion.InsertEnd : TextMotion.InsertAfter;
                case Keys.I:
                    // I (Shift+i) = insert at start of line; a bare i is the generic insert.
                    return shift ? TextMotion.InsertStart : (TextMotion?)null;
                default:
                    return null;
            }
        }

        public bool TryMove(Keys key)
        {
            // Physical shift state (GetAsyncKeyState, like the hook itself) — NOT WPF's
            // Keyboard.Modifiers, which lags behind injected keys because our hook callback runs
            // before WPF dispatches the Shift key-down message.
            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            TextMotion? motion = MapMotion(key, shift);
            if (motion == null)
            {
                return false;
            }

            // A focused WPF text box (modern tool windows), else a VS editor text view (the
            // Command Window / Immediate Window input is editor-hosted), else a WinForms text box
            // (legacy tool windows), else arrow-key fallback for h/l.
            if (FindFocusedTextBox() is System.Windows.Controls.TextBox wpf)
            {
                return ApplyMotionToBox(wpf.Text, wpf.CaretIndex,
                    caret => wpf.CaretIndex = caret,
                    key, motion.Value, styleCaret: true);
            }

            if (Keyboard.FocusedElement is IWpfTextView view)
            {
                try
                {
                    var snapshot = view.TextSnapshot;
                    string text = snapshot.GetText();
                    int caret = view.Caret.Position.BufferPosition.Position;
                    return ApplyMotionToBox(text, caret,
                        c => view.Caret.MoveTo(new SnapshotPoint(snapshot, Math.Max(0, Math.Min(c, snapshot.Length)))),
                        key, motion.Value, styleCaret: false);
                }
                catch
                {
                    // editor view read failed — fall through to the arrow fallback below
                }
            }

            if (FindFocusedWinFormsTextBox() is System.Windows.Forms.TextBoxBase win)
            {
                return ApplyMotionToBox(win.Text, win.SelectionStart,
                    caret => { win.SelectionStart = caret; win.SelectionLength = 0; },
                    key, motion.Value, styleCaret: false);
            }

            // No text box focused — fall back to arrow-key navigation for h/l so the window
            // still responds, but ignore the word/insert motions.
            if (motion == TextMotion.Left || motion == TextMotion.Right)
            {
                int vk = motion == TextMotion.Left ? KeyInjection.VK_LEFT : KeyInjection.VK_RIGHT;
                string focused = Keyboard.FocusedElement?.GetType().FullName ?? "null";
                IntPtr hwnd = GetFocus();
                Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}toolwindow-move key={key} -> arrow vk={vk} focused={focused} hwnd=0x{hwnd.ToInt64():X}");
                KeyInjection.Press(vk);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Runs a motion over <paramref name="text"/>/<paramref name="caret"/> using the shared
        /// navigator and applies the resulting caret through <paramref name="applyCaret"/>. Logs the
        /// motion so the E2E harness can assert caret positions.
        /// </summary>
        private bool ApplyMotionToBox(string text, int caret, Action<int> applyCaret, Keys key, TextMotion motion, bool styleCaret)
        {
            var navigator = new TextMotionNavigator();
            navigator.SetText(text);
            navigator.MoveTo(caret);

            switch (motion)
            {
                case TextMotion.Left: navigator.Left(); break;
                case TextMotion.Right: navigator.Right(); break;
                case TextMotion.NextWord: navigator.NextWord(); break;
                case TextMotion.PrevWord: navigator.PrevWord(); break;
                case TextMotion.EndWord: navigator.EndWord(); break;
                case TextMotion.InsertAfter: navigator.InsertAfter(); break;
                case TextMotion.InsertEnd: navigator.InsertEnd(); break;
                case TextMotion.InsertStart: navigator.InsertStart(); break;
                default: return false;
            }

            int newCaret = navigator.Caret;

            if (motion == TextMotion.InsertAfter || motion == TextMotion.InsertEnd || motion == TextMotion.InsertStart)
            {
                _isInputMode = true;
                applyCaret(newCaret);
                if (styleCaret)
                {
                    ApplyCaretStyle();
                }
                Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}textinput-enter-input {MotionName(motion)} caret={newCaret}");
            }
            else
            {
                applyCaret(newCaret);
                if (styleCaret)
                {
                    ApplyCaretStyle();
                }
                string sample = text.Length > 30 ? text.Substring(0, 30) : text;
                Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}text-motion key={key} caret={newCaret} len={text.Length} text='{sample}'");
            }
            return true;
        }

        private static string MotionName(TextMotion motion)
        {
            switch (motion)
            {
                case TextMotion.InsertAfter: return "after";
                case TextMotion.InsertEnd: return "end";
                case TextMotion.InsertStart: return "start";
                default: return motion.ToString();
            }
        }

        /// <summary>
        /// The WPF <see cref="System.Windows.Controls.TextBox"/> currently holding focus (walking
        /// the visual/logical tree up from <c>Keyboard.FocusedElement</c>), or null when the
        /// focused control is not a WPF text box.
        /// </summary>
        private static System.Windows.Controls.TextBox? FindFocusedTextBox()
        {
            var current = Keyboard.FocusedElement as System.Windows.DependencyObject;
            while (current != null)
            {
                if (current is System.Windows.Controls.TextBox box)
                {
                    return box;
                }
                current = GetParent(current);
            }
            return null;
        }

        /// <summary>
        /// The WinForms text box holding the Win32 keyboard focus (for legacy tool windows), or null.
        /// </summary>
        private static System.Windows.Forms.TextBoxBase? FindFocusedWinFormsTextBox()
        {
            try
            {
                IntPtr hwnd = GetFocus();
                return hwnd == IntPtr.Zero ? null : System.Windows.Forms.Control.FromHandle(hwnd) as System.Windows.Forms.TextBoxBase;
            }
            catch
            {
                return null;
            }
        }

        private static System.Windows.DependencyObject? GetParent(System.Windows.DependencyObject child)
        {
            try
            {
                var visual = VisualTreeHelper.GetParent(child);
                if (visual != null)
                {
                    return visual;
                }
            }
            catch
            {
                // not a visual — try the logical tree
            }
            try
            {
                return System.Windows.LogicalTreeHelper.GetParent(child);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Draws a block caret in normal mode and a thin line caret in insert mode: WPF
        /// <see cref="System.Windows.Controls.TextBox"/> inputs use a wide <c>CaretBrush</c>;
        /// editor-hosted inputs (Command Window / Immediate Window) get a block-caret adornment.
        /// </summary>
        private void ApplyCaretStyle()
        {
            if (FindFocusedTextBox() is System.Windows.Controls.TextBox box)
            {
                try
                {
                    box.CaretBrush = _isInputMode ? null : BlockCaretBrush;
                }
                catch
                {
                    // caret styling is best-effort
                }
            }

            if (Keyboard.FocusedElement is IWpfTextView view)
            {
                try
                {
                    BlockCaretAdornment.Attach(view).Active = !_isInputMode;
                }
                catch
                {
                    // the adornment must never crash the hook — block caret is best-effort
                }
            }
        }

        private static readonly DrawingBrush BlockCaretBrush = CreateBlockBrush();

        private static DrawingBrush CreateBlockBrush()
        {
            var rect = new System.Windows.Rect(0, 0, 8, 16);
            var drawing = new DrawingBrush(new GeometryDrawing(
                Brushes.Black, null, new RectangleGeometry(rect)));
            drawing.Freeze();
            return drawing;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}