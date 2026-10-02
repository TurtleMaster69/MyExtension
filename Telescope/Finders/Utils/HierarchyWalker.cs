using System;
using System.Collections.Generic;
using System.IO;

namespace Telescope.Finders
{
    /// <summary>
    /// A pure, dependency-free tree-walk seam over a DTE solution/project hierarchy. The DTE
    /// adapter (<see cref="ProjectFiles"/>) maps <c>Project</c>/<c>ProjectItem</c> into
    /// <see cref="IHierarchyNode"/>; the walker enumerates the physical file paths depth-first.
    /// </summary>
    internal interface IHierarchyNode
    {
        IEnumerable<IHierarchyNode> Children { get; }
        string? Path { get; }
    }

    /// <summary>
    /// Depth-first file enumeration over an <see cref="IHierarchyNode"/> tree: dedups paths
    /// (OrdinalIgnoreCase) and drops paths that do not exist on disk. Pure — no DTE/WPF, so it is
    /// unit-tested hermetically.
    /// </summary>
    internal static class HierarchyWalker
    {
        public static IReadOnlyList<string> EnumerateFiles(IEnumerable<IHierarchyNode> roots)
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                Walk(root, paths, seen);
            }
            return paths;
        }

        public static string? FirstFileEndingWith(IEnumerable<IHierarchyNode> roots, string extension)
        {
            foreach (string path in EnumerateFiles(roots))
            {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            return null;
        }

        private static void Walk(IHierarchyNode? node, List<string> paths, HashSet<string> seen)
        {
            if (node == null)
            {
                return;
            }
            if (!string.IsNullOrEmpty(node.Path) && File.Exists(node.Path) && seen.Add(node.Path!))
            {
                paths.Add(node.Path!);
            }
            foreach (var child in node.Children)
            {
                Walk(child, paths, seen);
            }
        }
    }
}
