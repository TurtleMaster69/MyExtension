namespace Telescope
{
    /// <summary>Category of a code issue shown in the <see cref="CodeIssuesFinder"/>.</summary>
    public enum CodeIssueKind
    {
        /// <summary>A TODO/FIXME/HACK/XXX marker comment.</summary>
        Todo,

        /// <summary>A compiler/analyzer error from the VS Error List.</summary>
        Error,

        /// <summary>A compiler/analyzer warning from the VS Error List.</summary>
        Warning,

        /// <summary>Anything else from the VS Error List.</summary>
        Info,
    }

    /// <summary>
    /// A single code issue (a TODO marker or an Error List item): the target file, the 1-based
    /// line, the category, and the human-readable text. Carried as a <c>FinderEntry.Payload</c> so
    /// <see cref="CodeIssuesFinder.OnSelected"/> can open the file at the line.
    /// </summary>
    public sealed class CodeIssue
    {
        public CodeIssue(CodeIssueKind kind, string filePath, int lineNumber, string text)
        {
            Kind = kind;
            FilePath = filePath ?? string.Empty;
            LineNumber = lineNumber;
            Text = text ?? string.Empty;
        }

        public CodeIssueKind Kind { get; }
        public string FilePath { get; }
        public int LineNumber { get; }
        public string Text { get; }
    }
}