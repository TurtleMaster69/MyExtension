using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using Telescope.Finders;
using Telescope.Logging;
using Telescope.Overlay;

namespace MyExtension.Package
{
    /// <summary>
    /// The REAL editor-view preview host (Section P): implements Telescope's IPreviewEditor seam
    /// with a read-only IWpfTextView hosted in the Telescope overlay. View creation is VS-coupled
    /// (MEF editor services + ITextEditorFactoryService) and therefore lives in MyExtension per
    /// spec.md's layering note — the overlay consumes only the seam.
    ///
    /// <para/>
    /// Roles: Document + Interactive + Zoomable, EXCLUDING Editable — Interactive is required for
    /// caret/selection; excluding Editable makes the view non-editable AND means VsVim never
    /// attaches (its view factory is exported [TextViewRole(PredefinedTextViewRoles.Editable)], and
    /// a programmatic view without a VsTextBuffer shim never raises VsTextViewCreated anyway).
    ///
    /// <para/>
    /// Lifetime: one host instance per overlay (a fresh overlay is built per open). The
    /// view/document are created per preview file and REUSED while the path + LastWriteTimeUtc are
    /// unchanged (the mtime-cache pattern that gated the old FlowDocument rebuilds); the overlay's
    /// CloseOverlay -> Dispose closes the view and disposes the document. UI thread only.
    /// </summary>
    internal sealed class PreviewEditorHost : IPreviewEditor
    {
        private readonly AsyncPackage _package;
        private readonly string? _unavailableReason;
        private bool _unavailableLogged;

        private ITextDocumentFactoryService? _textDocumentFactory;
        private IContentTypeRegistryService? _contentTypeRegistry;
        private IFileExtensionRegistryService? _fileExtensionRegistry;
        private ITextEditorFactoryService? _textEditorFactory;
        private IClassifierAggregatorService? _classifierAggregator;

        private IWpfTextViewHost? _host;
        private IWpfTextView? _view;
        private ITextDocument? _document;
        private string? _documentPath;
        private DateTime _documentStamp;
        private PreviewBufferDecision _decision;   // which buffer source the current view uses (owns the document?)

        // A2: the materialized preview text is cached keyed on ITextSnapshot.Version.VersionNumber
        // so Show does not call CurrentSnapshot.GetText() (a full-buffer copy) on every selection
        // move — only on a snapshot-version change (a rebuild). Cleared on CloseView (a rebuild
        // creates a fresh buffer whose version numbers restart at 0).
        private readonly PreviewTextCache _textCache = new();

        public PreviewEditorHost(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            // Resolve the MEF editor services eagerly (the repo's VsServices.Mef<T> SComponentModel
            // pattern). A resolution failure degrades to "no preview" (logged once), never a crash.
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _textDocumentFactory = VsServices.Mef<ITextDocumentFactoryService>(_package);
                _contentTypeRegistry = VsServices.Mef<IContentTypeRegistryService>(_package);
                _fileExtensionRegistry = VsServices.Mef<IFileExtensionRegistryService>(_package);
                _textEditorFactory = VsServices.Mef<ITextEditorFactoryService>(_package);
                _classifierAggregator = VsServices.Mef<IClassifierAggregatorService>(_package);
                _unavailableReason = _textDocumentFactory == null || _contentTypeRegistry == null
                    || _fileExtensionRegistry == null || _textEditorFactory == null
                    || _classifierAggregator == null
                    ? "editor MEF services unresolved" : null;
            }
            catch (Exception ex)
            {
                _unavailableReason = ex.Message;
            }
        }

        public PreviewEditorResult Show(IFileLocation location)
        {
            if (_unavailableReason != null)
            {
                LogUnavailableOnce();
                return PreviewEditorResult.Empty;
            }
            try
            {
                if (!File.Exists(location.FilePath))
                {
                    // The old renderer's missing-file path: an empty preview + the tokens=0 line.
                    TelescopeLog.Log("preview tokens=0");
                    return PreviewEditorResult.Empty;
                }

                // n4 (BP-22): the mtime stat IS the change detector — the review's "cache mtime per
                // file" fix is UNSOUND (skipping the stat breaks on-disk-edit refresh). It is a cheap
                // metadata read on a per-selection-move path; the _documentPath/_documentStamp
                // compare gates RebuildView, and the A2 PreviewTextCache already avoids the
                // full-buffer GetText() on the cache-hit path.
                DateTime stamp = File.GetLastWriteTimeUtc(location.FilePath);
                if (_documentPath != location.FilePath || _documentStamp != stamp)
                {
                    RebuildView(location.FilePath, stamp);
                }

                // A2: the text is cached keyed on the snapshot version — on the mtime-cache hit
                // path the version is unchanged, so the full-buffer GetText() runs once per
                // rebuild, not per selection move.
                var snapshot = _view!.TextBuffer.CurrentSnapshot;
                string text = _textCache.Get(snapshot.Version.VersionNumber)
                    ?? CachePreviewText(snapshot);

                return new PreviewEditorResult(_host!.HostControl, text);
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"preview load failed: {ex.Message}");
                return PreviewEditorResult.Empty;
            }
        }

        /// <summary>Materializes the snapshot text once and caches it keyed on the snapshot version
        /// (A2) — the cache-hit path in <see cref="Show"/> skips the full-buffer copy.</summary>
        private string CachePreviewText(ITextSnapshot snapshot)
        {
            string text = snapshot.GetText();
            _textCache.Store(snapshot.Version.VersionNumber, text);
            return text;
        }

        /// <summary>Closes the previous view+document and creates the new ones (the mtime-cache
        /// miss path — the role the old PreviewTokenCache.ShouldRebuild played for the FlowDocument).
        /// Workspace first (the Peek model): a solution file previews over its LIVE workspace
        /// buffer so the full Roslyn classifier chain attaches (syntactic + semantic); any miss
        /// falls back to the standalone content-type document the host owns.</summary>
        private void RebuildView(string path, DateTime stamp)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CloseView();

            // Try the workspace first (D1): MEF VisualStudioWorkspace -> CurrentSolution ->
            // GetDocumentIdsWithFilePath -> GetDocument(id) -> TryGetText ->
            // TryGetTextBuffer(container). The buffer is the LIVE shared buffer (editor-OPEN
            // documents only — a CLOSED document falls back to the standalone path); the
            // read-only view (no Editable role) never mutates it.
            Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace? workspace = TryResolveWorkspace();
            Microsoft.VisualStudio.Text.ITextBuffer? workspaceBuffer =
                workspace != null ? TryGetWorkspaceBuffer(workspace, path) : null;
            _decision = PreviewBufferSource.Resolve(
                workspaceAvailable: workspace != null,
                documentFound: workspaceBuffer != null);

            Microsoft.VisualStudio.Text.ITextBuffer buffer;
            if (_decision.Kind == PreviewBufferKind.Workspace)
            {
                buffer = workspaceBuffer!;
                _document = null;   // the workspace buffer is NOT ours to dispose (CloseView skips it)
            }
            else
            {
                // Content type by file extension (the preview shows ANY solution file, not just
                // C#): .cs -> CSharp, .txt -> plaintext, ...; unregistered ->
                // UnknownContentType (the view still displays, just unclassified).
                IContentType contentType =
                    _fileExtensionRegistry!.GetContentTypeForExtension(Path.GetExtension(path).TrimStart('.'))
                    ?? _contentTypeRegistry!.UnknownContentType;

                _document = _textDocumentFactory!.CreateAndLoadTextDocument(path, contentType);
                buffer = _document.TextBuffer;
            }
            _documentStamp = stamp;

            // Roles: Document + Interactive + Zoomable, EXCLUDING Editable (D-P3) — non-editable,
            // caret/selection enabled, VsVim never attaches. UNCHANGED by this plan (AC3).
            ITextViewRoleSet roles = _textEditorFactory!.CreateTextViewRoleSet(
                PredefinedTextViewRoles.Document,
                PredefinedTextViewRoles.Interactive,
                PredefinedTextViewRoles.Zoomable);

            var view = (IWpfTextView)_textEditorFactory.CreateTextView(buffer, roles);
            _host = _textEditorFactory.CreateTextViewHost(view, false /* do not steal focus */);
            _view = view;
            _documentPath = path;

            // The tokens= diagnostic: VS's own classifier-chain span count over the whole snapshot —
            // proves the editor's highlighting ran (the migration's point). Logged ONLY here (a
            // rebuild), matching the old renderer's emission pattern (reuse skipped the log). On
            // the workspace path the Roslyn classifier chain attaches and the count GROWS
            // (semantic spans); the harness's tokens=\d+ assertion is count-agnostic (presence-only).
            TelescopeLog.Log($"preview tokens={CountClassificationSpans(_view)}");
            TelescopeLog.Log(PreviewDiagnostics.File(path, buffer.CurrentSnapshot.Length));
        }

        /// <summary>
        /// Resolves the MEF <c>VisualStudioWorkspace</c> LAZILY (the MyExtensionPackage.cs:97
        /// pattern — <c>VsServices.Mef</c>). Lazy, NOT constructor-eager: the workspace is
        /// OPTIONAL (its absence degrades to the standalone fallback, never to "no preview" —
        /// it must NOT set _unavailableReason), and at package-init time (when the host is
        /// constructed) the solution may not be loaded yet. Null on any failure. Silent by
        /// design — the plan pins NO new diagnostic literal; the existing preview file=/tokens=
        /// lines still emit on either path.
        /// </summary>
        private Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace? TryResolveWorkspace()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return VsServices.Mef<Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace>(_package);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The workspace buffer for <paramref name="path"/> (the Peek model — the SAME chain as
        /// RoslynGatherers.TryGetCaretSymbol): CurrentSolution →
        /// GetDocumentIdsWithFilePath(path) → the FIRST id → GetDocument(id) → TryGetText →
        /// TryGetTextBuffer(text.Container). The container of an editor-OPEN document is the
        /// editor-backed buffer container, so TryGetTextBuffer returns the LIVE buffer identity
        /// — the semantic tagger keys the Document off the buffer's workspace attachment, so a
        /// text-clone would NOT work. Null on any miss (not a solution document / a CLOSED
        /// document whose text is not loaded / Roslyn failure) — the caller falls back to the
        /// standalone document.
        /// </summary>
        private Microsoft.VisualStudio.Text.ITextBuffer? TryGetWorkspaceBuffer(
            Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace workspace, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var docId = workspace.CurrentSolution.GetDocumentIdsWithFilePath(path).FirstOrDefault();
                if (docId == null)
                {
                    return null;
                }
                var document = workspace.CurrentSolution.GetDocument(docId);
                if (document == null || !document.TryGetText(out var text))
                {
                    // A CLOSED file's text is not loaded in the solution snapshot — zero disk
                    // I/O on the UI thread; the caller falls back to the standalone document.
                    return null;
                }
                return Microsoft.CodeAnalysis.Text.Extensions.TryGetTextBuffer(text.Container);
            }
            catch
            {
                return null;   // any Roslyn failure (solution churn, deleted file) -> fallback
            }
        }

        private int CountClassificationSpans(IWpfTextView view)
        {
            // Own try/catch: a classifier failure must not kill the (already hosted) view — the
            // count reads 0 and the harness's tokens=\d+ regex is presence-only (count-agnostic).
            try
            {
                IClassifier classifier = _classifierAggregator!.GetClassifier(view.TextBuffer);
                var snapshot = view.TextSnapshot;
                return classifier
                    .GetClassificationSpans(new SnapshotSpan(snapshot, 0, snapshot.Length))
                    .Count();
            }
            catch
            {
                return 0;
            }
        }

        public void ApplyCaret(int caretIndex)
        {
            IWpfTextView? view = _view;
            if (view == null)
            {
                return;
            }
            int index = Math.Max(0, Math.Min(caretIndex, view.TextSnapshot.Length));
            view.Caret.MoveTo(new SnapshotPoint(view.TextSnapshot, index));
            view.Caret.EnsureVisible();
        }

        public void Focus()
        {
            // The VisualElement is the focusable surface (the HostControl Border is not) — the
            // editor caret renders only while the view has keyboard focus.
            _view?.VisualElement.Focus();
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CloseView();
        }

        private void CloseView()
        {
            if (_host != null)
            {
                try { _host.Close(); }   // closes the hosted view (subsumes the research's view.Close())
                catch { /* already closed */ }
                _host = null;
                _view = null;
            }
            if (_decision.OwnsDocument && _document != null)
            {
                // ONLY the standalone document is ours (PreviewBufferSource: OwnsDocument=true).
                // The workspace buffer is the LIVE shared buffer — disposing it would corrupt the
                // workspace/main editor; _document is null on the workspace path anyway (belt and
                // braces: both conditions).
                try { _document.Dispose(); }
                catch { /* already disposed */ }
                _document = null;
            }
            _documentPath = null;
            _documentStamp = default;
            // A2: a rebuild creates a fresh buffer whose snapshot version numbers restart at 0 —
            // clear the version-keyed text cache so a colliding version can never return stale text.
            _textCache.Clear();
        }

        private void LogUnavailableOnce()
        {
            if (_unavailableLogged)
            {
                return;
            }
            _unavailableLogged = true;
            TelescopeLog.Log($"preview editor unavailable: {_unavailableReason}");
        }
    }
}
