using System;
using System.Runtime.InteropServices;

namespace MyExtension
{
    /// <summary>
    /// Injects synthetic keystrokes into whatever control currently has keyboard focus, via the
    /// Win32 <c>keybd_event</c> API.
    ///
    /// <para/>
    /// <b>Why:</b> tool-window lists/trees and the IntelliSense completion popup navigate with
    /// native arrow keys, so the cleanest way to move them from our keyboard hook is to *synthesize*
    /// the arrow press. This works uniformly across every control without knowing its type, unlike
    /// editor commands (<c>Edit.LineDown</c> etc.) which act on the editor caret.
    ///
    /// <para/>
    /// <b>How it interacts with our own hook:</b> an injected key re-enters the low-level hook.
    /// This is safe because we only ever inject *arrows*, never the vim keys we translate — so the
    /// hook sees the arrow, does nothing with it, and passes it to the focused window. No recursion.
    ///
    /// <para/>
    /// <b>keybd_event vs SendInput:</b> <c>keybd_event</c> is the older API; <c>SendInput</c> is the
    /// modern replacement (needed for UIPI / injected-input security). For same-process focus
    /// manipulation in a .NET Framework extension, <c>keybd_event</c> is sufficient and simpler.
    /// </summary>
    internal static class KeyInjection
    {
        // Virtual-key codes for the arrow keys.
        public const int VK_LEFT = 0x25;
        public const int VK_UP = 0x26;
        public const int VK_RIGHT = 0x27;
        public const int VK_DOWN = 0x28;

        // KEYEVENTF_KEYUP flag: emit the release after the press.
        private const uint KEYEVENTF_KEYUP = 0x0002;

        /// <summary>Presses and releases the given virtual key into the focused window.</summary>
        public static void Press(int vk)
        {
            keybd_event((byte)vk, 0, 0, UIntPtr.Zero);            // key down
            keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // key up
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}
