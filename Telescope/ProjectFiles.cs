using EnvDTE;
using System;
using System.Collections.Generic;
using System.IO;

namespace Telescope
{
    /// <summary>
    /// Enumerates the physical source-file paths in a DTE solution/project tree (solution folders,
    /// nested items). Shared by <see cref="FileFinder"/> and <see cref="CodeIssuesFinder"/>. Must
    /// run on the UI thread.
    /// </summary>
    internal static class ProjectFiles
    {
        public static IReadOnlyList<string> Enumerate(DTE dte)
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dte?.Solution == null)
            {
                return paths;
            }

            foreach (Project project in dte.Solution.Projects)
            {
                CollectProjectFiles(project, paths, seen);
            }
            return paths;
        }

        private static void CollectProjectFiles(Project project, List<string> paths, HashSet<string> seen)
        {
            try
            {
                if (project == null)
                {
                    return;
                }

                // Solution folders (kind "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}") have a
                // SubProject per contained project; recurse into them.
                if (project.ProjectItems != null && project.Kind != null &&
                    project.Kind.Equals("{66A26720-8FB5-11D2-AA7E-00C04F688DDE}", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (ProjectItem item in project.ProjectItems)
                    {
                        if (item.SubProject != null)
                        {
                            CollectProjectFiles(item.SubProject, paths, seen);
                        }
                    }
                    return;
                }

                if (project.ProjectItems != null)
                {
                    CollectItems(project.ProjectItems, paths, seen);
                }
            }
            catch
            {
                // A single unreadable project shouldn't abort the whole enumeration.
            }
        }

        private static void CollectItems(ProjectItems items, List<string> paths, HashSet<string> seen)
        {
            if (items == null)
            {
                return;
            }

            foreach (ProjectItem item in items)
            {
                try
                {
                    // Item.FullPath is a design-time property on ProjectItem (VS 2013+).
                    string? path = null;
                    try
                    {
                        path = item.Properties?.Item("FullPath")?.Value as string;
                    }
                    catch
                    {
                        // property may be unavailable for some item kinds
                    }

                    if (!string.IsNullOrEmpty(path) && File.Exists(path) && seen.Add(path!))
                    {
                        paths.Add(path!);
                    }

                    if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                    {
                        CollectItems(item.ProjectItems, paths, seen);
                    }
                }
                catch
                {
                    // skip items that can't be read
                }
            }
        }
    }
}