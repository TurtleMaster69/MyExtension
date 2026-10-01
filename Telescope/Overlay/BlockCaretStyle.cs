using System.Windows;
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

        private static readonly DrawingBrush SharedBrush = BuildBrush();

        /// <summary>Returns the single shared frozen white block-caret brush.</summary>
        public static DrawingBrush CreateBlockBrush() => SharedBrush;

        private static DrawingBrush BuildBrush()
        {
            var drawing = new DrawingBrush(new GeometryDrawing(
                Brushes.White, null, new RectangleGeometry(BlockRect)));
            drawing.Freeze();
            return drawing;
        }
    }
}
