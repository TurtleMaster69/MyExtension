using System;
using System.Collections.Generic;

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

    /// <summary>
    /// A contiguous run of source text with a single visual category. N64/BP-60: the segment stores
    /// the source string plus a start/length span instead of a materialized substring, so the
    /// tokenizer does not allocate a string per token; <see cref="Text"/> materializes on demand.
    /// </summary>
    internal readonly struct SyntaxSegment
    {
        private readonly string _source;
        private readonly int _start;
        private readonly int _length;

        public SyntaxSegment(string source, int start, int length, SyntaxCategory category)
        {
            _source = source;
            _start = start;
            _length = length;
            Category = category;
        }

        /// <summary>The segment text (may span lines for block comments / verbatim strings).</summary>
        public string Text => _source.Substring(_start, _length);

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

        // N64/BP-60: keywords bucketed by length so an identifier can be classified without
        // materializing a substring (ordinal compare against the source span).
        private static readonly Dictionary<int, List<string>> _keywordsByLength = BuildKeywordsByLength();

        private static Dictionary<int, List<string>> BuildKeywordsByLength()
        {
            var map = new Dictionary<int, List<string>>();
            foreach (string keyword in _keywords)
            {
                if (!map.TryGetValue(keyword.Length, out var list))
                {
                    list = new List<string>();
                    map[keyword.Length] = list;
                }
                list.Add(keyword);
            }
            return map;
        }

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

                if (c == '/' && next == '/')
                {
                    result.Add(ReadLineComment(text, ref i));
                }
                else if (c == '/' && next == '*')
                {
                    result.Add(ReadBlockComment(text, ref i));
                }
                else if (c == '@' && next == '"')
                {
                    result.Add(ReadVerbatimString(text, ref i, prefixLength: 1));
                }
                else if (c == '$' && next == '@' && i + 2 < len && text[i + 2] == '"')
                {
                    // N67/BP-63: interpolated verbatim string $@"..." (the '$' + '"' branch misses it).
                    result.Add(ReadVerbatimString(text, ref i, prefixLength: 2));
                }
                else if (c == '$' && next == '"')
                {
                    result.Add(ReadQuoted(text, ref i, '"'));
                }
                else if (c == '"')
                {
                    result.Add(ReadQuoted(text, ref i, '"'));
                }
                else if (c == '\'')
                {
                    result.Add(ReadQuoted(text, ref i, '\''));
                }
                else if (char.IsDigit(c) || (c == '.' && char.IsDigit(next)))
                {
                    result.Add(ReadNumber(text, ref i));
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    result.Add(ReadIdentifier(text, ref i));
                }
                else
                {
                    result.Add(ReadDefault(text, ref i));
                }
            }

            return result;
        }

        /// <summary>Line comment: <c>// ...</c> to end of line (the '\n' is left to the default scanner).</summary>
        private static SyntaxSegment ReadLineComment(string text, ref int i)
        {
            int start = i;
            i += 2;
            int len = text.Length;
            while (i < len && text[i] != '\n') i++;
            return new SyntaxSegment(text, start, i - start, SyntaxCategory.Comment);
        }

        /// <summary>Block comment: <c>/* ... */</c> (may span lines).</summary>
        private static SyntaxSegment ReadBlockComment(string text, ref int i)
        {
            int start = i;
            i += 2;
            int len = text.Length;
            while (i < len && !(text[i] == '*' && i + 1 < len && text[i + 1] == '/')) i++;
            if (i < len)
            {
                i += 2; // consume the closing */
            }
            return new SyntaxSegment(text, start, i - start, SyntaxCategory.Comment);
        }

        /// <summary>
        /// Verbatim string: <c>@"..."</c> or <c>$@"..."</c> (may span lines; <c>""</c> is an escaped
        /// quote). <paramref name="prefixLength"/> is 1 for <c>@</c> and 2 for <c>$@</c>.
        /// </summary>
        private static SyntaxSegment ReadVerbatimString(string text, ref int i, int prefixLength)
        {
            int start = i;
            i += prefixLength + 1; // skip the prefix + the opening quote
            int len = text.Length;
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
            return new SyntaxSegment(text, start, i - start, SyntaxCategory.String);
        }

        /// <summary>Identifier (possibly a keyword).</summary>
        private static SyntaxSegment ReadIdentifier(string text, ref int i)
        {
            int start = i;
            int len = text.Length;
            while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
            int length = i - start;
            return new SyntaxSegment(text, start, length,
                IsKeyword(text, start, length) ? SyntaxCategory.Keyword : SyntaxCategory.Default);
        }

        /// <summary>Ordinal keyword check against the source span (no substring allocation).</summary>
        private static bool IsKeyword(string text, int start, int length)
        {
            if (!_keywordsByLength.TryGetValue(length, out var candidates))
            {
                return false;
            }
            foreach (string candidate in candidates)
            {
                if (string.CompareOrdinal(text, start, candidate, 0, length) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Anything else: a default run up to the next token-start character.</summary>
        private static SyntaxSegment ReadDefault(string text, ref int i)
        {
            int start = i;
            int len = text.Length;
            while (i < len)
            {
                char d = text[i];
                char dNext = i + 1 < len ? text[i + 1] : '\0';
                if (IsTokenStart(d, dNext))
                {
                    break;
                }
                i++;
            }
            return new SyntaxSegment(text, start, i - start, SyntaxCategory.Default);
        }

        /// <summary>True when the character pair begins a token the main scanner handles.</summary>
        private static bool IsTokenStart(char c, char next)
        {
            return (c == '/' && (next == '/' || next == '*'))
                || c == '"' || c == '\''
                || (c == '@' && next == '"')
                || (c == '$' && (next == '"' || next == '@'))
                || char.IsDigit(c)
                || (c == '.' && char.IsDigit(next))
                || char.IsLetter(c) || c == '_';
        }

        /// <summary>Reads a quoted token starting at the opening quote (which is at <paramref name="i"/>).</summary>
        private static SyntaxSegment ReadQuoted(string text, ref int i, char quote)
        {
            int start = i;
            i++; // skip the opening quote
            int len = text.Length;
            while (i < len)
            {
                if (text[i] == '\\')
                {
                    i += (i + 1 < len) ? 2 : 1; // escaped character, e.g. \" or \n
                    continue;
                }
                if (text[i] == quote)
                {
                    i++; // closing quote
                    break;
                }
                i++;
            }
            return new SyntaxSegment(text, start, i - start, SyntaxCategory.String);
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
                return new SyntaxSegment(text, start, i - start, SyntaxCategory.Number);
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

            return new SyntaxSegment(text, start, i - start, SyntaxCategory.Number);
        }
    }
}
