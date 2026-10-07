// Telescope/Overlay/Utils/Panes/DelegatePane.cs
using System;
using System.Windows;
using System.Windows.Controls;

namespace Telescope.Overlay
{
    /// <summary>
    /// One pane that DELEGATES its content + focus-entry to an inner pane (n10/BP-21 — the
    /// PromptPane/PreviewPane merge). The Id is the pane's OWN (Input/Preview); the inner pane
    /// supplies the content + Activate/Deactivate. The optional chrome Border (the active-pane
    /// accent line) wraps the inner content and toggles Dim/Active on Activate/Deactivate — the
    /// Preview pane's chrome; the Input pane has none.
    /// </summary>
    internal sealed class DelegatePane : IPane
    {
        private readonly IPane _inner;
        private readonly Border? _chrome;

        public DelegatePane(FocusTarget id, IPane inner, bool chrome = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Id = id;
            if (chrome)
            {
                _chrome = new Border
                {
                    Child = inner.Content,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    BorderBrush = PaneChrome.Dim,
                };
            }
        }

        public FocusTarget Id { get; }
        public FrameworkElement Content => _chrome ?? _inner.Content;

        public void Activate()
        {
            if (_chrome != null)
            {
                _chrome.BorderBrush = PaneChrome.Active;
            }
            _inner.Activate();
        }

        public void Deactivate()
        {
            if (_chrome != null)
            {
                _chrome.BorderBrush = PaneChrome.Dim;
            }
            _inner.Deactivate();
        }
    }
}
