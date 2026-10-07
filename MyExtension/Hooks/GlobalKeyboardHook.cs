using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MyExtension.Input;
using MyExtension.ToolWindows;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyExtension.Hooks
{
    /// <summary>
    /// Installs a Win32 low-level keyboard hook (<c>WH_KEYBOARD_LL</c>) and routes keys of
    /// interest to <see cref="InputHandler"/>, which decides whether to swallow them.
    ///
    /// <para/>
    /// <b>Threading:</b> the hook is installed on the VS UI thread (the package switches to the
    /// main thread first), so the callback runs as part of the UI thread's message pump. That
    /// is deliberate: key decisions involve VS state (mode detection, navigation) and the
    /// synthetic-arrow injection (<see cref="KeyInjection"/>) interacts with the physical key
    /// stream, so doing everything inline on the UI thread keeps event ordering intact.
    /// A past variant that moved the hook to a dedicated message-pump thread caused the
    /// Solution Explorer type-ahead to double-fire (an injected arrow plus the original letter
    /// still reaching the tree); the hook is intentionally NOT on its own thread.
    ///
    /// <para/>
    /// <b>Performance:</b> a cheap pre-filter (<see cref="IsInteresting"/>) skips the handler
    /// entirely for plain typing keys, so the common path costs a few Win32 calls only. The
    /// VsVim mode state is tracked event-driven in <see cref="VimModeTracker"/> (no per-key
    /// consultation). The <c>else</c> marshal below is defensive only — with
    /// the hook on the UI thread it normally never runs (and must stay bounded: low-level hook
    /// callbacks that block too long are silently removed by Windows).
    ///
    /// <para/>
    /// <b>Swallowing:</b> returning <c>(IntPtr)1</c> from the callback drops the key — VS/VsVim
    /// never see it. Only the key-down is swallowed; the matching key-up passes through
    /// harmlessly (VS ignores orphan key-ups).
    /// </summary>
    internal sealed class GlobalKeyboardHook : IDisposable
    {
        // Win32 constants for the low-level keyboard hook and the key-down messages it reports.
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int HC_ACTION = 0;
        private const int HC_NOREMOVE = 3;

        // Delegate kept as a field so the GC can't collect it while the unmanaged hook uses it.
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;

        private readonly AsyncPackage _package;

        private readonly InputHandler _inputHandler;

        // Our process id never changes; cached because the focus check runs for every key.
        private static readonly int CurrentProcessId = Process.GetCurrentProcess().Id;

        public GlobalKeyboardHook(AsyncPackage package, Telescope.Controller.TelescopeController telescope, WindowManager windowManager, MyExtension.Package.TelescopeLauncher launcher, MyExtension.Package.ErrorListGatherer errorListGatherer)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            _inputHandler = new InputHandler(package, telescope, windowManager, launcher, errorListGatherer);

            // Prime the NeoVisual log pane eagerly (on the UI thread) so later any-thread writes
            // (OutputStringThreadSafe) work without a thread switch. N58: use the Log() helper
            // (it prepends the [Hook] prefix) instead of inlining the prefix.
            Log("starting");

            _proc = HookCallback;
            _hookId = SetHook(_proc);

            if (_hookId == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log($"FAILED to install keyboard hook. Win32 Error: {error}");
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.Hook}INSTALL FAILED error={error}");
            }
            else
            {
                Log("Keyboard hook installed successfully!");
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.Hook}installed");
            }
        }

        /// <summary>Runs on the UI thread for every keyboard event system-wide.</summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // nCode < 0: we must pass the event through untouched, no exceptions.
            if (nCode < 0)
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // m16 (BP-10): a peeked event (HC_NOREMOVE) is passed through untouched — only
            // HC_ACTION (0) is a real key event the hook processes.
            if (!IsActionEvent(nCode))
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // m10 (BP-5): a key-up returns BEFORE the focus check — only key-downs need it, so
            // the reorder avoids the two Win32 calls (GetForegroundWindow + GetWindowThreadProcessId)
            // on every key-up. ShouldProcessKey is the pure seam: false for a key-up without
            // consulting the focus check, the focus check's result for a key-down.
            if (!ShouldProcessKey((int)wParam, () => IsVisualStudioFocused()))
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // lParam points at a KBDLLHOOKSTRUCT; its first DWORD is the virtual-key code.
            // m19: guard a null lParam before the read — an exception in the hook callback is
            // fatal (Windows silently removes the hook).
            if (lParam == IntPtr.Zero)
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            int vkCode = Marshal.ReadInt32(lParam);
            Keys key = (Keys)vkCode;

            // Enter-storm guard (F1): if this key-down is one we just synthesized in
            // KeyInjection.Press, pass it through untouched so it reaches the focused
            // tree/control natively instead of re-triggering the controller action.
            if (InjectedKeyGuard.Instance.TryConsume(vkCode))
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // GetAsyncKeyState reads the *physical* modifier state (as opposed to the
            // message stream), so it's authoritative even if we later swallow a key.
            bool ctrl = (NativeMethods.GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;
            bool shift = (NativeMethods.GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0;
            bool alt = (NativeMethods.GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0;

            // Cheap pre-filter: plain typing keys that InputHandler can't possibly act on
            // return here immediately, without running the handler at all.
            if (IsInteresting(key, ctrl, shift, alt))
            {
                bool handled = false;

                // The hook is installed on the main thread, so HandleKey normally runs
                // directly here. The else is defensive only; its wait must stay bounded
                // (Windows removes low-level hooks whose callbacks block too long).
                if (ThreadHelper.CheckAccess())
                {
                    handled = _inputHandler.HandleKey(key, ctrl, shift, alt);
                }
                else
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        handled = _inputHandler.HandleKey(key, ctrl, shift, alt);
                    });
                }

                if (handled)
                {
                    return (IntPtr)1; // swallow the key-down — VS/VsVim never see it
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Cheap pre-filter run before the handler. Returns true for every key that
        /// <see cref="InputHandler.HandleKey"/> could possibly act on — a strict superset of
        /// the handler's interest, so that handled keys can never be skipped. Reads only Win32
        /// state and the handler's volatile leader flag.
        /// </summary>
        private bool IsInteresting(Keys key, bool ctrl, bool shift, bool alt) =>
            _inputHandler.IsKeyOfInterest(key, ctrl, shift, alt);

        /// <summary>m16 (BP-10): true when the hook event is a real key event (HC_ACTION), false
        /// for a peeked event (HC_NOREMOVE) that must be passed through untouched. The pure seam
        /// the callback gates on.</summary>
        internal static bool IsActionEvent(int nCode) => nCode == HC_ACTION;

        /// <summary>m10 (BP-5): true only for a key-down that passes the focus check. A key-up
        /// (WM_KEYUP) returns false WITHOUT consulting the focus check — the reorder avoids the
        /// two Win32 calls (GetForegroundWindow + GetWindowThreadProcessId) on every key-up. The
        /// pure seam the callback gates on.</summary>
        internal static bool ShouldProcessKey(int wParam, Func<bool> isFocused)
        {
            bool isKeyDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            if (!isKeyDown)
            {
                return false;
            }
            return isFocused();
        }

        /// <summary>
        /// Installs the low-level hook. <c>dwThreadId = 0</c> makes it global (all threads).
        /// The module handle is required so Windows can locate the callback.
        /// </summary>
        private static IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
        {
            IntPtr hMod = IntPtr.Zero;
            try
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    hMod = NativeMethods.GetModuleHandle(curModule.ModuleName);
                }
            }
            catch (Exception ex)
            {
                // n18: MainModule can throw Win32Exception (e.g. access denied). WH_KEYBOARD_LL
                // accepts IntPtr.Zero for hMod when the callback is in-process, so fall back
                // rather than fail the hook init step and degrade the extension to a no-op.
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.Hook}SetHook MainModule failed: {ex.Message}");
            }
            return NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, proc, hMod, 0);
        }

        /// <summary>
        /// True when the foreground window belongs to this process (i.e. this VS is focused).
        /// The hook is global, so without this we'd also eat keys while other apps are focused.
        ///
        /// This check also makes the hook safe with multiple VS instances open: each devenv.exe
        /// is a separate process, so each instance's hook only acts while *it* is the foreground
        /// window, and the other instance's hook callback returns immediately (a no-op) for keys
        /// typed here. VsVim runs per-process too, so there's no cross-instance bleed either.
        /// </summary>
        private bool IsVisualStudioFocused()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            return processId == (uint)CurrentProcessId;
        }

        /// <summary>
        /// Writes a diagnostic line through the full facade pipeline (structured log file, debug
        /// output, and the "NeoVisual" pane). Thread-safe, but NOT for the per-key path (each write
        /// is an interop call).
        /// </summary>
        private void Log(string message)
        {
            string fullMessage = $"{Telescope.Logging.DiagnosticLog.Hook}{message}";
            Telescope.Logging.NeoVisualLog.Log(fullMessage);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                Log("Keyboard hook uninstalled.");
            }

            GC.SuppressFinalize(this);
        }

        ~GlobalKeyboardHook() => Dispose();
    }
}
