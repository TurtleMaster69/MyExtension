using MyExtension.Hooks;
using System;
using System.Windows.Forms;

namespace MyExtension.ToolWindows
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
    internal sealed class GeneralToolWindowController : ToolWindowControllerBase
    {
        public GeneralToolWindowController(ToolWindowType type) : base(type)
        {
            // The initial mode comes from the type classification via the base ctor (m23).
        }

        /// <summary>The default controller acts on no non-hjkl keys.</summary>
        public override System.Collections.Generic.IReadOnlyCollection<Keys> ActionKeys =>
            System.Array.Empty<Keys>();

        /// <summary>
        /// Handles a normal-mode key. Only hjkl map to arrows (left/down/up/right respectively);
        /// any other key is not consumed here. Returns true when the key was handled.
        /// </summary>
        public override bool TryMove(Keys key)
        {
            return TryMoveArrow(key);
        }

        /// <summary>
        /// Shared hjkl→arrow path used by the default controller and the Solution Explorer
        /// controller's J/K entries: maps the key via <see cref="KeyToArrowVk"/>, logs the
        /// <c>[NeoVisual] toolwindow-move key=... -&gt; arrow vk=...</c> diagnostic, and presses the
        /// arrow via <see cref="KeyInjection.Press"/>. Returns true when the key was handled.
        /// </summary>
        internal static bool TryMoveArrow(Keys key)
        {
            int vk = KeyToArrowVk(key);
            if (vk == 0)
            {
                return false;
            }

            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}toolwindow-move key={key} -> arrow vk={vk}");
            KeyInjection.Press(vk);
            return true;
        }

        internal static int KeyToArrowVk(Keys key)
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
    }
}
