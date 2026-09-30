using System;
using System.Collections.Generic;

namespace MyExtension
{
    /// <summary>
    /// Pure DTO mirroring <see cref="HierarchyNode"/>: the DTE adapter
    /// (<see cref="SolutionExplorerController.MapChildren"/>) maps a project node's
    /// <c>UIHierarchyItems</c> into these, and <see cref="HierarchyForestBuilder.Build"/>
    /// turns them into the <see cref="HierarchyNode"/> forest. Dependency-free so the
    /// builder is unit-testable without DTE.
    /// </summary>
    internal sealed class HierarchyItemInfo
    {
        public HierarchyItemInfo(string kind, string name, string fullPath,
            IReadOnlyList<HierarchyItemInfo>? children)
        { Kind = kind; Name = name; FullPath = fullPath; Children = children; }
        public string Kind { get; }
        public string Name { get; }
        public string FullPath { get; }
        public IReadOnlyList<HierarchyItemInfo>? Children { get; }
    }

    /// <summary>
    /// Pure forest builder (M14): recurses physical folders and adds physical <c>.cs</c> files,
    /// reusing <see cref="HierarchyResolver.PhysicalFolderKind"/> / <see cref="HierarchyResolver.PhysicalFileKind"/>
    /// and producing the existing <see cref="HierarchyNode"/>. The <paramref name="pathToItem"/>
    /// map records path→path identity for every added <c>.cs</c> file (the pure builder cannot
    /// hold DTE objects; the DTE adapter owns the real path→<c>UIHierarchyItem</c> map).
    /// </summary>
    internal static class HierarchyForestBuilder
    {
        public static List<HierarchyNode> Build(
            IEnumerable<HierarchyItemInfo> items,
            Dictionary<string, string> pathToItem)
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
                    var children = Build(item.Children, pathToItem);
                    forest.Add(new HierarchyNode(HierarchyResolver.PhysicalFolderKind, item.Name, "", children));
                }
                else if (item.Kind == HierarchyResolver.PhysicalFileKind &&
                         item.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    forest.Add(new HierarchyNode(HierarchyResolver.PhysicalFileKind, item.Name, item.FullPath, null));
                    pathToItem[item.FullPath] = item.FullPath;
                }
            }
            return forest;
        }
    }
}
