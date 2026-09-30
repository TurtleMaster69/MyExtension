using EnvDTE;
using System;
using System.Collections.Generic;

namespace Telescope
{
    /// <summary>
    /// Enumerates the physical source-file paths in a DTE solution/project tree (solution folders,
    /// nested items). Shared by <see cref="FileFinder"/>, <see cref="CodeIssuesFinder"/>, and the
    /// package's first-source-file auto-open. Must run on the UI thread.
    ///
    /// <para/>
    /// The DTE <c>Project</c>/<c>ProjectItem</c> tree is adapted into the pure
    /// <see cref="IHierarchyNode"/> abstraction and walked by <see cref="HierarchyWalker"/> (the
    /// single shared walker — no hand-rolled DTE recursion here).
    /// </summary>
    internal static class ProjectFiles
    {
        private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        public static IReadOnlyList<string> Enumerate(DTE dte)
        {
            if (dte?.Solution == null)
            {
                return Array.Empty<string>();
            }

            var roots = new List<IHierarchyNode>();
            foreach (Project project in dte.Solution.Projects)
            {
                roots.Add(new ProjectNode(project));
            }
            return HierarchyWalker.EnumerateFiles(roots);
        }

        /// <summary>Adapts a DTE <see cref="Project"/> (or solution folder) to <see cref="IHierarchyNode"/>.</summary>
        private sealed class ProjectNode : IHierarchyNode
        {
            private readonly Project _project;

            public ProjectNode(Project project)
            {
                _project = project;
            }

            public string? Path => null;

            public IEnumerable<IHierarchyNode> Children
            {
                get
                {
                    var children = new List<IHierarchyNode>();
                    try
                    {
                        if (_project.ProjectItems == null)
                        {
                            return children;
                        }

                        // Solution folders (kind "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}") have a
                        // SubProject per contained project; recurse into them.
                        if (_project.Kind != null &&
                            _project.Kind.Equals(SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (ProjectItem item in _project.ProjectItems)
                            {
                                if (item.SubProject != null)
                                {
                                    children.Add(new ProjectNode(item.SubProject));
                                }
                            }
                        }
                        else
                        {
                            foreach (ProjectItem item in _project.ProjectItems)
                            {
                                children.Add(new ItemNode(item));
                            }
                        }
                    }
                    catch
                    {
                        // A single unreadable project shouldn't abort the whole enumeration.
                    }
                    return children;
                }
            }
        }

        /// <summary>Adapts a DTE <see cref="ProjectItem"/> to <see cref="IHierarchyNode"/>.</summary>
        private sealed class ItemNode : IHierarchyNode
        {
            private readonly ProjectItem _item;

            public ItemNode(ProjectItem item)
            {
                _item = item;
            }

            public string? Path
            {
                get
                {
                    try
                    {
                        // Item.FullPath is a design-time property on ProjectItem (VS 2013+).
                        return _item.Properties?.Item("FullPath")?.Value as string;
                    }
                    catch
                    {
                        // property may be unavailable for some item kinds
                        return null;
                    }
                }
            }

            public IEnumerable<IHierarchyNode> Children
            {
                get
                {
                    var children = new List<IHierarchyNode>();
                    try
                    {
                        if (_item.ProjectItems != null)
                        {
                            foreach (ProjectItem child in _item.ProjectItems)
                            {
                                children.Add(new ItemNode(child));
                            }
                        }
                    }
                    catch
                    {
                        // skip items that can't be read
                    }
                    return children;
                }
            }
        }
    }
}
