using MyExtension.Navigation;
using MyExtension.Package;
using MyExtension.ToolWindows;
using MyExtension.Vim;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Telescope.Controller;
using Telescope.Logging;

namespace MyExtension.Input
{
    /// <summary>
    /// The key-routing layer: turns a single key-down (from <see cref="GlobalKeyboardHook"/>) into
    /// "handled + swallow" or "not handled + pass through", and dispatches to the appropriate
    /// feature (window navigation, popup navigation, tool-window navigation, or a leader command).
    ///
    /// <para/>
    /// <b>Two kinds of bindings</b> (loaded from JSON by <see cref="KeybindingConfig"/>):
    ///   - <b>Leader sequences</b>: bare keys matched only after the leader key, e.g. <c>f,f</c> →
    ///     Go To File. These are the LazyVim-style chords.
    ///   - <b>Simple shortcuts</b>: a key plus Ctrl/Shift/Alt, matched directly, e.g. <c>Ctrl+H</c>.
    ///     They are distinguished from leader sequences by the <c>+</c> in their key string.
    ///
    /// <para/>
    /// <b>Leader state machine:</b> pressing the leader key sets <see cref="LeaderSequenceMatcher"/>
    /// active and records subsequent keys until they match a sequence (or become an invalid prefix,
    /// which resets). This is the classic "leader + prefix" input model reused from Vim/LazyVim.
    ///
    /// <para/>
    /// <b>Threading:</b> every branch ultimately touches VS state, so <see cref="HandleKey"/> must
    /// run on the UI thread (asserted at entry). The hook guarantees this by running its callback
    /// on the UI thread; the JoinableTaskFactory fallback covers the rare off-thread case.
    /// </summary>
    internal class InputHandler
    {
        /// <summary>
        /// The default controller's key set (hjkl + i) — the keys every tool-window controller
        /// acts on in normal mode. Shared by the hook's cheap pre-filter and <see cref="HandleKey"/>
        /// so the set is defined once instead of duplicated.
        /// </summary>
        internal static readonly IReadOnlyCollection<Keys> DefaultControllerKeys =
            new[] { Keys.H, Keys.J, Keys.K, Keys.L, Keys.I };

        private readonly AsyncPackage _package;
        private readonly VimModeTracker _vsVim;
        private readonly PopupNavigation _popupNav;
        private readonly WindowManager _windowManager;
        private readonly TelescopeController _telescope;
        private readonly TelescopeLauncher _launcher;

        // Leader-sequence state machine (pure, unit-tested): owns the leader key start, sequence
        // building, binding match, prefix detection, and abort.
        private readonly LeaderSequenceMatcher _leaderMatcher;

        // Simple-shortcut matcher (pure, unit-tested): builds the canonical shortcut string
        // (e.g. "Ctrl+H") from a key + modifiers and looks it up in the simple bindings.
        private readonly SimpleShortcutMatcher _simpleMatcher;

        // M2: the stale-toolwindow sentinel file is re-stat'd at most once per bounded interval
        // (not on every key-down) so IsKeyOfInterest stays cheap when NEOVISUAL_LOG_DIR is set.
        private DateTime _lastSentinelRefresh = DateTime.MinValue;
        private static readonly TimeSpan SentinelRefreshInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Snapshot of whether a leader sequence is in progress. Read only by the hook thread's
        /// cheap pre-filter: while true, every key must marshal to the UI thread so the sequence
        /// can be continued or broken there.
        /// </summary>
        public bool IsLeaderActive => _leaderMatcher.IsActive;

        /// <summary>
        /// Whether the event-driven editor-focus flag should veto tool-window routing. The flag is
        /// reliable for document editors but NOT for shell-routed text-input tool windows (Command
        /// Window, Find, ...): their focus transitions never reach <see cref="VimModeTracker"/> (its
        /// <c>IWpfTextViewCreationListener</c> is not created for them), so the flag can remain stuck
        /// <c>true</c> while such a window owns the keyboard. A tool window that is a text-input
        /// surface, or whose controller is in input mode, therefore genuinely owns its surface and is
        /// trusted regardless of the flag. Navigation tool windows (Solution Explorer) still honor
        /// it, which is what stops their action keys leaking into a focused editor.
        /// </summary>
        private bool EditorFocusedVeto =>
            _vsVim.IsEditorFocused && !OwnsKeyboard;

        /// <summary>
        /// M5: the single-source keyboard-ownership exemption, computed once from the cached
        /// <c>_windowManager.IsTextInputType</c> (not the recomputed
        /// <c>GeneralToolWindowController.IsTextInputType</c>). Shared by <see cref="EditorFocusedVeto"/>
        /// and the routing helper below so all routing formulations read one source.
        /// </summary>
        private bool OwnsKeyboard =>
            FocusGuard.OwnsKeyboard(
                _windowManager.CurrentController?.IsInputMode == true,
                _windowManager.IsTextInputType,
                _windowManager.TextInputSurfaceFocused);

        /// <summary>
        /// m7: the single tool-window routing decision, hoisted from the three identical
        /// <c>FocusGuard.ShouldRouteToolWindowKey</c> call sites (HandleKey, IsKeyOfInterest,
        /// ExitToolWindowInputMode).
        /// </summary>
        private bool ShouldRouteToolWindowKey()
            => FocusGuard.ShouldRouteToolWindowKey(
                _windowManager.IsToolWindow,
                _vsVim.IsEditorFocused,
                OwnsKeyboard);

        // The leader key itself (Space by default, user-configurable).
        private readonly Keys _leaderKey;

        // Leader sequences (matched only after the leader key): "w", "f,f", "b,d"...
        private readonly Dictionary<string, Action> _leaderBindings;

        // Simple modifier shortcuts (matched directly): "Ctrl+H", "Alt+X"...
        private readonly Dictionary<string, Action> _simpleBindings;

        public InputHandler(AsyncPackage package, TelescopeController telescope, WindowManager windowManager, TelescopeLauncher launcher)
        {
            _package = package;
            _telescope = telescope ?? throw new ArgumentNullException(nameof(telescope));
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

            // The Vim mode tracker is a shared MEF part (also an IWpfTextViewCreationListener
            // that VS instantiates for every code view). We retrieve the same singleton instance
            // here so its event-driven cached mode is what gates the leader key.
            _vsVim = ResolveVimModeTracker();

            _popupNav = new PopupNavigation(_windowManager);

            var config = KeybindingConfig.Load();
            _leaderKey = config.LeaderKey;
            (_leaderBindings, _simpleBindings) = BuildBindings(config.Bindings);
            _leaderMatcher = new LeaderSequenceMatcher(_leaderKey, _leaderBindings);
            _simpleMatcher = new SimpleShortcutMatcher(_simpleBindings);
        }

        /// <summary>
        /// Retrieves the shared <see cref="VimModeTracker"/> from the VS MEF container. Falls back
        /// to a fresh instance (whose cached mode stays false) if MEF composition is unavailable.
        /// </summary>
        private VimModeTracker ResolveVimModeTracker()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                return VsServices.Mef<VimModeTracker>(_package)
                    ?? new VimModeTracker();
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}Failed to resolve VimModeTracker: {ex.Message}");
                return new VimModeTracker();
            }
        }

        /// <summary>
        /// Converts the config's <c>sequence-&gt;action-name</c> map into two dictionaries of
        /// ready-to-run delegates, split by whether the key is a modifier shortcut (contains "+")
        /// or a leader sequence (everything else). This split is what keeps a bare <c>e</c> from
        /// firing the <c>e</c>-after-leader binding without the leader key being pressed first.
        /// The leader dictionary is case-sensitive (Ordinal) so leader combos distinguish "s,g"
        /// from "s,G" (a capital letter in the config means Shift+letter); the simple dictionary
        /// stays case-insensitive (KeyNameBuilder's "Ctrl+H" format is case-canonical).
        /// </summary>
        private (Dictionary<string, Action> leader, Dictionary<string, Action> simple) BuildBindings(Dictionary<string, string> namedBindings)
        {
            var leader = new Dictionary<string, Action>(StringComparer.Ordinal);
            var simple = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in namedBindings)
            {
                var action = ResolveAction(pair.Value);
                if (action == null)
                {
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}Unknown action '{pair.Value}' for binding '{pair.Key}' - ignored.");
                    continue;
                }

                if (KeybindingConfig.IsSimpleShortcut(pair.Key))
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
        /// Maps a named action string to a delegate. Built-in actions (cardinal navigation,
        /// telescope finders, solution-explorer toggle) resolve through the <see cref="Actions"/>
        /// registry; the generic <c>"command:Name"</c> form runs any VS command by name, which is
        /// how the LazyVim-style leader bindings (<c>w,-</c> -> split below, etc.) are wired into the
        /// extension without a code change per command.
        /// </summary>
        private Action? ResolveAction(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            string lower = trimmed.ToLowerInvariant();

            var action = Actions.Resolve(lower, this, _launcher);
            if (action != null)
            {
                return action;
            }
            return ParseCommand(trimmed);
        }

        /// <summary>
        /// Handles the generic <c>"command:Name"</c> action form: runs any VS command by name.
        /// Returns null when the name is not a command: prefix or names an empty command.
        /// </summary>
        private Action? ParseCommand(string trimmed)
        {
            const string commandPrefix = "command:";
            if (trimmed.StartsWith(commandPrefix, StringComparison.OrdinalIgnoreCase))
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
                var dte = VsServices.Dte(_package);
                if (dte == null)
                {
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}Command '{command}' failed: DTE unavailable");
                    return;
                }
                dte.ExecuteCommand(command, string.Empty);
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}Command '{command}' failed: {ex.Message}");
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
                // N29: this branch runs before the leader state machine — reset any in-progress
                // leader sequence so it is not left dangling.
                ResetSequence();
                return _popupNav.TryNavigate(down: key == Keys.N);
            }

            // Tool window: route through its controller's normal/input mode — but only while the
            // tool window actually holds keyboard focus. VS's frame-selection state can lag behind
            // real WPF focus, so when an editor is focused the key must fall through to VS instead
            // of being consumed by the (stale) tool-window controller. M8: the controller calls
            // (TryMove/EnterInputMode) are guarded so a controller exception never crashes the hook.
            try
            {
            if (ShouldRouteToolWindowKey())
            {
                var controller = _windowManager.CurrentController;
                if (controller != null)
                {
                    // Input mode: typing passes through. Only Escape (handled above) exits it.
                    if (controller.IsInputMode)
                    {
                        return false;
                    }

                    // Normal mode: i/I enter input mode (the controller may position the caret
                    // first, e.g. I = insert at line start in text-input windows); hjkl move the
                    // focused surface; the controller's action keys (e.g. Solution Explorer
                    // o/r/m/a, text-input w/b/e) act on it. R10: shift is gated for NON-text-input
                    // controllers (Shift+O/R/M/A/G in Solution Explorer must not fire tree actions
                    // and swallow the key); text-input controllers still need shift to tell I/i and
                    // A/a apart, so they are exempt from the shift gate.
                    if (!ctrl && !alt && FocusGuard.ShouldRouteToolWindowKey(
                        _windowManager.IsToolWindow,
                        _vsVim.IsEditorFocused,
                        _windowManager.CurrentController?.IsInputMode == true,
                        _windowManager.IsTextInputType,
                        _windowManager.TextInputSurfaceFocused,
                        shift,
                        shift && _windowManager.IsFocusedTextBoxInCurrentToolWindow()))
                    {
                        // A controller-specific insert key (text-input I = insert at line start) is
                        // handled by TryMove first; the generic 'i' below is the plain-insert
                        // fallback for controllers that don't consume it. M15: an in-progress
                        // leader sequence must not be interrupted by I — the key continues the
                        // sequence instead of entering input mode.
                        if (!_leaderMatcher.IsActive && key == Keys.I)
                        {
                            if (controller.TryMove(key))
                            {
                                return true;
                            }

                            NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}toolwindow-enter-input");
                            controller.EnterInputMode();
                            return true;
                        }

                        if (!_leaderMatcher.IsActive &&
                            (key == Keys.H || key == Keys.J || key == Keys.K || key == Keys.L ||
                             controller.ActionKeys.Contains(key)) &&
                            controller.TryMove(key))
                        {
                            return true;
                        }
                    }
                }
            }
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}toolwindow-move failed: {ex.Message}");
                return false;
            }

            // 1. Leader key pressed / sequence building: delegate to the pure leader state machine.
            //    "Typing" means a tool window in input mode, or the VsVim editor in insert/replace
            //    mode — then the leader key types a literal space instead of starting a sequence.
            var result = _leaderMatcher.HandleKey(key, ctrl, shift, alt, IsTyping());
            switch (result.Kind)
            {
                case LeaderResultKind.PassThrough:
                    break;
                case LeaderResultKind.Consume:
                    return true;
                case LeaderResultKind.Execute:
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}leader-binding executed: {result.Sequence}");
                    return true;
                case LeaderResultKind.Failed:
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}leader-binding failed: {result.Sequence}: {result.ErrorMessage}");
                    return true;
                case LeaderResultKind.Abort:
                    return false;
            }

            // 3. Simple modifier shortcut (e.g. Ctrl+H), matched directly against the key name.
            var simpleResult = _simpleMatcher.HandleKey(key, ctrl, shift, alt);
            if (simpleResult.Kind == SimpleShortcutResultKind.Execute)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}shortcut-binding executed: {simpleResult.Sequence}");
                return true;
            }
            if (simpleResult.Kind == SimpleShortcutResultKind.Failed)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}shortcut-binding failed: {simpleResult.Sequence}: {simpleResult.ErrorMessage}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Cheap pre-filter run by the hook before <see cref="HandleKey"/>. Returns true for every
        /// key that <see cref="HandleKey"/> could possibly act on — a strict superset of the
        /// handler's interest, so handled keys can never be skipped. Reads only in-process state
        /// (no COM/interop).
        /// </summary>
        public bool IsKeyOfInterest(Keys key, bool ctrl, bool shift, bool alt)
        {
            // M2: re-stat the stale-toolwindow sentinel at most once per bounded interval (the
            // test-only fault toggles without a focus change, so the harness creates/removes the
            // sentinel file between scenarios — but the file must not be File.Exists-stat'd on
            // every key-down). HandleKey is always preceded by this, so it is NOT refreshed there
            // too (that would double the syscall).
            DateTime now = DateTime.UtcNow;
            if (now - _lastSentinelRefresh >= SentinelRefreshInterval)
            {
                _lastSentinelRefresh = now;
                _windowManager.RefreshStaleSentinel();
            }

            // Any modifier chord is a candidate simple shortcut (Ctrl+H, ...), and while a leader
            // sequence is in progress ANY key can extend or break it — both handled. R11: shift
            // alone is NOT interesting here — every uppercase letter typed in the editor would
            // otherwise run the full HandleKey path (hot-path cost). Shift is only interesting when
            // a tool window with action keys is in normal mode (the tool-window branch below) or a
            // leader sequence is active (IsLeaderActive above).
            if (IsLeaderActive || ctrl || alt)
            {
                return true;
            }

            // The leader key (Space by default) starts a sequence.
            if (key == _leaderKey)
            {
                return true;
            }

            // Escape cancels a leader sequence / exits tool-window input mode.
            if (key == Keys.Escape)
            {
                return true;
            }

            // Tool-window normal mode: hjkl + the controller's action keys must reach the handler.
            if (ShouldRouteToolWindowKey())
            {
                var c = _windowManager.CurrentController;
                if (c != null && !c.IsInputMode && (DefaultControllerKeys.Contains(key) || c.ActionKeys.Contains(key)))
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetSequence()
        {
            _leaderMatcher.Reset();
        }

        /// <summary>
        /// If the focused tool window is in input mode, exits it (back to normal) and returns
        /// true so the Escape key is swallowed. Returns false otherwise.
        /// </summary>
        private bool ExitToolWindowInputMode()
        {
            // A focused tool-window controller in input mode owns Escape: exit it. This must NOT be
            // gated on the raw IsEditorFocused flag — a non-code text tool window (Command Window)
            // can hold focus without ever changing it. EditorFocusedVeto already excludes trusted
            // tool-window surfaces, so Escape still reaches a controller that genuinely owns focus.
            if (ShouldRouteToolWindowKey())
            {
                var controller = _windowManager.CurrentController;
                if (controller?.IsInputMode == true)
                {
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}toolwindow-exit-input");
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
            return FocusGuard.IsTyping(
                _windowManager.IsToolWindow,
                _windowManager.CurrentController?.IsInputMode == true,
                EditorFocusedVeto,
                _vsVim.IsInTypingMode);
        }

        /// <summary>Performs Cardinal window navigation in a compass direction (see WindowNavigator).</summary>
        internal void Navigate(Direction direction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}navigate direction={direction.ToChar()}");
            // Rebuild the window matrix each navigation (windows can be resized/opened/closed),
            // but source the active window from WindowManager's cached frame rather than re-deriving
            // it from DTE. The frame enumeration itself is cached by WindowManager and invalidated
            // on focus-change events.
            var wm = new WindowNavigator(_windowManager.GetWindowAdapters(_package), _package, _windowManager.CurrentWindow);
            var outcome = wm.NavigateInDirection(direction);
            if (outcome.Activated)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}navigate activated index={outcome.Index}");
            }
            else
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}navigate no-op: {outcome.NoOpReason}");
            }
        }

        /// <summary>
        /// Toggles the Solution Explorer tool window: opens + focuses it when hidden, closes it
        /// when visible. Backed by the DTE <c>View.SolutionExplorer</c> command and window Close.
        /// </summary>
        internal void ToggleSolutionExplorer()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = VsServices.Dte(_package);
                if (dte == null)
                {
                    return;
                }
                var window = dte.Windows.Item(EnvDTE.Constants.vsWindowKindSolutionExplorer);
                if (window.Visible)
                {
                    window.Close();
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer toggled closed");
                }
                else
                {
                    window.Activate();
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer toggled open");
                }
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}ToggleSolutionExplorer failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Focus-aware "delete window": closes the active tool window when a tool window holds
        /// focus, else the active document. Runs the native VS command by name via DTE; the
        /// surface choice is the pure <see cref="CloseWindowCommand"/> seam.
        /// </summary>
        internal void CloseWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ExecuteVsCommand(CloseWindowCommand.For(_windowManager.IsToolWindow));
        }
    }
}
