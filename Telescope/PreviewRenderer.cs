using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Telescope
{
    /// <summary>
    /// Renders a finder hit's file into the overlay's preview pane: reads the file, tokenizes it
    /// into syntax-highlighted runs, jumps the caret to the hit's line, and logs the preview
    /// diagnostics. Owns the read/tokenize/caret/log pipeline so the overlay's per-hit branches
    /// collapse to a single <see cref="IFileLocation"/> dispatch.
    /// </summary>
    internal static class PreviewRenderer
    {
        public static void Show(RichTextBox previewBox, TextMotionNavigator navigator, IFileLocation location)
        {
            if (!System.IO.File.Exists(location.FilePath))
            {
                SetContent(previewBox, navigator, string.Empty);
                return;
            }

            try
            {
                string content = System.IO.File.ReadAllText(location.FilePath);
                SetContent(previewBox, navigator, content);
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

        public static void SetContent(RichTextBox previewBox, TextMotionNavigator navigator, string content)
        {
            content ??= string.Empty;
            navigator.SetText(content);

            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)),
            };

            var segments = SyntaxHighlighter.Segment(content);
            var para = NewPreviewParagraph();
            foreach (var segment in segments)
            {
                string[] lines = segment.Text.Split('\n');
                for (int k = 0; k < lines.Length; k++)
                {
                    if (k > 0)
                    {
                        doc.Blocks.Add(para);
                        para = NewPreviewParagraph();
                    }
                    if (lines[k].Length > 0)
                    {
                        para.Inlines.Add(new Run(lines[k])
                        {
                            Foreground = ColorFor(segment.Category),
                        });
                    }
                }
            }
            doc.Blocks.Add(para);

            previewBox.Document = doc;
            previewBox.CaretPosition = doc.ContentStart;
            previewBox.ScrollToHome();
            TelescopeLog.Log($"preview tokens={segments.Count}");
        }

        public static void ApplyCaret(RichTextBox previewBox, TextMotionNavigator navigator)
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

        private static SolidColorBrush ColorFor(SyntaxCategory category)
        {
            // One-Dark/GitHub-dark palette that matches the overlay's dark chrome.
            switch (category)
            {
                case SyntaxCategory.Keyword: return new SolidColorBrush(Color.FromRgb(0xc7, 0x92, 0xea));
                case SyntaxCategory.String: return new SolidColorBrush(Color.FromRgb(0x98, 0xc3, 0x79));
                case SyntaxCategory.Comment: return new SolidColorBrush(Color.FromRgb(0x7f, 0x84, 0x8e));
                case SyntaxCategory.Number: return new SolidColorBrush(Color.FromRgb(0xd1, 0x9a, 0x66));
                default: return new SolidColorBrush(Color.FromRgb(0xc9, 0xd1, 0xd9));
            }
        }

        private static TextPointer CaretToPointer(RichTextBox previewBox, int index)
        {
            FlowDocument doc = previewBox.Document;
            int plain = 0;
            int blockCount = doc.Blocks.Count;
            int blockIndex = 0;

            foreach (var block in doc.Blocks)
            {
                bool lastBlock = ++blockIndex == blockCount;
                if (block is Paragraph para)
                {
                    foreach (var inline in para.Inlines)
                    {
                        if (inline is Run run)
                        {
                            int len = run.Text.Length;
                            if (index <= plain + len)
                            {
                                return run.ContentStart.GetPositionAtOffset(index - plain, LogicalDirection.Forward);
                            }
                            plain += len;
                        }
                    }
                }
                if (!lastBlock)
                {
                    plain += 1; // the '\n' separating this line from the next
                }
            }

            return doc.ContentEnd;
        }
    }
}
