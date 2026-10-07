// Telescope/Overlay/Utils/Panes/PreviewPane.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Telescope.Overlay
{
    /// <summary>
    /// The Preview pane: the read-only VS editor view slot (the columns plan's IPreviewEditor
    /// host — wrapped, NOT rewritten). NOT directly focusable (IsFocusable=false): the hosted
    /// editor's VisualElement is the focusable surface, so Activate routes through the overlay's
    /// editor-focus callback (IPreviewEditor.Focus + the caret apply). The empty-preview case
    /// keeps the prompt focused (the machine stays Preview — the preview motions no-op over the
    /// empty navigator; Escape returns to List — the carried M34 semantics).
    /// </summary>
    internal sealed class PreviewPane : IPane
    {
        private readonly Action _activate;
        private readonly Border _chrome;

        public PreviewPane(FrameworkElement content, Action activate)
        {
            _activate = activate ?? throw new ArgumentNullException(nameof(activate));
            _chrome = new Border
            {
                Child = content ?? throw new ArgumentNullException(nameof(content)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = PaneChrome.Dim,
            };
        }

        public FocusTarget Id => FocusTarget.Preview;
        public FrameworkElement Content => _chrome;

        public void Activate()
        {
            _chrome.BorderBrush = PaneChrome.Active;
            _activate();
        }

        public void Deactivate() => _chrome.BorderBrush = PaneChrome.Dim;
    }
}
