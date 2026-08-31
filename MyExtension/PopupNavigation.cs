using Microsoft.VisualStudio.Shell;
using System;

namespace MyExtension
{
    /// <summary>
    /// Navigates the editor's transient "list" surfaces — IntelliSense completion, Quick
    /// Actions (lightbulb) and Peek windows — via arrow-key injection.
    ///
    /// <para/>
    /// <b>Why inject arrows:</b> these popups consume native up/down arrows rather than editor
    /// commands (<c>Edit.LineDown</c> moves the caret). So <c>Ctrl+N</c>/<c>Ctrl+P</c> are
    /// translated to injected <c>VK_DOWN</c>/<c>VK_UP</c> while a code editor has focus.
    ///
    /// <para/>
    /// <b>Caveat:</b> holding Ctrl while the completion list is open also triggers VS's
    /// "Ctrl+click Go To Definition" affordance, which *dims* the list. We deliberately do
    /// NOT try to suppress that from the keyboard path — intercepting Ctrl corrupted other
    /// Ctrl+chords. To remove the dimming, set Tools > Options > Text Editor > General >
    /// "Use modifier key" to a value other than Ctrl (or disable Ctrl+click Go To Definition).
    /// </summary>
    internal sealed class PopupNavigation
    {
        private readonly VsVimIntegration _vsVim;

        public PopupNavigation(VsVimIntegration vsVim)
        {
            _vsVim = vsVim ?? throw new ArgumentNullException(nameof(vsVim));
        }

        /// <summary>
        /// Navigates the currently-focused popup list down or up by injecting an arrow key.
        /// Only acts while focus is in a code editor, so global shortcuts like Ctrl+N ("New
        /// File") are left alone elsewhere. Returns true (key swallowed) when it injects.
        /// </summary>
        public bool TryNavigate(bool down)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!_vsVim.IsInEditor())
            {
                return false;
            }

            KeyInjection.Press(down ? KeyInjection.VK_DOWN : KeyInjection.VK_UP);
            return true;
        }
    }
}
