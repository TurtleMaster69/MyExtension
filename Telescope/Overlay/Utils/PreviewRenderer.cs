using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Telescope.Finders;
using Telescope.Logging;

namespace Telescope.Overlay
{
    /// <summary>
    /// Renders a finder hit's file into the overlay's preview pane: reads the file, tokenizes it
    /// into syntax-highlighted runs, jumps the caret to the hit's line, and logs the preview
    /// diagnostics. Owns the read/tokenize/caret/log pipeline so the overlay's per-hit branches
    /// collapse to a single <see cref="IFileLocation"/> dispatch.
    ///
    /// <para/>
    /// <b>Instance state (M5):</b> the per-content line index + line-start pointers and the
    /// mtime-keyed content cache are instance-scoped (one preview box per overlay), so no shared
    /// static mutable state leaks across preview boxes. The file is re-read and re-tokenized only
    /// when its <c>LastWriteTimeUtc</c> changes.
    /// </summary>
    internal sealed class PreviewRenderer
    {
        // Per-content line index + one TextPointer per line start, built in SetContent so
        // CaretToPointer can binary-search the line and walk only that paragraph's runs (M7).
        private LineIndex? _lineIndex;
        private TextPointer[]? _linePointers;

        // M5: mtime-keyed content cache — the file is re-read only when its LastWriteTimeUtc
        // changes, so moving the selection between hits in the same file does not re-read +
        // re-tokenize the whole file per selection change.
        private readonly FileContentCache _contentCache = new FileContentCache(500);

        // M4: mtime-keyed token cache — the tokenized syntax segments are cached so SetContent
        // re-tokenizes only on content change (the FlowDocument rebuild stays the renderer's job).
        // N33/BP-46: this cache also serves the FlowDocument-rebuild decision (ShouldRebuild),
        // subsuming the deleted PreviewDocumentCache.
        private readonly PreviewTokenCache _tokenCache = new PreviewTokenCache();

        public void Show(RichTextBox previewBox, TextMotionNavigator navigator, IFileLocation location)
        {
            if (!System.IO.File.Exists(location.FilePath))
            {
                SetContent(previewBox, navigator, null, string.Empty);
                return;
            }

            try
            {
                string content = _contentCache.GetContent(location.FilePath);
                SetContent(previewBox, navigator, location.FilePath, content);
                if (location.LineNumber > 0)
                {
                    navigator.MoveToLine(location.LineNumber);
                    ApplyCaret(previewBox, navigator);
                    TelescopeLog.Log($"preview caret={navigator.Caret} line={navigator.LineNumber}");
                }
                TelescopeLog.Log($"preview file={location.FilePath} chars={content.Length}");
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"preview load failed: {ex.Message}");
            }
        }

        public void SetContent(RichTextBox previewBox, TextMotionNavigator navigator, string? path, string content)
        {
            content ??= string.Empty;
            navigator.SetText(content);

            // R2: the FlowDocument + line pointers are rebuilt only when the file's content changed
            // (mtime-keyed decision). Moving the selection between hits in the same file keeps the
            // existing document; MoveToLine/ApplyCaret remain the per-selection work. A null path
            // (empty content) always rebuilds so the preview clears.
            if (path != null && !_tokenCache.ShouldRebuild(path))
            {
                return;
            }

            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)),
            };

            // M4: the tokenized segments are cached keyed by the file's LastWriteTimeUtc, so
            // moving the selection between hits in the same file does not re-tokenize per change.
            // N34/BP-47: pass the already-read content through so a cache miss does not read the
            // file a second time.
            var segments = path != null
                ? _tokenCache.GetSegments(path, content, SyntaxHighlighter.Tokenize)
                : SyntaxHighlighter.Tokenize(content);
            var para = NewPreviewParagraph();
            foreach (var segment in segments)
            {
                string text = segment.Text;
                int start = 0;
                int k = 0;
                while (true)
                {
                    int nl = text.IndexOf('\n', start);
                    string line = nl < 0 ? text.Substring(start) : text.Substring(start, nl - start);
                    if (k > 0)
                    {
                        doc.Blocks.Add(para);
                        para = NewPreviewParagraph();
                    }
                    if (line.Length > 0)
                    {
                        para.Inlines.Add(new Run(line)
                        {
                            Foreground = ColorFor(segment.Category),
                        });
                    }
                    if (nl < 0)
                    {
                        break;
                    }
                    start = nl + 1;
                    k++;
                }
            }
            doc.Blocks.Add(para);

            _lineIndex = new LineIndex(content);
            var pointers = new List<TextPointer>();
            foreach (var block in doc.Blocks)
            {
                if (block is Paragraph p)
                {
                    pointers.Add(p.ContentStart);
                }
            }
            _linePointers = pointers.ToArray();

            previewBox.Document = doc;
            previewBox.CaretPosition = doc.ContentStart;
            previewBox.ScrollToHome();
            TelescopeLog.Log($"preview tokens={segments.Count}");
        }

        public void ApplyCaret(RichTextBox previewBox, TextMotionNavigator navigator)
        {
            previewBox.CaretPosition = CaretToPointer(previewBox, navigator.Caret);
            // Scroll so the caret's line is visible (RichTextBox has no ScrollToCaret).
            try
            {
                Rect caretRect = previewBox.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                previewBox.ScrollToVerticalOffset(caretRect.Top);
            }
            catch
            {
                // scroll is best-effort
            }
        }

        private static Paragraph NewPreviewParagraph()
        {
            return new Paragraph
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
            };
        }

        // N35/BP-48: one frozen brush per syntax category, reused across every FlowDocument
        // rebuild instead of allocating a new SolidColorBrush per segment.
        private static readonly SolidColorBrush KeywordBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xc7, 0x92, 0xea)));
        private static readonly SolidColorBrush StringBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x98, 0xc3, 0x79)));
        private static readonly SolidColorBrush CommentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x7f, 0x84, 0x8e)));
        private static readonly SolidColorBrush NumberBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xd1, 0x9a, 0x66)));
        private static readonly SolidColorBrush DefaultBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xc9, 0xd1, 0xd9)));

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush ColorFor(SyntaxCategory category)
        {
            // One-Dark/GitHub-dark palette that matches the overlay's dark chrome.
            switch (category)
            {
                case SyntaxCategory.Keyword: return KeywordBrush;
                case SyntaxCategory.String: return StringBrush;
                case SyntaxCategory.Comment: return CommentBrush;
                case SyntaxCategory.Number: return NumberBrush;
                default: return DefaultBrush;
            }
        }

        private TextPointer CaretToPointer(RichTextBox previewBox, int index)
        {
            FlowDocument doc = previewBox.Document;
            if (_lineIndex == null || _linePointers == null)
            {
                return doc.ContentEnd;
            }

            int line = _lineIndex.LineOf(index);
            if (line < 1 || line > _linePointers.Length)
            {
                return doc.ContentEnd;
            }

            TextPointer lineStart = _linePointers[line - 1];
            int offset = index - _lineIndex.LineStart(line);
            if (offset < 0)
            {
                offset = 0;
            }

            if (lineStart.Paragraph is Paragraph para)
            {
                int plain = 0;
                if (offset == plain) return para.ContentStart;
                foreach (var inline in para.Inlines)
                {
                    if (inline is Run run)
                    {
                        int len = run.Text.Length;
                        if (offset <= plain + len)
                        {
                            return run.ContentStart.GetPositionAtOffset(offset - plain, LogicalDirection.Forward);
                        }
                        plain += len;
                    }
                }
            }

            // Offset past the paragraph's runs (e.g. an index at a '\n' or an empty line): land at
            // the end of the line, matching the current behavior.
            return lineStart.Paragraph?.ContentEnd ?? doc.ContentEnd;
        }
    }
}
