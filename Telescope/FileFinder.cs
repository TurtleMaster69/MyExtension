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
    public sealed class FileFinder : FinderBase<FileHit>
    {
        private readonly Func<DTE> _dteFactory;

        // Hermetic-test seam: when set, candidate enumeration and file opening go through these
        // instead of DTE, so the finder's logic can be unit-tested without Visual Studio.
        private readonly Func<IReadOnlyList<string>>? _testCandidateSource;
        private readonly Action<string>? _testOpener;

        public override string Name => "Files";

        /// <param name="dteFactory">
        /// Returns the top-level DTE automation object. A factory (rather than a DTE) is injected
        /// so the finder can stay decoupled from how the host resolves DTE and can be constructed
        /// before VS services are ready.
        /// </param>
        public FileFinder(Func<DTE> dteFactory)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
        }

        /// <summary>Test-only constructor: drives candidate enumeration and opening without DTE.</summary>
        internal FileFinder(Func<IReadOnlyList<string>> candidateSource, Action<string> opener)
        {
            _testCandidateSource = candidateSource;
            _testOpener = opener;
            _dteFactory = () => null!;
        }

        protected override IReadOnlyList<FileHit> GatherHits()
        {
            if (_testCandidateSource != null)
            {
                // Hermetic test path: no VS thread affinity.
                var testHits = new List<FileHit>();
                foreach (string path in _testCandidateSource())
                {
                    testHits.Add(new FileHit(path, 0));
                }
                return testHits;
            }

            var hits = new List<FileHit>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            DTE dte = _dteFactory();
            if (dte?.Solution == null)
            {
                return hits;
            }

            foreach (Project project in dte.Solution.Projects)
            {
                CollectProjectFiles(project, hits, seen);
            }

            return hits;
        }

        protected override FinderEntry ToEntry(FileHit hit)
        {
            return new FinderEntry(Path.GetFileName(hit.FilePath), hit);
        }

        protected override void OpenHit(FileHit hit)
        {
            if (!File.Exists(hit.FilePath))
            {
                return;
            }

            if (_testOpener != null)
            {
                // Hermetic test path: no VS thread affinity.
                _testOpener(hit.FilePath);
                TelescopeLog.Log($"opened file: {hit.FilePath}");
                return;
            }

            _dteFactory()?.ItemOperations.OpenFile(hit.FilePath);
            TelescopeLog.Log($"opened file: {hit.FilePath}");
        }

        protected override string OpenErrorNoun => "file";

        private static void CollectProjectFiles(Project project, List<FileHit> hits, HashSet<string> seen)
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
                            CollectProjectFiles(item.SubProject, hits, seen);
                        }
                    }
                    return;
                }

                if (project.ProjectItems == null)
                {
                    return;
                }

                CollectItems(project.ProjectItems, hits, seen);
            }
            catch
            {
                // A single unreadable project shouldn't abort the whole finder.
            }
        }

        private static void CollectItems(ProjectItems items, List<FileHit> hits, HashSet<string> seen)
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
                        hits.Add(new FileHit(path!, 0));
                    }

                    if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                    {
                        CollectItems(item.ProjectItems, hits, seen);
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
