using Telescope.Finders;

namespace MyExtension.ToolWindows
{
    internal sealed class HierarchyNode : IHierarchyNode
    {
        public HierarchyNode(string kind, string name, string filePath,
            System.Collections.Generic.IReadOnlyList<HierarchyNode>? children)
        { Kind = kind; Name = name; FilePath = filePath; Children = children; }
        public string Kind { get; }
        public string Name { get; }
        public string FilePath { get; }
        public System.Collections.Generic.IReadOnlyList<HierarchyNode>? Children { get; }

        // m19: the forest is walkable by the shared HierarchyWalker (IHierarchyNode).
        System.Collections.Generic.IEnumerable<IHierarchyNode> IHierarchyNode.Children =>
            Children ?? System.Array.Empty<HierarchyNode>();
        string? IHierarchyNode.Path => FilePath;
    }

    internal static class HierarchyResolver
    {
        // EXACT literals — MUST equal EnvDTE.Constants.vsProjectItemKindPhysicalFile /
        // vsProjectItemKindPhysicalFolder (verify with a quick grep of the EnvDTE ref or a
        // one-line compare at runtime; these are the well-known values):
        public const string PhysicalFileKind   = "{6BB5F8EE-4483-11D3-8BCF-00C04F8EC28C}";
        public const string PhysicalFolderKind = "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}";

        /// <summary>
        /// Returns the PRIMARY file of a project item's file-name list. EnvDTE's
        /// <c>ProjectItem.FileNames</c> is 1-based and <c>FileNames[1]</c> is the primary file's
        /// full path; the list is passed in 0-based order, so the primary is <c>fileNames[0]</c>.
        /// R9: an empty list (a corrupt project item) resolves to null instead of throwing
        /// <c>IndexOutOfRangeException</c>.
        /// </summary>
        public static string? PrimaryFilePath(System.Collections.Generic.IReadOnlyList<string> fileNames)
        {
            return fileNames.Count > 0 ? fileNames[0] : null;
        }

        public static string? FirstSourceFilePath(
            System.Collections.Generic.IReadOnlyList<HierarchyNode> nodes)
        {
            // R19: delegate to the shared HierarchyWalker (single-source the walk + the .cs
            // filter) instead of re-implementing the recursion here. FirstPathEndingWith walks the
            // forest depth-first in tree order and returns the first path ending .cs
            // (OrdinalIgnoreCase, matching HierarchyForestBuilder.Build), so `g` never selects
            // e.g. a .resx.
            return HierarchyWalker.FirstPathEndingWith(nodes, ".cs");
        }

        public static string? FirstPathMatching(
            System.Collections.Generic.IReadOnlyList<HierarchyNode> nodes, string query)
        {
            if (string.IsNullOrEmpty(query)) return null;
            foreach (var n in nodes)
            {
                if (n.Kind == PhysicalFileKind &&
                    n.Name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return n.FilePath;                                  // name (with ext) contains the query
                if (n.Kind == PhysicalFolderKind && n.Children != null)
                {
                    var hit = FirstPathMatching(n.Children, query);     // folders recurse, in order
                    if (hit != null) return hit;
                }
            }
            return null;                                                 // no match / empty query -> null
        }
    }
}