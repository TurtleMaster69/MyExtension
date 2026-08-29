using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyExtension
{
    internal sealed class GlobalKeyboardLogger : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;

        // ===================== Output Window =====================
        private static IVsOutputWindowPane _pane;
        private static Guid PaneGuid = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890"); // Change this GUID if you want
        private bool _isHandled = false;

        public GlobalKeyboardLogger()
        {
            _proc = HookCallback; // Keep the delegate alive!
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

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    proc,
                    GetModuleHandle(curModule.ModuleName),
                    0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            _isHandled = false;
            if (nCode >= 0 && IsVisualStudioFocused())
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                bool isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

                if (isKeyDown)
                    Log($"KeyDown: {key} (VK={vkCode})");
                else if (isKeyUp)
                    Log($"KeyUp:   {key} (VK={vkCode})");
                bool ctrl = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0;

                if (key == Keys.L && ctrl)
                {
                    Log("Ctrl+Shift+L");
                }
                if (_isHandled)
                {
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

            // Always write to Debug as backup
            Debug.WriteLine(fullMessage);

            // Write to custom Output Window pane (must be on UI thread)
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    WriteToOutputWindow(fullMessage);
                });
            }
            catch
            {
                // Ignore if we can't switch threads (rare)
            }
        }

        private static void WriteToOutputWindow(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_pane == null)
            {
                var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                if (outputWindow == null) return;

                // Create the pane
                outputWindow.CreatePane(
                    ref PaneGuid,
                    "NeoVisual",      // ← Name shown in "Show output from" dropdown
                    fInitVisible: 1,
                    fClearWithSolution: 1);

                outputWindow.GetPane(ref PaneGuid, out _pane);
            }

            if (_pane != null)
            {
                _pane.OutputStringThreadSafe(message + Environment.NewLine);
                // Optional: activate the pane every time
                // _pane.Activate();
            }
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

        ~GlobalKeyboardLogger()
        {
            Dispose();
        }

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
    }
}