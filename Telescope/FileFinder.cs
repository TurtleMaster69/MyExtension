using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;

namespace Telescope
{
    /// <summary>
    /// A Telescope finder that lists the files in the currently-open solution/projects.
    /// Candidates are gathered once on the UI thread at open time via DTE, then filtered by fzf
    /// as the user types. Selecting an entry opens the file in the editor.
    ///
    /// <para/>
    /// <b>Threading:</b> <see cref="GetCandidates"/> and <see cref="OnSelected"/> both touch DTE
    /// and therefore must run on the UI thread (the controller guarantees this).
    /// </summary>
    public sealed class FileFinder : IFinder
    {
        private readonly Func<DTE> _dteFactory;

        public string Name => "Files";

        /// <param name="dteFactory">
        /// Returns the top-level DTE automation object. A factory (rather than a DTE) is injected
        /// so the finder can stay decoupled from how the host resolves DTE and can be constructed
        /// before VS services are ready.
        /// </param>
        public FileFinder(Func<DTE> dteFactory)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
        }

        public IReadOnlyList<FinderEntry> GetCandidates()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var entries = new List<FinderEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                DTE dte = _dteFactory();
                if (dte?.Solution == null)
                {
                    return entries;
                }

                foreach (Project project in dte.Solution.Projects)
                {
                    CollectProjectFiles(project, entries, seen);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Telescope] FileFinder failed to enumerate: {ex.Message}");
            }

            return entries;
        }

        public void OnSelected(FinderEntry entry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (entry.Payload is string path && File.Exists(path))
                {
                    _dteFactory()?.ItemOperations.OpenFile(path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Telescope] FileFinder failed to open '{entry.Display}': {ex.Message}");
            }
        }

        private static void CollectProjectFiles(Project project, List<FinderEntry> entries, HashSet<string> seen)
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
                            CollectProjectFiles(item.SubProject, entries, seen);
                        }
                    }
                    return;
                }

                if (project.ProjectItems == null)
                {
                    return;
                }

                CollectItems(project.ProjectItems, entries, seen);
            }
            catch
            {
                // A single unreadable project shouldn't abort the whole finder.
            }
        }

        private static void CollectItems(ProjectItems items, List<FinderEntry> entries, HashSet<string> seen)
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
                        entries.Add(new FinderEntry(Path.GetFileName(path!), path!));
                    }

                    if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                    {
                        CollectItems(item.ProjectItems, entries, seen);
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
