using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using MyExtension.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyExtension.Package
{
    /// <summary>
    /// VS-coupled gatherer for the severity-filtered diagnostics navigation (Gap 3): reads the
    /// VS Error List through the exact DTE2 API the Telescope CodeIssuesFinder established
    /// (Telescope/Finders/CodeIssuesFinder.cs:170-205), filters to ONE file + ONE severity, and
    /// returns DiagnosticEntry rows under the gather contract DiagnosticNavigator relies on:
    /// 1. severity filter — ErrorItem.ErrorLevel (vsBuildErrorLevel): errors =
    ///    vsBuildErrorLevelHigh, warnings = vsBuildErrorLevelMedium (messages/Low excluded);
    /// 2. file filter — ErrorItem.FileName equals the requested path (OrdinalIgnoreCase);
    /// 3. entries with Line &lt;= 0 dropped;
    /// 4. sorted ascending by Line (stable), duplicate lines collapsed to the FIRST Error List
    ///    item — repeated navigation never re-lands on the same line.
    /// Per-item read failures are skipped (the CodeIssuesFinder discipline); an outer read
    /// failure PROPAGATES so the caller logs <c>diagnostic-nav failed: {msg}</c>.
    /// <para/>
    /// <b>Threading:</b> UI thread only (DTE/COM).
    /// </summary>
    internal static class ErrorListGatherer
    {
        public static List<DiagnosticEntry> Gather(DTE dte, string filePath, bool severityError)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var entries = new List<DiagnosticEntry>();
            var dte2 = dte as DTE2;
            ErrorItems? items = dte2?.ToolWindows.ErrorList.ErrorItems;
            if (items == null)
            {
                return entries;
            }

            var wanted = severityError
                ? vsBuildErrorLevel.vsBuildErrorLevelHigh
                : vsBuildErrorLevel.vsBuildErrorLevelMedium;

            int count = items.Count;
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    ErrorItem item = items.Item(i);
                    if (item.ErrorLevel != wanted)
                    {
                        continue;
                    }
                    string fileName = item.FileName ?? string.Empty;
                    if (fileName.Length == 0 ||
                        !string.Equals(fileName, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (item.Line <= 0)
                    {
                        continue;
                    }
                    entries.Add(new DiagnosticEntry(fileName, item.Line));
                }
                catch
                {
                    // skip an item that can't be read (same discipline as CodeIssuesFinder)
                }
            }

            return entries
                .OrderBy(e => e.Line)
                .GroupBy(e => e.Line)
                .Select(g => g.First())
                .ToList();
        }
    }
}
