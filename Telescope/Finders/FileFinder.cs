using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using Telescope.Logging;

namespace Telescope.Finders
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
        private readonly ProjectFileCache? _fileCache;

        // Hermetic-test seam: when set, candidate enumeration and file opening go through these
        // instead of DTE, so the finder's logic can be unit-tested without Visual Studio.
        private readonly Func<IReadOnlyList<string>>? _testCandidateSource;
        private readonly Func<IReadOnlyList<string>>? _testEnumerate;
        private readonly Action<string>? _testOpener;

        public override string Name => "Files";

        /// <param name="dteFactory">
        /// Returns the top-level DTE automation object. A factory (rather than a DTE) is injected
        /// so the finder can stay decoupled from how the host resolves DTE and can be constructed
        /// before VS services are ready.
        /// </param>
        public FileFinder(Func<DTE> dteFactory)
            : this(dteFactory, null)
        {
        }

        /// <param name="dteFactory">Returns the top-level DTE automation object (see above).</param>
        /// <param name="fileCache">Shared project-file enumeration cache (N37/BP-50: amortizes the per-open solution walk).</param>
        /// <param name="testCandidateSource">Hermetic-test seam: drives candidate enumeration without DTE.</param>
        /// <param name="testEnumerate">Hermetic-test seam: routes the enumerate delegate through the shared cache (m34).</param>
        /// <param name="testOpener">Hermetic-test seam: drives opening without DTE.</param>
        internal FileFinder(
            Func<DTE> dteFactory,
            ProjectFileCache? fileCache,
            Func<IReadOnlyList<string>>? testCandidateSource = null,
            Func<IReadOnlyList<string>>? testEnumerate = null,
            Action<string>? testOpener = null)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            _fileCache = fileCache;
            _testCandidateSource = testCandidateSource;
            _testEnumerate = testEnumerate;
            _testOpener = testOpener;
        }

        protected override IReadOnlyList<FileHit> GatherHits()
        {
            if (_testEnumerate != null)
            {
                // Hermetic test path: no VS thread affinity; the shared cache serves the enumerate
                // delegate once across gathers.
                var testHits = new List<FileHit>();
                foreach (string path in _fileCache!.Get(_testEnumerate))
                {
                    testHits.Add(new FileHit(path, 0));
                }
                return testHits;
            }

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
            IReadOnlyList<string> files = _fileCache != null
                ? _fileCache.Get(() => ProjectFiles.Enumerate(_dteFactory()))
                : ProjectFiles.Enumerate(_dteFactory());
            foreach (string path in files)
            {
                hits.Add(new FileHit(path, 0));
            }
            return hits;
        }

        protected override FinderEntry ToEntry(FileHit hit)
        {
            return new FinderEntry(Path.GetFileName(hit.FilePath), hit);
        }

        protected override void OpenHit(FileHit hit)
        {
            if (_testOpener != null)
            {
                // Hermetic test path: no VS thread affinity. Keeps its own missing-file no-op.
                if (!File.Exists(hit.FilePath)) return;
                _testOpener(hit.FilePath);
                TelescopeLog.Log($"opened file: {hit.FilePath}");
                return;
            }

            // The real HitOpener path owns the missing-file no-op (HitOpener guards internally).
            HitOpener.OpenAtLine(hit, (path, line) =>
            {
                // N68/BP-64: only log when the open actually happened (a null DTE short-circuits it).
                DTE? dte = _dteFactory();
                if (dte == null)
                {
                    return;
                }
                dte.ItemOperations.OpenFile(path);
                TelescopeLog.Log($"opened file: {path}");
            });
        }

        protected override string OpenErrorNoun => "file";
    }
}
