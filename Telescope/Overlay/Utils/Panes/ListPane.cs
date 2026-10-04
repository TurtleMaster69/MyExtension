// Telescope/Overlay/Utils/Panes/ListPane.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Telescope.Overlay
{
    /// <summary>
    /// The List pane: the columned results ListView (Feature 7). The list becomes REALLY
    /// focusable (Focusable=true — it was false: the prompt owned all focus) so left-click and
    /// Ctrl+H/J/K/L give it real keyboard focus. The overlay still claims the vim selection
    /// gestures (j/k/gg/G/Enter/q/Esc/i/a/A/I — routed through the untouched OverlayKeyHandler
    /// via <see cref="ListKeyMap"/>); the native Up/Down arrows stay live (the ListView's own
    /// selection handling; the overlay's SelectionChanged sync adopts the index —
    /// <see cref="PaneSelectionSync"/>).
    ///
    /// <para/>Focus visuals (the pinned style, plan §1.6): FocusVisualStyle=null (no dotted
    /// rect); the active-pane indicator is the chrome Border's bottom accent line — constant 1px
    /// thickness, brush-only switch dim #333841 ↔ active #8b9dc3, so activation never shifts
    /// layout.
    /// </summary>
    internal sealed class ListPane : IPane
    {
        private readonly ListView _list;
        private readonly Border _chrome;

        public ListPane(ListView list)
        {
            _list = list ?? throw new ArgumentNullException(nameof(list));
            _list.Focusable = true;        // Feature 7: a real focus target (was false)
            _list.FocusVisualStyle = null; // no dotted rect — the accent line is the indicator
            _chrome = new Border
            {
                Child = _list,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = PaneChrome.Dim,
            };
        }

        public FocusTarget Id => FocusTarget.List;
        public FrameworkElement Content => _chrome;
        public bool IsFocusable => true;

        public void Activate()
        {
            _chrome.BorderBrush = PaneChrome.Active;
            _list.Focus();   // real WPF keyboard focus (best-effort; the machine already decided)
        }

        public void Deactivate() => _chrome.BorderBrush = PaneChrome.Dim;
    }

    /// <summary>
    /// The List pane's WPF-key → OverlayKey map — the PINNED consume-vs-fallthrough contract for
    /// the List pane (plan §1.3 R3): the vim selection gestures are claimed (routed through the
    /// untouched OverlayKeyHandler); EVERYTHING else — the native Up/Down arrows, the
    /// h/l/w/b/e/0/$ motions (no text caret here), the letters — maps to
    /// <see cref="OverlayKey.Other"/> and falls through to the ListView's own handling. The Ctrl
    /// chords never reach this map (the focus machine consumes them first — the tunneling
    /// dispatch).
    /// </summary>
    internal static class ListKeyMap
    {
        public static OverlayKey Map(Key key, bool shift)
        {
            switch (key)
            {
                case Key.Escape: return OverlayKey.Escape;
                case Key.Q: return OverlayKey.Q;
                case Key.Enter: return OverlayKey.Enter;
                case Key.J: return OverlayKey.J;
                case Key.K: return OverlayKey.K;
                case Key.G: return shift ? OverlayKey.ShiftG : OverlayKey.G;
                case Key.I: return OverlayKey.I;
                case Key.A: return OverlayKey.A;
                default: return OverlayKey.Other;   // Up/Down/h/l/w/b/e/0/$/...: NOT claimed
            }
        }
    }
}
