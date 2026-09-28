using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Telescope;

namespace MyExtension
{
    /// <summary>
    /// The VS package class — the entry point VS loads from the VSIX. A "package" is VS's unit of
    /// an extension: the class under <c>AsyncPackage</c> gets initialized when its auto-load
    /// context triggers, and is where we install the global keyboard hook.
    ///
    /// <para/>
    /// <b>Package registration attributes:</b>
    ///   - <c>[PackageRegistration(AllowsBackgroundLoading = true)]</c> lets VS initialize us on a
    ///     background thread instead of blocking the UI thread during startup.
    ///   - <c>[ProvideAutoLoad(NoSolution, BackgroundLoad)]</c> loads us even with no solution open,
    ///     again on a background thread.
    ///   - <c>[Guid]</c> gives the package a stable identity used in the .pkgdef / shell registration.
    ///
    /// <para/>
    /// <b>Async initialization + UI-thread switch:</b> <see cref="InitializeAsync"/> runs on a
    /// background thread. The keyboard hook and its callback must live on the UI thread (they
    /// marshal to VS objects), so after the base init we <c>await SwitchToMainThreadAsync</c>
    /// before constructing the hook. <see cref="JoinableTaskFactory"/> is the VS-provided
    /// async/threading coordinator that makes this transition correctly (never block on
    /// <c>.Result</c>/<c>.Wait()</c> — that deadlocks the VS UI thread).
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(MyExtensionPackage.PackageGuidString)]
    public sealed class MyExtensionPackage : AsyncPackage
    {
        /// <summary>Stable package identity used by the registration attributes.</summary>
        public const string PackageGuidString = "2f73bf14-6619-47e7-850c-29e95557f429";

        private GlobalKeyboardHook? _keyboardLogger;
        private WindowManager? _windowManager;
        private TelescopeController? _telescope;
        private TelescopeLauncher? _launcher;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            NeoVisualLog.Debug("=== Global Keyboard Logger Package STARTED ===");
            await base.InitializeAsync(cancellationToken, progress);

            // Configure the per-run log file (env-driven), start a fresh log for this instance,
            // and route every Debug.WriteLine into it too. The log folder/run-index come from the
            // harness (NEOVISUAL_LOG_DIR / NEOVISUAL_LOG_INDEX); the exp vs main suffix is
            // detected from this process's command line.
            ConfigureLogFile();
            Telescope.NeoVisualLog.Clear();
            Telescope.NeoVisualLog.InstallDebugListener();
            NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}session started");

            // The hook must be installed on the UI thread (its callback touches VS objects and
            // runs as part of the UI thread's message pump). Switch off the background init thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Wrapped so a bad state (e.g. missing VsVim) can't take the whole package down; the
            // extension degrades to a no-op and we log the reason.
            try
            {
                _telescope = new TelescopeController();
                _launcher = new TelescopeLauncher(this, _telescope);
                _telescope.RegisterFinder(new FileFinder(() => VsServices.Dte(this)));
                _telescope.RegisterFinder(new CodeIssuesFinder(() => VsServices.Dte(this)));
                _telescope.RegisterFinder(new GrepFinder(() => VsServices.Dte(this)));
                _telescope.RegisterFinder(new ReferencesFinder(
                    () => GatherReferences(),
                    hit => OpenReference(hit)));
                _telescope.RegisterFinder(new ImplementationFinder(
                    () => GatherImplementations(),
                    hit => OpenImplementation(hit)));

                // Build WindowManager (current-window tracking + tool-window controller dispatch)
                // before the hook so InputHandler can consume its cached state from the start.
                var monitorSelection = await GetServiceAsync<SVsShellMonitorSelection, IVsMonitorSelection>(throwOnFailure: true, cancellationToken);
                _windowManager = new WindowManager(monitorSelection);
                _windowManager.RegisterController(new SolutionExplorerController(() => VsServices.Dte(this)));

                // Register a controller for every tool-window type explicitly (one per type, so
                // mode is remembered per window type): text-input surfaces get the vim text-motion
                // controller, everything else the general hjkl controller.
                foreach (ToolWindowType type in Enum.GetValues(typeof(ToolWindowType)))
                {
                    if (type == ToolWindowType.Unknown)
                    {
                        continue;
                    }
                    _windowManager.RegisterController(GeneralToolWindowController.IsTextInputType(type)
                        ? new TextInputToolWindowController(type)
                        : new GeneralToolWindowController(type));
                }

                // The shell/main window is still configuring during early init and steals focus when
                // it finishes. For testing, wait until the shell is fully initialized so the
                // environment is stable (and won't grab focus) before we open the solution/hook.
                await WaitForShellInitializedAsync(cancellationToken);

                // For testing: if NEOVISUAL_TEST_SOLUTION is set, open it in this (experimental)
                // instance via DTE so VS isn't stuck on the "select project/solution" window.
                await TryAutoOpenSolutionAsync(cancellationToken);

                _keyboardLogger = new GlobalKeyboardHook(this, _telescope, _windowManager);

                await RegisterTelescopeCommandAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                NeoVisualLog.Debug($"{Telescope.DiagnosticLog.MyExtension}Failed to initialize keyboard hook: {ex}");
            }
        }

        /// <summary>
        /// Blocks until the VS shell is fully initialized (<c>IVsShell.IsShellInitialized</c>).
        /// During early package load the main window is still configuring and will grab keyboard
        /// focus when it finishes — which would steal focus from any overlay (e.g. Telescope) opened
        /// by the test harness. Waiting here keeps the environment stable before we install the hook
        /// or open the solution. Never throws.
        /// </summary>
        private async Task WaitForShellInitializedAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                try
                {
                    var shell = await GetServiceAsync(typeof(SVsShell)) as IVsShell;
                    if (shell != null)
                    {
                        object value = false;
                        if (shell.GetProperty((int)__VSSPROPID4.VSSPROPID_ShellInitialized, out value) == 0
                            && value is bool b && b)
                        {
                            NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}shell initialized");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}shell init check failed: {ex.Message}");
                    return;
                }
                try
                {
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// If the <c>NEOVISUAL_TEST_SOLUTION</c> environment variable is set, opens that solution
        /// in the current (experimental) Visual Studio instance via DTE — the same instance that is
        /// already hosting this extension. This is a testing convenience so VS doesn't sit on the
        /// "select project/solution" start window; it does nothing in normal (non-test) runs.
        /// </summary>
        private async Task TryAutoOpenSolutionAsync(CancellationToken cancellationToken)
        {
            // Read from the registry (User scope) as well as the process env. The experimental
            // instance inherits a SNAPSHOT of the spawning main VS's environment taken when that
            // main VS launched — so a User-level var set after the main VS started never reaches
            // the child. Reading the User target reads the registry directly, bypassing inheritance.
            string? path = Environment.GetEnvironmentVariable("NEOVISUAL_TEST_SOLUTION");
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Environment.GetEnvironmentVariable("NEOVISUAL_TEST_SOLUTION", EnvironmentVariableTarget.User);
            }
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                var dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null)
                {
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}DTE unavailable; cannot auto-open solution.");
                    return;
                }
                if (dte.Solution.IsOpen)
                {
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}solution already open: {dte.Solution.FullName}");
                    return;
                }
                dte.Solution.Open(path);
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}auto-opened solution: {path}");

                // Open the first source file in the opened solution so there is a real editor
                // window (not just an empty tab well).
                OpenFirstSourceFile(dte);

                // Bring the main window to the foreground once so the loaded solution's editor is
                // surfaced. (The "Get started" start window may still be up — that's fine to ignore.)
                await ActivateMainWindowAsync(dte, cancellationToken);
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}auto-open solution failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Brings the DTE main window to the foreground once. Done right after the programmatic
        /// solution open so the editor area is visible instead of sitting behind the start window.
        /// Never throws.
        /// </summary>
        private async Task ActivateMainWindowAsync(EnvDTE.DTE dte, CancellationToken cancellationToken)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            try
            {
                dte.MainWindow.Activate();
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}activate main window failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Opens the first <c>.cs</c> source file found in the opened solution via DTE so an
        /// editor window is visible. Never throws.
        /// </summary>
        private void OpenFirstSourceFile(EnvDTE.DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string? path = FindFirstSourceFile(dte.Solution.Projects);
                if (path == null || !System.IO.File.Exists(path))
                {
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}no source file found to open in editor");
                    return;
                }
                dte.ItemOperations.OpenFile(path);
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}opened editor file: {path}");
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}open editor file failed: {ex.Message}");
            }
        }

        private static string? FindFirstSourceFile(EnvDTE.Projects projects)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                foreach (EnvDTE.Project project in projects)
                {
                    if (project == null)
                    {
                        continue;
                    }
                    string? found = FindFirstSourceFileInItems(project.ProjectItems);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            catch
            {
                // solution enumeration can throw on odd projects — treat as not found
            }
            return null;
        }

        private static string? FindFirstSourceFileInItems(EnvDTE.ProjectItems items)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (items == null)
            {
                return null;
            }
            try
            {
                foreach (EnvDTE.ProjectItem item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    try
                    {
                        if (item.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        {
                            return item.FileNames[0];
                        }
                    }
                    catch
                    {
                        // item has no file name (folder, virtual node) — keep descending
                    }
                    if (item.SubProject != null)
                    {
                        string? sub = FindFirstSourceFileInItems(item.SubProject.ProjectItems);
                        if (sub != null)
                        {
                            return sub;
                        }
                    }
                    string? nested = FindFirstSourceFileInItems(item.ProjectItems);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }
            catch
            {
                // project model can be flaky for SDK-style projects — stop descending
            }
            return null;
        }

        /// <summary>
        /// Configures the per-run log files. Directory comes from the <c>NEOVISUAL_LOG_DIR</c> env
        /// var (set by the harness); the run index is an auto-incrementing integer (1, 2, 3, ...)
        /// derived from the highest existing index in that directory, unless the process inherited a
        /// valid <c>NEOVISUAL_LOG_INDEX</c> from its parent (the launching VS instance) so the
        /// two files of one run share the same index. Two files are produced per run, both prefixed
        /// with the index: <c>&lt;index&gt;-neovisual-main.log</c> (raw debug output) and
        /// <c>&lt;index&gt;-neovisual-exp.log</c> (structured NeoVisual log).
        /// </summary>
        private static void ConfigureLogFile()
        {
            string? dir = Environment.GetEnvironmentVariable("NEOVISUAL_LOG_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                return; // not a harness run — keep the default AppData path
            }

            string index;
            string? inherited = Environment.GetEnvironmentVariable("NEOVISUAL_LOG_INDEX");
            if (int.TryParse(inherited, out int provided) && provided > 0)
            {
                index = provided.ToString();
            }
            else
            {
                // No inherited index: compute the next one (highest existing + 1, starting at 1)
                // and publish it to the process env so child instances spawned from this one
                // (e.g. the experimental instance launched by the main VS) reuse the same index.
                index = GetNextRunIndex(dir).ToString();
                try
                {
                    Environment.SetEnvironmentVariable("NEOVISUAL_LOG_INDEX", index, EnvironmentVariableTarget.Process);
                }
                catch
                {
                    // logging must never break the extension
                }
            }

            NeoVisualLog.ConfigureLogPath(dir, index);
        }

        /// <summary>
        /// Returns the next run index: one more than the highest existing
        /// <c>&lt;n&gt;-neovisual-*.log</c> index in <paramref name="dir"/>, or 1 when none exist.
        /// Never throws.
        /// </summary>
        private static int GetNextRunIndex(string dir)
        {
            int max = 0;
            try
            {
                if (!System.IO.Directory.Exists(dir))
                {
                    return 1;
                }
                foreach (string file in System.IO.Directory.GetFiles(dir, "*-neovisual-*.log"))
                {
                    // Only integer-prefixed names like "<n>-neovisual-exp.log" count; legacy
                    // timestamp-named files ("20260913-123359-neovisual-exp.log") are ignored so
                    // the index restarts cleanly at 1.
                    var match = System.Text.RegularExpressions.Regex.Match(
                        System.IO.Path.GetFileName(file), @"^(\d+)-neovisual");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int n))
                    {
                        max = Math.Max(max, n);
                    }
                }
            }
            catch
            {
                return max + 1;
            }
            return max + 1;
        }

        /// <summary>
        /// Registers the <c>Telescope.Show</c> VS command so the picker can also be invoked from
        /// the command palette / menus, in addition to the leader-key binding.
        /// </summary>
        private async Task RegisterTelescopeCommandAsync(CancellationToken cancellationToken)
        {
            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                return;
            }

            var cmdId = new CommandID(GuidList.CommandSet, CommandList.TelescopeShow);
            var menuItem = new OleMenuCommand((_, _) => OpenTelescope(), cmdId);
            commandService.AddCommand(menuItem);
        }

        private void OpenTelescope()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _launcher?.Open("Files");
        }

        // ================================================================
        // References finder host: Roslyn find-references gatherer + DTE opener
        // ================================================================

        /// <summary>
        /// Opens a reference hit's file in the editor and jumps the caret to the hit's 1-based
        /// line (line-level ONLY — <see cref="ReferenceHit.Column"/> is reported metadata, not a
        /// column jump, matching <see cref="CodeIssuesFinder"/>). The finder's
        /// <c>OnSelected</c> wraps this call and emits the <c>opened reference</c> diagnostic.
        /// </summary>
        private void OpenReference(ReferenceHit hit)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!System.IO.File.Exists(hit.FilePath))
            {
                return;
            }
            OpenFileAtLine(hit.FilePath, hit.LineNumber);
        }

        /// <summary>
        /// Opens an implementation hit's file in the editor and jumps the caret to the hit's
        /// 1-based declaring line (line-level ONLY — no column metadata is carried on
        /// <see cref="ImplementationHit"/>). The finder's <c>OnSelected</c> wraps this call and
        /// emits the <c>opened implementation</c> diagnostic.
        /// </summary>
        private void OpenImplementation(ImplementationHit hit)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!System.IO.File.Exists(hit.FilePath))
            {
                return;
            }
            OpenFileAtLine(hit.FilePath, hit.LineNumber);
        }

        /// <summary>Opens a file in the editor and jumps the caret to the given 1-based line.</summary>
        private void OpenFileAtLine(string path, int line)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DteFileOpener.OpenAtLine(VsServices.Dte(this), path, line);
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

            var dte = VsServices.Dte(this);
            var active = dte?.ActiveDocument;
            if (active == null)
            {
                return false;
            }

            workspace = VsServices.Mef<Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace>(this);
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
            int caret = GetCaretOffset(dte, active, document);
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
        /// Gathers the read/write references to the symbol at the caret in the active document via
        /// Roslyn find-references (workspace = MEF <c>VisualStudioWorkspace</c>, symbol resolution
        /// via <c>SymbolFinder</c>). Runs on the UI thread; every Roslyn async call is wrapped in
        /// <c>ThreadHelper.JoinableTaskFactory.Run</c> — never a blocking sync-wait, which would
        /// deadlock the VS UI thread. Returns an empty list on any non-fatal failure.
        /// </summary>
        private IReadOnlyList<ReferenceHit> GatherReferences()
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
                    hits.Add(new ReferenceHit(path, line, col, IsWriteLocation(loc), symbol.Name, ReadLine(path, line)));
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
        private IReadOnlyList<ImplementationHit> GatherImplementations()
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
                var textManager = ((System.IServiceProvider)this).GetService(typeof(Microsoft.VisualStudio.TextManager.Interop.SVsTextManager))
                    as Microsoft.VisualStudio.TextManager.Interop.IVsTextManager;
                if (textManager != null)
                {
                    textManager.GetActiveView(1, null, out Microsoft.VisualStudio.TextManager.Interop.IVsTextView textView);
                    if (textView != null)
                    {
                        var adapter = VsServices.Mef<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService>(this);
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
                    int column = selection.ActivePoint.DisplayColumn;   // 1-based
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
                NeoVisualLog.Debug($"{Telescope.DiagnosticLog.Telescope}GetCaretOffset failed: {ex.Message}");
            }
            return -1;
        }

        /// <summary>Reads the given 1-based source line from <paramref name="path"/> for display; defensive.</summary>
        private static string ReadLine(string path, int line)
        {
            try
            {
                return System.IO.File.ReadLines(path).Skip(line - 1).FirstOrDefault() ?? string.Empty;
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
        /// </summary>
        private static bool IsWriteLocation(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation loc)
        {
            try
            {
                var property = typeof(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation).GetProperty(
                    "IsWrittenTo",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                return property != null && property.GetValue(loc, null) is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keyboardLogger?.Dispose();
                _keyboardLogger = null;
                _windowManager?.Dispose();
                _windowManager = null;
                _telescope?.Dispose();
                _telescope = null;
                Telescope.NeoVisualLog.Close();
            }

            base.Dispose(disposing);
        }
    }
}
