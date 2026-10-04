using System.Collections.Generic;
using System.Windows.Input;

namespace Telescope.Overlay
{
    /// <summary>
    /// Which pane currently owns the overlay's keyboard focus. The member names ARE the
    /// <c>[Telescope] focus target=Input|List|Preview</c> diagnostic tokens (M-M7) and the ORDER
    /// is pinned (Input=0, List=1, Preview=2): the order is the pane REGISTRY order and the
    /// directional tie-break's iteration order (the Cardinal's last-in-list rule —
    /// <see cref="PaneNavigationEngine"/>), so reordering the members silently changes the
    /// tie-break.
    /// </summary>
    internal enum FocusTarget
    {
        Input,
        List,
        Preview,
    }

    /// <summary>
    /// The normalized focus gestures the machine understands — mapped from the WPF key by
    /// <see cref="FocusTargetModel.MapKey"/> so the pure machine never reads
    /// <c>Keyboard.Modifiers</c>. Left/Right/Up/Down are DIRECTIONS (the Cardinal spatial
    /// mapping — the same keys as the window navigation, one level down), NOT fixed targets.
    /// (Own enum — NOT <see cref="OverlayKey"/>: adding CtrlJ/CtrlK there would touch the
    /// out-of-scope OverlayKeyHandler.cs.)
    /// </summary>
    internal enum PaneFocusKey
    {
        None,
        Left,    // Ctrl+H -> focus LEFT
        Right,   // Ctrl+L -> focus RIGHT
        Down,    // Ctrl+J -> focus DOWN
        Up,      // Ctrl+K -> focus UP
        Escape,  // Escape — consumed ONLY in Preview (-> List); elsewhere the pane's mode machine owns it
    }

    /// <summary>
    /// The outcome of feeding a key to <see cref="FocusTargetModel.Handle"/>:
    /// <see cref="None"/> = not a focus gesture (falls through to the pane dispatch);
    /// <see cref="Handled"/> = the focus changed (the overlay logs <c>focus target=</c> + applies);
    /// <see cref="NoOp"/> = a focus gesture with no pane in that direction — CONSUMED, nothing
    /// moves (the overlay logs the distinct <c>focus no-op:</c> reason — the m47 outcome
    /// pattern). NO wrap.
    /// </summary>
    internal enum FocusTargetAction
    {
        None,
        Handled,
        NoOp,
    }

    /// <summary>
    /// Dependency-free state machine for the overlay's pane focus (Feature 7 rev 1 — the
    /// GEOMETRIC directional move): Ctrl+H/J/K/L = focus LEFT/DOWN/UP/RIGHT — the machine asks
    /// the pure <see cref="PaneNavigationEngine"/> which pane lies in the requested direction of
    /// the focused pane's LAYOUT RECT (the same pipeline as the window navigation's
    /// WindowNavigationEngine: in-direction → aligned → closest gap → largest adjacency, ties →
    /// the last pane in registry order). A direction with no pane is a consumed NO-OP (no wrap).
    /// Escape-in-Preview→List (carried from M34) and the left-click normalization
    /// (<see cref="Focus"/>) are unchanged. The layout rects are PUSHED in by the host
    /// (<see cref="SetLayout"/> — the overlay measures; the machine never reads WPF).
    /// Extracted from <see cref="TelescopeOverlay"/> so every transition is unit-tested without
    /// WPF or Visual Studio.
    /// </summary>
    internal sealed class FocusTargetModel
    {
        private readonly List<KeyValuePair<FocusTarget, PaneRect>> _layout = new();

        public FocusTarget Current { get; private set; } = FocusTarget.Input;

        public void Reset() => Current = FocusTarget.Input;

        /// <summary>Pushes the pane layout rects (overlay DIP coordinates, REGISTRY order — the
        /// tie-break iterates it). Called by the overlay on SizeChanged + ContentRendered —
        /// BEFORE the first key can land; an empty layout makes every directional move a safe
        /// no-op. Geometry only — Reset() does NOT clear it.</summary>
        public void SetLayout(IReadOnlyList<KeyValuePair<FocusTarget, PaneRect>> layout)
        {
            _layout.Clear();
            _layout.AddRange(layout);
        }

        public FocusTargetAction Handle(PaneFocusKey key)
        {
            switch (key)
            {
                case PaneFocusKey.Left: return Move(PaneDirection.Left);
                case PaneFocusKey.Right: return Move(PaneDirection.Right);
                case PaneFocusKey.Up: return Move(PaneDirection.Up);
                case PaneFocusKey.Down: return Move(PaneDirection.Down);
                case PaneFocusKey.Escape when Current == FocusTarget.Preview:
                    Current = FocusTarget.List;
                    return FocusTargetAction.Handled;
                default:
                    return FocusTargetAction.None;
            }
        }

        /// <summary>The GEOMETRIC directional move: the engine picks the pane in
        /// <paramref name="direction"/>'s way of the focused rect; none → a consumed NoOp
        /// (NO wrap — the focus stays put).</summary>
        private FocusTargetAction Move(PaneDirection direction)
        {
            FocusTarget? target = PaneNavigationEngine.SelectTarget(_layout, Current, direction);
            if (target is null)
            {
                return FocusTargetAction.NoOp;
            }
            Current = target.GetValueOrDefault();
            return FocusTargetAction.Handled;
        }

        /// <summary>
        /// Left-click normalization: any pane is focusable by clicking it. Always
        /// <see cref="FocusTargetAction.Handled"/> (the caller logs + applies) — an idempotent
        /// click on the focused pane re-logs the same token (harmless, deterministic).
        /// </summary>
        public FocusTargetAction Focus(FocusTarget target)
        {
            Current = target;
            return FocusTargetAction.Handled;
        }

        /// <summary>WPF key → the normalized focus gesture. Only the Ctrl chords + Escape map;
        /// plain h/j/k/l are NOT focus keys (they are pane keys). Pure — the caller reads
        /// Keyboard.Modifiers once and passes the result.</summary>
        public static PaneFocusKey MapKey(Key key, bool hasCtrl)
        {
            switch (key)
            {
                case Key.H when hasCtrl: return PaneFocusKey.Left;
                case Key.L when hasCtrl: return PaneFocusKey.Right;
                case Key.J when hasCtrl: return PaneFocusKey.Down;
                case Key.K when hasCtrl: return PaneFocusKey.Up;
                case Key.Escape: return PaneFocusKey.Escape;
                default: return PaneFocusKey.None;
            }
        }

        /// <summary>The no-op reason's direction token (lowercase — the user's own words:
        /// "focus left,down,up,right"). Only the four directions can produce a NoOp; the
        /// default is defensive and unreachable.</summary>
        public static string DirectionName(PaneFocusKey key)
        {
            switch (key)
            {
                case PaneFocusKey.Left: return "left";
                case PaneFocusKey.Right: return "right";
                case PaneFocusKey.Up: return "up";
                case PaneFocusKey.Down: return "down";
                default: return key.ToString().ToLowerInvariant();
            }
        }

        /// <summary>A focus change whose target is not Input ends insert mode (insert typing only
        /// lands in the prompt; leaving the prompt mid-insert would strand the keyboard). The
        /// overlay feeds Escape to the mode machine when this returns true. A NoOp never reaches
        /// this (the target did not change — no mode exit on a no-op).</summary>
        public static bool ExitsInsert(FocusTarget target) => target != FocusTarget.Input;
    }
}
