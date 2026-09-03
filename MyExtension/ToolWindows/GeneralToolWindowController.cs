using System;
using System.Windows.Forms;

namespace MyExtension
{
    /// <summary>
    /// Default <see cref="IToolWindowController"/> used for every tool window that doesn't have a
    /// more specialized controller registered with <see cref="WindowManager"/>.
    ///
    /// <para/>
    /// In <b>normal mode</b> it maps hjkl to injected arrow keys (the same native navigation the
    /// lists/trees already understand, via <see cref="KeyInjection"/>), which is what makes hjkl
    /// work in essentially any tool window. In <b>input mode</b> keys pass through so the user can
    /// type (search, rename, filter, ...).
    ///
    /// <para/>
    /// A window's <b>initial mode</b> comes from whether its type is a text-input surface
    /// (<see cref="IsTextInputType"/>); the user can then toggle it with <c>i</c> (input) and
    /// <c>Esc</c> (normal), decided in <see cref="InputHandler"/>.
    ///
    /// <para/>
    /// <b>Threading:</b> all members are called on the UI thread only (same thread as the hook).
    /// </summary>
    internal sealed class GeneralToolWindowController : IToolWindowController
    {
        private readonly ToolWindowType _type;
        private bool _isInputMode;

        public GeneralToolWindowController(ToolWindowType type)
        {
            _type = type;
            // Text-input tool windows (search/rename/command surfaces) default to input mode so the
            // user can type immediately; everything else starts in normal (navigation) mode.
            _isInputMode = IsTextInputType(type);
        }

        public ToolWindowType Type => _type;

        public bool IsInputMode => _isInputMode;

        public void EnterInputMode() => _isInputMode = true;

        public void ExitInputMode() => _isInputMode = false;

        /// <summary>
        /// Handles a normal-mode key. Only hjkl map to arrows (left/down/up/right respectively);
        /// any other key is not consumed here. Returns true when the key was handled.
        /// </summary>
        public bool TryMove(Keys key)
        {
            int vk = KeyToArrowVk(key);
            if (vk == 0)
            {
                return false;
            }

            KeyInjection.Press(vk);
            return true;
        }

        private static int KeyToArrowVk(Keys key)
        {
            switch (key)
            {
                case Keys.H: return KeyInjection.VK_LEFT;
                case Keys.J: return KeyInjection.VK_DOWN;
                case Keys.K: return KeyInjection.VK_UP;
                case Keys.L: return KeyInjection.VK_RIGHT;
                default: return 0;
            }
        }

        /// <summary>
        /// True when a tool window of this type is primarily a text-input surface (search boxes,
        /// command/immediate consoles, browser address bars, ...), so it should start in input
        /// mode. Used as the default; the mode can still be toggled per window.
        /// </summary>
        public static bool IsTextInputType(ToolWindowType type)
        {
            switch (type)
            {
                case ToolWindowType.FindReplace:
                case ToolWindowType.FindAdvanced:
                case ToolWindowType.FindResults1:
                case ToolWindowType.FindResults2:
                case ToolWindowType.ObjectSearchWindow:
                case ToolWindowType.ObjectSearchResultsWindow:
                case ToolWindowType.ImmediateWindow:
                case ToolWindowType.CommandWindow:
                case ToolWindowType.ConsoleIO:
                case ToolWindowType.WebBrowserWindow:
                case ToolWindowType.WebBrowserPreviewWindow:
                case ToolWindowType.BrowserDoc:
                case ToolWindowType.HelpSearch:
                case ToolWindowType.HelpIndex:
                case ToolWindowType.HelpIndexResults:
                case ToolWindowType.HelpHowDoI:
                case ToolWindowType.StartPage:
                    return true;
                default:
                    return false;
            }
        }
    }
}
