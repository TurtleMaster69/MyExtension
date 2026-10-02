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

        private bool _active;

        // Tag for OUR adornment only. The native VS caret lives on the same "Caret" layer, so we
        // must never RemoveAllAdornments() — that would delete the native caret too (leaving NO
        // caret in insert mode). Removing by tag leaves the native caret untouched.
        private static readonly object AdornmentTag = new object();

        private BlockCaretAdornment(IWpfTextView view)
        {
            _view = view;
            _layer = view.GetAdornmentLayer(LayerName);

            _white = new Rectangle { Fill = new SolidColorBrush(Telescope.Overlay.BlockCaretStyle.WhiteFill) };
            _glyph = new TextBlock
            {
                Foreground = new SolidColorBrush(Telescope.Overlay.BlockCaretStyle.GlyphColor),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
            };
            _block = new Grid { Width = 2, Height = 2 };
            _block.Children.Add(_white);
            _block.Children.Add(_glyph);

            _view.Caret.PositionChanged += OnCaretChanged;
            _view.LayoutChanged += OnLayoutChanged;
            _view.LostAggregateFocus += OnLostFocus;
            _view.Closed += OnClosed;
        }

        /// <summary>True while a block caret should be drawn over this view.</summary>
        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value)
                {
                    return;
                }
                _active = value;
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}block-caret active={_active}");
                Update();
            }
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
            // deactivate the adornment so the native line caret shows. ApplyEditorViewCaret
            // re-activates it when the view regains focus in normal mode.
            Active = false;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _view.Caret.PositionChanged -= OnCaretChanged;
            _view.LayoutChanged -= OnLayoutChanged;
            _view.LostAggregateFocus -= OnLostFocus;
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
            if (!_active)
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

                _glyph.Text = GetCaretChar(caret).ToString();
                _glyph.FontSize = Math.Max(8, _block.Height * 0.8);

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