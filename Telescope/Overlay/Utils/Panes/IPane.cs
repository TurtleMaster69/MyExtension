// Telescope/Overlay/Utils/Panes/IPane.cs
using System.Windows;
using System.Windows.Media;

namespace Telescope.Overlay
{
    /// <summary>
    /// One focusable region of the overlay (the pane contract — Feature 7). The overlay composes
    /// its UI out of panes behind this contract; adding a surface (the future lazygit overlay) =
    /// implementing IPane + registering it in the PaneHost. The Id member name IS the
    /// <c>[Telescope] focus target=</c> diagnostic token (M-M7). UI thread only (WPF element
    /// ownership). NOTE: the key-routing participation is the pane's Id — the overlay dispatches
    /// by the FOCUSED pane's Id (the pinned consume-vs-fallthrough table, plan §1.3); the three
    /// panes share overlay-level state machines (OverlayKeyHandler, TextMotionNavigator), so a
    /// per-pane key hook would force those into the panes — deliberately deferred.
    /// NOTE (rev 1): the pane does NOT carry its layout rect — the HOST MEASURES it (the
    /// overlay's RefreshPaneLayout: TransformToVisual → PaneRect → FocusTargetModel.SetLayout,
    /// plan §1.2). The visual-tree position is the single source of truth; a pane-carried rect
    /// could drift from the rendered layout.
    /// </summary>
    internal interface IPane
    {
        /// <summary>The pane's identity — the diagnostic token (Input|List|Preview).</summary>
        FocusTarget Id { get; }

        /// <summary>The pane's visual (the chrome Border hosting the control) — composed into
        /// the overlay's layout tree and the click-tunneling target.</summary>
        FrameworkElement Content { get; }

        /// <summary>Whether the pane's own Content can take WPF keyboard focus directly
        /// (Prompt/List: true). The Preview pane is false — its hosted editor's VisualElement
        /// takes focus instead (Activate routes there).</summary>
        bool IsFocusable { get; }

        /// <summary>Focus-entry: take keyboard focus (per-pane semantics) + the active visuals.
        /// Never logs (the overlay's FocusPane logs the one focus target= line per change).</summary>
        void Activate();

        /// <summary>Focus-exit: the inactive visuals. Never moves focus (the caller activates
        /// another pane immediately after).</summary>
        void Deactivate();
    }

    /// <summary>The shared pane-chrome brushes (the active-pane accent line — plan §1.6). Frozen,
    /// one instance each, so every pane's visuals cannot drift.</summary>
    internal static class PaneChrome
    {
        /// <summary>The inactive accent (the overlay's border color).</summary>
        public static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)));

        /// <summary>The active accent (the mode-label blue — subtle).</summary>
        public static readonly Brush Active = Freeze(new SolidColorBrush(Color.FromRgb(0x8b, 0x9d, 0xc3)));

        private static Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
