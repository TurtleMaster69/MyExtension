using System;

namespace Telescope.Overlay
{
    /// <summary>
    /// The pure logical-shortening helpers (plan D4 / R3+R5). No WPF/VS dependencies.
    /// TailTruncate is for the PATH-LIKE columns (file/dir/path): remove the FRONT, keep
    /// the TAIL, prefix '…' — the end folder + file name survive (the user's explicit
    /// example: C:\Very\Long\Path\Models\Services\Order.cs → …\Services\Order.cs;
    /// Telescope.nvim's path_display="truncate" is the reference). EndTruncate is for the
    /// TEXT columns (message/text/symbol/kind/access/line/column): remove the END, suffix
    /// '…' — the start of a line is the important part there.
    /// </summary>
    internal static class ColumnTruncation
    {
        /// <summary>The single-character ellipsis both helpers pin.</summary>
        internal const string Ellipsis = "…";

        /// <summary>Path-like shortening: '…' + the LAST (maxWidth-1) characters.</summary>
        internal static string TailTruncate(string text, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (maxWidth <= 0) return string.Empty;
            if (text.Length <= maxWidth) return text;
            if (maxWidth == 1) return Ellipsis;
            return Ellipsis + text.Substring(text.Length - (maxWidth - 1));
        }

        /// <summary>Text shortening: the FIRST (maxWidth-1) characters + '…'.</summary>
        internal static string EndTruncate(string text, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (maxWidth <= 0) return string.Empty;
            if (text.Length <= maxWidth) return text;
            if (maxWidth == 1) return Ellipsis;
            return text.Substring(0, maxWidth - 1) + Ellipsis;
        }

        /// <summary>Applies the column's truncation kind at the given char width (the cell
        /// rendering's single entry point — plan D4's wiring seam).</summary>
        internal static string Apply(ResultColumnTruncation kind, string text, int maxWidth)
            => kind == ResultColumnTruncation.Tail ? TailTruncate(text, maxWidth) : EndTruncate(text, maxWidth);
    }
}
