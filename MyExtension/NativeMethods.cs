using System;
using System.Runtime.InteropServices;

namespace MyExtension
{
    /// <summary>
    /// The single Win32 P/Invoke surface for the extension. Every <c>DllImport</c> lives here so
    /// the native interop is declared once and reused across the keyboard hook, key injection,
    /// window-geometry helpers, and the text-motion surfaces.
    /// </summary>
    internal static class NativeMethods
    {
        // Delegate matching the native LowLevelKeyboardProc signature. Kept as a field on
        // GlobalKeyboardHook so the GC can't collect it while the unmanaged hook uses it.
        internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetFocus();

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRectNative(IntPtr hWnd, out NativeRect rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>Retrieves the on-screen rectangle (in pixels) of a window handle.</summary>
        internal static System.Drawing.Rectangle? GetWindowRect(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !GetWindowRectNative(hwnd, out var rect))
            {
                return null;
            }
            return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }
}
