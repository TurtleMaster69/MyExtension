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

        // R18: the tool-window motions (h/l/w/b/e/a/A/I) only need the text around the caret, so
        // the navigator runs over a caret-relative slice instead of the whole buffer — avoids the
        // O(n) GetText() copy + LineIndex build per motion key in a long console buffer.
        private const int MotionSliceRadius = 4096;

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
        public static bool TryMoveFocusedSurface(Keys key, out bool enteredInputMode)
        {
            return TryMoveFocusedSurface(key, out enteredInputMode, FindFocusedTextBox());
        }

        /// <summary>
        /// Applies a normal-mode vim motion to the focused text surface, if any: a WPF TextBox
        /// (modern tool windows), else a VS editor text view (the Command Window / Immediate Window
        /// input is editor-hosted), else a WinForms text box (legacy tool windows), else an
        /// arrow-key fallback for h/l. Returns true when a surface was focused and the key was a
        /// motion (h/l/w/b/e/a/A/I); <paramref name="isInputMode"/> is set true for the a/A/I insert
        /// placements. Logs the motion for the E2E harness. <paramref name="focusedBox"/> is the
        /// already-resolved WPF text box (R17 — the caller resolves it once so the visual tree is
        /// not walked twice per routed key).
        /// </summary>
        public static bool TryMoveFocusedSurface(Keys key, out bool enteredInputMode, System.Windows.Controls.TextBox? focusedBox)
        {
            enteredInputMode = false;
            // Physical shift state (GetAsyncKeyState, like the hook itself) — NOT WPF's
            // Keyboard.Modifiers, which lags behind injected keys because our hook callback runs
            // before WPF dispatches the Shift key-down message.
            bool shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
            TextMotion? motion = MapMotion(key, shift);
            if (motion == null)
            {
                return false;
            }

            if (focusedBox != null)
            {
                // N19: caret-relative slice (R18) for the WPF path too — the motions only need the
                // text around the caret, so the LineIndex build + motion run over a bounded slice.
                // A5: WPF TextBox exposes no bounded read (only the full .Text property), so the
                // O(n) copy is unavoidable here — the slice bounds the LineIndex build, not the copy.
                string fullText = focusedBox.Text;
                int caret = focusedBox.CaretIndex;
                int fullLength = fullText.Length;
                int start = Math.Max(0, caret - MotionSliceRadius);
                int length = Math.Min(fullLength - start, MotionSliceRadius * 2);
                string text = fullText.Substring(start, length);
                return ApplyMotionToBox(text, caret - start, fullLength, start, Sample(fullText),
                    c => focusedBox.CaretIndex = c,
                    key, motion.Value, styleCaret: true, focusedBox, null, out enteredInputMode,
                    pos => LineEndIn(fullText, pos));
            }

            if (Keyboard.FocusedElement is IWpfTextView view)
            {
                try
                {
                    var snapshot = view.TextSnapshot;
                    int caret = view.Caret.Position.BufferPosition.Position;
                    int fullLength = snapshot.Length;
                    // R18: fetch a caret-relative slice instead of the whole buffer — the
                    // tool-window motions only need the text around the caret, so the O(n)
                    // GetText() copy + LineIndex build run over a bounded slice, not the full
                    // buffer.
                    int start = Math.Max(0, caret - MotionSliceRadius);
                    int length = Math.Min(fullLength - start, MotionSliceRadius * 2);
                    string text = snapshot.GetText(start, length);
                    return ApplyMotionToBox(text, caret - start, fullLength, start, Sample(snapshot),
                        c => view.Caret.MoveTo(new SnapshotPoint(snapshot, Math.Max(0, Math.Min(c, snapshot.Length)))),
                        key, motion.Value, styleCaret: false, null, view, out enteredInputMode,
                        // N23: A must land at the true line end, not the caret-relative slice boundary.
                        pos => snapshot.GetLineFromPosition(Math.Max(0, Math.Min(pos, snapshot.Length))).End.Position);
                }
                catch
                {
                    // editor view read failed — fall through to the arrow fallback below
                }
            }

            if (FindFocusedWinFormsTextBox() is System.Windows.Forms.TextBoxBase win)
            {
                // N19: caret-relative slice for the WinForms path too.
                string fullText = win.Text;
                int caret = win.SelectionStart;
                int fullLength = fullText.Length;
                int start = Math.Max(0, caret - MotionSliceRadius);
                int length = Math.Min(fullLength - start, MotionSliceRadius * 2);
                string text = fullText.Substring(start, length);
                return ApplyMotionToBox(text, caret - start, fullLength, start, Sample(fullText),
                    c => { win.SelectionStart = c; win.SelectionLength = 0; },
                    key, motion.Value, styleCaret: false, null, null, out enteredInputMode,
                    pos => LineEndIn(fullText, pos));
            }

            // No text box focused — fall back to arrow-key navigation for h/l so the window
            // still responds, but ignore the word/insert motions.
            if (motion == TextMotion.Left || motion == TextMotion.Right)
            {
                // m9: single-source the key->arrow VK mapping (KeyToArrowVk).
                int vk = GeneralToolWindowController.KeyToArrowVk(motion == TextMotion.Left ? Keys.H : Keys.L);
                // n10: align with TryMoveArrow's bare contract (no focused=/hwnd= suffix).
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}toolwindow-move key={key} -> arrow vk={vk}");
                KeyInjection.Press(vk);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Runs a motion over <paramref name="text"/>/<paramref name="caret"/> using the shared
        /// navigator and applies the resulting caret through <paramref name="applyCaret"/>. Logs the
        /// motion so the E2E harness can assert caret positions. <paramref name="focusedBox"/> is the
        /// already-resolved WPF text box (m15 — no second visual-tree walk for the caret style);
        /// <paramref name="editorView"/> is the already-resolved VS editor view, styled on insert
        /// placements so the block caret doesn't persist in insert mode (m24). R18: <paramref name="text"/>
        /// may be a caret-relative slice of the buffer — <paramref name="fullLength"/> (for the
        /// <c>len=</c> diagnostic), <paramref name="offset"/> (to map the resulting caret back to
        /// full-buffer coordinates) and <paramref name="sample"/> (the first 30 chars of the full
        /// buffer, for the <c>text=</c> diagnostic) keep the emitted log line byte-identical.
        /// </summary>
        private static bool ApplyMotionToBox(string text, int caret, int fullLength, int offset, string sample, Action<int> applyCaret, Keys key, TextMotion motion, bool styleCaret, System.Windows.Controls.TextBox? focusedBox, IWpfTextView? editorView, out bool enteredInputMode, Func<int, int>? lineEndAt = null)
        {
            enteredInputMode = false;
            var navigator = new TextMotionNavigator();
            navigator.SetText(text);
            navigator.MoveTo(caret);

            if (!TextMotionDispatcher.Apply(motion, navigator, out CaretPlacement? insertPlacement))
            {
                return false;
            }

            // InsertStart (I) must land at the true start of the buffer (position 0), not the
            // slice start — the navigator only knows the caret-relative slice, so map it
            // explicitly. N23: InsertEnd (A) must land at the true line end (resolved from the
            // full buffer), not the caret-relative slice boundary. All other motions map back via
            // the slice offset.
            int newCaret;
            if (insertPlacement == CaretPlacement.Start)
            {
                newCaret = 0;
            }
            else if (insertPlacement == CaretPlacement.End && lineEndAt != null)
            {
                newCaret = lineEndAt(caret + offset);
            }
            else
            {
                newCaret = navigator.Caret + offset;
            }

            if (insertPlacement != null)
            {
                enteredInputMode = true;
                applyCaret(newCaret);
                if (styleCaret && focusedBox != null)
                {
                    ApplyCaretStyle(focusedBox, true);
                }
                if (editorView != null)
                {
                    ApplyEditorViewCaret(editorView, true);
                }
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}textinput-enter-input {MotionName(motion)} caret={newCaret}");
            }
            else
            {
                applyCaret(newCaret);
                if (styleCaret && focusedBox != null)
                {
                    ApplyCaretStyle(focusedBox, false);
                }
                if (editorView != null)
                {
                    ApplyEditorViewCaret(editorView, false);
                }
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}text-motion key={key} caret={newCaret} len={fullLength} text='{sample}'");
            }
            return true;
        }

        /// <summary>N23: the true end of the line containing <paramref name="position"/> in the full
        /// buffer (the next <c>\n</c>, or the buffer end) — used to map <c>A</c> past the
        /// caret-relative slice boundary.</summary>
        private static int LineEndIn(string text, int position)
        {
            if (position < 0) position = 0;
            if (position > text.Length) position = text.Length;
            int nl = text.IndexOf('\n', position);
            return nl < 0 ? text.Length : nl;
        }

        /// <summary>The first 30 chars of a full text buffer (the <c>text=</c> log sample), with
        /// newlines/control chars replaced by spaces so the sample never splits the log line (R39).</summary>
        private static string Sample(string text)
        {
            return DiagnosticLog.SanitizeText(text.Length > 30 ? text.Substring(0, 30) : text);
        }

        /// <summary>The first 30 chars of an editor snapshot (the <c>text=</c> log sample), read
        /// without materializing the whole buffer.</summary>
        private static string Sample(ITextSnapshot snapshot)
        {
            int length = Math.Min(30, snapshot.Length);
            return DiagnosticLog.SanitizeText(snapshot.GetText(0, length));
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

        /// <summary>Sets block (normal) vs line (insert) caret on a WPF TextBox (m35 — delegates to
        /// the shared <see cref="BlockCaretStyle.ApplyCaretStyle"/>).</summary>
        public static void ApplyCaretStyle(System.Windows.Controls.TextBox box, bool isInputMode, System.Windows.Media.Brush? lineCaretBrush = null)
        {
            BlockCaretStyle.ApplyCaretStyle(box, isInputMode, lineCaretBrush);
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

        /// <summary>N71: styles the already-resolved WPF text box (no second visual-tree walk) plus
        /// the focused editor view.</summary>
        public static void StyleFocusedSurface(bool isInputMode, System.Windows.Controls.TextBox? focusedBox)
        {
            if (focusedBox != null)
            {
                ApplyCaretStyle(focusedBox, isInputMode);
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

        /// <summary>
        /// The shared visual/logical-tree parent walk (m18): tries the visual tree first, then the
        /// logical tree. Used by <see cref="FindFocusedTextBox"/> and <see cref="WindowManager"/>.
        /// </summary>
        internal static System.Windows.DependencyObject? GetParent(System.Windows.DependencyObject child)
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
