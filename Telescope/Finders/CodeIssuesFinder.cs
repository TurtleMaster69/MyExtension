using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telescope.Logging;

namespace Telescope.Finders
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

        private readonly ProjectFileCache _fileCache;
        private string? _cachedSolutionName;

        // m15: shared mtime-keyed content cache — CollectTodos reads through it so a second scan
        // over the same file is served from memory instead of re-reading from disk. D7/BP-14: ONE
        // shared instance injected from the controller.
        private readonly FileContentCache _contentCache;

        public override string Name => "Issues";

        /// <param name="dteFactory">Returns the top-level DTE automation object (see <see cref="FileFinder"/>).</param>
        /// <param name="fileCache">Shared project-file enumeration cache (amortizes the per-query solution walk).</param>
        /// <param name="contentCache">Shared file-content cache (D7/BP-14 — ONE instance injected from the controller; m8/BP-14 makes it a REQUIRED param so a finder can never silently revert to its own cache).</param>
        /// <param name="testFileSource">Hermetic-test seam: when set, candidate gathering bypasses DTE entirely.</param>
        /// <param name="testOpener">Hermetic-test seam: when set, opening bypasses DTE entirely.</param>
        internal CodeIssuesFinder(
            Func<DTE> dteFactory,
            ProjectFileCache fileCache,
            FileContentCache contentCache,
            Func<IReadOnlyList<string>>? testFileSource = null,
            Action<CodeIssue>? testOpener = null)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            _fileCache = fileCache ?? throw new ArgumentNullException(nameof(fileCache));
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _testFileSource = testFileSource;
            _testOpener = testOpener;
        }

        /// <summary>BP-D3 (m53): the shared content cache (the reflection-free seam).</summary>
        internal FileContentCache ContentCache => _contentCache;

        protected override IReadOnlyList<CodeIssue> GatherHits()
        {
            // M4c (BP-4): the sync entry delegates to the async path so the existing sync callers
            // (the overlay's sync GetCandidates() at overlay-open + the tests) keep working.
            return GatherHitsAsync().GetAwaiter().GetResult();
        }

        public override async Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "", CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CodeIssue> issues = await GatherHitsAsync();
            return issues.Select(ToEntry).ToList();
        }

        private async Task<IReadOnlyList<CodeIssue>> GatherHitsAsync()
        {
            var issues = new List<CodeIssue>();

            if (_testFileSource != null)
            {
                // Hermetic test path: the TODO scan is pure file I/O — run it off-thread (m4/BP-6).
                await Task.Run(() =>
                {
                    foreach (string path in _testFileSource())
                    {
                        CollectTodos(path, issues);
                    }
                });
                return issues;
            }

            DTE dte = _dteFactory();
            if (dte?.Solution != null)
            {
                // m7 (BP-13): the ONE shared solution-invalidation helper (replaces the duplicated
                // compare + Invalidate block).
                ProjectFileCache.EnsureSolutionCache(_fileCache, ref _cachedSolutionName, dte?.Solution?.FullName);
                // m4 (BP-6): the TODO scan is pure file I/O — run it off-thread. The DTE
                // enumeration stays on the UI thread; CollectErrorList (the COM ErrorItems walk)
                // MUST stay on the UI thread (it asserts ThrowIfNotOnUIThread).
                IReadOnlyList<string> files = _fileCache.Get(() => ProjectFiles.Enumerate(dte));
                await Task.Run(() =>
                {
                    foreach (string path in files)
                    {
                        CollectTodos(path, issues);
                    }
                });
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
            string display = $"[{marker}] line {issue.LineNumber}: {SanitizeDisplay(issue.Text)} — {file}";
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

            HitOpener.OpenAtLine(hit, (path, line) =>
            {
                DTE dte = _dteFactory();
                DteFileOpener.OpenAtLine(dte, path, line);
                TelescopeLog.Log($"opened issue: {path} line={line}");
            });
        }

        protected override string OpenErrorNoun => "issue";

        /// <summary>Max length of the issue text embedded in a result row (N42/BP-56).</summary>
        private const int MaxDisplayTextLength = 200;

        /// <summary>
        /// Sanitizes an Error List description for a single result row: control characters become
        /// spaces (so a multi-line description cannot break fzf/ResultMapper or the results TextBox)
        /// and the text is bounded (N42/BP-56).
        /// </summary>
        private static string SanitizeDisplay(string? text)
        {
            string sanitized = DiagnosticLog.SanitizeText(text);
            return sanitized.Length > MaxDisplayTextLength
                ? sanitized.Substring(0, MaxDisplayTextLength)
                : sanitized;
        }

        private void CollectTodos(string path, List<CodeIssue> issues)
        {
            try
            {
                string[] lines = _contentCache.GetLines(path);
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

                // m33 (BP-24): the shared per-item walk (per-item try/catch — a throwing item is
                // skipped, the rest survive).
                ErrorItemsWalker.ForEach(items, item =>
                {
                    string fileName = item.FileName ?? string.Empty;
                    if (fileName.Length == 0)
                    {
                        return;
                    }
                    issues.Add(new CodeIssue(ClassifySeverity(item.ErrorLevel), fileName, item.Line, item.Description ?? string.Empty));
                });
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"Error List read failed: {ex.Message}");
            }
        }

        internal static CodeIssueKind ClassifySeverity(vsBuildErrorLevel severity)
        {
            return severity switch
            {
                vsBuildErrorLevel.vsBuildErrorLevelHigh => CodeIssueKind.Error,
                vsBuildErrorLevel.vsBuildErrorLevelMedium => CodeIssueKind.Warning,
                vsBuildErrorLevel.vsBuildErrorLevelLow => CodeIssueKind.Info,
                _ => CodeIssueKind.Info,
            };
        }
    }
}
