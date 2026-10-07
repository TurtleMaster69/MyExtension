using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyExtension.Adornments
{
    /// <summary>
    /// Draws a <b>block caret</b> over the caret of an editor-hosted text-input tool window (the
    /// Command Window, Immediate Window, ...) while it is in normal mode. The VS editor has no
    /// public block-caret switch, so we render one with a view adornment layer that tracks the
    /// caret position and shows a white block with the caret's character rendered black inside it
    /// (the classic inverted vim caret).
    ///
    /// <para/>
    /// Attached imperatively by <see cref="TextInputToolWindowController.ApplyCaretStyle"/> (stored
    /// per-view in the view's Properties bag); no MEF export is needed because the controller
    /// already has the focused view. <see cref="Active"/> true = block caret (normal mode),
    /// false = no adornment (the editor's native line caret shows in insert mode).
    /// </summary>
    internal sealed class BlockCaretAdornment
    {
        private const string LayerName = PredefinedAdornmentLayers.Caret;

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;

        // The block element: a white rectangle with the caret's character in black, centered.
        private readonly Grid _block;
        private readonly Rectangle _white;
        private readonly TextBlock _glyph;

        // m20 (BP-19): the last glyph char rendered — the per-keystroke Update() only sets
        // _glyph.Text when it changed (no per-caret/layout string churn).
        private char _lastGlyphChar;

        // m20 (BP-19): the glyph FontSize is computed once (on the first layout) and frozen — not
        // recomputed per caret/layout change.
        private double? _glyphFontSize;

        // m57 (BP-D6): the desired/rendered state model is delegated to the pure BlockCaretState
        // (the OverlayKeyHandler/TextMotionNavigator pattern) so the focus-loss/regain contract is
        // unit-testable without a view or an adornment layer.
        private readonly BlockCaretState _state = new BlockCaretState();

        // Tag for OUR adornment only. The native VS caret lives on the same "Caret" layer, so we
        // must never RemoveAllAdornments() — that would delete the native caret too (leaving NO
        // caret in insert mode). Removing by tag leaves the native caret untouched.
        private static readonly object AdornmentTag = new object();

        private BlockCaretAdornment(IWpfTextView view)
        {
            _view = view;
            _layer = view.GetAdornmentLayer(LayerName);

            // m20 (BP-19): the shared frozen brushes (never a per-instance unfrozen brush).
            _white = new Rectangle { Fill = Telescope.Overlay.BlockCaretStyle.WhiteBrush };
            _glyph = new TextBlock
            {
                Foreground = Telescope.Overlay.BlockCaretStyle.GlyphBrush,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
            };
            // m20 (BP-19): the block must not intercept clicks that should place the editor caret.
            _block = new Grid { Width = 2, Height = 2, IsHitTestVisible = false };
            _block.Children.Add(_white);
            _block.Children.Add(_glyph);

            _view.Caret.PositionChanged += OnCaretChanged;
            _view.LayoutChanged += OnLayoutChanged;
            _view.LostAggregateFocus += OnLostFocus;
            // m4 (BP-9): the mirror of LostAggregateFocus — a normal-mode editor view that loses
            // and regains focus must re-render the block caret (the desired state survives focus
            // loss; the rendered state is restored on regain).
            _view.GotAggregateFocus += OnGotFocus;
            _view.Closed += OnClosed;
        }

        /// <summary>True while a block caret SHOULD be drawn over this view (the DESIRED state —
        /// what <see cref="TextMotionHelper.ApplyEditorViewCaret"/> asked for). The RENDERED state
        /// (what the adornment actually draws) is tracked separately (m4/BP-9): a focus loss clears
        /// only the rendered state; a focus regain restores it to the desired value.</summary>
        public bool Active
        {
            get => _state.DesiredActive;
            set
            {
                _state.DesiredActive = value;
                ApplyRendered(value);
            }
        }

        /// <summary>Applies the RENDERED state: logs the <c>block-caret active=</c> diagnostic
        /// (which reflects the rendered state — unchanged literal) and draws/removes the block.
        /// The state tracking itself is delegated to <see cref="BlockCaretState.ApplyRendered"/>.</summary>
        private void ApplyRendered(bool value)
        {
            if (_state.RenderedActive == value)
            {
                return;
            }
            _state.ApplyRendered(value);
            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}block-caret active={_state.RenderedActive}");
            if (!_state.RenderedActive)
            {
                // N4: deactivation must remove OUR adornment. Update() early-returns when
                // inactive (the hot-path guard), so without this the block would persist and
                // the `block-caret active=False` diagnostic would lie.
                _layer.RemoveAdornmentsByTag(AdornmentTag);
            }
            Update();
        }

        /// <summary>Gets (or creates) the block-caret adornment for a view, keyed in its properties.</summary>
        public static BlockCaretAdornment Attach(IWpfTextView view)
        {
            return view.Properties.GetOrCreateSingletonProperty(
                () => new BlockCaretAdornment(view));
        }

        private void OnCaretChanged(object sender, CaretPositionChangedEventArgs e) => Update();

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) => Update();

        private void OnLostFocus(object sender, EventArgs e)
        {
            // R21: a normal-mode block caret must not persist over an unfocused editor view —
            // deactivate the RENDERED state so the native line caret shows. The DESIRED state
            // stays (ApplyEditorViewCaret asked for a block caret); GotAggregateFocus restores it.
            // m57 (BP-D6): the state transition is delegated to BlockCaretState.
            ApplyRendered(false);
        }

        private void OnGotFocus(object sender, EventArgs e)
        {
            // m4 (BP-9): on focus regain, restore the rendered state to the desired value — a
            // normal-mode editor view that lost and regained focus must show the block caret again
            // (the `block-caret active=True` diagnostic is correct immediately, not after a mode
            // toggle or a motion). m57 (BP-D6): the state transition is delegated to BlockCaretState.
            ApplyRendered(_state.DesiredActive);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _view.Caret.PositionChanged -= OnCaretChanged;
            _view.LayoutChanged -= OnLayoutChanged;
            _view.LostAggregateFocus -= OnLostFocus;
            _view.GotAggregateFocus -= OnGotFocus;
            _view.Closed -= OnClosed;
        }

        private void Update()
        {
            if (_view.IsClosed)
            {
                return;
            }

            // n2: when inactive (insert mode) there is no adornment of ours to remove — the
            // deactivation path already removed it — so skip the RemoveAdornmentsByTag call on
            // the caret/layout hot path entirely.
            if (!_state.RenderedActive)
            {
                return;
            }

            // Remove only OUR adornment (never all — the native caret is on this layer too).
            _layer.RemoveAdornmentsByTag(AdornmentTag);

            try
            {
                var caret = _view.Caret.Position.BufferPosition;
                TextBounds bounds = _view.TextViewLines.GetCharacterBounds(caret);

                _block.Width = Math.Max(2, bounds.Width);
                _block.Height = Math.Max(2, bounds.Height);
                Canvas.SetLeft(_block, bounds.Left);
                Canvas.SetTop(_block, bounds.Top);

                // m20 (BP-19): only set _glyph.Text when the caret's character changed, and compute
                // the FontSize once (frozen) instead of per caret/layout change.
                char glyphChar = GetCaretChar(caret);
                if (glyphChar != _lastGlyphChar)
                {
                    _lastGlyphChar = glyphChar;
                    _glyph.Text = glyphChar.ToString();
                }
                if (_glyphFontSize == null)
                {
                    _glyphFontSize = Math.Max(8, _block.Height * 0.8);
                    _glyph.FontSize = _glyphFontSize.Value;
                }

                _layer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled, null, AdornmentTag, _block, null);
            }
            catch
            {
                // caret not yet laid out (e.g. off-screen) — try again on the next caret/layout change
            }
        }

        /// <summary>The character under the caret (blank for line breaks / end of buffer).</summary>
        private char GetCaretChar(int position)
        {
            // ITextSnapshot exposes an indexer returning the char at a snapshot position.
            var snapshot = _view.TextSnapshot;
            if (position < 0 || position >= snapshot.Length)
            {
                return ' ';
            }
            char c = snapshot[position];
            return c == '\n' || c == '\r' ? ' ' : c;
        }
    }
}