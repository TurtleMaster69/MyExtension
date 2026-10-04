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

                DateTime stamp = File.GetLastWriteTimeUtc(location.FilePath);
                if (_documentPath != location.FilePath || _documentStamp != stamp)
                {
                    RebuildView(location.FilePath, stamp);
                }

                return new PreviewEditorResult(
                    _host!.HostControl, _document!.TextBuffer.CurrentSnapshot.GetText());
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"preview load failed: {ex.Message}");
                return PreviewEditorResult.Empty;
            }
        }

        /// <summary>Closes the previous view+document and creates the new ones (the mtime-cache
        /// miss path — the role the old PreviewTokenCache.ShouldRebuild played for the FlowDocument).</summary>
        private void RebuildView(string path, DateTime stamp)
        {
            CloseView();

            // Content type by file extension (the preview shows ANY solution file, not just C#):
            // .cs -> CSharp, .txt -> plaintext, ...; unregistered -> UnknownContentType (the view
            // still displays, just unclassified).
            IContentType contentType =
                _fileExtensionRegistry!.GetContentTypeForExtension(Path.GetExtension(path).TrimStart('.'))
                ?? _contentTypeRegistry!.UnknownContentType;

            _document = _textDocumentFactory!.CreateAndLoadTextDocument(path, contentType);
            _documentStamp = stamp;

            // Roles: Document + Interactive + Zoomable, EXCLUDING Editable (D-P3) — non-editable,
            // caret/selection enabled, VsVim never attaches.
            ITextViewRoleSet roles = _textEditorFactory!.CreateTextViewRoleSet(
                PredefinedTextViewRoles.Document,
                PredefinedTextViewRoles.Interactive,
                PredefinedTextViewRoles.Zoomable);

            var view = (IWpfTextView)_textEditorFactory.CreateTextView(_document.TextBuffer, roles);
            _host = _textEditorFactory.CreateTextViewHost(view, false /* do not steal focus */);
            _view = view;
            _documentPath = path;

            // The tokens= diagnostic: VS's own classifier-chain span count over the whole snapshot —
            // proves the editor's highlighting ran (the migration's point). Logged ONLY here (a
            // rebuild), matching the old renderer's emission pattern (reuse skipped the log).
            TelescopeLog.Log($"preview tokens={CountClassificationSpans(_view)}");
            TelescopeLog.Log(PreviewDiagnostics.File(path, _document.TextBuffer.CurrentSnapshot.Length));
        }

        private int CountClassificationSpans(IWpfTextView view)
        {
            // Own try/catch: a classifier failure must not kill the (already hosted) view — the
            // count reads 0 and the harness's tokens>=1 assertion surfaces the broken highlighting.
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
            if (_document != null)
            {
                try { _document.Dispose(); }
                catch { /* already disposed */ }
                _document = null;
            }
            _documentPath = null;
            _documentStamp = default;
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
