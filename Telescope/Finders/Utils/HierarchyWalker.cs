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

        /// <summary>
        /// Returns the first path ending with <paramref name="extension"/> in tree order, WITHOUT
        /// the on-disk existence filter (R19 — <c>HierarchyResolver.FirstSourceFilePath</c> delegates
        /// here so the walk + extension filter are single-sourced; the resolver's forest is built
        /// from live DTE items, so the paths exist on disk).
        /// </summary>
        public static string? FirstPathEndingWith(IEnumerable<IHierarchyNode> roots, string extension)
        {
            foreach (var root in roots)
            {
                string? hit = FirstPathEndingWithCore(root, extension);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }

        /// <summary>
        /// N18: returns the first path whose file name contains <paramref name="query"/>
        /// (OrdinalIgnoreCase) in tree order. Single-sources the name-contains walk used by
        /// <c>HierarchyResolver.FirstPathMatching</c>.
        /// </summary>
        public static string? FirstPathContaining(IEnumerable<IHierarchyNode> roots, string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }
            foreach (var root in roots)
            {
                string? hit = FirstPathContainingCore(root, query);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }

        private static string? FirstPathContainingCore(IHierarchyNode? node, string query)
        {
            if (node == null)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(node.Path) &&
                Path.GetFileName(node.Path!).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return node.Path;
            }
            foreach (var child in node.Children)
            {
                string? hit = FirstPathContainingCore(child, query);
                if (hit != null)
                {
                    return hit;
                }
            }
            return null;
        }

        private static string? FirstPathEndingWithCore(IHierarchyNode? node, string extension)
        {
            if (node == null)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(node.Path) &&
                node.Path!.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return node.Path;
            }
            foreach (var child in node.Children)
            {
                string? hit = FirstPathEndingWithCore(child, extension);
                if (hit != null)
                {
                    return hit;
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
