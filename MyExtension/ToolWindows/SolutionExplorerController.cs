using System;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using MyExtension.Hooks;
using MyExtension.Vim;

namespace MyExtension.ToolWindows
{
    /// <summary>
    /// Controller for the Solution Explorer tool window. Extends the default hjkl tree navigation
    /// with the common file actions mapped to single keys:
    ///   - <c>o</c> / <c>Enter</c>  open the selected item
    ///   - <c>r</c>                  rename the selected item
    ///   - <c>m</c>                  move the selected item
    ///   - <c>a</c>                  add a new item to the selected project
    ///
    /// <para/>
    /// The open/rename actions are performed by injecting the native keys VS already understands
    /// (Enter / F2); move and add run the equivalent DTE commands. Every action is logged so the
    /// live E2E harness can assert it fired.
    ///
    /// <para/>
    /// <b>Threading:</b> all members are called on the UI thread only (same thread as the hook).
    /// </summary>
    internal sealed class SolutionExplorerController : ToolWindowControllerBase
    {
        private readonly Func<EnvDTE.DTE> _dteFactory;

        // C2: the focus-keeper is per-controller (an instance, not the shared static) so one
        // controller's Run can never stop another's keeper.
        private readonly FocusKeeper _keeper = new FocusKeeper();

        // N22: the current focus-keeper handle, disposed before a new keeper starts so a superseded
        // keeper's queued tick cannot re-assert the old target. C6: reset to null after dispose
        // (ResetFocusKeeper) — a disposed handle is never reused.
        private IDisposable? _focusKeeper;

        // N71: the search box resolved by ExitInputMode, passed through OnModeChanged so the caret
        // restyle does not re-walk the visual tree.
        private System.Windows.Controls.TextBox? _pendingStyleBox;

        // m5 (BP-10): the single seam through which TryMove resolves the focused WPF text box — a
        // test replaces it with a counting lambda to prove the box is resolved ONCE per routed key
        // (no double visual-tree walk).
        private Func<System.Windows.Controls.TextBox?> _findFocusedTextBox = TextMotionHelper.FindFocusedTextBox;

        /// <summary>How long the focus-keeper re-asserts tree focus/selection (m9 — single source).</summary>
        private const int FocusKeeperDurationMs = 1500;

        public SolutionExplorerController(Func<EnvDTE.DTE> dteFactory) : base(ToolWindowType.SolutionExplorer)
        {
            _dteFactory = dteFactory;
            // N62: the _actions table + TryMove/ActionKeys lookup live in the base.
            _actions[Keys.I] = () => { FocusSearchBox(); return true; };
            _actions[Keys.O] = () => { OpenSelected(); return true; };
            _actions[Keys.Enter] = () => { OpenSelected(); return true; };
            _actions[Keys.R] = () => { RenameSelected(); return true; };
            _actions[Keys.M] = () => { MoveSelected(); return true; };
            _actions[Keys.A] = () => { AddItem(); return true; };
            _actions[Keys.G] = () => { SelectFirstSourceFile(); return true; };
            // N20: all four hjkl keys use one shape (TreeMove); only H/L emit the fold diagnostic.
            _actions[Keys.H] = TreeMove(Keys.H);
            _actions[Keys.L] = TreeMove(Keys.L);
            _actions[Keys.J] = TreeMove(Keys.J);
            _actions[Keys.K] = TreeMove(Keys.K);
            AddTextMotionKeys(_actions);
            _actions[Keys.D0] = TextMotion(Keys.D0);
            _actions[Keys.D4] = TextMotion(Keys.D4);
        }

        /// <summary>N20: the shared hjkl→arrow shape for the tree; H/L additionally log the fold
        /// diagnostic (byte-identical to the previous inline lambdas).</summary>
        private static Func<bool> TreeMove(Keys key) => () =>
        {
            if (key == Keys.H)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer collapse");
            }
            else if (key == Keys.L)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer expand");
            }
            return GeneralToolWindowController.TryMoveArrow(key);
        };

        protected override void OnModeChanged()
        {
            // N71: reuse the box ExitInputMode already resolved (no second visual-tree walk).
            var box = _pendingStyleBox;
            _pendingStyleBox = null;
            if (box != null)
            {
                TextMotionHelper.StyleFocusedSurface(_isInputMode, box);
            }
            else
            {
                TextMotionHelper.StyleFocusedSurface(_isInputMode);
            }
        }

        public override void ExitInputMode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Capture the typed query BEFORE any focus action: the first Escape clears the search
            // box's text, so reading it afterwards would always yield empty. R17: resolve the
            // focused box once and reuse it for the post-exit focus check (base.ExitInputMode does
            // not move focus, so the box is unchanged between the two reads).
            var focusedBox = TextMotionHelper.FindFocusedTextBox();
            string query = focusedBox?.Text ?? string.Empty;
            // N71: pass the already-resolved box through OnModeChanged so the caret restyle does
            // not re-walk the visual tree per Esc.
            _pendingStyleBox = focusedBox;
            base.ExitInputMode();
            // If we came out of input mode while the search box still had focus (i focused it),
            // return focus to the tree so j/k/h/l continue to navigate the tree, not type into
            // the search box.
            if (focusedBox != null)
            {
                ReturnFocusToTree(query);
            }
            else
            {
                ExecuteCommand("View.SolutionExplorer");
            }
        }

        /// <summary>
        /// Returns keyboard focus to the Solution Explorer tree and explicitly selects the tree node
        /// whose name matches the captured search-box <paramref name="query"/>. VS's native search
        /// filter does NOT select the matching item, so the query is resolved through
        /// <see cref="HierarchyResolver.FirstPathMatching"/> over the project's
        /// <see cref="HierarchyNode"/> forest (<see cref="FindFirstProjectNode"/> + <see cref="HierarchyForestBuilder"/>).
        /// A first injected Escape clears the query; a <see cref="System.Windows.Threading.DispatcherTimer"/>
        /// keeper observes the real focus state and injects further bounded Escapes while the search box
        /// still has focus (Escape #2 is what actually moves focus to the tree), then re-<c>Select</c>s
        /// the matched item + re-activates the tool window for ~1.5s to defeat VS's hover-preview focus steal.
        /// </summary>
        private void ReturnFocusToTree(string query)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RunGuarded("focus-tree failed", () =>
            {
                var dte2 = _dteFactory() as EnvDTE80.DTE2;
                // Early null guard (mirrors SelectFirstSourceFile): without it an NRE is swallowed by
                // the catch and the caller sees a misleading "focus never left" symptom.
                if (dte2 == null)
                {
                    ExecuteCommand("View.SolutionExplorer");
                    return;
                }

                // Resolve the query-matched tree item: VS's native search filter never selects the
                // matching node, so we must select it ourselves.
                var (target, _) = ResolveTreeItem(dte2, forest => HierarchyResolver.FirstPathMatching(forest, query));

                // Native Escape #1 clears the query. Press() records the VK in InjectedKeyGuard so the
                // hook passes it through; _isInputMode is already false, so no second
                // toolwindow-exit-input can fire.
                KeyInjection.Press(KeyInjection.VK_ESCAPE);

                // Set the selection as early as possible (focus follows the selection).
                target?.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
                ExecuteCommand("View.SolutionExplorer");

                // Hover-preview / async-focus robustness: re-assert tree focus + the matched selection
                // on a ~100ms DispatcherTimer for ~1.5s, like SelectFirstSourceFile.
                int escapeAttempts = 0;
                // N22: dispose the prior keeper before starting a new one so a superseded keeper's
                // queued tick cannot re-assert the old target. C6: reset the handle to null.
                ResetFocusKeeper();
                _focusKeeper = _keeper.Run(System.TimeSpan.FromMilliseconds(100), FocusKeeperDurationMs, elapsed =>
                {
                    // m21 stop-on-close: if the Solution Explorer window is no longer visible, stop
                    // re-asserting (the user closed it — don't keep re-opening it).
                    if (!IsSolutionExplorerVisible(dte2))
                    {
                        return false;
                    }
                    var decision = FocusKeeperSchedule.Decide(
                        TextMotionHelper.FindFocusedTextBox() != null, elapsed, escapeAttempts, FocusKeeperDurationMs);
                    if (decision == FocusKeeperSchedule.Decision.InjectEscape)
                    {
                        // Focus has NOT left the search box yet — Escape #2 is what actually moves
                        // focus to the tree; the counter bounds the loop.
                        escapeAttempts++;
                        KeyInjection.Press(KeyInjection.VK_ESCAPE);
                    }
                    else if (decision == FocusKeeperSchedule.Decision.Reassert)
                    {
                        target?.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
                        ExecuteCommand("View.SolutionExplorer");
                    }
                    return decision != FocusKeeperSchedule.Decision.Stop;
                });
            });
        }

        /// <summary>The non-hjkl action keys this controller handles in normal mode. w/b/e are vim
        /// text motions for the search box; when the tree (not the search box) is focused they are
        /// not consumed and fall through.</summary>
        public override System.Collections.Generic.IReadOnlyCollection<Keys> ActionKeys => _actions.Keys;

        public override bool TryMove(Keys key)
        {
            // If a WPF TextBox (the Solution Explorer search box) is focused, the controller behaves
            // like a text-input window: h/l/w/b/e/a/A/I move the caret, and every other key falls
            // through so it types into the search box (no tree actions, no j/k arrow injection). The
            // gate is mandatory: without it the merged helper's arrow fallback would swallow h/l in
            // the tree and replace the collapse/expand diagnostics with toolwindow-move.
            // A4: delegate to the shared text-input routing block (ToolWindowControllerBase.TextMotion)
            // — it applies the motion + the N21 enteredInputMode → EnterInputMode() side effect.
            // m5 (BP-10): the box is resolved ONCE and passed to the TextMotion(key, box) overload —
            // no second visual-tree walk per routed key.
            var box = _findFocusedTextBox();
            if (box != null)
            {
                return TextMotion(key, box)();
            }

            return _actions.TryGetValue(key, out var action) && action();
        }

        /// <summary>C6: disposes the current focus-keeper handle and resets it to null — a disposed
        /// handle is never reused (a re-run creates a fresh keeper).</summary>
        public void ResetFocusKeeper()
        {
            _focusKeeper?.Dispose();
            _focusKeeper = null;
        }

        private void OpenSelected()
        {
            // Enter is the native "open selected item" key in the Solution Explorer tree.
            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer open");
            KeyInjection.Press(KeyInjection.VK_RETURN);
        }

        /// <summary>
        /// Selects the FIRST physical .cs source file under the solution's first project, opens it
        /// in the editor, and leaves the Solution Explorer tree focused so a following <c>o</c>
        /// (Enter) reaches the controller. The file is found by walking the tree's
        /// <c>UIHierarchyItems</c> (the project node is expanded first so children are materialized)
        /// and selected programmatically (<c>UIHierarchyItem.Select</c> — no key injection, so the
        /// csproj-open trap of injected j/l is bypassed). The file is ALSO opened directly via
        /// <c>ItemOperations.OpenFile</c> so the E2E harness's <c>editor-view-opened</c> line pins
        /// the SAME full path as the <c>select file=</c> diagnostic; VS's hover-preview hijack is
        /// defeated by re-asserting the selection + tree focus for a short window. Emits
        /// <c>[NeoVisual] solution-explorer select file=&lt;full path&gt;</c> (or
        /// <c>select none</c> when no source file is reachable).
        /// </summary>
        private void SelectFirstSourceFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RunGuarded("solution-explorer select failed", () =>
            {
                var dte = _dteFactory();
                if (dte == null)
                {
                    Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer select none");
                    return;
                }

                var dte2 = dte as EnvDTE80.DTE2;
                if (dte2 == null)
                {
                    Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer select none");
                    return;
                }

                var (item, first) = ResolveTreeItem(dte2, HierarchyResolver.FirstSourceFilePath);
                if (first == null)
                {
                    Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer select none");
                    return;
                }

                // Programmatic select (no key injection). item is non-null whenever first != null —
                // the path came from the forest that populated pathToItem inside ResolveTreeItem.
                item!.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);

                // Open the file directly so the harness's `editor-view-opened` line pins the SAME
                // path as the `select file=` diagnostic (the injected-Enter chain in the harness
                // would otherwise race VS's hover-preview, which opens a different tree item).
                // Activate when a document for this file already exists, else ItemOperations.OpenFile.
                // R38: editor-view-opened is emitted exactly once per open — activating an
                // ALREADY-CREATED text view raises no TextViewCreated, so we emit it directly here;
                // a NEWLY-opened file's view is reported by VimModeTracker.TextViewCreated instead
                // (emitting it here too would double-count).
                EnvDTE.Document? doc = null;
                try { doc = dte.Documents.Item(first); } catch { doc = null; }
                if (doc != null)
                {
                    doc.Activate();
                    EditorViewOpenedLog.Emit(first);
                }
                else
                {
                    dte.ItemOperations.OpenFile(first);
                }

                // Keep the Solution Explorer tree focused + the selection pinned for ~1.5s: VS's
                // hover-preview (armed by the tree expansion) opens the tree's current item in the
                // editor and steals focus — re-selecting + re-focusing defeats it so the harness's
                // `o` still reaches the controller. (No per-tick document open: that would spam
                // editor-view-opened; we emitted exactly one above.)
                EnvDTE.UIHierarchyItem keepItem = item!;
                // N22: dispose the prior keeper before starting a new one.
                // C6: reset the handle to null.
                ResetFocusKeeper();
                _focusKeeper = _keeper.Run(System.TimeSpan.FromMilliseconds(100), FocusKeeperDurationMs, _ =>
                {
                    // m21 stop-on-close: if the Solution Explorer window is no longer visible, stop
                    // re-asserting (the user closed it — don't keep re-opening it).
                    if (!IsSolutionExplorerVisible(dte))
                    {
                        return false;
                    }
                    keepItem.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
                    ExecuteCommand("View.SolutionExplorer");
                    return true;
                });

                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer select file={first}");
            });
        }

        /// <summary>
        /// Shared expand→resolve→Select pipeline: expands the first project node, builds the pure
        /// <see cref="HierarchyNode"/> forest + full-path → <see cref="EnvDTE.UIHierarchyItem"/> map,
        /// runs the caller's <paramref name="pick"/> over the forest, and resolves the picked path
        /// back to its tree item. Returns <c>(null, null)</c> when no project node is reachable or
        /// the pick returns null; the callers keep their divergent null semantics (ReturnFocusToTree
        /// skips silently, SelectFirstSourceFile logs <c>select none</c>).
        /// </summary>
        private static (EnvDTE.UIHierarchyItem? Target, string? Path) ResolveTreeItem(
            EnvDTE80.DTE2 dte2,
            Func<System.Collections.Generic.List<HierarchyNode>, string?> pick)
        {
            EnvDTE.UIHierarchy seh = dte2.ToolWindows.SolutionExplorer;
            if (seh.UIHierarchyItems.Count == 0) return (null, null);
            var solutionNode = seh.UIHierarchyItems.Item(1);
            var projectNode = FindFirstProjectNode(solutionNode);
            if (projectNode == null) return (null, null);
            projectNode.UIHierarchyItems.Expanded = true;
            var pathToItem = new System.Collections.Generic.Dictionary<string, EnvDTE.UIHierarchyItem>(StringComparer.OrdinalIgnoreCase);
            var items = MapChildren(projectNode, pathToItem);
            var forest = HierarchyForestBuilder.Build(items);
            string? path = pick(forest);
            if (path == null) return (null, null);
            return (pathToItem.TryGetValue(path, out var t) ? t : null, path);
        }

        /// <summary>Finds the FIRST <see cref="EnvDTE.Project"/> node under the solution tree:
        /// descends the solution node's <c>UIHierarchyItems</c> through any
        /// <see cref="EnvDTE80.SolutionFolder"/> objects; other node kinds are skipped.</summary>
        private static EnvDTE.UIHierarchyItem? FindFirstProjectNode(EnvDTE.UIHierarchyItem node)
        {
            if (node.Object is EnvDTE.Project)
            {
                return node;
            }
            if (node.Object is EnvDTE.Solution || node.Object is EnvDTE80.SolutionFolder)
            {
                foreach (EnvDTE.UIHierarchyItem child in node.UIHierarchyItems)
                {
                    EnvDTE.UIHierarchyItem? hit = FindFirstProjectNode(child);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// DTE adapter: recurses a project node's tree into pure <see cref="HierarchyNode"/>
        /// DTOs plus a full-path → <see cref="EnvDTE.UIHierarchyItem"/> map. This is the ONLY place
        /// <c>pi.Kind</c> / <c>pi.Name</c> / <c>pi.FileNames[i]</c> are read. The caller
        /// must expand the node's <c>UIHierarchyItems</c> first (a collapsed node's children are
        /// not enumerated). Physical folders recurse; physical files are passed through (the
        /// <c>.cs</c> filter lives in <see cref="HierarchyForestBuilder"/>); everything else
        /// (virtual folders, references, sub-projects) is skipped.
        /// </summary>
        private static System.Collections.Generic.List<HierarchyNode> MapChildren(
            EnvDTE.UIHierarchyItem item,
            System.Collections.Generic.Dictionary<string, EnvDTE.UIHierarchyItem> pathToItem)
        {
            var result = new System.Collections.Generic.List<HierarchyNode>();
            foreach (EnvDTE.UIHierarchyItem child in item.UIHierarchyItems)
            {
                if (child.Object is EnvDTE.ProjectItem pi)
                {
                    string kind = pi.Kind;
                    if (kind == HierarchyResolver.PhysicalFolderKind)
                    {
                        var children = MapChildren(child, pathToItem);
                        result.Add(new HierarchyNode(kind, pi.Name, "", children));
                    }
                    else if (kind == HierarchyResolver.PhysicalFileKind)
                    {
                        // FileNames is a 1-based indexed property; FileNames[1] is the PRIMARY
                        // file's full path (the last file of a multi-file item is e.g. a .resx).
                        var fileNames = new System.Collections.Generic.List<string>();
                        for (short i = 1; i <= pi.FileCount; i++)
                        {
                            fileNames.Add(pi.FileNames[i]);
                        }
                        string? fullPath = HierarchyResolver.PrimaryFilePath(fileNames);
                        if (fullPath == null)
                        {
                            // R9: a corrupt project item with an empty FileNames list — skip it
                            // (no primary path to map).
                            continue;
                        }
                        result.Add(new HierarchyNode(kind, pi.Name, fullPath, null));
                        pathToItem[fullPath] = child;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Focuses the Solution Explorer search box via the native <c>Window.SolutionExplorerSearch</c>
        /// command (Ctrl+;), then enters input mode so the user can type the query. The command is
        /// executed through DTE because the search box is part of the tool window chrome, not the
        /// tree; we cannot tab/focus into it directly.
        /// </summary>
        private void FocusSearchBox()
        {
            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer search-focus");
            ExecuteCommand("Window.SolutionExplorerSearch");
            EnterInputMode();
            // TryMove(Keys.I) returns true, so InputHandler's generic tool-window branch never
            // logs its enter-input line — emit the same contract here so the E2E harness's
            // "i entered tool-window input mode" assertion stays stable.
            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}toolwindow-enter-input");
        }

        private void RenameSelected()
        {
            // F2 is the native rename shortcut in the Solution Explorer tree.
            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer rename");
            KeyInjection.Press(KeyInjection.VK_F2);
        }

        private void MoveSelected()
        {
            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer move");
            ExecuteCommand("SolutionExplorer.Move");
        }

        private void AddItem()
        {
            Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}solution-explorer add");
            ExecuteCommand("SolutionExplorer.AddItem");
        }

        private void ExecuteCommand(string command)
        {
            RunGuarded($"Command '{command}' failed", () =>
            {
                _dteFactory()?.ExecuteCommand(command, string.Empty);
            });
        }

        /// <summary>
        /// Runs <paramref name="action"/> and swallows any exception into the
        /// <c>[NeoVisual] {failureMessage}: {msg}</c> diagnostic (m25 — the single catch/log helper
        /// for the controller's guarded operations).
        /// </summary>
        private static void RunGuarded(string failureMessage, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}{failureMessage}: {ex.Message}");
            }
        }

        /// <summary>
        /// True while the Solution Explorer tool window is still visible. The focus-keeper's
        /// stop-on-close (m21): when the user closes the window, the keeper must stop re-asserting
        /// instead of re-opening it.
        /// </summary>
        private static bool IsSolutionExplorerVisible(EnvDTE.DTE dte)
        {
            try
            {
                EnvDTE.Window? window = dte.Windows.Item(EnvDTE.Constants.vsWindowKindSolutionExplorer);
                return window != null && window.Visible;
            }
            catch
            {
                return false;
            }
        }
    }
}