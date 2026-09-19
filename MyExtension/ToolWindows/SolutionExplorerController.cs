using System;
using System.Windows.Forms;

namespace MyExtension
{
    /// <summary>
    /// Controller for the Solution Explorer tool window. Extends the default hjkl tree navigation
    /// with the common file actions mapped to single keys:
    ///   - <c>o</c> / <c>Enter</c>  open the selected item
    ///   - <c>r</c>                  rename the selected item
    ///   - <c>m</c>                  move the selected item
    ///   - <c>a</c>                  add a new item to the selected project
    ///
    /// <para/>
    /// The open/rename actions are performed by injecting the native keys VS already understands
    /// (Enter / F2); move and add run the equivalent DTE commands. Every action is logged so the
    /// live E2E harness can assert it fired.
    ///
    /// <para/>
    /// <b>Threading:</b> all members are called on the UI thread only (same thread as the hook).
    /// </summary>
    internal sealed class SolutionExplorerController : IToolWindowController
    {
        private readonly Func<EnvDTE.DTE> _dteFactory;
        private bool _isInputMode;

        public SolutionExplorerController(Func<EnvDTE.DTE> dteFactory)
        {
            _dteFactory = dteFactory;
            // Solution Explorer is a tree, not a text-input surface: start in normal mode.
            _isInputMode = false;
        }

        public ToolWindowType Type => ToolWindowType.SolutionExplorer;

        public bool IsInputMode => _isInputMode;

        public void EnterInputMode()
        {
            _isInputMode = true;
            TextMotionHelper.StyleFocusedTextBox(true);
        }

        public void ExitInputMode()
        {
            _isInputMode = false;
            TextMotionHelper.StyleFocusedTextBox(false);
            // If we came out of input mode while the search box still had focus (i focused it),
            // return focus to the tree so j/k/h/l continue to navigate the tree, not type into
            // the search box. The native View.SolutionExplorer command refocuses the tree.
            if (TextMotionHelper.FindFocusedTextBox() != null)
            {
                ExecuteCommand("View.SolutionExplorer");
            }
        }

        /// <summary>The non-hjkl action keys this controller handles in normal mode. w/b/e are vim
        /// text motions for the search box; when the tree (not the search box) is focused they are
        /// not consumed and fall through.</summary>
        public System.Collections.Generic.IReadOnlyCollection<Keys> ActionKeys { get; } =
            new System.Collections.Generic.List<Keys>
            {
                Keys.O,
                Keys.Enter,
                Keys.R,
                Keys.M,
                Keys.A,
                Keys.W,
                Keys.B,
                Keys.E,
            };

        public bool TryMove(Keys key)
        {
            // If a WPF TextBox (the Solution Explorer search box) is focused, the controller behaves
            // like a text-input window: h/l/w/b/e/a/A/I move the caret, and every other key falls
            // through so it types into the search box (no tree actions, no j/k arrow injection).
            if (TextMotionHelper.TryMoveFocusedTextBox(key, ref _isInputMode))
            {
                return true;
            }
            if (TextMotionHelper.FindFocusedTextBox() != null)
            {
                return false;
            }

            switch (key)
            {
                case Keys.I:
                    // i focuses the Solution Explorer search box (the "search text box"), the same
                    // way Ctrl+; does natively. Entering input mode afterwards lets the user type
                    // the query; Escape returns to normal tree navigation.
                    FocusSearchBox();
                    return true;
                case Keys.O:
                    OpenSelected();
                    return true;
                case Keys.Enter:
                    OpenSelected();
                    return true;
                case Keys.R:
                    RenameSelected();
                    return true;
                case Keys.M:
                    MoveSelected();
                    return true;
                case Keys.A:
                    AddItem();
                    return true;
                case Keys.H:
                    // Collapse the selected node's fold (Left arrow).
                    Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer collapse");
                    KeyInjection.Press(KeyInjection.VK_LEFT);
                    return true;
                case Keys.L:
                    // Expand the selected node's fold (Right arrow).
                    Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer expand");
                    KeyInjection.Press(KeyInjection.VK_RIGHT);
                    return true;
                default:
                    // j/k move up/down the tree.
                    int vk = KeyToArrowVk(key);
                    if (vk == 0)
                    {
                        return false;
                    }
                    Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}toolwindow-move key={key} -> arrow vk={vk}");
                    KeyInjection.Press(vk);
                    return true;
            }
        }

        private void OpenSelected()
        {
            // Enter is the native "open selected item" key in the Solution Explorer tree.
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer open");
            KeyInjection.Press(KeyInjection.VK_RETURN);
        }

        /// <summary>
        /// Focuses the Solution Explorer search box via the native <c>Window.SolutionExplorerSearch</c>
        /// command (Ctrl+;), then enters input mode so the user can type the query. The command is
        /// executed through DTE because the search box is part of the tool window chrome, not the
        /// tree; we cannot tab/focus into it directly.
        /// </summary>
        private void FocusSearchBox()
        {
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer search-focus");
            ExecuteCommand("Window.SolutionExplorerSearch");
            EnterInputMode();
            // TryMove(Keys.I) returns true, so InputHandler's generic tool-window branch never
            // logs its enter-input line — emit the same contract here so the E2E harness's
            // "i entered tool-window input mode" assertion stays stable.
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}toolwindow-enter-input");
        }

        private void RenameSelected()
        {
            // F2 is the native rename shortcut in the Solution Explorer tree.
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer rename");
            KeyInjection.Press(KeyInjection.VK_F2);
        }

        private void MoveSelected()
        {
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer move");
            ExecuteCommand("SolutionExplorer.Move");
        }

        private void AddItem()
        {
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer add");
            ExecuteCommand("SolutionExplorer.AddItem");
        }

        private void ExecuteCommand(string command)
        {
            try
            {
                _dteFactory()?.ExecuteCommand(command, string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}Command '{command}' failed: {ex.Message}");
            }
        }

        private static int KeyToArrowVk(Keys key)
        {
            switch (key)
            {
                case Keys.J: return KeyInjection.VK_DOWN;
                case Keys.K: return KeyInjection.VK_UP;
                default: return 0;
            }
        }
    }
}