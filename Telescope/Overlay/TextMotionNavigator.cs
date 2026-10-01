using System;

namespace Telescope.Overlay
{
    /// <summary>
    /// Dependency-free vim-style cursor navigation over a block of text, shared by BOTH consumers:
    ///
    /// <list type="bullet">
    /// <item><b>Telescope file-preview pane</b> — read-only code preview (h/l/j/k/w/b/e/0/$/gg/G).</item>
    /// <item><b>Text-input tool windows</b> (Command Window, Find and Replace, Immediate Window, ...) —
    ///   normal-mode caret motions (h/l/w/b/e) plus the insert-position motions a/A/I.</item>
    /// </list>
    ///
    /// It holds the full text plus a caret index and mutates that caret via the motion methods.
    /// Pure state machine (no WPF / VS), so the motions are unit-tested hermetically; the WPF side
    /// reads the resulting <see cref="Caret"/> and applies it to the focused control.
    ///
    /// <para/>
    /// <b>Caret model:</b> a caret index in <c>0..Text.Length</c>, where <c>Length</c> is "after the
    /// last character". Word motions treat runs of non-whitespace as words.
    /// </summary>
    internal sealed class TextMotionNavigator
    {
        private string _text = string.Empty;
        private int _caret;

        /// <summary>The text being navigated.</summary>
        public string Text => _text;

        /// <summary>Current caret position (character index into <see cref="Text"/>).</summary>
        public int Caret => _caret;

        /// <summary>Total length of the text.</summary>
        public int Length => _text.Length;

        /// <summary>Current 1-based line number (1 = first line).</summary>
        public int LineNumber
        {
            get
            {
                int line = 1;
                for (int i = 0; i < _caret && i < _text.Length; i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                    }
                }
                return line;
            }
        }

        /// <summary>Replaces the text and resets the caret to the start.</summary>
        public void SetText(string text)
        {
            _text = text ?? string.Empty;
            _caret = 0;
        }

        /// <summary>Sets the caret to a specific character index, clamped into range.</summary>
        public void MoveTo(int index)
        {
            _caret = Math.Max(0, Math.Min(index, _text.Length));
        }

        /// <summary>Character left (h).</summary>
        public void Left() => MoveTo(_caret - 1);

        /// <summary>Character right (l).</summary>
        public void Right() => MoveTo(_caret + 1);

        /// <summary>Next line, keeping the column where possible (j).</summary>
        public void Down()
        {
            int lineStart = LineStart(_caret);
            int column = _caret - lineStart;
            int next = _text.IndexOf('\n', _caret);
            if (next < 0)
            {
                MoveTo(_text.Length);
                return;
            }
            int nextStart = next + 1;
            int target = nextStart + column;
            int lineEnd = _text.IndexOf('\n', nextStart);
            int maxIndex = lineEnd < 0 ? _text.Length : lineEnd;
            MoveTo(Math.Min(target, maxIndex));
        }

        /// <summary>Previous line, keeping the column where possible (k).</summary>
        public void Up()
        {
            if (_caret == 0)
            {
                return;
            }
            int lineStart = LineStart(_caret);
            int column = Math.Max(0, _caret - lineStart);
            if (lineStart == 0)
            {
                MoveTo(0);
                return;
            }
            // The current line's start is the char after a '\n' at (lineStart-1). The previous
            // line ends at the '\n' BEFORE that one, so search from (lineStart-2). M10: on a
            // leading blank line (text starting with '\n') lineStart-2 is -1 — clamping it to 0
            // finds the '\n' at index 0 and Up() stays put; only search from lineStart-2 when it
            // is >= 0, else there is no previous newline (prevStart = 0).
            int prevNewline = lineStart - 2 >= 0 ? _text.LastIndexOf('\n', lineStart - 2) : -1;
            int prevStart = prevNewline < 0 ? 0 : prevNewline + 1;
            int prevEnd = _text.IndexOf('\n', prevStart);
            int maxIndex = (prevEnd < 0 ? _text.Length : prevEnd);
            MoveTo(Math.Min(prevStart + column, maxIndex));
        }

        /// <summary>Next word start (w).</summary>
        public void NextWord()
        {
            int i = _caret;
            while (i < _text.Length && !char.IsWhiteSpace(_text[i])) i++;
            while (i < _text.Length && char.IsWhiteSpace(_text[i])) i++;
            MoveTo(i);
        }

        /// <summary>Previous word start (b).</summary>
        public void PrevWord()
        {
            int i = _caret;
            while (i > 0 && char.IsWhiteSpace(_text[i - 1])) i--;
            while (i > 0 && !char.IsWhiteSpace(_text[i - 1])) i--;
            MoveTo(i);
        }

        /// <summary>End of the current/next word (e).</summary>
        public void EndWord()
        {
            int i = _caret;
            while (i < _text.Length && char.IsWhiteSpace(_text[i])) i++;
            while (i < _text.Length - 1 && !char.IsWhiteSpace(_text[i + 1])) i++;
            MoveTo(i + 1);
        }

        /// <summary>Start of line (0).</summary>
        public void LineStart() => MoveTo(LineStart(_caret));

        /// <summary>End of line ($).</summary>
        public void LineEnd()
        {
            int end = _text.IndexOf('\n', _caret);
            MoveTo(end < 0 ? _text.Length : end);
        }

        /// <summary>First line (gg).</summary>
        public void Top() => MoveTo(0);

        /// <summary>Last line (G).</summary>
        public void Bottom() => MoveTo(_text.Length);

        /// <summary>Jumps the caret to the start of the given 1-based line (clamped to the file).</summary>
        public void MoveToLine(int line)
        {
            if (line <= 1)
            {
                MoveTo(0);
                return;
            }

            int current = 1;
            int target = _text.Length; // default: last line start when the requested line is past the end
            int lastNewline = -1;
            for (int i = 0; i < _text.Length; i++)
            {
                if (_text[i] == '\n')
                {
                    current++;
                    lastNewline = i;
                    if (current == line)
                    {
                        target = i + 1;
                        break;
                    }
                }
            }
            if (current < line)
            {
                // Requested line is past the last newline (single-line text or short file).
                target = lastNewline < 0 ? 0 : lastNewline + 1;
            }
            MoveTo(target);
        }

        /// <summary>Insert position just after the caret character (a) — enter insert here.</summary>
        public void InsertAfter() => MoveTo(_caret < _text.Length ? _caret + 1 : _text.Length);

        /// <summary>Insert position at the end of the text/line (A) — enter insert here.</summary>
        public void InsertEnd() => MoveTo(_text.Length);

        /// <summary>Insert position at the start of the text/line (I) — enter insert here.</summary>
        public void InsertStart() => MoveTo(0);

        private static int LineStart(int index, string text)
        {
            if (index <= 0 || text == null)
            {
                return 0;
            }
            int nl = text.LastIndexOf('\n', Math.Min(index - 1, text.Length - 1));
            return nl < 0 ? 0 : nl + 1;
        }

        private int LineStart(int index) => LineStart(index, _text);
    }
}