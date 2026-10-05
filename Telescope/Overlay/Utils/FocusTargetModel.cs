using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace Telescope.Overlay
{
    /// <summary>
    /// Which pane currently owns the overlay's keyboard focus. The member names ARE the
    /// <c>[Telescope] focus target=Input|List|Preview</c> diagnostic tokens (M-M7) and the ORDER
    /// is pinned (Input=0, List=1, Preview=2): the order is the pane REGISTRY order and the
    /// directional tie-break's iteration order (the Cardinal's last-in-list rule — the collapsed
    /// geometric selection pipeline in <see cref="FocusTargetModel"/>), so reordering the members
    /// silently changes the tie-break.
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
    /// A pane's layout rect in overlay DIP coordinates (origin top-left) — the pane analogue of
    /// MyExtension.Navigation.WindowRect (NOT shared: Telescope does not reference MyExtension).
    /// The geometric focus pipeline's input; the overlay measures the panes and pushes the rects
    /// in via <see cref="FocusTargetModel.SetLayout"/>.
    /// </summary>
    internal readonly struct PaneRect
    {
        public static readonly PaneRect Empty = new PaneRect(0, 0, 0, 0);

        public PaneRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public int Right => X + Width;

        public int Bottom => Y + Height;

        public bool IsEmpty => Width == 0 && Height == 0;

        /// <summary>The overlap length with <paramref name="other"/> on <paramref name="axis"/>
        /// (0-floor) — mirrors WindowRect.Adjacency.</summary>
        public int Adjacency(PaneRect other, PaneAxis axis)
        {
            if (axis == PaneAxis.X)
            {
                int start = Math.Max(X, other.X);
                int end = Math.Min(Right, other.Right);
                return Math.Max(0, end - start);
            }
            int yStart = Math.Max(Y, other.Y);
            int yEnd = Math.Min(Bottom, other.Bottom);
            return Math.Max(0, yEnd - yStart);
        }

        /// <summary>The distance from this rect to <paramref name="other"/> along
        /// <paramref name="direction"/> (NEGATIVE = overlapping/behind) — mirrors
        /// WindowRect.GapTo. Called ON THE CANDIDATE with the focused rect as other.</summary>
        public int GapTo(PaneRect other, PaneFocusKey direction)
        {
            switch (direction)
            {
                case PaneFocusKey.Up: return other.Y - Bottom;
                case PaneFocusKey.Down: return Y - other.Bottom;
                case PaneFocusKey.Left: return other.X - Right;
                case PaneFocusKey.Right: return X - other.Right;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }
    }

    /// <summary>The axis shared with the movement direction (the alignment/adjacency axis) —
    /// mirrors MyExtension.Navigation's DirectionExtensions.PerpendicularAxis.</summary>
    internal enum PaneAxis { X, Y }

    /// <summary>
    /// Dependency-free state machine for the overlay's pane focus (Feature 7 rev 1 — the
    /// GEOMETRIC directional move): Ctrl+H/J/K/L = focus LEFT/DOWN/UP/RIGHT — the machine asks
    /// the collapsed geometric selection pipeline (<see cref="SelectTarget"/>) which pane lies in
    /// the requested direction of the focused pane's LAYOUT RECT (the same pipeline as the window
    /// navigation's WindowNavigationEngine: in-direction → aligned → closest gap → largest
    /// adjacency, ties → the last pane in registry order). A direction with no pane is a consumed
    /// NO-OP (no wrap). Escape-in-Preview→List (carried from M34) and the left-click
    /// normalization (<see cref="Focus"/>) are unchanged. The layout rects are PUSHED in by the
    /// host (<see cref="SetLayout"/> — the overlay measures; the machine never reads WPF).
    /// Extracted from <see cref="TelescopeOverlay"/> so every transition is unit-tested without
    /// WPF or Visual Studio. The mirrored <c>PaneNavigationEngine</c> was COLLAPSED into this
    /// class (D1/D2 — one pure focus resolver); the pinned tie-break (Ctrl+K Input→Preview — the
    /// equal-width last-in-list net) + the no-op edges survive byte-identically.
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
                case PaneFocusKey.Left: return Move(PaneFocusKey.Left);
                case PaneFocusKey.Right: return Move(PaneFocusKey.Right);
                case PaneFocusKey.Up: return Move(PaneFocusKey.Up);
                case PaneFocusKey.Down: return Move(PaneFocusKey.Down);
                case PaneFocusKey.Escape when Current == FocusTarget.Preview:
                    Current = FocusTarget.List;
                    return FocusTargetAction.Handled;
                default:
                    return FocusTargetAction.None;
            }
        }

        /// <summary>The GEOMETRIC directional move: the collapsed pipeline picks the pane in
        /// <paramref name="direction"/>'s way of the focused rect; none → a consumed NoOp
        /// (NO wrap — the focus stays put).</summary>
        private FocusTargetAction Move(PaneFocusKey direction)
        {
            FocusTarget? target = SelectTarget(_layout, Current, direction);
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

        /// <summary>
        /// The single chord→direction map (D14): the Ctrl+H/J/K/L chords + Escape resolve through
        /// ONE table — <see cref="MapKey"/> and the overlay's Ctrl-chord path both call this, so a
        /// chord can never map to a different direction in one place than another. Pure — the
        /// caller reads Keyboard.Modifiers once and passes the result.
        /// </summary>
        public static PaneFocusKey ChordDirection(Key key, bool hasCtrl)
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

        /// <summary>WPF key → the normalized focus gesture. Only the Ctrl chords + Escape map;
        /// plain h/j/k/l are NOT focus keys (they are pane keys). Pure — the caller reads
        /// Keyboard.Modifiers once and passes the result. Resolves through
        /// <see cref="ChordDirection"/> (the single chord map).</summary>
        public static PaneFocusKey MapKey(Key key, bool hasCtrl)
        {
            return ChordDirection(key, hasCtrl);
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

        // ================================================================
        // The collapsed geometric selection pipeline (was PaneNavigationEngine — D1/D2).
        // The SAME pipeline over pane rects as the window navigation's WindowNavigationEngine:
        // in-direction → aligned → closest gap → largest adjacency → ties to the LAST entry in
        // the iteration (registry) order. TWO pinned deviations from the window engine:
        // (1) the candidate filter adds `gap >= 0` — window rects are disjoint by OS
        // construction (a negative gap cannot occur), but pane rects OVERLAP (the full-width
        // Input underlies both top panes); a negative gap means "overlapping/behind", not "in
        // direction". (2) the DPI divide is DROPPED — all panes share one window, so the
        // closest-gap band is `gap == minGap` (no cross-monitor tolerance to bridge).
        // ================================================================

        private readonly struct Candidate
        {
            public readonly FocusTarget Id;
            public readonly int Gap;
            public readonly int Adjacency;

            public Candidate(FocusTarget id, int gap, int adjacency)
            {
                Id = id;
                Gap = gap;
                Adjacency = adjacency;
            }
        }

        /// <summary>(the pane rects, the focused pane, the direction) → the target pane, or
        /// null when NO pane lies in that direction (the caller no-ops — no wrap).
        /// <paramref name="panes"/> MUST be in registry order [Input, List, Preview] — the
        /// tie-break iterates it and the LAST tie wins (the <c>&gt;=</c> comparison below).</summary>
        private static FocusTarget? SelectTarget(
            IReadOnlyList<KeyValuePair<FocusTarget, PaneRect>> panes,
            FocusTarget current,
            PaneFocusKey direction)
        {
            PaneRect active = GetRect(panes, current);
            if (active.IsEmpty)
            {
                return null;   // no layout yet (pre-RefreshLayout) — a safe no-op
            }

            List<Candidate> passing = new List<Candidate>();
            int minGap = int.MaxValue;
            for (int i = 0; i < panes.Count; i++)
            {
                if (panes[i].Key == current)
                {
                    continue;
                }
                PaneRect c = panes[i].Value;
                if (c.IsEmpty || !IsInDirection(c, active, direction) || !IsAligned(c, active, direction))
                {
                    continue;
                }
                int gap = c.GapTo(active, direction);
                if (gap < 0)
                {
                    continue;   // THE PINNED DEVIATION: overlapping/behind — not "in direction"
                }
                passing.Add(new Candidate(panes[i].Key, gap, c.Adjacency(active, PerpendicularAxis(direction))));
                if (gap < minGap)
                {
                    minGap = gap;
                }
            }

            if (passing.Count == 0)
            {
                return null;
            }

            FocusTarget? best = null;
            int bestAdjacency = int.MinValue;
            foreach (Candidate candidate in passing)
            {
                if (candidate.Gap > minGap)
                {
                    continue;   // outside the closest-gap band (the divide is dropped — §1.2)
                }
                if (best == null || candidate.Adjacency >= bestAdjacency)
                {
                    best = candidate.Id;   // ">=" — the LAST tie in registry order wins (pinned)
                    bestAdjacency = candidate.Adjacency;
                }
            }

            return best;
        }

        private static PaneRect GetRect(IReadOnlyList<KeyValuePair<FocusTarget, PaneRect>> panes, FocusTarget id)
        {
            for (int i = 0; i < panes.Count; i++)
            {
                if (panes[i].Key == id)
                {
                    return panes[i].Value;
                }
            }
            return PaneRect.Empty;
        }

        private static PaneAxis PerpendicularAxis(PaneFocusKey d)
            => (d == PaneFocusKey.Up || d == PaneFocusKey.Down) ? PaneAxis.X : PaneAxis.Y;

        private static bool IsInDirection(PaneRect c, PaneRect a, PaneFocusKey d)
        {
            switch (d)
            {
                case PaneFocusKey.Up: return c.Y < a.Y;
                case PaneFocusKey.Down: return c.Y > a.Y;
                case PaneFocusKey.Left: return c.X < a.X;
                case PaneFocusKey.Right: return c.X > a.X;
                default: throw new ArgumentOutOfRangeException(nameof(d));
            }
        }

        private static bool IsAligned(PaneRect c, PaneRect a, PaneFocusKey d)
        {
            switch (d)
            {
                case PaneFocusKey.Up:
                case PaneFocusKey.Down: return a.X <= c.Right && c.X <= a.Right;
                case PaneFocusKey.Left:
                case PaneFocusKey.Right: return a.Y <= c.Bottom && c.Y <= a.Bottom;
                default: throw new ArgumentOutOfRangeException(nameof(d));
            }
        }
    }
}
