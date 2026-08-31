using Microsoft.VisualStudio.Shell;
using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;

namespace MyExtension
{
    /// <summary>
    /// Navigates tool-window list/tree surfaces (Solution Explorer, Find Results, references,
    /// code search, error list, ...) with Vim-style hjkl:
    ///   j -> Down,  k -> Up,  h -> Left (collapse / prev column),  l -> Right (expand / next col).
    ///
    /// <para/>
    /// <b>Why inject arrow keys:</b> these controls already navigate with native arrows, and they
    /// don't expose a common "select next item" API we could call. Translating hjkl to an
    /// injected arrow key (<see cref="KeyInjection"/>) reuses their built-in handling uniformly
    /// across every kind of list/tree/grid, with no control-specific code.
    ///
    /// <para/>
    /// <b>Focus detection (in-process WPF):</b> VS's tool windows are WPF. Because we run *inside*
    /// the devenv process, we can read WPF's own focus directly via
    /// <see cref="Keyboard.FocusedElement"/> and walk the visual tree — no cross-process UI
    /// Automation (which throws "application is in a broken state" in VS, and adds latency).
    /// A focused <see cref="TextBoxBase"/> means the user is typing (insert mode) and hjkl/leader
    /// must fall through; a focused editor means VsVim owns hjkl; anything else (a list/tree) we
    /// translate.
    /// </summary>
    internal sealed class ToolWindowNavigation
    {
        private readonly VsVimIntegration _vsVim;

        public ToolWindowNavigation(VsVimIntegration vsVim)
        {
            _vsVim = vsVim ?? throw new ArgumentNullException(nameof(vsVim));
        }

        /// <summary>
        /// Translates a hjkl key into an arrow key if focus is NOT in the editor (VsVim owns hjkl
        /// there) and NOT in a text input (insert mode). Returns true when the key was handled
        /// and should be swallowed by the hook.
        /// </summary>
        public bool TryNavigate(Keys key)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            int vk = KeyToArrowVk(key);
            if (vk == 0)
            {
                return false; // not an hjkl key
            }

            if (_vsVim.IsInEditor() || IsTextInputFocused())
            {
                return false; // let it fall through to VsVim / the text box
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
        /// True when the focused WPF element is (or is inside) a text box. Used so the leader key
        /// and hjkl fall through as typed text inside filter/rename/search boxes rather than being
        /// consumed as navigation/leader actions. Serves as the extension's "insert mode" test.
        /// </summary>
        public static bool IsTextInputFocused()
        {
            // Keyboard.FocusedElement is WPF's notion of the focused IInputElement, valid only
            // because we run inside the same WPF process as the UI (on the UI thread).
            var focused = Keyboard.FocusedElement as DependencyObject;
            return focused != null && FindAncestor<System.Windows.Controls.Primitives.TextBoxBase>(focused) != null;
        }

        /// <summary>
        /// Walks the WPF visual tree upward from <paramref name="current"/> looking for an
        /// ancestor of type <typeparamref name="T"/>. The focused element may be a child of the
        /// text box (e.g. its inner editable area), hence the upward search rather than a simple
        /// <c>is</c> check.
        /// </summary>
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
