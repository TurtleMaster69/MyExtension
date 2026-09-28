using System;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace MyExtension
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
        private readonly System.Collections.Generic.Dictionary<Keys, Func<bool>> _actions;

        public SolutionExplorerController(Func<EnvDTE.DTE> dteFactory) : base(ToolWindowType.SolutionExplorer)
        {
            _dteFactory = dteFactory;
            // Solution Explorer is a tree, not a text-input surface: start in normal mode.
            _isInputMode = false;
            _actions = new System.Collections.Generic.Dictionary<Keys, Func<bool>>
            {
                [Keys.I] = () => { FocusSearchBox(); return true; },
                [Keys.O] = () => { OpenSelected(); return true; },
                [Keys.Enter] = () => { OpenSelected(); return true; },
                [Keys.R] = () => { RenameSelected(); return true; },
                [Keys.M] = () => { MoveSelected(); return true; },
                [Keys.A] = () => { AddItem(); return true; },
                [Keys.G] = () => { SelectFirstSourceFile(); return true; },
                [Keys.H] = () => { Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer collapse"); KeyInjection.Press(KeyInjection.VK_LEFT); return true; },
                [Keys.L] = () => { Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer expand"); KeyInjection.Press(KeyInjection.VK_RIGHT); return true; },
                [Keys.J] = () => { Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}toolwindow-move key=J -> arrow vk=40"); KeyInjection.Press(KeyInjection.VK_DOWN); return true; },
                [Keys.K] = () => { Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}toolwindow-move key=K -> arrow vk=38"); KeyInjection.Press(KeyInjection.VK_UP); return true; },
                [Keys.W] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.W, ref _isInputMode),
                [Keys.B] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.B, ref _isInputMode),
                [Keys.E] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.E, ref _isInputMode),
            };
        }

        protected override void OnModeChanged() => TextMotionHelper.StyleFocusedSurface(_isInputMode);

        public override void ExitInputMode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Capture the typed query BEFORE any focus action: the first Escape clears the search
            // box's text, so reading it afterwards would always yield empty.
            string query = TextMotionHelper.FindFocusedTextBox()?.Text ?? string.Empty;
            base.ExitInputMode();
            // If we came out of input mode while the search box still had focus (i focused it),
            // return focus to the tree so j/k/h/l continue to navigate the tree, not type into
            // the search box.
            if (TextMotionHelper.FindFocusedTextBox() != null)
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
        /// <see cref="HierarchyNode"/> forest (<see cref="FindFirstProjectNode"/> + <see cref="BuildForest"/>).
        /// A first injected Escape clears the query; a <see cref="System.Windows.Threading.DispatcherTimer"/>
        /// keeper observes the real focus state and injects further bounded Escapes while the search box
        /// still has focus (Escape #2 is what actually moves focus to the tree), then re-<c>Select</c>s
        /// the matched item + re-activates the tool window for ~1.5s to defeat VS's hover-preview focus steal.
        /// </summary>
        private void ReturnFocusToTree(string query)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
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
                var keeper = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal);
                keeper.Interval = System.TimeSpan.FromMilliseconds(100);
                System.Windows.Threading.DispatcherTimer keeperRef = keeper;
                var keeperStops = System.Environment.TickCount + 1500;
                int escapeAttempts = 0;
                keeper.Tick += (_, _) =>
                {
                    try
                    {
                        if (TextMotionHelper.FindFocusedTextBox() != null && escapeAttempts < 4)
                        {
                            // Focus has NOT left the search box yet — Escape #2 is what actually moves
                            // focus to the tree; the counter bounds the loop.
                            escapeAttempts++;
                            KeyInjection.Press(KeyInjection.VK_ESCAPE);
                            return;
                        }
                        target?.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
                        ExecuteCommand("View.SolutionExplorer");
                    }
                    catch
                    {
                        // selection/focus re-assert must never break the handler
                    }
                    if (System.Environment.TickCount >= keeperStops)
                    {
                        keeperRef.Stop();
                    }
                };
                keeper.Start();
            }
            catch (Exception ex)
            {
                // Debug aid ONLY — OUTSIDE the M-M7 diagnostic contract (never asserted by the harness;
                // M-M7 covers only the [NeoVisual]/[Telescope] LOG lines emitted via NeoVisualLog/Log).
                // Mirrors the established SelectFirstSourceFile catch.
                Telescope.NeoVisualLog.Debug($"{Telescope.DiagnosticLog.NeoVisual}focus-tree failed: {ex.Message}");
            }
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
            if (TextMotionHelper.FindFocusedTextBox() != null)
            {
                return TextMotionHelper.TryMoveFocusedSurface(key, ref _isInputMode);
            }

            return _actions.TryGetValue(key, out var action) && action();
        }

        private void OpenSelected()
        {
            // Enter is the native "open selected item" key in the Solution Explorer tree.
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer open");
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
            try
            {
                var dte = _dteFactory();
                if (dte == null)
                {
                    Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer select none");
                    return;
                }

                var dte2 = dte as EnvDTE80.DTE2;
                if (dte2 == null)
                {
                    Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer select none");
                    return;
                }

                var (item, first) = ResolveTreeItem(dte2, HierarchyResolver.FirstSourceFilePath);
                if (first == null)
                {
                    Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer select none");
                    return;
                }

                // Programmatic select (no key injection). item is non-null whenever first != null —
                // the path came from the forest that populated pathToItem inside ResolveTreeItem.
                item!.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);

                // Open the file directly so the harness's `editor-view-opened` line pins the SAME
                // path as the `select file=` diagnostic (the injected-Enter chain in the harness
                // would otherwise race VS's hover-preview, which opens a different tree item).
                // Activate when a document for this file already exists, else ItemOperations.OpenFile.
                // NOTE: activating an ALREADY-CREATED text view does NOT raise TextViewCreated, so
                // editor-view-opened cannot be left to that side effect here — we emit it directly
                // for the file we open (below), which is truthful and order-deterministic.
                EnvDTE.Document? doc = null;
                try { doc = dte.Documents.Item(first); } catch { doc = null; }
                if (doc != null) { doc.Activate(); }
                else { dte.ItemOperations.OpenFile(first); }

                // Keep the Solution Explorer tree focused + the selection pinned for ~1.5s: VS's
                // hover-preview (armed by the tree expansion) opens the tree's current item in the
                // editor and steals focus — re-selecting + re-focusing defeats it so the harness's
                // `o` still reaches the controller. (No per-tick document open: that would spam
                // editor-view-opened; we emitted exactly one above.)
                var keeper = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal);
                keeper.Interval = System.TimeSpan.FromMilliseconds(100);
                System.Windows.Threading.DispatcherTimer keeperRef = keeper;
                var keeperStops = System.Environment.TickCount + 1500;
                EnvDTE.UIHierarchyItem keepItem = item!;
                keeper.Tick += (_, _) =>
                {
                    try
                    {
                        keepItem.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
                        ExecuteCommand("View.SolutionExplorer");
                    }
                    catch
                    {
                        // selection/focus re-assert must never break the handler
                    }
                    if (System.Environment.TickCount >= keeperStops)
                    {
                        keeperRef.Stop();
                    }
                };
                keeper.Start();

                // Emit editor-view-opened for the file we just opened/activated — this is the SAME
                // diagnostic/format VimModeTracker.TextViewCreated emits, but it is produced here
                // deterministically (activating an already-created view raises no TextViewCreated).
                // Mirrors solution-explorer select/open; `editor-view-opened file=` is verified by
                // `explorer-open-navigation` / `explorer-open-searchbox`.
                Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}editor-view-opened file={first}");
                Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer select file={first}");
            }
            catch (Exception ex)
            {
                Telescope.NeoVisualLog.Debug($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer select failed: {ex.Message}");
            }
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
            var forest = new System.Collections.Generic.List<HierarchyNode>();
            var pathToItem = new System.Collections.Generic.Dictionary<string, EnvDTE.UIHierarchyItem>(StringComparer.OrdinalIgnoreCase);
            BuildForest(projectNode, forest, pathToItem);
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
        /// Recurses a project node's tree into a pure <see cref="HierarchyNode"/> forest plus a
        /// full-path → <see cref="EnvDTE.UIHierarchyItem"/> map. The caller must expand the node's
        /// <c>UIHierarchyItems</c> first (a collapsed node's children are not enumerated). Physical
        /// folders recurse; physical files are added ONLY when their name ends with <c>.cs</c>
        /// (non-.cs files such as .csproj/.json are never added, so the seam cannot return them);
        /// everything else (virtual folders, references, sub-projects) is skipped.
        /// </summary>
        private static void BuildForest(
            EnvDTE.UIHierarchyItem item,
            System.Collections.Generic.List<HierarchyNode> forest,
            System.Collections.Generic.Dictionary<string, EnvDTE.UIHierarchyItem> pathToItem)
        {
            foreach (EnvDTE.UIHierarchyItem child in item.UIHierarchyItems)
            {
                if (child.Object is EnvDTE.ProjectItem pi)
                {
                    string kind = pi.Kind;
                    if (kind == HierarchyResolver.PhysicalFolderKind)
                    {
                        var children = new System.Collections.Generic.List<HierarchyNode>();
                        BuildForest(child, children, pathToItem);
                        forest.Add(new HierarchyNode(HierarchyResolver.PhysicalFolderKind, pi.Name, "", children));
                    }
                    else if (kind == HierarchyResolver.PhysicalFileKind &&
                             pi.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        // FileNames is an indexed property; index FileCount (NOT index 1, which is
                        // the short name) to get the item's FULL path.
                        string fullPath = pi.FileNames[(short)pi.FileCount];
                        forest.Add(new HierarchyNode(HierarchyResolver.PhysicalFileKind, pi.Name, fullPath, null));
                        pathToItem[fullPath] = child;
                    }
                }
            }
        }

        /// <summary>
        /// Focuses the Solution Explorer search box via the native <c>Window.SolutionExplorerSearch</c>
        /// command (Ctrl+;), then enters input mode so the user can type the query. The command is
        /// executed through DTE because the search box is part of the tool window chrome, not the
        /// tree; we cannot tab/focus into it directly.
        /// </summary>
        private void FocusSearchBox()
        {
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer search-focus");
            ExecuteCommand("Window.SolutionExplorerSearch");
            EnterInputMode();
            // TryMove(Keys.I) returns true, so InputHandler's generic tool-window branch never
            // logs its enter-input line — emit the same contract here so the E2E harness's
            // "i entered tool-window input mode" assertion stays stable.
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}toolwindow-enter-input");
        }

        private void RenameSelected()
        {
            // F2 is the native rename shortcut in the Solution Explorer tree.
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer rename");
            KeyInjection.Press(KeyInjection.VK_F2);
        }

        private void MoveSelected()
        {
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer move");
            ExecuteCommand("SolutionExplorer.Move");
        }

        private void AddItem()
        {
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer add");
            ExecuteCommand("SolutionExplorer.AddItem");
        }

        private void ExecuteCommand(string command)
        {
            try
            {
                _dteFactory()?.ExecuteCommand(command, string.Empty);
            }
            catch (Exception ex)
            {
                Telescope.NeoVisualLog.Debug($"{Telescope.DiagnosticLog.NeoVisual}Command '{command}' failed: {ex.Message}");
            }
        }
    }
}