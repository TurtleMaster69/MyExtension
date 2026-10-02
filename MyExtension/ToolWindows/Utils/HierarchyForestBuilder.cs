using System;
using System.Collections.Generic;

namespace MyExtension.ToolWindows
{
    /// <summary>
    /// Pure forest builder (M14): recurses physical folders and adds physical <c>.cs</c> files,
    /// reusing <see cref="HierarchyResolver.PhysicalFolderKind"/> / <see cref="HierarchyResolver.PhysicalFileKind"/>
    /// and producing the existing <see cref="HierarchyNode"/>. The <c>.cs</c> filter lives here
    /// (the DTE adapter passes all physical files through). N61: <see cref="HierarchyNode"/> is the
    /// single DTO (the near-identical <c>HierarchyItemInfo</c> was merged into it).
    /// </summary>
    internal static class HierarchyForestBuilder
    {
        public static List<HierarchyNode> Build(IEnumerable<HierarchyNode> items)
        {
            var forest = new List<HierarchyNode>();
            if (items == null)
            {
                return forest;
            }
            foreach (var item in items)
            {
                if (item.Kind == HierarchyResolver.PhysicalFolderKind)
                {
                    var children = Build(item.Children);
                    forest.Add(new HierarchyNode(HierarchyResolver.PhysicalFolderKind, item.Name, "", children));
                }
                else if (item.Kind == HierarchyResolver.PhysicalFileKind &&
                         item.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    forest.Add(new HierarchyNode(HierarchyResolver.PhysicalFileKind, item.Name, item.FilePath, null));
                }
            }
            return forest;
        }
    }
}
