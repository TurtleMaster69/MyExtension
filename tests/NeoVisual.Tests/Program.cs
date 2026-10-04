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

        // R20 (BP-26): minimal IVsMonitorSelection fake so a WindowManager can be constructed
        // hermetically (the ctor AdviseSelectionEvents + GetCurrentElementValue). GetCurrentElementValue
        // returns a null frame, so OnWindowFocusChanged early-returns and the manager is inert — the
        // test only needs the instance to invoke the private GetController via reflection.
        private sealed class FakeMonitorSelection : IVsMonitorSelection
        {
            public int AdviseSelectionEvents(IVsSelectionEvents pSink, out uint pdwCookie)
            {
                pdwCookie = 1;
                return 0; // S_OK
            }

            public int UnadviseSelectionEvents(uint dwCookie) => 0;

            public int GetCurrentElementValue(uint elementid, out object pvarValue)
            {
                pvarValue = null!;
                return 0; // S_OK
            }

            public int GetCurrentSelection(out IntPtr ppHier, out uint pitemid, out IVsMultiItemSelect ppMIS, out IntPtr ppSC)
            {
                ppHier = IntPtr.Zero;
                pitemid = 0;
                ppMIS = null!;
                ppSC = IntPtr.Zero;
                return 0;
            }

            public int IsCmdUIContextActive(uint dwCmdUICookie, out int pfActive)
            {
                pfActive = 0;
                return 0;
            }

            public int SetCmdUIContext(uint dwCmdUICookie, int fActive) => 0;

            public int GetCmdUIContextCookie(ref Guid rguidCmdUI, out uint pdwCmdUICookie)
            {
                pdwCmdUICookie = 0;
                return 0;
            }

            public int GetSelectionInfo(out uint pnHier, out uint pnItemid, out uint pnMIS, out uint pnSC)
            {
                pnHier = 0;
                pnItemid = 0;
                pnMIS = 0;
                pnSC = 0;
                return 0;
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

        public static void Run_KeybindingConfig_ParseLeaderRejectsModifiers()
        {
            // N70 (BP-43): Enum.TryParse accepts modifier keys ("Control"/"Shift"/"Alt"), which
            // silently disables the leader key. The fix rejects modifiers and falls back to Space.
            // RED today: ParseLeader("Control") returns Keys.Control, so the leader is not Space.
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"Control\",\"bindings\":{}}").LeaderKey);
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"Shift\",\"bindings\":{}}").LeaderKey);
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"Alt\",\"bindings\":{}}").LeaderKey);
            // A real physical key (ControlKey) is still honored (the existing CustomLeaderParsed test).
            Assert.Equal(Keys.ControlKey,
                KeybindingConfig.LoadFromJson("{\"leader\":\"ControlKey\",\"bindings\":{}}").LeaderKey);
        }

        public static void Run_Keybinding_BindingsParsed()
        {
            var cfg = KeybindingConfig.LoadFromJson("{\"bindings\":{\"w\":\"command:File.SaveSelectedItems\",\"Ctrl+H\":\"navigate-left\"}}");
            Assert.Equal(2, cfg.Bindings.Count);
            Assert.Equal("command:File.SaveSelectedItems", cfg.Bindings["w"]);
            Assert.Equal("navigate-left", cfg.Bindings["Ctrl+H"]);
        }

        public static void Run_Keybinding_CaseSensitive()
        {
            // Gap 1 (AC5/D2): the config bindings map is Ordinal — an uppercase leader key no
            // longer matches its lowercase form (a capital letter in the config = Shift+letter).
            var upper = KeybindingConfig.LoadFromJson("{\"bindings\":{\"W\":\"command:X\"}}");
            Assert.False(upper.Bindings.ContainsKey("w"), "leader binding lookup is case-sensitive (W != w)");
            Assert.True(upper.Bindings.ContainsKey("W"), "the key as written is preserved");
            // Distinct-case leader sequences coexist in one config (an OrdinalIgnoreCase map
            // would collapse them into one entry).
            var both = KeybindingConfig.LoadFromJson("{\"bindings\":{\"s,g\":\"a\",\"s,G\":\"b\"}}");
            Assert.Equal(2, both.Bindings.Count);
            Assert.Equal("a", both.Bindings["s,g"]);
            Assert.Equal("b", both.Bindings["s,G"]);
        }

        public static void Run_Keybinding_NullUnbinds()
        {
            // A null binding value must remove the key from the map (falls through to editor).
            var cfg = KeybindingConfig.LoadFromJson("{\"bindings\":{\"q\":null,\"w\":\"command:X\"}}");
            Assert.True(cfg.Bindings.ContainsKey("w"), "non-null binding preserved");
            Assert.False(cfg.Bindings.ContainsKey("q"), "null binding removed");
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
            Assert.True(cfg.Bindings.ContainsKey("f,t"), "f,t -> telescope binding present (lowercase leader migration)");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+H"), "Ctrl+H -> navigate-left present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+J"), "Ctrl+J -> navigate-down present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+K"), "Ctrl+K -> navigate-up present");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+L"), "Ctrl+L -> navigate-right present");
            Assert.Equal(Keys.Space, cfg.LeaderKey);
            // Gap 1 (AC6/D3): EVERY leader binding in the defaults is lowercase under the case-based
            // contract (simple Ctrl+/Shift+/Alt+ shortcuts keep their canonical form).
            foreach (var key in cfg.Bindings.Keys)
            {
                if (!KeybindingConfig.IsSimpleShortcut(key))
                {
                    Assert.True(key.Equals(key.ToLowerInvariant(), StringComparison.Ordinal),
                        $"leader binding '{key}' must be lowercase after the migration (D3)");
                }
            }
        }

        public static void Run_Keybinding_DefaultFileHasWindowManagement()
        {
            // Gap 1 (AC1/AC2/AC3/AC4): the shipped defaults gain the LazyVim-style w prefix
            // (split below / split right / close window) and lose the W save binding (the user
            // saves with Ctrl+S). LoadDefaults reads ONLY the embedded resource (hermetic).
            var cfg = KeybindingConfig.LoadDefaults();
            Assert.True(cfg.Bindings.ContainsKey("w,-"), "w,- -> split-below binding present");
            Assert.Equal("command:Window.NewHorizontalTabGroup", cfg.Bindings["w,-"]);
            Assert.True(cfg.Bindings.ContainsKey("w,|"), "w,| -> split-right binding present");
            Assert.Equal("command:Window.NewVerticalTabGroup", cfg.Bindings["w,|"]);
            Assert.True(cfg.Bindings.ContainsKey("w,d"), "w,d -> close-window binding present");
            Assert.Equal("close-window", cfg.Bindings["w,d"]);
            Assert.False(cfg.Bindings.ContainsKey("w"),
                "the W save binding is removed (w is a prefix, not a binding)");
        }

        public static void Run_Keybinding_DefaultFileHasDiagnosticNav()
        {
            // Gap 3 (AC1-AC5/D2): the shipped defaults gain the six LazyVim-style
            // diagnostic-nav bindings. ],d/[,d are pure command: bindings (native in-file
            // squiggle nav); the severity pairs are the new built-in actions. LoadDefaults
            // reads ONLY the embedded resource (hermetic).
            var cfg = KeybindingConfig.LoadDefaults();
            Assert.True(cfg.Bindings.ContainsKey("],d"), "],d -> next-diagnostic binding present");
            Assert.Equal("command:Edit.GotoNextIssueinFile", cfg.Bindings["],d"]);
            Assert.True(cfg.Bindings.ContainsKey("[,d"), "[,d -> prev-diagnostic binding present");
            Assert.Equal("command:Edit.GotoPreviousIssueinFile", cfg.Bindings["[,d"]);
            Assert.True(cfg.Bindings.ContainsKey("],e"), "],e -> next-error binding present");
            Assert.Equal("next-error", cfg.Bindings["],e"]);
            Assert.True(cfg.Bindings.ContainsKey("[,e"), "[,e -> prev-error binding present");
            Assert.Equal("prev-error", cfg.Bindings["[,e"]);
            Assert.True(cfg.Bindings.ContainsKey("],w"), "],w -> next-warning binding present");
            Assert.Equal("next-warning", cfg.Bindings["],w"]);
            Assert.True(cfg.Bindings.ContainsKey("[,w"), "[,w -> prev-warning binding present");
            Assert.Equal("prev-warning", cfg.Bindings["[,w"]);
            // D2: NO bare ]/[ binding — the matcher's complete-match-before-prefix check means
            // a bare binding would shadow every pair (] alone would execute instead of waiting
            // for ,d).
            Assert.False(cfg.Bindings.ContainsKey("]"),
                "a bare ] binding would shadow the ] pairs (prefix trap)");
            Assert.False(cfg.Bindings.ContainsKey("["),
                "a bare [ binding would shadow the [ pairs (prefix trap)");
        }

        public static void Run_Keybinding_DefaultFileHasGitBindings()
        {
            // Gap 11 (AC1-AC4/D1): the shipped defaults gain the three git leader bindings under
            // the existing `g` prefix and DROP the branches binding (g,b rebinds branches -> blame;
            // the user's 2026-10-04 decision — the deferred lazygit overlay owns branches). g,g/g,c
            // are UNCHANGED (they rebind when that overlay ships). LoadDefaults reads ONLY the
            // embedded resource (hermetic — never the user's %APPDATA% file).
            // RED classification: assertion-RED, not compile-RED — no new API is referenced; before
            // BP-1 the ContainsKey("g,d")/ContainsKey("g,h") asserts throw and the g,b value assert
            // sees command:Team.Git.Branches.
            var cfg = KeybindingConfig.LoadDefaults();
            Assert.True(cfg.Bindings.ContainsKey("g,d"), "g,d -> diff binding present");
            Assert.Equal("command:Team.Git.CompareWithUnmodified", cfg.Bindings["g,d"]);
            Assert.True(cfg.Bindings.ContainsKey("g,b"), "g,b -> blame binding present");
            Assert.Equal("command:Team.Git.Annotate", cfg.Bindings["g,b"]);
            // The branches binding is GONE: a dictionary has one value per key, so the Equal above
            // already proves g,b no longer maps to Team.Git.Branches — pinned explicitly for the
            // diff reader (the Assert API has no NotEqual: tests/TestRunner.cs:95-127).
            Assert.False(cfg.Bindings["g,b"] == "command:Team.Git.Branches",
                "g,b must not map to Team.Git.Branches (rebound to blame)");
            Assert.True(cfg.Bindings.ContainsKey("g,h"), "g,h -> history binding present");
            Assert.Equal("command:Team.Git.ViewHistory", cfg.Bindings["g,h"]);
            // AC4: the deferred-overlay rebinds must NOT happen in this plan.
            Assert.Equal("command:View.GitChanges", cfg.Bindings["g,g"]);
            Assert.Equal("command:Team.Git.Commit", cfg.Bindings["g,c"]);
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
            Assert.False(KeybindingConfig.IsSimpleShortcut("f,+"), "a leader key containing + is NOT a simple shortcut");
            Assert.False(KeybindingConfig.IsSimpleShortcut("w"), "a bare leader key is not a simple shortcut");
        }

        // ================================================================
        // KeyNames — shared printable-key mapping (M21)
        // RED: `KeyNames` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_KeyNames_PrintableMappings()
        {
            // The printable-key contract shared by the leader and shortcut paths: the "/" key
            // (Keys.OemQuestion) must map to "/", "+" (Oemplus) to "+", "-" (OemMinus) to "-",
            // and a plain letter to its enum name. Gap 3 (AC7/D1): the bracket keys map to
            // their printable characters so the diagnostic-nav sequences are readable
            // ("]"/"[" — not "OemCloseBrackets"/"OemOpenBrackets").
            Assert.Equal("/", KeyNames.ToString(Keys.OemQuestion));
            Assert.Equal("+", KeyNames.ToString(Keys.Oemplus));
            Assert.Equal("-", KeyNames.ToString(Keys.OemMinus));
            Assert.Equal("|", KeyNames.ToString(Keys.OemPipe));
            Assert.Equal("]", KeyNames.ToString(Keys.OemCloseBrackets));
            Assert.Equal("[", KeyNames.ToString(Keys.OemOpenBrackets));
            Assert.Equal("F", KeyNames.ToString(Keys.F));
        }

        public static void Run_KeyNames_CaseEncodesShift()
        {
            // Gap 1 (AC5/D1): the shift-aware leader-sequence mapping — a letter's case encodes
            // Shift (lowercase = unshifted, uppercase = Shift+letter). Non-letters delegate to the
            // printable mapping, shift-insensitive (shift is not noted for non-letters).
            Assert.Equal("f", KeyNames.ToString(Keys.F, false));
            Assert.Equal("F", KeyNames.ToString(Keys.F, true));
            Assert.Equal("g", KeyNames.ToString(Keys.G, false));
            Assert.Equal("G", KeyNames.ToString(Keys.G, true));
            Assert.Equal("-", KeyNames.ToString(Keys.OemMinus, false));
            Assert.Equal("-", KeyNames.ToString(Keys.OemMinus, true));
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

        public static void Run_KeyNames_RoundTrip_WindowPrefix()
        {
            // Gap 1 (AC1/AC2/AC3): the w-prefix window bindings round-trip through the matcher:
            // Space, w (prefix), then the physical key builds the exact config sequence and fires.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,-"] = () => executed++,
                ["w,|"] = () => executed++,
                ["w,d"] = () => executed++,
            };

            // Split below: Space, w, OemMinus -> "w,-"
            var minusMatcher = new LeaderSequenceMatcher(Keys.Space, bindings);
            Assert.Equal(LeaderResultKind.Consume, minusMatcher.HandleKey(Keys.Space, false, false, false, false).Kind);
            Assert.Equal(LeaderResultKind.Consume, minusMatcher.HandleKey(Keys.W, false, false, false, false).Kind);
            var minus = minusMatcher.HandleKey(Keys.OemMinus, false, false, false, false);
            Assert.Equal(LeaderResultKind.Execute, minus.Kind);
            Assert.Equal<string?>("w,-", minus.Sequence);

            // Split right: Space, w, Shift+OemPipe -> "w,|" (Shift+OemPipe types '|')
            var pipeMatcher = new LeaderSequenceMatcher(Keys.Space, bindings);
            pipeMatcher.HandleKey(Keys.Space, false, false, false, false);
            pipeMatcher.HandleKey(Keys.W, false, false, false, false);
            var pipe = pipeMatcher.HandleKey(Keys.OemPipe, false, true, false, false);
            Assert.Equal(LeaderResultKind.Execute, pipe.Kind);
            Assert.Equal<string?>("w,|", pipe.Sequence);

            // Close window: Space, w, d -> "w,d"
            var closeMatcher = new LeaderSequenceMatcher(Keys.Space, bindings);
            closeMatcher.HandleKey(Keys.Space, false, false, false, false);
            closeMatcher.HandleKey(Keys.W, false, false, false, false);
            var close = closeMatcher.HandleKey(Keys.D, false, false, false, false);
            Assert.Equal(LeaderResultKind.Execute, close.Kind);
            Assert.Equal<string?>("w,d", close.Sequence);

            Assert.Equal(3, executed);
        }

        public static void Run_KeyNames_RoundTrip_SimpleShortcut()
        {
            // The "Ctrl+/" config key must survive parsing, and KeyNames.ToString(Keys.OemQuestion)
            // must produce the "/" that makes the Ctrl+/ config key match (the round-trip contract).
            var cfg = KeybindingConfig.LoadFromJson("{\"bindings\":{\"Ctrl+/\":\"navigate-left\"}}");
            Assert.True(cfg.Bindings.ContainsKey("Ctrl+/"), "the Ctrl+/ config key is preserved");
            Assert.Equal("Ctrl+/", "Ctrl+" + KeyNames.ToString(Keys.OemQuestion));
        }

        public static void Run_KeyNames_RoundTrip_DiagnosticNav()
        {
            // Gap 3 (AC1-AC5/AC7): all six diagnostic-nav sequences round-trip through the
            // matcher — Space, the physical bracket key (OemCloseBrackets/OemOpenBrackets),
            // then the letter builds the exact config sequence and fires. RED until D1's
            // KeyNames cases exist: the bracket builds "OemCloseBrackets", the sequence
            // "OemCloseBrackets,d" is neither a binding nor a prefix -> Abort (not Execute).
            var sequences = new[]
            {
                new { Sequence = "],d", Bracket = Keys.OemCloseBrackets, Letter = Keys.D },
                new { Sequence = "[,d", Bracket = Keys.OemOpenBrackets, Letter = Keys.D },
                new { Sequence = "],e", Bracket = Keys.OemCloseBrackets, Letter = Keys.E },
                new { Sequence = "[,e", Bracket = Keys.OemOpenBrackets, Letter = Keys.E },
                new { Sequence = "],w", Bracket = Keys.OemCloseBrackets, Letter = Keys.W },
                new { Sequence = "[,w", Bracket = Keys.OemOpenBrackets, Letter = Keys.W },
            };
            foreach (var s in sequences)
            {
                var executed = 0;
                var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
                {
                    [s.Sequence] = () => executed++,
                };
                var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

                matcher.HandleKey(Keys.Space, false, false, false, false);
                var prefix = matcher.HandleKey(s.Bracket, false, false, false, false);
                Assert.Equal(LeaderResultKind.Consume, prefix.Kind); // the bracket alone is a live prefix
                var final = matcher.HandleKey(s.Letter, false, false, false, false);
                Assert.Equal(LeaderResultKind.Execute, final.Kind);
                Assert.Equal<string?>(s.Sequence, final.Sequence);
                Assert.Equal(1, executed);
            }

            // Non-letters are shift-insensitive (the two-arg overload delegates to the printable
            // mapping): Shift+bracket types '}'/'{' but builds the SAME sequence name — the
            // documented ambiguity (no }/{ binding exists or is planned, per plan D1).
            Assert.Equal("]", KeyNames.ToString(Keys.OemCloseBrackets, true));
            Assert.Equal("[", KeyNames.ToString(Keys.OemOpenBrackets, true));
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
            // Feature 6 (AC5): 0/$ must be action keys so the hook pre-filter routes them to the
            // search box (they are not in DefaultControllerKeys). RED: _actions lacks D0/D4 today.
            Assert.True(keys.Contains(Keys.D0), "0 is an action key (search-box line start)");
            Assert.True(keys.Contains(Keys.D4), "$ is an action key (search-box line end)");
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

        public static void Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance()
        {
            // R20 (BP-26): ResolveController/DefaultControllerFor create a fresh default controller
            // on every dictionary miss — the "mode remembered per type" guarantee holds only because
            // package init eagerly registers every enum value. The fix caches per-type default
            // instances in an INSTANCE-scoped _defaultControllers dictionary on WindowManager
            // (populated on miss in GetController; NOT a static cache — the R40 class of issue).
            // RED: today a fresh instance is created per miss, so two GetController calls for the
            // same type return DIFFERENT instances. GetController is private + WindowManager is
            // VS-coupled (IVsMonitorSelection), so the test drives it via a fake monitor selection
            // + reflection (the only hermetic path to the instance cache). The ctor calls
            // RefreshCurrentWindow (ThreadHelper.ThrowIfNotOnUIThread), so mark this thread as the
            // UI thread first (the MTA test host is not the UI thread by default) by pointing
            // ThreadHelper's uiThreadDispatcher at the current thread's dispatcher.
            var uiThreadField = typeof(Microsoft.VisualStudio.Shell.ThreadHelper).GetField(
                "uiThreadDispatcher",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            // N50 (BP-7): save + restore the static dispatcher in a finally so a later test never
            // sees a stale dispatcher (the old test mutated it and never restored it).
            object? originalDispatcher = uiThreadField!.GetValue(null);
            try
            {
                uiThreadField.SetValue(null, System.Windows.Threading.Dispatcher.CurrentDispatcher);
                var manager = new WindowManager(new FakeMonitorSelection());
                var method = typeof(WindowManager).GetMethod(
                    "GetController",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.True(method != null, "WindowManager.GetController must exist (private instance)");

                var first = method!.Invoke(manager, new object[] { ToolWindowType.Toolbox });
                var second = method.Invoke(manager, new object[] { ToolWindowType.Toolbox });

                Assert.True(ReferenceEquals(first, second),
                    "the per-type default controller must be cached (same instance per type) — R20");
            }
            finally
            {
                uiThreadField.SetValue(null, originalDispatcher);
            }
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

        public static void Run_HierarchyResolver_PrimaryFilePath_EmptyReturnsNull()
        {
            // R9 (BP-22): PrimaryFilePath does `fileNames[0]` with no empty-list guard ->
            // IndexOutOfRangeException on a corrupt project item (an empty FileNames list). The fix
            // adds an empty-list guard (return null/empty). RED: today an empty list throws
            // IndexOutOfRangeException, so this test fails with that exception (the missing
            // empty-list guard).
            Assert.True(HierarchyResolver.PrimaryFilePath(new List<string>()) == null,
                "an empty file-name list must resolve to null (not throw IndexOutOfRangeException)");
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
        // HierarchyForestBuilder — pure forest builder over HierarchyNode DTOs
        // (M14: extract the DTE-coupled BuildForest recursion into a testable seam)
        // ================================================================

        public static void Run_HierarchyForestBuilder_NestedFoldersProduceNestedChildren()
        {
            // folder -> folder -> file: the builder recurses physical folders and nests the
            // file node under the inner folder node.
            var items = new[]
            {
                new HierarchyNode(
                    HierarchyResolver.PhysicalFolderKind, "Models", "",
                    new[]
                    {
                        new HierarchyNode(
                            HierarchyResolver.PhysicalFolderKind, "Sub", "",
                            new[]
                            {
                                new HierarchyNode(
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
                new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Program.CS", @"C:\p\Program.CS", null),
                new HierarchyNode(HierarchyResolver.PhysicalFileKind, "App.config", @"C:\p\App.config", null),
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
                new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Alpha.cs", fullPath, null),
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
                new HierarchyNode("{00000000-0000-0000-0000-000000000000}", "Dependencies", "",
                    new[]
                    {
                        new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Hidden.cs", @"C:\p\Hidden.cs", null),
                    }),
            };
            var forest = HierarchyForestBuilder.Build(items);

            Assert.Equal(0, forest.Count);
        }

        public static void Run_HierarchyForestBuilder_EmptyChildrenEmptyForest()
        {
            var forest = HierarchyForestBuilder.Build(new HierarchyNode[0]);

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

        public static void Run_TextMotionEngine_MapMotion_DownUp()
        {
            // Feature 6 (AC1/AC2): the WinForms mapping must translate j/k to Down/Up so the
            // Solution Explorer search box consumes them as motions (single-line -> no-op).
            // RED: MapKey(Keys.J/K) is not in the switch today -> returns null, not Down/Up.
            Assert.Equal(TextMotion.Down, TextMotionDispatcher.MapKey(Keys.J, false));
            Assert.Equal(TextMotion.Up, TextMotionDispatcher.MapKey(Keys.K, false));
        }

        public static void Run_TextMotionEngine_MapMotion_LineStartEnd()
        {
            // Feature 6 (AC3/AC4): 0 -> LineStart; $ (Shift+D4) -> LineEnd; a bare 4 is NOT a
            // motion (the $ drift fix). RED: MapKey(Keys.D0/D4) is not in the switch today ->
            // returns null.
            Assert.Equal(TextMotion.LineStart, TextMotionDispatcher.MapKey(Keys.D0, false));
            Assert.Equal(TextMotion.LineEnd, TextMotionDispatcher.MapKey(Keys.D4, true));
            Assert.Equal(null, TextMotionDispatcher.MapKey(Keys.D4, false));
        }

        public static void Run_TextMotionHelper_MapMotionDelegatesToDispatcher()
        {
            // N51 (BP-33): TextMotionHelper.TryMoveFocusedSurface's motion dispatch was verified
            // only by e2e. Pin the pure dispatch seam it delegates to (MapMotion ->
            // TextMotionDispatcher.MapKey) so the tool-window motion set is unit-covered.
            Assert.Equal(TextMotion.Left, TextMotionHelper.MapMotion(Keys.H, false));
            Assert.Equal(TextMotion.Right, TextMotionHelper.MapMotion(Keys.L, false));
            Assert.Equal(TextMotion.NextWord, TextMotionHelper.MapMotion(Keys.W, false));
            Assert.Equal(TextMotion.PrevWord, TextMotionHelper.MapMotion(Keys.B, false));
            Assert.Equal(TextMotion.EndWord, TextMotionHelper.MapMotion(Keys.E, false));
            Assert.Equal(TextMotion.InsertAfter, TextMotionHelper.MapMotion(Keys.A, false));
            Assert.Equal(TextMotion.InsertEnd, TextMotionHelper.MapMotion(Keys.A, true));
            Assert.Equal(TextMotion.InsertStart, TextMotionHelper.MapMotion(Keys.I, true));
            Assert.Equal(null, TextMotionHelper.MapMotion(Keys.I, false));
            Assert.Equal(null, TextMotionHelper.MapMotion(Keys.X, false));
            // Feature 6: the search-box motion set (j/k/0/$) must flow through the same
            // delegation seam. RED: MapKey does not map J/K/D0/D4 today -> null.
            Assert.Equal(TextMotion.Down, TextMotionHelper.MapMotion(Keys.J, false));
            Assert.Equal(TextMotion.Up, TextMotionHelper.MapMotion(Keys.K, false));
            Assert.Equal(TextMotion.LineStart, TextMotionHelper.MapMotion(Keys.D0, false));
            Assert.Equal(TextMotion.LineEnd, TextMotionHelper.MapMotion(Keys.D4, true));
            Assert.Equal(null, TextMotionHelper.MapMotion(Keys.D4, false));
        }

        public static void Run_TextMotionHelper_ApplyMotionMovesNavigator()
        {
            // N51 (BP-33): the motion math the tool-window surface applies (via
            // TextMotionDispatcher.Apply) — h/l/w/b/e + the a/A/I insert placements.
            var n = new TextMotionNavigator();
            n.SetText("one two");
            n.MoveTo(0);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.NextWord, n, out _));
            Assert.Equal(4, n.Caret);

            n.MoveTo(4);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.PrevWord, n, out _));
            Assert.Equal(0, n.Caret);

            n.SetText("hello");
            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.InsertAfter, n, out CaretPlacement? after));
            Assert.Equal(CaretPlacement.Current, after);
            Assert.Equal(3, n.Caret);

            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.InsertEnd, n, out CaretPlacement? end));
            Assert.Equal(CaretPlacement.End, end);
            Assert.Equal(5, n.Caret);

            n.MoveTo(2);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.InsertStart, n, out CaretPlacement? start));
            Assert.Equal(CaretPlacement.Start, start);
            Assert.Equal(0, n.Caret);

            // Feature 6: the navigator math the search box applies for j/k/0/$ — Down/Up are
            // single-line no-ops, LineStart/LineEnd move to the line bounds. (The navigator
            // already supports these; this pins the shared math the new mapping feeds.)
            n.SetText("one two");
            n.MoveTo(4);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.Down, n, out _));
            Assert.Equal(4, n.Caret);   // single line -> Down is a no-op
            Assert.True(TextMotionDispatcher.Apply(TextMotion.Up, n, out _));
            Assert.Equal(4, n.Caret);   // first line -> Up is a no-op
            Assert.True(TextMotionDispatcher.Apply(TextMotion.LineStart, n, out _));
            Assert.Equal(0, n.Caret);
            Assert.True(TextMotionDispatcher.Apply(TextMotion.LineEnd, n, out _));
            Assert.Equal(7, n.Caret);
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

        public static void Run_VimBufferSubscriptions_UnsubscribeRemovesClosedAndRefcount()
        {
            // R3 (BP-3): the lifecycle bookkeeping — UnsubscribeBuffer removes the Closed
            // subscription + the buffer from the subscribed set; OnBufferClosed decrements the
            // refcount to 0 and removes the entry; no double-decrement with Detach.
            var subs = new VimBufferSubscriptions();
            var viewA = new FakeTextView();
            var viewB = new FakeTextView();
            var bufferA = new object();
            var bufferB = new object();
            var textBuffer = new object(); // shared text buffer (split view)

            subs.Attach(viewA, bufferA, textBuffer);
            subs.Attach(viewB, bufferB, textBuffer);
            subs.MarkClosedSubscribed(bufferA);
            subs.MarkClosedSubscribed(bufferB);

            // UnsubscribeBuffer removes the Closed subscription + the buffer from the subscribed
            // set: after unsubscribing bufferA, a second UnsubscribeBuffer is a no-op (already
            // removed) — the refcount drops 2 -> 1 (not last).
            Assert.False(subs.UnsubscribeBuffer(bufferA),
                "UnsubscribeBuffer removes the Closed sub (refcount 2 -> 1, not last)");
            Assert.False(subs.UnsubscribeBuffer(bufferA),
                "a second UnsubscribeBuffer is a no-op (Closed sub already removed)");

            // OnBufferClosed decrements the refcount to 0 and removes the entry (last view).
            Assert.True(subs.OnBufferClosed(bufferB),
                "OnBufferClosed decrements the refcount to 0 and removes the entry");

            // No double-decrement with Detach: after OnBufferClosed removed bufferB (and
            // UnsubscribeBuffer removed bufferA), Detach must not decrement again.
            Assert.False(subs.Detach(viewB), "Detach after OnBufferClosed must not double-decrement");
            Assert.False(subs.Detach(viewA), "Detach after UnsubscribeBuffer must not double-decrement");
        }

        public static void Run_VimBufferSubscriptions_DetachSharedBufferSecondViewDecrements()
        {
            // N3 (BP-10): a shared-text-buffer second view is never Closed-subscribed
            // (VimModeSource early-returns before MarkClosedSubscribed), so Detach must decrement
            // the refcount regardless of _closedSubscribed membership. Today Detach returns false
            // without decrementing, so the refcount stays at 2 and closing the first view never
            // reaches 0 (the SwitchedMode subscription never drops).
            var subs = new VimBufferSubscriptions();
            var viewA = new FakeTextView();
            var viewB = new FakeTextView();
            var bufferA = new object();
            var bufferB = new object();
            var textBuffer = new object(); // shared text buffer (split view)

            subs.Attach(viewA, bufferA, textBuffer);
            subs.Attach(viewB, bufferB, textBuffer);
            subs.MarkClosedSubscribed(bufferA); // only A is Closed-subscribed; B is not

            // Detaching B (a non-Closed-subscribed shared-buffer view) must decrement 2 -> 1.
            Assert.False(subs.Detach(viewB), "detaching B is not the last ref (2 -> 1)");
            // Closing A now drops the refcount to 0 (B's detach already decremented).
            Assert.True(subs.OnBufferClosed(bufferA),
                "after B detached, closing A drops the shared-text-buffer refcount to 0");
        }

        public static void Run_VimBufferSubscriptions_DetachRemovesMapEntryAtZero()
        {
            // N3 (BP-10): when the refcount hits 0 the buffer->textBuffer map entry must be removed
            // (today it leaks for the whole session). The map is private, so inspect it via
            // reflection (the only hermetic path to the leak).
            var subs = new VimBufferSubscriptions();
            var view = new FakeTextView();
            var buffer = new object();
            var textBuffer = new object();

            subs.Attach(view, buffer, textBuffer);
            subs.MarkClosedSubscribed(buffer);
            Assert.True(subs.Detach(view), "detaching the last view drops the refcount to 0");

            var field = typeof(VimBufferSubscriptions).GetField(
                "_bufferToTextBuffer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.True(field != null, "VimBufferSubscriptions._bufferToTextBuffer must exist");
            var map = (System.Collections.IDictionary)field!.GetValue(subs)!;
            Assert.False(map.Contains(buffer),
                "_bufferToTextBuffer entry must be removed at refcount 0 (no per-session leak)");
        }

        // ================================================================
        // EditorViewOpenedLog — dedupe the editor-view-opened emission (N24/BP-12)
        // RED: `EditorViewOpenedLog` does not exist yet -> compile error (CS0246).
        // ================================================================

        public static void Run_EditorViewOpenedLog_SuppressesDuplicateWithinWindow()
        {
            // N24 (BP-12): the guarded helper suppresses a duplicate emission for the SAME path
            // within a short window (split/peek/preview views and the SelectFirstSourceFile +
            // TextViewCreated double-count). A unique path keeps the static last-path state from
            // leaking across tests.
            string path = @"C:\p\" + Guid.NewGuid().ToString("N") + ".cs";
            Assert.True(EditorViewOpenedLog.Emit(path), "first emit of a path is allowed");
            Assert.False(EditorViewOpenedLog.Emit(path),
                "a duplicate emit of the same path within the window is suppressed");
            Assert.True(EditorViewOpenedLog.Emit(path + ".other"), "a different path is allowed");
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
                // Feature 6 (AC5): 0/$ are search-box text motions and must be action keys so the
                // hook pre-filter routes them (they are not in DefaultControllerKeys).
                Keys.D0, Keys.D4,
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

        public static void Run_InjectedKeyGuard_StaleRecordExpiredAtRecord()
        {
            // R12 (BP-12): the TTL is checked on consume, not on record — a >1s UI stall between
            // Press's Record and the injected key-down expires the record -> re-injection storm.
            // The fix checks/expires the TTL at Record time: a stale record must be dropped when a
            // new Record arrives (the new Record starts fresh at count 1). RED: today Record carries
            // the stale record's count forward (count 2), so TWO consumes succeed — the stale
            // record survives into the next Record.
            var now = DateTime.UtcNow;
            var guard = new InjectedKeyGuard(TimeSpan.FromMilliseconds(100), () => now);
            guard.Record(13);          // record at T0
            now = now.AddSeconds(1);   // advance past the TTL (the record is now stale)
            guard.Record(13);          // a NEW Record arrives while the old one is stale

            // The stale record must NOT be carried forward: the new Record starts fresh (count 1),
            // so a single consume succeeds and a second fails.
            Assert.True(guard.TryConsume(13), "the fresh record consumes once");
            Assert.False(guard.TryConsume(13),
                "the stale record must not be carried into the next Record (count 1, not 2)");
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
            // N46 (BP-3): one assertion per case (the duplicate asserted the same expression).
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "editor-focused tool window must not route keys");
        }

        public static void Run_FocusGuard_TreeFocusedAllowsRouting()
        {
            // N46 (BP-3): one assertion per case (the duplicate asserted the same expression).
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: false, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "tree-focused tool window routes keys");
        }

        public static void Run_FocusGuard_InputModeBlocksActionKeys()
        {
            // N1 (BP-1): assert the guard's OWN return value directly. The old test asserted a
            // constant-false composite (`... && !isInputMode` with isInputMode=true), so it passed
            // regardless of FocusGuard. The guard returns TRUE for input mode (input mode owns the
            // keyboard -> routes -> blocks the key from the editor); the `!isInputMode` gate is
            // caller-side (InputHandler), not part of this assertion. (The deleted
            // `HasToolWindowActionKeys` was exactly that composite pre-filter decision.)
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: false, isInputMode: true, isTextInputSurface: false, textInputSurfaceFocused: false),
                "input-mode tool window routes keys (input mode owns the keyboard)");
        }

        public static void Run_FocusGuard_ZeroActionKeysBlocks()
        {
            // N2 (BP-2): assert the guard's OWN return value directly. The old test asserted a
            // constant-false composite (`... && actionKeyCount > 0` with actionKeyCount=0). The
            // action-key-count gate lives in the caller (InputHandler.IsKeyOfInterest); the guard
            // routes a tree-focused tool window regardless of action-key count.
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: false, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
                "a tree-focused tool window routes keys regardless of action-key count");
        }

        public static void Run_FocusGuard_NonToolWindowBlocks()
        {
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: false, editorFocused: false, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
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

        public static void Run_FocusGuard_TruthTable_ActionKeysEditorVeto()
        {
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false),
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
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: false),
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
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: true),
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
            // passes it into the guard, so EditorFocusedVeto and ShouldRouteToolWindowKey read
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

        public static void Run_FocusGuard_ShiftGatesActionKeysForNonTextInput()
        {
            // R10 (BP-10): action-key routing gates only !ctrl && !alt, not shift — Shift+O/R/M/A/G
            // in Solution Explorer fire the same tree actions and swallow the key. The fix gates
            // shift for non-text-input controllers (text-input controllers still need shift to tell
            // I/i and A/a apart), extractable to FocusGuard (pure). RED: today the guard has no
            // shift awareness — a non-text-input controller's action keys are interesting regardless
            // of shift, so this returns true and the Assert.False fails. The fix adds a shiftHeld
            // param to the guard; the build-agent updates this call to pass shiftHeld: true.
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(
                    isToolWindow: true, editorFocused: false, isInputMode: false,
                    isTextInputSurface: false, textInputSurfaceFocused: false, shiftHeld: true),
                "shift+action-key must not be interesting for a non-text-input controller (R10)");
        }

        public static void Run_FocusGuard_ShiftAllowsSearchBoxTextMotion()
        {
            // Feature 6 (AC6/D4): $ (Shift+D4) must reach the Solution Explorer search box despite
            // the R10 shift gate. A focused WPF TextBox that belongs to the current tool window is
            // exempt from the shift gate; with no such box the gate still blocks Shift+action-key
            // (R10 preserved). RED: the shift overload has no textBoxFocused parameter -> compile
            // error CS1739 (the best overload does not have a parameter named 'textBoxFocused').
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(
                    isToolWindow: true, editorFocused: false, isInputMode: false,
                    isTextInputSurface: false, textInputSurfaceFocused: false,
                    shiftHeld: true, textBoxFocused: true),
                "a focused search-box TextBox exempts $ from the shift gate");
            Assert.False(
                FocusGuard.ShouldRouteToolWindowKey(
                    isToolWindow: true, editorFocused: false, isInputMode: false,
                    isTextInputSurface: false, textInputSurfaceFocused: false,
                    shiftHeld: true, textBoxFocused: false),
                "without a focused search-box TextBox the shift gate still blocks (R10)");
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
            //   - search box still focused after 4 attempts  -> stop (N26/BP-32: do not fight the user)
            //   - otherwise                                  -> re-assert the selection
            Assert.Equal(FocusKeeperSchedule.Decision.InjectEscape, FocusKeeperSchedule.Decide(true, 0, 0, 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop, FocusKeeperSchedule.Decide(true, 0, 4, 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Reassert, FocusKeeperSchedule.Decide(false, 0, 0, 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop, FocusKeeperSchedule.Decide(false, 1500, 0, 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop, FocusKeeperSchedule.Decide(true, 1500, 0, 1500));
        }

        public static void Run_FocusKeeperSchedule_StopsAfterMaxEscapeAttempts()
        {
            // N26 (BP-32): after MaxEscapeAttempts (4) with the search box STILL focused, the
            // keeper must Stop (not Reassert) — it must not fight the user. RED today: Decide
            // returns Reassert for the 4-attempt case.
            Assert.Equal(FocusKeeperSchedule.Decision.InjectEscape,
                FocusKeeperSchedule.Decide(searchBoxFocused: true, elapsedMs: 0, escapeAttempts: 3, durationMs: 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop,
                FocusKeeperSchedule.Decide(searchBoxFocused: true, elapsedMs: 0, escapeAttempts: 4, durationMs: 1500));
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
            // The registry must hold exactly the 17 built-in action names, kept in sync with
            // default-keybindings.json (the hand-sync bug this seam removes). Gap 1 (AC3/D5)
            // added the focus-aware "close-window" action; Gap 3 (AC3-AC5/D4) adds the four
            // severity-filtered diagnostic-nav actions; Gap 6 adds the derived
            // "telescope-definition" action (the Definition finder).
            Assert.Equal(17, Actions.Registry.Count);
            var names = new[]
            {
                "navigate-left", "navigate-right", "navigate-up", "navigate-down",
                "telescope", "telescope-issues", "telescope-references",
                "telescope-implementation", "telescope-grep", "telescope-fzf",
                "telescope-definition",
                "toggle-solution-explorer", "close-window",
                "next-error", "prev-error", "next-warning", "prev-warning",
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
            // M30 / N48 (BP-5): the telescope action names in Actions.Registry must EXACTLY match
            // the keys of TelescopeLauncher.FinderNames (the single source of truth), and each
            // telescope action must map to a non-empty finder name. A telescope action added to one
            // map but not the other would throw KeyNotFoundException in the hook path. (The two
            // former tests computed the same key sets and asserted the same SetEquals — merged.)
            var registryTelescopeKeys = Actions.Registry.Keys
                .Where(k => k.StartsWith("telescope", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var finderNamesKeys = TelescopeLauncher.FinderNames.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.True(registryTelescopeKeys.SetEquals(finderNamesKeys),
                "Actions.Registry telescope keys must exactly match TelescopeLauncher.FinderNames keys");
            foreach (string key in registryTelescopeKeys)
            {
                string finder = TelescopeLauncher.FinderNames[key];
                Assert.True(!string.IsNullOrEmpty(finder), $"telescope action '{key}' must map to a finder name");
            }
        }

        // ================================================================
        // CloseWindowCommand — focus-aware close-window command seam (Gap 1 D5)
        // RED: `CloseWindowCommand` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_CloseWindowCommand_For()
        {
            // The pure focus-aware decision InputHandler.CloseWindow delegates to: a focused tool
            // window closes via Window.CloseToolWindow; anything else (editor/document) via
            // Window.CloseDocumentWindow.
            Assert.Equal("Window.CloseToolWindow", CloseWindowCommand.For(true));
            Assert.Equal("Window.CloseDocumentWindow", CloseWindowCommand.For(false));
        }

        // ================================================================
        // DiagnosticNavigator — pure severity-filtered diagnostics navigation (Gap 3 D3)
        // RED: `DiagnosticNavigator`/`DiagnosticEntry` do not exist yet -> compile error
        // (CS0246). The seam mirrors CloseWindowCommand (Gap 1 D5): a dependency-free static
        // decision the VS-coupled InputHandler.NavigateDiagnostic delegates to. Entries are
        // pre-sorted ascending by Line (the caller's gather contract).
        // ================================================================

        public static void Run_DiagnosticNavigator_Next_PicksFirstBelowCaret()
        {
            // Happy path: Next returns the FIRST entry strictly AFTER the caret line.
            var entries = new List<DiagnosticEntry>
            {
                new DiagnosticEntry(@"C:\p\A.cs", 10),
                new DiagnosticEntry(@"C:\p\B.cs", 20),
                new DiagnosticEntry(@"C:\p\C.cs", 30),
            };

            var first = DiagnosticNavigator.Next(entries, 5);
            Assert.True(first.HasValue, "a caret above every entry finds the first one");
            Assert.Equal(10, first.Value.Line);
            Assert.Equal(@"C:\p\A.cs", first.Value.FilePath);

            var mid = DiagnosticNavigator.Next(entries, 15);
            Assert.True(mid.HasValue, "a caret between entries finds the next one");
            Assert.Equal(20, mid.Value.Line);
            Assert.Equal(@"C:\p\B.cs", mid.Value.FilePath);
        }

        public static void Run_DiagnosticNavigator_Prev_PicksLastAboveCaret()
        {
            // Happy path: Prev returns the LAST entry strictly BEFORE the caret line.
            var entries = new List<DiagnosticEntry>
            {
                new DiagnosticEntry(@"C:\p\A.cs", 10),
                new DiagnosticEntry(@"C:\p\B.cs", 20),
                new DiagnosticEntry(@"C:\p\C.cs", 30),
            };

            var prev = DiagnosticNavigator.Prev(entries, 25);
            Assert.True(prev.HasValue, "a caret below the last entry finds the previous one");
            Assert.Equal(20, prev.Value.Line);
            Assert.Equal(@"C:\p\B.cs", prev.Value.FilePath);

            var last = DiagnosticNavigator.Prev(entries, 40);
            Assert.True(last.HasValue, "a caret past every entry finds the last one");
            Assert.Equal(30, last.Value.Line);
            Assert.Equal(@"C:\p\C.cs", last.Value.FilePath);
        }

        public static void Run_DiagnosticNavigator_Next_AtEndReturnsNull()
        {
            // AC6: NO wrap — at/past the end the navigator returns null so the caller logs
            // `[NeoVisual] diagnostic-nav no-op: at-end` (LazyVim buffer-local semantics).
            var entries = new List<DiagnosticEntry>
            {
                new DiagnosticEntry(@"C:\p\A.cs", 10),
                new DiagnosticEntry(@"C:\p\B.cs", 20),
            };

            Assert.False(DiagnosticNavigator.Next(entries, 20).HasValue,
                "a caret ON the last entry has no next (no wrap)");
            Assert.False(DiagnosticNavigator.Next(entries, 21).HasValue,
                "a caret past the last entry has no next (no wrap)");
        }

        public static void Run_DiagnosticNavigator_Prev_AtStartReturnsNull()
        {
            // AC6: NO wrap at the start either.
            var entries = new List<DiagnosticEntry>
            {
                new DiagnosticEntry(@"C:\p\A.cs", 10),
                new DiagnosticEntry(@"C:\p\B.cs", 20),
            };

            Assert.False(DiagnosticNavigator.Prev(entries, 10).HasValue,
                "a caret ON the first entry has no previous (no wrap)");
            Assert.False(DiagnosticNavigator.Prev(entries, 1).HasValue,
                "a caret before the first entry has no previous (no wrap)");
        }

        public static void Run_DiagnosticNavigator_EmptyReturnsNull()
        {
            // AC6: no entries in the file -> no-op (the caller logs
            // `[NeoVisual] diagnostic-nav no-op: no-entries`).
            var entries = new List<DiagnosticEntry>();
            Assert.False(DiagnosticNavigator.Next(entries, 1).HasValue, "Next on an empty list is a no-op");
            Assert.False(DiagnosticNavigator.Prev(entries, 1).HasValue, "Prev on an empty list is a no-op");
        }

        public static void Run_DiagnosticNavigator_CaretOnDiagnosticSkipsIt()
        {
            // LazyVim semantics: ]e with the caret ON a diagnostic moves to the NEXT one
            // (strict inequality both directions) — it never re-selects the entry at the caret.
            var entries = new List<DiagnosticEntry>
            {
                new DiagnosticEntry(@"C:\p\A.cs", 10),
                new DiagnosticEntry(@"C:\p\B.cs", 20),
                new DiagnosticEntry(@"C:\p\C.cs", 30),
            };

            var next = DiagnosticNavigator.Next(entries, 20);
            Assert.True(next.HasValue, "a caret ON the middle entry still has a next");
            Assert.Equal(30, next.Value.Line);

            var prev = DiagnosticNavigator.Prev(entries, 20);
            Assert.True(prev.HasValue, "a caret ON the middle entry still has a previous");
            Assert.Equal(10, prev.Value.Line);
        }

        public static void Run_DiagnosticNavigator_SingleEntry_BothDirections()
        {
            var entries = new List<DiagnosticEntry> { new DiagnosticEntry(@"C:\p\A.cs", 10) };

            var found = DiagnosticNavigator.Next(entries, 1);
            Assert.True(found.HasValue, "the single entry is found from above");
            Assert.Equal(10, found.Value.Line);
            Assert.False(DiagnosticNavigator.Next(entries, 10).HasValue, "no next past the single entry");
            Assert.False(DiagnosticNavigator.Prev(entries, 1).HasValue, "no previous before the single entry");
            var fromBelow = DiagnosticNavigator.Prev(entries, 99);
            Assert.True(fromBelow.HasValue, "the single entry is found from below");
            Assert.Equal(10, fromBelow.Value.Line);
        }

        public static void Run_DiagnosticNavigator_UnsortedInput_ListOrderContract()
        {
            // The gather contract: the CALLER pre-sorts ascending by Line; the navigator does
            // NOT sort. With unsorted input the scan is in LIST order — Next returns the first
            // entry (in list order) whose Line is past the caret, even when a smaller line
            // follows it. Pinning this keeps the navigator dependency-free (no OrderBy) and
            // makes the gatherer's sort obligation observable.
            var unsorted = new List<DiagnosticEntry>
            {
                new DiagnosticEntry(@"C:\p\C.cs", 30),
                new DiagnosticEntry(@"C:\p\A.cs", 10),
            };

            var next = DiagnosticNavigator.Next(unsorted, 5);
            Assert.True(next.HasValue, "an unsorted list still yields a next entry");
            Assert.Equal(30, next.Value.Line);
            Assert.Equal(@"C:\p\C.cs", next.Value.FilePath);
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
            var uiThreadField = typeof(Microsoft.VisualStudio.Shell.ThreadHelper).GetField(
                "uiThreadDispatcher",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object? originalDispatcher = uiThreadField!.GetValue(null);
            try
            {
                uiThreadField.SetValue(null, System.Windows.Threading.Dispatcher.CurrentDispatcher);
                Assert.Equal(null, WindowFrameAdapter.TryGetScreenRect(null));
            }
            finally
            {
                uiThreadField.SetValue(null, originalDispatcher);
            }
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
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            var result = matcher.HandleKey(Keys.Space, false, false, false, false);

            Assert.Equal(LeaderResultKind.Consume, result.Kind);
            Assert.True(matcher.IsActive, "leader key starts a sequence");
        }

        public static void Run_LeaderMatcher_LeaderKeyWhileTypingPassesThrough()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            var result = matcher.HandleKey(Keys.Space, false, false, false, true);

            Assert.Equal(LeaderResultKind.PassThrough, result.Kind);
            Assert.False(matcher.IsActive, "a typing leader key does not start a sequence");
        }

        public static void Run_LeaderMatcher_SingleKeyBindingExecutes()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var result = matcher.HandleKey(Keys.F, false, false, false, false);

            Assert.Equal(LeaderResultKind.Execute, result.Kind);
            Assert.Equal<string?>("f", result.Sequence);
            Assert.Equal(1, executed);
            Assert.False(matcher.IsActive, "sequence ends after execution");
        }

        public static void Run_LeaderMatcher_MultiKeySequence()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f,f"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var first = matcher.HandleKey(Keys.F, false, false, false, false);
            Assert.Equal(LeaderResultKind.Consume, first.Kind);
            Assert.True(matcher.IsActive, "a prefix keeps the sequence alive");

            var second = matcher.HandleKey(Keys.F, false, false, false, false);
            Assert.Equal(LeaderResultKind.Execute, second.Kind);
            Assert.Equal<string?>("f,f", second.Sequence);
            Assert.Equal(1, executed);
            Assert.False(matcher.IsActive, "sequence ends after execution");
        }

        public static void Run_LeaderMatcher_UnknownSequenceAborts()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f"] = () => executed++,
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
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f"] = () => executed++,
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
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f"] = () => executed++,
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
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f"] = () => throw new KeyNotFoundException("bad finder"),
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var result = matcher.HandleKey(Keys.F, false, false, false, false);

            Assert.Equal(LeaderResultKind.Failed, result.Kind);
            Assert.Equal<string?>("f", result.Sequence);
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
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["i,f"] = () => executed++,
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
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["f,f"] = () => executed++,
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

        public static void Run_LeaderSequenceMatcher_CaseSensitive()
        {
            // Gap 1 (AC5/D2): leader combos are CASE-SENSITIVE — s,g and s,G are distinct
            // sequences (a capital letter in the config means Shift+letter). The fixture dict is
            // Ordinal, matching production's BuildBindings leader dictionary.
            var executedLower = 0;
            var executedUpper = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["s,g"] = () => executedLower++,
                ["s,G"] = () => executedUpper++,
            };

            // Space, s, g -> "s,g"
            var lowerMatcher = new LeaderSequenceMatcher(Keys.Space, bindings);
            lowerMatcher.HandleKey(Keys.Space, false, false, false, false);
            lowerMatcher.HandleKey(Keys.S, false, false, false, false);
            var lower = lowerMatcher.HandleKey(Keys.G, false, false, false, false);
            Assert.Equal(LeaderResultKind.Execute, lower.Kind);
            Assert.Equal<string?>("s,g", lower.Sequence);
            Assert.Equal(1, executedLower);
            Assert.Equal(0, executedUpper);

            // Space, s, Shift+g -> "s,G" (fresh matcher: the first sequence ended on Execute)
            var upperMatcher = new LeaderSequenceMatcher(Keys.Space, bindings);
            upperMatcher.HandleKey(Keys.Space, false, false, false, false);
            upperMatcher.HandleKey(Keys.S, false, false, false, false);
            var upper = upperMatcher.HandleKey(Keys.G, false, true, false, false);
            Assert.Equal(LeaderResultKind.Execute, upper.Kind);
            Assert.Equal<string?>("s,G", upper.Sequence);
            Assert.Equal(1, executedUpper);
        }

        public static void Run_LeaderSequenceMatcher_PrefixWaits()
        {
            // Gap 1 (AC4, matcher half): with only "w,-" bound, Space+W alone is a live PREFIX —
            // the matcher consumes and waits, fires nothing. An unrelated next key aborts.
            // (GREEN today as well — this pins the contract so a regression that executes or
            // drops a prefix fails here.)
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,-"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var w = matcher.HandleKey(Keys.W, false, false, false, false);
            Assert.Equal(LeaderResultKind.Consume, w.Kind);
            Assert.True(matcher.IsActive, "w is a prefix: the sequence stays active");
            Assert.Equal(0, executed);

            var abort = matcher.HandleKey(Keys.X, false, false, false, false);
            Assert.Equal(LeaderResultKind.Abort, abort.Kind);
            Assert.False(matcher.IsActive, "an unrelated key after the prefix aborts");
            Assert.Equal(0, executed);
        }

        public static void Run_LeaderSequenceMatcher_ModifierChordTransparent()
        {
            // Gap 1 e2e defect (runs 152/153): '|' is typed with a Shift chord (Shift+0xDC). The
            // Shift key-down arrived while the "w" prefix was pending and was APPENDED
            // ("w,Shift" -> Abort), so the following 0xDC was pre-filter-rejected and "w,|" could
            // never fire. While a sequence is active, a modifier key-down must be TRANSPARENT:
            // consumed, not appended, not aborting.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,|"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var w = matcher.HandleKey(Keys.W, false, false, false, false);
            Assert.Equal(LeaderResultKind.Consume, w.Kind);

            // The Shift key-DOWN of the chord: transparent — consumed, sequence unchanged.
            var shiftDown = matcher.HandleKey(Keys.ShiftKey, false, true, false, false);
            Assert.Equal(LeaderResultKind.Consume, shiftDown.Kind);
            Assert.True(matcher.IsActive, "the Shift chord must not abort the pending sequence");

            // The shifted key itself completes the binding.
            var pipe = matcher.HandleKey(Keys.OemPipe, false, true, false, false);
            Assert.Equal(LeaderResultKind.Execute, pipe.Kind);
            Assert.Equal<string?>("w,|", pipe.Sequence);
            Assert.Equal(1, executed);
            Assert.False(matcher.IsActive, "sequence ends after execution");
        }

        public static void Run_LeaderSequenceMatcher_AllModifiersTransparentDownAndUp()
        {
            // Every physical modifier VK (Shift/Ctrl/Alt/Win, left and right variants) is
            // transparent while a sequence is active — key-DOWN and key-UP alike: consumed,
            // never appended, never aborting. (The hook currently dispatches only key-downs,
            // but the pure machine must stay coherent for both directions.)
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,|"] = () => executed++,
            };
            var modifiers = new[]
            {
                Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
                Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
                Keys.Menu, Keys.LMenu, Keys.RMenu,
                Keys.LWin, Keys.RWin,
            };

            foreach (var modifier in modifiers)
            {
                var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);
                matcher.HandleKey(Keys.Space, false, false, false, false);
                matcher.HandleKey(Keys.W, false, false, false, false);

                var down = matcher.HandleKey(modifier, false, false, false, false);
                Assert.Equal(LeaderResultKind.Consume, down.Kind);
                Assert.True(matcher.IsActive, $"{modifier} key-down must not abort the pending sequence");

                var up = matcher.HandleKey(modifier, false, false, false, false);
                Assert.Equal(LeaderResultKind.Consume, up.Kind);
                Assert.True(matcher.IsActive, $"{modifier} key-up must not abort the pending sequence");

                // The sequence is untouched: the shifted member still completes the binding.
                var pipe = matcher.HandleKey(Keys.OemPipe, false, true, false, false);
                Assert.Equal(LeaderResultKind.Execute, pipe.Kind);
                Assert.Equal<string?>("w,|", pipe.Sequence);
            }
            Assert.Equal(modifiers.Length, executed);
        }

        public static void Run_LeaderSequenceMatcher_LettersStillAppendAfterModifier()
        {
            // No over-broad change: a NON-modifier key after the transparent Shift still appends
            // and can still abort — modifier transparency must not swallow sequence members.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,d"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            matcher.HandleKey(Keys.W, false, false, false, false);
            var shift = matcher.HandleKey(Keys.ShiftKey, false, true, false, false);
            Assert.Equal(LeaderResultKind.Consume, shift.Kind);

            var x = matcher.HandleKey(Keys.X, false, false, false, false); // not a binding/prefix
            Assert.Equal(LeaderResultKind.Abort, x.Kind);
            Assert.False(matcher.IsActive, "a non-modifier non-prefix key still aborts");
            Assert.Equal(0, executed);
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