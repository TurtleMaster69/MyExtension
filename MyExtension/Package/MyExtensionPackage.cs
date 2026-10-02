using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MyExtension.Hooks;
using MyExtension.ToolWindows;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Telescope.Controller;
using Telescope.Finders;
using Telescope.Logging;

namespace MyExtension.Package
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

        private GlobalKeyboardHook? _keyboardHook;
        private WindowManager? _windowManager;
        private TelescopeController? _telescope;
        private TelescopeLauncher? _launcher;
        private IVsMonitorSelection? _monitorSelection;

        // m4 (BP-63): the Roslyn/VS-coupled gatherer logic (caret symbol resolution, references +
        // implementations gathering) lives in RoslynGatherers; the package supplies the DTE /
        // workspace / text-manager / editor-adapter factories.
        private RoslynGatherers? _roslynGatherers;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}=== Global Keyboard Logger Package STARTED ===");
            await base.InitializeAsync(cancellationToken, progress);

            // Configure the per-run log file (env-driven), start a fresh log for this instance,
            // and route every Debug.WriteLine into it too. The log folder/run-index come from the
            // harness (NEOVISUAL_LOG_DIR / NEOVISUAL_LOG_INDEX); the exp vs main suffix is
            // hardcoded in ConfigureLogPath (-exp.log = structured NeoVisual log the harness
            // asserts on, -main.log = raw debug-output stream), not command-line-detected. Each
            // pre-try step is protected so logging can never break package load.
            RunInitStep("configure-log", () => ConfigureLogFile());
            RunInitStep("clear-log", () => Telescope.Logging.NeoVisualLog.Clear());
            RunInitStep("install-debug-listener", () => Telescope.Logging.NeoVisualLog.InstallDebugListener());
            NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}session started");

            // The hook must be installed on the UI thread (its callback touches VS objects and
            // runs as part of the UI thread's message pump). Switch off the background init thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Wrapped so a bad state (e.g. missing VsVim) can't take the whole package down; the
            // extension degrades to a no-op and we log the reason. Each named step runs in its own
            // try/catch (via InitSteps) so a failure never aborts the later steps.
            try
            {
                var steps = new (string Name, Func<Task> Step)[]
                {
                    ("telescope", () =>
                    {
                        _telescope = new TelescopeController();
                        _launcher = new TelescopeLauncher(this, _telescope);
                        return Task.CompletedTask;
                    }),
                    ("finders", () =>
                    {
                        var fileCache = new ProjectFileCache();
                        _roslynGatherers = new RoslynGatherers(
                            () => VsServices.Dte(this),
                            () => VsServices.Mef<Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace>(this),
                            () => ((System.IServiceProvider)this).GetService(typeof(Microsoft.VisualStudio.TextManager.Interop.SVsTextManager))
                                as Microsoft.VisualStudio.TextManager.Interop.IVsTextManager,
                            () => VsServices.Mef<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService>(this));
                        _telescope.RegisterFinder(new FileFinder(() => VsServices.Dte(this)!));
                        _telescope.RegisterFinder(new CodeIssuesFinder(() => VsServices.Dte(this)!, fileCache));
                        _telescope.RegisterFinder(new GrepFinder(() => VsServices.Dte(this)!, fileCache));
                        _telescope.RegisterFinder(new ReferencesFinder(
                            () => _roslynGatherers!.GatherReferences(),
                            hit => OpenHitAtLine(hit)));
                        _telescope.RegisterFinder(new ImplementationFinder(
                            () => _roslynGatherers!.GatherImplementations(),
                            hit => OpenHitAtLine(hit)));
                        return Task.CompletedTask;
                    }),
                    ("monitor-selection", async () =>
                    {
                        _monitorSelection = await GetServiceAsync<SVsShellMonitorSelection, IVsMonitorSelection>(throwOnFailure: true, cancellationToken);
                    }),
                    ("window-manager", () =>
                    {
                        // Build WindowManager (current-window tracking + tool-window controller
                        // dispatch) before the hook so InputHandler can consume its cached state
                        // from the start.
                        _windowManager = new WindowManager(_monitorSelection);
                        _windowManager.RegisterController(new SolutionExplorerController(() => VsServices.Dte(this)!));
                        return Task.CompletedTask;
                    }),
                    ("controllers", () =>
                    {
                        // Register a controller for every tool-window type explicitly (one per
                        // type, so mode is remembered per window type): text-input surfaces get the
                        // vim text-motion controller, everything else the general hjkl controller.
                        // The factory returns null for SolutionExplorer (the specialized controller
                        // above is the ONLY registration) and Unknown, so the specialized
                        // controller is never overwritten regardless of registration order.
                        foreach (ToolWindowType type in Enum.GetValues(typeof(ToolWindowType)))
                        {
                            var controller = WindowManager.DefaultControllerFor(type);
                            if (controller != null)
                            {
                                _windowManager.RegisterController(controller);
                            }
                        }
                        return Task.CompletedTask;
                    }),
                    ("shell-wait", () =>
                    {
                        // The shell/main window is still configuring during early init and steals
                        // focus when it finishes. For testing, wait until the shell is fully
                        // initialized so the environment is stable (and won't grab focus) before
                        // we open the solution/hook. R13: this wait exists only for e2e focus
                        // stability — gate it on the harness env vars so normal (non-test) runs
                        // skip the up-to-20s dead-key delay.
                        if (!IsHarnessRun())
                        {
                            return Task.CompletedTask;
                        }
                        return WaitForShellInitializedAsync(cancellationToken);
                    }),
                    ("auto-open-solution", () =>
                    {
                        // For testing: if NEOVISUAL_TEST_SOLUTION is set, open it in this
                        // (experimental) instance via DTE so VS isn't stuck on the "select
                        // project/solution" window.
                        return TryAutoOpenSolutionAsync(cancellationToken);
                    }),
                    ("hook", () =>
                    {
                        // m8: the package's _launcher (constructed once in the "telescope" step) is
                        // injected through the hook into InputHandler — no second TelescopeLauncher.
                        _keyboardHook = new GlobalKeyboardHook(this, _telescope, _windowManager, _launcher!);
                        return Task.CompletedTask;
                    }),
                    ("command", () => RegisterTelescopeCommandAsync(cancellationToken)),
                };

                await new InitSteps(msg => NeoVisualLog.Log(msg)).RunAsync(steps);
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}init failed: {ex}");
            }
        }

        /// <summary>
        /// Runs a synchronous init step in its own try/catch, logging
        /// <c>[MyExtension] init &lt;name&gt; ok</c> / <c>[MyExtension] init &lt;name&gt; failed:
        /// {ex.Message}</c>. Never throws — logging must not break package load. Thin delegate to
        /// the shared <see cref="InitSteps.RunSync"/> contract.
        /// </summary>
        private void RunInitStep(string name, Action step)
        {
            InitSteps.RunSync(name, step, msg => NeoVisualLog.Log(msg));
        }

        /// <summary>
        /// True when this is a harness (e2e) run — the harness sets
        /// <c>NEOVISUAL_TEST_SOLUTION</c> / <c>NEOVISUAL_LOG_DIR</c>. R13: the shell-wait init
        /// step is gated on this so normal (non-test) runs skip the up-to-20s wait that exists
        /// only for e2e focus stability.
        /// </summary>
        private static bool IsHarnessRun()
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEOVISUAL_TEST_SOLUTION"))
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEOVISUAL_LOG_DIR"));
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
                            NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}shell initialized");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}shell init check failed: {ex.Message}");
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
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}DTE unavailable; cannot auto-open solution.");
                    return;
                }
                if (dte.Solution.IsOpen)
                {
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}solution already open: {dte.Solution.FullName}");
                    return;
                }
                dte.Solution.Open(path);
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}auto-opened solution: {path}");

                // Open the first source file in the opened solution so there is a real editor
                // window (not just an empty tab well).
                OpenFirstSourceFile(dte);

                // Bring the main window to the foreground once so the loaded solution's editor is
                // surfaced. (The "Get started" start window may still be up — that's fine to ignore.)
                await ActivateMainWindowAsync(dte, cancellationToken);
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}auto-open solution failed: {ex.Message}");
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
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}activate main window failed: {ex.Message}");
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
                string? path = ProjectFiles.Enumerate(dte).FirstOrDefault(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
                if (path == null || !System.IO.File.Exists(path))
                {
                    NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}no source file found to open in editor");
                    return;
                }
                dte.ItemOperations.OpenFile(path);
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}opened editor file: {path}");
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.MyExtension}open editor file failed: {ex.Message}");
            }
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
        // References/implementations finder host: DTE opener (the Roslyn gatherer logic lives in
        // RoslynGatherers — m4/BP-63)
        // ================================================================

        /// <summary>
        /// Opens a finder hit's file in the editor and jumps the caret to the hit's 1-based line
        /// (line-level ONLY — <see cref="ReferenceHit.Column"/> is reported metadata, not a column
        /// jump, matching <see cref="CodeIssuesFinder"/>). Shared by the references and
        /// implementations finders via <see cref="HitOpener"/>; the finders' <c>OnSelected</c>
        /// wraps this call and emits the <c>opened reference</c> / <c>opened implementation</c>
        /// diagnostic.
        /// </summary>
        private void OpenHitAtLine(IFileLocation hit)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            HitOpener.OpenAtLine(hit, (path, line) => OpenFileAtLine(path, line));
        }

        /// <summary>Opens a file in the editor and jumps the caret to the given 1-based line.</summary>
        private void OpenFileAtLine(string path, int line)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = VsServices.Dte(this);
            if (dte == null)
            {
                return;
            }
            DteFileOpener.OpenAtLine(dte, path, line);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keyboardHook?.Dispose();
                _keyboardHook = null;
                _windowManager?.Dispose();
                _windowManager = null;
                _telescope?.Dispose();
                _telescope = null;
                Telescope.Logging.NeoVisualLog.Close();
            }

            base.Dispose(disposing);
        }
    }
}
