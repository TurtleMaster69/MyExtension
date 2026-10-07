using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using MyExtension.Adornments;
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
using Telescope.Controller;
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

        // BP-12 (M2): builds a WindowFrameAdapter hermetically (GetUninitializedObject skips the
        // ctor's ThrowIfNotOnUIThread) with the given DTE window injected via the _dte field — the
        // Run_WindowNavigator_CacheClearedOnReenum pattern.
        private static WindowFrameAdapter MakeAdapter(EnvDTE.Window dte)
        {
            var adapter = (WindowFrameAdapter)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(WindowFrameAdapter));
            typeof(WindowFrameAdapter).GetField("_dte",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(adapter, dte);
            return adapter;
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

        public static void Run_Keybinding_PhysicalModifierLeaderRejected()
        {
            // BP-9 (m14): "ControlKey" is a physical modifier key — accepted by ParseLeader yet
            // non-functional as a leader (a lone modifier key can never be a leader). The fix
            // rejects it: the leader falls back to Space.
            var cfg = KeybindingConfig.LoadFromJson("{\"leader\":\"ControlKey\",\"bindings\":{}}");
            Assert.Equal(Keys.Space, cfg.LeaderKey);
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
            // BP-9 (m14): a physical modifier key (ControlKey) is also rejected — accepted yet
            // non-functional as a leader (a lone modifier key can never be a leader).
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"ControlKey\",\"bindings\":{}}").LeaderKey);
        }

        public static void Run_KeybindingConfig_RejectsPhysicalModifierLeader()
        {
            // BP-9 (m14): ParseLeader accepts physical modifier keys (LControlKey/RControlKey/
            // LShiftKey/RShiftKey/LMenu/RMenu) as a leader — accepted yet non-functional (a lone
            // modifier key can never be a leader). The fix rejects them: the leader falls back to
            // Space. RED today: ParseLeader("LControlKey") returns Keys.LControlKey, so the leader
            // is not Space.
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"LControlKey\",\"bindings\":{}}").LeaderKey);
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"RControlKey\",\"bindings\":{}}").LeaderKey);
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"LShiftKey\",\"bindings\":{}}").LeaderKey);
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"RShiftKey\",\"bindings\":{}}").LeaderKey);
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"LMenu\",\"bindings\":{}}").LeaderKey);
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"RMenu\",\"bindings\":{}}").LeaderKey);
            // A non-modifier leader is unchanged: Space is accepted (and a non-default key like F1
            // proves the rejection is scoped to physical modifier keys, not a blanket rejection).
            Assert.Equal(Keys.Space,
                KeybindingConfig.LoadFromJson("{\"leader\":\"Space\",\"bindings\":{}}").LeaderKey);
            Assert.Equal(Keys.F1,
                KeybindingConfig.LoadFromJson("{\"leader\":\"F1\",\"bindings\":{}}").LeaderKey);
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
            // diff reader via Assert.NotEqual (n8/BP-30).
            Assert.NotEqual("command:Team.Git.Branches", cfg.Bindings["g,b"]);
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

        public static void Run_KeybindingConfig_Merge()
        {
            // BP-4 (m60): the defaults+user merge is the core of the user-config feature and is
            // untested. The real API is `Merge(IReadOnlyList<(string Json, string ErrorMessage)>)`
            // (private — invoked via reflection, the file's established pattern): later sources
            // override earlier ones. Coverage addition — passes against the current code.
            //   - Merge([defaults]) -> defaults only (the "no user file" case).
            //   - Merge([defaults, user]) -> the user override wins for the same sequence; a
            //     user-only binding is added; an unoverridden default survives.
            //   - Deterministic: same input -> same output.
            //   - m61 (BP-D14): a malformed source is logged (the [NeoVisual] keybinding-parse
            //     failure diagnostic) and skipped — the good sources' bindings survive the merge
            //     (the "one bad source never discards the others" contract).
            var merge = typeof(KeybindingConfig).GetMethod(
                "Merge",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.True(merge != null, "KeybindingConfig.Merge exists (private static)");

            const string defaultsJson = "{\"bindings\":{\"w\":\"command:Default\",\"f,t\":\"telescope\"}}";
            const string userJson = "{\"bindings\":{\"w\":\"command:User\",\"x\":\"user-only\"}}";
            const string malformedJson = "{not valid json";

            using (var dir = new TempDir())
            {
                string logPath = System.IO.Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    // Merge([defaults]) -> defaults only.
                    var defaultsOnly = (KeybindingConfig)merge!.Invoke(null, new object[]
                    {
                        new List<(string Json, string ErrorMessage)> { (defaultsJson, "defaults") },
                    })!;
                    Assert.Equal("command:Default", defaultsOnly.Bindings["w"]);
                    Assert.True(defaultsOnly.Bindings.ContainsKey("f,t"), "a default binding is present");
                    Assert.False(defaultsOnly.Bindings.ContainsKey("x"), "no user-only binding in defaults-only");

                    // Merge([defaults, user]) -> user override wins; user-only added; default survives.
                    var merged = (KeybindingConfig)merge.Invoke(null, new object[]
                    {
                        new List<(string Json, string ErrorMessage)>
                        {
                            (defaultsJson, "defaults"),
                            (userJson, "user"),
                        },
                    })!;
                    Assert.Equal("command:User", merged.Bindings["w"]);
                    Assert.Equal("telescope", merged.Bindings["f,t"]);
                    Assert.Equal("user-only", merged.Bindings["x"]);

                    // Deterministic: the same input produces the same merged set.
                    var merged2 = (KeybindingConfig)merge.Invoke(null, new object[]
                    {
                        new List<(string Json, string ErrorMessage)>
                        {
                            (defaultsJson, "defaults"),
                            (userJson, "user"),
                        },
                    })!;
                    Assert.Equal(merged.Bindings.Count, merged2.Bindings.Count);
                    Assert.Equal("command:User", merged2.Bindings["w"]);
                    Assert.Equal("user-only", merged2.Bindings["x"]);

                    // m61 (BP-D14): a malformed source is logged and skipped — the good sources'
                    // bindings survive the merge.
                    var mergedWithBad = (KeybindingConfig)merge.Invoke(null, new object[]
                    {
                        new List<(string Json, string ErrorMessage)>
                        {
                            (defaultsJson, "defaults"),
                            (userJson, "user"),
                            (malformedJson, "malformed"),
                        },
                    })!;
                    Assert.Equal("command:User", mergedWithBad.Bindings["w"]);
                    Assert.Equal("telescope", mergedWithBad.Bindings["f,t"]);
                    Assert.Equal("user-only", mergedWithBad.Bindings["x"]);

                    Telescope.Logging.LogFileWriter.Flush();
                    string content = ReadAllTextShared(logPath);
                    Assert.True(content.Contains("[NeoVisual] malformed:"),
                        "the malformed source logs the [NeoVisual] keybinding-parse failure");
                });
            }
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
            // Shift (lowercase = unshifted, uppercase = Shift+letter). BP-7 (m5): non-letter keys
            // map the SHIFTED printable characters (shift-aware), so a binding like `w,}` (Shift+])
            // fires distinctly from `w,]`. RED today: ToString(Keys.OemMinus, true) returns "-".
            Assert.Equal("f", KeyNames.ToString(Keys.F, false));
            Assert.Equal("F", KeyNames.ToString(Keys.F, true));
            Assert.Equal("g", KeyNames.ToString(Keys.G, false));
            Assert.Equal("G", KeyNames.ToString(Keys.G, true));
            Assert.Equal("-", KeyNames.ToString(Keys.OemMinus, false));
            Assert.Equal("_", KeyNames.ToString(Keys.OemMinus, true));
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

        public static void Run_KeyNames_RoundTrip_WindowPrefix_SplitBelow()
        {
            // Gap 1 (AC1): the w-prefix split-below binding round-trips through the matcher:
            // Space, w (prefix), then the physical OemMinus key builds the exact config sequence
            // "w,-" and fires.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,-"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            Assert.Equal(LeaderResultKind.Consume, matcher.HandleKey(Keys.Space, false, false, false, false).Kind);
            Assert.Equal(LeaderResultKind.Consume, matcher.HandleKey(Keys.W, false, false, false, false).Kind);
            var minus = matcher.HandleKey(Keys.OemMinus, false, false, false, false);
            Assert.Equal(LeaderResultKind.Execute, minus.Kind);
            Assert.Equal<string?>("w,-", minus.Sequence);
            Assert.Equal(1, executed);
        }

        public static void Run_KeyNames_RoundTrip_WindowPrefix_SplitRight()
        {
            // Gap 1 (AC2): the w-prefix split-right binding round-trips through the matcher:
            // Space, w (prefix), then Shift+OemPipe (types '|') builds the exact config sequence
            // "w,|" and fires.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,|"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            matcher.HandleKey(Keys.W, false, false, false, false);
            var pipe = matcher.HandleKey(Keys.OemPipe, false, true, false, false);
            Assert.Equal(LeaderResultKind.Execute, pipe.Kind);
            Assert.Equal<string?>("w,|", pipe.Sequence);
            Assert.Equal(1, executed);
        }

        public static void Run_KeyNames_RoundTrip_WindowPrefix_CloseWindow()
        {
            // Gap 1 (AC3): the w-prefix close-window binding round-trips through the matcher:
            // Space, w (prefix), then the physical d key builds the exact config sequence "w,d"
            // and fires.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,d"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            matcher.HandleKey(Keys.W, false, false, false, false);
            var close = matcher.HandleKey(Keys.D, false, false, false, false);
            Assert.Equal(LeaderResultKind.Execute, close.Kind);
            Assert.Equal<string?>("w,d", close.Sequence);
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

            // The diagnostic-nav sequences are UNSHIFTED: the unshifted bracket maps to ']'/'['
            // (the printable mapping). The SHIFTED bracket maps to '}'/'{' (m5/BP-7 — pinned by
            // Run_KeyNames_ShiftAwareNonLetter).
            Assert.Equal("]", KeyNames.ToString(Keys.OemCloseBrackets, false));
            Assert.Equal("[", KeyNames.ToString(Keys.OemOpenBrackets, false));
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
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.CommandWindow), "CommandWindow is text input");
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.ImmediateWindow), "ImmediateWindow is text input");
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.FindReplace), "FindReplace is text input");
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.WebBrowserWindow), "WebBrowserWindow is text input");
        }

        public static void Run_ToolWindowMode_NavigationTypesClassified()
        {
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.SolutionExplorer), "SolutionExplorer is navigation");
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.OutputWindow), "OutputWindow is navigation");
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.Toolbox), "Toolbox is navigation");
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

        public static void Run_HierarchyResolver_SingleCsFilter()
        {
            // BP-1 (m49): the `.cs` filter lives ONLY in FirstSourceFilePath (the single filter).
            // FirstSourceFilePath returns the .cs path; FirstPathMatching is extension-agnostic and
            // returns the non-.cs path. This pins the post-BP-1 contract (the forest is unfiltered,
            // so the resolver is the ONLY place the .cs filter applies). Contract-pinning — passes
            // against the current code (the filter already lives in FirstSourceFilePath).
            var resx = new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Form1.resx", @"C:\p\Form1.resx", null);
            var cs = new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Form1.cs", @"C:\p\Form1.cs", null);
            var json = new HierarchyNode(HierarchyResolver.PhysicalFileKind, "appsettings.json", @"C:\p\appsettings.json", null);

            Assert.Equal(@"C:\p\Form1.cs",
                HierarchyResolver.FirstSourceFilePath(new HierarchyNode[] { resx, cs, json }));
            Assert.Equal(@"C:\p\appsettings.json",
                HierarchyResolver.FirstPathMatching(new HierarchyNode[] { resx, cs, json }, "appsettings"));
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
            // BP-1 (M8/m49): the forest is UNFILTERED — the `.cs` filter moved out of Build into
            // HierarchyResolver.FirstSourceFilePath. The forest now contains BOTH the
            // uppercase-extension Program.CS and the non-.cs App.config (the OLD contract pinned
            // App.config's ABSENCE; the new contract keeps every physical item).
            // RED today: Build still filters to `.cs`, so App.config is dropped (Count = 1).
            var items = new[]
            {
                new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Program.CS", @"C:\p\Program.CS", null),
                new HierarchyNode(HierarchyResolver.PhysicalFileKind, "App.config", @"C:\p\App.config", null),
            };
            var forest = HierarchyForestBuilder.Build(items);

            Assert.Equal(2, forest.Count);
            Assert.Equal("Program.CS", forest[0].Name);
            Assert.Equal("App.config", forest[1].Name);
            Assert.Equal(HierarchyResolver.PhysicalFileKind, forest[0].Kind);
            Assert.Equal(HierarchyResolver.PhysicalFileKind, forest[1].Kind);
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

        public static void Run_HierarchyForestBuilder_Unfiltered()
        {
            // BP-1 (M8/m49): the forest must contain ALL physical items (.cs, .resx, .json, ...);
            // the `.cs` filter lives only in HierarchyResolver.FirstSourceFilePath. RED today:
            // HierarchyForestBuilder.Build filters to `.cs` only, so the .resx/.json items are
            // dropped (Count = 1, not 3).
            var items = new[]
            {
                new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Program.cs", @"C:\p\Program.cs", null),
                new HierarchyNode(HierarchyResolver.PhysicalFileKind, "Form1.resx", @"C:\p\Form1.resx", null),
                new HierarchyNode(HierarchyResolver.PhysicalFileKind, "appsettings.json", @"C:\p\appsettings.json", null),
            };
            var forest = HierarchyForestBuilder.Build(items);

            Assert.Equal(3, forest.Count);
            Assert.Equal("Program.cs", forest[0].Name);
            Assert.Equal("Form1.resx", forest[1].Name);
            Assert.Equal("appsettings.json", forest[2].Name);
            Assert.Equal(HierarchyResolver.PhysicalFileKind, forest[0].Kind);
            Assert.Equal(HierarchyResolver.PhysicalFileKind, forest[1].Kind);
            Assert.Equal(HierarchyResolver.PhysicalFileKind, forest[2].Kind);
        }

        public static void Run_SolutionExplorer_MapChildrenExpandsFolders()
        {
            // BP-2 (M8): MapChildren must set Expanded=true on each child folder BEFORE recursing —
            // a collapsed folder's UIHierarchyItems is empty, so `g` (SelectFirstSourceFile) only
            // sees top-level files + already-expanded folders today. With the expansion, `g`
            // reaches files in collapsed folders.
            // RED today: MapChildren is `private static` -> compile error (CS0122, the internal
            // seam does not exist yet); once internal, the behavior fails too (Expanded stays
            // false, so the collapsed folder's items are never enumerated).
            var innerFile = new FakeUIHierarchyItem(
                DispatchProjectItem.Create(HierarchyResolver.PhysicalFileKind, "Inner.cs", new[] { @"C:\p\Models\Inner.cs" }),
                new FakeUIHierarchyItems(new List<EnvDTE.UIHierarchyItem>()));
            var folderItem = new FakeUIHierarchyItem(
                DispatchProjectItem.Create(HierarchyResolver.PhysicalFolderKind, "Models", new string[0]),
                new FakeUIHierarchyItems(new List<EnvDTE.UIHierarchyItem> { innerFile })); // collapsed
            var topFile = new FakeUIHierarchyItem(
                DispatchProjectItem.Create(HierarchyResolver.PhysicalFileKind, "Top.cs", new[] { @"C:\p\Top.cs" }),
                new FakeUIHierarchyItems(new List<EnvDTE.UIHierarchyItem>()));
            var rootItem = new FakeUIHierarchyItem(
                DispatchProjectItem.Create(HierarchyResolver.PhysicalFolderKind, "Project", new string[0]),
                new FakeUIHierarchyItems(new List<EnvDTE.UIHierarchyItem> { folderItem, topFile }));
            rootItem.UIHierarchyItems.Expanded = true; // the caller expands the project node first

            var pathToItem = new Dictionary<string, EnvDTE.UIHierarchyItem>(StringComparer.OrdinalIgnoreCase);
            var result = SolutionExplorerController.MapChildren(rootItem, pathToItem);

            // The child folder must be expanded so its items are enumerated (BP-2).
            Assert.True(folderItem.UIHierarchyItems.Expanded,
                "a child folder is expanded before recursion (BP-2)");
            // The folder's .cs child is enumerated into the forest.
            var folderNode = result.Single(n => n.Name == "Models");
            Assert.Equal(1, folderNode.Children!.Count);
            Assert.Equal("Inner.cs", folderNode.Children![0].Name);
            // The top-level file is still mapped.
            Assert.True(result.Any(n => n.Name == "Top.cs"), "the top-level file is mapped");
        }

        // ================================================================
        // TextInputToolWindowController — vim text motions in text-input windows
        // ================================================================

        public static void Run_TextInput_StartsInInsertMode()
        {
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            Assert.True(controller.IsInputMode, "text-input windows start in insert mode");
            // n24 (BP-D22): the exact action-key set, not just Count > 0 — a wrong-but-nonempty
            // set fails. The CommandWindow controller exposes {A,H,L,I,J,K,D0,D4,W,B,E} (the
            // shared text-motion wiring + the n7/BP-24 j/k/0/$ additions).
            var expected = new HashSet<Keys>
            {
                Keys.A, Keys.H, Keys.L, Keys.I, Keys.J, Keys.K, Keys.D0, Keys.D4,
                Keys.W, Keys.B, Keys.E,
            };
            var actual = new HashSet<Keys>(controller.ActionKeys);
            Assert.Equal(expected.Count, actual.Count, "the exact action-key set size");
            foreach (var key in expected)
            {
                Assert.True(actual.Contains(key), $"ActionKeys contains {key}");
            }
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
            // m46 (BP-16): Apply reports AfterCaret for InsertAfter (matching PromptMotionRouter).
            Assert.Equal(CaretPlacement.AfterCaret, after);
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

        public static void Run_TextMotionHelper_SanitizeUsesShared()
        {
            // BP-25 (m34): TextMotionHelper.SanitizeSample (TextMotionHelper.cs:277-290) is
            // byte-identical to the shared DiagnosticLog.SanitizeText (DiagnosticLog.cs:22-40) —
            // never migrated. The fix deletes SanitizeSample and routes the call site (the private
            // Sample(string)/Sample(ITextSnapshot) helpers at TextMotionHelper.cs:264/:272) through
            // DiagnosticLog.SanitizeText. The call site is PRIVATE, so this test pins the SHARED
            // sanitization contract directly (the surviving method); the compile-RED is the
            // production-side residual SanitizeSample reference failing the build. The build-agent
            // must route the call site through DiagnosticLog.SanitizeText (NOT re-copy the
            // sanitizer). Diagnostic: [NeoVisual] text-motion key=... caret=... UNCHANGED.
            //
            // A clean string passes through byte-identical (no control chars -> the original is
            // returned, not a copy).
            Assert.Equal("some sample text", DiagnosticLog.SanitizeText("some sample text"));

            // Control characters are replaced with a single space each (R39 — the sample must never
            // split the log line): newline, tab, and leading/trailing control chars.
            Assert.Equal("a b", DiagnosticLog.SanitizeText("a\nb"));
            Assert.Equal("a b", DiagnosticLog.SanitizeText("a\tb"));
            Assert.Equal(" b", DiagnosticLog.SanitizeText("\nb"));
            Assert.Equal("a ", DiagnosticLog.SanitizeText("a\n"));

            // Multiple control chars each become a space — no collapsing.
            Assert.Equal("a  b", DiagnosticLog.SanitizeText("a\n\tb"));

            // Null/empty -> empty string (the shared method's null contract; SanitizeSample had
            // none — the migrated call site inherits it).
            Assert.Equal(string.Empty, DiagnosticLog.SanitizeText(null));
            Assert.Equal(string.Empty, DiagnosticLog.SanitizeText(string.Empty));
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

        public static void Run_VimBufferSubscriptions_NoLeak()
        {
            // BP-7 (m12): the _bufferToTextBuffer entry must be removed when the shared-text-buffer
            // refcount reaches 0 — a detached view sharing a text buffer must not leave a stale
            // entry for the whole session. Two views share one buffer + text buffer: detach one
            // (2 -> 1, the entry stays — the buffer is still live), detach the second (1 -> 0, the
            // entry is removed). The map is private, so inspect it via reflection (the existing
            // _DetachRemovesMapEntryAtZero pattern).
            var subs = new VimBufferSubscriptions();
            var viewA = new FakeTextView();
            var viewB = new FakeTextView();
            var buffer = new object();
            var textBuffer = new object();

            subs.Attach(viewA, buffer, textBuffer);
            subs.Attach(viewB, buffer, textBuffer);
            subs.MarkClosedSubscribed(buffer);

            // Detaching one of two views is not the last ref (2 -> 1).
            Assert.False(subs.Detach(viewA), "detaching one of two views is not the last ref (2 -> 1)");

            var field = typeof(VimBufferSubscriptions).GetField(
                "_bufferToTextBuffer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.True(field != null, "VimBufferSubscriptions._bufferToTextBuffer must exist");
            var map = (System.Collections.IDictionary)field!.GetValue(subs)!;
            Assert.True(map.Contains(buffer), "the buffer is still live at refcount 1 (entry stays)");

            // Detaching the last view drops the refcount to 0.
            Assert.True(subs.Detach(viewB), "detaching the last view drops the refcount to 0");
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
            // CR1 + n7 (BP-24): the exact action-key set is {A,H,L,I,J,K,D0,D4,W,B,E} (count 11) —
            // the text-input controller registers A/H/L/I + J/K/D0/D4 (the Command Window gains
            // 0/$ like the search box) plus the shared W/B/E word motions (AddTextMotionKeys).
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            var expected = new[] { Keys.A, Keys.H, Keys.L, Keys.I, Keys.J, Keys.K, Keys.D0, Keys.D4, Keys.W, Keys.B, Keys.E };
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
            // map to InsertStart (Shift+i). n23 (BP-D21): the MotionForActionKey seam reads the
            // shared action-table -> motion mapping directly (no TextMotionHelper re-derivation).
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            var keys = new List<Keys>(controller.ActionKeys);
            Assert.True(keys.Contains(Keys.I), "I is an action key (CR1)");
            // The pure mapping the I action applies: Shift+I -> InsertStart.
            Assert.Equal(TextMotion.InsertStart, controller.MotionForActionKey(Keys.I));
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
            // m63 (BP-D16): reset the static singleton so a prior test's injected state cannot
            // leak into this test (order-independent).
            InjectedKeyGuard.Instance.Reset();
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
        }

        public static void Run_FocusGuard_TextInputSurfaceFocused_GenuinelyFocusedOwnsKeyboard()
        {
            // D4 preservation: a text-input surface that genuinely holds focus owns the keyboard
            // even when the editor-focus flag is stale.
            Assert.True(
                FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: true, isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: true),
                "genuinely-focused text-input surface action keys are interesting");
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
            Assert.Equal(FocusKeeperSchedule.Decision.InjectEscape, FocusKeeperSchedule.Decide(true, 0, 0, 1500, editorFocused: false));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop, FocusKeeperSchedule.Decide(true, 0, 4, 1500, editorFocused: false));
            Assert.Equal(FocusKeeperSchedule.Decision.Reassert, FocusKeeperSchedule.Decide(false, 0, 0, 1500, editorFocused: false));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop, FocusKeeperSchedule.Decide(false, 1500, 0, 1500, editorFocused: false));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop, FocusKeeperSchedule.Decide(true, 1500, 0, 1500, editorFocused: false));
        }

        public static void Run_FocusKeeperSchedule_StopsAfterMaxEscapeAttempts()
        {
            // N26 (BP-32): after MaxEscapeAttempts (4) with the search box STILL focused, the
            // keeper must Stop (not Reassert) — it must not fight the user. RED today: Decide
            // returns Reassert for the 4-attempt case.
            Assert.Equal(FocusKeeperSchedule.Decision.InjectEscape,
                FocusKeeperSchedule.Decide(searchBoxFocused: true, elapsedMs: 0, escapeAttempts: 3, durationMs: 1500, editorFocused: false));
            Assert.Equal(FocusKeeperSchedule.Decision.Stop,
                FocusKeeperSchedule.Decide(searchBoxFocused: true, elapsedMs: 0, escapeAttempts: 4, durationMs: 1500, editorFocused: false));
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

        public static void Run_VimModeSource_OnBufferClosedUsesCachedBuffer()
        {
            // BP-8 (m13): OnBufferClosed (VimModeSource.cs:170-188) re-reads get_VimTextBuffer via
            // reflection (GetTextBuffer at :398-415) on a CLOSING buffer — a reflection failure on
            // the teardown path makes GetTextBuffer return null, so the
            // `if (textBuffer != null && lastView)` block is skipped and the SwitchedMode
            // subscription LEAKS (RemoveSwitchedMode never runs). The fix uses the cached
            // _bufferToTextBuffer value instead of re-resolving via reflection on the closing
            // buffer.
            // RED today: the fake buffer's get_VimTextBuffer throws on the close-path re-read, so
            // GetTextBuffer returns null and _subscribedTextBuffers still contains the text buffer
            // (the subscription leaks) -> the Assert.False fails.
            var source = new VsVimModeSource();
            var view = new FakeTextView();
            var textBuffer = new object(); // the cached IVimTextBuffer (a plain object is enough)
            var buffer = new ClosingVimBuffer(textBuffer); // get_VimTextBuffer: 1st call OK, then throws

            // Populate the real subscription state hermetically: the subscriptions map (buffer ->
            // textBuffer + Closed-subscribed) and the SwitchedMode subscription set via the real
            // SubscribeBuffer path. Reflection is the only hermetic route into the private members
            // (the established pattern in this file).
            var subsField = typeof(VsVimModeSource).GetField(
                "_subscriptions", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(subsField != null, "VsVimModeSource._subscriptions must exist");
            var subs = (VimBufferSubscriptions)subsField!.GetValue(source)!;
            subs.Attach(view, buffer, textBuffer);
            subs.MarkClosedSubscribed(buffer);

            var subscribe = typeof(VsVimModeSource).GetMethod(
                "SubscribeBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(subscribe != null, "VsVimModeSource.SubscribeBuffer must exist");
            subscribe!.Invoke(source, new object[] { buffer, true });

            var subscribedField = typeof(VsVimModeSource).GetField(
                "_subscribedTextBuffers", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(subscribedField != null, "VsVimModeSource._subscribedTextBuffers must exist");
            var subscribed = (HashSet<object>)subscribedField!.GetValue(source)!;
            Assert.True(subscribed.Contains(textBuffer),
                "setup: the SwitchedMode subscription is active before close");

            // Drive the close path. The fake's get_VimTextBuffer now THROWS (the buffer is closing).
            var onBufferClosed = typeof(VsVimModeSource).GetMethod(
                "OnBufferClosed", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(onBufferClosed != null, "VsVimModeSource.OnBufferClosed must exist");

            Exception? thrown = null;
            try
            {
                onBufferClosed!.Invoke(source, new object[] { buffer, EventArgs.Empty });
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
            Assert.True(thrown == null,
                "OnBufferClosed must not throw on a closing buffer (the cached text buffer is used)");
            Assert.False(subscribed.Contains(textBuffer),
                "the SwitchedMode subscription must be removed on close (cached value used, no reflection re-read)");
        }

        // ================================================================
        // Actions — ActionRegistry (ResolveAction via a registry, BP-4/C3)
        // RED: `Actions` / `TelescopeLauncher` do not exist yet -> compile error
        // ================================================================

        public static void Run_ActionsRegistry_ContainsAllBuiltins()
        {
            // The registry must hold exactly the 18 built-in action names, kept in sync with
            // default-keybindings.json (the hand-sync bug this seam removes). Gap 1 (AC3/D5)
            // added the focus-aware "close-window" action; Gap 3 (AC3-AC5/D4) adds the four
            // severity-filtered diagnostic-nav actions; Gap 6 adds the derived
            // "telescope-definition" action (the Definition finder); Gap 4 adds the derived
            // "telescope-recent" action (the Recent finder).
            Assert.Equal(18, Actions.Registry.Count);
            var names = new[]
            {
                "navigate-left", "navigate-right", "navigate-up", "navigate-down",
                "telescope", "telescope-issues", "telescope-references",
                "telescope-implementation", "telescope-grep", "telescope-fzf",
                "telescope-definition", "telescope-recent",
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
        // RED: Right/Bottom/IsEmpty + Axis/Direction don't exist -> compile error
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
        // [minGap, minGap+divide], last-wins ties, and the M7 (BP-13) STRICT edge
        // semantics — Down accepts a candidate strictly below the active's bottom
        // (c.Y > a.Bottom), Up strictly above the active's top (c.Bottom < a.Y);
        // no tolerance.
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

        // BP-14 (M11): Run_WindowNavigationEngine_Down_ToleranceExcludes was DELETED — it passed
        // vacuously (the >1 tolerance was removed in 349fc05, so both candidates pass the direction
        // filter and the assertion is satisfied by the last-wins tie-break). The real contract is
        // pinned by the rewritten Run_WindowNavigationEngine_Down_OnePixelGapAccepted (BP-13) +
        // Run_WindowNavigationEngine_Down_BelowBottom (BP-13).

        public static void Run_WindowNavigationEngine_Down_OnePixelGapAccepted()
        {
            // M7 (BP-13): the Down/Up edge semantics are SYMMETRIC and STRICT — a candidate must be
            // strictly beyond the active's edge. Up accepts a candidate strictly above the active's
            // top (c.Bottom < a.Y); Down accepts a candidate strictly below the active's bottom
            // (c.Y > a.Bottom). (The old assertions pinned the pre-M7 `c.Y > a.Y` / `c.Y < a.Y`
            // semantics and were rewritten in this step.)
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100); // Bottom=200

            // Up: c.Y=49, Height=50 -> Bottom=99 < a.Y=100 (strictly above the top).
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active,
                new[] { new WindowRect(100, 49, 100, 50) }, Direction.Up, settings));

            // Down: c.Y=201 > a.Bottom=200 (strictly below the bottom).
            Assert.Equal(0, WindowNavigationEngine.SelectTarget(active,
                new[] { new WindowRect(100, 201, 100, 50) }, Direction.Down, settings));
        }

        public static void Run_WindowNavigationEngine_Down_BelowBottom()
        {
            // M7 (BP-13): Down must target the TRULY-below window — a partially-overlapping window
            // (a.Y < c.Y < a.Bottom) is excluded by the strict `c.Y > a.Bottom` direction filter.
            // RED today: the overlapping window wins on adjacency -> returns 0.
            var settings = NavigationSettings.FromDpi(96, 96);
            var active = new WindowRect(100, 100, 100, 100); // Bottom=200
            var candidates = new[]
            {
                new WindowRect(100, 150, 100, 100), // overlapping: Y=150 < Bottom=200, gap -50, adjacency 100
                new WindowRect(100, 250, 50, 50),   // truly below: Y=250 > Bottom=200, gap 50, adjacency 50
            };
            Assert.Equal(1, WindowNavigationEngine.SelectTarget(active, candidates, Direction.Down, settings));
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
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                Assert.Equal(null, WindowFrameAdapter.TryGetScreenRect(null));
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
            // BP-6 (m4): only Shift (ShiftKey/LShiftKey/RShiftKey) is transparent while a sequence
            // is active — needed for shifted sequence members like w,|. A Ctrl/Alt/Win key-down
            // ABORTS the sequence (clears it) so the modifier passes through to VS (the Ctrl+chord
            // works). RED today: every modifier is consumed as transparent, so the abort keys fail.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,|"] = () => executed++,
            };
            var shiftKeys = new[]
            {
                Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
            };
            var abortKeys = new[]
            {
                Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
                Keys.Menu, Keys.LMenu, Keys.RMenu,
                Keys.LWin, Keys.RWin,
            };

            foreach (var modifier in shiftKeys)
            {
                var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);
                matcher.HandleKey(Keys.Space, false, false, false, false);
                matcher.HandleKey(Keys.W, false, false, false, false);

                var down = matcher.HandleKey(modifier, false, false, false, false);
                Assert.Equal(LeaderResultKind.Consume, down.Kind);
                Assert.True(matcher.IsActive, $"{modifier} key-down must not abort the pending sequence");

                // The sequence is untouched: the shifted member still completes the binding.
                var pipe = matcher.HandleKey(Keys.OemPipe, false, true, false, false);
                Assert.Equal(LeaderResultKind.Execute, pipe.Kind);
                Assert.Equal<string?>("w,|", pipe.Sequence);
            }

            foreach (var modifier in abortKeys)
            {
                var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);
                matcher.HandleKey(Keys.Space, false, false, false, false);
                matcher.HandleKey(Keys.W, false, false, false, false);

                var down = matcher.HandleKey(modifier, false, false, false, false);
                Assert.Equal(LeaderResultKind.Abort, down.Kind);
                Assert.False(matcher.IsActive, $"{modifier} key-down must abort the pending sequence (m4)");
            }
            Assert.Equal(shiftKeys.Length, executed);
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

        public static void Run_LeaderSequenceMatcher_NoDeadAction()
        {
            // BP-14 (m37): `LeaderResult.Action` (LeaderSequenceMatcher.cs:171) is write-only —
            // the matcher already invoked the action, and HandleKey never reads result.Action.
            // The fix deletes the member + the assignment. This test pins the SURVIVING contract:
            // the matcher still invokes the action exactly once on a matched sequence, and the
            // result still carries the Kind/Sequence members. It deliberately does NOT reference
            // `result.Action` — a residual reference would fail the build (compile-RED) once the
            // member is deleted.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["s,g"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            matcher.HandleKey(Keys.S, false, false, false, false);
            var result = matcher.HandleKey(Keys.G, false, false, false, false);

            Assert.Equal(LeaderResultKind.Execute, result.Kind);
            Assert.Equal<string?>("s,g", result.Sequence);
            Assert.Equal(1, executed);
            Assert.False(matcher.IsActive, "sequence ends after execution");
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

        // BP-5 (M3): a bound Shift+ chord is recognized by the pre-filter (IsBoundShiftChord) so
        // it reaches HandleKey; an unbound uppercase letter stays cheap. COMPILE-RED:
        // SimpleShortcutMatcher.IsBoundShiftChord does not exist yet -> CS1061.
        public static void Run_SimpleShortcutMatcher_IsBoundShiftChord()
        {
            var bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["Shift+F4"] = () => { },
            };
            var matcher = new SimpleShortcutMatcher(bindings);

            Assert.True(matcher.IsBoundShiftChord(Keys.F4), "a bound Shift+F4 chord is recognized");
            Assert.False(matcher.IsBoundShiftChord(Keys.A), "an unbound uppercase letter is not a bound shift chord");
        }

        // ================================================================
        // InputHandler.IsKeyOfInterest — the overlay-open short-circuit (BP-6 / m3; seam-driven
        // per BP-D15 / m59)
        // RED: `TelescopeController.SetOverlayOpenForTest(bool)` (the internal test seam) and
        // `InputHandler(TelescopeController, WindowManager)` (the test-only ctor) do not exist
        // yet -> compile errors (CS0117 + CS1729). The m59 defect: the test drove IsOpen via
        // FormatterServices.GetUninitializedObject + reflection into _overlay/TelescopeOverlay.IsOpen
        // and the InputHandler's private fields; the fix adds the seam so IsOpen is drivable
        // hermetically and the real ctor sets the leader fields (no reflection).
        // ================================================================

        public static void Run_IsKeyOfInterest_OverlayOpenShortCircuit()
        {
            // SEAMS THE BUILD-AGENT MUST CREATE (documented here so the ctor/fields compile):
            //   1. internal void TelescopeController.SetOverlayOpenForTest(bool open) — sets the
            //      private _overlay to a minimal non-WPF overlay stub (a private nested FakeOverlay
            //      with IsOpen => true) or null, so IsOpen is drivable hermetically without
            //      FormatterServices.GetUninitializedObject or reflection into _overlay/
            //      TelescopeOverlay.IsOpen.
            //   2. internal InputHandler(TelescopeController telescope, WindowManager windowManager)
            //      — the same test-only ctor as Run_InputHandler_NoPerKeySentinelRead (skips the
            //      VS-coupled parts: ResolveVimModeTracker MEF + KeybindingConfig.Load). It must set
            //      _leaderMatcher = new LeaderSequenceMatcher(Keys.Space, empty bindings),
            //      _simpleMatcher = new SimpleShortcutMatcher(empty bindings), _leaderKey = Keys.Space,
            //      _lastSentinelRefresh, and _vsVim = new VimModeTracker() so IsKeyOfInterest's
            //      closed-overlay path is hermetic (no reflection).

            // WindowManager is VS-coupled (IVsMonitorSelection + ThreadHelper.ThrowIfNotOnUIThread
            // in the ctor), so construct it hermetically with the FakeMonitorSelection + a
            // UI-thread dispatcher (the Run_WindowManager_* pattern).
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {

                var telescope = new TelescopeController();
                var handler = new InputHandler(telescope, new WindowManager(new FakeMonitorSelection()));

                // Overlay open: the modal overlay owns all keys — every key is swallowed (BP-6).
                telescope.SetOverlayOpenForTest(true);
                Assert.True(telescope.IsOpen, "the seam must report IsOpen=true");
                Assert.False(handler.IsKeyOfInterest(Keys.H, true, false, false), "a Ctrl chord while the overlay is open");
                Assert.False(handler.IsKeyOfInterest(Keys.Space, false, false, false), "the leader key while the overlay is open");
                Assert.False(handler.IsKeyOfInterest(Keys.Escape, false, false, false), "Escape while the overlay is open");

                // Overlay closed: the same keys are interesting (the full pre-filter runs).
                telescope.SetOverlayOpenForTest(false);
                Assert.False(telescope.IsOpen, "the seam must report IsOpen=false");
                Assert.True(handler.IsKeyOfInterest(Keys.H, true, false, false), "a Ctrl chord while the overlay is closed");
                Assert.True(handler.IsKeyOfInterest(Keys.Space, false, false, false), "the leader key while the overlay is closed");
                Assert.True(handler.IsKeyOfInterest(Keys.Escape, false, false, false), "Escape while the overlay is closed");
            }
        }

        // ================================================================
        // InputHandler sentinel clock — BP-6 (m11): no per-key DateTime.UtcNow read
        // RED: `InputHandler(TelescopeController, WindowManager)` (test-only ctor) does not
        // exist yet -> compile error (CS1729). The m11 bug: InputHandler.cs:481-486 reads
        // DateTime.UtcNow on every key-down for the 250ms sentinel interval — a no-op in
        // production (the sentinel is only meaningful in tests). The fix guards the read
        // behind a _sentinelArmed flag (set by the test seam), so a disarmed sentinel never
        // touches the clock.
        // ================================================================

        public static void Run_InputHandler_NoPerKeySentinelRead()
        {
            // SEAMS THE BUILD-AGENT MUST CREATE (documented here so the ctor/fields compile):
            //   1. internal InputHandler(TelescopeController telescope, WindowManager windowManager)
            //      — a test-only ctor that skips the VS-coupled parts (ResolveVimModeTracker MEF +
            //      KeybindingConfig.Load). It must tolerate a NULL telescope (the test passes
            //      null!): substitute a fresh TelescopeController whose IsOpen is false, or
            //      null-guard the _telescope.IsOpen read, so IsKeyOfInterest can be driven
            //      hermetically.
            //   2. internal Func<DateTime> Clock — the clock seam, defaulting to
            //      () => DateTime.UtcNow (the production read at :481).
            //   3. internal void SetSentinelArmedForTest(bool armed) — sets the _sentinelArmed
            //      flag the fix guards the sentinel block behind.

            // WindowManager is VS-coupled (IVsMonitorSelection + ThreadHelper.ThrowIfNotOnUIThread
            // in the ctor), so construct it hermetically with the FakeMonitorSelection + a
            // UI-thread dispatcher (the Run_WindowManager_* pattern).
            // BP-3 (M1): explicitly unset NEOVISUAL_LOG_DIR so the disarmed default assertion stays
            // hermetic (the sentinel is armed from the env var in production).
            string original = Environment.GetEnvironmentVariable("NEOVISUAL_LOG_DIR");
            try
            {
                Environment.SetEnvironmentVariable("NEOVISUAL_LOG_DIR", null);
                using (TestScaffold.SetCurrentDispatcherAsUiThread())
                {
                    var handler = new InputHandler(null!, new WindowManager(new FakeMonitorSelection()));

                    // Counting clock seam: every read increments the counter.
                    int clockReads = 0;
                    handler.Clock = () => { clockReads++; return DateTime.UtcNow; };

                    // Sentinel DISARMED (the production default): N key-downs must never read the
                    // clock. RED today: the unguarded read at :481 fires once per key-down.
                    for (int i = 0; i < 5; i++)
                    {
                        handler.IsKeyOfInterest(Keys.H, true, false, false);
                    }
                    Assert.Equal(0, clockReads);

                    // Sentinel ARMED (the test seam): the clock IS read (the sentinel block runs).
                    handler.SetSentinelArmedForTest(true);
                    handler.IsKeyOfInterest(Keys.H, true, false, false);
                    Assert.True(clockReads > 0, "the clock must be read when the sentinel is armed");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("NEOVISUAL_LOG_DIR", original);
            }
        }

        // ================================================================
        // InputHandler ShouldRouteToolWindowKey — BP-12 (m35): collapse the two near-identical
        // overloads to the single controller overload.
        // RED: `InputHandler(TelescopeController, WindowManager)` (test-only ctor) does not exist
        // yet -> compile error (CS1729). The m35 bug: InputHandler.cs:115-135 has two near-identical
        // ShouldRouteToolWindowKey overloads (the no-arg at :115-119 and the controller overload at
        // :128-135). The fix deletes the no-arg overload and updates :547 to
        // `ShouldRouteToolWindowKey(_windowManager.CurrentController)`. This test pins the SURVIVING
        // controller overload's behavior through the public IsKeyOfInterest surface (which calls
        // ShouldRouteToolWindowKey(c) at :523): a tool-window state routes the key, a non-tool-window
        // state does not, and an editor-focused tool window whose controller does not own the
        // keyboard is vetoed.
        // ================================================================

        public static void Run_InputHandler_ShouldRouteToolWindowKey_SingleOverload()
        {
            // m60 (BP-D13): the reflection is gone — the test-only ctor initializes the leader
            // matcher state, SetToolWindowStateForTest forces the frame-derived tool-window state,
            // and SetEditorFocusedForTest drives the editor-focus veto (no private-field reads).

            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var manager = new WindowManager(new FakeMonitorSelection());
                var tracker = new VimModeTracker();
                var handler = new InputHandler(null!, manager, tracker);

                // NON-tool-window state (the FakeMonitorSelection yields a null frame, so
                // _isToolWindow is false and CurrentController is null): the surviving controller
                // overload must NOT route the key.
                Assert.False(handler.IsKeyOfInterest(Keys.H, false, false, false),
                    "no tool window -> the controller overload must not route H");

                // TOOL-window state: force the frame-derived state to a Toolbox (a non-text-input
                // type with a default GeneralToolWindowController in normal mode) and re-run the
                // same key. Editor not focused (the default) -> the controller overload routes it.
                manager.SetToolWindowStateForTest(true, ToolWindowType.Toolbox);
                Assert.True(handler.IsKeyOfInterest(Keys.H, false, false, false),
                    "a focused tool window (editor not focused) -> the controller overload must route H");

                // EDITOR-FOCUSED veto: the same tool-window state with an editor focused and a
                // controller that does not own the keyboard (not input mode, not a focused
                // text-input surface) -> the controller overload must NOT route the key.
                tracker.SetEditorFocusedForTest(true);
                Assert.False(handler.IsKeyOfInterest(Keys.H, false, false, false),
                    "editor focused + controller not owning the keyboard -> the controller overload must veto H");
            }
        }

        // ================================================================
        // InputHandler shift gate — BP-13 (m36): the production shift gate routes through the
        // pure 7-arg FocusGuard.ShouldRouteToolWindowKey overload (FocusGuard.cs:50-52).
        // RED: `InputHandler(TelescopeController, WindowManager)` (test-only ctor) does not
        // exist yet -> compile error (CS1729). The m36 bug: InputHandler.cs:422 inlines the R10
        // shift gate while the pure 7-arg overload is now TEST-ONLY. The fix routes the
        // production shift gate through the pure overload (deletes the inlined copy) so the
        // shift logic is single-sourced. This test drives IsKeyOfInterest with shift held and
        // asserts the pure overload's shift-gate behavior: a non-text-input surface must NOT
        // route a shift+key (R10 — Shift+O/R/M/A/G in Solution Explorer must not fire tree
        // actions), a text-input surface must (I/i and A/a must stay distinguishable).
        // ================================================================

        public static void Run_InputHandler_ShiftGateUsesPureOverload()
        {
            // m60 (BP-D13): the reflection is gone — the test-only ctor initializes the leader /
            // simple-matcher / sentinel state, and SetToolWindowStateForTest forces the frame-
            // derived tool-window state (no private-field reads).

            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var manager = new WindowManager(new FakeMonitorSelection());
                var handler = new InputHandler(null!, manager);

                // NON-TEXT-INPUT tool window (Toolbox -> a GeneralToolWindowController in normal
                // mode; the manager reports no text-input surface).
                manager.SetToolWindowStateForTest(true, ToolWindowType.Toolbox);

                // No shift: the key routes (the shift gate only blocks when shift is held).
                Assert.True(handler.IsKeyOfInterest(Keys.H, false, false, false),
                    "no shift + a non-text-input tool window -> the key routes");

                // Shift held: the pure overload's shift gate blocks a non-text-input surface (R10).
                // RED today: the :523 call uses the 3-arg overload (no shift gate) -> returns true.
                Assert.False(handler.IsKeyOfInterest(Keys.H, false, true, false),
                    "shift + a non-text-input tool window -> the pure overload's shift gate blocks (R10)");

                // TEXT-INPUT surface (the same Toolbox controller, but the manager reports a
                // text-input surface with a genuinely focused text box): the pure overload's shift
                // gate exempts it (I/i and A/a must stay distinguishable), so the key still routes.
                manager.SetToolWindowStateForTest(true, ToolWindowType.Toolbox, isTextInputType: true, textInputSurfaceFocused: true);
                Assert.True(handler.IsKeyOfInterest(Keys.H, false, true, false),
                    "shift + a text-input surface -> the pure overload's shift gate exempts it");
            }
        }

        // ================================================================
        // InputHandler single controller resolution — BP-15 (m38): TryRouteToolWindowKey resolves
        // CurrentController ONCE per key. RED: `InputHandler(TelescopeController, WindowManager)`
        // (test-only ctor) does not exist yet -> compile error (CS1729). The m38 bug:
        // InputHandler.cs:398,403 resolves CurrentController twice per key — :398 `_routeDecision()`
        // invokes the lambda `() => ShouldRouteToolWindowKey(_windowManager.CurrentController)`
        // (resolves CurrentController once), then :403 `var controller = _windowManager.CurrentController;`
        // resolves it AGAIN. The fix resolves CurrentController ONCE at the top of
        // TryRouteToolWindowKey and passes it to both the routing decision and the controller
        // variable — the `_routeDecision` seam signature changes from `Func<bool>` to
        // `Func<IToolWindowController?, bool>`. This test replaces the seam with a counting lambda
        // of the NEW signature and asserts one TryRouteToolWindowKey call hands the resolution to
        // the routing decision exactly once.
        // ================================================================

        public static void Run_InputHandler_SingleControllerResolution()
        {
            // m60 (BP-D13): the reflection is gone — SetToolWindowStateForTest forces the tool-
            // window state, SetRouteDecisionForTest replaces the routing seam, and the internal
            // TryRouteToolWindowKey is invoked directly (no private-field/method reads).

            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var manager = new WindowManager(new FakeMonitorSelection());
                var handler = new InputHandler(null!, manager);

                // Force a tool-window state (Toolbox -> a GeneralToolWindowController in normal mode).
                manager.SetToolWindowStateForTest(true, ToolWindowType.Toolbox);

                // The counting seam: the NEW signature (Func<IToolWindowController?, bool>) — the
                // seam receives the controller TryRouteToolWindowKey resolved ONCE at the top. The
                // lambda counts how many times the resolution is handed to the routing decision.
                int resolutions = 0;
                handler.SetRouteDecisionForTest(c => { resolutions++; return true; });

                // Route a key through the internal TryRouteToolWindowKey. The seam returns true
                // (route), so the method proceeds past the decision to the controller variable. X
                // is not a motion and not an action key, so TryMove is never reached (no injection).
                handler.TryRouteToolWindowKey(Keys.X, false, false, false);

                Assert.Equal(1, resolutions);
            }
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

        // ================================================================
        // BP-30 (C1): the common extra VsVim modes must emit NAMED tokens, not numerics —
        // Command=3, Visual=4, VisualBlock=5, Select=6 (Vim.Core ModeKind). RED: today they fall
        // through to the numeric fallback (Classify(4).Name == "4").
        // ================================================================

        public static void Run_VimModeClassifier_ExtraModes()
        {
            Assert.Equal("Command", VimModeClassifier.Classify(3).Name);
            Assert.Equal("Visual", VimModeClassifier.Classify(4).Name);
            Assert.Equal("VisualBlock", VimModeClassifier.Classify(5).Name);
            Assert.Equal("Select", VimModeClassifier.Classify(6).Name);
        }

        // ================================================================
        // BP-30 (C1): focus loss must emit ONLY the documented `Unknown` token (never a numeric or
        // a stale mode name). Guard — pins already-correct behavior.
        // ================================================================

        public static void Run_VimModeState_FocusLoss()
        {
            var state = new VimModeState();
            state.SetMode(VimModeClassifier.Insert);
            state.OnViewLostFocus(isFocusedView: true);
            Assert.Equal("Unknown", state.ModeName);
            Assert.False(state.IsTyping);
        }

        // ================================================================
        // COMPILE-RED tests — the test DEFINES the contract the build-agent must implement.
        // Each references a NEW API that does not exist yet (the missing symbol is the RED).
        // ================================================================

        // BP-17 (A7): IsTextInputType must derive from a single classification source — a
        // ToolWindowTypeResolver-owned classification table (not the hardcoded switch that must
        // be manually kept in sync with the enum). COMPILE-RED: ToolWindowTypeResolver.IsTextInputType
        // does not exist yet -> CS0117.
        public static void Run_IsTextInputType_Classification()
        {
            // Every text-input type resolves consistently through the single classification source.
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.CommandWindow), "CommandWindow is text input");
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.ImmediateWindow), "ImmediateWindow is text input");
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.FindReplace), "FindReplace is text input");
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.WebBrowserWindow), "WebBrowserWindow is text input");
            Assert.True(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.StartPage), "StartPage is text input");
            // Navigation types are NOT text input.
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.SolutionExplorer), "SolutionExplorer is navigation");
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.OutputWindow), "OutputWindow is navigation");
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.Toolbox), "Toolbox is navigation");
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.Unknown), "Unknown is not text input");
        }

        // BP-22 (n14): Run_GetGuidProperty_HResult was DELETED — the WindowTypeProbe class is
        // deleted by the build (the one-line ShouldLogFailure is inlined as `guidHr < 0` at the
        // call site). The behavior (`hr < 0` -> log `[NeoVisual] window type probe failed: 0x...`)
        // is unchanged and the byte-stable diagnostic is the contract.

        // BP-19 (A2): the preview text is cached keyed on ITextSnapshot.Version.VersionNumber —
        // the same version returns the cached text (no full-buffer GetText() per selection move);
        // a new version has no cached text yet (re-read). COMPILE-RED: PreviewTextCache does not
        // exist yet -> CS0246.
        public static void Run_PreviewTextCache_VersionKeyed()
        {
            var cache = new PreviewTextCache();
            cache.Store(1, "alpha");
            Assert.Equal("alpha", cache.Get(1));
            Assert.True(cache.Get(2) == null, "a new snapshot version has no cached text yet (re-read)");
            cache.Store(2, "beta");
            Assert.Equal("beta", cache.Get(2));
            Assert.Equal("alpha", cache.Get(1));
        }

        // BP-20 (A3): the Error List cache decision is a pure helper — a fresh cache is a hit; a
        // stale cache (past the TTL) expires and forces a re-scan. COMPILE-RED:
        // ErrorListCacheDecision does not exist yet -> CS0246.
        public static void Run_ErrorListCacheDecision_TTL()
        {
            Assert.True(ErrorListCacheDecision.IsFresh(0, 5000), "a just-written cache is fresh");
            Assert.True(ErrorListCacheDecision.IsFresh(4999, 5000), "within the TTL is fresh");
            Assert.False(ErrorListCacheDecision.IsFresh(5000, 5000), "at the TTL the cache is stale");
            Assert.False(ErrorListCacheDecision.IsFresh(6000, 5000), "past the TTL the cache is stale");
        }

        // BP-7 (m1): the Error List cache is INSTANCE-scoped (two gatherers do not share state) and
        // Invalidate() clears it (a build-done/document-saved event forces a fresh gather). RED:
        // ErrorListGatherer is a static class today (the static R40 cache) -> CS0712 on the
        // instance construction + CS1061 on Invalidate().
        public static void Run_ErrorListCacheDecision_Invalidation()
        {
            // m60 (BP-D13): the reflection is gone — the CacheForTest seam reads/writes the
            // instance cache directly (no private-field reads).
            var first = new ErrorListGatherer();
            var second = new ErrorListGatherer();

            // The instance cache is per-instance: seeding one gatherer's cache must not leak into the other.
            first.CacheForTest = new List<DiagnosticEntry>();
            Assert.True(second.CacheForTest == null,
                "two gatherers do not share cache state (the instance cache is per-instance)");

            // Invalidate() clears the cache (a build-done/document-saved event forces a fresh gather).
            first.Invalidate();
            Assert.True(first.CacheForTest == null,
                "Invalidate() clears the instance cache (a build-done/document-saved event forces a fresh gather)");
        }

        // BP-24 (A9): the session MRU is a bounded, O(1) move-to-front structure — re-opening a
        // path moves it to the front; the list never exceeds the cap. COMPILE-RED:
        // RecentFilesMru does not exist yet -> CS0246.
        public static void Run_RecentFilesMru_LinkedList()
        {
            var mru = new RecentFilesMru(capacity: 3);
            mru.Add(@"C:\p\a.cs");
            mru.Add(@"C:\p\b.cs");
            mru.Add(@"C:\p\c.cs");
            Assert.Equal(3, mru.Count);
            Assert.Equal(@"C:\p\c.cs", mru.MostRecent);

            // Re-opening a.cs moves it to the front (O(1) remove + insert) without growing the list.
            mru.Add(@"C:\p\a.cs");
            Assert.Equal(@"C:\p\a.cs", mru.MostRecent);
            Assert.Equal(3, mru.Count);

            // The cap: adding a 4th evicts the least-recent (b.cs).
            mru.Add(@"C:\p\d.cs");
            Assert.Equal(3, mru.Count);
            Assert.Equal(@"C:\p\d.cs", mru.MostRecent);
            var order = mru.ToList();
            Assert.Equal(@"C:\p\d.cs", order[0]);
            Assert.Equal(@"C:\p\a.cs", order[1]);
            Assert.Equal(@"C:\p\c.cs", order[2]);
        }

        // BP-8 (m2): RecentFilesGatherer is IDisposable — Dispose() unhooks the DocumentOpened
        // subscription (the COM connection point is released) and nulls _documentEvents. RED:
        // today there is no Dispose/unhook -> CS1061 on the missing member.
        public static void Run_RecentFilesGatherer_Dispose()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var gatherer = new RecentFilesGatherer(() => null);

                // Simulate the hooked state: the ctor's HookSessionEvents no-ops on a null DTE, so
                // inject the fake DocumentEvents the fix must unhook (m60/BP-D13: the
                // DocumentEventsForTest seam replaces the _documentEvents field reflection).
                var docEvents = new FakeDocumentEvents();
                gatherer.DocumentEventsForTest = docEvents;

                gatherer.Dispose();

                Assert.Equal(1, docEvents.UnhookCount,
                    "Dispose() must unhook the DocumentOpened subscription (the COM connection point is released)");
                Assert.True(gatherer.DocumentEventsForTest == null, "Dispose() must null _documentEvents");
            }
        }

        // BP-27 (C6): _focusKeeper must be reset to null after dispose — a disposed handle is
        // never reused. The fix adds SolutionExplorerController.ResetFocusKeeper(). COMPILE-RED:
        // ResetFocusKeeper() does not exist yet -> CS1061.
        public static void Run_FocusKeeper_ResetAfterDispose()
        {
            var controller = new SolutionExplorerController(() => null!);
            // n25 (BP-D23): inject a tracking IDisposable via the FocusKeeperForTest seam and
            // prove ResetFocusKeeper disposes it AND nulls the keeper field (a disposed handle is
            // never reused).
            var tracking = new TrackingDisposable();
            controller.FocusKeeperForTest = tracking;

            // The new seam: reset the keeper to null after dispose (BP-27).
            controller.ResetFocusKeeper();

            Assert.True(tracking.Disposed,
                "ResetFocusKeeper disposes the injected keeper handle (n25)");
            Assert.True(controller.FocusKeeperForTest == null,
                "_focusKeeper must be null after ResetFocusKeeper (BP-27) — a disposed handle is never reused");
        }

        // ================================================================
        // BP-D6 (m57): the block-caret state model tracks DESIRED + RENDERED separately, driven
        // through the pure `BlockCaretState` seam (the OverlayKeyHandler/TextMotionNavigator
        // pattern). RED today: `BlockCaretState` does not exist yet -> CS0246. The fix extracts
        // the state model from BlockCaretAdornment (its `_desiredActive`/`_renderedActive` +
        // OnLostFocus/OnGotFocus bodies delegate to the state; the `block-caret active=` diagnostic
        // literal stays logged from the adornment's ApplyRendered wrapper). The contract: a focus
        // loss clears the RENDERED state while the DESIRED state stays; a focus regain restores the
        // rendered state to the desired value; ApplyRendered is a no-op when unchanged.
        // ================================================================

        public static void Run_BlockCaretState_DesiredVsRendered()
        {
            var state = new BlockCaretState();

            // A normal-mode editor view asks for a block caret and gains focus: rendered = desired.
            state.DesiredActive = true;
            state.OnGotFocus();
            Assert.True(state.RenderedActive, "focus regain renders the desired block caret");

            // Focus loss: rendered cleared, desired kept.
            state.OnLostFocus();
            Assert.False(state.RenderedActive, "focus loss clears the rendered state");
            Assert.True(state.DesiredActive, "focus loss keeps the desired state");

            // Focus regain: rendered restored to the desired value.
            state.OnGotFocus();
            Assert.True(state.RenderedActive, "focus regain restores the rendered state to the desired value");

            // ApplyRendered is a no-op when unchanged.
            state.ApplyRendered(true);
            Assert.True(state.RenderedActive, "ApplyRendered(true) when already rendered is a no-op");
        }

        // ================================================================
        // BP-10 (m5): SolutionExplorerController.TryMove resolves the focused text box ONCE per
        // routed key. RED: today TryMove calls TextMotionHelper.FindFocusedTextBox() twice per
        // routed key when a box is focused — the gate (:207) + the TextMotion->TryMoveFocusedSurface
        // internal walk (:77) — two visual-tree walks. The fix resolves the box once and passes it
        // to a new TextMotion(key, box) overload (the focusedBox overload at TextMotionHelper.cs:90
        // avoids the second walk). The counting seam: a `_findFocusedTextBox` Func<TextBox?> field
        // the test replaces with a counting lambda.
        // ================================================================

        public static void Run_SolutionExplorer_TryMoveSingleWalk()
        {
            // m60 (BP-D13): the reflection is gone — SetFindFocusedTextBoxForTest replaces the
            // focused-box resolver (no private-field reads).
            var controller = new SolutionExplorerController(() => null!);

            // A non-null box (never dereferenced — X is not a motion, so TryMoveFocusedSurface
            // returns before touching it). GetUninitializedObject skips the WPF ctor (no STA needed).
            var fakeBox = (System.Windows.Controls.TextBox)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(System.Windows.Controls.TextBox));
            int calls = 0;
            controller.SetFindFocusedTextBoxForTest(() => { calls++; return fakeBox; });

            // X is not a text motion and not an action key — it routes through the text-box path
            // (box != null) and is not consumed. The seam must be called exactly once.
            Assert.False(controller.TryMove(Keys.X), "an unmapped key is not consumed");
            Assert.Equal(1, calls);
        }

        // ================================================================
        // BP-11 (n3): InputHandler.TryRouteToolWindowKey computes the tool-window routing decision
        // ONCE per key. RED: today the decision is computed twice per key — ShouldRouteToolWindowKey()
        // at :379 (the no-arg overload -> the 3-arg FocusGuard) + the inline 6-arg
        // FocusGuard.ShouldRouteToolWindowKey at :403-410. The fix resolves the controller once and
        // computes `bool route = ShouldRouteToolWindowKey(controller)` once, reusing it for both the
        // null-guard and the shift-gated branch. The counting seam: a `_routeDecision` Func<bool>
        // field the test replaces with a counting lambda.
        // ================================================================

        public static void Run_TryRouteToolWindowKey_SingleDecision()
        {
            // m60 (BP-D13): the reflection is gone — the test-only ctor wires the fields
            // TryRouteToolWindowKey reads, SetToolWindowStateForTest forces the tool-window state,
            // SetRouteDecisionForTest replaces the routing seam, and the internal
            // TryRouteToolWindowKey is invoked directly (no GetUninitializedObject / private reads).
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                // A WindowManager forced into a tool-window state (Toolbox -> a GeneralToolWindowController).
                var manager = new WindowManager(new FakeMonitorSelection());
                manager.SetToolWindowStateForTest(true, ToolWindowType.Toolbox);

                // The test-only ctor (skips the VS-coupled parts) wires the fields
                // TryRouteToolWindowKey reads.
                var handler = new InputHandler(null!, manager);

                // The counting seam: the single routing decision. m38 (BP-15): the field is
                // Func<IToolWindowController?, bool> (the controller is resolved once and passed in).
                int calls = 0;
                handler.SetRouteDecisionForTest(_ => { calls++; return false; });

                // Route a key through the internal TryRouteToolWindowKey. The seam returns false (don't
                // route), so the method returns null after computing the decision once.
                handler.TryRouteToolWindowKey(Keys.H, false, false, false);

                Assert.Equal(1, calls);
            }
        }

        // ================================================================
        // BP-20 (n1): the WindowNavigator static cache is reference-keyed (adapters list + active
        // window) and BuildActiveWindows returns a COPY. Guard — pins the already-correct A6 copy
        // guarantee + the reference-keying contract (the plan's n1 research correction: instance-
        // scoping is NOT the fix; the reference-keyed static cache self-invalidates on window-set
        // change). RED: the copy guarantee was unpinned (no test).
        // ================================================================

        public static void Run_WindowNavigator_CacheClearedOnReenum()
        {
            // m60 (BP-D13): the reflection is gone — the CachedLinkedForTest / CachedLinkedSourceForTest /
            // CachedLinkedActiveForTest seams read/write/reset the reference-keyed static cache (no
            // private static-field reads).
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                try
                {
                    // Fake adapters (GetUninitializedObject skips the ctor's ThrowIfNotOnUIThread) + a fake
                    // active window (LinkedWindowFrame -> null, so the cache-miss LinkedTo returns empty).
                    var fakeAdapter = (WindowFrameAdapter)System.Runtime.Serialization.FormatterServices
                        .GetUninitializedObject(typeof(WindowFrameAdapter));
                    var adapters1 = new List<WindowFrameAdapter> { fakeAdapter };
                    var fakeActive = new FakeWindow();

                    // Pre-populate the cache with a NON-EMPTY linked list so the copy guarantee is meaningful.
                    WindowNavigator.CachedLinkedForTest = new List<WindowFrameAdapter> { fakeAdapter };
                    WindowNavigator.CachedLinkedSourceForTest = adapters1;
                    WindowNavigator.CachedLinkedActiveForTest = fakeActive;

                    // Cache-hit: same refs -> the cached list is returned as a COPY, never the cache itself.
                    var result = WindowNavigator.BuildActiveWindows(fakeActive, adapters1);
                    var cached = WindowNavigator.CachedLinkedForTest!;
                    Assert.False(ReferenceEquals(result, cached),
                        "BuildActiveWindows returns a COPY, never the cached list itself (A6)");
                    Assert.Equal(1, result.Count);

                    // Mutating the returned copy must not corrupt the cache.
                    result.Add(fakeAdapter);
                    Assert.Equal(1, cached.Count);
                    Assert.Equal(2, result.Count);

                    // Keying: a NEW adapters list reference is NOT a cache hit -> recompute (the cache is
                    // reference-keyed on the adapters list + active window).
                    var adapters2 = new List<WindowFrameAdapter> { fakeAdapter };
                    WindowNavigator.BuildActiveWindows(fakeActive, adapters2);
                    Assert.True(ReferenceEquals(WindowNavigator.CachedLinkedSourceForTest, adapters2),
                        "a new adapters list reference recomputes (the cache is reference-keyed)");
                }
                finally
                {
                    // Reset the static cache so no later test sees a stale window-set.
                    WindowNavigator.CachedLinkedForTest = null;
                    WindowNavigator.CachedLinkedSourceForTest = null;
                    WindowNavigator.CachedLinkedActiveForTest = null;
                }
            }
        }

        // ================================================================
        // Code-review fixes (34 findings) — FINAL NeoVisual RED chunk (BP-21/29/30/31).
        // ================================================================

        public static void Run_WindowNavigator_LazyDte()
        {
            // BP-21 (n2): the ctor must NOT resolve DTE when currentFrame != null (the active
            // window comes from VsShellUtilities.GetWindowObject(currentFrame), never DTE). A
            // counting package records every GetService(typeof(DTE)) request; on the non-null-frame
            // path the count must be 0.
            // RED: today the ctor resolves VsServices.Dte(package) unconditionally at :53, so the
            // recording package sees exactly one DTE request -> Assert.Equal(0, ...) fails.
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {

                // GetUninitializedObject skips the AsyncPackage base ctor (which NREs outside VS).
                var package = (RecordingPackage)System.Runtime.Serialization.FormatterServices
                    .GetUninitializedObject(typeof(RecordingPackage));
                var frame = new FakeFrame();
                var adapters = new List<WindowFrameAdapter>();

                // currentFrame != null: the active window is sourced from the frame, never DTE.
                var navigator = new WindowNavigator(adapters, package, frame);

                Assert.Equal(0, package.DteRequestCount);
            }
        }

        public static void Run_WindowManager_DefaultControllerCache_NoReflection()
        {
            // BP-29 (m17): the internal GetController seam — the test calls it directly (no
            // GetField/GetMethod reflection) and asserts the per-type default controller is cached
            // (same instance per type). The WindowManager ctor still calls RefreshCurrentWindow ->
            // ThrowIfNotOnUIThread(), so the dispatcher setup remains (the fix removes the
            // reflection into the private GetController, not the dispatcher setup).
            // RED: GetController is private today -> CS0122 (the internal seam does not exist).
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var manager = new WindowManager(new FakeMonitorSelection());

                var first = manager.GetController(ToolWindowType.Toolbox);
                var second = manager.GetController(ToolWindowType.Toolbox);

                Assert.True(ReferenceEquals(first, second),
                    "the per-type default controller must be cached (same instance per type) — BP-29");
            }
        }

        public static void Run_Assert_NotEqual()
        {
            // BP-30 (n8): Assert.NotEqual throws when the values are equal and passes when they
            // differ. RED: Assert.NotEqual does not exist in the shared TestRunner -> CS1061.
            Assert.NotEqual("a", "b");   // different -> passes
            Assert.NotEqual(1, 2);       // different -> passes

            var threw = false;
            try
            {
                Assert.NotEqual("same", "same");  // equal -> must throw
            }
            catch (Exception)
            {
                threw = true;
            }
            Assert.True(threw, "Assert.NotEqual must throw when the values are equal (BP-30)");
        }

        public static void Run_Assert_Equal()
        {
            // BP-D12 (n7): the shared Assert.Equal prints actual/expected so a failure is
            // diagnosable. Coverage addition (passes today): Assert.Equal(1, 2) must throw with a
            // message containing BOTH values (the "Expected [1] but got [2]" format from
            // tests/TestRunner.cs:126-132) — a future regression that drops actual/expected from
            // the message fails this pin.
            try
            {
                Assert.Equal(1, 2);
                Assert.True(false, "Assert.Equal(1, 2) must throw");
            }
            catch (Exception ex)
            {
                Assert.True(ex.Message.Contains("1"), $"message must contain the expected value: {ex.Message}");
                Assert.True(ex.Message.Contains("2"), $"message must contain the actual value: {ex.Message}");
            }
        }

        public static void Run_TestRunner_Timeout()
        {
            // BP-31 (n9): a deadlocking test must be reported as timed out (FAIL) and the runner
            // must continue to the next test — it must NOT hang the whole suite. The nested
            // SleepingTests type has Run_Sleeps (blocks far beyond any plausible per-test timeout)
            // and Run_After (a quick test that must still run after the timeout).
            // RED: today the runner has no per-test timeout, so Run_Sleeps hangs the nested run
            // forever; the bounded wait below expires and the first assertion fails.
            var output = new System.IO.StringWriter();
            var originalOut = Console.Out;
            var done = new System.Threading.ManualResetEventSlim(false);
            var thread = new System.Threading.Thread(() =>
            {
                Console.SetOut(output);
                try
                {
                    // BP-31: the nested runner gets a SHORT per-test timeout (5s) so the nested
                    // run completes well within this test's own budget — the nested run's duration
                    // must be strictly less than the outer runner's per-test timeout or the outer
                    // runner would kill this observing test at the same moment the nested timeout
                    // fires.
                    TestRunner.Run(typeof(SleepingTests), new string[0], perTestTimeoutSeconds: 5);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
                done.Set();
            });
            thread.IsBackground = true;
            thread.Start();

            // Bounded wait: generous enough to cover the plan's ~60s per-test timeout. In the RED
            // case the nested runner hangs on Run_Sleeps, so this wait expires and the assertion
            // below fails (the suite would hang forever without the per-test timeout).
            var completed = done.Wait(TimeSpan.FromSeconds(100));
            // Restore the console on the main thread too — the background thread may be hung.
            Console.SetOut(originalOut);

            Assert.True(completed,
                "the runner must complete: a deadlocking test is reported as timed out, not hang the suite (BP-31)");
            var text = output.ToString();
            Assert.True(text.Contains("FAIL  Run_Sleeps"),
                "the sleeping test is reported as FAIL (timed out) — BP-31");
            Assert.True(text.Contains("PASS  Run_After"),
                "the runner continues to the next test after a timeout — BP-31");
        }

        public static void Run_Assert_StackTrace()
        {
            // BP-D8 (n2): the runner's FAIL line prints only the exception message
            // (Console.WriteLine($"FAIL  {method.Name}: {Unwrap(testError).Message}")) — no stack
            // trace. The fix extracts the formatting into a pure seam
            // TestRunner.FormatFailure(methodName, ex) that prints the full exception
            // (ToString() includes the stack trace).
            // RED: TestRunner.FormatFailure does not exist in the shared TestRunner -> CS0117.
            // NOTE: the exception must be THROWN first — an unthrown exception's ToString() has no
            // stack trace (StackTrace is captured at the throw site), so the "at " frame assertion
            // would fail vacuously.
            Exception boom;
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (Exception ex)
            {
                boom = ex;
            }
            var formatted = TestRunner.FormatFailure("X", boom);

            Assert.True(formatted.Contains("boom"),
                "the formatted failure contains the exception message (BP-D8)");
            Assert.True(formatted.Contains("InvalidOperationException"),
                "the formatted failure contains the exception type (BP-D8)");
            Assert.True(formatted.Contains("at "),
                "the formatted failure contains a stack-trace frame (BP-D8)");
        }

        // BP-21: a counting AsyncPackage — records every GetService(typeof(DTE)) request so the
        // test can assert the WindowNavigator ctor does NOT resolve DTE on the non-null-frame path.
        private sealed class RecordingPackage : Microsoft.VisualStudio.Shell.AsyncPackage
        {
            public int DteRequestCount { get; private set; }

            protected override object GetService(Type serviceType)
            {
                if (serviceType == typeof(EnvDTE.DTE))
                {
                    DteRequestCount++;
                }
                return null!;
            }
        }

        // BP-21: a minimal IVsWindowFrame fake. GetProperty returns E_NOTIMPL so
        // VsShellUtilities.GetWindowObject returns null (the ctor degrades to a no-op); every other
        // member throws (the ctor's try/catch swallows it). The fake only needs to be a non-null
        // IVsWindowFrame so the ctor takes the currentFrame != null branch.
        private sealed class FakeFrame : IVsWindowFrame
        {
            public int GetProperty(int propid, out object pvar)
            {
                pvar = null!;
                return unchecked((int)0x80004001); // E_NOTIMPL
            }
            public int Show() => throw new NotImplementedException();
            public int Hide() => throw new NotImplementedException();
            public int IsVisible() => throw new NotImplementedException();
            public int ShowNoActivate() => throw new NotImplementedException();
            public int CloseFrame(uint grfSaveOptions) => throw new NotImplementedException();
            public int SetFramePos(VSSETFRAMEPOS dwSFP, ref Guid rguidRelativeTo, int x, int y, int cx, int cy) => throw new NotImplementedException();
            public int GetFramePos(VSSETFRAMEPOS[] pdwSFP, out Guid pguidRelativeTo, out int px, out int py, out int pcx, out int pcy) => throw new NotImplementedException();
            public int SetProperty(int propid, object var) => throw new NotImplementedException();
            public int GetGuidProperty(int propid, out Guid pguid) => throw new NotImplementedException();
            public int SetGuidProperty(int propid, ref Guid rguid) => throw new NotImplementedException();
            public int QueryViewInterface(ref Guid riid, out IntPtr ppv) => throw new NotImplementedException();
            public int IsOnScreen(out int pfOnScreen) => throw new NotImplementedException();
        }

        // BP-31: a nested test type whose Run_Sleeps blocks just past the nested per-test timeout
        // (BP-D13: shortened from 10 minutes to ~6s so the timeout still fires but the abandoned
        // thread dies quickly) and whose Run_After must still run after the timeout fires.
        private sealed class SleepingTests
        {
            public static void Run_Sleeps()
            {
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(6));
            }

            public static void Run_After()
            {
            }
        }

        // ================================================================
        // 106-findings plan — Section A (Phase 1: navigation + interop)
        // BP-12 (M6) — GetLinkedWindowsList per-window try/catch: one stale COM frame must not
        // kill navigation. RED: the stale window's LinkedWindowFrame throw propagates.
        // ================================================================

        public static void Run_WindowFrameUtils_LinkedWindowIsolation()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {

                var parent = new FakeWindow();
                var linked = new LinkedWindow(parent);
                var stale = new ThrowingLinkedWindow();

                List<EnvDTE.Window>? result = null;
                Exception? thrown = null;
                try
                {
                    result = WindowFrameUtils.GetLinkedWindowsList(parent, new List<EnvDTE.Window> { linked, stale });
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }

                Assert.True(thrown == null,
                    $"one stale window must not kill navigation (BP-12): {thrown?.GetType().Name}: {thrown?.Message}");
                Assert.True(result != null && result.Contains(linked),
                    "the linked window survives the stale window (BP-12)");
            }
        }

        // ================================================================
        // 106-findings plan — Section A Phase 1 (navigation isolation, M2/m10/m12/m13/n4)
        // ================================================================

        // BP-12 (M2): FindActive's FirstOrDefault predicate + LinkedTo's Where clause must isolate
        // each CompareWindows call — a stale adapter whose DteWindow throws on Caption/Type must be
        // SKIPPED, not propagated (the F7 residual). RED today: the stale adapter's CompareWindows
        // throw escapes FindActive/LinkedTo.
        public static void Run_WindowFrameAdapter_LinkedIsolation()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                // The Properties-window quirk: a ToolWindow active + a Properties window with the
                // same caption compare equal (CompareWindows returns true).
                var activeWindow = new ComparableWindow("Foo", vsWindowType.vsWindowTypeToolWindow);
                var healthy = MakeAdapter(new ComparableWindow("Foo", vsWindowType.vsWindowTypeProperties));
                var stale = MakeAdapter(new ThrowingWindow());

                // FindActive: the stale adapter is FIRST so its CompareWindows is evaluated before
                // the healthy match — the throw must be isolated, not propagated.
                Exception? findThrown = null;
                WindowFrameAdapter? found = null;
                try
                {
                    found = WindowFrameAdapter.FindActive(activeWindow, new List<WindowFrameAdapter> { stale, healthy });
                }
                catch (Exception ex)
                {
                    findThrown = ex;
                }
                Assert.True(findThrown == null,
                    $"a stale adapter must not kill FindActive (BP-12): {findThrown?.GetType().Name}: {findThrown?.Message}");
                Assert.True(ReferenceEquals(found, healthy),
                    "FindActive skips the stale adapter and returns the surviving one (BP-12)");

                // LinkedTo: the stale adapter's DteWindow Caption read inside the Where clause must
                // be isolated too.
                Exception? linkedThrown = null;
                IEnumerable<WindowFrameAdapter>? linked = null;
                try
                {
                    linked = WindowFrameAdapter.LinkedTo(activeWindow, new List<WindowFrameAdapter> { stale, healthy });
                }
                catch (Exception ex)
                {
                    linkedThrown = ex;
                }
                Assert.True(linkedThrown == null,
                    $"a stale adapter must not kill LinkedTo (BP-12): {linkedThrown?.GetType().Name}: {linkedThrown?.Message}");
                Assert.True(linked != null && linked.Contains(healthy),
                    "LinkedTo skips the stale adapter and returns the surviving one (BP-12)");
            }
        }

        // BP-13 (m10): the dead WindowRect.Adjacency/GapTo mirrors of the shared
        // GeometricSelectionEngine formulas are deleted. Reflection-absence is the only hermetic way
        // to pin a deletion (a single reflection call, not systemic).
        public static void Run_WindowRect_DeadMirrorsRemoved()
        {
            Assert.True(typeof(WindowRect).GetMethod("Adjacency",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) == null,
                "WindowRect.Adjacency is deleted (m10)");
            Assert.True(typeof(WindowRect).GetMethod("GapTo",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) == null,
                "WindowRect.GapTo is deleted (m10)");
        }

        // BP-15 (m12): WindowNavigationEngine.SelectTarget must map Direction -> the explicit
        // GeometricSelectionEngine token (Up/Down/Left/Right), independent of the enum's numeric
        // values. PIN: the mapping is correct today (the (int)direction coupling happens to match);
        // the fix replaces the coupling with an explicit switch without changing behavior.
        public static void Run_Direction_ExplicitMapping()
        {
            var settings = NavigationSettings.FromDpi(96, 96); // XDivide=24, YDivide=100
            var active = new WindowRect(100, 100, 100, 100);
            var candidates = new[]
            {
                new WindowRect(100, 0, 100, 50),   // above (Up)
                new WindowRect(100, 200, 100, 50), // below (Down)
                new WindowRect(0, 100, 50, 100),   // left
                new WindowRect(200, 100, 50, 100), // right
            };

            Assert.Equal(
                GeometricSelectionEngine.SelectTarget(active, candidates, GeometricSelectionEngine.Up, allowNegativeGap: true, divide: settings.YDivide, strictEdge: true),
                WindowNavigationEngine.SelectTarget(active, candidates, Direction.Up, settings));
            Assert.Equal(
                GeometricSelectionEngine.SelectTarget(active, candidates, GeometricSelectionEngine.Down, allowNegativeGap: true, divide: settings.YDivide, strictEdge: true),
                WindowNavigationEngine.SelectTarget(active, candidates, Direction.Down, settings));
            Assert.Equal(
                GeometricSelectionEngine.SelectTarget(active, candidates, GeometricSelectionEngine.Left, allowNegativeGap: true, divide: settings.XDivide, strictEdge: true),
                WindowNavigationEngine.SelectTarget(active, candidates, Direction.Left, settings));
            Assert.Equal(
                GeometricSelectionEngine.SelectTarget(active, candidates, GeometricSelectionEngine.Right, allowNegativeGap: true, divide: settings.XDivide, strictEdge: true),
                WindowNavigationEngine.SelectTarget(active, candidates, Direction.Right, settings));
        }

        // BP-16 (m13): a WindowNavigator whose active window cannot be paired to an adapter (the
        // adapters' DteWindow is null, so FindActive returns null) must degrade to a no-op, not
        // throw. PIN: the runtime guard at NavigateInDirection already returns NoOp("no active
        // window"); the fix makes the field nullable so the compiler enforces it.
        public static void Run_WindowNavigator_NullActive()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var package = (RecordingPackage)System.Runtime.Serialization.FormatterServices
                    .GetUninitializedObject(typeof(RecordingPackage));
                // The frame's DocView is a FakeWindow, so VsShellUtilities.GetWindowObject resolves
                // a non-null active window; the adapter's DteWindow is null, so FindActive returns null.
                var frame = new WindowFrameWithDocView(new FakeWindow());
                var adapter = new WindowFrameAdapter(frame, null!);
                var adapters = new List<WindowFrameAdapter> { adapter };

                var navigator = new WindowNavigator(adapters, package, frame);
                var outcome = navigator.NavigateInDirection(Direction.Left);

                Assert.False(outcome.Activated,
                    "an active window that cannot be paired to an adapter is a no-op (BP-16)");
                Assert.Equal("no active window", outcome.NoOpReason);
            }
        }

        // BP-17 (n4): the redundant WindowFrameAdapter.ExtractFrames wrapper is deleted (the call
        // sites call ExtractFramesCore directly). Reflection-absence is the only hermetic way to pin
        // a deletion.
        public static void Run_ExtractFrames_Removed()
        {
            Assert.True(typeof(WindowFrameAdapter).GetMethod("ExtractFrames",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) == null,
                "WindowFrameAdapter.ExtractFrames is deleted (n4)");
        }

        // ================================================================
        // BP-15 (m17) — RecentFilesGatherer.Dispose asserts the UI thread before the COM unhook.
        // RED: no assert today -> Dispose() from a background thread does not throw.
        // ================================================================

        public static void Run_RecentFilesGatherer_Dispose_RequiresUiThread()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var gatherer = new RecentFilesGatherer(() => null);

                Exception? thrown = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try { gatherer.Dispose(); }
                    catch (Exception ex) { thrown = ex; }
                });
                thread.Start();
                thread.Join();

                Assert.True(thrown != null,
                    "Dispose() must assert the UI thread before the COM unhook (BP-15) — today it runs off-thread");
            }
        }

        // ================================================================
        // BP-17 (m21) — FocusKeeper cancels a superseded keeper's queued tick.
        // NOTE: the RED is the guard mechanism — the race (a tick queued before Stop()) is
        // timing-dependent, so this may pass without the fix; the test pins the contract.
        // ================================================================

        public static void Run_FocusKeeper_TickCancelled()
        {
            var keeper = new FocusKeeper();
            int keeper1Ticks = 0;

            // Keeper 1: a 1ms interval, tick increments a counter.
            keeper.Run(TimeSpan.FromMilliseconds(1), 5000, _ => { keeper1Ticks++; return true; });

            // Keeper 2 immediately supersedes keeper 1.
            keeper.Run(TimeSpan.FromMilliseconds(100), 5000, _ => true);

            // m64 (BP-D17): invoke the superseded keeper's tick DIRECTLY through the InvokeTick
            // seam (no 30ms DispatcherTimer pump). The ReferenceEquals guard makes a non-current
            // keeper's tick a no-op — the tick lambda must never run for a superseded keeper.
            var supersededTimer = new System.Windows.Threading.DispatcherTimer();
            keeper.InvokeTick(supersededTimer, 5000, System.Diagnostics.Stopwatch.StartNew(),
                _ => { keeper1Ticks++; return true; });

            // A superseded keeper's queued tick must be a no-op (m21).
            Assert.Equal(0, keeper1Ticks);
        }

        // ================================================================
        // BP-18 (m22) — FindResults1/FindResults2 are read-only lists, not text-input surfaces.
        // RED: both return true today.
        // ================================================================

        public static void Run_ToolWindowTypeResolver_FindResultsNotTextInput()
        {
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.FindResults1),
                "FindResults1 is not a text-input surface (m22)");
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.FindResults2),
                "FindResults2 is not a text-input surface (m22)");
        }

        // ================================================================
        // BP-19 (m47) — SolutionExplorerController.TryMove caches the visual-tree walk across keys.
        // RED: the second TryMove re-walks -> the counting seam is called twice.
        // ================================================================

        public static void Run_SolutionExplorer_TryMoveCachedAcrossKeys()
        {
            // m60 (BP-D13): the reflection is gone — SetFindFocusedTextBoxForTest replaces the
            // focused-box resolver (no private-field reads).
            var controller = new SolutionExplorerController(() => null!);

            // A non-null box (never dereferenced — X is not a motion, so TryMoveFocusedSurface
            // returns before touching it). GetUninitializedObject skips the WPF ctor (no STA needed).
            var fakeBox = (System.Windows.Controls.TextBox)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(System.Windows.Controls.TextBox));
            int calls = 0;
            controller.SetFindFocusedTextBoxForTest(() => { calls++; return fakeBox; });

            // Two consecutive TryMove calls must resolve the box ONCE (the walk is cached across
            // keys, m47). RED today: the second call re-walks -> calls == 2.
            Assert.False(controller.TryMove(Keys.X), "an unmapped key is not consumed");
            Assert.False(controller.TryMove(Keys.X), "an unmapped key is not consumed");
            Assert.Equal(1, calls);
        }

        // ================================================================
        // BP-1 (M5) — VimModeTracker document-view discriminator + MainEditorFocused event.
        // RED: `MainEditorFocused` + `IsMainEditorView` do not exist -> compile error
        // (CS1061/CS0117).
        // ================================================================
        public static void Run_VimModeTracker_MainEditorFocusedEvent()
        {
            // The document-view discriminator (ITextDocument.FilePath) fires MainEditorFocused on
            // a document view's focus; a non-document view (no ITextDocument, or empty FilePath)
            // does not. This is the identity that distinguishes the main editor from the Command
            // Window's non-document editor view (M5).
            using (var dir = new TempDir())
            {
                string logPath = System.IO.Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var tracker = new VimModeTracker(new FakeVimModeSource());

                    // (1) A document view (ITextDocument with a non-empty FilePath) fires.
                    var docProps = new Microsoft.VisualStudio.Utilities.PropertyCollection();
                    docProps.AddProperty(typeof(ITextDocument), new FakeTextDocument("C:\\foo.cs"));
                    var docView = new FakeTextView(docProps);
                    bool fired = false;
                    tracker.MainEditorFocused += () => fired = true;
                    tracker.TextViewCreated(docView);
                    docView.RaiseGotAggregateFocus();
                    Assert.True(fired, "a document view's focus must fire MainEditorFocused");
                    Assert.True(VimModeTracker.IsMainEditorView(docView),
                        "the document view is a main-editor view");

                    // (2) A non-document view (no ITextDocument) does not fire.
                    var plainView = new FakeTextView();
                    bool fired2 = false;
                    tracker.MainEditorFocused += () => fired2 = true;
                    tracker.TextViewCreated(plainView);
                    plainView.RaiseGotAggregateFocus();
                    Assert.False(fired2, "a non-document view must not fire MainEditorFocused");
                    Assert.False(VimModeTracker.IsMainEditorView(plainView),
                        "the non-document view is not a main-editor view");

                    // (3) A document view with an EMPTY FilePath does not fire.
                    var emptyProps = new Microsoft.VisualStudio.Utilities.PropertyCollection();
                    emptyProps.AddProperty(typeof(ITextDocument), new FakeTextDocument(""));
                    var emptyView = new FakeTextView(emptyProps);
                    bool fired3 = false;
                    tracker.MainEditorFocused += () => fired3 = true;
                    tracker.TextViewCreated(emptyView);
                    emptyView.RaiseGotAggregateFocus();
                    Assert.False(fired3, "an empty-FilePath document view must not fire MainEditorFocused");
                    Assert.False(VimModeTracker.IsMainEditorView(emptyView),
                        "the empty-FilePath view is not a main-editor view");
                });
            }
        }

        // ================================================================
        // BP-2 (M5) — WindowManager invalidation on a document-view focus.
        // RED: `InvalidateTextInputSurfaceFocused` does not exist -> compile error (CS1061).
        // ================================================================

        public static void Run_FocusGuard_TextInputSurfaceInvalidatedOnEditorFocus()
        {
            // A document-view focus must invalidate the cached text-input-surface flag so a stale
            // Command Window frame can never claim keyboard ownership over a focused editor. The
            // invalidation callback is the InputHandler ctor subscription the fix wires
            // (MainEditorFocused -> WindowManager.InvalidateTextInputSurfaceFocused).
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var manager = new WindowManager(new FakeMonitorSelection());
                var tracker = new VimModeTracker(new FakeVimModeSource());

                bool invalidated = false;
                tracker.MainEditorFocused += () =>
                {
                    invalidated = true;
                    manager.InvalidateTextInputSurfaceFocused();
                };

                var docProps = new Microsoft.VisualStudio.Utilities.PropertyCollection();
                docProps.AddProperty(typeof(ITextDocument), new FakeTextDocument("C:\\foo.cs"));
                var docView = new FakeTextView(docProps);
                tracker.TextViewCreated(docView);
                docView.RaiseGotAggregateFocus();

                Assert.True(invalidated, "a document-view focus must run the invalidation callback");
                Assert.False(
                    FocusGuard.OwnsKeyboard(isInputMode: false, isTextInputSurface: true, textInputSurfaceFocused: false),
                    "a stale text-input frame must not own the keyboard after a document-view focus");
            }
        }

        // ================================================================
        // BP-10 (m16) — the hook callback gates on HC_ACTION.
        // RED: `IsActionEvent` does not exist -> compile error (CS1061).
        // ================================================================

        public static void Run_GlobalKeyboardHook_HcActionGate()
        {
            // A peeked event (HC_NOREMOVE = 3) must be passed through untouched — only HC_ACTION
            // (0) is a real key event the hook processes.
            Assert.True(GlobalKeyboardHook.IsActionEvent(0), "HC_ACTION (0) is an action event");
            Assert.False(GlobalKeyboardHook.IsActionEvent(3), "HC_NOREMOVE (3) is not an action event");
        }

        // ================================================================
        // BP-5 (m10) — the hook callback checks isKeyDown BEFORE the focus check.
        // RED: `ShouldProcessKey` does not exist -> compile error (CS0117).
        // The build-agent creates this pure seam during BUILD: ShouldProcessKey(wParam,
        // isFocused) returns false for a key-up WITHOUT invoking the focus-check delegate,
        // and only invokes it for a key-down (returning its result). Today HookCallback runs
        // IsVisualStudioFocused() before the isKeyDown check, so every key-up pays the two
        // Win32 calls — this test pins the reordered contract.
        // ================================================================

        public static void Run_GlobalKeyboardHook_IsKeyDownFirst()
        {
            int focusChecks = 0;
            Func<bool> countingFocus = () => { focusChecks++; return true; };

            // WM_KEYUP (0x101): a key-up must return before the focus check — the delegate is
            // never invoked.
            Assert.False(
                GlobalKeyboardHook.ShouldProcessKey(0x101, countingFocus),
                "a key-up must be rejected without consulting the focus check");
            Assert.Equal(0, focusChecks);

            // WM_KEYDOWN (0x100): a key-down invokes the delegate exactly once and returns its
            // result.
            focusChecks = 0;
            Assert.True(
                GlobalKeyboardHook.ShouldProcessKey(0x100, countingFocus),
                "a key-down must consult the focus check and return its result");
            Assert.Equal(1, focusChecks);
        }

        // ================================================================
        // BP-10 (m15) — the DTE TextSelection column is clamped to the line
        // length. RED: `RoslynGatherers.ResolveOffset` does not exist ->
        // compile error (CS0117). The build-agent extracts this pure seam
        // during BUILD: the current `GetCaretOffset` (RoslynGatherers.cs:325)
        // inlines `text.Lines[line-1].Start + Math.Max(0, column-1)` — the
        // column is NOT clamped to the line length, so a caret in virtual
        // space resolves the wrong symbol. ResolveOffset(text, line, column)
        // clamps `column` to `text.Lines[line-1].Span.Length + 1` before the
        // offset computation.
        // ================================================================

        public static void Run_RoslynGatherers_ColumnClamped()
        {
            // "one\ntwo\nthree": line 2 ("two") starts at offset 4, length 3.
            var text = Microsoft.CodeAnalysis.Text.SourceText.From("one\ntwo\nthree");

            // A column in virtual space (10 > the line's 3 chars) is clamped to
            // the line end -> offset 4 + 3 = 7 (RED today: 4 + 9 = 13, past the
            // line end, resolving the wrong symbol).
            Assert.Equal(4 + 3, RoslynGatherers.ResolveOffset(text, 2, 10));

            // A normal column resolves to the exact character: offset 4 + (2-1) = 5.
            Assert.Equal(4 + 1, RoslynGatherers.ResolveOffset(text, 2, 2));
        }

        // ================================================================
        // 107-findings plan — Section B (Phases 2-4: Vim interop, Hook/input/package, Tool-windows)
        // ================================================================

        // BP-1 (m1): OnBufferClosed-then-Detach double-decrements the shared-text-buffer refcount.
        // Two views A/B share one text buffer, both Closed-subscribed: OnBufferClosed(bufferA) drops
        // 2 -> 1; Detach(viewA) must NOT decrement again (the refcount was already decremented by
        // OnBufferClosed); Detach(viewB) drops to 0. RED today: Detach(viewA) double-decrements to 0
        // (returns true) and Detach(viewB) then returns false.
        public static void Run_VimBufferSubscriptions_NoDoubleDecrement()
        {
            var subs = new VimBufferSubscriptions();
            var viewA = new FakeTextView();
            var viewB = new FakeTextView();
            var bufferA = new object();
            var bufferB = new object();
            var textBuffer = new object();

            subs.Attach(viewA, bufferA, textBuffer);
            subs.Attach(viewB, bufferB, textBuffer);
            subs.MarkClosedSubscribed(bufferA);
            subs.MarkClosedSubscribed(bufferB);

            // OnBufferClosed(bufferA): refcount 2 -> 1, not last.
            Assert.False(subs.OnBufferClosed(bufferA), "OnBufferClosed drops 2 -> 1 (not last)");
            // Detach(viewA) must NOT decrement again (the refcount was already decremented by
            // OnBufferClosed). RED today: the unconditional decrement drops it to 0 -> returns true.
            Assert.False(subs.Detach(viewA),
                "Detach after OnBufferClosed must not double-decrement (refcount stays 1)");
            // Detach(viewB) drops the refcount to 0.
            Assert.True(subs.Detach(viewB), "Detach(viewB) drops the refcount to 0");
        }

        // BP-2 (m6): VsVimModeSource.Detach -> UnsubscribeBuffer re-resolves the text buffer via
        // reflection (GetTextBuffer) on a possibly-closing buffer — a reflection failure leaks the
        // SwitchedMode subscription. The fix reads the CACHED text buffer before the refcount
        // decrement removes the entry. RED today: GetTextBuffer throws on the closing buffer ->
        // RemoveSwitchedMode skipped -> _subscribedTextBuffers still contains the text buffer.
        public static void Run_VimModeSource_DetachCached()
        {
            var source = new VsVimModeSource();
            var view = new FakeTextView();
            var textBuffer = new object();
            var buffer = new ClosingVimBuffer(textBuffer); // get_VimTextBuffer: 1st call OK, then throws

            var subsField = typeof(VsVimModeSource).GetField(
                "_subscriptions", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(subsField != null, "VsVimModeSource._subscriptions must exist");
            var subs = (VimBufferSubscriptions)subsField!.GetValue(source)!;
            subs.Attach(view, buffer, textBuffer);
            subs.MarkClosedSubscribed(buffer);

            var subscribe = typeof(VsVimModeSource).GetMethod(
                "SubscribeBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(subscribe != null, "VsVimModeSource.SubscribeBuffer must exist");
            subscribe!.Invoke(source, new object[] { buffer, true });

            var subscribedField = typeof(VsVimModeSource).GetField(
                "_subscribedTextBuffers", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(subscribedField != null, "VsVimModeSource._subscribedTextBuffers must exist");
            var subscribed = (HashSet<object>)subscribedField!.GetValue(source)!;
            Assert.True(subscribed.Contains(textBuffer),
                "setup: the SwitchedMode subscription is active before detach");

            // Drive Detach. The fake's get_VimTextBuffer now THROWS (the buffer is closing).
            Exception? thrown = null;
            try
            {
                source.Detach(view);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
            Assert.True(thrown == null,
                "Detach must not throw on a closing buffer (the cached text buffer is used)");
            Assert.False(subscribed.Contains(textBuffer),
                "the SwitchedMode subscription must be removed on detach (cached value used, no reflection re-read)");
        }

        // BP-5 (m3): ExitToolWindowInputMode must resolve CurrentController ONCE and route through
        // the single _routeDecision seam (the m38 pattern). DEVIATION (accepted): the plan's stated
        // RED ("returns false") does not reproduce — ExitToolWindowInputMode calls the private
        // ShouldRouteToolWindowKey directly, NOT the seam, so the test uses a COUNTING assertion:
        // the seam must be invoked exactly once. RED today: the seam is not on the path at all
        // (calls == 0), so the calls == 1 assertion fails.
        public static void Run_InputHandler_ExitToolWindowInputMode_SingleResolution()
        {
            // m60 (BP-D13): the reflection is gone — SetToolWindowStateForTest forces the tool-
            // window state, SetRouteDecisionForTest replaces the routing seam, and the Escape path
            // is driven through the public HandleKey (which calls ExitToolWindowInputMode) instead
            // of reflecting into the private method.
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var manager = new WindowManager(new FakeMonitorSelection());
                var handler = new InputHandler(null!, manager);

                // Force a tool-window state (Toolbox -> a GeneralToolWindowController).
                manager.SetToolWindowStateForTest(true, ToolWindowType.Toolbox);

                // Enter input mode so ExitToolWindowInputMode has something to exit.
                var controller = manager.CurrentController;
                Assert.True(controller != null, "a Toolbox controller is resolved");
                controller!.EnterInputMode();

                // The counting seam: the single routing decision. The lambda's side effect
                // (ExitInputMode) flips the mode so a double-resolution would observe the flipped
                // controller. The m3 fix must route ExitToolWindowInputMode through this seam.
                int calls = 0;
                handler.SetRouteDecisionForTest(c => { calls++; c?.ExitInputMode(); return true; });

                // Drive Escape through the public HandleKey path — the Escape branch calls
                // ExitToolWindowInputMode, which routes through the seam exactly once.
                handler.HandleKey(Keys.Escape, false, false, false);

                Assert.Equal(1, calls);
            }
        }

        // BP-6 (m4): a Ctrl/Alt/Win key-down while a leader sequence is pending ABORTS the sequence
        // (clears it) so the modifier passes through to VS (the Ctrl+chord works). Only Shift stays
        // transparent (needed for shifted sequence members like w,|). RED today: every modifier
        // key-down is consumed as transparent.
        public static void Run_LeaderSequenceMatcher_ModifierChordPassesThrough()
        {
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,|"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);

            matcher.HandleKey(Keys.Space, false, false, false, false);
            var w = matcher.HandleKey(Keys.W, false, false, false, false);
            Assert.Equal(LeaderResultKind.Consume, w.Kind);

            // Ctrl key-down while the sequence is pending: must ABORT (not swallowed) so the
            // Ctrl+chord passes through to VS. RED today: Consume + sequence stays active.
            var ctrl = matcher.HandleKey(Keys.ControlKey, false, false, false, false);
            Assert.Equal(LeaderResultKind.Abort, ctrl.Kind);
            Assert.False(matcher.IsActive, "a Ctrl key-down aborts the pending sequence (m4)");

            // A Shift key-down is still transparent (needed for shifted sequence members like w,|).
            var matcher2 = new LeaderSequenceMatcher(Keys.Space, bindings);
            matcher2.HandleKey(Keys.Space, false, false, false, false);
            matcher2.HandleKey(Keys.W, false, false, false, false);
            var shift = matcher2.HandleKey(Keys.ShiftKey, false, true, false, false);
            Assert.Equal(LeaderResultKind.Consume, shift.Kind);
            Assert.True(matcher2.IsActive, "a Shift key-down stays transparent (w,|)");
        }

        // BP-7 (m5): the shift-aware KeyNames.ToString overload maps the SHIFTED printable
        // characters for non-letter keys, so a binding like `w,}` (Shift+]) fires distinctly from
        // `w,]`. RED today: ToString(Keys.OemCloseBrackets, true) returns "]" -> the `w,}` binding
        // never fires.
        public static void Run_KeyNames_ShiftAwareNonLetter()
        {
            Assert.Equal("}", KeyNames.ToString(Keys.OemCloseBrackets, true));
            Assert.Equal("]", KeyNames.ToString(Keys.OemCloseBrackets, false));
            Assert.Equal("{", KeyNames.ToString(Keys.OemOpenBrackets, true));
            Assert.Equal("?", KeyNames.ToString(Keys.OemQuestion, true));
            Assert.Equal("_", KeyNames.ToString(Keys.OemMinus, true));

            // Round-trip: bind `w,}` -> drive Space, w, Shift+OemCloseBrackets -> Execute with `w,}`.
            var executed = 0;
            var bindings = new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                ["w,}"] = () => executed++,
            };
            var matcher = new LeaderSequenceMatcher(Keys.Space, bindings);
            matcher.HandleKey(Keys.Space, false, false, false, false);
            matcher.HandleKey(Keys.W, false, false, false, false);
            var result = matcher.HandleKey(Keys.OemCloseBrackets, false, true, false, false);
            Assert.Equal(LeaderResultKind.Execute, result.Kind);
            Assert.Equal<string?>("w,}", result.Sequence);
            Assert.Equal(1, executed);
        }

        // BP-8 (m7): the version-keyed PreviewTextCache is bounded (max 8) — Store evicts the lowest
        // version when the capacity is exceeded (the oldest snapshot is the least likely to be
        // re-read). RED today: all 10 versions are retained (no eviction).
        public static void Run_PreviewEditorHost_CachePruned()
        {
            var cache = new PreviewTextCache();
            for (int i = 1; i <= 10; i++)
            {
                cache.Store(i, "text" + i);
            }
            Assert.True(cache.Get(1) == null, "the oldest version is evicted (bounded cache, m7)");
            Assert.True(cache.Get(2) == null, "the second-oldest version is evicted (bounded cache, m7)");
            Assert.Equal("text10", cache.Get(10));
            Assert.Equal("text9", cache.Get(9));
            cache.Clear();
            Assert.True(cache.Get(10) == null, "Clear empties the cache");
        }

        // BP-10 (n1): the dead GotoDecision enum is deleted (the GotoDispatcher it documented was
        // inlined into ExecuteGoto). Reflection-absence is the only hermetic way to pin a deletion.
        public static void Run_GotoDecision_Deleted()
        {
            Assert.True(typeof(MyExtensionPackage).Assembly.GetType("MyExtension.Package.GotoDecision") == null,
                "the dead GotoDecision enum is deleted (n1)");
        }

        // BP-12 (n3): the test-only InputHandler ctor initializes the shared matcher/seam state
        // (_leaderMatcher/_simpleMatcher/_routeDecision) — a null matcher/seam means the shared init
        // is incomplete. PIN: the ctor already sets all three today.
        public static void Run_InputHandler_TestCtor_SharedInit()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var handler = new InputHandler(null!, new WindowManager(new FakeMonitorSelection()));
                var leaderMatcher = typeof(InputHandler).GetField("_leaderMatcher",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(handler);
                var simpleMatcher = typeof(InputHandler).GetField("_simpleMatcher",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(handler);
                var routeDecision = typeof(InputHandler).GetField("_routeDecision",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(handler);
                Assert.True(leaderMatcher != null, "the test-only ctor initializes _leaderMatcher (n3)");
                Assert.True(simpleMatcher != null, "the test-only ctor initializes _simpleMatcher (n3)");
                Assert.True(routeDecision != null, "the test-only ctor initializes _routeDecision (n3)");
                Assert.False(((LeaderSequenceMatcher)leaderMatcher!).IsActive, "the leader matcher starts inactive");
            }
        }

        // BP-16 (m17): the WindowManager ctor must check the AdviseSelectionEvents HRESULT and log
        // `[NeoVisual] selection events advise failed: 0x{hr:X8}` on failure (the C7 precedent) —
        // a failure must not silently leave _selectionEventsCookie = 0. RED today: the HRESULT is
        // discarded, so no failure line is logged.
        public static void Run_WindowManager_AdviseSelectionHresult()
        {
            using (var dir = new TempDir())
            {
                string logPath = System.IO.Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    using (TestScaffold.SetCurrentDispatcherAsUiThread())
                    {
                        Exception? thrown = null;
                        try
                        {
                            var manager = new WindowManager(new FailingMonitorSelection());
                        }
                        catch (Exception ex)
                        {
                            thrown = ex;
                        }
                        Assert.True(thrown == null,
                            "the ctor must not throw when AdviseSelectionEvents fails (m17)");
                        Telescope.Logging.LogFileWriter.Flush();
                        // The log file may not exist today (no failure line is logged) — treat a
                        // missing file as an empty log so the assertion below is the RED, not a
                        // FileNotFoundException from ReadAllTextShared.
                        string log = System.IO.File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                        Assert.True(log.Contains("[NeoVisual] selection events advise failed: 0x"),
                            "the failed AdviseSelectionEvents HRESULT must be logged (m17)");
                    }
                });
            }
        }

        // BP-20 (m21): GetController delegates to ResolveController for the decision, then applies
        // the instance cache — registered wins, else the per-type default (cached). PIN: the two
        // paths already agree today.
        public static void Run_WindowManager_SingleResolution()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var manager = new WindowManager(new FakeMonitorSelection());
                var toolboxController = new GeneralToolWindowController(ToolWindowType.Toolbox);
                manager.RegisterController(toolboxController);

                // Registered wins.
                Assert.True(ReferenceEquals(toolboxController, manager.GetController(ToolWindowType.Toolbox)),
                    "the registered controller wins (m21)");

                // Per-type default + cached.
                var first = manager.GetController(ToolWindowType.OutputWindow);
                Assert.True(first is GeneralToolWindowController, "OutputWindow gets a GeneralToolWindowController (m21)");
                var second = manager.GetController(ToolWindowType.OutputWindow);
                Assert.True(ReferenceEquals(first, second), "the per-type default is cached (m21)");

                // ResolveController agrees on the type.
                var controllersField = typeof(WindowManager).GetField("_controllers",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.True(controllersField != null, "WindowManager._controllers must exist");
                var controllers = (IReadOnlyDictionary<ToolWindowType, IToolWindowController>)controllersField!.GetValue(manager)!;
                Assert.True(ReferenceEquals(toolboxController, WindowManager.ResolveController(controllers, ToolWindowType.Toolbox)),
                    "ResolveController agrees with GetController for a registered type (m21)");
                Assert.True(WindowManager.ResolveController(controllers, ToolWindowType.OutputWindow) is GeneralToolWindowController,
                    "ResolveController agrees with GetController for a default type (m21)");
            }
        }

        // BP-21 (m22): OnWindowFocusChanged's GetProperty/GetGuidProperty calls run inside the
        // IVsSelectionEvents COM callback with no try/catch — a disposed frame's GetGuidProperty
        // throw must be swallowed, not escape the callback. RED today: the throw propagates out of
        // the ctor's OnWindowFocusChanged call.
        public static void Run_WindowManager_FocusChangeTryCatch()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var frame = new ThrowingGuidFrame();
                var monitor = new FrameReturningMonitorSelection(frame);
                Exception? thrown = null;
                try
                {
                    var manager = new WindowManager(monitor);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
                Assert.True(thrown == null,
                    $"a throwing GetGuidProperty must not escape OnWindowFocusChanged (m22): {thrown?.GetType().Name}: {thrown?.Message}");
            }
        }

        // BP-22 (n5): the no-box StyleFocusedSurface overload delegates to the box overload — both
        // are no-ops on the test host (no focused box/view) and do not throw. PIN: both overloads
        // already behave identically today.
        public static void Run_StyleFocusedSurface_Delegates()
        {
            Exception? thrown1 = null;
            try { TextMotionHelper.StyleFocusedSurface(false); }
            catch (Exception ex) { thrown1 = ex; }
            Exception? thrown2 = null;
            try { TextMotionHelper.StyleFocusedSurface(false, null); }
            catch (Exception ex) { thrown2 = ex; }
            Assert.True(thrown1 == null, "StyleFocusedSurface(false) is a no-op on the test host (n5)");
            Assert.True(thrown2 == null, "StyleFocusedSurface(false, null) is a no-op on the test host (n5)");
        }

        // BP-23 (n6): OnWindowFocusChanged runs two separate COM/visual-tree walks per focus change
        // (ComputeTextInputSurfaceFocused + ComputeFocusedTextBoxInCurrentToolWindow), each reading
        // GetProperty(VSFPROPID_DocView). The fix merges them into one walk that reads the DocView
        // ONCE and introduces a _findFocusedTextBox seam. RED today: the seam does not exist
        // (reflection-absence) — the field assertion fails.
        public static void Run_WindowManager_SingleWalk()
        {
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var frame = new CountingFrame();
                var monitor = new FrameReturningMonitorSelection(frame);
                var manager = new WindowManager(monitor);

                // The merged walk resolves the focused box through a single seam (n6). RED today:
                // the field does not exist -> GetField returns null -> the assertion fails.
                var field = typeof(WindowManager).GetField("_findFocusedTextBox",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.True(field != null, "the merged walk resolves the focused box through a single seam (n6)");

                var fakeBox = (System.Windows.Controls.TextBox)System.Runtime.Serialization.FormatterServices
                    .GetUninitializedObject(typeof(System.Windows.Controls.TextBox));
                field!.SetValue(manager, (Func<System.Windows.Controls.TextBox?>)(() => fakeBox));

                // Reset the DocView count (the ctor already ran the walk once), then invoke
                // OnWindowFocusChanged explicitly and assert the merged walk reads the DocView ONCE.
                frame.ResetDocViewReads();
                var method = typeof(WindowManager).GetMethod("OnWindowFocusChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.True(method != null, "OnWindowFocusChanged must exist (private instance)");
                method!.Invoke(manager, null);

                Assert.Equal(1, frame.DocViewReads);
            }
        }

        // BP-24 (n7): the text-input controller registers J/K/D0/D4 (the Command Window lacks 0/$
        // that the search box has). RED today: J/K/D0/D4 are missing from ActionKeys.
        public static void Run_TextInput_KeysJKD0D4()
        {
            var controller = new TextInputToolWindowController(ToolWindowType.CommandWindow);
            var keys = new List<Keys>(controller.ActionKeys);
            Assert.True(keys.Contains(Keys.J), "J is an action key (n7)");
            Assert.True(keys.Contains(Keys.K), "K is an action key (n7)");
            Assert.True(keys.Contains(Keys.D0), "D0 is an action key (n7)");
            Assert.True(keys.Contains(Keys.D4), "D4 is an action key (n7)");
        }

        // BP-25 (n8): ObjectSearchResultsWindow is reclassified as non-text-input (it starts in
        // input mode today, unlike FindResults1/2). RED today: IsTextInputType returns true.
        public static void Run_ToolWindowTypeResolver_ObjectSearchNotTextInput()
        {
            Assert.False(ToolWindowTypeResolver.IsTextInputType(ToolWindowType.ObjectSearchResultsWindow),
                "ObjectSearchResultsWindow is not a text-input surface (n8)");
        }

        // ================================================================
        // Section B — compile-RED batch (Code review fixes plan, Edit B)
        // Each test references a NOT-YET-EXISTING production member; the COMPILE ERROR is the
        // RED proof. The build-agent implements the members per the plan's BP steps.
        // ================================================================

        // BP-3 (M1): the sentinel is armed from the env var in production — the InputHandler ctor
        // reads StaleToolWindowSentinel.IsConfigured (a NEW internal static) to decide whether to
        // arm the per-key sentinel clock read. RED: `StaleToolWindowSentinel.IsConfigured` does
        // not exist yet -> compile error (CS0117). The m11 bug: the sentinel block reads
        // DateTime.UtcNow on every key-down even when the sentinel is not configured — a no-op in
        // production. The fix arms _sentinelArmed from IsConfigured so a disarmed sentinel never
        // touches the clock.
        public static void Run_InputHandler_SentinelArmedInProduction()
        {
            string original = Environment.GetEnvironmentVariable("NEOVISUAL_LOG_DIR");
            try
            {
                using (var dir = new TempDir())
                {
                    Environment.SetEnvironmentVariable("NEOVISUAL_LOG_DIR", dir.Path);
                    Assert.True(StaleToolWindowSentinel.IsConfigured,
                        "with NEOVISUAL_LOG_DIR set the sentinel is configured (BP-3/M1)");

                    using (TestScaffold.SetCurrentDispatcherAsUiThread())
                    {
                        var handler = new InputHandler(null!, new WindowManager(new FakeMonitorSelection()));

                        int clockReads = 0;
                        handler.Clock = () => { clockReads++; return DateTime.UtcNow; };

                        // Sentinel ARMED in production (NEOVISUAL_LOG_DIR set): the clock IS read.
                        handler.IsKeyOfInterest(Keys.H, true, false, false);
                        Assert.True(clockReads > 0,
                            "the sentinel is armed in production (NEOVISUAL_LOG_DIR set) -> the clock is read");
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("NEOVISUAL_LOG_DIR", original);
            }
        }

        // BP-4 (m2): the single-source physical-modifier VK set — KeyNames.IsPhysicalModifierKey
        // (a NEW internal static) is the one place that knows which VKs are physical modifiers.
        // RED: `KeyNames.IsPhysicalModifierKey` does not exist yet -> compile error (CS0117).
        public static void Run_LeaderSequenceMatcher_SingleModifierVkSet()
        {
            var physical = new[]
            {
                Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
                Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
                Keys.Menu, Keys.LMenu, Keys.RMenu,
                Keys.LWin, Keys.RWin,
            };
            foreach (var key in physical)
            {
                Assert.True(KeyNames.IsPhysicalModifierKey(key), $"{key} is a physical modifier VK");
            }
            Assert.False(KeyNames.IsPhysicalModifierKey(Keys.A), "A is not a physical modifier VK");
        }

        // BP-9 (m8): the Error List cache must be invalidated on the events that change the Error
        // List contents — ErrorListCacheDecision.ShouldInvalidateOnEvent (a NEW internal static).
        // RED: `ErrorListCacheDecision.ShouldInvalidateOnEvent` does not exist yet -> compile error
        // (CS0117). The m8 bug: the TTL-only cache (IsFresh) can serve a stale Error List after a
        // build/save/open/activate; the fix invalidates on those events.
        public static void Run_ErrorListGatherer_TtlInvalidated()
        {
            Assert.True(ErrorListCacheDecision.ShouldInvalidateOnEvent("build-done"),
                "a build changes the Error List -> invalidate");
            Assert.True(ErrorListCacheDecision.ShouldInvalidateOnEvent("document-saved"),
                "a document save can change the Error List -> invalidate");
            Assert.True(ErrorListCacheDecision.ShouldInvalidateOnEvent("document-opened"),
                "a document open can change the Error List -> invalidate");
            Assert.True(ErrorListCacheDecision.ShouldInvalidateOnEvent("window-activated"),
                "a window activation can change the Error List -> invalidate");
            Assert.False(ErrorListCacheDecision.ShouldInvalidateOnEvent("selection-changed"),
                "a selection change does not change the Error List -> keep the cache");
        }

        // BP-11 (n2): the kind-name resolution is shared — RoslynGatherers.KindName (a NEW internal
        // static) is the single source for the Implementation finder's Kind column. RED:
        // `RoslynGatherers.KindName` does not exist yet -> compile error (CS0117). The n2 bug: the
        // type/member kind-name logic is duplicated across the finders; the fix centralizes it.
        public static void Run_RoslynGatherers_KindNameShared()
        {
            Assert.Equal("Class", RoslynGatherers.KindName("Class", "Method", true));
            Assert.Equal("Method", RoslynGatherers.KindName("Class", "Method", false));
            Assert.Equal("Method", RoslynGatherers.KindName("", "Method", false));
        }

        // BP-13 (m14): the cached focused-box resolution must be invalidatable — the fix adds
        // SolutionExplorerController.InvalidateFocusedBoxCache() (a NEW internal method) so a
        // focus change re-resolves the box instead of serving the stale cache. RED:
        // `InvalidateFocusedBoxCache` does not exist yet -> compile error (CS1061).
        public static void Run_SolutionExplorer_FocusCacheInvalidated()
        {
            // m60 (BP-D13): the reflection is gone — SetFindFocusedTextBoxForTest replaces the
            // focused-box resolver (no private-field reads).
            var controller = new SolutionExplorerController(() => null!);

            // Two distinct boxes (never dereferenced — X is not a motion, so TryMoveFocusedSurface
            // returns before touching them). GetUninitializedObject skips the WPF ctor (no STA needed).
            var boxA = (System.Windows.Controls.TextBox)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(System.Windows.Controls.TextBox));
            var boxB = (System.Windows.Controls.TextBox)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(System.Windows.Controls.TextBox));
            int calls = 0;
            controller.SetFindFocusedTextBoxForTest(() => { calls++; return boxA; });

            // First TryMove resolves + caches boxA (the m47 single-walk contract).
            Assert.False(controller.TryMove(Keys.X), "an unmapped key is not consumed");
            Assert.Equal(1, calls);

            // The new seam: invalidate the cache, then the next TryMove re-resolves (the lambda
            // now returns boxB). RED today: no InvalidateFocusedBoxCache -> the stale boxA cache
            // is served and the lambda is never re-called (calls stays 1).
            controller.InvalidateFocusedBoxCache();
            controller.SetFindFocusedTextBoxForTest(() => { calls++; return boxB; });

            Assert.False(controller.TryMove(Keys.X), "an unmapped key is not consumed");
            Assert.Equal(2, calls);
        }

        // BP-14 (m15): FindFirstProjectNode must expand a collapsed SolutionFolder to reach the
        // project node beneath it. The fix makes FindFirstProjectNode `internal static` (currently
        // private static). RED: `SolutionExplorerController.FindFirstProjectNode` is not an
        // accessible static -> compile error (CS0122).
        public static void Run_SolutionExplorer_SolutionFolderExpanded()
        {
            var project = DispatchProxy.Create<EnvDTE.Project, DispatchAny>();
            var folder = DispatchProxy.Create<EnvDTE80.SolutionFolder, DispatchAny>();
            var solution = DispatchProxy.Create<EnvDTE.Solution, DispatchAny>();

            var projectNode = new FakeUIHierarchyItem(project, new FakeUIHierarchyItems(new List<EnvDTE.UIHierarchyItem>()));
            var folderItems = new FakeUIHierarchyItems(new List<EnvDTE.UIHierarchyItem> { projectNode });
            var folderNode = new FakeUIHierarchyItem(folder, folderItems);
            var solutionItems = new FakeUIHierarchyItems(new List<EnvDTE.UIHierarchyItem> { folderNode });
            var solutionNode = new FakeUIHierarchyItem(solution, solutionItems);

            // The folder starts COLLAPSED (its children are not enumerated).
            Assert.False(folderItems.Expanded, "precondition: the SolutionFolder starts collapsed");

            var found = SolutionExplorerController.FindFirstProjectNode(solutionNode);

            Assert.True(folderItems.Expanded, "FindFirstProjectNode expands the collapsed SolutionFolder (m15)");
            Assert.True(ReferenceEquals(found, projectNode), "the project node beneath the folder is returned");
        }

        // BP-15 (m16): the focus-keeper must STOP when the editor regains focus — the keeper must
        // not fight the user's return to the editor. The fix adds an `editorFocused` parameter to
        // FocusKeeperSchedule.Decide (a NEW parameter). RED: `Decide` has no `editorFocused`
        // parameter -> compile error (CS1739).
        public static void Run_FocusKeeper_StopsOnEditorFocus()
        {
            Assert.Equal(FocusKeeperSchedule.Decision.Stop,
                FocusKeeperSchedule.Decide(searchBoxFocused: true, editorFocused: true, elapsedMs: 0, escapeAttempts: 0, durationMs: 1500));
            Assert.Equal(FocusKeeperSchedule.Decision.Reassert,
                FocusKeeperSchedule.Decide(searchBoxFocused: false, editorFocused: false, elapsedMs: 0, escapeAttempts: 0, durationMs: 1500));
        }

        // BP-17 (m18): the motion-sample slice math is shared — TextMotionHelper.ComputeSlice (a
        // NEW internal static returning (int Start, int Length)) is the single source for the
        // caret window around the caret (4096 before, 8192 after, capped at the buffer end). RED:
        // `TextMotionHelper.ComputeSlice` does not exist yet -> compile error (CS0117).
        public static void Run_TextMotionHelper_SliceShared()
        {
            Assert.Equal((0, 100), TextMotionHelper.ComputeSlice(0, 100));
            Assert.Equal((904, 12288), TextMotionHelper.ComputeSlice(5000, 20000));
            Assert.Equal((14904, 5096), TextMotionHelper.ComputeSlice(19000, 20000));
        }

        // BP-18 (m19): the focus-keeper setup is shared — SolutionExplorerController.StartFocusKeeper
        // (a NEW internal method) is the single place that starts the keeper. RED:
        // `StartFocusKeeper` does not exist yet -> compile error (CS1061). The m19 bug: the keeper
        // setup is duplicated; the fix centralizes it (and a second StartFocusKeeper resets the
        // prior keeper instead of stacking).
        public static void Run_FocusKeeper_SetupShared()
        {
            // m60 (BP-D13): the reflection is gone — the FocusKeeperForTest seam reads the current
            // keeper handle (no private-field reads).
            using (TestScaffold.SetCurrentDispatcherAsUiThread())
            {
                var controller = new SolutionExplorerController(() => null!);

                controller.StartFocusKeeper(_ => false);
                Assert.True(controller.FocusKeeperForTest != null,
                    "StartFocusKeeper arms the _focusKeeper field (m19)");

                controller.StartFocusKeeper(_ => false);
                Assert.True(controller.FocusKeeperForTest != null,
                    "a second StartFocusKeeper resets the prior keeper, not stacks it (m19)");
            }
        }

        // BP-19 (m20): the block-caret brushes are frozen statics — BlockCaretStyle.WhiteBrush and
        // BlockCaretStyle.GlyphBrush (NEW frozen SolidColorBrush statics) so the adornment never
        // mutates a shared brush. RED: `BlockCaretStyle.WhiteBrush`/`GlyphBrush` do not exist yet
        // -> compile error (CS0117).
        public static void Run_BlockCaretAdornment_Frozen()
        {
            Assert.True(BlockCaretStyle.WhiteBrush.IsFrozen, "WhiteBrush is frozen (m20)");
            Assert.True(BlockCaretStyle.GlyphBrush.IsFrozen, "GlyphBrush is frozen (m20)");
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
    /// Test-local EnvDTE.DocumentEvents fake (BP-8): records the DocumentOpened add/remove so the
    /// test can assert Dispose() unhooks the subscription (the COM connection point is released).
    /// The other three events are no-ops — the gatherer only subscribes to DocumentOpened.
    /// </summary>
    internal sealed class FakeDocumentEvents : DocumentEvents
    {
        public int HookCount { get; private set; }
        public int UnhookCount { get; private set; }

        public event _dispDocumentEvents_DocumentOpenedEventHandler DocumentOpened
        {
            add { HookCount++; }
            remove { UnhookCount++; }
        }

        public event _dispDocumentEvents_DocumentClosingEventHandler DocumentClosing { add { } remove { } }
        public event _dispDocumentEvents_DocumentOpeningEventHandler DocumentOpening { add { } remove { } }
        public event _dispDocumentEvents_DocumentSavedEventHandler DocumentSaved { add { } remove { } }
    }

    /// <summary>
    /// Test-local tracking IDisposable (n25/BP-D23): records whether Dispose() ran, so the
    /// FocusKeeper reset test can prove ResetFocusKeeper disposes the injected keeper handle.
    /// </summary>
    internal sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// Minimal ITextView fake for the VimBufferSubscriptions tests (CR2). The subscription map
    /// only uses the view as a dictionary key (reference identity), so every member throws — the
    /// fake just needs to be a distinct, non-null ITextView instance.
    /// </summary>
    internal sealed class FakeTextView : IWpfTextView
    {
        // BP-1 (M5): optional document-view support — when a PropertyCollection is supplied, the
        // fake exposes a real Properties/TextBuffer (so VimModeTracker's ITextDocument.FilePath
        // discriminator can be exercised) and a raisable GotAggregateFocus. Backward-compatible:
        // `new FakeTextView()` keeps every member throwing (the VimBufferSubscriptions tests only
        // use the view as a dictionary key).
        private readonly Microsoft.VisualStudio.Utilities.PropertyCollection? _properties;
        private readonly FakeTextBuffer? _buffer;

        public FakeTextView(Microsoft.VisualStudio.Utilities.PropertyCollection? properties = null)
        {
            _properties = properties;
            _buffer = properties == null ? null : new FakeTextBuffer(properties);
        }

        public IBufferGraph BufferGraph => throw new NotImplementedException();
        public ITextCaret Caret => throw new NotImplementedException();
        public void Close() => throw new NotImplementedException();
        public event EventHandler? Closed { add { } remove { } }
        public void DisplayTextLineContainingBufferPosition(SnapshotPoint bufferPosition, double verticalDistance, ViewRelativePosition relativeTo) => throw new NotImplementedException();
        public void DisplayTextLineContainingBufferPosition(SnapshotPoint bufferPosition, double verticalDistance, ViewRelativePosition relativeTo, double? viewportWidthOverride, double? viewportHeightOverride) => throw new NotImplementedException();
        public SnapshotSpan GetTextElementSpan(SnapshotPoint point) => throw new NotImplementedException();
        public IWpfTextViewLine GetTextViewLineContainingBufferPosition(SnapshotPoint bufferPosition) => throw new NotImplementedException();
        ITextViewLine ITextView.GetTextViewLineContainingBufferPosition(SnapshotPoint bufferPosition) => throw new NotImplementedException();
        public event EventHandler? GotAggregateFocus;
        public void RaiseGotAggregateFocus() => GotAggregateFocus?.Invoke(this, EventArgs.Empty);
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
        public Microsoft.VisualStudio.Utilities.PropertyCollection Properties =>
            _properties ?? throw new NotImplementedException();
        public ITrackingSpan? ProvisionalTextHighlight { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public void QueueSpaceReservationStackRefresh() => throw new NotImplementedException();
        public ITextViewRoleSet Roles => throw new NotImplementedException();
        public ITextSelection Selection => throw new NotImplementedException();
        public ITextBuffer TextBuffer =>
            _buffer ?? throw new NotImplementedException();
        public ITextDataModel TextDataModel => throw new NotImplementedException();
        public ITextSnapshot TextSnapshot => throw new NotImplementedException();
        public IWpfTextViewLineCollection TextViewLines => throw new NotImplementedException();
        ITextViewLineCollection ITextView.TextViewLines => throw new NotImplementedException();
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
        // IWpfTextView members (BP-1/M5 — the VimModeTracker.TextViewCreated signature).
        public IAdornmentLayer GetAdornmentLayer(string name) => throw new NotImplementedException();
        public ISpaceReservationManager GetSpaceReservationManager(string name) => throw new NotImplementedException();
        public System.Windows.FrameworkElement VisualElement => throw new NotImplementedException();
        public System.Windows.Media.Brush Background { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public event EventHandler<BackgroundBrushChangedEventArgs>? BackgroundBrushChanged { add { } remove { } }
        public IFormattedLineSource FormattedLineSource => throw new NotImplementedException();
        public ILineTransformSource LineTransformSource => throw new NotImplementedException();
        public double ZoomLevel { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public event EventHandler<ZoomLevelChangedEventArgs>? ZoomLevelChanged { add { } remove { } }
    }

    /// <summary>
    /// BP-1 (M5): minimal ITextBuffer fake — only Properties is real (VimModeTracker reads
    /// <c>view.TextBuffer.Properties</c>); every other member throws.
    /// </summary>
    internal sealed class FakeTextBuffer : ITextBuffer
    {
        private readonly Microsoft.VisualStudio.Utilities.PropertyCollection _properties;

        public FakeTextBuffer(Microsoft.VisualStudio.Utilities.PropertyCollection properties)
        {
            _properties = properties;
        }

        public Microsoft.VisualStudio.Utilities.PropertyCollection Properties => _properties;
        public Microsoft.VisualStudio.Utilities.IContentType ContentType => throw new NotImplementedException();
        public ITextSnapshot CurrentSnapshot => throw new NotImplementedException();
        public ITextEdit CreateEdit(EditOptions options, int? reiteratedVersionNumber, object? editTag) => throw new NotImplementedException();
        public ITextEdit CreateEdit() => throw new NotImplementedException();
        public IReadOnlyRegionEdit CreateReadOnlyRegionEdit() => throw new NotImplementedException();
        public bool EditInProgress => throw new NotImplementedException();
        public void TakeThreadOwnership() => throw new NotImplementedException();
        public bool CheckEditAccess() => throw new NotImplementedException();
        public event EventHandler<SnapshotSpanEventArgs>? ReadOnlyRegionsChanged { add { } remove { } }
        public event EventHandler<TextContentChangedEventArgs>? Changed { add { } remove { } }
        public event EventHandler<TextContentChangedEventArgs>? ChangedLowPriority { add { } remove { } }
        public event EventHandler<TextContentChangedEventArgs>? ChangedHighPriority { add { } remove { } }
        public event EventHandler<TextContentChangingEventArgs>? Changing { add { } remove { } }
        public event EventHandler? PostChanged { add { } remove { } }
        public event EventHandler<ContentTypeChangedEventArgs>? ContentTypeChanged { add { } remove { } }
        public void ChangeContentType(Microsoft.VisualStudio.Utilities.IContentType newContentType, object? editTag) => throw new NotImplementedException();
        public ITextSnapshot Insert(int position, string text) => throw new NotImplementedException();
        public ITextSnapshot Delete(Span deleteSpan) => throw new NotImplementedException();
        public ITextSnapshot Replace(Span replaceSpan, string text) => throw new NotImplementedException();
        public bool IsReadOnly(int position) => throw new NotImplementedException();
        public bool IsReadOnly(int position, bool isEdit) => throw new NotImplementedException();
        public bool IsReadOnly(Span span) => throw new NotImplementedException();
        public bool IsReadOnly(Span span, bool isEdit) => throw new NotImplementedException();
        public NormalizedSpanCollection GetReadOnlyExtents(Span span) => throw new NotImplementedException();
    }

    /// <summary>
    /// BP-1 (M5): minimal ITextDocument fake — only FilePath is real (VimModeTracker reads
    /// <c>doc.FilePath</c>); every other member throws.
    /// </summary>
    internal sealed class FakeTextDocument : ITextDocument
    {
        public FakeTextDocument(string filePath) { FilePath = filePath; }

        public string FilePath { get; }
        public ITextBuffer TextBuffer => throw new NotImplementedException();
        public bool IsDirty => throw new NotImplementedException();
        public DateTime LastSavedTime => throw new NotImplementedException();
        public DateTime LastContentModifiedTime => throw new NotImplementedException();
        public System.Text.Encoding Encoding { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public void SetEncoderFallback(System.Text.EncoderFallback fallback) => throw new NotImplementedException();
        public event EventHandler<EncodingChangedEventArgs>? EncodingChanged { add { } remove { } }
        public event EventHandler<TextDocumentFileActionEventArgs>? FileActionOccurred { add { } remove { } }
        public event EventHandler? DirtyStateChanged { add { } remove { } }
        public void Rename(string newFilePath) => throw new NotImplementedException();
        public ReloadResult Reload() => throw new NotImplementedException();
        public ReloadResult Reload(EditOptions options) => throw new NotImplementedException();
        public bool IsReloading => throw new NotImplementedException();
        public void Save() => throw new NotImplementedException();
        public void SaveAs(string filePath, bool overwrite) => throw new NotImplementedException();
        public void SaveAs(string filePath, bool overwrite, bool makeReadOnly) => throw new NotImplementedException();
        public void SaveAs(string filePath, bool overwrite, Microsoft.VisualStudio.Utilities.IContentType newContentType) => throw new NotImplementedException();
        public void SaveAs(string filePath, bool overwrite, bool makeReadOnly, Microsoft.VisualStudio.Utilities.IContentType newContentType) => throw new NotImplementedException();
        public void SaveCopy(string filePath, bool overwrite) => throw new NotImplementedException();
        public void SaveCopy(string filePath, bool overwrite, bool makeReadOnly) => throw new NotImplementedException();
        public void UpdateDirtyState(bool isDirty, DateTime lastSavedTime) => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
    }

    /// <summary>
    /// BP-2 (M8): minimal EnvDTE.UIHierarchyItem fake — only UIHierarchyItems + Object are real
    /// (MapChildren reads them); every other member throws. The fake models the real VS behavior
    /// where a COLLAPSED folder's UIHierarchyItems enumerates nothing.
    /// </summary>
    internal sealed class FakeUIHierarchyItem : EnvDTE.UIHierarchyItem
    {
        private readonly EnvDTE.UIHierarchyItems _uiHierarchyItems;
        private readonly object _object;

        public FakeUIHierarchyItem(object projectItem, EnvDTE.UIHierarchyItems uiHierarchyItems)
        {
            _object = projectItem;
            _uiHierarchyItems = uiHierarchyItems;
        }

        public EnvDTE.UIHierarchyItems UIHierarchyItems => _uiHierarchyItems;
        public object Object => _object;
        public EnvDTE.DTE DTE => throw new NotImplementedException();
        public EnvDTE.UIHierarchyItems Collection => throw new NotImplementedException();
        public string Name => throw new NotImplementedException();
        public bool IsSelected { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public void Select(EnvDTE.vsUISelectionType how) => throw new NotImplementedException();
    }

    /// <summary>
    /// BP-2 (M8): minimal EnvDTE.UIHierarchyItems fake — Expanded + enumeration are real (a
    /// collapsed folder's children are NOT enumerated, mirroring VS); every other member throws.
    /// </summary>
    internal sealed class FakeUIHierarchyItems : EnvDTE.UIHierarchyItems
    {
        private readonly List<EnvDTE.UIHierarchyItem> _children;

        public FakeUIHierarchyItems(List<EnvDTE.UIHierarchyItem> children)
        {
            _children = children;
        }

        public bool Expanded { get; set; }
        public int Count => Expanded ? _children.Count : 0;
        public EnvDTE.UIHierarchyItem Item(object index) => _children[(int)index - 1];
        public System.Collections.IEnumerator GetEnumerator() =>
            Expanded ? _children.GetEnumerator() : Enumerable.Empty<EnvDTE.UIHierarchyItem>().GetEnumerator();
        public EnvDTE.DTE DTE => throw new NotImplementedException();
        public object Parent => throw new NotImplementedException();
    }

    /// <summary>
    /// BP-2 (M8): minimal EnvDTE.ProjectItem fake. EnvDTE.ProjectItem exposes COM NAMED indexed
    /// properties (FileNames[short], IsOpen[string], Extender[string]) that C# source cannot
    /// implement (the compiler rejects both `this[...]` and named-indexer syntax), so the fake is
    /// built with System.Reflection.DispatchProxy — only Kind/Name/FileCount/FileNames are real
    /// (MapChildren reads them); every other member throws. PUBLIC + NOT sealed: DispatchProxy
    /// requires a non-sealed base type that the dynamically-generated proxy type can access
    /// (internal -> TypeLoadException "Access is denied"; sealed -> ArgumentException).
    /// </summary>
    public class DispatchProjectItem : DispatchProxy
    {
        private string _kind = "";
        private string _name = "";
        private string[] _fileNames = new string[0];

        public static EnvDTE.ProjectItem Create(string kind, string name, string[] fileNames)
        {
            var proxy = DispatchProxy.Create<EnvDTE.ProjectItem, DispatchProjectItem>();
            var typed = (DispatchProjectItem)(object)proxy;
            typed._kind = kind;
            typed._name = name;
            typed._fileNames = fileNames;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Kind": return _kind;
                case "get_Name": return _name;
                case "get_FileCount": return (short)_fileNames.Length;
                case "get_FileNames": return _fileNames[(short)args![0]! - 1]; // 1-based
                default: throw new NotImplementedException(targetMethod?.Name);
            }
        }
    }

    /// <summary>
    /// Test-local EnvDTE.Window fake (BP-20): only LinkedWindowFrame is read by the hermetic
    /// BuildActiveWindows paths (the cache-hit path uses ReferenceEquals only; the cache-miss
    /// path reads LinkedWindowFrame, which returns null -> GetLinkedWindowsList returns empty).
    /// Every other member throws — the fake just needs to be a distinct, non-null EnvDTE.Window.
    /// BP-12 (M6): unsealed + LinkedWindowFrame virtual so the test can add a linked variant
    /// (returns the parent) and a throwing variant (a stale RCW) for GetLinkedWindowsList.
    /// </summary>
    internal class FakeWindow : EnvDTE.Window
    {
        public virtual EnvDTE.Window LinkedWindowFrame => null!;
        public bool AutoHides { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public virtual string Caption { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public EnvDTE.Windows Collection => throw new NotImplementedException();
        public EnvDTE.ContextAttributes ContextAttributes => throw new NotImplementedException();
        public EnvDTE.Document Document => throw new NotImplementedException();
        public object get_DocumentData(string bstrWhichData) => throw new NotImplementedException();
        public EnvDTE.DTE DTE => throw new NotImplementedException();
        public int Height { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public System.IntPtr HWnd => throw new NotImplementedException();
        public bool IsFloating { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string Kind => throw new NotImplementedException();
        public int Left { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool Linkable { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public EnvDTE.LinkedWindows LinkedWindows => throw new NotImplementedException();
        public object Object => throw new NotImplementedException();
        public string ObjectKind => throw new NotImplementedException();
        public EnvDTE.Project Project => throw new NotImplementedException();
        public EnvDTE.ProjectItem ProjectItem => throw new NotImplementedException();
        public object Selection => throw new NotImplementedException();
        public int Top { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public virtual vsWindowType Type => throw new NotImplementedException();
        public bool Visible { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int Width { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public vsWindowState WindowState { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public void Activate() => throw new NotImplementedException();
        public void Attach(System.IntPtr lWindowHandle) => throw new NotImplementedException();
        public void Close(vsSaveChanges SaveChanges = vsSaveChanges.vsSaveChangesNo) => throw new NotImplementedException();
        public void Detach() => throw new NotImplementedException();
        public void SetFocus() => throw new NotImplementedException();
        public void SetKind(vsWindowType eKind) => throw new NotImplementedException();
        public void SetSelectionContainer(ref object[] Objects) => throw new NotImplementedException();
        public void SetTabPicture(object Picture) => throw new NotImplementedException();
    }

    /// <summary>
    /// BP-12 (M6): a FakeWindow whose LinkedWindowFrame returns the given parent — the healthy
    /// linked window GetLinkedWindowsList must keep.
    /// </summary>
    internal sealed class LinkedWindow : FakeWindow
    {
        private readonly EnvDTE.Window _parent;
        public LinkedWindow(EnvDTE.Window parent) { _parent = parent; }
        public override EnvDTE.Window LinkedWindowFrame => _parent;
    }

    /// <summary>
    /// BP-12 (M6): a FakeWindow whose LinkedWindowFrame THROWS — a stale/disconnected RCW. The
    /// per-window try/catch in GetLinkedWindowsList must skip it instead of propagating.
    /// </summary>
    internal sealed class ThrowingLinkedWindow : FakeWindow
    {
        public override EnvDTE.Window LinkedWindowFrame => throw new InvalidOperationException("stale RCW");
    }

    /// <summary>
    /// BP-12 (M2): a FakeWindow with REAL Caption/Type/LinkedWindowFrame — the healthy adapter's
    /// DTE window in Run_WindowFrameAdapter_LinkedIsolation. The Properties-window quirk: a
    /// ToolWindow active + a Properties window with the same caption compare equal via
    /// WindowFrameUtils.CompareWindows.
    /// </summary>
    internal sealed class ComparableWindow : FakeWindow
    {
        private readonly string _caption;
        private readonly vsWindowType _type;
        private readonly EnvDTE.Window _linked;

        public ComparableWindow(string caption, vsWindowType type, EnvDTE.Window? linked = null)
        {
            _caption = caption;
            _type = type;
            _linked = linked ?? this;
        }

        public override string Caption
        {
            get => _caption;
            set => throw new NotImplementedException();
        }

        public override vsWindowType Type => _type;

        public override EnvDTE.Window LinkedWindowFrame => _linked;
    }

    /// <summary>
    /// BP-12 (M2): a FakeWindow whose Caption/Type THROW — a stale/disconnected RCW adapter. The
    /// per-window try/catch around CompareWindows in FindActive/LinkedTo must skip it instead of
    /// propagating (the F7 residual the M2 fix closes).
    /// </summary>
    internal sealed class ThrowingWindow : FakeWindow
    {
        // The base FakeWindow's Caption/Type already throw; LinkedWindowFrame throws too (a stale
        // RCW's frame reads fail).
        public override EnvDTE.Window LinkedWindowFrame => throw new InvalidOperationException("stale RCW");
    }

    /// <summary>
    /// BP-16 (m13): an IVsWindowFrame whose GetProperty(VSFPROPID_DocView) returns a given object
    /// (a FakeWindow) so VsShellUtilities.GetWindowObject resolves a non-null active window for
    /// Run_WindowNavigator_NullActive. Every other member throws.
    /// </summary>
    internal sealed class WindowFrameWithDocView : IVsWindowFrame
    {
        private readonly object _docView;
        public WindowFrameWithDocView(object docView) { _docView = docView; }

        public int GetProperty(int propid, out object pvar)
        {
            if (propid == (int)__VSFPROPID.VSFPROPID_DocView)
            {
                pvar = _docView;
                return 0; // S_OK
            }
            pvar = null!;
            return unchecked((int)0x80004001); // E_NOTIMPL
        }
        public int Show() => throw new NotImplementedException();
        public int Hide() => throw new NotImplementedException();
        public int IsVisible() => throw new NotImplementedException();
        public int ShowNoActivate() => throw new NotImplementedException();
        public int CloseFrame(uint grfSaveOptions) => throw new NotImplementedException();
        public int SetFramePos(VSSETFRAMEPOS dwSFP, ref Guid rguidRelativeTo, int x, int y, int cx, int cy) => throw new NotImplementedException();
        public int GetFramePos(VSSETFRAMEPOS[] pdwSFP, out Guid pguidRelativeTo, out int px, out int py, out int pcx, out int pcy) => throw new NotImplementedException();
        public int SetProperty(int propid, object var) => throw new NotImplementedException();
        public int GetGuidProperty(int propid, out Guid pguid) => throw new NotImplementedException();
        public int SetGuidProperty(int propid, ref Guid rguid) => throw new NotImplementedException();
        public int QueryViewInterface(ref Guid riid, out IntPtr ppv) => throw new NotImplementedException();
        public int IsOnScreen(out int pfOnScreen) => throw new NotImplementedException();
    }

    /// <summary>
    /// BP-21 (m22): an IVsWindowFrame whose GetProperty(VSFPROPID_Type) returns S_OK with a Tool
    /// type and whose GetGuidProperty THROWS — a disposed frame inside OnWindowFocusChanged. The
    /// try/catch the m22 fix adds must swallow the throw instead of letting it escape the COM
    /// callback.
    /// </summary>
    internal sealed class ThrowingGuidFrame : IVsWindowFrame
    {
        public int GetProperty(int propid, out object pvar)
        {
            if (propid == (int)__VSFPROPID.VSFPROPID_Type)
            {
                pvar = (int)__WindowFrameTypeFlags.WINDOWFRAMETYPE_Tool;
                return 0; // S_OK
            }
            pvar = null!;
            return unchecked((int)0x80004001); // E_NOTIMPL
        }
        public int GetGuidProperty(int propid, out Guid pguid)
        {
            throw new InvalidOperationException("disposed frame");
        }
        public int Show() => throw new NotImplementedException();
        public int Hide() => throw new NotImplementedException();
        public int IsVisible() => throw new NotImplementedException();
        public int ShowNoActivate() => throw new NotImplementedException();
        public int CloseFrame(uint grfSaveOptions) => throw new NotImplementedException();
        public int SetFramePos(VSSETFRAMEPOS dwSFP, ref Guid rguidRelativeTo, int x, int y, int cx, int cy) => throw new NotImplementedException();
        public int GetFramePos(VSSETFRAMEPOS[] pdwSFP, out Guid pguidRelativeTo, out int px, out int py, out int pcx, out int pcy) => throw new NotImplementedException();
        public int SetProperty(int propid, object var) => throw new NotImplementedException();
        public int SetGuidProperty(int propid, ref Guid rguid) => throw new NotImplementedException();
        public int QueryViewInterface(ref Guid riid, out IntPtr ppv) => throw new NotImplementedException();
        public int IsOnScreen(out int pfOnScreen) => throw new NotImplementedException();
    }

    /// <summary>
    /// BP-23 (n6): an IVsWindowFrame that counts GetProperty(VSFPROPID_DocView) reads — the merged
    /// single walk must read the DocView ONCE per focus change. GetProperty(VSFPROPID_Type) returns
    /// a Tool type; GetGuidProperty returns the Toolbox GUID; GetProperty(VSFPROPID_DocView) returns
    /// a WPF Grid (a FrameworkElement so the descendant walk runs).
    /// </summary>
    internal sealed class CountingFrame : IVsWindowFrame
    {
        public int DocViewReads { get; private set; }
        public void ResetDocViewReads() => DocViewReads = 0;

        public int GetProperty(int propid, out object pvar)
        {
            if (propid == (int)__VSFPROPID.VSFPROPID_Type)
            {
                pvar = (int)__WindowFrameTypeFlags.WINDOWFRAMETYPE_Tool;
                return 0; // S_OK
            }
            if (propid == (int)__VSFPROPID.VSFPROPID_DocView)
            {
                DocViewReads++;
                pvar = new System.Windows.Controls.Grid();
                return 0; // S_OK
            }
            pvar = null!;
            return unchecked((int)0x80004001); // E_NOTIMPL
        }
        public int GetGuidProperty(int propid, out Guid pguid)
        {
            pguid = new Guid(ToolWindowGuids80.Toolbox);
            return 0; // S_OK
        }
        public int Show() => throw new NotImplementedException();
        public int Hide() => throw new NotImplementedException();
        public int IsVisible() => throw new NotImplementedException();
        public int ShowNoActivate() => throw new NotImplementedException();
        public int CloseFrame(uint grfSaveOptions) => throw new NotImplementedException();
        public int SetFramePos(VSSETFRAMEPOS dwSFP, ref Guid rguidRelativeTo, int x, int y, int cx, int cy) => throw new NotImplementedException();
        public int GetFramePos(VSSETFRAMEPOS[] pdwSFP, out Guid pguidRelativeTo, out int px, out int py, out int pcx, out int pcy) => throw new NotImplementedException();
        public int SetProperty(int propid, object var) => throw new NotImplementedException();
        public int SetGuidProperty(int propid, ref Guid rguid) => throw new NotImplementedException();
        public int QueryViewInterface(ref Guid riid, out IntPtr ppv) => throw new NotImplementedException();
        public int IsOnScreen(out int pfOnScreen) => throw new NotImplementedException();
    }

    /// <summary>
    /// BP-16 (m17): an IVsMonitorSelection whose AdviseSelectionEvents returns E_FAIL — the ctor
    /// must log the failure instead of silently leaving _selectionEventsCookie = 0.
    /// </summary>
    internal sealed class FailingMonitorSelection : IVsMonitorSelection
    {
        public int AdviseSelectionEvents(IVsSelectionEvents pSink, out uint pdwCookie)
        {
            pdwCookie = 0;
            return unchecked((int)0x80004005); // E_FAIL
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

    /// <summary>
    /// BP-21/BP-23 (m22/n6): an IVsMonitorSelection whose GetCurrentElementValue returns a given
    /// frame — so RefreshCurrentWindow resolves the frame the test wants OnWindowFocusChanged to
    /// process (the ctor's own OnWindowFocusChanged call runs the same path).
    /// </summary>
    internal sealed class FrameReturningMonitorSelection : IVsMonitorSelection
    {
        private readonly IVsWindowFrame _frame;
        public FrameReturningMonitorSelection(IVsWindowFrame frame) { _frame = frame; }

        public int AdviseSelectionEvents(IVsSelectionEvents pSink, out uint pdwCookie)
        {
            pdwCookie = 1;
            return 0; // S_OK
        }
        public int UnadviseSelectionEvents(uint dwCookie) => 0;
        public int GetCurrentElementValue(uint elementid, out object pvarValue)
        {
            pvarValue = _frame;
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

    /// <summary>
    /// BP-14 (m15): a DispatchProxy that returns a default value for every member — used to build
    /// EnvDTE.Project / EnvDTE.Solution / EnvDTE80.SolutionFolder instances for the
    /// FindFirstProjectNode type checks (only the `is` checks run; no member is invoked).
    /// </summary>
    public class DispatchAny : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType;
            if (returnType == typeof(void)) return null;
            if (returnType == typeof(bool)) return false;
            if (returnType == typeof(int)) return 0;
            if (returnType == typeof(string)) return "";
            if (returnType == typeof(Guid)) return Guid.Empty;
            return null;
        }
    }
}

// BP-8 (m13): a test-local stand-in for VsVim's Vim.IVimBuffer. VsVimModeSource resolves the
// interface by FullName string ("Vim.IVimBuffer") in GetInterfaceMethod, so a type in a namespace
// literally named `Vim` is matched — no Vim.Core.dll reference is needed. Only the member the
// close path touches (get_VimTextBuffer) is declared.
namespace Vim
{
    internal interface IVimBuffer
    {
        object? get_VimTextBuffer();
    }
}

namespace NeoVisual.Tests
{
    /// <summary>
    /// BP-8 (m13): a Vim.IVimBuffer whose get_VimTextBuffer returns the text buffer on the FIRST
    /// call (the buffer was healthy when the SwitchedMode subscription was created) and THROWS on
    /// every later call (the buffer is closing — the reflection re-read on the close path fails).
    /// This models the m13 scenario: OnBufferClosed must use the cached _bufferToTextBuffer value
    /// instead of re-resolving via reflection on the closing buffer.
    /// </summary>
    internal sealed class ClosingVimBuffer : Vim.IVimBuffer
    {
        private int _calls;
        private readonly object _textBuffer;

        public ClosingVimBuffer(object textBuffer)
        {
            _textBuffer = textBuffer;
        }

        public object? get_VimTextBuffer()
        {
            _calls++;
            if (_calls > 1)
            {
                throw new InvalidOperationException("buffer is closing");
            }
            return _textBuffer;
        }
    }
}