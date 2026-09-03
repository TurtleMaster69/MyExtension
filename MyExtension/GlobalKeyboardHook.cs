using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyExtension
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

        // Delegate kept as a field so the GC can't collect it while the unmanaged hook uses it.
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;

        private readonly AsyncPackage _package;

        private readonly InputHandler _inputHandler;

        // The VS Output window pane we write diagnostics to ("NeoVisual").
        private static IVsOutputWindowPane _pane;
        private static Guid PaneGuid = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

        // Our process id never changes; cached because the focus check runs for every key.
        private static readonly int CurrentProcessId = Process.GetCurrentProcess().Id;

        public GlobalKeyboardHook(AsyncPackage package, Telescope.TelescopeController telescope, WindowManager windowManager)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            _inputHandler = new InputHandler(package, telescope, windowManager);

            // Create the output pane eagerly (on the UI thread) so Log() never has to switch
            // threads afterwards (OutputStringThreadSafe is then usable from any thread).
            EnsureOutputPane();

            _proc = HookCallback;
            _hookId = SetHook(_proc);

            if (_hookId == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Log($"FAILED to install keyboard hook. Win32 Error: {error}");
            }
            else
            {
                Log("Keyboard hook installed successfully!");
            }
        }

        /// <summary>Runs on the UI thread for every keyboard event system-wide.</summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // nCode < 0: we must pass the event through untouched, no exceptions.
            if (nCode < 0)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (!IsVisualStudioFocused())
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // lParam points at a KBDLLHOOKSTRUCT; its first DWORD is the virtual-key code.
            int vkCode = Marshal.ReadInt32(lParam);
            Keys key = (Keys)vkCode;

            bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;

            if (isKeyDown)
            {
                // GetAsyncKeyState reads the *physical* modifier state (as opposed to the
                // message stream), so it's authoritative even if we later swallow a key.
                bool ctrl = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0;
                bool alt = (GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0;

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
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Cheap pre-filter run before the handler. Returns true for every key that
        /// <see cref="InputHandler.HandleKey"/> could possibly act on — a strict superset of
        /// the handler's interest, so that handled keys can never be skipped. Reads only Win32
        /// state and the handler's volatile leader flag.
        /// </summary>
        private bool IsInteresting(Keys key, bool ctrl, bool shift, bool alt)
        {
            // Any modifier chord is a candidate simple shortcut (Ctrl+H, ...), and while a
            // leader sequence is in progress ANY key can extend or break it — both handled.
            if (_inputHandler.IsLeaderActive || ctrl || shift || alt)
            {
                return true;
            }

            switch (key)
            {
                case Keys.Space:      // leader key
                case Keys.Escape:     // sequence cancel / exit tool-window input mode
                case Keys.H:
                case Keys.J:
                case Keys.K:
                case Keys.L:          // h/j/k/l tool-window navigation
                case Keys.I:          // i = enter tool-window input mode
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey: // Ctrl swallow while a completion popup is open
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Installs the low-level hook. <c>dwThreadId = 0</c> makes it global (all threads).
        /// The module handle is required so Windows can locate the callback.
        /// </summary>
        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
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
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint processId);
            return processId == (uint)CurrentProcessId;
        }

        /// <summary>
        /// Writes a diagnostic line to Debug output and the "NeoVisual" pane. Thread-safe, but
        /// NOT for the per-key path (each write is an interop call).
        /// </summary>
        private void Log(string message)
        {
            string fullMessage = $"[GlobalKeyboard] {DateTime.Now:HH:mm:ss.fff}  {message}";
            Debug.WriteLine(fullMessage);

            try
            {
                WriteToOutputWindow(fullMessage);
            }
            catch { }
        }

        /// <summary>
        /// Lazily creates the "NeoVisual" output pane. Must run on the UI thread; we ensure the
        /// constructor calls it at the right time so later writes don't need a thread switch.
        /// </summary>
        private static void EnsureOutputPane()
        {
            if (_pane != null)
            {
                return;
            }

            var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null) return;

            outputWindow.CreatePane(ref PaneGuid, "NeoVisual", 1, 1);
            outputWindow.GetPane(ref PaneGuid, out _pane);
        }

        /// <summary>
        /// Thread-safe output-pane write. <c>OutputStringThreadSafe</c> (vs <c>OutputString</c>)
        /// is specifically the com-callable, any-thread variant — but we still create the pane
        /// on the UI thread first (above).
        /// </summary>
        private static void WriteToOutputWindow(string message)
        {
            _pane?.OutputStringThreadSafe(message + Environment.NewLine);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                Log("Keyboard hook uninstalled.");
            }

            GC.SuppressFinalize(this);
        }

        ~GlobalKeyboardHook() => Dispose();

        // ===================== P/Invoke: Win32 API surface =====================

        // Delegate matching the native LowLevelKeyboardProc signature.
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
