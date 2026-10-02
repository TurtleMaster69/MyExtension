using System;
using System.Collections.Generic;
using System.Text;

namespace Telescope.Overlay
{
    /// <summary>Visual category of a highlighted source segment.</summary>
    internal enum SyntaxCategory
    {
        Default,
        Comment,
        String,
        Keyword,
        Number,
    }

    /// <summary>A contiguous run of source text with a single visual category.</summary>
    internal readonly struct SyntaxSegment
    {
        public SyntaxSegment(string text, SyntaxCategory category)
        {
            Text = text;
            Category = category;
        }

        /// <summary>The segment text (may span lines for block comments / verbatim strings).</summary>
        public string Text { get; }

        public SyntaxCategory Category { get; }
    }

    /// <summary>
    /// Dependency-free lexical highlighter for the Telescope file-preview pane. Splits source text
    /// into colored segments (keywords, strings, comments, numbers, default), so the overlay can
    /// render them as per-color WPF runs. Pure logic — no WPF/VS — so it is unit-tested hermetically.
    ///
    /// <para/>
    /// The scanner is C#-oriented but the keyword set is small and shared, so other code languages
    /// still get reasonable comment/string/number coloring.
    /// </summary>
    internal static class SyntaxHighlighter
    {
        private static readonly HashSet<string> _keywords = new(StringComparer.Ordinal)
        {
            // C# reserved keywords
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",
            // Contextual keywords
            "async", "await", "var", "record", "init", "required", "global", "partial", "yield",
            "get", "set", "where", "when", "nameof", "not", "and", "or", "file", "scoped", "value",
        };

        /// <summary>
        /// Splits <paramref name="text"/> into colored segments, in order. Handles multi-line
        /// constructs (block comments, verbatim strings) by emitting segments that contain '\n'.
        /// </summary>
        public static IReadOnlyList<SyntaxSegment> Tokenize(string text)
        {
            var result = new List<SyntaxSegment>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            int i = 0;
            int len = text.Length;

            while (i < len)
            {
                char c = text[i];
                char next = i + 1 < len ? text[i + 1] : '\0';

                // Line comment: // ... to end of line (the '\n' is left to the default scanner).
                if (c == '/' && next == '/')
                {
                    int start = i;
                    i += 2;
                    while (i < len && text[i] != '\n') i++;
                    result.Add(new SyntaxSegment(text.Substring(start, i - start), SyntaxCategory.Comment));
                    continue;
                }

                // Block comment: /* ... */ (may span lines).
                if (c == '/' && next == '*')
                {
                    int start = i;
                    i += 2;
                    while (i < len && !(text[i] == '*' && i + 1 < len && text[i + 1] == '/')) i++;
                    if (i < len)
                    {
                        i += 2; // consume the closing */
                    }
                    result.Add(new SyntaxSegment(text.Substring(start, i - start), SyntaxCategory.Comment));
                    continue;
                }

                // Verbatim string: @"..." (may span lines; "" is an escaped quote).
                if (c == '@' && next == '"')
                {
                    int start = i;
                    i += 2;
                    while (i < len)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < len && text[i + 1] == '"')
                            {
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        i++;
                    }
                    result.Add(new SyntaxSegment(text.Substring(start, i - start), SyntaxCategory.String));
                    continue;
                }

                // Interpolated string: $"..."
                if (c == '$' && next == '"')
                {
                    result.Add(ReadQuoted(text, ref i, '"'));
                    continue;
                }

                // Normal string: "..."
                if (c == '"')
                {
                    result.Add(ReadQuoted(text, ref i, '"'));
                    continue;
                }

                // Character literal: '...'
                if (c == '\'')
                {
                    result.Add(ReadQuoted(text, ref i, '\''));
                    continue;
                }

                // Number: 123, 0x1F, 1.5e-3, 100L (a leading '.' is included for ".5").
                if (char.IsDigit(c) || (c == '.' && char.IsDigit(next)))
                {
                    result.Add(ReadNumber(text, ref i));
                    continue;
                }

                // Identifier (possibly a keyword).
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string word = text.Substring(start, i - start);
                    result.Add(new SyntaxSegment(word, _keywords.Contains(word) ? SyntaxCategory.Keyword : SyntaxCategory.Default));
                    continue;
                }

                // Anything else: a default run up to the next token-start character.
                int dStart = i;
                while (i < len)
                {
                    char d = text[i];
                    char dNext = i + 1 < len ? text[i + 1] : '\0';
                    if ((d == '/' && (dNext == '/' || dNext == '*'))
                        || d == '"' || d == '\''
                        || (d == '@' && dNext == '"')
                        || (d == '$' && dNext == '"')
                        || char.IsDigit(d)
                        || (d == '.' && char.IsDigit(dNext))
                        || char.IsLetter(d) || d == '_')
                    {
                        break;
                    }
                    i++;
                }
                result.Add(new SyntaxSegment(text.Substring(dStart, i - dStart), SyntaxCategory.Default));
            }

            return result;
        }

        /// <summary>Reads a quoted token starting at the opening quote (which is at <paramref name="i"/>).</summary>
        private static SyntaxSegment ReadQuoted(string text, ref int i, char quote)
        {
            int start = i;
            i++; // skip the opening quote
            while (i < text.Length)
            {
                if (text[i] == '\\')
                {
                    i += (i + 1 < text.Length) ? 2 : 1; // escaped character, e.g. \" or \n
                    continue;
                }
                if (text[i] == quote)
                {
                    i++; // closing quote
                    break;
                }
                i++;
            }
            return new SyntaxSegment(text.Substring(start, i - start), SyntaxCategory.String);
        }

        /// <summary>Reads a numeric literal starting at the first digit (or the '.' of ".5").</summary>
        private static SyntaxSegment ReadNumber(string text, ref int i)
        {
            int start = i;
            int len = text.Length;

            if (text[i] == '0' && i + 1 < len && (text[i + 1] == 'x' || text[i + 1] == 'X'))
            {
                i += 2;
                while (i < len && (Uri.IsHexDigit(text[i]) || text[i] == '_')) i++;
                while (i < len && (char.IsLetter(text[i]) || text[i] == '_')) i++; // suffix
                return new SyntaxSegment(text.Substring(start, i - start), SyntaxCategory.Number);
            }

            while (i < len && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == '_')) i++;
            if (i < len && (text[i] == 'e' || text[i] == 'E'))
            {
                int expStart = i;
                i++;
                if (i < len && (text[i] == '+' || text[i] == '-')) i++;
                if (i < len && char.IsDigit(text[i]))
                {
                    while (i < len && char.IsDigit(text[i])) i++;
                }
                else
                {
                    i = expStart; // not an exponent — backtrack so the 'e' scans as an identifier
                }
            }
            while (i < len && (char.IsLetter(text[i]) || text[i] == '_')) i++; // suffix (f/d/m/L/U)

            return new SyntaxSegment(text.Substring(start, i - start), SyntaxCategory.Number);
        }
    }
}