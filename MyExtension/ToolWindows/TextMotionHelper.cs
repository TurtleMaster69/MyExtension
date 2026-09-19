using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Telescope;

namespace MyExtension
{
    /// <summary>
    /// Shared vim-caret behavior for WPF <see cref="System.Windows.Controls.TextBox"/> surfaces
    /// inside tool windows — the Solution Explorer search box and the text-input tool windows
    /// (Command Window, Find and Replace, ...). Finds the focused WPF text box, applies a
    /// normal-mode motion to its caret via the shared <see cref="TextMotionNavigator"/>, and toggles
    /// the <b>block</b> (normal mode) vs <b>line</b> (insert mode) caret.
    /// </summary>
    internal static class TextMotionHelper
    {
        /// <summary>White block caret brush for WPF TextBoxes in normal mode (visible on dark themes).</summary>
        public static readonly DrawingBrush BlockCaretBrush = CreateBlockBrush();

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
        /// Applies a normal-mode vim motion to the focused WPF TextBox, if any. Returns true when a
        /// box was focused and the key was a motion (h/l/w/b/e/a/A/I); <paramref name="isInputMode"/>
        /// is set true for the a/A/I insert placements. Logs the motion for the E2E harness.
        /// </summary>
        public static bool TryMoveFocusedTextBox(Keys key, ref bool isInputMode)
        {
            var box = FindFocusedTextBox();
            if (box == null)
            {
                return false;
            }

            // Physical shift state (GetAsyncKeyState, like the hook itself) — NOT WPF's
            // Keyboard.Modifiers, which lags behind injected keys.
            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            TextMotion? motion = TextInputToolWindowController.MapMotion(key, shift);
            if (motion == null)
            {
                return false;
            }

            var navigator = new TextMotionNavigator();
            navigator.SetText(box.Text);
            navigator.MoveTo(box.CaretIndex);

            switch (motion.Value)
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
            box.CaretIndex = newCaret;

            if (motion.Value is TextMotion.InsertAfter or TextMotion.InsertEnd or TextMotion.InsertStart)
            {
                isInputMode = true;
                ApplyCaretStyle(box, true);
                Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}textinput-enter-input {MotionName(motion.Value)} caret={newCaret}");
            }
            else
            {
                ApplyCaretStyle(box, false);
                string sample = box.Text.Length > 30 ? box.Text.Substring(0, 30) : box.Text;
                Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}text-motion key={key} caret={newCaret} len={box.Text.Length} text='{sample}'");
            }
            return true;
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

        /// <summary>Applies the caret style to the currently focused WPF TextBox, if any.</summary>
        public static void StyleFocusedTextBox(bool isInputMode)
        {
            if (FindFocusedTextBox() is System.Windows.Controls.TextBox box)
            {
                ApplyCaretStyle(box, isInputMode);
            }
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

        private static DrawingBrush CreateBlockBrush()
        {
            var rect = new System.Windows.Rect(0, 0, 8, 16);
            var drawing = new DrawingBrush(new GeometryDrawing(
                Brushes.White, null, new RectangleGeometry(rect)));
            drawing.Freeze();
            return drawing;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}