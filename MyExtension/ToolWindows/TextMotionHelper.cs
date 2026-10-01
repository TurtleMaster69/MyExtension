using System;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using MyExtension.Adornments;
using MyExtension.Hooks;
using Telescope.Logging;
using Telescope.Overlay;

namespace MyExtension.ToolWindows
{
    /// <summary>
    /// Shared vim-caret behavior for the tool-window text surfaces — the Solution Explorer search
    /// box and the text-input tool windows (Command Window, Find and Replace, ...). Finds the
    /// focused surface (WPF text box, VS editor text view, or WinForms text box), applies a
    /// normal-mode motion to its caret via the shared <see cref="TextMotionNavigator"/>, and toggles
    /// the <b>block</b> (normal mode) vs <b>line</b> (insert mode) caret.
    /// </summary>
    internal static class TextMotionHelper
    {
        /// <summary>White block caret brush for WPF TextBoxes in normal mode (visible on dark themes).</summary>
        public static readonly DrawingBrush BlockCaretBrush = BlockCaretStyle.CreateBlockBrush();

        /// <summary>The WPF TextBox currently holding focus (walking the visual/logical tree), or null.
        /// Wrapped so it degrades to "no text box" on non-STA threads (hermetic unit tests run on
        /// the MTA, where <c>Keyboard.FocusedElement</c> throws).</summary>
        public static System.Windows.Controls.TextBox? FindFocusedTextBox()
        {
            try
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
            }
            catch
            {
                // non-STA / no WPF dispatcher — treat as no focused text box
            }
            return null;
        }

        /// <summary>
        /// Maps a normal-mode key to the text motion it triggers (shift distinguishes A/a and I/i).
        /// Returns null when the key is not a text-input motion (e.g. a bare <c>i</c>, which the
        /// generic insert handler in <see cref="InputHandler"/> takes care of). Delegates to the
        /// shared <see cref="TextMotionDispatcher"/> (M19 — the single key→motion table).
        /// </summary>
        public static TextMotion? MapMotion(Keys key, bool shift)
        {
            return TextMotionDispatcher.MapKey(key, shift);
        }

        /// <summary>
        /// Applies a normal-mode vim motion to the focused text surface, if any: a WPF TextBox
        /// (modern tool windows), else a VS editor text view (the Command Window / Immediate Window
        /// input is editor-hosted), else a WinForms text box (legacy tool windows), else an
        /// arrow-key fallback for h/l. Returns true when a surface was focused and the key was a
        /// motion (h/l/w/b/e/a/A/I); <paramref name="isInputMode"/> is set true for the a/A/I insert
        /// placements. Logs the motion for the E2E harness.
        /// </summary>
        public static bool TryMoveFocusedSurface(Keys key, ref bool isInputMode)
        {
            // Physical shift state (GetAsyncKeyState, like the hook itself) — NOT WPF's
            // Keyboard.Modifiers, which lags behind injected keys because our hook callback runs
            // before WPF dispatches the Shift key-down message.
            bool shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
            TextMotion? motion = MapMotion(key, shift);
            if (motion == null)
            {
                return false;
            }

            if (FindFocusedTextBox() is System.Windows.Controls.TextBox wpf)
            {
                return ApplyMotionToBox(wpf.Text, wpf.CaretIndex,
                    caret => wpf.CaretIndex = caret,
                    key, motion.Value, styleCaret: true, ref isInputMode);
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
                        key, motion.Value, styleCaret: false, ref isInputMode);
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
                    key, motion.Value, styleCaret: false, ref isInputMode);
            }

            // No text box focused — fall back to arrow-key navigation for h/l so the window
            // still responds, but ignore the word/insert motions.
            if (motion == TextMotion.Left || motion == TextMotion.Right)
            {
                int vk = motion == TextMotion.Left ? KeyInjection.VK_LEFT : KeyInjection.VK_RIGHT;
                string focused = Keyboard.FocusedElement?.GetType().FullName ?? "null";
                IntPtr hwnd = NativeMethods.GetFocus();
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}toolwindow-move key={key} -> arrow vk={vk} focused={focused} hwnd=0x{hwnd.ToInt64():X}");
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
        private static bool ApplyMotionToBox(string text, int caret, Action<int> applyCaret, Keys key, TextMotion motion, bool styleCaret, ref bool isInputMode)
        {
            var navigator = new TextMotionNavigator();
            navigator.SetText(text);
            navigator.MoveTo(caret);

            if (!TextMotionDispatcher.Apply(motion, navigator, out CaretPlacement? insertPlacement))
            {
                return false;
            }

            int newCaret = navigator.Caret;

            if (insertPlacement != null)
            {
                isInputMode = true;
                applyCaret(newCaret);
                if (styleCaret && FindFocusedTextBox() is System.Windows.Controls.TextBox focusedBox)
                {
                    ApplyCaretStyle(focusedBox, true);
                }
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}textinput-enter-input {MotionName(motion)} caret={newCaret}");
            }
            else
            {
                applyCaret(newCaret);
                if (styleCaret && FindFocusedTextBox() is System.Windows.Controls.TextBox focusedBox)
                {
                    ApplyCaretStyle(focusedBox, false);
                }
                string sample = text.Length > 30 ? text.Substring(0, 30) : text;
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}text-motion key={key} caret={newCaret} len={text.Length} text='{sample}'");
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

        /// <summary>Sets block (normal) vs line (insert) caret on a WPF TextBox.</summary>
        public static void ApplyCaretStyle(System.Windows.Controls.TextBox box, bool isInputMode)
        {
            try
            {
                box.CaretBrush = isInputMode ? null : BlockCaretBrush;
            }
            catch
            {
                // caret styling is best-effort
            }
        }

        /// <summary>
        /// Applies the caret style to the currently focused surface: a WPF TextBox via
        /// <see cref="ApplyCaretStyle"/> and a VS editor text view via <see cref="ApplyEditorViewCaret"/>.
        /// </summary>
        public static void StyleFocusedSurface(bool isInputMode)
        {
            if (FindFocusedTextBox() is System.Windows.Controls.TextBox box)
            {
                ApplyCaretStyle(box, isInputMode);
            }
            if (Keyboard.FocusedElement is IWpfTextView view)
            {
                ApplyEditorViewCaret(view, isInputMode);
            }
        }

        /// <summary>
        /// Toggles the block-caret adornment on a VS editor text view: active in normal mode,
        /// inactive (native line caret) in insert mode. Best-effort — the adornment must never
        /// crash the hook.
        /// </summary>
        public static void ApplyEditorViewCaret(IWpfTextView view, bool isInputMode)
        {
            try
            {
                BlockCaretAdornment.Attach(view).Active = !isInputMode;
            }
            catch
            {
                // the adornment must never crash the hook — block caret is best-effort
            }
        }

        /// <summary>
        /// The WinForms text box holding the Win32 keyboard focus (for legacy tool windows), or null.
        /// </summary>
        private static System.Windows.Forms.TextBoxBase? FindFocusedWinFormsTextBox()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetFocus();
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
                var visual = System.Windows.Media.VisualTreeHelper.GetParent(child);
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
    }
}
