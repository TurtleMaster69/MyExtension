using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Telescope
{
    /// <summary>
    /// A Telescope finder that lists the <b>code issues</b> in the open solution:
    /// <list type="bullet">
    /// <item>warning/error items from the VS Error List (compiler/analyzer diagnostics), and</item>
    /// <item>TODO/FIXME/HACK/XXX marker comments scanned from the project's source files.</item>
    /// </list>
    ///
    /// Each row shows the kind, the line number, and the message. Selecting an entry opens the
    /// file in the editor and jumps the caret to that line.
    ///
    /// <para/>
    /// <b>Threading:</b> <see cref="GetCandidates"/> and <see cref="OnSelected"/> touch DTE and
    /// therefore must run on the UI thread (the controller guarantees this).
    /// </summary>
    public sealed class CodeIssuesFinder : FinderBase<CodeIssue>
    {

        private readonly Func<DTE> _dteFactory;

        // Hermetic-test seams: when set, candidate gathering and opening bypass DTE entirely.
        private readonly Func<IReadOnlyList<string>>? _testFileSource;
        private readonly Action<CodeIssue>? _testOpener;

        public override string Name => "Issues";

        /// <param name="dteFactory">Returns the top-level DTE automation object (see <see cref="FileFinder"/>).</param>
        public CodeIssuesFinder(Func<DTE> dteFactory)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
        }

        /// <summary>Test-only constructor: scans the given files for TODO markers and reports opens without DTE.</summary>
        internal CodeIssuesFinder(Func<IReadOnlyList<string>> fileSource, Action<CodeIssue> opener)
        {
            _testFileSource = fileSource;
            _testOpener = opener;
            _dteFactory = () => null!;
        }

        protected override IReadOnlyList<CodeIssue> GatherHits()
        {
            var issues = new List<CodeIssue>();

            if (_testFileSource != null)
            {
                // Hermetic test path: no VS thread affinity.
                foreach (string path in _testFileSource())
                {
                    CollectTodos(path, issues);
                }
                return issues;
            }

            DTE dte = _dteFactory();
            if (dte?.Solution != null)
            {
                foreach (string path in ProjectFiles.Enumerate(dte))
                {
                    CollectTodos(path, issues);
                }
                CollectErrorList(dte, issues);
            }

            return issues;
        }

        protected override FinderEntry ToEntry(CodeIssue issue)
        {
            string marker = issue.Kind switch
            {
                CodeIssueKind.Error => "ERR",
                CodeIssueKind.Warning => "WARN",
                CodeIssueKind.Todo => "TODO",
                _ => "INFO",
            };
            string file = Path.GetFileName(issue.FilePath);
            string display = $"[{marker}] line {issue.LineNumber}: {issue.Text} — {file}";
            return new FinderEntry(display, issue);
        }

        protected override void OpenHit(CodeIssue hit)
        {
            if (_testOpener != null)
            {
                // Hermetic test path: no VS thread affinity.
                _testOpener(hit);
                return;
            }

            if (!File.Exists(hit.FilePath))
            {
                return;
            }

            DTE dte = _dteFactory();
            DteFileOpener.OpenAtLine(dte, hit.FilePath, hit.LineNumber);
            TelescopeLog.Log($"opened issue: {hit.FilePath} line={hit.LineNumber}");
        }

        protected override string OpenErrorNoun => "issue";

        private static void CollectTodos(string path, List<CodeIssue> issues)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (HasTodoMarker(trimmed))
                    {
                        issues.Add(new CodeIssue(CodeIssueKind.Todo, path, i + 1, trimmed));
                    }
                }
            }
            catch
            {
                // unreadable/binary file — skip
            }
        }

        private static readonly System.Text.RegularExpressions.Regex TodoRegex =
            new(@"\b(TODO|FIXME|HACK|XXX)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static bool HasTodoMarker(string text)
        {
            return TodoRegex.IsMatch(text ?? string.Empty);
        }

        private static void CollectErrorList(DTE dte, List<CodeIssue> issues)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte2 = dte as DTE2;
                ErrorItems items = dte2?.ToolWindows.ErrorList.ErrorItems ?? null;
                if (items == null)
                {
                    return;
                }

                int count = items.Count;
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        ErrorItem item = items.Item(i);
                        string fileName = item.FileName ?? string.Empty;
                        if (fileName.Length == 0)
                        {
                            continue;
                        }
                        issues.Add(new CodeIssue(Classify(item.Description), fileName, item.Line, item.Description ?? string.Empty));
                    }
                    catch
                    {
                        // skip an item that can't be read
                    }
                }
            }
            catch (Exception ex)
            {
                NeoVisualLog.Debug($"{Telescope.DiagnosticLog.Telescope}Error List read failed: {ex.Message}");
            }
        }

        private static CodeIssueKind Classify(string description)
        {
            string d = (description ?? string.Empty).ToLowerInvariant();
            if (d.IndexOf("error", StringComparison.Ordinal) >= 0)
            {
                return CodeIssueKind.Error;
            }
            if (d.IndexOf("warning", StringComparison.Ordinal) >= 0)
            {
                return CodeIssueKind.Warning;
            }
            return CodeIssueKind.Info;
        }
    }
}
