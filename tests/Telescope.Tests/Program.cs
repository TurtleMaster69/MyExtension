using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TestHarness;

namespace Telescope.Tests
{
    internal static class Program
    {
        private static int Main(string[] args) => TestHarness.TestRunner.Run(typeof(Tests), args);
    }

    internal static class Tests
    {
        private static FinderEntry E(string s) => new FinderEntry(s);

        // True when the line is a LogFileWriter timestamped line: "HH:mm:ss.fff <message>"
        // (12 timestamp chars, then a space) — the per-call File.AppendAllText format the
        // buffered writer must reproduce byte-identically.
        private static bool IsTimestampedLine(string line)
        {
            if (line == null || line.Length < 13)
            {
                return false;
            }
            for (int i = 0; i < 12; i++)
            {
                char c = line[i];
                if (i == 2 || i == 5)
                {
                    if (c != ':') { return false; }
                }
                else if (i == 8)
                {
                    if (c != '.') { return false; }
                }
                else if (!char.IsDigit(c))
                {
                    return false;
                }
            }
            return line[12] == ' ';
        }

        // Reads a file with FileShare.ReadWrite so it can be read while the buffered
        // LogFileWriter still holds it open (File.ReadAllText uses FileShare.Read, which
        // conflicts with the writer's existing FileAccess.Write -> sharing violation).
        private static string ReadAllTextShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                return sr.ReadToEnd();
            }
        }

        // ================================================================
        // Lane 3 test-local seam types (BP-1/BP-2).
        // RED: FileLocation / FinderBase<THit> do not exist yet -> compile error.
        // ================================================================

        private sealed class TestHit : FileLocation
        {
            public TestHit(string filePath, int lineNumber) : base(filePath, lineNumber) { }
        }

        private sealed class TestFinder : FinderBase<TestHit>
        {
            private readonly Func<IReadOnlyList<TestHit>> _gather;
            private readonly Action<TestHit>? _open;

            public TestFinder(Func<IReadOnlyList<TestHit>> gather, Action<TestHit>? open = null)
            {
                _gather = gather;
                _open = open;
            }

            public override string Name => "Test";
            protected override IReadOnlyList<TestHit> GatherHits() => _gather();
            protected override FinderEntry ToEntry(TestHit hit) => new FinderEntry(hit.FilePath, hit);
            protected override void OpenHit(TestHit hit) => _open?.Invoke(hit);
        }

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
                // A4 (X1/BP-1): the buffered write is not on disk until Flush().
                LogFileWriter.Flush();
                Assert.True(ReadAllTextShared(logPath).Contains("structured line"), "structured log should contain the NeoVisual line");
                Assert.True(ReadAllTextShared(debugPath).Contains("debug line"), "debug log should contain the debug line");

                // The two files are separate: the structured line is NOT in the debug file and
                // the debug line is NOT in the structured file.
                Assert.False(ReadAllTextShared(debugPath).Contains("structured line"), "debug file should not contain structured lines");
                Assert.False(ReadAllTextShared(logPath).Contains("debug line"), "structured file should not contain debug lines");

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

        // ================================================================
        // LogFileWriter buffered writes (BP-1/X1)
        // RED: `LogFileWriter.Flush()` / `LogFileWriter.Close()` do not exist
        //      yet -> compile error; `NotFlushedYet` fails because the current
        //      code writes immediately via File.AppendAllText.
        // NOTE: these tests must NOT call Clear() — the once-per-process
        //       `_clearedThisProcess` flag is consumed by
        //       Run_LogFileWriter_WritesAndClearsFile.
        // ================================================================

        public static void Run_LogFileWriter_Buffered_NotFlushedYet()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_buffered_" + Guid.NewGuid().ToString("N"));
            string logPath = Path.Combine(dir, "neovisual-exp.log");
            string originalLog = LogFileWriter.LogPath;
            try
            {
                LogFileWriter.LogPath = logPath;
                LogFileWriter.Write("x");

                // The buffered write must NOT be on disk until Flush() — the file either does not
                // exist yet or is empty. (Current code writes immediately, so this fails RED.)
                string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                Assert.False(content.Contains("x"), "a buffered write must not hit disk until Flush()");
            }
            finally
            {
                LogFileWriter.LogPath = originalLog;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_LogFileWriter_Buffered_FlushWritesToDisk()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_buffered_" + Guid.NewGuid().ToString("N"));
            string logPath = Path.Combine(dir, "neovisual-exp.log");
            string originalLog = LogFileWriter.LogPath;
            try
            {
                LogFileWriter.LogPath = logPath;
                LogFileWriter.Write("x");
                LogFileWriter.Flush();

                Assert.True(ReadAllTextShared(logPath).Contains("x"), "Flush() writes the buffered line to disk");
            }
            finally
            {
                LogFileWriter.LogPath = originalLog;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_LogFileWriter_Buffered_ContentIdenticalToAppend()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_buffered_" + Guid.NewGuid().ToString("N"));
            string logPath = Path.Combine(dir, "neovisual-exp.log");
            string originalLog = LogFileWriter.LogPath;
            try
            {
                LogFileWriter.LogPath = logPath;
                LogFileWriter.Write("first");
                LogFileWriter.Write("second");
                LogFileWriter.Write("third");
                LogFileWriter.Flush();

                // The buffered output must be byte-identical to the per-call File.AppendAllText
                // format: each line is "HH:mm:ss.fff <message>" + Environment.NewLine, in order.
                string[] lines = ReadAllTextShared(logPath)
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                // File.ReadAllLines drops the trailing empty element left by the final newline.
                if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                {
                    Array.Resize(ref lines, lines.Length - 1);
                }
                Assert.Equal(3, lines.Length);
                Assert.True(IsTimestampedLine(lines[0]) && lines[0].EndsWith(" first"), $"line 1 is a timestamped 'first', got '{lines[0]}'");
                Assert.True(IsTimestampedLine(lines[1]) && lines[1].EndsWith(" second"), $"line 2 is a timestamped 'second', got '{lines[1]}'");
                Assert.True(IsTimestampedLine(lines[2]) && lines[2].EndsWith(" third"), $"line 3 is a timestamped 'third', got '{lines[2]}'");
            }
            finally
            {
                LogFileWriter.LogPath = originalLog;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_LogFileWriter_Buffered_FlushOnClose()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_buffered_" + Guid.NewGuid().ToString("N"));
            string logPath = Path.Combine(dir, "neovisual-exp.log");
            string originalLog = LogFileWriter.LogPath;
            try
            {
                LogFileWriter.LogPath = logPath;
                LogFileWriter.Write("x");
                LogFileWriter.Close();

                Assert.True(File.ReadAllText(logPath).Contains("x"), "Close() flushes the buffered line to disk");
            }
            finally
            {
                LogFileWriter.LogPath = originalLog;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_LogFileWriter_Buffered_PathChangeReopens()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_buffered_" + Guid.NewGuid().ToString("N"));
            string pathA = Path.Combine(dir, "a.log");
            string pathB = Path.Combine(dir, "b.log");
            string originalLog = LogFileWriter.LogPath;
            try
            {
                LogFileWriter.LogPath = pathA;
                LogFileWriter.Write("line1");
                LogFileWriter.Flush();

                // Repointing LogPath must close the writer for A and reopen it for B, so the two
                // files never share lines.
                LogFileWriter.LogPath = pathB;
                LogFileWriter.Write("line2");
                LogFileWriter.Flush();

                string a = ReadAllTextShared(pathA);
                string b = ReadAllTextShared(pathB);
                Assert.True(a.Contains("line1"), "path A holds the first line");
                Assert.False(a.Contains("line2"), "path A must not receive the second line (writer reopened)");
                Assert.True(b.Contains("line2"), "path B holds the second line");
                Assert.False(b.Contains("line1"), "path B must not receive the first line (writer reopened)");
            }
            finally
            {
                LogFileWriter.LogPath = originalLog;
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
        // CaretPlacement — shared insert-caret enum (BP-7/L7)
        // RED: `CaretPlacement` does not exist yet -> compile error
        // ================================================================

        public static void Run_CaretPlacement_EnterInsertMapsToActions()
        {
            var h = new OverlayKeyHandler();
            h.Reset();
            h.SetResults(3);
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.True(h.IsNormalMode, "'Escape' enters normal mode");

            // I -> EnterInsert (caret at current position).
            Assert.Equal(OverlayAction.EnterInsert, h.Handle(OverlayKey.I));
            Assert.False(h.IsNormalMode, "'i' returns to insert mode");

            // A -> EnterInsertAppend (caret at end).
            Assert.Equal(OverlayAction.EnterNormal, h.Handle(OverlayKey.Escape));
            Assert.Equal(OverlayAction.EnterInsertAppend, h.Handle(OverlayKey.A));

            // EnterInsertMode(CaretPlacement) mapping (requires EnterInsertMode to be internal).
            Assert.Equal(OverlayAction.EnterInsertAppend, h.EnterInsertMode(CaretPlacement.End));
            Assert.Equal(OverlayAction.EnterInsertStart, h.EnterInsertMode(CaretPlacement.Start));
            Assert.Equal(OverlayAction.EnterInsert, h.EnterInsertMode(CaretPlacement.Current));
        }

        public static void Run_CaretPlacement_EnumValues()
        {
            var names = Enum.GetNames(typeof(CaretPlacement));
            Assert.Equal(3, names.Length);
            Assert.True(names.Contains("Current"), "Current member exists");
            Assert.True(names.Contains("End"), "End member exists");
            Assert.True(names.Contains("Start"), "Start member exists");
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

        // ================================================================
        // FileLocation / IFileLocation / FileHit (BP-1/L3)
        // RED: FileLocation / IFileLocation / FileHit do not exist yet -> compile error
        // ================================================================

        public static void Run_FileLocation_NullCoalescing()
        {
            // The shared FileLocation ctor null-coalesces FilePath to "" (the behavior the 4 hit
            // models currently hand-roll) and stores LineNumber verbatim.
            var hit = new TestHit(null!, 5);
            Assert.Equal("", hit.FilePath);
            Assert.Equal(5, hit.LineNumber);
        }

        public static void Run_FileLocation_SubclassExtraFields()
        {
            // Each hit model keeps its extra fields and inherits FilePath/LineNumber from
            // FileLocation (the base-class relationship is the contract being pinned).
            var issue = new CodeIssue(CodeIssueKind.Todo, @"C:\p\A.cs", 3, "fix me");
            Assert.True(issue is FileLocation, "CodeIssue inherits FileLocation");
            Assert.Equal(@"C:\p\A.cs", issue.FilePath);
            Assert.Equal(3, issue.LineNumber);
            Assert.Equal(CodeIssueKind.Todo, issue.Kind);
            Assert.Equal("fix me", issue.Text);

            var grep = new GrepHit(@"C:\p\B.cs", 7, "// needle");
            Assert.True(grep is FileLocation, "GrepHit inherits FileLocation");
            Assert.Equal(@"C:\p\B.cs", grep.FilePath);
            Assert.Equal(7, grep.LineNumber);
            Assert.Equal("// needle", grep.LineText);

            var reference = new ReferenceHit(@"C:\p\C.cs", 9, 4, true, "Value", "Value = 1;");
            Assert.True(reference is FileLocation, "ReferenceHit inherits FileLocation");
            Assert.Equal(@"C:\p\C.cs", reference.FilePath);
            Assert.Equal(9, reference.LineNumber);
            Assert.Equal(4, reference.Column);
            Assert.True(reference.IsWrite, "ReferenceHit keeps IsWrite");
            Assert.Equal("Value", reference.Symbol);
            Assert.Equal("Value = 1;", reference.LineText);

            var impl = new ImplementationHit(@"C:\p\D.cs", 2, "Shape", "Class");
            Assert.True(impl is FileLocation, "ImplementationHit inherits FileLocation");
            Assert.Equal(@"C:\p\D.cs", impl.FilePath);
            Assert.Equal(2, impl.LineNumber);
            Assert.Equal("Shape", impl.SymbolName);
            Assert.Equal("Class", impl.Kind);
        }

        public static void Run_IFileLocation_Contract()
        {
            // IFileLocation exposes FilePath + LineNumber; all 5 hit models implement it.
            IFileLocation issue = new CodeIssue(CodeIssueKind.Todo, @"C:\p\A.cs", 3, "fix me");
            Assert.Equal(@"C:\p\A.cs", issue.FilePath);
            Assert.Equal(3, issue.LineNumber);

            IFileLocation grep = new GrepHit(@"C:\p\B.cs", 7, "// needle");
            Assert.Equal(@"C:\p\B.cs", grep.FilePath);
            Assert.Equal(7, grep.LineNumber);

            IFileLocation reference = new ReferenceHit(@"C:\p\C.cs", 9, 4, true, "Value", "Value = 1;");
            Assert.Equal(@"C:\p\C.cs", reference.FilePath);
            Assert.Equal(9, reference.LineNumber);

            IFileLocation impl = new ImplementationHit(@"C:\p\D.cs", 2, "Shape", "Class");
            Assert.Equal(@"C:\p\D.cs", impl.FilePath);
            Assert.Equal(2, impl.LineNumber);

            IFileLocation file = new FileHit(@"C:\p\E.cs", 0);
            Assert.Equal(@"C:\p\E.cs", file.FilePath);
            Assert.Equal(0, file.LineNumber);
        }

        public static void Run_IFileLocation_FileHit()
        {
            // FileHit is the new file-finder hit model (LineNumber = 0 for plain files).
            var hit = new FileHit(@"C:\p\A.cs", 0);
            Assert.Equal(@"C:\p\A.cs", hit.FilePath);
            Assert.Equal(0, hit.LineNumber);
        }

        // ================================================================
        // FinderBase<THit> (BP-2/L2) — the shared finder skeleton
        // RED: FinderBase<THit> does not exist yet -> compile error
        // ================================================================

        public static void Run_FinderBase_GetCandidatesMapsGather()
        {
            var hits = new List<TestHit> { new TestHit(@"C:\p\A.cs", 1), new TestHit(@"C:\p\B.cs", 2) };
            var finder = new TestFinder(() => hits);

            var entries = finder.GetCandidates();

            Assert.Equal(2, entries.Count);
            Assert.True(ReferenceEquals(hits[0], entries[0].Payload), "first entry carries the exact first hit");
            Assert.True(ReferenceEquals(hits[1], entries[1].Payload), "second entry carries the exact second hit");
        }

        public static void Run_FinderBase_OnSelectedOpensHit()
        {
            var hit = new TestHit(@"C:\p\A.cs", 1);
            TestHit? opened = null;
            var finder = new TestFinder(() => new[] { hit }, h => opened = h);

            finder.OnSelected(new FinderEntry("A.cs", hit));

            Assert.True(ReferenceEquals(hit, opened), "OnSelected invokes OpenHit with the exact payload");
        }

        public static void Run_FinderBase_GatherErrorSwallowed()
        {
            var finder = new TestFinder(() => throw new InvalidOperationException("gather boom"));

            var entries = finder.GetCandidates();

            Assert.Equal(0, entries.Count);
        }

        public static void Run_FinderBase_OpenErrorSwallowed()
        {
            var hit = new TestHit(@"C:\p\A.cs", 1);
            var finder = new TestFinder(() => new[] { hit }, _ => throw new InvalidOperationException("open boom"));

            // A throwing OpenHit must not propagate out of OnSelected (the runner fails the test
            // if it does).
            finder.OnSelected(new FinderEntry("A.cs", hit));
        }

        public static void Run_FinderBase_NonMatchingPayloadIgnored()
        {
            var hit = new TestHit(@"C:\p\A.cs", 1);
            int opened = 0;
            var finder = new TestFinder(() => new[] { hit }, _ => opened++);

            finder.OnSelected(new FinderEntry("A.cs", "not a TestHit"));

            Assert.Equal(0, opened);
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
                Assert.Equal(a, (entries[0].Payload as FileHit)?.FilePath);
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

                finder.OnSelected(new FinderEntry("Alpha.cs", new FileHit(a, 0)));
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

                finder.OnSelected(new FinderEntry("Ghost.cs", new FileHit(missing, 0)));
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
        // IFinder query fold — GetCandidates(string) + IsQueryDriven (BP-8/L6)
        // RED: `GetCandidates(string)` / `IsQueryDriven` do not exist on IFinder
        //      -> compile error (the fold has not happened)
        // ================================================================

        public static void Run_GetCandidates_DefaultQuery_MatchesNoArg()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_fold_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "Alpha.cs");
                string b = Path.Combine(dir, "Beta.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var finder = new FileFinder(() => new[] { a, b }, _ => { });
                var noArg = finder.GetCandidates();
                var emptyArg = finder.GetCandidates("");

                Assert.Equal(noArg.Count, emptyArg.Count);
                Assert.Equal(noArg[0].Display, emptyArg[0].Display);
                Assert.Equal(noArg[1].Display, emptyArg[1].Display);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_GetCandidates_DefaultQuery_GrepEmpty()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_fold_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                // Written against IFinder so the test pins the NEW interface contract: before the
                // fold IFinder.GetCandidates() takes no args, so GetCandidates("") does not compile.
                IFinder finder = new GrepFinder(() => new[] { a }, _ => { });
                var noArg = finder.GetCandidates();
                var emptyArg = finder.GetCandidates("");

                Assert.Equal(0, noArg.Count);
                Assert.Equal(0, emptyArg.Count);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_GetCandidates_IsQueryDriven()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_fold_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "A.cs");
                File.WriteAllText(a, "// x\n");

                IFinder grep = new GrepFinder(() => new[] { a }, _ => { });
                IFinder files = new FileFinder(() => new[] { a }, _ => { });

                Assert.True(grep.IsQueryDriven, "GrepFinder is query-driven");
                Assert.False(files.IsQueryDriven, "FileFinder is not query-driven");
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

        // ================================================================
        // TelescopeLog — one-line prefix helper (BP-6/L5)
        // RED: `TelescopeLog` does not exist yet -> compile error
        // ================================================================

        public static void Run_TelescopeLog_Prefix()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_telescopelog_" + Guid.NewGuid().ToString("N"));
            string logPath = Path.Combine(dir, "neovisual-exp.log");
            string originalLog = LogFileWriter.LogPath;
            try
            {
                LogFileWriter.LogPath = logPath;
                TelescopeLog.Log("hello");
                // F3 (X1/BP-1): under buffering the write is not on disk until Flush().
                LogFileWriter.Flush();
                Assert.True(ReadAllTextShared(logPath).Contains("[Telescope] hello"),
                    "TelescopeLog prefixes the message with [Telescope] ");
            }
            finally
            {
                LogFileWriter.LogPath = originalLog;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        public static void Run_TelescopeLog_EmptyMessage()
        {
            string dir = Path.Combine(Path.GetTempPath(), "neovisual_telescopelog_" + Guid.NewGuid().ToString("N"));
            string logPath = Path.Combine(dir, "neovisual-exp.log");
            string originalLog = LogFileWriter.LogPath;
            try
            {
                LogFileWriter.LogPath = logPath;
                TelescopeLog.Log("");
                // F3 (X1/BP-1): under buffering the write is not on disk until Flush().
                LogFileWriter.Flush();
                Assert.True(ReadAllTextShared(logPath).Contains("[Telescope] "),
                    "an empty message still emits the [Telescope] prefix");
            }
            finally
            {
                LogFileWriter.LogPath = originalLog;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }
}
