// Telescope/Overlay/Utils/Panes/PromptPane.cs
using System;
using System.Windows;

namespace Telescope.Overlay
{
    /// <summary>
    /// The Input pane: the prompt TextBox (the query input). Focus-entry delegates to the
    /// overlay's FocusPrompt (the byte-stable <c>Focus prompt => ...</c> diagnostic + the caret style
    /// live there — the pane is a thin adapter). No active-pane chrome: the prompt's focus is
    /// self-evident (the caret + the mode label) — plan §1.6.
    /// </summary>
    internal sealed class PromptPane : IPane
    {
        private readonly Action _activate;

        public PromptPane(FrameworkElement content, Action activate)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        }

        public FocusTarget Id => FocusTarget.Input;
        public FrameworkElement Content { get; }
        public bool IsFocusable => true;
        public void Activate() => _activate();
        public void Deactivate() { /* no chrome — see the class comment */ }
    }
}
