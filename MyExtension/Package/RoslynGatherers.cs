using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using Telescope.Finders;
using Telescope.Logging;

namespace MyExtension.Package
{
    /// <summary>
    /// Roslyn/VS-coupled gatherer logic for the references + implementations finders: resolves the
    /// symbol at the caret, gathers read/write references and implementations, and maps them to hit
    /// models. The package supplies the DTE / workspace / text-manager / editor-adapter factories
    /// (the finder host-injection pattern); this class owns the pure gather + reflection logic.
    /// <see cref="IsWriteLocation"/> is static so the reflection read of
    /// <c>ReferenceLocation.IsWrittenTo</c> is unit-testable offline.
    /// </summary>
    internal sealed class RoslynGatherers
    {
        // m5: read-once-per-gather source-line cache for the references finder — a gather no longer
        // re-opens + re-scans the file prefix per hit (O(hits x line) -> O(files)).
        private readonly FileContentCache _lineCache = new FileContentCache(500);

        private readonly Func<EnvDTE.DTE?> _dteFactory;
        private readonly Func<Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace?> _workspaceFactory;
        private readonly Func<Microsoft.VisualStudio.TextManager.Interop.IVsTextManager?> _textManagerFactory;
        private readonly Func<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService?> _editorAdapterFactory;

        public RoslynGatherers(
            Func<EnvDTE.DTE?> dteFactory,
            Func<Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace?> workspaceFactory,
            Func<Microsoft.VisualStudio.TextManager.Interop.IVsTextManager?> textManagerFactory,
            Func<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService?> editorAdapterFactory)
        {
            _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
            _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));
            _textManagerFactory = textManagerFactory ?? throw new ArgumentNullException(nameof(textManagerFactory));
            _editorAdapterFactory = editorAdapterFactory ?? throw new ArgumentNullException(nameof(editorAdapterFactory));
        }

        /// <summary>
        /// Gathers the read/write references to the symbol at the caret in the active document via
        /// Roslyn find-references (workspace = MEF <c>VisualStudioWorkspace</c>, symbol resolution
        /// via <c>SymbolFinder</c>). Runs on the UI thread; every Roslyn async call is wrapped in
        /// <c>ThreadHelper.JoinableTaskFactory.Run</c> — never a blocking sync-wait, which would
        /// deadlock the VS UI thread. Returns an empty list on any non-fatal failure.
        /// </summary>
        public IReadOnlyList<ReferenceHit> GatherReferences()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetCaretSymbol(out var workspace, out var document, out var symbol))
            {
                return Array.Empty<ReferenceHit>();
            }

            var solution = workspace.CurrentSolution;
            var refs = ThreadHelper.JoinableTaskFactory.Run(() =>
                Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindReferencesAsync(symbol, solution));

            var hits = new List<ReferenceHit>();
            foreach (var rs in refs)
            {
                foreach (var loc in rs.Locations)
                {
                    var span = loc.Location.GetLineSpan();
                    if (!span.IsValid)
                    {
                        continue;
                    }
                    string path = span.Path;
                    int line = span.StartLinePosition.Line + 1;       // 0-based -> 1-based
                    int col = span.StartLinePosition.Character + 1; // 0-based -> 1-based
                    hits.Add(new ReferenceHit(path, line, col, IsWriteLocation(loc), symbol.Name, ReadLineFromCache(path, line)));
                }
            }
            return hits;
        }

        /// <summary>
        /// Gathers the <b>implementations/overrides</b> of the symbol at the caret in the active
        /// document via Roslyn find-implementations (workspace = MEF <c>VisualStudioWorkspace</c>,
        /// symbol resolution via <c>SymbolFinder</c>). Each implementation symbol is mapped to ONE
        /// hit at its FIRST in-source declaring position (type-decl line for a type, override-decl
        /// line for a member); symbols with no in-source location (metadata types from referenced
        /// assemblies) are skipped. Runs on the UI thread; every Roslyn async call is wrapped in
        /// <c>ThreadHelper.JoinableTaskFactory.Run</c> — never a blocking sync-wait, which would
        /// deadlock the VS UI thread. Returns an empty list on any non-fatal failure.
        /// </summary>
        public IReadOnlyList<ImplementationHit> GatherImplementations()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetCaretSymbol(out var workspace, out var document, out var symbol))
            {
                return Array.Empty<ImplementationHit>();
            }

            var solution = workspace.CurrentSolution;
            var impls = ThreadHelper.JoinableTaskFactory.Run(() =>
                Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindImplementationsAsync(symbol, solution));

            var hits = new List<ImplementationHit>();
            foreach (var impl in impls)
            {
                // Map the implementation SYMBOL (not a reference location) to its declaring source
                // position: take the FIRST declaring syntax reference (partial types may have
                // several), falling back to the first in-source location. Skip any symbol with no
                // in-source location (metadata types from referenced assemblies).
                string? path = null;
                int line = 0;

                var src = impl.DeclaringSyntaxReferences.FirstOrDefault();
                if (src != null)
                {
                    var span = src.SyntaxTree.GetLineSpan(src.Span);
                    if (!string.IsNullOrEmpty(span.Path))
                    {
                        path = span.Path;
                        line = span.StartLinePosition.Line + 1; // 0-based -> 1-based
                    }
                }

                if (path == null)
                {
                    var loc = impl.Locations.FirstOrDefault(l => l.IsInSource);
                    if (loc != null)
                    {
                        var span = loc.GetLineSpan();
                        if (!string.IsNullOrEmpty(span.Path))
                        {
                            path = span.Path;
                            line = span.StartLinePosition.Line + 1; // 0-based -> 1-based
                        }
                    }
                }

                if (path == null)
                {
                    continue;
                }

                string kind = impl is Microsoft.CodeAnalysis.INamedTypeSymbol nts
                    ? nts.TypeKind.ToString()
                    : impl.Kind.ToString();
                hits.Add(new ImplementationHit(path, line, impl.Name, kind));
            }

            // Deterministic ordering: the seeded Shape type (declaring line 2) and its member
            // Shape.Draw (declaring line 4) live in the SAME file, so OrderBy(FilePath) ties and
            // ThenBy(LineNumber) is the discriminator — the type-level implementation always sorts
            // before any member implementation, independent of Roslyn's enumeration order.
            return hits
                .OrderBy(h => h.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h.LineNumber)
                .ToList();
        }

        /// <summary>
        /// Gathers the <b>definition locations</b> of the symbol at the caret in the active
        /// document. The caret symbol resolved by <see cref="TryGetCaretSymbol"/>
        /// (SymbolFinder.FindSymbolAtPositionAsync) IS the definition symbol — Roslyn unifies
        /// symbol instances across occurrences — so its <c>ISymbol.DeclaringSyntaxReferences</c>
        /// are exactly the definition locations (synchronous; NO extra SymbolFinder async
        /// round-trip). Each syntax reference is mapped via SyntaxTree.GetLineSpan; symbols with
        /// no syntax-backed declaration (metadata-only, e.g. the caret on `string`) fall back to
        /// <c>ISymbol.Locations.Where(IsInSource)</c> and yield no hits when neither source exists
        /// (the ImplementationFinder skip precedent). Runs on the UI thread; the only Roslyn async
        /// calls are inside TryGetCaretSymbol (already wrapped in
        /// ThreadHelper.JoinableTaskFactory.Run — never a blocking sync-wait). Returns an empty
        /// list on any non-fatal failure.
        /// </summary>
        public IReadOnlyList<DefinitionHit> GatherDefinitions()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!TryGetCaretSymbol(out var workspace, out var document, out var symbol))
            {
                return Array.Empty<DefinitionHit>();
            }

            var locations = new List<(string Path, int Line)>();
            foreach (var src in symbol.DeclaringSyntaxReferences)
            {
                var span = src.SyntaxTree.GetLineSpan(src.Span);
                locations.Add((span.Path, span.StartLinePosition.Line + 1)); // 0-based -> 1-based
            }

            if (locations.Count == 0)
            {
                // Fallback (the ImplementationFinder precedent): symbols whose declarations are not
                // syntax-backed but do carry in-source locations.
                foreach (var loc in symbol.Locations)
                {
                    if (!loc.IsInSource)
                    {
                        continue;
                    }
                    var span = loc.GetLineSpan();
                    locations.Add((span.Path, span.StartLinePosition.Line + 1));
                }
            }

            // The display kind — the GatherImplementations precedent (RoslynGatherers.cs:141-143):
            // named types report their TypeKind (Class/Interface/...), other symbols their Kind.
            string kind = symbol is Microsoft.CodeAnalysis.INamedTypeSymbol nts
                ? nts.TypeKind.ToString()
                : symbol.Kind.ToString();
            return MapDefinitionHits(locations, symbol.Name, kind);
        }

        /// <summary>
        /// The PURE definition-hit mapping (hermetic-shape, no Roslyn types in the signature):
        /// drops empty paths / non-positive lines, dedupes (path,line) — a workspace-resolved
        /// symbol can carry declaring syntax references from several linked projects'
        /// compilations of the SAME file, and a duplicated single definition would wrongly flip
        /// the 1-hit direct jump into an overlay. NO ordering here (rev 1): the deterministic
        /// OrderBy(FilePath).ThenBy(LineNumber) lives FINDER-side in
        /// <see cref="DefinitionFinder.GatherHits"/> (contract X5) so it is hermetically
        /// testable; this mapping preserves the gather order.
        /// </summary>
        internal static IReadOnlyList<DefinitionHit> MapDefinitionHits(
            IEnumerable<(string Path, int Line)> locations, string symbolName, string kind)
        {
            return locations
                .Where(l => !string.IsNullOrEmpty(l.Path) && l.Line >= 1)
                .Select(l => new DefinitionHit(l.Path, l.Line, symbolName, kind))
                .GroupBy(h => h.FilePath + "|" + h.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// Resolves the symbol at the caret in the active document via Roslyn (workspace = MEF
        /// <c>VisualStudioWorkspace</c>, symbol resolution via <c>SymbolFinder</c>). The shared
        /// prologue of the references/implementations gatherers. Runs on the UI thread; every
        /// Roslyn async call is wrapped in <c>ThreadHelper.JoinableTaskFactory.Run</c> — never a
        /// blocking sync-wait, which would deadlock the VS UI thread. Returns false (with all out
        /// params null) when no reliable symbol can be resolved.
        /// </summary>
        private bool TryGetCaretSymbol(
            out Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace workspace,
            out Microsoft.CodeAnalysis.Document document,
            out Microsoft.CodeAnalysis.ISymbol symbol)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            workspace = null!;
            document = null!;
            symbol = null!;

            var dte = _dteFactory();
            var active = dte?.ActiveDocument;
            if (active == null)
            {
                return false;
            }

            workspace = _workspaceFactory()!;
            if (workspace == null)
            {
                return false;
            }

            var solution = workspace.CurrentSolution;
            var filePath = active.FullName;
            var docId = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
            if (docId == null)
            {
                return false;
            }
            document = solution.GetDocument(docId);
            if (document == null)
            {
                return false;
            }

            // Caret offset: prefer the active editor text view (robust under VsVim). Fall back to
            // DTE TextSelection line/col -> SourceText offset if the view is unavailable.
            int caret = GetCaretOffset(dte!, active, document);
            if (caret < 0)
            {
                return false;
            }

            var doc = document;
            var ws = workspace;
            var semanticModel = ThreadHelper.JoinableTaskFactory.Run(
                () => doc.GetSemanticModelAsync(System.Threading.CancellationToken.None));
            symbol = ThreadHelper.JoinableTaskFactory.Run(() =>
                Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindSymbolAtPositionAsync(semanticModel, caret, ws));
            return symbol != null;
        }

        /// <summary>
        /// Resolves the caret's 0-based buffer offset in <paramref name="document"/>: the active
        /// editor text view's caret position when available (robust under VsVim), else the DTE
        /// TextSelection line/column mapped through the document's <see cref="Microsoft.CodeAnalysis.Text.SourceText"/>.
        /// Returns -1 when no reliable caret can be resolved.
        /// </summary>
        private int GetCaretOffset(EnvDTE.DTE dte, EnvDTE.Document active, Microsoft.CodeAnalysis.Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var textManager = _textManagerFactory();
                if (textManager != null)
                {
                    textManager.GetActiveView(1, null, out Microsoft.VisualStudio.TextManager.Interop.IVsTextView textView);
                    if (textView != null)
                    {
                        var adapter = _editorAdapterFactory();
                        var wpfView = adapter?.GetWpfTextView(textView);
                        if (wpfView != null)
                        {
                            return wpfView.Caret.Position.BufferPosition.Position;
                        }
                    }
                }

                if (active?.Selection is EnvDTE.TextSelection selection)
                {
                    int line = selection.ActivePoint.Line;              // 1-based
                    int column = selection.ActivePoint.LineCharOffset;  // 1-based, non-expanded (n17)
                    var text = ThreadHelper.JoinableTaskFactory.Run(
                        () => document.GetTextAsync(System.Threading.CancellationToken.None));
                    if (line >= 1 && line <= text.Lines.Count)
                    {
                        return text.Lines[line - 1].Start + Math.Max(0, column - 1);
                    }
                }
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}GetCaretOffset failed: {ex.Message}");
            }
            return -1;
        }

        /// <summary>Reads the given 1-based source line from <paramref name="path"/> for display; defensive.
        /// Served from the shared mtime-keyed <see cref="FileContentCache"/> so a gather does not re-open +
        /// re-scan the file prefix per hit (m5).</summary>
        private string ReadLineFromCache(string path, int line)
        {
            try
            {
                string[] lines = _lineCache.GetLines(path);
                return line >= 1 && line <= lines.Length ? lines[line - 1] : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Resolves a reference location's read/write flag. Roslyn's <c>ReferenceLocation.IsWrittenTo</c>
        /// (the read/write source of truth from the find-references engine) is public in older Roslyn
        /// but internal in Roslyn 4.14+ (VS 17.14) — the property name is stable across both, so it is
        /// read via reflection, mirroring the extension's VsVim interop pattern. False on any failure.
        /// N27/BP-51: the <see cref="System.Reflection.PropertyInfo"/> is resolved once per type
        /// (static cache) instead of per reference location.
        /// </summary>
        private static readonly System.Reflection.PropertyInfo? IsWrittenToProperty =
            typeof(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation).GetProperty(
                "IsWrittenTo",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        public static bool IsWriteLocation(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation loc)
        {
            try
            {
                return IsWrittenToProperty != null && IsWrittenToProperty.GetValue(loc, null) is bool b && b;
            }
            catch
            {
                return false;
            }
        }
    }
}
