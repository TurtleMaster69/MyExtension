using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyExtension.Hooks;
using MyExtension.Input;
using MyExtension.Navigation;
using MyExtension.Package;
using MyExtension.ToolWindows;
using MyExtension.Vim;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Projection;
using Telescope.Logging;
using Telescope.Overlay;
using TestHarness;
using static TestHarness.TestScaffold;

namespace NeoVisual.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            MyExtension.Hooks.KeyInjection.SimulateOnly = true;
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

        // Counts non-overlapping occurrences of a substring (used to assert log lines are emitted
        // exactly once, e.g. `vim-mode=` on a mode change).
        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
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

        public static void Run_KeybindingConfig_IsSimpleShortcut()
        {
            // m42: BuildBindings classifies any binding key containing "+" as a simple shortcut
            // (InputHandler.cs:181-188), so a leader key like "F,+" can never match. The fix
            // extracts a pure modifier-prefix check into KeybindingConfig.IsSimpleShortcut:
            // TRUE only for Ctrl+/Shift+/Alt+ prefixes, FALSE for a leader key containing "+".
            // RED: the method does not exist -> compile error (CS0117: 'KeybindingConfig' does
            // not contain a definition for 'IsSimpleShortcut').
            Assert.True(KeybindingConfig.IsSimpleShortcut("Ctrl+H"), "Ctrl+ prefix is a simple shortcut");
            Assert.True(KeybindingConfig.IsSimpleShortcut("Shift+F"), "Shift+ prefix is a simple shortcut");
            Assert.True(KeybindingConfig.IsSimpleShortcut("Alt+X"), "Alt+ prefix is a simple shortcut");
            Assert.False(KeybindingConfig.IsSimpleShortcut("F,+"), "a leader key containing + is NOT a simple shortcut");
            Assert.False(KeybindingConfig.IsSimpleShortcut("W"), "a bare leader key is not a simple shortcut");
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

        public static void Run_GeneralToolWindowController_InitialModeFromType()
        {
            // m23/BP-36: the ctor `_isInputMode = IsTextInputType(type)` branch
            // (GeneralToolWindowController.cs:31) is dropped; the initial mode must still come from
            // the type classification (the base/initial-mode flow covers it). Behavior-preserving
            // pin: a text-input type starts in input mode, a navigation type starts in normal mode.
            var textInput = new GeneralToolWindowController(ToolWindowType.CommandWindow);
            Assert.True(textInput.IsInputMode, "a text-input type starts in input mode");
            var nav = new GeneralToolWindowController(ToolWindowType.Toolbox);
            Assert.False(nav.IsInputMode, "a navigation type starts in normal mode");
        }

        public static void Run_GeneralToolWindowController_KeyToArrowVk()
        {
            // m9/BP-41: the key->arrow VK mapping is single-sourced in KeyToArrowVk
            // (GeneralToolWindowController.cs:64-74). Pin the exact mapping so the
            // TextMotionHelper fallback (TextMotionHelper.cs:117) and SolutionExplorer's
            // H/L (SolutionExplorerController.cs:46-47) can delegate to it without drift.
            Assert.Equal(KeyInjection.VK_LEFT, GeneralToolWindowController.KeyToArrowVk(Keys.H));
            Assert.Equal(KeyInjection.VK_DOWN, GeneralToolWindowController.KeyToArrowVk(Keys.J));
            Assert.Equal(KeyInjection.VK_UP, GeneralToolWindowController.KeyToArrowVk(Keys.K));
            Assert.Equal(KeyInjection.VK_RIGHT, GeneralToolWindowController.KeyToArrowVk(Keys.L));
            Assert.Equal(0, GeneralToolWindowController.KeyToArrowVk(Keys.X));
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

        public static void Run_WindowManager_PerTypeDefaultsDoNotLeakInputMode()
        {
            // m22/BP-35: the shared `_defaultController` (WindowManager.cs:25,186-190) leaks
            // `_isInputMode` across ALL unknown-GUID tool windows — one window's input mode bleeds
            // into the next. The target is per-type default instances. This test pins the pure
            // resolution seam the fix needs: `WindowManager.ResolveController(registered, type)`
            // returns a registered controller or a PER-TYPE default instance (never a shared one).
            // RED: `ResolveController` does not exist yet -> compile error (CS0117: 'WindowManager'
            // does not contain a definition for 'ResolveController').
            var registered = new Dictionary<ToolWindowType, IToolWindowController>();
            var toolbox = WindowManager.ResolveController(registered, ToolWindowType.Toolbox);
            var output = WindowManager.ResolveController(registered, ToolWindowType.OutputWindow);
            Assert.True(toolbox != null, "Toolbox resolves a controller");
            Assert.True(output != null, "OutputWindow resolves a controller");
            Assert.True(!ReferenceEquals(toolbox, output),
                "two unknown-GUID tool windows must NOT share a controller instance (m22 input-mode leak)");
            toolbox.EnterInputMode();
            Assert.True(toolbox.IsInputMode, "the first window is in input mode");
            Assert.False(output.IsInputMode,
                "the second window's input mode must not be affected by the first (shared _defaultController leak)");
        }

        public static void Run_WindowManager_RegisteredControllerWins()
        {
            // m22/BP-35 contract: a controller registered for a type wins over the per-type default.
            // RED: `ResolveController` does not exist yet -> compile error (CS0117).
            var registered = new Dictionary<ToolWindowType, IToolWindowController>
            {
                [ToolWindowType.Toolbox] = new GeneralToolWindowController(ToolWindowType.Toolbox),
            };
            var resolved = WindowManager.ResolveController(registered, ToolWindowType.Toolbox);
            Assert.True(ReferenceEquals(resolved, registered[ToolWindowType.Toolbox]),
                "a registered controller wins over the default");
        }

        // ================================================================
        // HierarchyResolver — select-first-source-file resolution (pure seam)
        // ================================================================

        public static void Run_HierarchyResolver_FirstSourceFile_FileVsFolder()
        {
            // m25: a physical FILE returns its own path (not recursed).
            var fileNode = new HierarchyNode(
                HierarchyResolver.PhysicalFileKind, "Beta.cs", @"C:\p\Beta.cs", null);
            Assert.True(
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { fileNode }) == @"C:\p\Beta.cs",
                "a physical file returns its own path");
        }

        public static void Run_HierarchyResolver_FirstSourceFile_FolderRecursion()
        {
            // m25: a physical FOLDER recurses to its first physical file (in order).
            var folder = new HierarchyNode(
                HierarchyResolver.PhysicalFolderKind, "Models", "",
                new HierarchyNode[]
                {
                    new HierarchyNode(HierarchyResolver.PhysicalFileKind, "User.cs", @"C:\p\Models\User.cs", null),
                    new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Order.cs", @"C:\p\Models\Order.cs", null),
                });
            Assert.True(
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { folder }) == @"C:\p\Models\User.cs",
                "a physical folder recurses to its first physical file");
        }

        public static void Run_HierarchyResolver_FirstSourceFile_SkipsNonFileNonFolder()
        {
            // m25: non-file/non-folder nodes are skipped (not recursed), first real file still found.
            var unknown = new HierarchyNode("{00000000-0000-0000-0000-000000000000}", "Dependencies", "", null);
            var realFile = new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Alpha.cs", @"C:\p\Alpha.cs", null);
            Assert.True(
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { unknown, realFile }) == @"C:\p\Alpha.cs",
                "non-file/non-folder nodes are skipped, the first real file is still found");
        }

        public static void Run_HierarchyResolver_FirstSourceFile_EmptyReturnsNull()
        {
            // m25: empty -> null (no reachable source file).
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

        public static void Run_HierarchyResolver_PrimaryFilePath()
        {
            // M8: `g` must open the PRIMARY file of a multi-file project item. EnvDTE's
            // ProjectItem.FileNames is 1-based and FileNames[1] is the primary file's full
            // path — the current caller indexes the LAST file (FileNames[FileCount]), so a
            // Form1.cs/Form1.Designer.cs/Form1.resx item opens the .resx. The pure helper
            // takes a primitive string list (EnvDTE.ProjectItem is a COM interface that
            // cannot be constructed hermetically) and returns fileNames[0] (the primary).
            // RED: `HierarchyResolver.PrimaryFilePath` does not exist yet -> compile error
            // (CS0117).
            var fileNames = new List<string> { "Form1.cs", "Form1.Designer.cs", "Form1.resx" };
            Assert.Equal("Form1.cs", HierarchyResolver.PrimaryFilePath(fileNames));
        }

        public static void Run_HierarchyResolver_FirstSourceFile_PrefersCsOverNonCs()
        {
            // m26/BP-39: the `.cs` filter must live in the resolver — a forest whose first physical
            // file is non-.cs (e.g. .resx) must still pick the .cs file. RED: today
            // `FirstSourceFilePath` returns the first physical file regardless of extension (the
            // `.cs` filter lives only in HierarchyForestBuilder), so this returns the .resx path.
            var resx = new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Form1.resx", @"C:\p\Form1.resx", null);
            var cs = new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Form1.cs", @"C:\p\Form1.cs", null);
            Assert.Equal(@"C:\p\Form1.cs",
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { resx, cs }));
        }

        public static void Run_HierarchyResolver_FirstSourceFile_PrefersCsInLaterFolder()
        {
            // m26/BP-39: the `.cs` filter applies through folder recursion — a folder whose first
            // physical file is non-.cs must still yield a .cs file from a later folder. RED: today
            // the first physical file (the .resx) wins regardless of extension.
            var resxFolder = new HierarchyNode(
                HierarchyResolver.PhysicalFolderKind, "Resources", "",
                new HierarchyNode[]
                {
                    new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Form1.resx", @"C:\p\Resources\Form1.resx", null),
                });
            var csFolder = new HierarchyNode(
                HierarchyResolver.PhysicalFolderKind, "Models", "",
                new HierarchyNode[]
                {
                    new HierarchyNode(HierarchyResolver.PhysicalFileKind, "User.cs", @"C:\p\Models\User.cs", null),
                });
            Assert.Equal(@"C:\p\Models\User.cs",
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { resxFolder, csFolder }));
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
            // M21: Build takes no pathToItem map (the throwaway map was never read by any caller).
            var forest = HierarchyForestBuilder.Build(items);

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
            var forest = HierarchyForestBuilder.Build(items);

            Assert.Equal(1, forest.Count);
            Assert.Equal("Program.CS", forest[0].Name);
            Assert.Equal(HierarchyResolver.PhysicalFileKind, forest[0].Kind);
        }

        public static void Run_HierarchyForestBuilder_FullPathFlowsThroughAndPathMap()
        {
            // The file's FullPath lands in HierarchyNode.FilePath. (M21: the throwaway pathToItem
            // map is gone — Build takes no map param, so only the forest is asserted.)
            const string fullPath = @"C:\p\Alpha.cs";
            var items = new[]
            {
                new HierarchyItemInfo(HierarchyResolver.PhysicalFileKind, "Alpha.cs", fullPath, null),
            };
            var forest = HierarchyForestBuilder.Build(items);

            Assert.Equal(1, forest.Count);
            Assert.Equal(fullPath, forest[0].FilePath);
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
            var forest = HierarchyForestBuilder.Build(items);

            Assert.Equal(0, forest.Count);
        }

        public static void Run_HierarchyForestBuilder_EmptyChildrenEmptyForest()
        {
            var forest = HierarchyForestBuilder.Build(new HierarchyItemInfo[0]);

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
            Assert.True(controller.ActionKeys.Count > 0, "text-input controller exposes action keys");
        }

        public static void Run_TextInput_TryMove_UnmappedKeyNotConsumed()
        {
            // m28: TryMove is callable on the test host (the csproj references
            // Microsoft.VisualStudio.Text.UI, so the IWpfTextView type JITs fine). An unmapped key
            // (X) is not consumed and does not throw. RED: if TryMove throws on the test host, the
            // stale "Text.UI not referenced" comment was right and this test fails.
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            Assert.False(controller.TryMove(Keys.X), "an unmapped key is not consumed (no throw)");
        }

        // ================================================================
        // TextMotionEngine — TextMotionDispatcher.MapKey (BP-16/M19)
        // The shared pure dispatcher owns the single key->motion table for BOTH surfaces; the
        // tool-window surface uses its WinForms-Keys mapping. RED: the dispatcher does not exist
        // yet -> compile error (CS0246). (The skeleton exists so the tests compile; the M19 fix
        // wires TextMotionHelper.MapMotion / TryDispatch.Handle to delegate to it.)
        // ================================================================

        public static void Run_TextMotionEngine_MapMotion_LeftRight()
        {
            Assert.Equal(TextMotion.Left, TextMotionDispatcher.MapKey(Keys.H, false));
            Assert.Equal(TextMotion.Right, TextMotionDispatcher.MapKey(Keys.L, false));
        }

        public static void Run_TextMotionEngine_MapMotion_Words()
        {
            Assert.Equal(TextMotion.NextWord, TextMotionDispatcher.MapKey(Keys.W, false));
            Assert.Equal(TextMotion.PrevWord, TextMotionDispatcher.MapKey(Keys.B, false));
            Assert.Equal(TextMotion.EndWord, TextMotionDispatcher.MapKey(Keys.E, false));
        }

        public static void Run_TextMotionEngine_MapMotion_InsertShift()
        {
            // A (Shift+a) = insert at end; a = insert after caret; I (Shift+i) = insert at start.
            Assert.Equal(TextMotion.InsertEnd, TextMotionDispatcher.MapKey(Keys.A, true));
            Assert.Equal(TextMotion.InsertAfter, TextMotionDispatcher.MapKey(Keys.A, false));
            Assert.Equal(TextMotion.InsertStart, TextMotionDispatcher.MapKey(Keys.I, true));
            // A bare i (no shift) is the generic insert handled by InputHandler, not a motion.
            Assert.Equal(null, TextMotionDispatcher.MapKey(Keys.I, false));
        }

        public static void Run_TextMotionEngine_MapMotion_UnknownNull()
        {
            Assert.Equal(null, TextMotionDispatcher.MapKey(Keys.X, false));
        }

        // ================================================================
        // VimBufferSubscriptions — pure per-view Vim-buffer subscription map (BP-2/CR2)
        // RED: the map type does not exist yet -> compile error (CS0246). The skeleton exists so
        // the tests compile; the CR2 fix wires VsVimModeSource to use it (per-view Detach).
        // ================================================================

        public static void Run_VimBufferSubscriptions_AttachTwoDetachOneKeepsOther()
        {
            // CR2: with two views attached, detaching A must NOT kill B's subscription.
            var subs = new VimBufferSubscriptions();
            var viewA = new FakeTextView();
            var viewB = new FakeTextView();
            var bufferA = new object();
            var bufferB = new object();

            subs.Attach(viewA, bufferA);
            subs.Attach(viewB, bufferB);
            subs.Detach(viewA);

            Assert.True(ReferenceEquals(bufferB, subs.BufferFor(viewB)),
                "detaching A keeps B's buffer subscription");
            Assert.True(subs.BufferFor(viewA) == null, "detaching A removes A's buffer");
        }

        public static void Run_VimBufferSubscriptions_DetachNonAttachedNoOp()
        {
            var subs = new VimBufferSubscriptions();
            var viewA = new FakeTextView();
            var viewB = new FakeTextView();
            subs.Attach(viewA, new object());

            // Detaching a view that was never attached must be a no-op (no throw, no effect).
            subs.Detach(viewB);

            Assert.True(subs.BufferFor(viewA) != null, "the attached view's buffer survives a non-attached detach");
        }

        public static void Run_VimBufferSubscriptions_ReattachAfterDetach()
        {
            var subs = new VimBufferSubscriptions();
            var view = new FakeTextView();
            var first = new object();
            var second = new object();

            subs.Attach(view, first);
            subs.Detach(view);
            subs.Attach(view, second);

            Assert.True(ReferenceEquals(second, subs.BufferFor(view)),
                "re-attaching a view after detach re-subscribes it to the new buffer");
        }

        // ================================================================
        // ActionTable — ActionKeys == _actions.Keys incl. hjkl (BP-2/T2)
        // RED: hjkl are not in ActionKeys today (SolutionExplorer =
        // O/Enter/R/M/A/W/B/E/G, TextInput = W/B/E/A) -> assertion failure
        // ================================================================

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
            // CR1: the exact action-key set is {W,B,E,A,H,L,I} (count 7). RED today: the
            // consolidation dropped Keys.I from _actions, so the actual set is {W,B,E,A,H,L}
            // (count 6) and this exact-set assertion fails.
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            var expected = new[] { Keys.W, Keys.B, Keys.E, Keys.A, Keys.H, Keys.L, Keys.I };
            var actual = new List<Keys>(controller.ActionKeys);
            Assert.Equal(expected.Length, actual.Count);
            foreach (var key in expected)
            {
                Assert.True(actual.Contains(key), $"ActionKeys contains {key}");
            }
        }

        public static void Run_TextInput_KeysIMapsToInsertStart()
        {
            // CR1: the I action must be present in the text-input controller's action table and
            // map to InsertStart (Shift+i). RED today: Keys.I is missing from _actions, so the
            // action table does not contain it.
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            var keys = new List<Keys>(controller.ActionKeys);
            Assert.True(keys.Contains(Keys.I), "I is an action key (CR1)");
            // The pure mapping the I action applies: Shift+I -> InsertStart.
            Assert.Equal(TextMotion.InsertStart, TextMotionHelper.MapMotion(Keys.I, true));
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

        public static void Run_ActionTable_SolutionExplorer_HlArrowVk()
        {
            // m17/BP-41: SolutionExplorer's H/L must press the SAME arrow VKs KeyToArrowVk
            // produces (VK_LEFT/VK_RIGHT) — the fix replaces the hardcoded VK_LEFT/VK_RIGHT
            // (SolutionExplorerController.cs:46-47) with KeyToArrowVk. Pin the exact VK so a
            // refactor cannot silently change which arrow h/l press.
            var controller = new SolutionExplorerController(() => null!);
            while (InjectedKeyGuard.Instance.TryConsume(KeyInjection.VK_LEFT)) { }
            while (InjectedKeyGuard.Instance.TryConsume(KeyInjection.VK_RIGHT)) { }
            Assert.True(controller.TryMove(Keys.H), "h collapses (handled)");
            Assert.True(InjectedKeyGuard.Instance.TryConsume(KeyInjection.VK_LEFT),
                "h presses VK_LEFT (m17: KeyToArrowVk, not a hardcoded drift)");
            Assert.True(controller.TryMove(Keys.L), "l expands (handled)");
            Assert.True(InjectedKeyGuard.Instance.TryConsume(KeyInjection.VK_RIGHT),
                "l presses VK_RIGHT (m17: KeyToArrowVk, not a hardcoded drift)");
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

        public static void Run_InjectedKeyGuard_StaleRecordExpires()
        {
            // m43: a stale pending record must NOT consume the next physical key-down. The guard
            // records a timestamp per VK; a record older than the TTL is treated as absent.
            // RED: the clock-seam ctor does not exist -> compile error (CS1729: 'InjectedKeyGuard'
            // does not contain a constructor that takes 2 arguments) — the TTL behavior is missing
            // today, so a stale record consumes the next physical key.
            var now = DateTime.UtcNow;
            var guard = new InjectedKeyGuard(TimeSpan.FromMilliseconds(100), () => now);
            guard.Record(13);
            now = now.AddSeconds(1); // advance the clock past the TTL
            Assert.False(guard.TryConsume(13), "a stale record must not consume the next physical key-down");
        }

        public static void Run_InjectedKeyGuard_FreshRecordConsumesOnce()
        {
            // m43: a fresh record (within the TTL) still consumes once — the TTL must not break
            // the existing consume-once semantics.
            var now = DateTime.UtcNow;
            var guard = new InjectedKeyGuard(TimeSpan.FromMilliseconds(100), () => now);
            guard.Record(13);
            Assert.True(guard.TryConsume(13), "a fresh record consumes once");
            Assert.False(guard.TryConsume(13), "a fresh record is consumed once (no double consume)");
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
        // 7 = the real TextInputToolWindowController action-key count after CR1 added Keys.I (m26).
        private const int PositiveActionKeyCount = 7;

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

        public static void Run_FocusGuard_OwnsKeyboard_TruthTable()
        {
            // M22: the single keyboard-ownership exemption helper. Input mode owns the keyboard;
            // a genuinely focused text-input surface owns it; neither -> false.
            Assert.True(
                FocusGuard.OwnsKeyboard(isInputMode: true, isTextInputSurface: false, textInputSurfaceFocused: false),
                "input mode owns the keyboard");
            Assert.True(
                FocusGuard.OwnsKeyboard(isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: true),
                "a genuinely focused text-input surface owns the keyboard");
            Assert.False(
                FocusGuard.OwnsKeyboard(isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "neither input mode nor a focused text-input surface -> does not own the keyboard");
            Assert.False(
                FocusGuard.OwnsKeyboard(isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: false),
                "a non-focused text-input surface does not own the keyboard");
        }

        public static void Run_FocusGuard_TextInputSurfaceFocused_EditorFocusedNotFocusedSurface()
        {
            // M16: a text-input surface only owns the keyboard when it genuinely holds focus.
            // With the editor focused and the text-input surface NOT focused, routing must be off
            // (the current isTextInputSurface exemption leaks — it returns TRUE here).
            Assert.False(
                FocusGuard.HasToolWindowActionKeys(
                    isToolWindow: true, isInputMode: false, actionKeyCount: PositiveActionKeyCount, editorFocused: true, isTextInputSurface: true, textInputSurfaceFocused: false),
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
                    isToolWindow: true, isInputMode: false, actionKeyCount: PositiveActionKeyCount, editorFocused: true, isTextInputSurface: true, textInputSurfaceFocused: true),
                "genuinely-focused text-input surface action keys are interesting");
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: true),
                "genuinely-focused text-input surface routes keys");
        }

        public static void Run_FocusGuard_IsTypingTruthTable()
        {
            // Editor insert/replace -> typing (leader key must type a space).
            Assert.True(
                FocusGuard.IsTyping(isToolWindow: false, isInputMode: false, editorFocusedVeto: true, editorInTypingMode: true),
                "editor insert/replace is typing");
            // Editor normal -> not typing.
            Assert.False(
                FocusGuard.IsTyping(isToolWindow: false, isInputMode: false, editorFocusedVeto: true, editorInTypingMode: false),
                "editor normal is not typing");
        }

        public static void Run_FocusGuard_IsTypingToolWindowInput()
        {
            // Tool window in input mode -> typing regardless of editor state.
            Assert.True(
                FocusGuard.IsTyping(isToolWindow: true, isInputMode: true, editorFocusedVeto: false, editorInTypingMode: false),
                "tool-window input mode is typing");
            // Tool window in normal mode, editor not focused -> not typing.
            Assert.False(
                FocusGuard.IsTyping(isToolWindow: true, isInputMode: false, editorFocusedVeto: false, editorInTypingMode: false),
                "tool-window normal mode is not typing");
        }

        public static void Run_FocusGuard_OwnsKeyboard()
        {
            // M5: the single-source ownsKeyboard routing. The caller computes ONE ownsKeyboard
            // bool (from the cached _windowManager.IsTextInputType + input mode + focused) and
            // passes it into the guard, so EditorFocusedVeto and HasToolWindowActionKeys read
            // the same source. A text-input window that owns the keyboard is NOT vetoed by a
            // stale editor-focus flag; a navigation window that does not own the keyboard IS
            // vetoed.
            // RED: the current 5-arg signature has no ownsKeyboard parameter -> compile error
            // (CS1739: the best overload for 'ShouldRouteToolWindowKey' does not have a
            // parameter named 'ownsKeyboard').
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, ownsKeyboard: true),
                "a text-input window that owns the keyboard is not vetoed by the stale editor flag");
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, ownsKeyboard: false),
                "a navigation window that does not own the keyboard is vetoed by the editor flag");
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
            state.SetMode(VimModeClassifier.Insert);
            state.OnViewLostFocus(isFocusedView: false);
            Assert.True(state.IsTyping, "out-of-order LostFocus must not clear typing");
        }

        public static void Run_VimModeState_ClosedNonFocusedKeepsTyping()
        {
            // M17: a Closed event from a non-focused view must not clear the typing flag
            // (VimModeTracker.cs:209 currently clears _cachedTyping unconditionally).
            var state = new VimModeState();
            state.SetMode(VimModeClassifier.Insert);
            state.OnViewClosed(isFocusedView: false);
            Assert.True(state.IsTyping, "non-focused Closed must not clear typing");
        }

        public static void Run_VimModeState_ClassificationTruthTable()
        {
            // Pins the vim-mode= name contract: Normal/Insert/Replace/Unknown + the typing flag.
            var state = new VimModeState();
            state.SetMode(VimModeClassifier.Normal);
            Assert.False(state.IsTyping, "Normal is not typing");
            Assert.Equal("Normal", state.ModeName);
            state.SetMode(VimModeClassifier.Insert);
            Assert.True(state.IsTyping, "Insert is typing");
            Assert.Equal("Insert", state.ModeName);
            state.SetMode(VimModeClassifier.Replace);
            Assert.True(state.IsTyping, "Replace is typing");
            Assert.Equal("Replace", state.ModeName);
            state.SetMode(null);
            Assert.False(state.IsTyping, "null mode is not typing");
            Assert.Equal("Unknown", state.ModeName);
        }

        public static void Run_VimModeState_LostFocusNormalReturnsModeChange()
        {
            // m41: OnViewLostFocus must return the MODE-change (not just the typing-change) so the
            // tracker emits `vim-mode=Unknown` on editor-focus loss even when the editor was in
            // Normal mode (typing didn't change, but the mode name did: Normal -> Unknown).
            // RED today: the current code returns only the typing-change, so a Normal-mode focus
            // loss returns false and `vim-mode=Unknown` is never emitted.
            var state = new VimModeState();
            state.SetMode(VimModeClassifier.Normal);
            bool changed = state.OnViewLostFocus(isFocusedView: true);
            Assert.True(changed, "focus loss from Normal must signal a mode change (Normal -> Unknown)");
            Assert.Equal("Unknown", state.ModeName);
        }

        public static void Run_VimModeState_ClosedNormalReturnsModeChange()
        {
            // m41: same contract for OnViewClosed — closing a focused Normal-mode view must signal
            // the mode change (Normal -> Unknown) so `vim-mode=Unknown` is emitted.
            var state = new VimModeState();
            state.SetMode(VimModeClassifier.Normal);
            bool changed = state.OnViewClosed(isFocusedView: true);
            Assert.True(changed, "closing a focused Normal-mode view must signal a mode change (Normal -> Unknown)");
            Assert.Equal("Unknown", state.ModeName);
        }

        public static void Run_VimModeState_LostFocusInsertStillSignalsChange()
        {
            // Behavior-preserving pin: an Insert-mode focus loss still signals a change (typing
            // true -> false AND mode Insert -> Unknown), so `vim-mode=Unknown` keeps being emitted.
            var state = new VimModeState();
            state.SetMode(VimModeClassifier.Insert);
            bool changed = state.OnViewLostFocus(isFocusedView: true);
            Assert.True(changed, "focus loss from Insert must signal a change");
            Assert.Equal("Unknown", state.ModeName);
        }

        public static void Run_VimModeState_NonFocusedViewNoChange()
        {
            // Behavior-preserving pin: a non-focused view's LostFocus/Closed must not signal a
            // change and must not clear the typing state (M17 out-of-order guard).
            var state = new VimModeState();
            state.SetMode(VimModeClassifier.Insert);
            Assert.False(state.OnViewLostFocus(isFocusedView: false), "non-focused LostFocus signals no change");
            Assert.True(state.IsTyping, "non-focused LostFocus must not clear typing");
            Assert.False(state.OnViewClosed(isFocusedView: false), "non-focused Closed signals no change");
            Assert.True(state.IsTyping, "non-focused Closed must not clear typing");
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
                    Telescope.Logging.LogFileWriter.Flush();

                    string content = ReadAllTextShared(logPath);
                    Assert.True(content.Contains("[NeoVisual] vim-mode=Insert"),
                        "Insert mode logs vim-mode=Insert");

                    source.Mode = 1;
                    source.RaiseModeChanged();
                    Telescope.Logging.LogFileWriter.Flush();

                    content = ReadAllTextShared(logPath);
                    Assert.True(content.Contains("[NeoVisual] vim-mode=Normal"),
                        "Normal mode logs vim-mode=Normal");
                });
            }
        }

        public static void Run_VimModeTracker_LogOnlyOnChange()
        {
            // M16: `vim-mode=` must be logged only when the mode value actually changes. A focus
            // gain/loss with the SAME mode emits nothing. RED today: UpdateTypingFromMode logs on
            // every call, so two identical Insert events emit two `vim-mode=Insert` lines.
            using (var dir = new TempDir())
            {
                string logPath = System.IO.Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var source = new FakeVimModeSource();
                    var tracker = new VimModeTracker(source);

                    source.Mode = 2;
                    source.RaiseModeChanged();
                    source.Mode = 2;
                    source.RaiseModeChanged();
                    source.Mode = 1;
                    source.RaiseModeChanged();
                    Telescope.Logging.LogFileWriter.Flush();

                    string content = ReadAllTextShared(logPath);
                    int insertCount = CountOccurrences(content, "[NeoVisual] vim-mode=Insert");
                    int normalCount = CountOccurrences(content, "[NeoVisual] vim-mode=Normal");
                    Assert.Equal(1, insertCount);
                    Assert.Equal(1, normalCount);
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
        // Helpers — WindowRect
        // ================================================================

        public static void Run_RectCoordinate_StoresFields()
        {
            // N2 (BP-1): WindowRect becomes a readonly struct with uppercase readonly fields.
            // RED: `r.X` does not compile before the merge (fields are lowercase x,y,width,height).
            var r = new WindowRect(1, 2, 3, 4);
            Assert.Equal(1, r.X);
            Assert.Equal(2, r.Y);
            Assert.Equal(3, r.Width);
            Assert.Equal(4, r.Height);
        }

        // ================================================================
        // WindowRect geometry members (BP-1/N2)
        // RED: Right/Bottom/IsEmpty/Adjacency/GapTo + Axis/Direction don't exist -> compile error
        // ================================================================

        public static void Run_RectCoordinate_Right_Bottom()
        {
            var r = new WindowRect(1, 2, 3, 4);
            Assert.Equal(4, r.Right);   // X + Width
            Assert.Equal(6, r.Bottom);  // Y + Height
        }

        public static void Run_RectCoordinate_IsEmpty()
        {
            Assert.True(new WindowRect(0, 0, 0, 0).IsEmpty, "all-zero rect is empty");
            Assert.False(new WindowRect(1, 0, 0, 0).IsEmpty, "non-zero X means not empty");
        }

        public static void Run_RectCoordinate_Adjacency()
        {
            // 1-D span overlap on the given axis (closed-form AdjacencySize).
            Assert.Equal(5, new WindowRect(0, 0, 10, 10).Adjacency(new WindowRect(5, 0, 10, 10), Axis.X));
            Assert.Equal(0, new WindowRect(0, 0, 10, 10).Adjacency(new WindowRect(20, 0, 10, 10), Axis.X));
            Assert.Equal(5, new WindowRect(0, 0, 10, 10).Adjacency(new WindowRect(0, 5, 10, 10), Axis.Y));
        }

        public static void Run_RectCoordinate_GapTo()
        {
            var active = new WindowRect(100, 100, 100, 100); // Right=200, Bottom=200
            Assert.Equal(50, new WindowRect(100, 0, 100, 50).GapTo(active, Direction.Up));
            Assert.Equal(2, new WindowRect(100, 202, 100, 50).GapTo(active, Direction.Down));
            Assert.Equal(50, new WindowRect(0, 100, 50, 100).GapTo(active, Direction.Left));
            Assert.Equal(50, new WindowRect(250, 100, 50, 100).GapTo(active, Direction.Right));
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
            var active = new WindowRect(100, 100, 100, 100); // Right=200, Bottom=200
            var candidates = new[]
            {
                new WindowRect(100, 0, 100, 50),  // gap 50, adjacency 100
                new WindowRect(150, 0, 50, 50),   // gap 50, adjacency 50
            };
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_Down_ToleranceExcludes()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[]
            {
                new WindowRect(100, 101, 100, 50), // c.Y - a.Y = 1, EXCLUDED by the >1 tolerance
                new WindowRect(100, 102, 100, 50), // c.Y - a.Y = 2, passes
            };
            Assert.Equal(1, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Down, settings));
        }

        public static void Run_WindowNavigationEngine_Down_OnePixelGapAccepted()
        {
            // m48 (BP-27): the Down/Up tolerance must be SYMMETRIC. Up accepts a 1px-gap
            // candidate above (bare `c.Y < a.Y`, WindowNavigationEngine.cs:85); Down must
            // accept the mirror-image 1px-gap candidate below. Today Down uses
            // `c.Y - a.Y > 1` (WindowNavigationEngine.cs:86), so it REJECTS the 1px-gap
            // candidate -> SelectTarget returns null -> RED (asymmetric tolerance).
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);

            // Baseline: Up accepts a 1px-gap candidate above (c.Y = 99 < 100).
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active,
                new[] { new WindowRect(100, 99, 100, 50) }, Direction.Up, settings));

            // Symmetric: Down must accept the mirror-image 1px-gap candidate below (c.Y = 101).
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active,
                new[] { new WindowRect(100, 101, 100, 50) }, Direction.Down, settings));
        }

        public static void Run_WindowNavigationEngine_Left_PicksLargestAdjacency()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[]
            {
                new WindowRect(0, 100, 50, 100),   // gap 50, adjacency 100
                new WindowRect(0, 150, 50, 50),    // gap 50, adjacency 50
            };
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Left, settings));
        }

        public static void Run_WindowNavigationEngine_Right_PicksLargestAdjacency()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[]
            {
                new WindowRect(250, 100, 50, 100), // gap 50, adjacency 100
                new WindowRect(250, 150, 50, 50),  // gap 50, adjacency 50
            };
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Right, settings));
        }

        public static void Run_WindowNavigationEngine_EmptyCandidates_ReturnsNull()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            Assert.Equal(null, WindowNavigationEngine.SelectTarget(active, new WindowRect[0], Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_NoCandidateInDirection_ReturnsNull()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[] { new WindowRect(100, 201, 100, 50) }; // below, but direction is Up
            Assert.Equal(null, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_NotAligned_Excluded()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[] { new WindowRect(0, 0, 50, 50) }; // above but no X-overlap with [100,200)
            Assert.Equal(null, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_AdjacencyTie_LastWins()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[]
            {
                new WindowRect(100, 0, 100, 50),  // gap 50, adjacency 100
                new WindowRect(100, 20, 100, 50), // gap 30, adjacency 100 (tie)
            };
            Assert.Equal(1, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_DivideWindow_ExcludesBeyond()
        {
            var settings = NavigationSettings.FromDpi(96, 96); // YDivide=100
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[]
            {
                new WindowRect(100, 0, 100, 50),   // gap 50 (min)
                new WindowRect(100, -200, 100, 50), // gap 250 > 50+100, beyond the divide window
            };
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        public static void Run_WindowNavigationEngine_HiddenZeroRect_Excluded()
        {
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[]
            {
                new WindowRect(0, 0, 0, 0),        // hidden/empty, excluded
                new WindowRect(100, 0, 100, 50),  // the only real candidate
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
                new WindowRect(0, 0, 100, 100),
                new WindowRect(200, 0, 100, 100),
            };
            var snapshot = NavigationSnapshot.Capture(rects, 1);
            Assert.Equal(snapshot.Value.Candidates[1], snapshot.Value.Active);
        }

        public static void Run_NavigationSnapshot_ActiveIndexOutOfRange_ReturnsNull()
        {
            // M2 (BP-1): an out-of-range active index (IndexOf returns -1, or the active
            // window is not in the list) must no-op — Capture returns null so the caller
            // returns without indexing Candidates[ActiveIndex] out of range.
            var rects = new[]
            {
                new WindowRect(0, 0, 100, 100),
                new WindowRect(200, 0, 100, 100),
            };
            Assert.Equal(null, NavigationSnapshot.Capture(rects, -1));
            Assert.Equal(null, NavigationSnapshot.Capture(rects, rects.Length));
        }

        // ================================================================
        // WindowFrameAdapter.TryGetScreenRect — M12 null-frame + Empty fallback (BP-4)
        // RED: `WindowFrameAdapter.TryGetScreenRect` doesn't exist -> compile error (CS0117)
        // ================================================================

        public static void Run_WindowAdapter_TryGetScreenRect_NullFrameReturnsNull()
        {
            // M12 (BP-4): a null IVsWindowFrame4 must yield null (the old code path
            // `(IVsWindowFrame4)_frame` throws InvalidCastException on a non-conforming frame).
            Assert.Equal(null, WindowFrameAdapter.TryGetScreenRect(null));
        }

        public static void Run_WindowNavigationEngine_EmptyRectExcludedBySelectTarget()
        {
            // m55: this test never calls WindowFrameAdapter.TryGetScreenRect (a COM frame cannot be
            // built hermetically) — it asserts the "Empty fallback" contract directly: when a
            // frame's rect fetch fails and RefreshRect falls back to WindowRect.Empty, the
            // engine must exclude that (0,0,0,0) entry and return the real candidate — mirrors
            // Run_WindowNavigationEngine_HiddenZeroRect_Excluded above.
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[]
            {
                new WindowRect(0, 0, 0, 0),        // WindowRect.Empty fallback (BP-4 adds the constant)
                new WindowRect(100, 0, 100, 50),  // the only real candidate
            };
            Assert.Equal(1, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
        }

        // ================================================================
        // WindowNavigator.BuildActiveWindows — m44 null-active-window helper (BP-31)
        // RED: `WindowNavigator.BuildActiveWindows` doesn't exist -> compile error (CS0117)
        // ================================================================

        public static void Run_WindowNavigator_BuildActiveWindowsNullActive()
        {
            // m44 (BP-31): a null active window must degrade to an empty/no-op window list —
            // the null check must run BEFORE WindowFrameAdapter.LinkedTo dereferences activeWindow
            // (WindowNavigator.cs:47-58 today derefs first, so the graceful-degradation branch is
            // dead). The helper takes the adapters as a primitive list (WindowFrameAdapter is a
            // VS-coupled COM-paired type that cannot be constructed hermetically), so the test
            // passes an empty list — the null-active path must not touch it and must not throw.
            // RED: `WindowNavigator.BuildActiveWindows` does not exist -> compile error (CS0117).
            var adapters = new List<WindowFrameAdapter>();
            var result = WindowNavigator.BuildActiveWindows(null, adapters);
            Assert.Equal(0, result.Count);
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

        public static void Run_LeaderMatcher_ActiveSequenceConsumesI()
        {
            // M15: while a leader sequence is active, I must be treated as a sequence key
            // (consumed), NOT passed through — the InputHandler guard (`!_leaderMatcher.IsActive &&
            // key == Keys.I`) relies on the matcher consuming I as part of the sequence.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["I,F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var result = matcher.HandleKey(Keys.I, false, false, false, false);

            Assert.Equal(LeaderResultKind.Consume, result.Kind);
            Assert.True(matcher.IsActive, "I continues the active leader sequence");
            Assert.Equal(0, executed);
        }

        public static void Run_LeaderMatcher_PrefixSetBuiltOnce()
        {
            // m56: assert BEHAVIOR instead of the private field. The prefix set's observable
            // contract: with only "F,F" bound, typing "F" after the leader must NOT abort (it is a
            // proper prefix of a longer binding) — the matcher keeps waiting (Consume). Then "F"
            // again executes "F,F". If the prefix set were absent/broken, the first "F" would Abort.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["F,F"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false); // leader
            var first = matcher.HandleKey(Keys.F, false, false, false, false);
            Assert.Equal(LeaderResultKind.Consume, first.Kind);
            Assert.True(matcher.IsActive, "a proper prefix keeps the sequence active");
            Assert.Equal(0, executed);

            var second = matcher.HandleKey(Keys.F, false, false, false, false);
            Assert.Equal(LeaderResultKind.Execute, second.Kind);
            Assert.Equal(1, executed);
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

        public static void Run_InitSteps_RunSyncLogsOk()
        {
            // M24: the sync step runner shares the per-step try/catch + `[MyExtension] init <name>
            // ok/failed` contract with RunAsync. RED: `InitSteps.RunSync` does not exist yet ->
            // compile error (CS0117).
            var sink = new List<string>();
            InitSteps.RunSync("x", () => { }, sink.Add);

            Assert.True(sink.Contains("[MyExtension] init x ok"), "a sync step logs ok");
        }

        public static void Run_InitSteps_RunSyncFailingLogsFailed()
        {
            // M24: a throwing sync step logs `[MyExtension] init x failed: boom` and does NOT throw
            // (the per-step try/catch swallows it, matching RunAsync).
            var sink = new List<string>();
            InitSteps.RunSync("x", () => throw new InvalidOperationException("boom"), sink.Add);

            Assert.True(sink.Contains("[MyExtension] init x failed: boom"),
                "a throwing sync step logs failed and does not throw");
        }

        public static void Run_RoslynGatherers_IsWriteLocation()
        {
            // m4 (BP-63): the reflection read of ReferenceLocation.IsWrittenTo must be unit-testable
            // offline. RED: `RoslynGatherers` does not exist -> compile error (CS0246).
            var writeLoc = MakeReferenceLocation(isWrittenTo: true);
            var readLoc = MakeReferenceLocation(isWrittenTo: false);

            Assert.True(RoslynGatherers.IsWriteLocation(writeLoc),
                "a write location must report IsWrittenTo=true");
            Assert.False(RoslynGatherers.IsWriteLocation(readLoc),
                "a read-only location must report IsWrittenTo=false");
            Assert.False(RoslynGatherers.IsWriteLocation(default(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation)),
                "a default location must report false (the reflection-failure defensive path)");
        }

        // Constructs a ReferenceLocation with the given IsWrittenTo flag via the internal ctor
        // (Roslyn 4.14 makes the ctor + IsWrittenTo internal; the production IsWriteLocation reads
        // the property via reflection, so the test builds the struct the same way). The 7-param
        // ctor is (document, alias, location, isImplicit, symbolUsageInfo, additionalProperties,
        // candidateReason) and ReferenceLocation.IsWrittenTo is DERIVED from
        // SymbolUsageInfo.IsWrittenTo() — so the write/read flag must be encoded in the
        // SymbolUsageInfo (ValueUsageInfo.Write -> IsWrittenTo=true, ValueUsageInfo.Read -> false),
        // not in the isImplicit arg (which is always false here). SymbolUsageInfo/ValueUsageInfo
        // are internal in the Roslyn 4.14 netstandard2.0 build, so the SymbolUsageInfo is built
        // via the internal static SymbolUsageInfo.Create(ValueUsageInfo) through reflection.
        private static Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation MakeReferenceLocation(bool isWrittenTo)
        {
            var type = typeof(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation);
            var ctor = type.GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .First(c => c.GetParameters().Length == 7);
            var ps = ctor.GetParameters();
            var args = new object[7];
            args[0] = null; // Document
            args[1] = null; // IAliasSymbol
            args[2] = null; // Location
            args[3] = false; // isImplicit
            args[4] = MakeSymbolUsageInfo(isWrittenTo); // SymbolUsageInfo
            args[5] = Activator.CreateInstance(ps[5].ParameterType); // ImmutableArray<(string,string)>
            args[6] = Activator.CreateInstance(ps[6].ParameterType); // CandidateReason
            return (Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation)ctor.Invoke(args);
        }

        // Builds a SymbolUsageInfo whose IsWrittenTo() matches the requested flag via the internal
        // static SymbolUsageInfo.Create(ValueUsageInfo) (both types are internal in Roslyn 4.14).
        private static object MakeSymbolUsageInfo(bool isWrittenTo)
        {
            var asm = typeof(Microsoft.CodeAnalysis.FindSymbols.ReferenceLocation).Assembly;
            var suiType = asm.GetType("Microsoft.CodeAnalysis.SymbolUsageInfo", throwOnError: true)!;
            var vuiType = asm.GetType("Microsoft.CodeAnalysis.ValueUsageInfo", throwOnError: true)!;
            var create = suiType.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                .First(m => m.Name == "Create" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == vuiType);
            var usage = Enum.Parse(vuiType, isWrittenTo ? "Write" : "Read");
            return create.Invoke(null, new[] { usage })!;
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

    /// <summary>
    /// Minimal ITextView fake for the VimBufferSubscriptions tests (CR2). The subscription map
    /// only uses the view as a dictionary key (reference identity), so every member throws — the
    /// fake just needs to be a distinct, non-null ITextView instance.
    /// </summary>
    internal sealed class FakeTextView : ITextView
    {
        public IBufferGraph BufferGraph => throw new NotImplementedException();
        public ITextCaret Caret => throw new NotImplementedException();
        public void Close() => throw new NotImplementedException();
        public event EventHandler? Closed { add { } remove { } }
        public void DisplayTextLineContainingBufferPosition(SnapshotPoint bufferPosition, double verticalDistance, ViewRelativePosition relativeTo) => throw new NotImplementedException();
        public void DisplayTextLineContainingBufferPosition(SnapshotPoint bufferPosition, double verticalDistance, ViewRelativePosition relativeTo, double? viewportWidthOverride, double? viewportHeightOverride) => throw new NotImplementedException();
        public SnapshotSpan GetTextElementSpan(SnapshotPoint point) => throw new NotImplementedException();
        public ITextViewLine GetTextViewLineContainingBufferPosition(SnapshotPoint bufferPosition) => throw new NotImplementedException();
        public event EventHandler? GotAggregateFocus { add { } remove { } }
        public bool HasAggregateFocus => throw new NotImplementedException();
        public bool InLayout => throw new NotImplementedException();
        public bool IsClosed => throw new NotImplementedException();
        public bool IsMouseOverViewOrAdornments => throw new NotImplementedException();
        public event EventHandler<TextViewLayoutChangedEventArgs>? LayoutChanged { add { } remove { } }
        public double LineHeight => throw new NotImplementedException();
        public event EventHandler? LostAggregateFocus { add { } remove { } }
        public double MaxTextRightCoordinate => throw new NotImplementedException();
        public event EventHandler<MouseHoverEventArgs>? MouseHover { add { } remove { } }
        public IEditorOptions Options => throw new NotImplementedException();
        public Microsoft.VisualStudio.Utilities.PropertyCollection Properties => throw new NotImplementedException();
        public ITrackingSpan? ProvisionalTextHighlight { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public void QueueSpaceReservationStackRefresh() => throw new NotImplementedException();
        public ITextViewRoleSet Roles => throw new NotImplementedException();
        public ITextSelection Selection => throw new NotImplementedException();
        public ITextBuffer TextBuffer => throw new NotImplementedException();
        public ITextDataModel TextDataModel => throw new NotImplementedException();
        public ITextSnapshot TextSnapshot => throw new NotImplementedException();
        public ITextViewLineCollection TextViewLines => throw new NotImplementedException();
        public ITextViewModel TextViewModel => throw new NotImplementedException();
        public double ViewportBottom => throw new NotImplementedException();
        public double ViewportHeight => throw new NotImplementedException();
        public event EventHandler? ViewportHeightChanged { add { } remove { } }
        public double ViewportLeft { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public event EventHandler? ViewportLeftChanged { add { } remove { } }
        public double ViewportRight => throw new NotImplementedException();
        public double ViewportTop => throw new NotImplementedException();
        public double ViewportWidth => throw new NotImplementedException();
        public event EventHandler? ViewportWidthChanged { add { } remove { } }
        public IViewScroller ViewScroller => throw new NotImplementedException();
        public ITextSnapshot VisualSnapshot => throw new NotImplementedException();
    }
}