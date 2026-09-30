using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CardinalNavigation;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using MyExtension;
using TestHarness;
using static TestHarness.TestScaffold;

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
        // Reads a file with FileShare.ReadWrite so it can be read while the buffered
        // LogFileWriter still holds it open (File.ReadAllText uses FileShare.Read, which
        // conflicts with the writer's existing FileAccess.Write -> sharing violation).
        private static string ReadAllTextShared(string path)
        {
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            using (var sr = new System.IO.StreamReader(fs))
            {
                return sr.ReadToEnd();
            }
        }

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
            // embedded defaults only (hermetic — never reads the user's %APPDATA% file)
            var cfg = KeybindingConfig.LoadDefaults();
            Assert.True(cfg.Bindings.ContainsKey("F,T"), "F,T -> telescope binding present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+H"), "Ctrl+H -> navigate-left present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+J"), "Ctrl+J -> navigate-down present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+K"), "Ctrl+K -> navigate-up present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+L"), "Ctrl+L -> navigate-right present");
            Assert.Equal(Keys.Space, cfg.LeaderKey);
        }

        // ================================================================
        // KeyNames — shared printable-key mapping (M21)
        // RED: `KeyNames` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_KeyNames_PrintableMappings()
        {
            // The printable-key contract shared by the leader and shortcut paths: the "/" key
            // (Keys.OemQuestion) must map to "/", "+" (Oemplus) to "+", "-" (OemMinus) to "-",
            // and a plain letter to its enum name.
            Assert.Equal("/", KeyNames.ToString(Keys.OemQuestion));
            Assert.Equal("+", KeyNames.ToString(Keys.Oemplus));
            Assert.Equal("-", KeyNames.ToString(Keys.OemMinus));
            Assert.Equal("F", KeyNames.ToString(Keys.F));
        }

        public static void Run_KeyNames_RoundTrip_LeaderSequence()
        {
            // The shipped "/" leader binding must round-trip: bind "/" -> action, drive Space then
            // the physical "/" key (Keys.OemQuestion) through the leader matcher, and the matched
            // sequence must be the printable "/" (not "OemQuestion").
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["/"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var result = matcher.HandleKey(Keys.OemQuestion, false, false, false, false);

            Assert.Equal(LeaderResultKind.Execute, result.Kind);
            Assert.Equal<string?>("/", result.Sequence);
            Assert.Equal(1, executed);
        }

        public static void Run_KeyNames_RoundTrip_SimpleShortcut()
        {
            // The "Ctrl+/" config key must survive parsing, and KeyNames.ToString(Keys.OemQuestion)
            // must produce the "/" that makes the Ctrl+/ config key match (the round-trip contract).
            var cfg = KeybindingConfig.LoadFromJson("{\"bindings\":{\"Ctrl+/\":\"navigate-left\"}}");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+/"), "the Ctrl+/ config key is preserved");
            Assert.Equal("Ctrl+/", "Ctrl+" + KeyNames.ToString(Keys.OemQuestion));
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

        public static void Run_ToolWindowMode_TextInputTypesClassified()
        {
            Assert.True(GeneralToolWindowController.IsTextInputType(ToolWindowType.CommandWindow), "CommandWindow is text input");
            Assert.True(GeneralToolWindowController.IsTextInputType(ToolWindowType.ImmediateWindow), "ImmediateWindow is text input");
            Assert.True(GeneralToolWindowController.IsTextInputType(ToolWindowType.FindReplace), "FindReplace is text input");
            Assert.True(GeneralToolWindowController.IsTextInputType(ToolWindowType.WebBrowserWindow), "WebBrowserWindow is text input");
        }

        public static void Run_ToolWindowMode_NavigationTypesClassified()
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
        // WindowManager.DefaultControllerFor — CR1 static pure factory (BP-1)
        // RED: `WindowManager.DefaultControllerFor` does not exist yet -> compile error
        // The existing controller tests construct controllers directly and never construct a
        // WindowManager, so they give false confidence and cannot catch the per-type loop
        // overwriting the SolutionExplorerController. This pins the factory contract instead.
        // ================================================================

        public static void Run_WindowManager_DefaultControllerFor()
        {
            // SolutionExplorer and Unknown have NO default controller (the specialized
            // SolutionExplorerController is registered separately by the package); CommandWindow is
            // a text-input surface; Toolbox is a plain navigation surface.
            Assert.True(WindowManager.DefaultControllerFor(ToolWindowType.SolutionExplorer) == null,
                "SolutionExplorer has no default controller (the specialized one is registered separately)");
            Assert.True(WindowManager.DefaultControllerFor(ToolWindowType.Unknown) == null,
                "Unknown has no default controller");
            Assert.True(WindowManager.DefaultControllerFor(ToolWindowType.CommandWindow) is TextInputToolWindowController,
                "CommandWindow defaults to a TextInputToolWindowController");
            Assert.True(WindowManager.DefaultControllerFor(ToolWindowType.Toolbox) is GeneralToolWindowController,
                "Toolbox defaults to a GeneralToolWindowController");
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
        // HierarchyForestBuilder — pure forest builder over HierarchyItemInfo DTOs
        // (M14: extract the DTE-coupled BuildForest recursion into a testable seam)
        // ================================================================

        public static void Run_HierarchyForestBuilder_NestedFoldersProduceNestedChildren()
        {
            // folder -> folder -> file: the builder recurses physical folders and nests the
            // file node under the inner folder node.
            var items = new[]
            {
                new HierarchyItemInfo(
                    HierarchyResolver.PhysicalFolderKind, "Models", "",
                    new[]
                    {
                        new HierarchyItemInfo(
                            HierarchyResolver.PhysicalFolderKind, "Sub", "",
                            new[]
                            {
                                new HierarchyItemInfo(
                                    HierarchyResolver.PhysicalFileKind, "User.cs", @"C:\p\Models\Sub\User.cs", null),
                            }),
                    }),
            };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var forest = HierarchyForestBuilder.Build(items, map);

            Assert.Equal(1, forest.Count);
            Assert.Equal(HierarchyResolver.PhysicalFolderKind, forest[0].Kind);
            Assert.Equal(1, forest[0].Children!.Count);
            Assert.Equal(HierarchyResolver.PhysicalFolderKind, forest[0].Children![0].Kind);
            Assert.Equal(1, forest[0].Children![0].Children!.Count);
            Assert.Equal(HierarchyResolver.PhysicalFileKind, forest[0].Children![0].Children![0].Kind);
        }

        public static void Run_HierarchyForestBuilder_CsFilterCaseInsensitive()
        {
            // The .cs filter is OrdinalIgnoreCase: an uppercase-extension Program.CS is included,
            // a non-.cs App.config is not.
            var items = new[]
            {
                new HierarchyItemInfo(HierarchyResolver.PhysicalFileKind, "Program.CS", @"C:\p\Program.CS", null),
                new HierarchyItemInfo(HierarchyResolver.PhysicalFileKind, "App.config", @"C:\p\App.config", null),
            };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var forest = HierarchyForestBuilder.Build(items, map);

            Assert.Equal(1, forest.Count);
            Assert.Equal("Program.CS", forest[0].Name);
            Assert.Equal(HierarchyResolver.PhysicalFileKind, forest[0].Kind);
        }

        public static void Run_HierarchyForestBuilder_FullPathFlowsThroughAndPathMap()
        {
            // The file's FullPath lands in HierarchyNode.FilePath AND is recorded in the passed
            // pathToItem map (path -> path identity; the DTE adapter owns the real path->item map).
            const string fullPath = @"C:\p\Alpha.cs";
            var items = new[]
            {
                new HierarchyItemInfo(HierarchyResolver.PhysicalFileKind, "Alpha.cs", fullPath, null),
            };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var forest = HierarchyForestBuilder.Build(items, map);

            Assert.Equal(1, forest.Count);
            Assert.Equal(fullPath, forest[0].FilePath);
            Assert.True(map.ContainsKey(fullPath), "pathToItem records the added .cs file");
            Assert.Equal(fullPath, map[fullPath]);
        }

        public static void Run_HierarchyForestBuilder_NonFolderNonFileKindsSkipped()
        {
            // An unknown Kind GUID is skipped: not recursed (its .cs child must not appear) and
            // not added to the forest.
            var items = new[]
            {
                new HierarchyItemInfo("{00000000-0000-0000-0000-000000000000}", "Dependencies", "",
                    new[]
                    {
                        new HierarchyItemInfo(HierarchyResolver.PhysicalFileKind, "Hidden.cs", @"C:\p\Hidden.cs", null),
                    }),
            };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var forest = HierarchyForestBuilder.Build(items, map);

            Assert.Equal(0, forest.Count);
            Assert.False(map.ContainsKey(@"C:\p\Hidden.cs"), "unknown-kind children are not recursed");
        }

        public static void Run_HierarchyForestBuilder_EmptyChildrenEmptyForest()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var forest = HierarchyForestBuilder.Build(new HierarchyItemInfo[0], map);

            Assert.True(forest != null, "Build returns a list");
            Assert.Equal(0, forest.Count);
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

        // Guard only checks > 0 — any positive action-key count behaves identically.
        private const int PositiveActionKeyCount = 5;

        public static void Run_FocusGuard_EditorFocusedBlocksRouting()
        {
            // The leak: with stale tool-window state but an editor focused, routing must be off.
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "editor-focused tool window must not route keys");
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: PositiveActionKeyCount, editorFocused: true, isTextInputSurface: false, textInputSurfaceFocused: false),
                "editor-focused action keys must not be interesting");
        }

        public static void Run_FocusGuard_TreeFocusedAllowsRouting()
        {
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: false, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "tree-focused tool window routes keys");
            Assert.True(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: PositiveActionKeyCount, editorFocused: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "tree-focused action keys are interesting");
        }

        public static void Run_FocusGuard_InputModeBlocksActionKeys()
        {
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: true, actionKeyCount: PositiveActionKeyCount, editorFocused: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "input-mode tool window has no action-key pre-filter");
        }

        public static void Run_FocusGuard_ZeroActionKeysBlocks()
        {
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: 0, editorFocused: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "zero action keys is never interesting");
        }

        public static void Run_FocusGuard_NonToolWindowBlocks()
        {
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: false, isInputMode: false, actionKeyCount: PositiveActionKeyCount, editorFocused: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "non-tool-window has no action-key pre-filter");
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: false, editorFocused: false, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
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
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: true),
                "a text-input surface routes keys despite the stale editor flag");
        }

        public static void Run_FocusGuard_TruthTable_InputModeOwnsKeyboard()
        {
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: true, isTextInputSurface: false, textInputSurfaceFocused: false),
                "input-mode tool window routes keys despite the stale editor flag");
        }

        public static void Run_FocusGuard_TruthTable_EditorVetoesNavigation()
        {
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "editor-focused navigation tool window must not route keys");
        }

        public static void Run_FocusGuard_TruthTable_NonToolWindowNeverRoutes()
        {
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: false, editorFocused: false, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "non-tool-window never routes");
        }

        public static void Run_FocusGuard_TruthTable_ActionKeysTextInputSurface()
        {
            Assert.True(
                FocusGuard.HasToolWindowActionKeys(isToolWindow: true, isInputMode: false, actionKeyCount: PositiveActionKeyCount, editorFocused: true, isTextInputSurface: true, textInputSurfaceFocused: true),
                "text-input-surface action keys are interesting despite the stale editor flag");
        }

        public static void Run_FocusGuard_TruthTable_ActionKeysEditorVeto()
        {
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(isToolWindow: true, isInputMode: false, actionKeyCount: PositiveActionKeyCount, editorFocused: true, isTextInputSurface: false, textInputSurfaceFocused: false),
                "editor-focused action keys are not interesting");
        }

        public static void Run_FocusGuard_TextInputSurfaceFocused_EditorFocusedNotFocusedSurface()
        {
            // M16: a text-input surface only owns the keyboard when it genuinely holds focus.
            // With the editor focused and the text-input surface NOT focused, routing must be off
            // (the current isTextInputSurface exemption leaks — it returns TRUE here).
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: 6, editorFocused: true, isTextInputSurface: true, textInputSurfaceFocused: false),
                "editor-focused, non-focused text-input surface must not expose action keys");
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: false),
                "editor-focused, non-focused text-input surface must not route keys");
        }

        public static void Run_FocusGuard_TextInputSurfaceFocused_GenuinelyFocusedOwnsKeyboard()
        {
            // D4 preservation: a text-input surface that genuinely holds focus owns the keyboard
            // even when the editor-focus flag is stale.
            Assert.True(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: 6, editorFocused: true, isTextInputSurface: true, textInputSurfaceFocused: true),
                "genuinely-focused text-input surface action keys are interesting");
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: true),
                "genuinely-focused text-input surface routes keys");
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
        // FocusKeeperSchedule — pure focus-keeper tick decision (M26)
        // RED: `FocusKeeperSchedule` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_FocusKeeperSchedule_TruthTable()
        {
            // The pure decision the focus-keeper timer tick makes each 100ms:
            //   - search box focused + escape attempts < 4  -> inject Escape
            //   - elapsed >= duration                        -> stop (elapsed wins)
            //   - otherwise                                  -> re-assert the selection
            Assert.Equal(FocusKeeperSchedule.Decision.InjectEscape, FocusKeeperSchedule.Decide(true, 0, 0, 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Reassert, FocusKeeperSchedule.Decide(true, 0, 4, 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Reassert, FocusKeeperSchedule.Decide(false, 0, 0, 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop, FocusKeeperSchedule.Decide(false, 1500, 0, 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop, FocusKeeperSchedule.Decide(true, 1500, 0, 1500));
        }

        // ================================================================
        // VimModeState — pure owner of typing/mode state + focus guards + resolution latch (M17)
        // RED: `VimModeState` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_VimModeState_OutOfOrderLostFocusKeepsTyping()
        {
            // M17: an out-of-order LostFocus from a non-focused view must not clear the typing
            // flag (VimModeTracker.cs:189 currently clears _cachedTyping unconditionally).
            var state = new VimModeState();
            state.SetMode(VimModeState.Insert);
            state.OnViewLostFocus(isFocusedView: false);
            Assert.True(state.IsTyping, "out-of-order LostFocus must not clear typing");
        }

        public static void Run_VimModeState_ClosedNonFocusedKeepsTyping()
        {
            // M17: a Closed event from a non-focused view must not clear the typing flag
            // (VimModeTracker.cs:209 currently clears _cachedTyping unconditionally).
            var state = new VimModeState();
            state.SetMode(VimModeState.Insert);
            state.OnViewClosed(isFocusedView: false);
            Assert.True(state.IsTyping, "non-focused Closed must not clear typing");
        }

        public static void Run_VimModeState_ClassificationTruthTable()
        {
            // Pins the vim-mode= name contract: Normal/Insert/Replace/Unknown + the typing flag.
            var state = new VimModeState();
            state.SetMode(VimModeState.Normal);
            Assert.False(state.IsTyping, "Normal is not typing");
            Assert.Equal("Normal", state.ModeName);
            state.SetMode(VimModeState.Insert);
            Assert.True(state.IsTyping, "Insert is typing");
            Assert.Equal("Insert", state.ModeName);
            state.SetMode(VimModeState.Replace);
            Assert.True(state.IsTyping, "Replace is typing");
            Assert.Equal("Replace", state.ModeName);
            state.SetMode(null);
            Assert.False(state.IsTyping, "null mode is not typing");
            Assert.Equal("Unknown", state.ModeName);
        }

        public static void Run_VimModeState_ResolutionRetriesAfterFailure()
        {
            // M17: a failed VsVim resolution must not latch (VimModeTracker.cs:488 sets
            // _resolved = true before the try) — the next call retries the resolver.
            var state = new VimModeState();
            int failures = 0;
            int resolves = 0;

            object? first = state.ResolveOnce(
                () => throw new InvalidOperationException("MEF down"),
                _ => failures++);
            Assert.True(first == null, "failed resolution returns null");
            Assert.Equal(1, failures);

            object? second = state.ResolveOnce(
                () => { resolves++; return "vim"; },
                _ => failures++);
            Assert.Equal("vim", second);
            Assert.Equal(1, resolves);
            Assert.Equal(1, failures);

            object? third = state.ResolveOnce(
                () => { resolves++; return "vim"; },
                _ => failures++);
            Assert.Equal("vim", third);
            Assert.Equal(1, resolves);
            Assert.Equal(1, failures);
        }

        // ================================================================
        // VimModeClassifier — pure vim-mode classification (M32)
        // RED: `VimModeClassifier`/`IVimModeSource` don't exist -> compile error (CS0246/CS0103)
        // ================================================================

        public static void Run_VimModeClassifier_InsertIsTyping()
        {
            Assert.True(VimModeClassifier.Classify(2).IsTyping, "Insert (2) is typing");
        }

        public static void Run_VimModeClassifier_ReplaceIsTyping()
        {
            Assert.True(VimModeClassifier.Classify(7).IsTyping, "Replace (7) is typing");
        }

        public static void Run_VimModeClassifier_NormalNotTyping()
        {
            Assert.False(VimModeClassifier.Classify(1).IsTyping, "Normal (1) is not typing");
        }

        public static void Run_VimModeClassifier_NullNotTyping()
        {
            Assert.False(VimModeClassifier.Classify(null).IsTyping, "null mode is not typing");
        }

        public static void Run_VimModeClassifier_Names()
        {
            // Pins the vim-mode= name contract: Normal/Insert/Replace/Unknown + the numeric
            // fallback for an unknown mode.
            Assert.Equal("Normal", VimModeClassifier.Classify(1).Name);
            Assert.Equal("Insert", VimModeClassifier.Classify(2).Name);
            Assert.Equal("Replace", VimModeClassifier.Classify(7).Name);
            Assert.Equal("99", VimModeClassifier.Classify(99).Name);
            Assert.Equal("Unknown", VimModeClassifier.Classify(null).Name);
        }

        // ================================================================
        // VimModeSource — IVimModeSource fake drives the tracker (M32)
        // RED: `IVimModeSource` doesn't exist -> compile error (CS0246);
        //       `new VimModeTracker(source)` has no ctor taking IVimModeSource -> CS1729
        // ================================================================

        public static void Run_VimModeSource_FakeDrivesTypingFlag()
        {
            var source = new FakeVimModeSource();
            var tracker = new VimModeTracker(source);

            source.Mode = 2;
            source.RaiseModeChanged();
            Assert.True(tracker.IsInTypingMode, "Insert (2) drives IsInTypingMode true");

            source.Mode = 1;
            source.RaiseModeChanged();
            Assert.False(tracker.IsInTypingMode, "Normal (1) drives IsInTypingMode false");
        }

        public static void Run_VimModeSource_FakeLogsVimMode()
        {
            using (var dir = new TempDir())
            {
                string logPath = System.IO.Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var source = new FakeVimModeSource();
                    var tracker = new VimModeTracker(source);

                    source.Mode = 2;
                    source.RaiseModeChanged();
                    Telescope.LogFileWriter.Flush();

                    string content = ReadAllTextShared(logPath);
                    Assert.True(content.Contains("[NeoVisual] vim-mode=Insert"),
                        "Insert mode logs vim-mode=Insert");

                    source.Mode = 1;
                    source.RaiseModeChanged();
                    Telescope.LogFileWriter.Flush();

                    content = ReadAllTextShared(logPath);
                    Assert.True(content.Contains("[NeoVisual] vim-mode=Normal"),
                        "Normal mode logs vim-mode=Normal");
                });
            }
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
            // M30: every telescope action in the Registry must resolve to a finder name in
            // TelescopeLauncher.FinderNames (the single source of truth) — set-equality of the two
            // key sets, and each telescope action maps to a non-empty finder name. A telescope
            // action with no FinderNames entry would throw KeyNotFoundException in the hook path.
            var registryTelescopeKeys = Actions.Registry.Keys
                .Where(k => k.StartsWith("telescope", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var finderNamesKeys = TelescopeLauncher.FinderNames.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.True(registryTelescopeKeys.SetEquals(finderNamesKeys),
                "telescope action keys must exactly match FinderNames keys");
            foreach (string key in registryTelescopeKeys)
            {
                string finder = TelescopeLauncher.FinderNames[key];
                Assert.True(!string.IsNullOrEmpty(finder), $"telescope action '{key}' must map to a finder name");
            }
        }

        public static void Run_ActionsRegistry_TelescopeKeysMatchFinderNames()
        {
            // M30: the telescope action names in Actions.Registry must EXACTLY match the keys of
            // TelescopeLauncher.FinderNames (the single source of truth). A 6th telescope action
            // added to one map but not the other would throw KeyNotFoundException in the hook path.
            var registryTelescopeKeys = Actions.Registry.Keys
                .Where(k => k.StartsWith("telescope", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var finderNamesKeys = TelescopeLauncher.FinderNames.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.True(registryTelescopeKeys.SetEquals(finderNamesKeys),
                "Actions.Registry telescope keys must exactly match TelescopeLauncher.FinderNames keys");
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
        // DirectionExtensions.ToChar (M40, BP-3) — the single-char token contract.
        // InputHandler logs `navigate direction={direction.ToChar()}` and the harness asserts
        // `navigate direction=L/R/D/U` — the emitted token MUST be byte-identical single-char.
        // RED: `ToChar` does not exist yet -> compile error (CS1061).
        // ================================================================

        public static void Run_Direction_ToChar()
        {
            Assert.Equal('L', Direction.Left.ToChar());
            Assert.Equal('R', Direction.Right.ToChar());
            Assert.Equal('U', Direction.Up.ToChar());
            Assert.Equal('D', Direction.Down.ToChar());
        }

        // ================================================================
        // NavigationSnapshot — single-pass rect snapshot (M2, BP-1)
        // RED: `NavigationSnapshot` doesn't exist -> compile error (CS0246)
        // ================================================================

        public static void Run_NavigationSnapshot_ActiveComesFromSnapshot()
        {
            // M2 (BP-1): the active rect is derived from the snapshot's candidate list
            // (single-pass — no separate m_activeWindow.Rect re-fetch / N+1).
            var rects = new[]
            {
                new RectCoordinate(0, 0, 100, 100),
                new RectCoordinate(200, 0, 100, 100),
            };
            var snapshot = NavigationSnapshot.Capture(rects, 1);
            Assert.Equal(snapshot.Candidates[1], snapshot.Active);
        }

        public static void Run_NavigationSnapshot_ActiveIndexOutOfRange_ReturnsNull()
        {
            // M2 (BP-1): an out-of-range active index (IndexOf returns -1, or the active
            // window is not in the list) must no-op — Capture returns null so the caller
            // returns without indexing Candidates[ActiveIndex] out of range.
            var rects = new[]
            {
                new RectCoordinate(0, 0, 100, 100),
                new RectCoordinate(200, 0, 100, 100),
            };
            Assert.Equal(null, NavigationSnapshot.Capture(rects, -1));
            Assert.Equal(null, NavigationSnapshot.Capture(rects, rects.Length));
        }

        // ================================================================
        // WindowAdapter.TryGetScreenRect — M12 null-frame + Empty fallback (BP-4)
        // RED: `WindowAdapter.TryGetScreenRect` doesn't exist -> compile error (CS0117)
        // ================================================================

        public static void Run_WindowAdapter_TryGetScreenRect_NullFrameReturnsNull()
        {
            // M12 (BP-4): a null IVsWindowFrame4 must yield null (the old code path
            // `(IVsWindowFrame4)_frame` throws InvalidCastException on a non-conforming frame).
            Assert.Equal(null, WindowAdapter.TryGetScreenRect(null));
        }

        public static void Run_WindowAdapter_TryGetScreenRect_EmptyRectExcludedBySelectTarget()
        {
            // M12 (BP-4): RefreshRect falls back to RectCoordinate.Empty (BP-4 adds the
            // constant) when the frame4 cast/rect fetch fails; SelectTarget must exclude
            // that (0,0,0,0) entry and return the real candidate — mirrors
            // Run_WindowNavigationEngine_HiddenZeroRect_Excluded above.
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new RectCoordinate(100, 100, 100, 100);
            var candidates = new[]
            {
                new RectCoordinate(0, 0, 0, 0),        // RectCoordinate.Empty fallback (BP-4 adds the constant)
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

        public static void Run_LeaderMatcher_ThrowingActionIsCaught()
        {
            // M15: a binding handler that throws must not escape the hook path. The matcher
            // returns a Failed result carrying the handler's message and clears its state.
            // RED today: `action()` at LeaderSequenceMatcher.cs:63 throws and the exception
            // escapes HandleKey (and this test). The Failed seam does not exist yet.
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F"] = () => throw new KeyNotFoundException("bad finder"),
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var result = matcher.HandleKey(Keys.F, false, false, false, false);

            Assert.Equal(LeaderResultKind.Failed, result.Kind);
            Assert.Equal<string?>("F", result.Sequence);
            Assert.True(result.ErrorMessage != null && result.ErrorMessage.Contains("bad finder"),
                "ErrorMessage carries the handler's message");
            Assert.False(matcher.IsActive, "a failed execution still ends the sequence");
        }

        // ================================================================
        // SimpleShortcutMatcher — pure simple-shortcut state machine (M31)
        // RED: `SimpleShortcutMatcher`/`SimpleShortcutResult`/`SimpleShortcutResultKind` don't
        // exist -> compile error (CS0246/CS0103)
        // ================================================================

        public static void Run_SimpleShortcutMatcher_CtrlHExecutes()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ctrl+H"] = () => executed++,
            };
            var matcher = new SimpleShortcutMatcher(bindings);

            var result = matcher.HandleKey(Keys.H, true, false, false);

            Assert.Equal(SimpleShortcutResultKind.Execute, result.Kind);
            Assert.Equal<string?>("Ctrl+H", result.Sequence);
            Assert.Equal(1, executed);
        }

        public static void Run_SimpleShortcutMatcher_NoModifierPassesThrough()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ctrl+H"] = () => executed++,
            };
            var matcher = new SimpleShortcutMatcher(bindings);

            var result = matcher.HandleKey(Keys.A, false, false, false);

            Assert.Equal(SimpleShortcutResultKind.PassThrough, result.Kind);
            Assert.Equal(0, executed);
        }

        public static void Run_SimpleShortcutMatcher_UnboundChordPassesThrough()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ctrl+J"] = () => executed++,
            };
            var matcher = new SimpleShortcutMatcher(bindings);

            var result = matcher.HandleKey(Keys.H, true, false, false);

            Assert.Equal(SimpleShortcutResultKind.PassThrough, result.Kind);
            Assert.Equal(0, executed);
        }

        public static void Run_SimpleShortcutMatcher_ModifierOrderShiftF4()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["Shift+F4"] = () => executed++,
            };
            var matcher = new SimpleShortcutMatcher(bindings);

            var result = matcher.HandleKey(Keys.F4, false, true, false);

            Assert.Equal(SimpleShortcutResultKind.Execute, result.Kind);
            Assert.Equal<string?>("Shift+F4", result.Sequence);
            Assert.Equal(1, executed);
        }

        public static void Run_SimpleShortcutMatcher_PrintableKeys()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["/"] = () => executed++,
                ["+"] = () => executed++,
                ["-"] = () => executed++,
            };
            var matcher = new SimpleShortcutMatcher(bindings);

            var slash = matcher.HandleKey(Keys.OemQuestion, false, false, false);
            Assert.Equal(SimpleShortcutResultKind.Execute, slash.Kind);
            Assert.Equal<string?>("/", slash.Sequence);

            var plus = matcher.HandleKey(Keys.Oemplus, false, false, false);
            Assert.Equal(SimpleShortcutResultKind.Execute, plus.Kind);
            Assert.Equal<string?>("+", plus.Sequence);

            var minus = matcher.HandleKey(Keys.OemMinus, false, false, false);
            Assert.Equal(SimpleShortcutResultKind.Execute, minus.Kind);
            Assert.Equal<string?>("-", minus.Sequence);

            Assert.Equal(3, executed);
        }

        public static void Run_SimpleShortcutMatcher_CaseInsensitiveMatch()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["ctrl+h"] = () => executed++,
            };
            var matcher = new SimpleShortcutMatcher(bindings);

            var result = matcher.HandleKey(Keys.H, true, false, false);

            Assert.Equal(SimpleShortcutResultKind.Execute, result.Kind);
            Assert.Equal(1, executed);
        }

        public static void Run_SimpleShortcutMatcher_AltX()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["Alt+X"] = () => executed++,
            };
            var matcher = new SimpleShortcutMatcher(bindings);

            var result = matcher.HandleKey(Keys.X, false, false, true);

            Assert.Equal(SimpleShortcutResultKind.Execute, result.Kind);
            Assert.Equal<string?>("Alt+X", result.Sequence);
            Assert.Equal(1, executed);
        }

        // ================================================================
        // StaleToolWindowSentinel — M3 sentinel cache (BP-2)
        // RED: `StaleToolWindowSentinel` does not exist yet -> compile error
        // ================================================================

        public static void Run_StaleToolWindowSentinel_FileExistsAfterRefresh()
        {
            using (var dir = new TempDir())
            {
                string path = System.IO.Path.Combine(dir.Path, "sentinel");
                System.IO.File.WriteAllText(path, "");
                var sentinel = new StaleToolWindowSentinel(path);
                sentinel.Refresh();
                Assert.True(sentinel.IsStale, "a file that exists is stale after Refresh");
            }
        }

        public static void Run_StaleToolWindowSentinel_DeletedAfterRefresh()
        {
            using (var dir = new TempDir())
            {
                string path = System.IO.Path.Combine(dir.Path, "sentinel");
                System.IO.File.WriteAllText(path, "");
                var sentinel = new StaleToolWindowSentinel(path);
                sentinel.Refresh();
                Assert.True(sentinel.IsStale, "precondition: an existing file is stale after Refresh");
                System.IO.File.Delete(path);
                sentinel.Refresh();
                Assert.False(sentinel.IsStale, "after the file is deleted and Refresh runs, IsStale is false");
            }
        }

        public static void Run_StaleToolWindowSentinel_CachedWithoutRefresh()
        {
            using (var dir = new TempDir())
            {
                string path = System.IO.Path.Combine(dir.Path, "sentinel");
                System.IO.File.WriteAllText(path, "");
                var sentinel = new StaleToolWindowSentinel(path);
                sentinel.Refresh();
                Assert.True(sentinel.IsStale, "precondition: an existing file is stale after Refresh");
                System.IO.File.Delete(path);
                // NO Refresh() — the cached value must persist until the next refresh.
                Assert.True(sentinel.IsStale, "IsStale is cached: deleting the file without Refresh keeps IsStale true");
            }
        }

        public static void Run_StaleToolWindowSentinel_RefreshReportsChange()
        {
            using (var dir = new TempDir())
            {
                string path = System.IO.Path.Combine(dir.Path, "sentinel");
                var sentinel = new StaleToolWindowSentinel(path);
                System.IO.File.WriteAllText(path, "");
                Assert.True(sentinel.Refresh(), "create -> Refresh reports a change (false->true)");
                Assert.False(sentinel.Refresh(), "Refresh again with no change reports false");
                System.IO.File.Delete(path);
                Assert.True(sentinel.Refresh(), "delete -> Refresh reports a change (true->false)");
            }
        }

        public static void Run_StaleToolWindowSentinel_NullPathNeverStale()
        {
            var sentinel = new StaleToolWindowSentinel(null);
            sentinel.Refresh();
            Assert.False(sentinel.IsStale, "a null path is never stale (production no-op when NEOVISUAL_LOG_DIR is unset)");
        }

        // ================================================================
        // KeyNameBuilder — m1 StringBuilder key-name builder (BP-5)
        // RED: `KeyNameBuilder` does not exist yet -> compile error
        // ================================================================

        public static void Run_KeyNameBuilder_CtrlH()
        {
            Assert.Equal("Ctrl+H", KeyNameBuilder.Build(Keys.H, true, false, false));
        }

        public static void Run_KeyNameBuilder_ShiftF4()
        {
            Assert.Equal("Shift+F4", KeyNameBuilder.Build(Keys.F4, false, true, false));
        }

        public static void Run_KeyNameBuilder_AltX()
        {
            Assert.Equal("Alt+X", KeyNameBuilder.Build(Keys.X, false, false, true));
        }

        public static void Run_KeyNameBuilder_CtrlShiftAltDelete()
        {
            Assert.Equal("Ctrl+Shift+Alt+Delete", KeyNameBuilder.Build(Keys.Delete, true, true, true));
        }

        public static void Run_KeyNameBuilder_NoModifiers()
        {
            Assert.Equal("A", KeyNameBuilder.Build(Keys.A, false, false, false));
        }

        public static void Run_KeyNameBuilder_PrintableKeys()
        {
            Assert.Equal("/", KeyNameBuilder.Build(Keys.OemQuestion, false, false, false));
        }

        // ================================================================
        // InitSteps — per-step init diagnostics + named-step orchestration (M33)
        // RED: `InitSteps` doesn't exist -> compile error (CS0246/CS0103)
        // ================================================================

        public static void Run_InitSteps_SuccessLogsOk()
        {
            var sink = new List<string>();
            var steps = new InitSteps(sink.Add);
            Func<Task> ok = () => Task.CompletedTask;

            steps.RunAsync(new (string Name, Func<Task> Step)[]
            {
                ("telescope", ok),
                ("hook", ok),
            }).GetAwaiter().GetResult();

            Assert.True(sink.Contains("[MyExtension] init telescope ok"), "telescope step logs ok");
            Assert.True(sink.Contains("[MyExtension] init hook ok"), "hook step logs ok");
        }

        public static void Run_InitSteps_FailingStepLogsOwnDiagnosticAndContinues()
        {
            var sink = new List<string>();
            var steps = new InitSteps(sink.Add);
            Func<Task> ok = () => Task.CompletedTask;

            steps.RunAsync(new (string Name, Func<Task> Step)[]
            {
                ("telescope", ok),
                ("finders", () => throw new InvalidOperationException("boom")),
                ("hook", ok),
            }).GetAwaiter().GetResult();

            Assert.True(sink.Contains("[MyExtension] init telescope ok"), "telescope step logs ok");
            Assert.True(sink.Contains("[MyExtension] init finders failed: boom"),
                "the failing finders step logs its own diagnostic");
            Assert.True(sink.Contains("[MyExtension] init hook ok"),
                "hook step still runs after a failure (orchestration continues)");
        }

        public static void Run_InitSteps_DiagnosticNamesTheStep()
        {
            var sink = new List<string>();
            var steps = new InitSteps(sink.Add);

            steps.RunAsync(new (string Name, Func<Task> Step)[]
            {
                ("window-manager", () => throw new InvalidOperationException("wm down")),
            }).GetAwaiter().GetResult();

            Assert.True(sink.Any(l => l.StartsWith("[MyExtension] init window-manager failed: ", StringComparison.Ordinal)),
                "the diagnostic text names the failing step");
        }
    }

    /// <summary>
    /// Test-local IVimModeSource fake (M32): a settable Mode, a GetModeKind that ignores the
    /// view, no-op Attach/Detach, and a RaiseModeChanged that fires ModeChanged. Declared
    /// top-level (not nested in Tests) so an unresolvable IVimModeSource base does not suppress
    /// the compiler's diagnostics for the rest of the Tests type.
    /// </summary>
    internal sealed class FakeVimModeSource : IVimModeSource
    {
        public int? Mode { get; set; }
        public event Action<int?>? ModeChanged;
        public int? GetModeKind(ITextView view) => Mode;
        public void Attach(ITextView view) { }
        public void Detach(ITextView view) { }
        public void RaiseModeChanged() => ModeChanged?.Invoke(Mode);
    }
}