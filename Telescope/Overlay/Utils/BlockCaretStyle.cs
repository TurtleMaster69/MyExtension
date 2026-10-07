using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Telescope.Overlay
{
    /// <summary>
    /// Shared block-caret brush/geometry used by the Telescope prompt, the tool-window text
    /// surfaces, and the editor-view block-caret adornment. One frozen white DrawingBrush instance
    /// is cached so every consumer draws the SAME block caret.
    /// </summary>
    internal static class BlockCaretStyle
    {
        public static readonly Color WhiteFill = Colors.White;
        public static readonly Color GlyphColor = Colors.Black;
        public static readonly Rect BlockRect = new Rect(0, 0, 8, 16);

        // m20 (BP-19): frozen SolidColorBrush instances (created frozen from WhiteFill/GlyphColor)
        // so the BlockCaretAdornment never allocates a per-instance unfrozen brush.
        public static readonly SolidColorBrush WhiteBrush = CreateFrozenBrush(WhiteFill);
        public static readonly SolidColorBrush GlyphBrush = CreateFrozenBrush(GlyphColor);

        private static readonly DrawingBrush SharedBrush = BuildBrush();

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>Returns the single shared frozen white block-caret brush.</summary>
        public static DrawingBrush CreateBlockBrush() => SharedBrush;

        /// <summary>
        /// Sets block (normal) vs line (insert) caret on a WPF TextBox (m35 — the single shared
        /// caret-style helper for the Telescope prompt and the tool-window text surfaces).
        /// <paramref name="lineCaretBrush"/> is the insert-mode line caret brush (null = the
        /// TextBox default).
        /// </summary>
        public static void ApplyCaretStyle(TextBox box, bool isInputMode, Brush? lineCaretBrush = null)
        {
            try
            {
                box.CaretBrush = isInputMode ? lineCaretBrush : CreateBlockBrush();
            }
            catch
            {
                // caret styling is best-effort
            }
        }

        private static DrawingBrush BuildBrush()
        {
            var drawing = new DrawingBrush(new GeometryDrawing(
                Brushes.White, null, new RectangleGeometry(BlockRect)));
            drawing.Freeze();
            return drawing;
        }
    }
}
