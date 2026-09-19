using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Telescope.Tests
{
    /// <summary>
    /// Minimal, dependency-free test runner for the Telescope unit tests. Each public static
    /// method named <c>Run_*</c> on <see cref="Tests"/> is discovered and executed; any thrown
    /// exception fails that test. Exit code is the number of failures.
    ///
    /// <para/>
    /// <b>Why not xUnit/MSTest:</b> those frameworks are not in the local NuGet cache and pulling
    /// them needs network. This runner is hermetic and guaranteed to build/run offline, which
    /// suits a solo-iteration harness. Tests are exercised via <c>dotnet run --project tests/Telescope.Tests</c>.
    ///
    /// <para/>
    /// <b>Running a subset:</b> pass a substring filter as the first argument; only tests whose
    /// name contains it run (e.g. <c>dotnet run --project tests/Telescope.Tests -- KeyHandler</c>
    /// runs just the overlay key-handler tests). Pass <c>--list</c> to print the available tests.
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
        private static FinderEntry E(string s) => new FinderEntry(s);

        public static void Run_ResultsFormatter_Empty()
        {
            Assert.Equal("", ResultsFormatter.ToText(new List<FinderEntry>(), 0));
        }

        public static void Run_ResultsFormatter_SingleSelected()
        {
            var list = new List<FinderEntry> { E("alpha") };
            Assert.Equal("> alpha", ResultsFormatter.ToText(list, 0));
        }

        public static void Run_ResultsFormatter_SelectionMarkerOnIndex()
        {
            var list = new List<FinderEntry> { E("a"), E("b"), E("c") };
            Assert.Equal("  a\n> b\n  c", ResultsFormatter.ToText(list, 1));
        }

        public static void Run_ResultsFormatter_NewlinesBetween()
        {
            var list = new List<FinderEntry> { E("a"), E("b"), E("c") };
            Assert.Equal("> a\n  b\n  c", ResultsFormatter.ToText(list, 0));
        }

        public static void Run_LogFileWriter_WritesAndClearsFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_tests_" + Guid.NewGuid().ToString("N"));
            string logPath = Path.Combine(dir, "neovisual-exp.log");
            string debugPath = Path.Combine(dir, "neovisual-main.log");
            string originalLog = LogFileWriter.LogPath;
            string originalDebug = LogFileWriter.DebugLogPath;
            try
            {
                LogFileWriter.LogPath = logPath;
                LogFileWriter.DebugLogPath = debugPath;

                LogFileWriter.Write("structured line");
                LogFileWriter.WriteDebug("debug line");
                Assert.True(File.ReadAllText(logPath).Contains("structured line"), "structured log should contain the NeoVisual line");
                Assert.True(File.ReadAllText(debugPath).Contains("debug line"), "debug log should contain the debug line");

                // The two files are separate: the structured line is NOT in the debug file and
                // the debug line is NOT in the structured file.
                Assert.False(File.ReadAllText(debugPath).Contains("structured line"), "debug file should not contain structured lines");
                Assert.False(File.ReadAllText(logPath).Contains("debug line"), "structured file should not contain debug lines");

                LogFileWriter.Clear();
                Assert.Equal(0, new FileInfo(logPath).Length);
                Assert.Equal(0, new FileInfo(debugPath).Length);
            }
            finally
            {
                LogFileWriter.LogPath = originalLog;
                LogFileWriter.DebugLogPath = originalDebug;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_FzfFilter_FilterMatchesPrefix()
        {
            // Guards: skip if fzf is not on PATH (so the suite passes without it).
            var fzf = new FzfFilter();
            if (!fzf.IsAvailable())
            {
                Console.WriteLine("      (skipped: fzf not on PATH)");
                return;
            }

            var matched = fzf.FilterAsync(
                new[] { "alpha.cs", "beta.txt", "gamma.cs" },
                "alp",
                new System.Threading.CancellationToken()).GetAwaiter().GetResult();

            Assert.True(matched.Any(m => m.Contains("alpha")), "expected 'alpha' to match 'alp'");
        }

        // ================================================================
        // OverlayKeyHandler — navigation (j/k/gg/G, wrap) + insert/normal mode
        // ================================================================

        public static void Run_KeyHandler_StartsInInsertMode()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            Assert.False(h.IsNormalMode, "overlay starts in insert mode");
            Assert.Equal(0, h.SelectedIndex);
        }

        public static void Run_KeyHandler_EscapeEntersNormalMode()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            var action = h.Handle(OverlayKey.Escape);
            Assert.Equal(OverlayAction.EnterNormal, action);
            Assert.True(h.IsNormalMode, "Escape in insert mode enters normal mode");
        }

        public static void Run_KeyHandler_JKMoveSelection()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(3);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.True(h.IsNormalMode, "Escape enters normal mode");

            Assert.Equal(OverlayAction.MoveDown, h.Handle(OverlayKey.J));
            Assert.Equal(1, h.SelectedIndex);
            Assert.Equal(OverlayAction.MoveDown, h.Handle(OverlayKey.J));
            Assert.Equal(2, h.SelectedIndex);
            Assert.Equal(OverlayAction.MoveUp, h.Handle(OverlayKey.K));
            Assert.Equal(1, h.SelectedIndex);
        }

        public static void Run_KeyHandler_SelectionWrapsAround()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(2);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));

            // From index 0, Down wraps to 1 then back to 0.
            Assert.Equal(OverlayAction.MoveDown, h.Handle(OverlayKey.Down));
            Assert.Equal(1, h.SelectedIndex);
            Assert.Equal(OverlayAction.MoveDown, h.Handle(OverlayKey.Down));
            Assert.Equal(0, h.SelectedIndex);
            // Up from 0 wraps to the last entry.
            Assert.Equal(OverlayAction.MoveUp, h.Handle(OverlayKey.Up));
            Assert.Equal(1, h.SelectedIndex);
        }

        public static void Run_KeyHandler_GgMovesToFirst()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(4);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));

            // Move down a couple, then "gg" returns to the first.
            h.Handle(OverlayKey.J);
            h.Handle(OverlayKey.J);
            Assert.Equal(2, h.SelectedIndex);
            // First 'g' arms the pending gg (no action yet).
            Assert.Equal(OverlayAction.None, h.Handle(OverlayKey.G));
            Assert.Equal(OverlayAction.MoveToFirst, h.Handle(OverlayKey.G));
            Assert.Equal(0, h.SelectedIndex);
        }

        public static void Run_KeyHandler_ShiftGMovesToLast()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(5);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));

            Assert.Equal(OverlayAction.MoveToLast, h.Handle(OverlayKey.ShiftG));
            Assert.Equal(4, h.SelectedIndex);
        }

        public static void Run_KeyHandler_IAEnterInsertWithCaret()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(3);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.True(h.IsNormalMode, "'Escape' enters normal mode");

            Assert.Equal(OverlayAction.EnterInsert, h.Handle(OverlayKey.I));
            Assert.False(h.IsNormalMode, "'i' returns to insert mode");

            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.Equal(OverlayAction.EnterInsertAppend, h.Handle(OverlayKey.A));
            Assert.False(h.IsNormalMode, "'a' returns to insert mode (append caret)");
        }

        public static void Run_KeyHandler_EnterSelectsInInsertAndNormal()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(2);
            // Insert mode: Enter selects.
            Assert.Equal(OverlayAction.SelectCurrent, h.Handle(OverlayKey.Enter));
            // Normal mode: Enter also selects.
            h.Handle(OverlayKey.Escape);
            Assert.Equal(OverlayAction.SelectCurrent, h.Handle(OverlayKey.Enter));
        }

        public static void Run_KeyHandler_QAndEscapeCloseInNormal()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(2);
            h.Handle(OverlayKey.Escape);
            Assert.Equal(OverlayAction.Close, h.Handle(OverlayKey.Q));
            Assert.Equal(OverlayAction.Close, h.Handle(OverlayKey.Escape));
        }

        public static void Run_KeyHandler_NoSelectionWithoutResults()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(0);
            h.Handle(OverlayKey.Escape);
            // Movement on an empty list is a no-op that keeps index at 0.
            h.Handle(OverlayKey.J);
            Assert.Equal(0, h.SelectedIndex);
            h.Handle(OverlayKey.ShiftG);
            Assert.Equal(0, h.SelectedIndex);
        }

        // ================================================================
        // TextMotionNavigator (shared) — vim motions over preview text
        // ================================================================

        public static void Run_Preview_HlMoveByChar()
        {
            var n = new TextMotionNavigator();
            n.SetText("hello world");
            n.MoveTo(0);
            n.Right();
            Assert.Equal(1, n.Caret);
            n.Left();
            Assert.Equal(0, n.Caret);
        }

        public static void Run_Preview_JkMoveByLine()
        {
            var n = new TextMotionNavigator();
            n.SetText("alpha\nbeta\ngamma");
            n.MoveTo(0);
            n.Down();
            Assert.Equal(2, n.LineNumber);
            Assert.Equal(0, n.ColumnNumber - 1); // column 0 on "beta"
            n.Down();
            Assert.Equal(3, n.LineNumber);
            n.Up();
            Assert.Equal(2, n.LineNumber);
            Assert.Equal(0, n.ColumnNumber - 1);
        }

        public static void Run_Preview_WordMotions()
        {
            var n = new TextMotionNavigator();
            n.SetText("one two three");
            n.MoveTo(0);
            n.NextWord();
            Assert.Equal(4, n.Caret);
            n.NextWord();
            Assert.Equal(8, n.Caret);
            n.PrevWord();
            Assert.Equal(4, n.Caret);
            n.PrevWord();
            Assert.Equal(0, n.Caret);
        }

        public static void Run_Preview_GgAndG()
        {
            var n = new TextMotionNavigator();
            n.SetText("a\nb\nc\nd");
            n.MoveTo(6); // in "d" line
            n.Top();
            Assert.Equal(0, n.Caret);
            Assert.Equal(1, n.LineNumber);
            n.Bottom();
            Assert.Equal(7, n.Caret);
            Assert.Equal(4, n.LineNumber);
        }

        public static void Run_Preview_LineStartEnd()
        {
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(4); // start of "def"
            n.LineEnd();
            Assert.Equal(7, n.Caret);
            n.LineStartHome();
            Assert.Equal(4, n.Caret);
        }

        public static void Run_Preview_CaretClamped()
        {
            var n = new TextMotionNavigator();
            n.SetText("abc");
            n.MoveTo(99);
            Assert.Equal(3, n.Caret);
            n.MoveTo(-5);
            Assert.Equal(0, n.Caret);
        }

        public static void Run_Preview_MoveToLine()
        {
            var n = new TextMotionNavigator();
            n.SetText("one\ntwo\nthree");
            n.MoveToLine(1);
            Assert.Equal(0, n.Caret);
            n.MoveToLine(2);
            Assert.Equal(4, n.Caret);
            n.MoveToLine(3);
            Assert.Equal(8, n.Caret);
            // Beyond the last line clamps to the last line start.
            n.MoveToLine(99);
            Assert.Equal(8, n.Caret);
            // Single line: line 1 = 0.
            var single = new TextMotionNavigator();
            single.SetText("only");
            single.MoveToLine(5);
            Assert.Equal(0, single.Caret);
        }

        public static void Run_Preview_InsertMotions()
        {
            var n = new TextMotionNavigator();
            n.SetText("hello world");
            n.MoveTo(2);
            n.InsertAfter();
            Assert.Equal(3, n.Caret);
            n.InsertEnd();
            Assert.Equal(11, n.Caret);
            n.InsertStart();
            Assert.Equal(0, n.Caret);
        }

        // ================================================================
        // Prompt motions — normal-mode h/l/w/b/e/0/$ over the search box text
        // (mirrors the text-input tool-window motions)
        // ================================================================

        public static void Run_PromptMotion_HlMoveByChar()
        {
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(3);
            n.Right();
            Assert.Equal(4, n.Caret);
            n.Left();
            Assert.Equal(3, n.Caret);
            n.Left();
            Assert.Equal(2, n.Caret);
        }

        public static void Run_PromptMotion_WordMotions()
        {
            var n = new TextMotionNavigator();
            n.SetText("find my file");
            n.MoveTo(0);
            n.NextWord();
            Assert.Equal(5, n.Caret);
            n.NextWord();
            Assert.Equal(8, n.Caret);
            n.PrevWord();
            Assert.Equal(5, n.Caret);
            n.PrevWord();
            Assert.Equal(0, n.Caret);
        }

        public static void Run_PromptMotion_LineStartEnd()
        {
            var n = new TextMotionNavigator();
            n.SetText("search text");
            n.MoveTo(4);
            n.LineStartHome();
            Assert.Equal(0, n.Caret);
            n.LineEnd();
            Assert.Equal(11, n.Caret);
        }

        public static void Run_PromptMotion_EndWord()
        {
            var n = new TextMotionNavigator();
            n.SetText("foo bar");
            n.MoveTo(0);
            n.EndWord();
            Assert.Equal(3, n.Caret);
        }

        public static void Run_FileFinder_EnumeratesCandidates()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_files_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "Alpha.cs");
                string b = Path.Combine(dir, "Beta.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var finder = new FileFinder(() => new[] { a, b }, _ => { });
                var entries = finder.GetCandidates();

                Assert.Equal(2, entries.Count);
                Assert.Equal("Alpha.cs", entries[0].Display);
                Assert.Equal(a, entries[0].Payload as string);
                Assert.Equal("Beta.cs", entries[1].Display);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_FileFinder_OpenSelectedCallsOpener()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_files_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "Alpha.cs");
                File.WriteAllText(a, "// a");
                string? opened = null;
                var finder = new FileFinder(() => new[] { a }, p => opened = p);

                finder.OnSelected(new FinderEntry("Alpha.cs", a));
                Assert.Equal(a, opened);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_FileFinder_OpenMissingFileIsNoOp()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_files_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string missing = Path.Combine(dir, "Ghost.cs");
                int opened = 0;
                var finder = new FileFinder(() => new[] { missing }, _ => opened++);

                finder.OnSelected(new FinderEntry("Ghost.cs", missing));
                Assert.Equal(0, opened);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        // ================================================================
        // SyntaxHighlighter — preview syntax coloring (keywords/strings/comments/numbers)
        // ================================================================

        public static void Run_Syntax_KeywordsAndIdentifiers()
        {
            var segs = SyntaxHighlighter.Segment("public class Foo { }");
            var pairs = segs.Select(s => (s.Text, s.Category)).ToList();
            Assert.True(pairs.Any(p => p.Text == "public" && p.Category == SyntaxCategory.Keyword), "public is a keyword");
            Assert.True(pairs.Any(p => p.Text == "class" && p.Category == SyntaxCategory.Keyword), "class is a keyword");
            Assert.True(pairs.Any(p => p.Text == "Foo" && p.Category == SyntaxCategory.Default), "Foo is an identifier, not a keyword");
            Assert.True(pairs.Any(p => p.Category == SyntaxCategory.Default && p.Text.Contains("{") && p.Text.Contains("}")), "braces are default text");
        }

        public static void Run_Syntax_LineComment()
        {
            var segs = SyntaxHighlighter.Segment("int x = 1; // hello");
            var comment = segs.FirstOrDefault(s => s.Category == SyntaxCategory.Comment);
            Assert.True(comment.Text == "// hello", $"line comment captured, got '{comment.Text}'");
        }

        public static void Run_Syntax_BlockCommentSpansLines()
        {
            var segs = SyntaxHighlighter.Segment("a /* one\ntwo */ b");
            var comment = segs.FirstOrDefault(s => s.Category == SyntaxCategory.Comment);
            Assert.True(comment.Text.Contains('\n'), "block comment spans lines");
            Assert.True(comment.Text.StartsWith("/*") && comment.Text.EndsWith("*/"), "block comment includes delimiters");
        }

        public static void Run_Syntax_Strings()
        {
            var segs = SyntaxHighlighter.Segment("var s = \"hi \\\"there\\\"\";");
            var str = segs.FirstOrDefault(s => s.Category == SyntaxCategory.String);
            Assert.True(str.Text == "\"hi \\\"there\\\"\"", $"string captured with escapes, got '{str.Text}'");
        }

        public static void Run_Syntax_VerbatimStringSpansLines()
        {
            var segs = SyntaxHighlighter.Segment("var s = @\"line1\nline2\"\"quote\";");
            var str = segs.FirstOrDefault(s => s.Category == SyntaxCategory.String);
            Assert.True(str.Text.StartsWith("@\""), "verbatim string captured");
            Assert.True(str.Text.Contains("line1"), "verbatim string spans lines");
        }

        public static void Run_Syntax_Numbers()
        {
            var segs = SyntaxHighlighter.Segment("var x = 42; var y = 0xFF; var z = 1.5e3; var f = 100L;");
            var nums = segs.Where(s => s.Category == SyntaxCategory.Number).Select(s => s.Text).ToList();
            Assert.True(nums.Contains("42"), "decimal literal");
            Assert.True(nums.Contains("0xFF"), "hex literal");
            Assert.True(nums.Contains("1.5e3"), "exponent literal");
            Assert.True(nums.Contains("100L"), "suffixed literal");
        }

        public static void Run_Syntax_RoundTripsText()
        {
            const string code = "using System;\n\npublic class Probe\n{\n    // note\n    static int X = 42;\n    string s = \"hello\";\n}";
            var segs = SyntaxHighlighter.Segment(code);
            var rebuilt = string.Concat(segs.Select(s => s.Text));
            Assert.Equal(code, rebuilt);
        }

        // ================================================================
        // CodeIssuesFinder — warnings/errors/TODO markers
        // ================================================================

        public static void Run_Issues_TodoScanFindsMarkers()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_issues_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "class A\n{\n    // TODO: fix this\n    // FIXME: and this\n    int x;\n}");
                string b = Path.Combine(dir, "B.cs");
                File.WriteAllText(b, "// nothing here\n");

                var finder = new CodeIssuesFinder(() => new[] { a, b }, _ => { });
                var entries = finder.GetCandidates();

                Assert.Equal(2, entries.Count);
                Assert.True(entries[0].Display.StartsWith("[TODO] line 3:"), $"todo on line 3, got '{entries[0].Display}'");
                Assert.True(entries[1].Display.StartsWith("[TODO] line 4:"), $"fixme on line 4, got '{entries[1].Display}'");
                Assert.True(entries[0].Display.Contains("A.cs"), "display names the file");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_Issues_NoFalsePositiveOnTodoWord()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_issues_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "var todoList = new List<int>();\nint total = 1;\n");

                var finder = new CodeIssuesFinder(() => new[] { a }, _ => { });
                Assert.Equal(0, finder.GetCandidates().Count);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_Issues_OnSelectedReportsPathAndLine()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_issues_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "line one\n// TODO: here\n");

                CodeIssue? opened = null;
                var finder = new CodeIssuesFinder(() => new[] { a }, issue => opened = issue);
                var entry = finder.GetCandidates()[0];

                finder.OnSelected(entry);
                Assert.True(opened != null, "opener invoked");
                Assert.Equal(a, opened!.FilePath);
                Assert.Equal(2, opened!.LineNumber);
                Assert.Equal(CodeIssueKind.Todo, opened!.Kind);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        // ================================================================
        // ReferencesFinder — read/write references to the caret symbol
        // (hermetic seams: injected gatherer Func<IReadOnlyList<ReferenceHit>> + opener
        // Action<ReferenceHit>, mirroring CodeIssuesFinder)
        // ================================================================

        public static void Run_ReferencesFinder_DisplayShowsAccessMarker()
        {
            var read = new ReferenceHit(@"C:\p\Reader.cs", 5, 16, isWrite: false, "Value", "return Shared.Value;");
            var write = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            var finder = new ReferencesFinder(() => new[] { read, write }, _ => { });

            var entries = finder.GetCandidates();
            Assert.Equal(2, entries.Count);

            // The display must show the read/write access deterministically: "(read)" for a read,
            // "(write)" for a write — this is the per-row access contract (A2).
            Assert.True(entries[0].Display.Contains("(read)"), $"read hit shows '(read)', got '{entries[0].Display}'");
            Assert.True(entries[1].Display.Contains("(write)"), $"write hit shows '(write)', got '{entries[1].Display}'");
            // Display must carry the file name and the symbol so a human can disambiguate the row.
            Assert.True(entries[0].Display.Contains("Reader.cs"), $"read hit names its file, got '{entries[0].Display}'");
            Assert.True(entries[0].Display.Contains("Value"), $"read hit names the symbol, got '{entries[0].Display}'");
        }

        public static void Run_ReferencesFinder_PayloadRoundTrips()
        {
            var hit = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            var finder = new ReferencesFinder(() => new[] { hit }, _ => { });

            var entry = finder.GetCandidates()[0];
            // The ReferenceHit payload must round-trip through FinderEntry.Payload so OnSelected can
            // recover the exact file/line/col/access to open.
            Assert.True(ReferenceEquals(hit, entry.Payload), "payload must be the exact ReferenceHit instance");
            var payload = entry.Payload as ReferenceHit;
            Assert.True(payload != null, "payload is a ReferenceHit");
            Assert.Equal(@"C:\p\Writer.cs", payload!.FilePath);
            Assert.Equal(5, payload.LineNumber);
            Assert.True(payload.IsWrite, "write hit carries IsWrite=true");
        }

        public static void Run_ReferencesFinder_OnSelectedOpensHitWithAccess()
        {
            var write = new ReferenceHit(@"C:\p\Writer.cs", 5, 5, isWrite: true, "Value", "Shared.Value = 1;");
            ReferenceHit? opened = null;
            var finder = new ReferencesFinder(() => new[] { write }, hit => opened = hit);
            var entry = finder.GetCandidates()[0];

            finder.OnSelected(entry);
            Assert.True(opened != null, "opener invoked");
            Assert.Equal(@"C:\p\Writer.cs", opened!.FilePath);
            Assert.Equal(5, opened.LineNumber);
            Assert.Equal(5, opened.Column);
            Assert.True(opened.IsWrite, "opened hit preserves its write access");
        }

        public static void Run_ReferencesFinder_LineNumberDrivesPreviewJump()
        {
            // A3 line mapping: the hit's 1-based LineNumber is what positions the preview caret.
            // Feed a hit whose line is 3 (mid-file) into the shared navigator and prove it lands
            // on line 3 — the pure mapping the overlay's ReferenceHit preview branch relies on.
            string text = "one\ntwo\nthree\nfour";
            var hit = new ReferenceHit(@"C:\p\File.cs", 3, 1, isWrite: false, "Value", "three");
            var nav = new TextMotionNavigator();
            nav.SetText(text);
            nav.MoveToLine(hit.LineNumber);
            Assert.Equal(3, nav.LineNumber);
            Assert.Equal(8, nav.Caret); // start of "three"
        }

        // ================================================================
        // ImplementationFinder — implementations/overrides of the caret symbol
        // (hermetic seams: injected gatherer Func<IReadOnlyList<ImplementationHit>> + opener
        // Action<ImplementationHit>, mirroring ReferencesFinder)
        // ================================================================

        public static void Run_ImplementationFinder_DisplayFormatting()
        {
            var hit = new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            var finder = new ImplementationFinder(() => new[] { hit }, _ => { });

            var entries = finder.GetCandidates();
            Assert.Equal(1, entries.Count);
            // Deterministic display: {Kind} {SymbolName} — {file}:{line} (A2 contract).
            Assert.Equal("Class Shape — Shape.cs:2", entries[0].Display);
        }

        public static void Run_ImplementationFinder_PayloadRoundTrips()
        {
            var hit = new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            var finder = new ImplementationFinder(() => new[] { hit }, _ => { });

            var entry = finder.GetCandidates()[0];
            // The ImplementationHit payload must round-trip through FinderEntry.Payload so
            // OnSelected can recover the exact file/line/kind to open.
            Assert.True(ReferenceEquals(hit, entry.Payload), "payload must be the exact ImplementationHit instance");
            var payload = entry.Payload as ImplementationHit;
            Assert.True(payload != null, "payload is an ImplementationHit");
            Assert.Equal(@"C:\p\Shape.cs", payload!.FilePath);
            Assert.Equal(2, payload.LineNumber);
            Assert.Equal("Shape", payload.SymbolName);
            Assert.Equal("Class", payload.Kind);
        }

        public static void Run_ImplementationFinder_OnSelectedOpensHitAtLine()
        {
            var hit = new ImplementationHit(@"C:\p\Shape.cs", 2, "Shape", "Class");
            ImplementationHit? opened = null;
            var finder = new ImplementationFinder(() => new[] { hit }, h => opened = h);
            var entry = finder.GetCandidates()[0];

            finder.OnSelected(entry);
            Assert.True(opened != null, "opener invoked");
            Assert.Equal(@"C:\p\Shape.cs", opened!.FilePath);
            Assert.Equal(2, opened.LineNumber);
            Assert.Equal("Shape", opened.SymbolName);
            Assert.Equal("Class", opened.Kind);
        }

        public static void Run_ImplementationFinder_LineNumberDrivesPreviewJump()
        {
            // A4 line mapping: the hit's 1-based LineNumber is what positions the preview caret.
            // Feed a hit whose line is 3 (mid-file) into the shared navigator and prove it lands
            // on line 3 — the pure mapping the overlay's ImplementationHit preview branch relies on.
            string text = "one\ntwo\nthree\nfour";
            var hit = new ImplementationHit(@"C:\p\File.cs", 3, "File", "Class");
            var nav = new TextMotionNavigator();
            nav.SetText(text);
            nav.MoveToLine(hit.LineNumber);
            Assert.Equal(3, nav.LineNumber);
            Assert.Equal(8, nav.Caret); // start of "three"
        }

        // ================================================================
        // GrepFinder — query-driven live grep over the solution's files
        // (hermetic seams mirroring CodeIssuesFinder: injected file-PATH source +
        // Action<GrepHit> opener; the finder reads file CONTENT off disk from those paths)
        // ================================================================

        public static void Run_GrepFinder_EmptyQueryReturnsZeroCandidates()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_grep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                Assert.Equal(0, finder.GetCandidates("").Count);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_GrepFinder_LineScanMatchesCaseInsensitive()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_grep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");
                string b = Path.Combine(dir, "B.cs");
                File.WriteAllText(b, "// nothing here\n");

                var finder = new GrepFinder(() => new[] { a, b }, _ => { });
                // Case-insensitive substring: "needle" matches BOTH "NEEDLE here" (line 2) and
                // "needle again" (line 4); the non-matching "middle" line and B.cs are excluded.
                var entries = finder.GetCandidates("needle");

                Assert.Equal(2, entries.Count);
                Assert.True(entries.All(e => e.Display.Contains("A.cs")), "only A.cs contains matches");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_GrepFinder_DisplayIsFileNameLineText()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_grep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "first\nNEEDLE here\n");

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var entry = finder.GetCandidates("NEEDLE")[0];
                // Deterministic {fileName}:{line}: {lineText} display (1-based line, fileName only).
                Assert.Equal("A.cs:2: NEEDLE here", entry.Display);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_GrepFinder_PayloadRoundTripsGrepHit()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_grep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "x\n// NEEDLE x\n");

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var entry = finder.GetCandidates("NEEDLE")[0];
                // The GrepHit payload must round-trip through FinderEntry.Payload so OnSelected can
                // recover the exact file/line/text to open.
                var payload = entry.Payload as GrepHit;
                Assert.True(payload != null, "payload is a GrepHit");
                Assert.Equal(a, payload!.FilePath);
                Assert.Equal(2, payload.LineNumber);
                Assert.Equal("// NEEDLE x", payload.LineText);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_GrepFinder_OnSelectedOpensHitAtLine()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_grep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "first\n// NEEDLE x\n");

                GrepHit? opened = null;
                var finder = new GrepFinder(() => new[] { a }, hit => opened = hit);
                var entry = finder.GetCandidates("NEEDLE")[0];

                finder.OnSelected(entry);
                Assert.True(opened != null, "opener invoked");
                Assert.Equal(a, opened!.FilePath);
                Assert.Equal(2, opened.LineNumber);
                Assert.Equal("// NEEDLE x", opened.LineText);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_GrepFinder_HitCapBounded()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_grep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 500; i++) { sb.AppendLine("NEEDLE " + i); }
                File.WriteAllText(a, sb.ToString());

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var entries = finder.GetCandidates("NEEDLE");
                Assert.True(entries.Count > 0, "hits are still returned up to the cap");
                Assert.True(entries.Count <= 200, $"hit cap bounds the result set (got {entries.Count})");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        // ================================================================
        // DiagnosticLog — pins the log-prefix constants the harness relies on
        // (F45: Telescope/DiagnosticLog.cs does not exist yet -> this is RED)
        // ================================================================

        public static void Run_LogPrefixes_Pinned()
        {
            Assert.Equal("[NeoVisual] ", DiagnosticLog.NeoVisual);
            Assert.Equal("[Telescope] ", DiagnosticLog.Telescope);
            Assert.Equal("[Hook] ", DiagnosticLog.Hook);
            Assert.Equal("[MyExtension] ", DiagnosticLog.MyExtension);
            Assert.Equal("[GlobalKeyboard] ", DiagnosticLog.GlobalKeyboard);
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
