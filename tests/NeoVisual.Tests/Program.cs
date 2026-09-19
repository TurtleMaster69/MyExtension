using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CardinalNavigation;
using Microsoft.VisualStudio.Shell.Interop;
using MyExtension;

namespace NeoVisual.Tests
{
    /// <summary>
    /// Minimal, dependency-free test runner for the NeoVisual extension's pure logic. Each public
    /// static method named <c>Run_*</c> on <see cref="Tests"/> is discovered and executed; any
    /// thrown exception fails that test. Exit code is the number of failures.
    ///
    /// <para/>
    /// <b>Scope:</b> only logic that is testable without a running Visual Studio — keybinding
    /// parsing, tool-window type classification, navigation helpers. The VS-coupled layers
    /// (keyboard hook, WindowMatrix over IVsWindowFrame, DTE calls) are exercised end-to-end by
    /// <c>tools/iterate-telescope.ps1</c> and the F5 harness instead.
    ///
    /// <para/>
    /// <b>Running a subset:</b> pass a substring filter as the first argument; only tests whose
    /// name contains it run (e.g. <c>-- Keybinding</c>). Pass <c>--list</c> to print tests.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var methods = typeof(Tests).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name.StartsWith("Run_", StringComparison.Ordinal))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToList();

            if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine("Available tests:");
                foreach (var m in methods)
                {
                    Console.WriteLine($"  {m.Name}");
                }
                return 0;
            }

            string? filter = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(filter))
            {
                methods = methods.Where(m => m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            if (methods.Count == 0)
            {
                Console.WriteLine("No tests matched.");
                return 1;
            }

            int passed = 0;
            int failed = 0;
            foreach (var method in methods)
            {
                try
                {
                    method.Invoke(null, null);
                    Console.WriteLine($"PASS  {method.Name}");
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL  {method.Name}: {Unwrap(ex).Message}");
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{passed} passed, {failed} failed, {methods.Count} total.");
            return failed;
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException tie && tie.InnerException != null)
            {
                ex = tie.InnerException;
            }
            return ex;
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
            // o/Enter/r/m/a are the non-hjkl action keys.
            var keys = new List<Keys>(controller.ActionKeys);
            Assert.True(keys.Contains(Keys.O), "o is an action key");
            Assert.True(keys.Contains(Keys.Enter), "Enter is an action key");
            Assert.True(keys.Contains(Keys.R), "r is an action key");
            Assert.True(keys.Contains(Keys.M), "m is an action key");
            Assert.True(keys.Contains(Keys.A), "a is an action key");
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
            Assert.Equal(TextMotion.Left, TextInputToolWindowController.MapMotion(Keys.H, false));
            Assert.Equal(TextMotion.Right, TextInputToolWindowController.MapMotion(Keys.L, false));
            Assert.Equal(TextMotion.NextWord, TextInputToolWindowController.MapMotion(Keys.W, false));
            Assert.Equal(TextMotion.PrevWord, TextInputToolWindowController.MapMotion(Keys.B, false));
            Assert.Equal(TextMotion.EndWord, TextInputToolWindowController.MapMotion(Keys.E, false));
        }

        public static void Run_TextInput_MapInsertMotions()
        {
            // A (Shift+a) = insert at end; a = insert after caret; I (Shift+i) = insert at start.
            Assert.Equal(TextMotion.InsertEnd, TextInputToolWindowController.MapMotion(Keys.A, true));
            Assert.Equal(TextMotion.InsertAfter, TextInputToolWindowController.MapMotion(Keys.A, false));
            Assert.Equal(TextMotion.InsertStart, TextInputToolWindowController.MapMotion(Keys.I, true));
            // A bare i (no shift) is the generic insert handled by InputHandler, not a motion.
            Assert.Equal(null, TextInputToolWindowController.MapMotion(Keys.I, false));
            Assert.Equal(null, TextInputToolWindowController.MapMotion(Keys.X, false));
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
            var r = new RectCoordinate(1, 2, 3, 4);
            Assert.Equal(1, r.x);
            Assert.Equal(2, r.y);
            Assert.Equal(3, r.width);
            Assert.Equal(4, r.height);
        }
    }

    internal static class Assert
    {
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception($"Expected [{expected}] but got [{actual}]");
            }
        }

        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        public static void False(bool condition, string message)
        {
            if (condition)
            {
                throw new Exception(message);
            }
        }
    }
}