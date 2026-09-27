namespace MyExtension
{
    internal sealed class HierarchyNode
    {
        public HierarchyNode(string kind, string name, string filePath,
            System.Collections.Generic.IReadOnlyList<HierarchyNode>? children)
        { Kind = kind; Name = name; FilePath = filePath; Children = children; }
        public string Kind { get; }
        public string Name { get; }
        public string FilePath { get; }
        public System.Collections.Generic.IReadOnlyList<HierarchyNode>? Children { get; }
    }

    internal static class HierarchyResolver
    {
        // EXACT literals — MUST equal EnvDTE.Constants.vsProjectItemKindPhysicalFile /
        // vsProjectItemKindPhysicalFolder (verify with a quick grep of the EnvDTE ref or a
        // one-line compare at runtime; these are the well-known values):
        public const string PhysicalFileKind   = "{6BB5F8EE-4483-11D3-8BCF-00C04F8EC28C}";
        public const string PhysicalFolderKind = "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}";

        public static string? FirstSourceFilePath(
            System.Collections.Generic.IReadOnlyList<HierarchyNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.Kind == PhysicalFileKind) return n.FilePath;             // physical file -> return path
                if (n.Kind == PhysicalFolderKind && n.Children != null)        // folder -> recurse (in order)
                {
                    var hit = FirstSourceFilePath(n.Children);
                    if (hit != null) return hit;
                }
                // any other kind (project/solution/virtual-folder/references/unknown) -> SKIP, no recursion
            }
            return null;                                                       // empty / no reachable file -> null
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