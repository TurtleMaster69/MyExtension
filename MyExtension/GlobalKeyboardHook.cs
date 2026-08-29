using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyExtension
{
    internal sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;

        private readonly AsyncPackage _package;
        private readonly InputHandler _inputHandler;   // ← added

        // Output window
        private static IVsOutputWindowPane _pane;
        private static Guid PaneGuid = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

        public GlobalKeyboardHook(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _inputHandler = new InputHandler(package);   // ← create handler

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

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsVisualStudioFocused())
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;

                if (isKeyDown)
                {
                    bool ctrl = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0;

                    Log($"KeyDown: {key} | Ctrl={ctrl} Shift={shift} Alt={alt}");

                    bool handled = false;

                    // Switch to UI thread and let InputHandler decide
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        handled = _inputHandler.HandleKey(key, ctrl, shift, alt);
                    });

                    if (handled)
                    {
                        Log("→ Key was handled by InputHandler");
                        return (IntPtr)1;   // block the key
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // ===================== Helper methods (unchanged) =====================

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private bool IsVisualStudioFocused()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint processId);
            return processId == (uint)Process.GetCurrentProcess().Id;
        }

        private void Log(string message)
        {
            string fullMessage = $"[GlobalKeyboard] {DateTime.Now:HH:mm:ss.fff}  {message}";
            Debug.WriteLine(fullMessage);

            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    WriteToOutputWindow(fullMessage);
                });
            }
            catch { }
        }

        private static void WriteToOutputWindow(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_pane == null)
            {
                var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                if (outputWindow == null) return;

                outputWindow.CreatePane(ref PaneGuid, "NeoVisual", 1, 1);
                outputWindow.GetPane(ref PaneGuid, out _pane);
            }

            _pane?.OutputStringThreadSafe(message + Environment.NewLine);
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                Log("Keyboard hook uninstalled.");
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~GlobalKeyboardHook() => Dispose();

        // ===================== P/Invoke =====================

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