using CardinalNavigation;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Telescope;

namespace MyExtension
{
    /// <summary>
    /// The key-routing layer: turns a single key-down (from <see cref="GlobalKeyboardHook"/>) into
    /// "handled + swallow" or "not handled + pass through", and dispatches to the appropriate
    /// feature (window navigation, popup navigation, tool-window navigation, or a leader command).
    ///
    /// <para/>
    /// <b>Two kinds of bindings</b> (loaded from JSON by <see cref="KeybindingConfig"/>):
    ///   - <b>Leader sequences</b>: bare keys matched only after the leader key, e.g. <c>w</c> →
    ///     save, <c>f f</c> → Go To File. These are the LazyVim-style chords.
    ///   - <b>Simple shortcuts</b>: a key plus Ctrl/Shift/Alt, matched directly, e.g. <c>Ctrl+H</c>.
    ///     They are distinguished from leader sequences by the <c>+</c> in their key string.
    ///
    /// <para/>
    /// <b>Leader state machine:</b> pressing the leader key sets <see cref="_leaderActive"/> and CPU
    /// records subsequent keys until they match a sequence (or become an invalid prefix, which
    /// resets). This is the classic "leader + prefix" input model reused from Vim/LazyVim.
    ///
    /// <para/>
    /// <b>Threading:</b> every branch ultimately touches VS state, so <see cref="HandleKey"/> must
    /// run on the UI thread (asserted at entry). The hook guarantees this by running its callback
    /// on the UI thread; the JoinableTaskFactory fallback covers the rare off-thread case.
    /// </summary>
    internal class InputHandler
    {
        private readonly AsyncPackage _package;
        private readonly VsVimIntegration _vsVim;
        private readonly PopupNavigation _popupNav;
        private readonly WindowManager _windowManager;
        private readonly TelescopeController _telescope;

        // Leader-sequence state: the keys typed since the leader key, and whether a sequence is
        // currently in progress. _leaderActive is volatile because the hook thread's cheap
        // pre-filter reads it (GetIsLeaderActive) while HandleKey mutates it on the UI thread.
        private readonly List<Keys> _currentSequence = new();
        private volatile bool _leaderActive = false;

        /// <summary>
        /// Thread-safe snapshot of whether a leader sequence is in progress. Read only by the
        /// hook thread's pre-filter: while true, every key must marshal to the UI thread so the
        /// sequence can be continued or broken there.
        /// </summary>
        public bool IsLeaderActive => _leaderActive;

        // The leader key itself (Space by default, user-configurable).
        private readonly Keys _leaderKey;

        // Leader sequences (matched only after the leader key): "W", "F,F", "B,D"...
        private readonly Dictionary<string, Action> _leaderBindings;

        // Simple modifier shortcuts (matched directly): "Ctrl+H", "Alt+X"...
        private readonly Dictionary<string, Action> _simpleBindings;

        public InputHandler(AsyncPackage package, TelescopeController telescope, WindowManager windowManager)
        {
            _package = package;
            _telescope = telescope ?? throw new ArgumentNullException(nameof(telescope));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            _vsVim = new VsVimIntegration(package);
            _popupNav = new PopupNavigation(_windowManager);

            var config = KeybindingConfig.Load();
            _leaderKey = config.LeaderKey;
            (_leaderBindings, _simpleBindings) = BuildBindings(config.Bindings);
        }

        /// <summary>
        /// Converts the config's <c>sequence-&gt;action-name</c> map into two dictionaries of
        /// ready-to-run delegates, split by whether the key is a modifier shortcut (contains "+")
        /// or a leader sequence (everything else). This split is what keeps a bare <c>e</c> from
        /// firing the <c>e</c>-after-leader binding without the leader key being pressed first.
        /// </summary>
        private (Dictionary<string, Action> leader, Dictionary<string, Action> simple) BuildBindings(Dictionary<string, string> namedBindings)
        {
            var leader = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
            var simple = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in namedBindings)
            {
                var action = ResolveAction(pair.Value);
                if (action == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[NeoVisual] Unknown action '{pair.Value}' for binding '{pair.Key}' - ignored.");
                    continue;
                }

                if (pair.Key.Contains("+"))
                {
                    simple[pair.Key] = action;
                }
                else
                {
                    leader[pair.Key] = action;
                }
            }

            return (leader, simple);
        }

        /// <summary>
        /// Maps a named action string to a delegate. Built-in actions (cardinal navigation) are
        /// a fixed switch; the generic <c>"command:Name"</c> form runs any VS command by name,
        /// which is how the LazyVim-style leader bindings (<c>w</c> -> save, etc.) are wired into
        /// the extension without a code change per command.
        /// </summary>
        private Action ResolveAction(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            string lower = trimmed.ToLowerInvariant();

            switch (lower)
            {
                case "navigate-left": return () => Navigate(CardinalNavigationConstants.LEFT);
                case "navigate-right": return () => Navigate(CardinalNavigationConstants.RIGHT);
                case "navigate-up": return () => Navigate(CardinalNavigationConstants.UP);
                case "navigate-down": return () => Navigate(CardinalNavigationConstants.DOWN);
                case "telescope": return () => OpenTelescope();
                default:
                    break;
            }

            const string commandPrefix = "command:";
            if (lower.StartsWith(commandPrefix, StringComparison.Ordinal))
            {
                string command = trimmed.Substring(commandPrefix.Length).Trim();
                if (command.Length == 0)
                {
                    return null;
                }
                return () => ExecuteVsCommand(command);
            }

            return null;
        }

        /// <summary>
        /// Runs an arbitrary Visual Studio command by name via the top-level automation object
        /// (DTE). DTE must be used on the UI thread; failures (e.g. a command not available in
        /// this VS configuration) are logged and swallowed rather than crashing the hook.
        /// </summary>
        private void ExecuteVsCommand(string command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = CardinalNavigation.UtilityMethods.GetDTE(_package);
                dte.ExecuteCommand(command, string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] Command '{command}' failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Entry point called by the hook for every key-down. Returns true if the key should be
        /// swallowed (the hook drops it), false to let it pass through to VS/VsVim. The branches
        /// are ordered so the cheapest/common cases lead and the mode checks only run for the few
        /// key chords that actually need editor state.
        /// </summary>
        public bool HandleKey(Keys key, bool ctrl, bool shift, bool alt)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // While the Telescope overlay is open it owns keyboard focus. Every key must pass
            // through to the overlay's filter box / key handlers — the extension must NOT act on
            // Space (leader), hjkl, or Escape. Returning false lets the key reach the overlay.
            if (_telescope.IsOpen)
            {
                return false;
            }

            // Escape: exits a tool window's input mode first (back to normal), otherwise cancels
            // an in-progress leader sequence (and otherwise passes through).
            if (key == Keys.Escape)
            {
                if (ExitToolWindowInputMode())
                {
                    return true;
                }
                ResetSequence();
                return false;
            }

            // Deliberately NO handling of the bare Ctrl key. Intercepting it corrupted every
            // Ctrl+chord while IntelliSense completion was open (Ctrl+W arrived as a bare W,
            // etc.), so the Ctrl key-down is always passed through untouched.

            // Ctrl+N / Ctrl+P: navigate the focused list/popup (completion, quick actions, peek)
            // by injecting a Down/Up arrow key. Only meaningful in a document (code editor)
            // context; popup navigation gates on that itself.
            if ((key == Keys.N || key == Keys.P) && ctrl && !shift && !alt)
            {
                return _popupNav.TryNavigate(down: key == Keys.N);
            }

            // Tool window: route through its controller's normal/input mode.
            if (_windowManager.IsToolWindow)
            {
                var controller = _windowManager.CurrentController;
                if (controller != null)
                {
                    // Input mode: typing passes through. Only Escape (handled above) exits it.
                    if (controller.IsInputMode)
                    {
                        return false;
                    }

                    // Normal mode: i enters input mode; hjkl move the focused surface.
                    if (!ctrl && !shift && !alt)
                    {
                        if (key == Keys.I)
                        {
                            controller.EnterInputMode();
                            return true;
                        }

                        if (!_leaderActive &&
                            (key == Keys.H || key == Keys.J || key == Keys.K || key == Keys.L) &&
                            controller.TryMove(key))
                        {
                            return true;
                        }
                    }
                }
            }

            // 1. Leader key pressed: begin a sequence, unless the user is typing — then Space
            //    types a literal space. "Typing" means a tool window in input mode, or the VsVim
            //    editor in insert/replace mode.
            if (key == _leaderKey && !ctrl && !shift && !alt)
            {
                if (IsTyping())
                {
                    return false;
                }

                _leaderActive = true;
                _currentSequence.Clear();
                return true; // consume the leader key itself
            }

            // 2. Building a leader sequence: append the key and match against leader bindings,
            //    using prefix detection to keep waiting for multi-key sequences like "f f".
            if (_leaderActive)
            {
                _currentSequence.Add(key);

                string sequence = string.Join(",", _currentSequence.Select(KeyToString));

                if (_leaderBindings.TryGetValue(sequence, out var action))
                {
                    action();
                    ResetSequence();
                    return true;
                }

                bool isPrefix = _leaderBindings.Keys.Any(k => k.StartsWith(sequence + ",", StringComparison.OrdinalIgnoreCase));

                if (!isPrefix)
                {
                    // Typed something that matches no sequence and prefixes none: abort.
                    ResetSequence();
                    return false;
                }

                return true; // still waiting for more keys in the sequence
            }

            // 3. Simple modifier shortcut (e.g. Ctrl+H), matched directly against the key name.
            string simple = BuildSimpleKey(key, ctrl, shift, alt);

            if (_simpleBindings.TryGetValue(simple, out var simpleAction))
            {
                simpleAction();
                return true;
            }

            return false;
        }

        private void ResetSequence()
        {
            _leaderActive = false;
            _currentSequence.Clear();
        }

        /// <summary>
        /// If the focused tool window is in input mode, exits it (back to normal) and returns
        /// true so the Escape key is swallowed. Returns false otherwise.
        /// </summary>
        private bool ExitToolWindowInputMode()
        {
            if (_windowManager.IsToolWindow)
            {
                var controller = _windowManager.CurrentController;
                if (controller?.IsInputMode == true)
                {
                    controller.ExitInputMode();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when the user is typing, so the leader key must type a literal space instead of
        /// starting a leader sequence: a tool window in input mode, or the VsVim editor in
        /// insert/replace mode.
        /// </summary>
        private bool IsTyping()
        {
            if (_windowManager.IsToolWindow)
            {
                return _windowManager.CurrentController?.IsInputMode == true;
            }
            return _vsVim.IsInTypingMode();
        }

        /// <summary>Builds the canonical shortcut string, e.g. Ctrl+H, Shift+F4, Alt+X.</summary>
        private string BuildSimpleKey(Keys key, bool ctrl, bool shift, bool alt)
        {
            var parts = new List<string>();

            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (alt) parts.Add("Alt");
            parts.Add(KeyToString(key));

            return string.Join("+", parts);
        }

        /// <summary>
        /// Friendly, stable key name used in the config file. A few non-alphanumeric keys have
        /// awkward enum names (e.g. the "/" key maps to <c>Keys.OemQuestion</c>/<c>Oem2</c>,
        /// "+" to <c>Keys.Oemplus</c>), so we map those to their printable character for a
        /// readable default config.
        /// </summary>
        private static string KeyToString(Keys key)
        {
            switch (key)
            {
                case Keys.OemQuestion: return "/";   // 191, same value as Keys.Oem2
                case Keys.Oemplus: return "+";       // 187
                case Keys.OemMinus: return "-";      // 189
                default: return key.ToString();
            }
        }

        /// <summary>Performs Cardinal window navigation in a compass direction (see WindowMatrix).</summary>
        private void Navigate(char direction)
        {
            // Rebuild the window matrix each navigation (windows can be resized/opened/closed),
            // but source the active window from WindowManager's cached frame rather than re-deriving
            // it from DTE.
            var wm = new WindowMatrix(_package, _windowManager.CurrentWindow);
            wm.NavigateInDirection(direction);
        }

        /// <summary>
        /// Opens the Telescope overlay with the "Files" finder, centered over the VS main window.
        /// The <see cref="FileFinder"/> gathers its candidates via DTE on the UI thread.
        /// </summary>
        private void OpenTelescope()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = CardinalNavigation.UtilityMethods.GetDTE(_package);
                var centerRect = GetWindowRect(dte.MainWindow.HWnd);
                _telescope.Open("Files", centerRect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] Failed to open Telescope: {ex.Message}");
            }
        }

        /// <summary>Retrieves the on-screen rectangle (in pixels) of a window handle.</summary>
        private static System.Drawing.Rectangle? GetWindowRect(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !GetWindowRectNative(hwnd, out var rect))
            {
                return null;
            }
            return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRectNative(IntPtr hWnd, out NativeRect rect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
