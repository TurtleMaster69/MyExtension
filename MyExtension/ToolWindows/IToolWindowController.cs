using System.Windows.Forms;

namespace MyExtension
{
    /// <summary>
    /// General contract for a tool window's normal/input mode behavior.
    ///
    /// <para/>
    /// Every tool window gets a controller. In <b>normal mode</b> keys are routed through
    /// <see cref="TryMove"/> (hjkl move the selection/items, injected as arrow keys by default);
    /// in <b>input mode</b> keys pass through as typed text (search, rename, ...). The mode is
    /// toggled with Vim-like keys (<c>i</c> = insert, <c>Esc</c> = normal), decided by
    /// <see cref="InputHandler"/> on the UI thread.
    ///
    /// <para/>
    /// <see cref="Type"/> identifies which tool window this controller drives. A window's initial
    /// mode comes from whether its type is a text-input surface (see
    /// <see cref="GeneralToolWindowController.IsTextInputType"/>), and the user can then toggle it.
    ///
    /// <para/>
    /// <b>Threading:</b> all members are called on the UI thread only (the same thread that runs
    /// the keyboard hook). Specific controllers for the windows a user cares about (Solution
    /// Explorer rename/add/move, Watch add-to-watch/calculate, ...) implement this interface and
    /// are registered with <see cref="WindowManager"/>.
    /// </summary>
    public interface IToolWindowController
    {
        /// <summary>The tool-window type this controller drives.</summary>
        ToolWindowType Type { get; }

        /// <summary>True while the window is in input mode (typing passes through).</summary>
        bool IsInputMode { get; }

        /// <summary>Enter input mode: subsequent keys are treated as typed text.</summary>
        void EnterInputMode();

        /// <summary>Exit input mode and return to normal (navigation) mode.</summary>
        void ExitInputMode();

        /// <summary>
        /// Handles a normal-mode key (hjkl and friends). Return true if the key was consumed and
        /// should be swallowed; false to let it pass through.
        /// </summary>
        bool TryMove(Keys key);
    }
}
