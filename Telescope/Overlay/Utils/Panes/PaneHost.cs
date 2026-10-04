// Telescope/Overlay/Utils/Panes/PaneHost.cs
using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace Telescope.Overlay
{
    /// <summary>
    /// The ordered pane registry + activation + left-click wiring (Feature 7). THIN by design:
    /// every focus DECISION lives in the pure <see cref="FocusTargetModel"/> (unit-tested); the
    /// host only APPLIES decisions (deactivate the old pane, activate the new) and normalizes
    /// left-clicks into <see cref="PaneClicked"/> notifications the overlay routes through the
    /// machine. The registry order is pinned [Input, List, Preview] (the layout: Input bottom,
    /// List left, Preview right). UI thread only.
    /// </summary>
    internal sealed class PaneHost
    {
        private readonly List<IPane> _panes;
        private IPane? _active;

        /// <summary>
        /// Raised when a pane is left-clicked (the WPF PreviewMouseDown tunneling handler; LEFT
        /// button only — right-click passes through to the header chooser). The overlay routes it
        /// through the focus machine (the click normalization) — the host never decides focus.
        /// </summary>
        public event Action<FocusTarget>? PaneClicked;

        public PaneHost(params IPane[] panes)
        {
            _panes = new List<IPane>(panes ?? throw new ArgumentNullException(nameof(panes)));
            foreach (IPane pane in _panes)
            {
                FocusTarget id = pane.Id;   // capture per pane (no closure-over-loop-variable trap)
                pane.Content.PreviewMouseDown += (_, e) =>
                {
                    if (e.ChangedButton == MouseButton.Left)
                    {
                        NotifyClicked(id);
                    }
                };
            }
        }

        /// <summary>The pane registered for <paramref name="id"/>, or null.</summary>
        public IPane? GetPane(FocusTarget id) => _panes.Find(p => p.Id == id);

        /// <summary>The registered panes in REGISTRY order — the overlay's RefreshPaneLayout
        /// iterates this to measure the layout rects the geometric focus move consumes
        /// (plan §1.2: the host measures; the machine never reads WPF).</summary>
        public IEnumerable<IPane> Panes => _panes;

        /// <summary>
        /// Applies a focus decision: deactivates the previously active pane, activates the new
        /// one. Idempotent (re-activating the active pane does NOT re-deactivate it). Unknown id
        /// → no-op (defense; the machine only produces registered ids).
        /// </summary>
        public void Activate(FocusTarget id)
        {
            IPane? pane = GetPane(id);
            if (pane == null)
            {
                return;
            }
            if (_active != null && !ReferenceEquals(_active, pane))
            {
                _active.Deactivate();
            }
            _active = pane;
            pane.Activate();
        }

        /// <summary>The testable click seam: the WPF handler delegates here; unit tests call it
        /// directly (no mouse simulation needed).</summary>
        public void NotifyClicked(FocusTarget id) => PaneClicked?.Invoke(id);
    }
}
