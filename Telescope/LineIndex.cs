using System;
using System.Collections.Generic;

namespace Telescope
{
    /// <summary>
    /// Pure index → (line, offset) mapping over a block of text: computes the character offset of
    /// each line's start (line 1 starts at 0; each <c>\n</c> at index <c>i</c> opens the next line
    /// at <c>i + 1</c>) and answers 1-based line lookups via binary search. Shared by the preview
    /// caret placement (M7) and the blank-line fallback (M11) so both use the same semantics as
    /// <see cref="TextMotionNavigator.LineNumber"/> (count of <c>\n</c> in <c>text[0..index)</c> + 1).
    /// </summary>
    internal sealed class LineIndex
    {
        private readonly int[] _lineStarts;
        private readonly int _textLength;

        public LineIndex(string text)
        {
            text ??= string.Empty;
            _textLength = text.Length;
            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    starts.Add(i + 1);
                }
            }
            _lineStarts = starts.ToArray();
        }

        /// <summary>Number of lines (1-based line numbers run 1..<see cref="LineCount"/>).</summary>
        public int LineCount => _lineStarts.Length;

        /// <summary>
        /// 1-based line number of the given character index (clamped to <c>[0, text.Length]</c>):
        /// the largest line start ≤ index. Matches <see cref="TextMotionNavigator.LineNumber"/>.
        /// </summary>
        public int LineOf(int index)
        {
            index = Math.Max(0, Math.Min(index, _textLength));
            int lo = 0;
            int hi = _lineStarts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_lineStarts[mid] <= index)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return lo + 1;
        }

        /// <summary>Character offset of the start of the given 1-based line (clamped to the text).</summary>
        public int LineStart(int line)
        {
            if (line < 1)
            {
                return 0;
            }
            if (line > _lineStarts.Length)
            {
                return _lineStarts[_lineStarts.Length - 1];
            }
            return _lineStarts[line - 1];
        }
    }
}
