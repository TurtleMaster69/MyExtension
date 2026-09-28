using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using CardinalNavigation;
using Microsoft.VisualStudio.Shell.Interop;
using MyExtension;
using TestHarness;

namespace NeoVisual.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            MyExtension.KeyInjection.SimulateOnly = true;
            return TestHarness.TestRunner.Run(typeof(Tests), args);
        }
    }

    internal static class Tests
    {
        // ================================================================
        // KeybindingConfig — leader key + binding parsing
        // ================================================================

        public static void Run_Keybinding_DefaultLeaderIsSpace()
        {
            var cfg = KeybindingConfig.LoadFromJson("{\"bindings\":{}}");
            Assert.Equal(Keys.Space, cfg.LeaderKey);
        }

        public static void Run_Keybinding_CustomLeaderParsed()
        {
            // "ControlKey" is a valid Keys enum member (a real physical key), so it must be honored.
            var cfg = KeybindingConfig.LoadFromJson("{\"leader\":\"ControlKey\",\"bindings\":{}}");
            Assert.Equal(Keys.ControlKey, cfg.LeaderKey);
        }

        public static void Run_Keybinding_InvalidLeaderFallsBackToSpace()
        {
            var cfg = KeybindingConfig.LoadFromJson("{\"leader\":\"NotAKey\",\"bindings\":{}}");
            Assert.Equal(Keys.Space, cfg.LeaderKey);
        }

        public static void Run_Keybinding_BindingsParsed()
        {
            var cfg = KeybindingConfig.LoadFromJson("{\"bindings\":{\"W\":\"command:File.SaveSelectedItems\",\"Ctrl+H\":\"navigate-left\"}}");
            Assert.Equal(2, cfg.Bindings.Count);
            Assert.Equal("command:File.SaveSelectedItems", cfg.Bindings["W"]);
            Assert.Equal("navigate-left", cfg.Bindings["Ctrl+H"]);
        }

        public static void Run_Keybinding_CaseInsensitive()
        {
            var cfg = KeybindingConfig.LoadFromJson("{\"bindings\":{\"W\":\"command:File.Save\"}}");
            Assert.True(cfg.Bindings.ContainsKey("w"), "binding lookup is case-insensitive");
        }

        public static void Run_Keybinding_NullUnbinds()
        {
            // A null binding value must remove the key from the map (falls through to editor).
            var cfg = KeybindingConfig.LoadFromJson("{\"bindings\":{\"Q\":null,\"W\":\"command:X\"}}");
            Assert.True(cfg.Bindings.ContainsKey("W"), "non-null binding preserved");
            Assert.False(cfg.Bindings.ContainsKey("Q"), "null binding removed");
        }

        public static void Run_Keybinding_EmptyJsonOk()
        {
            var cfg = KeybindingConfig.LoadFromJson("{}");
            Assert.Equal(0, cfg.Bindings.Count);
        }

        public static void Run_Keybinding_DefaultFileHasTelescopeAndNav()
        {
            // Sanity check the shipped defaults: the leader binding for Telescope and the four
            // cardinal nav shortcuts must survive a full Load() (embedded resource present).
            var cfg = KeybindingConfig.Load();
            Assert.True(cfg.Bindings.ContainsKey("F,T"), "F,T -> telescope binding present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+H"), "Ctrl+H -> navigate-left present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+J"), "Ctrl+J -> navigate-down present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+K"), "Ctrl+K -> navigate-up present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+L"), "Ctrl+L -> navigate-right present");
            Assert.Equal(Keys.Space, cfg.LeaderKey);
        }

        // ================================================================
        // ToolWindowTypeResolver — GUID -> tool-window kind
        // ================================================================

        public static void Run_ToolWindowType_KnownGuids()
        {
            Assert.Equal(ToolWindowType.SolutionExplorer, ToolWindowTypeResolver.FromGuid(new Guid(ToolWindowGuids80.SolutionExplorer)));
            Assert.Equal(ToolWindowType.OutputWindow, ToolWindowTypeResolver.FromGuid(new Guid(ToolWindowGuids80.Outputwindow)));
            Assert.Equal(ToolWindowType.StartPage, ToolWindowTypeResolver.FromGuid(new Guid(ToolWindowGuids80.StartPage)));
            Assert.Equal(ToolWindowType.PropertyBrowser, ToolWindowTypeResolver.FromGuid(new Guid(ToolWindowGuids80.PropertiesWindow)));
            Assert.Equal(ToolWindowType.Toolbox, ToolWindowTypeResolver.FromGuid(new Guid(ToolWindowGuids80.Toolbox)));
        }

        public static void Run_ToolWindowType_UnknownGuid()
        {
            Assert.Equal(ToolWindowType.Unknown, ToolWindowTypeResolver.FromGuid(Guid.NewGuid()));
        }

        // ================================================================
        // GeneralToolWindowController — text-input classification (default mode)
        // ================================================================

        public static void Run_ToolWindowMode_TextInputTypesStartInInsert()
        {
            Assert.True(GeneralToolWindowController.IsTextInputType(ToolWindowType.CommandWindow), "CommandWindow is text input");
            Assert.True(GeneralToolWindowController.IsTextInputType(ToolWindowType.ImmediateWindow), "ImmediateWindow is text input");
            Assert.True(GeneralToolWindowController.IsTextInputType(ToolWindowType.FindReplace), "FindReplace is text input");
            Assert.True(GeneralToolWindowController.IsTextInputType(ToolWindowType.WebBrowserWindow), "WebBrowserWindow is text input");
        }

        public static void Run_ToolWindowMode_NavigationTypesStartInNormal()
        {
            Assert.False(GeneralToolWindowController.IsTextInputType(ToolWindowType.SolutionExplorer), "SolutionExplorer is navigation");
            Assert.False(GeneralToolWindowController.IsTextInputType(ToolWindowType.OutputWindow), "OutputWindow is navigation");
            Assert.False(GeneralToolWindowController.IsTextInputType(ToolWindowType.Toolbox), "Toolbox is navigation");
        }

        public static void Run_ToolWindowMode_HjklMoves()
        {
            // The default controller maps j/k/h/l to Down/Up/Left/Right via injected arrows.
            var controller = new GeneralToolWindowController(ToolWindowType.SolutionExplorer);
            Assert.True(controller.TryMove(Keys.J), "j moves down");
            Assert.True(controller.TryMove(Keys.K), "k moves up");
            Assert.True(controller.TryMove(Keys.H), "h moves left");
            Assert.True(controller.TryMove(Keys.L), "l moves right");
            Assert.False(controller.TryMove(Keys.X), "unmapped key is not consumed");
        }

        public static void Run_SolutionExplorer_ActionKeys()
        {
            var controller = new SolutionExplorerController(() => null!);
            // o/Enter/r/m/a/g are the non-hjkl action keys.
            var keys = new List<Keys>(controller.ActionKeys);
            Assert.True(keys.Contains(Keys.O), "o is an action key");
            Assert.True(keys.Contains(Keys.Enter), "Enter is an action key");
            Assert.True(keys.Contains(Keys.R), "r is an action key");
            Assert.True(keys.Contains(Keys.M), "m is an action key");
            Assert.True(keys.Contains(Keys.A), "a is an action key");
            Assert.True(keys.Contains(Keys.G), "g is an action key");
            // All are consumed by TryMove (logged actions).
            Assert.True(controller.TryMove(Keys.O), "o opens");
            Assert.True(controller.TryMove(Keys.R), "r renames");
            Assert.True(controller.TryMove(Keys.M), "m moves");
            Assert.True(controller.TryMove(Keys.A), "a adds");
            // hjkl still navigate.
            Assert.True(controller.TryMove(Keys.J), "j navigates down");
            // Unknown key not consumed.
            Assert.False(controller.TryMove(Keys.X), "unmapped key is not consumed");
        }

        public static void Run_GeneralController_NoActionKeys()
        {
            var controller = new GeneralToolWindowController(ToolWindowType.Toolbox);
            Assert.Equal(0, controller.ActionKeys.Count);
        }

        // ================================================================
        // HierarchyResolver — select-first-source-file resolution (pure seam)
        // ================================================================

        public static void Run_HierarchyResolver_FirstSourceFile()
        {
            // Pure seam: resolve the first physical SOURCE file under a project's child nodes.
            // Classification is by Kind GUID: physical file (returned) vs physical folder (recursed);
            // any other kind (project/solution/virtual-folder/references) is skipped, not recursed.

            // (1) file-vs-folder classification — a physical FILE returns its own path.
            var fileNode = new HierarchyNode(
                HierarchyResolver.PhysicalFileKind, "Beta.cs", @"C:\p\Beta.cs", null);
            Assert.Equal(@"C:\p\Beta.cs",
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { fileNode }));

            // (2) folder recursion — a physical FOLDER recurses to its first physical file (in order).
            var folder = new HierarchyNode(
                HierarchyResolver.PhysicalFolderKind, "Models", "",
                new HierarchyNode[]
                {
                    new HierarchyNode(HierarchyResolver.PhysicalFileKind, "User.cs", @"C:\p\Models\User.cs", null),
                    new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Order.cs", @"C:\p\Models\Order.cs", null),
                });
            Assert.Equal(@"C:\p\Models\User.cs",
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { folder }));

            // (3) non-file/non-folder nodes are skipped (not recursed), first real file still found.
            var unknown = new HierarchyNode("{00000000-0000-0000-0000-000000000000}", "Dependencies", "", null);
            var realFile = new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Alpha.cs", @"C:\p\Alpha.cs", null);
            Assert.Equal(@"C:\p\Alpha.cs",
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { unknown, realFile }));

            // (4) empty -> null (no reachable source file).
            Assert.True(
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { }) == null,
                "empty nodes resolve to null");
        }

        public static void Run_HierarchyResolver_FirstMatch_FindsByName()
        {
            // Query "GrepProbe" finds GrepProbe.cs (name with extension contains the query).
            var file = new HierarchyNode(
                HierarchyResolver.PhysicalFileKind, "GrepProbe.cs", @"C:\p\GrepProbe.cs", null);
            Assert.Equal(@"C:\p\GrepProbe.cs",
                HierarchyResolver.FirstPathMatching(new HierarchyNode[] { file }, "GrepProbe"));
        }

        public static void Run_HierarchyResolver_FirstMatch_CaseInsensitive()
        {
            var file = new HierarchyNode(
                HierarchyResolver.PhysicalFileKind, "GrepProbe.cs", @"C:\p\GrepProbe.cs", null);
            Assert.Equal(@"C:\p\GrepProbe.cs",
                HierarchyResolver.FirstPathMatching(new HierarchyNode[] { file }, "grepprobe"));
        }

        public static void Run_HierarchyResolver_FirstMatch_RecursesFolders()
        {
            // A folder-wrapped hit recurses in tree order; folder nodes have no file path.
            var folder = new HierarchyNode(
                HierarchyResolver.PhysicalFolderKind, "Services", "",
                new HierarchyNode[]
                {
                    new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Other.cs", @"C:\p\Services\Other.cs", null),
                    new HierarchyNode(HierarchyResolver.PhysicalFileKind, "GrepProbe.cs", @"C:\p\Services\GrepProbe.cs", null),
                });
            Assert.Equal(@"C:\p\Services\GrepProbe.cs",
                HierarchyResolver.FirstPathMatching(new HierarchyNode[] { folder }, "GrepProbe"));
        }

        public static void Run_HierarchyResolver_FirstMatch_NoMatchReturnsNull()
        {
            var file = new HierarchyNode(
                HierarchyResolver.PhysicalFileKind, "Alpha.cs", @"C:\p\Alpha.cs", null);
            Assert.True(
                HierarchyResolver.FirstPathMatching(new HierarchyNode[] { file }, "GrepProbe") == null,
                "a non-matching query resolves to null");
        }

        public static void Run_HierarchyResolver_FirstMatch_EmptyQueryReturnsNull()
        {
            var file = new HierarchyNode(
                HierarchyResolver.PhysicalFileKind, "GrepProbe.cs", @"C:\p\GrepProbe.cs", null);
            Assert.True(
                HierarchyResolver.FirstPathMatching(new HierarchyNode[] { file }, "") == null,
                "an empty query resolves to null");
        }

        // ================================================================
        // TextInputToolWindowController — vim text motions in text-input windows
        // ================================================================

        public static void Run_TextInput_StartsInInsertMode()
        {
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            Assert.True(controller.IsInputMode, "text-input windows start in insert mode");
            Assert.False(controller.ActionKeys.Count == 0, "text-input controller exposes action keys");
        }

        public static void Run_TextInput_MapMotions()
        {
            Assert.Equal(TextMotion.Left, TextMotionHelper.MapMotion(Keys.H, false));
            Assert.Equal(TextMotion.Right, TextMotionHelper.MapMotion(Keys.L, false));
            Assert.Equal(TextMotion.NextWord, TextMotionHelper.MapMotion(Keys.W, false));
            Assert.Equal(TextMotion.PrevWord, TextMotionHelper.MapMotion(Keys.B, false));
            Assert.Equal(TextMotion.EndWord, TextMotionHelper.MapMotion(Keys.E, false));
        }

        public static void Run_TextInput_MapInsertMotions()
        {
            // A (Shift+a) = insert at end; a = insert after caret; I (Shift+i) = insert at start.
            Assert.Equal(TextMotion.InsertEnd, TextMotionHelper.MapMotion(Keys.A, true));
            Assert.Equal(TextMotion.InsertAfter, TextMotionHelper.MapMotion(Keys.A, false));
            Assert.Equal(TextMotion.InsertStart, TextMotionHelper.MapMotion(Keys.I, true));
            // A bare i (no shift) is the generic insert handled by InputHandler, not a motion.
            Assert.Equal(null, TextMotionHelper.MapMotion(Keys.I, false));
            Assert.Equal(null, TextMotionHelper.MapMotion(Keys.X, false));
        }

        // ================================================================
        // TextMotionEngine — TextMotionHelper.MapMotion (BP-1/T1)
        // RED: `TextMotionHelper.MapMotion` does not exist yet -> compile error
        // ================================================================

        public static void Run_TextMotionEngine_MapMotion_LeftRight()
        {
            Assert.Equal(TextMotion.Left, TextMotionHelper.MapMotion(Keys.H, false));
            Assert.Equal(TextMotion.Right, TextMotionHelper.MapMotion(Keys.L, false));
        }

        public static void Run_TextMotionEngine_MapMotion_Words()
        {
            Assert.Equal(TextMotion.NextWord, TextMotionHelper.MapMotion(Keys.W, false));
            Assert.Equal(TextMotion.PrevWord, TextMotionHelper.MapMotion(Keys.B, false));
            Assert.Equal(TextMotion.EndWord, TextMotionHelper.MapMotion(Keys.E, false));
        }

        public static void Run_TextMotionEngine_MapMotion_InsertShift()
        {
            // A (Shift+a) = insert at end; a = insert after caret; I (Shift+i) = insert at start.
            Assert.Equal(TextMotion.InsertEnd, TextMotionHelper.MapMotion(Keys.A, true));
            Assert.Equal(TextMotion.InsertAfter, TextMotionHelper.MapMotion(Keys.A, false));
            Assert.Equal(TextMotion.InsertStart, TextMotionHelper.MapMotion(Keys.I, true));
            // A bare i (no shift) is the generic insert handled by InputHandler, not a motion.
            Assert.Equal(null, TextMotionHelper.MapMotion(Keys.I, false));
        }

        public static void Run_TextMotionEngine_MapMotion_UnknownNull()
        {
            Assert.Equal(null, TextMotionHelper.MapMotion(Keys.X, false));
        }

        public static void Run_TextInput_ActionKeys()
        {
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            var keys = new List<Keys>(controller.ActionKeys);
            Assert.True(keys.Contains(Keys.W), "w is an action key");
            Assert.True(keys.Contains(Keys.B), "b is an action key");
            Assert.True(keys.Contains(Keys.E), "e is an action key");
            Assert.True(keys.Contains(Keys.A), "a/A is an action key");
        }

        // ================================================================
        // ActionTable — ActionKeys == _actions.Keys incl. hjkl (BP-2/T2)
        // RED: hjkl are not in ActionKeys today (SolutionExplorer =
        // O/Enter/R/M/A/W/B/E/G, TextInput = W/B/E/A) -> assertion failure
        // ================================================================

        public static void Run_ActionTable_SolutionExplorer_HjklInActionKeys()
        {
            var controller = new SolutionExplorerController(() => null!);
            var keys = new List<Keys>(controller.ActionKeys);
            Assert.True(keys.Contains(Keys.H), "h is an action key");
            Assert.True(keys.Contains(Keys.J), "j is an action key");
            Assert.True(keys.Contains(Keys.K), "k is an action key");
            Assert.True(keys.Contains(Keys.L), "l is an action key");
        }

        public static void Run_ActionTable_TextInput_HjklInActionKeys()
        {
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            var keys = new List<Keys>(controller.ActionKeys);
            Assert.True(keys.Contains(Keys.H), "h is an action key");
            Assert.True(keys.Contains(Keys.L), "l is an action key");
        }

        public static void Run_ActionTable_SolutionExplorer_ActionKeysMatchTable()
        {
            var controller = new SolutionExplorerController(() => null!);
            var expected = new[]
            {
                Keys.O, Keys.Enter, Keys.R, Keys.M, Keys.A, Keys.G,
                Keys.W, Keys.B, Keys.E, Keys.H, Keys.J, Keys.K, Keys.L, Keys.I,
            };
            var actual = new List<Keys>(controller.ActionKeys);
            Assert.Equal(expected.Length, actual.Count);
            foreach (var key in expected)
            {
                Assert.True(actual.Contains(key), $"ActionKeys contains {key}");
            }
        }

        public static void Run_ActionTable_TextInput_ActionKeysMatchTable()
        {
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            var expected = new[] { Keys.W, Keys.B, Keys.E, Keys.A, Keys.H, Keys.L };
            var actual = new List<Keys>(controller.ActionKeys);
            Assert.Equal(expected.Length, actual.Count);
            foreach (var key in expected)
            {
                Assert.True(actual.Contains(key), $"ActionKeys contains {key}");
            }
        }

        public static void Run_ActionTable_UnmappedKeyNotConsumed()
        {
            // SolutionExplorer's TryMove is hermetic. TextInput's TryMove JITs an IWpfTextView
            // reference (Microsoft.VisualStudio.Text.UI) that the test project does not reference,
            // so only the SolutionExplorer surface is exercised here (documented deviation).
            var se = new SolutionExplorerController(() => null!);
            Assert.False(se.TryMove(Keys.X), "SolutionExplorer does not consume X");
        }

        public static void Run_ActionTable_SolutionExplorer_ConsumesMappedKeys()
        {
            // O/R/M/A are hermetic. G (SelectFirstSourceFile) calls
            // ThreadHelper.ThrowIfNotOnUIThread() and cannot run on the MTA test host, so it is
            // excluded here (VS-coupled; documented deviation).
            var controller = new SolutionExplorerController(() => null!);
            Assert.True(controller.TryMove(Keys.O), "o opens");
            Assert.True(controller.TryMove(Keys.R), "r renames");
            Assert.True(controller.TryMove(Keys.M), "m moves");
            Assert.True(controller.TryMove(Keys.A), "a adds");
        }

        // ================================================================
        // InjectedKeyGuard — per-VK pending counter (Enter-storm fix, F1)
        // ================================================================

        public static void Run_InjectedKeyGuard_ConsumeOnce()
        {
            var guard = new InjectedKeyGuard();
            guard.Record(13);
            Assert.True(guard.TryConsume(13), "first consume of a recorded VK succeeds");
            Assert.False(guard.TryConsume(13), "second consume of the same VK fails (consume-once)");
        }

        public static void Run_InjectedKeyGuard_WrongVkNotConsumed()
        {
            var guard = new InjectedKeyGuard();
            guard.Record(13);
            Assert.False(guard.TryConsume(0x28), "a different VK does not consume the pending record");
            Assert.True(guard.TryConsume(13), "the recorded VK is still pending and consumable");
        }

        public static void Run_InjectedKeyGuard_NoRecord()
        {
            var guard = new InjectedKeyGuard();
            Assert.False(guard.TryConsume(13), "no pending record -> nothing to consume");
        }

        public static void Run_InjectedKeyGuard_MultipleRecords()
        {
            var guard = new InjectedKeyGuard();
            guard.Record(13);
            guard.Record(13);
            Assert.True(guard.TryConsume(13), "first consume succeeds");
            Assert.True(guard.TryConsume(13), "second consume succeeds (two records)");
            Assert.False(guard.TryConsume(13), "third consume fails (per-Press counting exhausted)");
        }

        // ================================================================
        // KeyInjection — SimulateOnly (interrupting bugfix, BP-1)
        // RED: `SimulateOnly` does not exist yet -> compile error
        // ================================================================

        public static void Run_KeyInjection_SimulateOnly()
        {
            // Simulate mode: Press must record the VK in the guard but skip the real
            // keybd_event (no OS-level injection into whatever window has focus).
            // Drain any VK_DOWN records left by earlier controller tests so this
            // assertion is about THIS Press call, not a stale record.
            while (InjectedKeyGuard.Instance.TryConsume(KeyInjection.VK_DOWN)) { }

            KeyInjection.SimulateOnly = true;
            KeyInjection.Press(KeyInjection.VK_DOWN);

            Assert.True(InjectedKeyGuard.Instance.TryConsume(KeyInjection.VK_DOWN),
                "Press recorded VK_DOWN in the guard even in simulate mode");
            Assert.False(InjectedKeyGuard.Instance.TryConsume(KeyInjection.VK_DOWN),
                "the recorded VK is consumed once (no double record)");
        }

        // ================================================================
        // FocusGuard — tool-window routing decision (pure seam)
        // ================================================================

        public static void Run_FocusGuard_EditorFocusedBlocksRouting()
        {
            // The leak: with stale tool-window state but an editor focused, routing must be off.
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: false),
                "editor-focused tool window must not route keys");
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: 5, editorFocused: true, isTextInputSurface: false),
                "editor-focused action keys must not be interesting");
        }

        public static void Run_FocusGuard_TreeFocusedAllowsRouting()
        {
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: false, isInputMode: false, isTextInputSurface: false),
                "tree-focused tool window routes keys");
            Assert.True(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: 5, editorFocused: false, isTextInputSurface: false),
                "tree-focused action keys are interesting");
        }

        public static void Run_FocusGuard_InputModeBlocksActionKeys()
        {
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: true, actionKeyCount: 5, editorFocused: false, isTextInputSurface: false),
                "input-mode tool window has no action-key pre-filter");
        }

        public static void Run_FocusGuard_ZeroActionKeysBlocks()
        {
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: 0, editorFocused: false, isTextInputSurface: false),
                "zero action keys is never interesting");
        }

        public static void Run_FocusGuard_NonToolWindowBlocks()
        {
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: false, isInputMode: false, actionKeyCount: 5, editorFocused: false, isTextInputSurface: false),
                "non-tool-window has no action-key pre-filter");
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: false, editorFocused: false, isInputMode: false, isTextInputSurface: false),
                "non-tool-window routes nothing");
        }

        // ================================================================
        // FocusGuard truth table — veto owned by the guard (BP-4/T5)
        // RED: the new 4-arg/5-arg signatures don't exist yet -> compile error
        // ================================================================

        public static void Run_FocusGuard_TruthTable_TextInputSurfaceOwnsKeyboard()
        {
            // A text-input surface (Command Window) owns the keyboard even when the editor flag
            // is stale — the exception that keeps text-input routing alive.
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: true),
                "a text-input surface routes keys despite the stale editor flag");
        }

        public static void Run_FocusGuard_TruthTable_InputModeOwnsKeyboard()
        {
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: true, isTextInputSurface: false),
                "input-mode tool window routes keys despite the stale editor flag");
        }

        public static void Run_FocusGuard_TruthTable_EditorVetoesNavigation()
        {
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: false),
                "editor-focused navigation tool window must not route keys");
        }

        public static void Run_FocusGuard_TruthTable_NonToolWindowNeverRoutes()
        {
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: false, editorFocused: false, isInputMode: false, isTextInputSurface: false),
                "non-tool-window never routes");
        }

        public static void Run_FocusGuard_TruthTable_ActionKeysTextInputSurface()
        {
            Assert.True(
                FocusGuard.HasToolWindowActionKeys(isToolWindow: true, isInputMode: false, actionKeyCount: 5, editorFocused: true, isTextInputSurface: true),
                "text-input-surface action keys are interesting despite the stale editor flag");
        }

        public static void Run_FocusGuard_TruthTable_ActionKeysEditorVeto()
        {
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(isToolWindow: true, isInputMode: false, actionKeyCount: 5, editorFocused: true, isTextInputSurface: false),
                "editor-focused action keys are not interesting");
        }

        public static void Run_FocusGuard_IsTypingTruthTable()
        {
            // Editor insert/replace -> typing (leader key must type a space).
            Assert.True(
                FocusGuard.IsTyping(isToolWindow: false, isInputMode: false, editorFocused: true, editorInTypingMode: true),
                "editor insert/replace is typing");
            // Editor normal -> not typing.
            Assert.False(
                FocusGuard.IsTyping(isToolWindow: false, isInputMode: false, editorFocused: true, editorInTypingMode: false),
                "editor normal is not typing");
        }

        public static void Run_FocusGuard_IsTypingToolWindowInput()
        {
            // Tool window in input mode -> typing regardless of editor state.
            Assert.True(
                FocusGuard.IsTyping(isToolWindow: true, isInputMode: true, editorFocused: false, editorInTypingMode: false),
                "tool-window input mode is typing");
            // Tool window in normal mode, editor not focused -> not typing.
            Assert.False(
                FocusGuard.IsTyping(isToolWindow: true, isInputMode: false, editorFocused: false, editorInTypingMode: false),
                "tool-window normal mode is not typing");
        }

        // ================================================================
        // Actions — ActionRegistry (ResolveAction via a registry, BP-4/C3)
        // RED: `Actions` / `TelescopeLauncher` do not exist yet -> compile error
        // ================================================================

        public static void Run_ActionsRegistry_ContainsAllBuiltins()
        {
            // The registry must hold exactly the 10 built-in action names, kept in sync with
            // default-keybindings.json (the hand-sync bug this seam removes).
            Assert.Equal(10, Actions.Registry.Count);
            var names = new[]
            {
                "navigate-left", "navigate-right", "navigate-up", "navigate-down",
                "telescope", "telescope-issues", "telescope-references",
                "telescope-implementation", "telescope-grep", "toggle-solution-explorer",
            };
            foreach (string name in names)
            {
                Assert.True(Actions.Registry.ContainsKey(name), $"registry contains '{name}'");
            }
        }

        public static void Run_ActionsRegistry_CaseInsensitive()
        {
            // The registry is OrdinalIgnoreCase, so an uppercased built-in name still resolves.
            Assert.True(Actions.Resolve("NAVIGATE-LEFT", null!, null!) != null,
                "case-insensitive resolve of a built-in name");
        }

        public static void Run_ActionsRegistry_UnknownFallsThrough()
        {
            // command: names are NOT registry entries — they fall through to ParseCommand in
            // ResolveAction, so Resolve returns null for them.
            Assert.True(Actions.Resolve("command:File.Save", null!, null!) == null,
                "command: names fall through to ParseCommand");
            Assert.True(Actions.Resolve("bogus", null!, null!) == null,
                "unknown names resolve to null");
        }

        public static void Run_ActionsRegistry_TelescopeMapsToFinder()
        {
            // The 5 telescope action names map to the finder names the launcher opens.
            Assert.Equal("Files", TelescopeLauncher.FinderNames["telescope"]);
            Assert.Equal("Issues", TelescopeLauncher.FinderNames["telescope-issues"]);
            Assert.Equal("References", TelescopeLauncher.FinderNames["telescope-references"]);
            Assert.Equal("Implementation", TelescopeLauncher.FinderNames["telescope-implementation"]);
            Assert.Equal("Grep", TelescopeLauncher.FinderNames["telescope-grep"]);
        }

        // ================================================================
        // Helpers — DistinctBy, RectCoordinate
        // ================================================================

        public static void Run_DistinctBy_DeduplicatesOnKey()
        {
            var items = new[] { "a", "b", "a", "c", "b" };
            var distinct = items.DistinctBy(x => x).ToList();
            Assert.Equal(3, distinct.Count);
            Assert.Equal("a", distinct[0]);
            Assert.Equal("b", distinct[1]);
            Assert.Equal("c", distinct[2]);
        }

        public static void Run_RectCoordinate_StoresFields()
        {
            // N2 (BP-1): RectCoordinate becomes a readonly struct with uppercase readonly fields.
            // RED: `r.X` does not compile before the merge (fields are lowercase x,y,width,height).
            var r = new RectCoordinate(1, 2, 3, 4);
            Assert.Equal(1, r.X);
            Assert.Equal(2, r.Y);
            Assert.Equal(3, r.Width);
            Assert.Equal(4, r.Height);
        }

        // ================================================================
        // RectCoordinate geometry members (BP-1/N2)
        // RED: Right/Bottom/IsEmpty/Adjacency/GapTo + Axis/Direction don't exist -> compile error
        // ================================================================

        public static void Run_RectCoordinate_Right_Bottom()
        {
            var r = new RectCoordinate(1, 2, 3, 4);
            Assert.Equal(4, r.Right);   // X + Width
            Assert.Equal(6, r.Bottom);  // Y + Height
        }

        public static void Run_RectCoordinate_IsEmpty()
        {
            Assert.True(new RectCoordinate(0, 0, 0, 0).IsEmpty, "all-zero rect is empty");
            Assert.False(new RectCoordinate(1, 0, 0, 0).IsEmpty, "non-zero X means not empty");
        }

        public static void Run_RectCoordinate_Adjacency()
        {
            // 1-D span overlap on the given axis (closed-form AdjacencySize).
            Assert.Equal(5, new RectCoordinate(0, 0, 10, 10).Adjacency(new RectCoordinate(5, 0, 10, 10), Axis.X));
            Assert.Equal(0, new RectCoordinate(0, 0, 10, 10).Adjacency(new RectCoordinate(20, 0, 10, 10), Axis.X));
            Assert.Equal(5, new RectCoordinate(0, 0, 10, 10).Adjacency(new RectCoordinate(0, 5, 10, 10), Axis.Y));
        }

        public static void Run_RectCoordinate_GapTo()
        {
            var active = new RectCoordinate(100, 100, 100, 100); // Right=200, Bottom=200
            Assert.Equal(50, new RectCoordinate(100, 0, 100, 50).GapTo(active, Direction.Up));
            Assert.Equal(2, new RectCoordinate(100, 202, 100, 50).GapTo(active, Direction.Down));
            Assert.Equal(50, new RectCoordinate(0, 100, 50, 100).GapTo(active, Direction.Left));
            Assert.Equal(50, new RectCoordinate(250, 100, 50, 100).GapTo(active, Direction.Right));
        }

        // ================================================================
        // NavigationSettings — DPI divide (BP-2/N4)
        // RED: `NavigationSettings` doesn't exist -> compile error
        // ================================================================

        public static void Run_NavigationSettings_FromDpi()
        {
            // XDivide = 12 * (dpi/96) * 2; YDivide = 50 * (dpi/96) * 2 (exact integers for real DPIs).
            var s96 = NavigationSettings.FromDpi(96, 96);
            Assert.Equal(24, s96.XDivide);
            Assert.Equal(100, s96.YDivide);

            var s144 = NavigationSettings.FromDpi(144, 144);
            Assert.Equal(36, s144.XDivide);
            Assert.Equal(150, s144.YDivide);

            var s120 = NavigationSettings.FromDpi(120, 120);
            Assert.Equal(30, s120.XDivide);
            Assert.Equal(125, s120.YDivide);
        }

        // ================================================================
        // WindowNavigationEngine — pure navigation seam (BP-3/N3)
        // RED: `WindowNavigationEngine` doesn't exist -> compile error
        // Pins the CURRENT algorithm: max adjacency within the divide window
        // [minGap, minGap+divide], last-wins ties, DOWN `c.Y - a.Y > 1`.
        // ================================================================

        public static void Run_WindowNavigationEngine_Up_PicksLargestAdjacency()
        {
            var settings = NavigationSettings.FromDpi(96, 96); // XDivide=24, YDivide=100
            var active = new RectCoordinate(100, 100, 100, 100); // Right=200, Bottom=200
            var candidates = new[]
            {
                new RectCoordinate(100, 0, 100, 50),  // gap 50, adjacency 100
                new RectCoordinate(150, 0, 50, 50),   // gap 50, adjacency 50
            };
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_Down_ToleranceExcludes()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[]
            {
                new RectCoordinate(100, 101, 100, 50), // c.Y - a.Y = 1, EXCLUDED by the >1 tolerance
                new RectCoordinate(100, 102, 100, 50), // c.Y - a.Y = 2, passes
            };
            Assert.Equal(1, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Down, settings));
        }

        public static void Run_WindowNavigationEngine_Left_PicksLargestAdjacency()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[]
            {
                new RectCoordinate(0, 100, 50, 100),   // gap 50, adjacency 100
                new RectCoordinate(0, 150, 50, 50),    // gap 50, adjacency 50
            };
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Left, settings));
        }

        public static void Run_WindowNavigationEngine_Right_PicksLargestAdjacency()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[]
            {
                new RectCoordinate(250, 100, 50, 100), // gap 50, adjacency 100
                new RectCoordinate(250, 150, 50, 50),  // gap 50, adjacency 50
            };
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Right, settings));
        }

        public static void Run_WindowNavigationEngine_EmptyCandidates_ReturnsNull()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            Assert.Equal(null, WindowNavigationEngine.SelectTarget(active, new RectCoordinate[0], Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_NoCandidateInDirection_ReturnsNull()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[] { new RectCoordinate(100, 201, 100, 50) }; // below, but direction is Up
            Assert.Equal(null, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_NotAligned_Excluded()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[] { new RectCoordinate(0, 0, 50, 50) }; // above but no X-overlap with [100,200)
            Assert.Equal(null, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_AdjacencyTie_LastWins()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[]
            {
                new RectCoordinate(100, 0, 100, 50),  // gap 50, adjacency 100
                new RectCoordinate(100, 20, 100, 50), // gap 30, adjacency 100 (tie)
            };
            Assert.Equal(1, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_DivideWindow_ExcludesBeyond()
        {
            var settings = NavigationSettings.FromDpi(96, 96); // YDivide=100
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[]
            {
                new RectCoordinate(100, 0, 100, 50),   // gap 50 (min)
                new RectCoordinate(100, -200, 100, 50), // gap 250 > 50+100, beyond the divide window
            };
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_HiddenZeroRect_Excluded()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[]
            {
                new RectCoordinate(0, 0, 0, 0),        // hidden/empty, excluded
                new RectCoordinate(100, 0, 100, 50),  // the only real candidate
            };
            Assert.Equal(1, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        // ================================================================
        // LeaderSequenceMatcher — pure leader state machine (F22)
        // RED: `LeaderSequenceMatcher`/`LeaderResult`/`LeaderResultKind` don't exist -> compile error
        // ================================================================

        public static void Run_LeaderMatcher_LeaderKeyStartsSequence()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            var result = matcher.HandleKey(Keys.Space, false, false, false, false);

            Assert.Equal(LeaderResultKind.Consume, result.Kind);
            Assert.True(matcher.IsActive, "leader key starts a sequence");
        }

        public static void Run_LeaderMatcher_LeaderKeyWhileTypingPassesThrough()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            var result = matcher.HandleKey(Keys.Space, false, false, false, true);

            Assert.Equal(LeaderResultKind.PassThrough, result.Kind);
            Assert.False(matcher.IsActive, "a typing leader key does not start a sequence");
        }

        public static void Run_LeaderMatcher_SingleKeyBindingExecutes()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var result = matcher.HandleKey(Keys.F, false, false, false, false);

            Assert.Equal(LeaderResultKind.Execute, result.Kind);
            Assert.Equal<string?>("F", result.Sequence);
            Assert.Equal(1, executed);
            Assert.False(matcher.IsActive, "sequence ends after execution");
        }

        public static void Run_LeaderMatcher_MultiKeySequence()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F,F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var first = matcher.HandleKey(Keys.F, false, false, false, false);
            Assert.Equal(LeaderResultKind.Consume, first.Kind);
            Assert.True(matcher.IsActive, "a prefix keeps the sequence alive");

            var second = matcher.HandleKey(Keys.F, false, false, false, false);
            Assert.Equal(LeaderResultKind.Execute, second.Kind);
            Assert.Equal<string?>("F,F", second.Sequence);
            Assert.Equal(1, executed);
            Assert.False(matcher.IsActive, "sequence ends after execution");
        }

        public static void Run_LeaderMatcher_UnknownSequenceAborts()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var result = matcher.HandleKey(Keys.X, false, false, false, false);

            Assert.Equal(LeaderResultKind.Abort, result.Kind);
            Assert.False(matcher.IsActive, "an unknown sequence aborts and clears state");
            Assert.Equal(0, executed);
        }

        public static void Run_LeaderMatcher_ResetClearsState()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            Assert.True(matcher.IsActive, "sequence started");

            matcher.Reset();

            Assert.False(matcher.IsActive, "Reset clears the active sequence");
            // After reset, a non-leader key passes through (no stale sequence state).
            var result = matcher.HandleKey(Keys.F, false, false, false, false);
            Assert.Equal(LeaderResultKind.PassThrough, result.Kind);
            Assert.Equal(0, executed);
        }

        public static void Run_LeaderMatcher_NonLeaderKeyPassesThrough()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            var result = matcher.HandleKey(Keys.F, false, false, false, false);

            Assert.Equal(LeaderResultKind.PassThrough, result.Kind);
            Assert.False(matcher.IsActive, "inactive matcher stays inactive");
            Assert.Equal(0, executed);
        }
    }
}