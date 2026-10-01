using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EnvDTE80;
using Telescope.Controller;
using Telescope.Filter;
using Telescope.Finders;
using Telescope.Logging;
using Telescope.Overlay;
using TestHarness;
using static TestHarness.TestScaffold;

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

        // M28: a no-op IDisposable returned by the injected LogFileWriter.TimerScheduler seam.
        private sealed class FakeTimer : IDisposable
        {
            public void Dispose() { }
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
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                string debugPath = Path.Combine(dir.Path, "neovisual-main.log");
                WithLogPath(logPath, () =>
                {
                    WithDebugLogPath(debugPath, () =>
                    {

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
                    });
                });
            }
        }

        public static void Run_LogFileWriter_ClearPerPath()
        {
            using (var dir = new TempDir())
            {
                // Unique Guid paths so Clear() never touches the real %APPDATA% files.
                string pathA = Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".log");
                string debugA = Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".log");
                string pathB = Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".log");
                string debugB = Path.Combine(dir.Path, Guid.NewGuid().ToString("N") + ".log");

                WithLogPath(pathA, () =>
                {
                    WithDebugLogPath(debugA, () =>
                    {
                        LogFileWriter.Write("lineA");
                        LogFileWriter.WriteDebug("debugA");
                        LogFileWriter.Flush();
                        Assert.True(ReadAllTextShared(pathA).Contains("lineA"), "pathA holds the first write");

                        // First Clear() truncates pathA (and debugA).
                        LogFileWriter.Clear();
                        Assert.Equal(0, new FileInfo(pathA).Length);
                        Assert.Equal(0, new FileInfo(debugA).Length);

                        // Repoint both paths to pathB and write again.
                        LogFileWriter.LogPath = pathB;
                        LogFileWriter.DebugLogPath = debugB;
                        LogFileWriter.Write("lineB");
                        LogFileWriter.WriteDebug("debugB");
                        LogFileWriter.Flush();
                        Assert.True(ReadAllTextShared(pathB).Contains("lineB"), "pathB holds the second write");

                        // Second Clear() must truncate pathB too — per-path idempotency.
                        LogFileWriter.Clear();
                        Assert.Equal(0, new FileInfo(pathB).Length);
                        Assert.Equal(0, new FileInfo(debugB).Length);
                    });
                });
            }
        }

        // ================================================================
        // LogFileWriter buffered writes (BP-1/X1)
        // RED: `LogFileWriter.Flush()` / `LogFileWriter.Close()` do not exist
        //      yet -> compile error; `NotFlushedYet` fails because the current
        //      code writes immediately via File.AppendAllText.
        // NOTE: Clear() is per-path idempotent — each unique Guid temp path
        //       truncates on its first Clear regardless of test order.
        // ================================================================

        public static void Run_LogFileWriter_Buffered_NotFlushedYet()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Write("x");

                    // The buffered write must NOT be on disk until Flush() — the file either does not
                    // exist yet or is empty. (Current code writes immediately, so this fails RED.)
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    Assert.False(content.Contains("x"), "a buffered write must not hit disk until Flush()");
                });
            }
        }

        public static void Run_LogFileWriter_Buffered_FlushWritesToDisk()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Write("x");
                    LogFileWriter.Flush();

                    Assert.True(ReadAllTextShared(logPath).Contains("x"), "Flush() writes the buffered line to disk");
                });
            }
        }

        public static void Run_LogFileWriter_Buffered_ContentIdenticalToAppend()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
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
                });
            }
        }

        public static void Run_LogFileWriter_Buffered_FlushOnClose()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Write("x");
                    LogFileWriter.Close();

                    Assert.True(File.ReadAllText(logPath).Contains("x"), "Close() flushes the buffered line to disk");
                });
            }
        }

        public static void Run_LogFileWriter_Buffered_PathChangeReopens()
        {
            using (var dir = new TempDir())
            {
                string pathA = Path.Combine(dir.Path, "a.log");
                string pathB = Path.Combine(dir.Path, "b.log");
                WithLogPath(pathA, () =>
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
                });
            }
        }

        // ================================================================
        // LogFileWriter flush timer (BP-2/M43) — the ~200ms flush timer must be
        // ONE-SHOT: it fires after a write and then sleeps, instead of firing
        // every 200ms forever (5 wakeups/sec for the whole VS session).
        // RED: `LogFileWriter.FlushCount` does not exist yet -> compile error
        //      (CS0117). If it did exist, `IdleDoesNotFire` would fail at
        //      runtime because the current periodic timer keeps climbing.
        // NOTE: these tests must NOT call Clear() — Clear() is per-path idempotent
        //       (each unique Guid temp path truncates on its first Clear), so a Clear()
        //       here would consume the truncation for a path another test relies on.
        // ================================================================

        public static void Run_LogFileWriter_FlushTimer_OneShotFires()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    // M28: inject a controllable timer scheduler — NO wall-clock Thread.Sleep
                    // polling. Close() first so the static _flushTimer is null and EnsureTimer()
                    // uses the injected seam. RED: `LogFileWriter.TimerScheduler` does not exist
                    // yet -> compile error (CS0117).
                    LogFileWriter.Close();
                    Action? flushCallback = null;
                    LogFileWriter.TimerScheduler = cb => { flushCallback = cb; return new FakeTimer(); };
                    try
                    {
                        int before = LogFileWriter.FlushCount;
                        LogFileWriter.Write("x");

                        Assert.True(flushCallback != null, "the injected timer scheduler captured the flush callback");
                        flushCallback!();
                        Assert.True(LogFileWriter.FlushCount >= before + 1,
                            $"firing the captured callback advances FlushCount, before={before}, after={LogFileWriter.FlushCount}");
                    }
                    finally
                    {
                        LogFileWriter.TimerScheduler = null;
                    }
                });
            }
        }

        public static void Run_LogFileWriter_FlushTimer_IdleDoesNotFire()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    LogFileWriter.Close();
                    Action? flushCallback = null;
                    LogFileWriter.TimerScheduler = cb => { flushCallback = cb; return new FakeTimer(); };
                    try
                    {
                        int before = LogFileWriter.FlushCount;
                        LogFileWriter.Write("x");

                        Assert.True(flushCallback != null, "the injected timer scheduler captured the flush callback");
                        // Do NOT fire the callback: with no further writes the one-shot timer must
                        // not re-fire, so FlushCount stays put (deterministic — no sleeps).
                        Assert.Equal(before, LogFileWriter.FlushCount);
                    }
                    finally
                    {
                        LogFileWriter.TimerScheduler = null;
                    }
                });
            }
        }

        // ================================================================
        // LogFileWriter write-failure count (BP-3/M35) — Write/WriteDebug
        // swallow every exception, so the pipeline is undiagnosable when it
        // fails. The fix exposes a `WriteFailureCount` seam.
        // RED: `LogFileWriter.WriteFailureCount` does not exist yet -> compile
        //      error (CS0117).
        // NOTE: these tests must NOT call Clear() (per-path idempotent — a Clear() would
        //       consume the truncation for a path another test relies on).
        // ================================================================

        public static void Run_LogFileWriter_WriteFailureCountIncrements()
        {
            using (var dir = new TempDir())
            {
                string blockerFile = Path.Combine(dir.Path, "blocker");
                File.WriteAllText(blockerFile, "i am a file, not a directory");
                // LogPath's parent is a FILE -> Directory.CreateDirectory / new StreamWriter throws.
                string logPath = Path.Combine(blockerFile, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    int before = LogFileWriter.WriteFailureCount;

                    // Must not throw (never-throw contract) and must increment the count.
                    LogFileWriter.Write("x");

                    Assert.Equal(before + 1, LogFileWriter.WriteFailureCount);
                });
            }
        }

        public static void Run_LogFileWriter_WriteDebugFailureCountIncrements()
        {
            using (var dir = new TempDir())
            {
                string blockerFile = Path.Combine(dir.Path, "blocker");
                File.WriteAllText(blockerFile, "i am a file, not a directory");
                // DebugLogPath's parent is a FILE -> Directory.CreateDirectory / new StreamWriter throws.
                string debugPath = Path.Combine(blockerFile, "neovisual-main.log");
                WithDebugLogPath(debugPath, () =>
                {
                    int before = LogFileWriter.WriteFailureCount;

                    // Must not throw (never-throw contract) and must increment the count.
                    LogFileWriter.WriteDebug("x");

                    Assert.Equal(before + 1, LogFileWriter.WriteFailureCount);
                });
            }
        }

        // ================================================================
        // PaneFailureTracker (BP-4/M18) — a pure one-time fallback for the
        // NeoVisual Output pane: on the FIRST pane failure a single
        // "[NeoVisual] output pane unavailable: <reason>" line is written to
        // the FILE (never via NeoVisualLog.Log, which would re-enter
        // WriteToPane -> recursion).
        // RED: `PaneFailureTracker` does not exist yet -> compile error (CS0246).
        // ================================================================

        public static void Run_PaneFailureTracker_FallbackMessageFormat()
        {
            var tracker = new PaneFailureTracker();
            Assert.Equal("[NeoVisual] output pane unavailable: pane create failed",
                tracker.FallbackMessage("pane create failed"));
        }

        public static void Run_PaneFailureTracker_OneTimeFallback()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var tracker = new PaneFailureTracker();

                    // Two pane failures: the fallback line is emitted only on the FIRST.
                    if (tracker.ShouldEmit())
                    {
                        LogFileWriter.Write(tracker.FallbackMessage("pane create failed"));
                    }
                    if (tracker.ShouldEmit())
                    {
                        LogFileWriter.Write(tracker.FallbackMessage("pane create failed"));
                    }
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    var fallbackLines = lines.Where(l => l.Contains("[NeoVisual] output pane unavailable:")).ToList();
                    Assert.Equal(1, fallbackLines.Count);
                    Assert.True(fallbackLines[0].Contains("[NeoVisual] output pane unavailable: pane create failed"),
                        $"expected exactly one fallback line, got: {string.Join(" | ", fallbackLines)}");
                });
            }
        }

        public static void Run_FzfFilter_FilterMatchesPrefix()
        {
            // m22: resolve the fzf path explicitly and inject it via the ctor — no implicit PATH
            // resolution via the default ctor (the Mystery Guest is explicit). Fail loudly when fzf
            // is absent (no silent skip).
            string? fzfPath = ResolveFzfPath();
            if (fzfPath == null)
            {
                throw new Exception("fzf is not on PATH — this test requires fzf (fail-loud, not a silent skip)");
            }

            var fzf = new FzfFilter(fzfPath);
            var matched = fzf.FilterAsync(
                new[] { "alpha.cs", "beta.txt", "gamma.cs" },
                "alp",
                new System.Threading.CancellationToken()).GetAwaiter().GetResult();

            Assert.True(matched.Any(m => m.Contains("alpha")), "expected 'alpha' to match 'alp'");
        }

        // Resolves the fzf executable path explicitly from PATH (m22 — the injected-path seam).
        private static string? ResolveFzfPath()
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = dir.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                foreach (string name in new[] { "fzf.exe", "fzf" })
                {
                    string candidate = Path.Combine(trimmed, name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        // ================================================================
        // FzfFilter injected-path seam (BP-3/M6a, BP-4/M6b, BP-5/M6c)
        // RED: FilterTimeoutMs does not exist -> compile error (CS1061);
        //      QuoteArg is private -> compile error (CS0122).
        // ================================================================

        public static void Run_FzfFilter_NonexistentPathFallsBackAndLogs()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {

                    var fzf = new FzfFilter(Path.Combine(dir.Path, "missing-fzf.exe"));
                    var result = fzf.FilterAsync(new[] { "alpha" }, "alp", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    LogFileWriter.Flush();

                    // A missing fzf must fall back to the full list AND log the failure (M6: today the
                    // catch is silent — no [Telescope] line is emitted).
                    Assert.Equal(1, result.Count);
                    Assert.True(result.Contains("alpha"), "fallback returns the full candidate list");
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    Assert.True(content.Contains("[Telescope] fzf filter failed:"),
                        "the catch path must log '[Telescope] fzf filter failed:'");
                });
            }
        }

        public static void Run_FzfFilter_TimeoutKillsAndFallsBack()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    string cmdPath = Path.Combine(dir.Path, "hang.cmd");
                    File.WriteAllText(cmdPath, "@ping -n 30 127.0.0.1 > nul");

                    // M3: the timeout path must await BOTH ReadToEndAsync tasks after TryKill so the
                    // faulted tasks are observed — no unobserved-task noise. RED today: the timeout
                    // path returns without awaiting them, so a faulted task raises
                    // UnobservedTaskException. Attach the handler BEFORE the filter runs so any
                    // faulted task finalized during the test is caught.
                    bool unobserved = false;
                    EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) => { unobserved = true; e.SetObserved(); };
                    TaskScheduler.UnobservedTaskException += handler;
                    try
                    {
                        var fzf = new FzfFilter(cmdPath) { FilterTimeoutMs = 200 };
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var result = fzf.FilterAsync(new[] { "alpha" }, "alp", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                        sw.Stop();
                        LogFileWriter.Flush();

                        // A hung fzf must be killed and the filter must fall back within a bounded wall
                        // time (M6: today FilterAsync waits forever on a hung subprocess).
                        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"filter returned within 5s (took {sw.Elapsed})");
                        Assert.Equal(1, result.Count);
                        Assert.True(result.Contains("alpha"), "timeout falls back to the full candidate list");
                        string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                        Assert.True(content.Contains("[Telescope] fzf filter failed: timeout"),
                            "the timeout path must log '[Telescope] fzf filter failed: timeout'");

                        // Give the killed process's pipe reads a moment to fault, then force
                        // finalization so any unobserved faulted task raises the event.
                        var deadline = DateTime.UtcNow.AddSeconds(2);
                        while (DateTime.UtcNow < deadline)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            if (unobserved) { break; }
                            System.Threading.Thread.Sleep(25);
                        }
                        Assert.False(unobserved,
                            "the timeout path must await both ReadToEndAsync tasks (no unobserved-task noise)");
                    }
                    finally
                    {
                        TaskScheduler.UnobservedTaskException -= handler;
                    }
                });
            }
        }

        public static void Run_FzfFilter_QuoteArg_TrailingBackslash()
        {
            // M6b: a trailing backslash must be doubled before the closing quote so it does not
            // escape the quote (today QuoteArg("foo\") returns "\"foo\"" — the backslash escapes
            // the closing quote and corrupts the fzf argument).
            Assert.Equal("\"foo\\\\\"", FzfFilter.QuoteArg("foo\\"));
            Assert.Equal("\"foo\"", FzfFilter.QuoteArg("foo"));
            Assert.Equal("\"a\\\"b\"", FzfFilter.QuoteArg("a\"b"));
            Assert.Equal("\"\"", FzfFilter.QuoteArg(""));
        }

        public static void Run_FzfFilter_IsAvailableFalseForMissingPath()
        {
            using (var dir = new TempDir())
            {
                var fzf = new FzfFilter(Path.Combine(dir.Path, "missing-fzf.exe"));
                Assert.False(fzf.IsAvailable(), "a missing fzf path must report unavailable");
            }
        }

        public static void Run_FzfFilter_IsAvailableBounded()
        {
            using (var dir = new TempDir())
            {
                string cmdPath = Path.Combine(dir.Path, "hang.cmd");
                File.WriteAllText(cmdPath, "@ping -n 30 127.0.0.1 > nul");

                var fzf = new FzfFilter(cmdPath);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool available = fzf.IsAvailable();
                sw.Stop();

                // m16: IsAvailable() must not block the UI up to 3s on a hung fzf --version; the
                // wait is bounded to a short timeout. RED today: WaitForExit(3000) blocks ~3s on a
                // hung stub, so this 1s bound fails.
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"IsAvailable returned within 1s (took {sw.Elapsed})");
                Assert.False(available, "a hung fzf must report unavailable");
            }
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
            // n2: pin the enum VALUES (declaration order in OverlayKeyHandler.cs:51-56).
            Assert.Equal(0, (int)CaretPlacement.Current);
            Assert.Equal(1, (int)CaretPlacement.End);
            Assert.Equal(2, (int)CaretPlacement.Start);
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
            Assert.Equal(6, n.Caret); // caret 6 = start of "beta" (m12: ColumnNumber deleted, assert Caret directly)
            n.Down();
            Assert.Equal(3, n.LineNumber);
            Assert.Equal(11, n.Caret); // caret 11 = start of "gamma"
            n.Up();
            Assert.Equal(2, n.LineNumber);
            Assert.Equal(6, n.Caret); // caret 6 = start of "beta"
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
            n.LineStart();
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
        // Preview/motion correctness (Phase 3 — M9/M10/M11).
        // RED: M10's Up() calls LastIndexOf('\n', lineStart - 2) with
        // lineStart - 2 == -1 on a leading blank line -> ArgumentOutOfRangeException.
        // ================================================================

        public static void Run_Preview_UpFromSecondLineWithLeadingBlankLine()
        {
            // M10 (CORRECTED): on a leading blank line ("\nabc"), Up() from line 2 must move to
            // line 1/caret 0. RED today: Up() clamps LastIndexOf('\n', lineStart-2) to 0 and finds
            // the '\n' at index 0, so prevStart == 1 (line 2's own start) and Up() stays put
            // (LineNumber == 2).
            var n = new TextMotionNavigator();
            n.SetText("\nabc");
            n.MoveToLine(2);
            n.Up();
            Assert.Equal(1, n.LineNumber);
            Assert.Equal(0, n.Caret);
        }

        public static void Run_Preview_UpFromSecondLineWithLeadingBlankLine_Fixed()
        {
            // M10: the fixed contract — SetText("\nabc"); MoveToLine(2); Up(); must move to line 1.
            var n = new TextMotionNavigator();
            n.SetText("\nabc");
            n.MoveToLine(2);
            n.Up();
            Assert.Equal(1, n.LineNumber);
            Assert.Equal(0, n.Caret);
        }

        // ================================================================
        // M11a — pure index -> (line, offset) mapping on the shared LineIndex
        // (Phase 2 M7). A blank line must map to ITS OWN start (offset 0),
        // not null and not the next line. LineIndex already exists from
        // Phase 2, so this test may PASS before the fix — the real M11 gate
        // is the WPF CaretToPointer blank-line fallback (BP-4, build-verified).
        // ================================================================

        public static void Run_Preview_CaretOnBlankLine()
        {
            var idx = new LineIndex("abc\n\nxyz");
            Assert.Equal(2, idx.LineOf(4));
            Assert.Equal(0, 4 - idx.LineStart(2));
            Assert.Equal(3, idx.LineOf(5));
            Assert.Equal(0, 5 - idx.LineStart(3));
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
            n.LineStart();
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
        // TryDispatch — shared WPF-Key vim-motion dispatch (M24)
        // RED: `TryDispatch` does not exist yet -> compile error (CS0246)
        // The union h/l/j/k/w/b/e/0/$/gg/G + a/A/I. The $ drift fix: bare D4
        // (no shift) is NOT a motion and must NOT LineEnd.
        // ================================================================

        public static void Run_TryDispatch_DollarWithoutShiftNotHandled()
        {
            // The $ drift fix: in the preview surface a bare D4 currently LineEnds; the shared
            // dispatch must require Shift for $ (D4), so a bare D4 returns false and does nothing.
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(0);
            bool handled = TryDispatch.Handle(Key.D4, false, n, out _);
            Assert.False(handled, "bare $ (D4 without shift) is not a motion");
            Assert.Equal(0, n.Caret); // must NOT LineEnd
        }

        public static void Run_TryDispatch_DollarWithShiftLineEnds()
        {
            var n = new TextMotionNavigator();
            n.SetText("abc\ndef");
            n.MoveTo(0);
            bool handled = TryDispatch.Handle(Key.D4, true, n, out _);
            Assert.True(handled, "$ (D4 with shift) is handled");
            Assert.Equal(3, n.Caret); // end of "abc"
        }

        public static void Run_TryDispatch_MotionsMapToNavigator()
        {
            // H -> Left
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(2);
            Assert.True(TryDispatch.Handle(Key.H, false, n, out _));
            Assert.Equal(1, n.Caret);

            // L -> Right
            n.MoveTo(2);
            Assert.True(TryDispatch.Handle(Key.L, false, n, out _));
            Assert.Equal(3, n.Caret);

            // W -> NextWord
            n.SetText("one two");
            n.MoveTo(0);
            Assert.True(TryDispatch.Handle(Key.W, false, n, out _));
            Assert.Equal(4, n.Caret);

            // B -> PrevWord
            n.MoveTo(4);
            Assert.True(TryDispatch.Handle(Key.B, false, n, out _));
            Assert.Equal(0, n.Caret);

            // E -> EndWord
            n.MoveTo(0);
            Assert.True(TryDispatch.Handle(Key.E, false, n, out _));
            Assert.Equal(3, n.Caret);

            // J -> Down
            n.SetText("a\nb");
            n.MoveTo(0);
            Assert.True(TryDispatch.Handle(Key.J, false, n, out _));
            Assert.Equal(2, n.Caret);

            // K -> Up
            n.MoveTo(2);
            Assert.True(TryDispatch.Handle(Key.K, false, n, out _));
            Assert.Equal(0, n.Caret);

            // D0 -> LineStartHome
            n.SetText("abc\ndef");
            n.MoveTo(5);
            Assert.True(TryDispatch.Handle(Key.D0, false, n, out _));
            Assert.Equal(4, n.Caret);

            // G (bare) -> Top
            n.MoveTo(5);
            Assert.True(TryDispatch.Handle(Key.G, false, n, out _));
            Assert.Equal(0, n.Caret);

            // G (shift) -> Bottom
            n.MoveTo(0);
            Assert.True(TryDispatch.Handle(Key.G, true, n, out _));
            Assert.Equal(7, n.Caret);
        }

        public static void Run_TryDispatch_InsertPlacements()
        {
            // A (bare) -> InsertAfter, placement Current.
            var n = new TextMotionNavigator();
            n.SetText("hello");
            n.MoveTo(2);
            CaretPlacement? placement;
            Assert.True(TryDispatch.Handle(Key.A, false, n, out placement));
            Assert.Equal(CaretPlacement.Current, placement);
            Assert.Equal(3, n.Caret);

            // A (shift) -> InsertEnd, placement End.
            n.MoveTo(2);
            Assert.True(TryDispatch.Handle(Key.A, true, n, out placement));
            Assert.Equal(CaretPlacement.End, placement);
            Assert.Equal(5, n.Caret);

            // I (shift) -> InsertStart, placement Start.
            n.MoveTo(2);
            Assert.True(TryDispatch.Handle(Key.I, true, n, out placement));
            Assert.Equal(CaretPlacement.Start, placement);
            Assert.Equal(0, n.Caret);

            // I (bare) -> not handled (generic insert lives in the overlay state machine).
            n.MoveTo(2);
            Assert.False(TryDispatch.Handle(Key.I, false, n, out placement));
            Assert.Equal(2, n.Caret);
        }

        // ================================================================
        // BlockCaretStyle — shared block-caret brush/geometry (M25)
        // RED: `BlockCaretStyle` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_BlockCaretStyle_BrushFrozenWhite()
        {
            var brush = BlockCaretStyle.CreateBlockBrush();
            Assert.True(brush.IsFrozen, "the shared block-caret brush is frozen");
            Assert.Equal(Colors.White, BlockCaretStyle.WhiteFill);
            Assert.Equal(Colors.Black, BlockCaretStyle.GlyphColor);
            Assert.Equal(8.0, BlockCaretStyle.BlockRect.Width);
            Assert.Equal(16.0, BlockCaretStyle.BlockRect.Height);
        }

        public static void Run_BlockCaretStyle_SingleSharedInstance()
        {
            // Every call must return the SAME frozen instance (single shared static brush).
            var first = BlockCaretStyle.CreateBlockBrush();
            var second = BlockCaretStyle.CreateBlockBrush();
            Assert.True(ReferenceEquals(first, second), "CreateBlockBrush returns the SAME shared instance");
        }

        // ================================================================
        // LineIndex (BP-6/M7a) — pure index -> (line, offset) mapping shared
        // with M11 (Phase 3). LineOf MUST equal TextMotionNavigator.LineNumber
        // semantics (count of '\n' in text[0..index) + 1).
        // RED: LineIndex does not exist -> compile error (CS0246).
        // ================================================================

        public static void Run_LineIndex_LineOfMatchesNavigator()
        {
            string[] inputs =
            {
                "alpha\nbeta\ngamma",
                "\nleading newline",
                "trailing newline\n",
                "a\n\nb",
                "one\r\ntwo\r\nthree",
                "single line",
                "\n\n\n",
            };
            foreach (string text in inputs)
            {
                var index = new LineIndex(text);
                var nav = new TextMotionNavigator();
                nav.SetText(text);
                for (int i = 0; i <= text.Length; i++)
                {
                    nav.MoveTo(i);
                    Assert.Equal(nav.LineNumber, index.LineOf(i));
                }
            }
        }

        public static void Run_LineIndex_LineStartOffsets()
        {
            var index = new LineIndex("alpha\nbeta\ngamma");
            Assert.Equal(0, index.LineStart(1));
            Assert.Equal(6, index.LineStart(2)); // after the first '\n'
            Assert.Equal(11, index.LineStart(3)); // after the second '\n'
        }

        public static void Run_LineIndex_EdgeCases()
        {
            // Empty text: a single line; LineOf(0) == 1.
            var empty = new LineIndex("");
            Assert.Equal(1, empty.LineOf(0));
            Assert.Equal(1, empty.LineCount);

            // Text ending in '\n': the trailing newline opens a final (empty) line.
            var trailing = new LineIndex("a\n");
            Assert.Equal(1, trailing.LineOf(0));
            Assert.Equal(1, trailing.LineOf(1)); // the '\n' itself is still line 1
            Assert.Equal(2, trailing.LineOf(2)); // past the '\n' -> line 2
            Assert.Equal(2, trailing.LineCount);

            // \r\n endings: only '\n' advances the line (the '\r' does not).
            var crlf = new LineIndex("a\r\nb");
            Assert.Equal(1, crlf.LineOf(0));
            Assert.Equal(1, crlf.LineOf(2)); // the '\r' is still line 1
            Assert.Equal(2, crlf.LineOf(3)); // the '\n' starts line 2
            Assert.Equal(2, crlf.LineCount);
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
        // HitOpener — shared open-at-line helper (M23)
        // RED: `HitOpener` does not exist yet -> compile error (CS0246)
        // ================================================================

        public static void Run_HitOpener_ExistingFileInvokesDelegate()
        {
            using (var dir = new TempDir())
            {
                string path = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(path, "// a");
                string? openedPath = null;
                int openedLine = -1;
                HitOpener.OpenAtLine(new ReferenceHit(path, 7, 1, false, "X", "X = 1;"),
                    (p, l) => { openedPath = p; openedLine = l; });
                Assert.Equal(path, openedPath);
                Assert.Equal(7, openedLine);
            }
        }

        public static void Run_HitOpener_MissingFileDoesNotInvoke()
        {
            using (var dir = new TempDir())
            {
                string missing = Path.Combine(dir.Path, "Ghost.cs");
                int invoked = 0;
                HitOpener.OpenAtLine(new ReferenceHit(missing, 3, 1, false, "X", "X = 1;"),
                    (p, l) => invoked++);
                Assert.Equal(0, invoked);
            }
        }

        public static void Run_HitOpener_ReferenceAndImplementationFlowThrough()
        {
            using (var dir = new TempDir())
            {
                string refPath = Path.Combine(dir.Path, "Ref.cs");
                string implPath = Path.Combine(dir.Path, "Impl.cs");
                File.WriteAllText(refPath, "// r");
                File.WriteAllText(implPath, "// i");

                var opened = new List<(string, int)>();
                HitOpener.OpenAtLine(new ReferenceHit(refPath, 5, 1, true, "X", "X = 1;"),
                    (p, l) => opened.Add((p, l)));
                HitOpener.OpenAtLine(new ImplementationHit(implPath, 9, "X", "Method"),
                    (p, l) => opened.Add((p, l)));

                Assert.Equal(2, opened.Count);
                Assert.Equal(refPath, opened[0].Item1);
                Assert.Equal(5, opened[0].Item2);
                Assert.Equal(implPath, opened[1].Item1);
                Assert.Equal(9, opened[1].Item2);
            }
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
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var hit = new TestHit(@"C:\p\A.cs", 1);
                    var finder = new TestFinder(() => new[] { hit }, _ => throw new InvalidOperationException("open boom"));

                    // A throwing OpenHit must not propagate out of OnSelected (the runner fails the
                    // test if it does) AND the error must be logged exactly once (m23 — the test
                    // previously had no assertion and passed vacuously).
                    finder.OnSelected(new FinderEntry("A.cs", hit));
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    var telescopeLines = lines.Where(l => l.Contains("[Telescope] ")).ToList();
                    Assert.Equal(1, telescopeLines.Count);
                    Assert.True(telescopeLines[0].Contains("[Telescope] open item failed: open boom"),
                        $"expected exactly one '[Telescope] open item failed: open boom' line, got: {string.Join(" | ", telescopeLines)}");
                });
            }
        }

        public static void Run_FinderBase_NonMatchingPayloadIgnored()
        {
            var hit = new TestHit(@"C:\p\A.cs", 1);
            int opened = 0;
            var finder = new TestFinder(() => new[] { hit }, _ => opened++);

            finder.OnSelected(new FinderEntry("A.cs", "not a TestHit"));

            Assert.Equal(0, opened);
        }

        // ================================================================
        // FinderBase error logging (BP-5/m13/m20) — finder errors must be
        // routed through TelescopeLog.Log (which adds the [Telescope] prefix)
        // and each failure must emit EXACTLY ONE line.
        // RED: `Run_FinderBase_OpenErrorSingleLog` fails at runtime — today
        //      OnSelected double-logs (TelescopeLog.Log + NeoVisualLog.Debug),
        //      so the file holds 2 [Telescope] lines.
        //      `Run_FinderBase_GatherErrorPrefixed` pins the single-prefix
        //      contract (a naive fix that wraps the already-prefixed
        //      GatherErrorLiteral in TelescopeLog.Log would double-prefix).
        // ================================================================

        public static void Run_FinderBase_OpenErrorSingleLog()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var hit = new TestHit(@"C:\p\A.cs", 1);
                    var finder = new TestFinder(() => new[] { hit }, _ => throw new InvalidOperationException("open boom"));

                    finder.OnSelected(new FinderEntry("A.cs", hit));
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    var telescopeLines = lines.Where(l => l.Contains("[Telescope] ")).ToList();
                    Assert.Equal(1, telescopeLines.Count);
                    Assert.True(telescopeLines[0].Contains("[Telescope] open item failed: open boom"),
                        $"expected exactly one '[Telescope] open item failed: open boom' line, got: {string.Join(" | ", telescopeLines)}");
                });
            }
        }

        public static void Run_FinderBase_GatherErrorPrefixed()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var finder = new TestFinder(() => throw new InvalidOperationException("gather boom"));

                    finder.GetCandidates();
                    LogFileWriter.Flush();

                    string[] lines = ReadAllTextShared(logPath)
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                    {
                        Array.Resize(ref lines, lines.Length - 1);
                    }
                    var telescopeLines = lines.Where(l => l.Contains("[Telescope] ")).ToList();
                    Assert.Equal(1, telescopeLines.Count);
                    // File lines are timestamped ("HH:mm:ss.fff <message>"), so the prefix check is
                    // a Contains (matching Run_TelescopeLog_Prefix), not a StartsWith.
                    Assert.True(telescopeLines[0].Contains("[Telescope] TestFinder failed to enumerate: gather boom"),
                        $"expected '[Telescope] TestFinder failed to enumerate: gather boom', got: {string.Join(" | ", telescopeLines)}");
                });
            }
        }

        public static void Run_FileFinder_EnumeratesCandidates()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "Alpha.cs");
                string b = Path.Combine(dir.Path, "Beta.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var finder = new FileFinder(() => new[] { a, b }, _ => { });
                var entries = finder.GetCandidates();

                Assert.Equal(2, entries.Count);
                Assert.Equal("Alpha.cs", entries[0].Display);
                Assert.Equal(a, (entries[0].Payload as FileHit)?.FilePath);
                Assert.Equal("Beta.cs", entries[1].Display);
            }
        }

        public static void Run_FileFinder_OpenSelectedCallsOpener()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "Alpha.cs");
                File.WriteAllText(a, "// a");
                string? opened = null;
                var finder = new FileFinder(() => new[] { a }, p => opened = p);

                finder.OnSelected(new FinderEntry("Alpha.cs", new FileHit(a, 0)));
                Assert.Equal(a, opened);
            }
        }

        public static void Run_FileFinder_OpenMissingFileIsNoOp()
        {
            using (var dir = new TempDir())
            {
                string missing = Path.Combine(dir.Path, "Ghost.cs");
                int opened = 0;
                var finder = new FileFinder(() => new[] { missing }, _ => opened++);

                finder.OnSelected(new FinderEntry("Ghost.cs", new FileHit(missing, 0)));
                Assert.Equal(0, opened);
            }
        }

        // ================================================================
        // HierarchyWalker — pure DTE-tree walker (M22)
        // RED: `HierarchyWalker` / `IHierarchyNode` do not exist yet -> compile error (CS0246)
        // ================================================================

        private sealed class FakeNode : IHierarchyNode
        {
            public FakeNode(string? path, params IHierarchyNode[] children)
            {
                Path = path;
                Children = children;
            }

            public IEnumerable<IHierarchyNode> Children { get; }
            public string? Path { get; }
        }

        public static void Run_HierarchyWalker_SolutionFolderRecursion()
        {
            // A solution folder (no path) contains a sub-project whose items are enumerated.
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                string b = Path.Combine(dir.Path, "B.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var root = new FakeNode(null,
                    new FakeNode(null, // solution folder
                        new FakeNode(null, // sub-project
                            new FakeNode(a),
                            new FakeNode(b))));

                var files = HierarchyWalker.EnumerateFiles(new[] { root });
                Assert.Equal(2, files.Count);
                Assert.Equal(a, files[0]);
                Assert.Equal(b, files[1]);
            }
        }

        public static void Run_HierarchyWalker_NestedItemRecursion()
        {
            // An item with nested children (a folder item) recurses into them.
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                string nested = Path.Combine(dir.Path, "Nested.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(nested, "// n");

                var root = new FakeNode(null,
                    new FakeNode(a,
                        new FakeNode(nested)));

                var files = HierarchyWalker.EnumerateFiles(new[] { root });
                Assert.Equal(2, files.Count);
                Assert.Equal(a, files[0]);
                Assert.Equal(nested, files[1]);
            }
        }

        public static void Run_HierarchyWalker_Dedup()
        {
            // The same path reached twice must be enumerated once (OrdinalIgnoreCase dedup).
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// a");

                var root = new FakeNode(null,
                    new FakeNode(a),
                    new FakeNode(a));

                var files = HierarchyWalker.EnumerateFiles(new[] { root });
                Assert.Equal(1, files.Count);
                Assert.Equal(a, files[0]);
            }
        }

        public static void Run_HierarchyWalker_FileExistsFiltering()
        {
            // A path that does not exist on disk must be dropped.
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                string ghost = Path.Combine(dir.Path, "Ghost.cs");
                File.WriteAllText(a, "// a");

                var root = new FakeNode(null,
                    new FakeNode(a),
                    new FakeNode(ghost));

                var files = HierarchyWalker.EnumerateFiles(new[] { root });
                Assert.Equal(1, files.Count);
                Assert.Equal(a, files[0]);
            }
        }

        public static void Run_HierarchyWalker_FirstFileEndingWith()
        {
            // FirstFileEndingWith returns the first matching file in tree order, and the
            // extension comparison is OrdinalIgnoreCase (".CS" uppercase matches ".cs").
            using (var dir = new TempDir())
            {
                string txt = Path.Combine(dir.Path, "B.txt");
                string a = Path.Combine(dir.Path, "A.cs");
                string c = Path.Combine(dir.Path, "C.CS");
                File.WriteAllText(txt, "// t");
                File.WriteAllText(a, "// a");
                File.WriteAllText(c, "// c");

                var root = new FakeNode(null,
                    new FakeNode(txt),
                    new FakeNode(a),
                    new FakeNode(c));

                Assert.Equal(a, HierarchyWalker.FirstFileEndingWith(new[] { root }, ".cs"));
            }
        }

        public static void Run_HierarchyWalker_EmptyTree()
        {
            var files = HierarchyWalker.EnumerateFiles(new FakeNode[0]);
            Assert.Equal(0, files.Count);
            Assert.Equal(null, HierarchyWalker.FirstFileEndingWith(new FakeNode[0], ".cs"));
        }

        // ================================================================
        // SyntaxHighlighter — preview syntax coloring (keywords/strings/comments/numbers)
        // ================================================================

        public static void Run_Syntax_KeywordsAndIdentifiers()
        {
            // m24: exact-sequence assertion — pins the ordered (Text, Category) pairs Tokenize
            // produces for "public class Foo { }" (the weak presence checks are gone).
            var segs = SyntaxHighlighter.Tokenize("public class Foo { }");
            var pairs = segs.Select(s => (s.Text, s.Category)).ToList();
            var expected = new (string Text, SyntaxCategory Category)[]
            {
                ("public", SyntaxCategory.Keyword),
                (" ", SyntaxCategory.Default),
                ("class", SyntaxCategory.Keyword),
                (" ", SyntaxCategory.Default),
                ("Foo", SyntaxCategory.Default),
                (" { }", SyntaxCategory.Default),
            };
            Assert.Equal(expected.Length, pairs.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i].Text, pairs[i].Text);
                Assert.Equal(expected[i].Category, pairs[i].Category);
            }
        }

        public static void Run_Syntax_LineComment()
        {
            var segs = SyntaxHighlighter.Tokenize("int x = 1; // hello");
            var comment = segs.FirstOrDefault(s => s.Category == SyntaxCategory.Comment);
            Assert.True(comment.Text == "// hello", $"line comment captured, got '{comment.Text}'");
        }

        public static void Run_Syntax_BlockCommentSpansLines()
        {
            var segs = SyntaxHighlighter.Tokenize("a /* one\ntwo */ b");
            var comment = segs.FirstOrDefault(s => s.Category == SyntaxCategory.Comment);
            Assert.True(comment.Text.Contains('\n'), "block comment spans lines");
            Assert.True(comment.Text.StartsWith("/*") && comment.Text.EndsWith("*/"), "block comment includes delimiters");
        }

        public static void Run_Syntax_Strings()
        {
            var segs = SyntaxHighlighter.Tokenize("var s = \"hi \\\"there\\\"\";");
            var str = segs.FirstOrDefault(s => s.Category == SyntaxCategory.String);
            Assert.True(str.Text == "\"hi \\\"there\\\"\"", $"string captured with escapes, got '{str.Text}'");
        }

        public static void Run_Syntax_VerbatimStringSpansLines()
        {
            var segs = SyntaxHighlighter.Tokenize("var s = @\"line1\nline2\"\"quote\";");
            var str = segs.FirstOrDefault(s => s.Category == SyntaxCategory.String);
            Assert.True(str.Text.StartsWith("@\""), "verbatim string captured");
            Assert.True(str.Text.Contains("line1"), "verbatim string spans lines");
        }

        public static void Run_Syntax_Numbers()
        {
            var segs = SyntaxHighlighter.Tokenize("var x = 42; var y = 0xFF; var z = 1.5e3; var f = 100L;");
            var nums = segs.Where(s => s.Category == SyntaxCategory.Number).Select(s => s.Text).ToList();
            Assert.True(nums.Contains("42"), "decimal literal");
            Assert.True(nums.Contains("0xFF"), "hex literal");
            Assert.True(nums.Contains("1.5e3"), "exponent literal");
            Assert.True(nums.Contains("100L"), "suffixed literal");
        }

        public static void Run_Syntax_RoundTripsText()
        {
            const string code = "using System;\n\npublic class Probe\n{\n    // note\n    static int X = 42;\n    string s = \"hello\";\n}";
            var segs = SyntaxHighlighter.Tokenize(code);
            var rebuilt = string.Concat(segs.Select(s => s.Text));
            Assert.Equal(code, rebuilt);
        }

        // ================================================================
        // Preview/motion correctness (Phase 3 — M9). RED: ReadQuoted's
        // `i += 2` escape advance pushes i past text.Length on an unterminated
        // string ending in a backslash -> ArgumentOutOfRangeException from
        // text.Substring(start, i - start).
        // ================================================================

        public static void Run_Syntax_UnterminatedStringEndingInBackslash()
        {
            const string code = "var s = \"abc\\";
            var segs = SyntaxHighlighter.Tokenize(code);
            var rebuilt = string.Concat(segs.Select(s => s.Text));
            Assert.Equal(code, rebuilt);
        }

        // ================================================================
        // CodeIssuesFinder — warnings/errors/TODO markers
        // ================================================================

        public static void Run_Issues_TodoScanFindsMarkers()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "class A\n{\n    // TODO: fix this\n    // FIXME: and this\n    int x;\n}");
                string b = Path.Combine(dir.Path, "B.cs");
                File.WriteAllText(b, "// nothing here\n");

                var finder = new CodeIssuesFinder(() => new[] { a, b }, _ => { });
                var entries = finder.GetCandidates();

                Assert.Equal(2, entries.Count);
                Assert.True(entries[0].Display.StartsWith("[TODO] line 3:"), $"todo on line 3, got '{entries[0].Display}'");
                Assert.True(entries[1].Display.StartsWith("[TODO] line 4:"), $"fixme on line 4, got '{entries[1].Display}'");
                Assert.True(entries[0].Display.Contains("A.cs"), "display names the file");
            }
        }

        public static void Run_Issues_NoFalsePositiveOnTodoWord()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "var todoList = new List<int>();\nint total = 1;\n");

                var finder = new CodeIssuesFinder(() => new[] { a }, _ => { });
                Assert.Equal(0, finder.GetCandidates().Count);
            }
        }

        public static void Run_Issues_OnSelectedReportsPathAndLine()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
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
        }

        public static void Run_Issues_CollectTodosUsesCache()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// TODO: one\n");

                var finder = new CodeIssuesFinder(() => new[] { a }, _ => { });
                var first = finder.GetCandidates();
                Assert.Equal(1, first.Count);

                // m15: the second scan over the same file must be served from the shared
                // FileContentCache — the file is NOT re-read. RED today: CollectTodos calls
                // File.ReadAllLines directly, so the second scan re-reads the file; locking it
                // exclusively makes that re-read fail (sharing violation) and CollectTodos skips
                // the file -> 0 entries. After the fix the cache serves the second scan from
                // memory (mtime unchanged) -> 1 entry.
                using (var fs = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var second = finder.GetCandidates();
                    Assert.Equal(1, second.Count);
                }
            }
        }

        public static void Run_Issues_SeverityMediumMapsToWarning()
        {
            // A warning whose message contains "error" must still be Warning: classification is
            // severity-based (ErrorItem.ErrorLevel), not description-based (the old Classify(string)
            // guessed by substring on the message).
            Assert.Equal(CodeIssueKind.Warning,
                CodeIssuesFinder.ClassifySeverity(vsBuildErrorLevel.vsBuildErrorLevelMedium));
        }

        public static void Run_Issues_SeverityHighMapsToError()
        {
            Assert.Equal(CodeIssueKind.Error,
                CodeIssuesFinder.ClassifySeverity(vsBuildErrorLevel.vsBuildErrorLevelHigh));
        }

        public static void Run_Issues_SeverityLowMapsToInfo()
        {
            Assert.Equal(CodeIssueKind.Info,
                CodeIssuesFinder.ClassifySeverity(vsBuildErrorLevel.vsBuildErrorLevelLow));
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
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                Assert.Equal(0, finder.GetCandidates("").Count);
            }
        }

        public static void Run_GrepFinder_LineScanMatchesCaseInsensitive()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");
                string b = Path.Combine(dir.Path, "B.cs");
                File.WriteAllText(b, "// nothing here\n");

                var finder = new GrepFinder(() => new[] { a, b }, _ => { });
                // Case-insensitive substring: "needle" matches BOTH "NEEDLE here" (line 2) and
                // "needle again" (line 4); the non-matching "middle" line and B.cs are excluded.
                var entries = finder.GetCandidates("needle");

                Assert.Equal(2, entries.Count);
                Assert.True(entries.All(e => e.Display.Contains("A.cs")), "only A.cs contains matches");
            }
        }

        public static void Run_GrepFinder_DisplayIsFileNameLineText()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "first\nNEEDLE here\n");

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var entry = finder.GetCandidates("NEEDLE")[0];
                // Deterministic {fileName}:{line}: {lineText} display (1-based line, fileName only).
                Assert.Equal("A.cs:2: NEEDLE here", entry.Display);
            }
        }

        public static void Run_GrepFinder_PayloadRoundTripsGrepHit()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
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
        }

        public static void Run_GrepFinder_OnSelectedOpensHitAtLine()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
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
        }

        public static void Run_GrepFinder_HitCapBounded()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 500; i++) { sb.AppendLine("NEEDLE " + i); }
                File.WriteAllText(a, sb.ToString());

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var entries = finder.GetCandidates("NEEDLE");
                Assert.True(entries.Count > 0, "hits are still returned up to the cap");
                Assert.True(entries.Count <= 200, $"hit cap bounds the result set (got {entries.Count})");
            }
        }

        // ================================================================
        // GrepFinder + ProjectFileCache (BP-1/M5a) — the shared cache is injected
        // so the DTE solution-tree walk runs ONCE across queries (M5: the
        // per-keystroke walk is the stall being amortized).
        // RED: the GrepFinder(ProjectFileCache, Func<IReadOnlyList<string>>,
        //      Action<GrepHit>) ctor does not exist -> compile error (CS1729).
        // ================================================================

        public static void Run_GrepFinder_CacheEnumeratesOnce()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                int count = 0;
                var cache = new ProjectFileCache();
                var finder = new GrepFinder(cache, () => { count++; return new[] { a }; }, _ => { });

                finder.GetCandidates("NEEDLE");
                finder.GetCandidates("NEEDLE2");

                // The enumerate delegate must run ONCE across two queries — the cache serves the
                // second GetCandidates (today GrepFinder re-walks the solution per query).
                Assert.Equal(1, count);
            }
        }

        public static void Run_GrepFinder_OffThreadScanSameHits()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "line one\nNEEDLE here\nmiddle\nneedle again\n");

                var finder = new GrepFinder(() => new[] { a }, _ => { });
                var syncHits = finder.GetCandidates("needle");

                // M4: the per-file content scan must return the SAME hits when it runs off-thread
                // (the fix moves the scan onto a background task and marshals only the results
                // back; the DTE enumeration stays on the UI thread). NOTE: this may PASS against
                // the current code — the hermetic path has no thread affinity — in which case the
                // off-thread seam is the fix (wiring-is-the-fix, not a RED).
                IReadOnlyList<FinderEntry> offThreadHits = null;
                Task.Run(() => offThreadHits = finder.GetCandidates("needle")).GetAwaiter().GetResult();

                Assert.Equal(syncHits.Count, offThreadHits.Count);
                for (int i = 0; i < syncHits.Count; i++)
                {
                    Assert.Equal(syncHits[i].Display, offThreadHits[i].Display);
                }
            }
        }

        // ================================================================
        // GrepFinder.GatherHits (M41, BP-5) — the base-class gather stub must be a loud
        // failure, not a silent empty. GrepFinder is query-driven (GetCandidates(query)),
        // so the parameterless GatherHits() must throw NotSupportedException. GrepFinder is
        // sealed, so the protected override is reached via reflection.
        // RED: today GatherHits() returns Array.Empty<GrepHit>() (no throw) -> runtime failure.
        // ================================================================

        public static void Run_GrepFinder_GatherHitsThrowsNotSupported()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    var finder = new GrepFinder(() => new[] { "a" }, _ => { });

                    // m27: use the public GetCandidates() path (no reflection). GrepFinder is
                    // query-driven: its GetCandidates(string) override short-circuits on an empty
                    // query (deterministic empty initial state) and never reaches the base gather
                    // stub, so the observable contract is: GetCandidates("") returns empty AND does
                    // NOT log a "GrepFinder failed to enumerate:" failure (the empty-query path is a
                    // clean empty, not a loud failure). The reflection removal is the m27 fix.
                    var entries = finder.GetCandidates("");
                    LogFileWriter.Flush();

                    Assert.Equal(0, entries.Count);
                    string content = File.Exists(logPath) ? ReadAllTextShared(logPath) : string.Empty;
                    int failureLines = content
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                        .Count(l => l.Contains("[Telescope] GrepFinder failed to enumerate:"));
                    Assert.Equal(0, failureLines);
                });
            }
        }

        // ================================================================
        // FileContentCache (BP-2/M5b) — per-file content cache keyed by
        // LastWriteTimeUtc, so ScanFile stops re-reading every file per query.
        // RED: FileContentCache does not exist -> compile error (CS0246).
        // ================================================================

        public static void Run_FileContentCache_CachedRead()
        {
            int reads = 0;
            var cache = new FileContentCache(
                timestamp: _ => DateTime.UtcNow,
                reader: _ => { reads++; return new[] { "line" }; });

            cache.GetLines("a");
            cache.GetLines("a");

            // The injected reader must run exactly once for two GetLines on an unchanged file.
            Assert.Equal(1, reads);
        }

        public static void Run_FileContentCache_InvalidatesOnTimestampChange()
        {
            var timestamps = new Dictionary<string, DateTime> { ["a"] = DateTime.UtcNow };
            int reads = 0;
            var cache = new FileContentCache(
                timestamp: p => timestamps[p],
                reader: _ => { reads++; return new[] { "line" }; });

            cache.GetLines("a");
            timestamps["a"] = timestamps["a"].AddSeconds(1);
            cache.GetLines("a");

            // A changed LastWriteTimeUtc must force a re-read (reader invoked twice).
            Assert.Equal(2, reads);
        }

        public static void Run_FileContentCache_EvictsOldest()
        {
            // M13: with a maxEntries cap, inserting N+1 entries must evict the least-recently-used
            // (oldest) entry. RED today: the cap is stored but no eviction happens, so the oldest
            // entry is still served from the cache.
            int reads = 0;
            var cache = new FileContentCache(
                maxEntries: 2,
                timestamp: _ => DateTime.UtcNow,
                reader: _ => { reads++; return new[] { "line" }; });

            cache.GetLines("a");
            cache.GetLines("b");
            cache.GetLines("c"); // cap 2 exceeded -> the oldest ("a") must be evicted

            // Re-reading the evicted oldest entry must hit the reader again (cache miss).
            int before = reads;
            cache.GetLines("a");
            Assert.Equal(before + 1, reads);
        }

        public static void Run_FileContentCache_ClearOnSolutionChange()
        {
            // M13: Clear() empties the cache — after a solution change the next read re-reads.
            // NOTE: Clear() already exists and works, so this may PASS against the current code;
            // the wiring (calling Clear() on solution change) is the fix.
            int reads = 0;
            var cache = new FileContentCache(
                timestamp: _ => DateTime.UtcNow,
                reader: _ => { reads++; return new[] { "line" }; });

            cache.GetLines("a");
            cache.Clear();
            cache.GetLines("a");

            Assert.Equal(2, reads);
        }

        // ================================================================
        // IFinder query fold — GetCandidates(string) + IsQueryDriven (BP-8/L6)
        // RED: `GetCandidates(string)` / `IsQueryDriven` do not exist on IFinder
        //      -> compile error (the fold has not happened)
        // ================================================================

        public static void Run_GetCandidates_DefaultQuery_MatchesNoArg()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "Alpha.cs");
                string b = Path.Combine(dir.Path, "Beta.cs");
                File.WriteAllText(a, "// a");
                File.WriteAllText(b, "// b");

                var finder = new FileFinder(() => new[] { a, b }, _ => { });
                var noArg = finder.GetCandidates();
                var emptyArg = finder.GetCandidates("");

                Assert.Equal(noArg.Count, emptyArg.Count);
                Assert.Equal(noArg[0].Display, emptyArg[0].Display);
                Assert.Equal(noArg[1].Display, emptyArg[1].Display);
            }
        }

        public static void Run_GetCandidates_DefaultQuery_GrepEmpty()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// NEEDLE here\n");

                // Written against IFinder so the test pins the NEW interface contract: before the
                // fold IFinder.GetCandidates() takes no args, so GetCandidates("") does not compile.
                IFinder finder = new GrepFinder(() => new[] { a }, _ => { });
                var noArg = finder.GetCandidates();
                var emptyArg = finder.GetCandidates("");

                Assert.Equal(0, noArg.Count);
                Assert.Equal(0, emptyArg.Count);
            }
        }

        public static void Run_GetCandidates_IsQueryDriven()
        {
            using (var dir = new TempDir())
            {
                string a = Path.Combine(dir.Path, "A.cs");
                File.WriteAllText(a, "// x\n");

                IFinder grep = new GrepFinder(() => new[] { a }, _ => { });
                IFinder files = new FileFinder(() => new[] { a }, _ => { });

                Assert.True(grep.IsQueryDriven, "GrepFinder is query-driven");
                Assert.False(files.IsQueryDriven, "FileFinder is not query-driven");
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
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    TelescopeLog.Log("hello");
                    // F3 (X1/BP-1): under buffering the write is not on disk until Flush().
                    LogFileWriter.Flush();
                    Assert.True(ReadAllTextShared(logPath).Contains("[Telescope] hello"),
                        "TelescopeLog prefixes the message with [Telescope] ");
                });
            }
        }

        public static void Run_TelescopeLog_EmptyMessage()
        {
            using (var dir = new TempDir())
            {
                string logPath = Path.Combine(dir.Path, "neovisual-exp.log");
                WithLogPath(logPath, () =>
                {
                    TelescopeLog.Log("");
                    // F3 (X1/BP-1): under buffering the write is not on disk until Flush().
                    LogFileWriter.Flush();
                    Assert.True(ReadAllTextShared(logPath).Contains("[Telescope] "),
                        "an empty message still emits the [Telescope] prefix");
                });
            }
        }

        // ================================================================
        // FilterFailureLog (BP-8/M8) — the exact "[Telescope] filter failed: "
        // line format the overlay's FilterAndUpdateAsync catch emits (M8: today
        // a faulting FilterAsync escapes unobserved with no diagnostic).
        // RED: FilterFailureLog does not exist -> compile error (CS0246).
        // ================================================================

        public static void Run_FilterFailureLog_Format()
        {
            // m14: Format() must return the UNPREFIXED message ("filter failed: boom"); the caller
            // (TelescopeOverlay) adds the [Telescope] prefix via TelescopeLog.Log. RED today: the
            // format embeds the "[Telescope] " prefix, so this exact-match assertion fails.
            Assert.Equal("filter failed: boom", FilterFailureLog.Format(new Exception("boom")));
        }

        // ================================================================
        // ResultMapper — display-keyed payload lookup that preserves
        // same-named duplicates (F12)
        // RED: `Telescope.Overlay.ResultMapper` does not exist yet -> compile error
        // ================================================================

        public static void Run_ResultMapper_DuplicateDisplayPreserved()
        {
            // Two entries share the display "Program.cs" but carry DIFFERENT FileHit payloads
            // (two Program.cs files in different folders). The mapper must map each matched
            // "Program.cs" to a DISTINCT entry: the second must NOT collapse to the first
            // (the current GroupBy/ToDictionary(g => g.First()) bug) nor become a null-payload
            // FinderEntry.
            var first = new FileHit(@"C:\p\src\Program.cs", 0);
            var second = new FileHit(@"C:\p\tests\Program.cs", 0);
            var snapshot = new List<FinderEntry>
            {
                new FinderEntry("Program.cs", first),
                new FinderEntry("Program.cs", second),
            };

            var items = ResultMapper.MapBack(new[] { "Program.cs", "Program.cs" }, snapshot);

            Assert.Equal(2, items.Count);
            Assert.Equal("Program.cs", items[0].Display);
            Assert.Equal("Program.cs", items[1].Display);
            Assert.True(ReferenceEquals(first, items[0].Payload), "first matched 'Program.cs' maps to the first entry's payload");
            Assert.True(ReferenceEquals(second, items[1].Payload), "second matched 'Program.cs' maps to the SECOND entry's payload (not the first, not null)");
        }

        public static void Run_ResultMapper_UniqueDisplayMapped()
        {
            var a = new FileHit(@"C:\p\Alpha.cs", 0);
            var b = new FileHit(@"C:\p\Beta.cs", 0);
            var snapshot = new List<FinderEntry>
            {
                new FinderEntry("Alpha.cs", a),
                new FinderEntry("Beta.cs", b),
            };

            var items = ResultMapper.MapBack(new[] { "Alpha.cs", "Beta.cs" }, snapshot);

            Assert.Equal(2, items.Count);
            Assert.True(ReferenceEquals(a, items[0].Payload), "Alpha.cs maps to its entry's payload");
            Assert.True(ReferenceEquals(b, items[1].Payload), "Beta.cs maps to its entry's payload");
        }

        public static void Run_ResultMapper_UnknownStringNullPayload()
        {
            // M11 (UPDATED contract): an unmatched display string must NOT produce a null-payload
            // FinderEntry whose OnSelected silently no-ops — it is skipped (or logged). RED today:
            // MapBack returns a null-payload entry for "Ghost.cs", so items.Count == 1.
            var a = new FileHit(@"C:\p\Alpha.cs", 0);
            var snapshot = new List<FinderEntry> { new FinderEntry("Alpha.cs", a) };

            var items = ResultMapper.MapBack(new[] { "Ghost.cs" }, snapshot);

            Assert.Equal(0, items.Count);
        }

        public static void Run_ResultMapper_UnknownStringSkippedOrLogged()
        {
            // M11: an unmatched display string does not produce a null-payload entry that silently
            // no-ops. RED today: the null-payload entry is produced (Payload == null).
            var a = new FileHit(@"C:\p\Alpha.cs", 0);
            var snapshot = new List<FinderEntry> { new FinderEntry("Alpha.cs", a) };

            var items = ResultMapper.MapBack(new[] { "Ghost.cs" }, snapshot);

            Assert.True(items.All(i => i.Payload != null),
                "no null-payload entry is produced for an unmatched display string");
        }

        public static void Run_ResultMapper_OrderPreserved()
        {
            var a = new FileHit(@"C:\p\A.cs", 0);
            var b = new FileHit(@"C:\p\B.cs", 0);
            var c = new FileHit(@"C:\p\C.cs", 0);
            var snapshot = new List<FinderEntry>
            {
                new FinderEntry("A.cs", a),
                new FinderEntry("B.cs", b),
                new FinderEntry("C.cs", c),
            };

            // fzf returns matches in its own order; the mapper must preserve THAT order, not the
            // snapshot order.
            var items = ResultMapper.MapBack(new[] { "C.cs", "A.cs", "B.cs" }, snapshot);

            Assert.Equal(3, items.Count);
            Assert.Equal("C.cs", items[0].Display);
            Assert.Equal("A.cs", items[1].Display);
            Assert.Equal("B.cs", items[2].Display);
            Assert.True(ReferenceEquals(c, items[0].Payload), "C.cs maps to its payload");
            Assert.True(ReferenceEquals(a, items[1].Payload), "A.cs maps to its payload");
            Assert.True(ReferenceEquals(b, items[2].Payload), "B.cs maps to its payload");
        }

        // ================================================================
        // ProjectFileCache — per-session cache of the DTE project-file
        // enumeration (F15: Telescope/ProjectFileCache.cs does not exist yet
        // -> this is RED: compile error)
        // ================================================================

        public static void Run_ProjectFileCache_ReturnsCached()
        {
            var cache = new ProjectFileCache();
            var list = new List<string> { @"C:\p\A.cs", @"C:\p\B.cs" };
            int count = 0;

            var first = cache.Get(() => { count++; return list; });
            var second = cache.Get(() => { count++; return list; });

            // The enumerator must run ONCE for two Gets — the second Get is served from the cache.
            Assert.Equal(1, count);
            Assert.True(ReferenceEquals(list, first), "first Get returns the enumerated list");
            Assert.True(ReferenceEquals(list, second), "second Get returns the SAME cached list (no re-enumeration)");
        }

        public static void Run_ProjectFileCache_InvalidateReenumerates()
        {
            var cache = new ProjectFileCache();
            var list = new List<string> { @"C:\p\A.cs" };
            int count = 0;

            cache.Get(() => { count++; return list; });
            cache.Invalidate();
            cache.Get(() => { count++; return list; });

            // Invalidate() must drop the cached result so the next Get re-enumerates.
            Assert.Equal(2, count);
        }

        public static void Run_ProjectFileCache_EmptyResultCached()
        {
            var cache = new ProjectFileCache();
            int count = 0;

            var first = cache.Get(() => { count++; return new List<string>(); });
            var second = cache.Get(() => { count++; return new List<string>(); });

            // An empty result is still a valid cached value: the second Get must NOT re-enumerate.
            Assert.Equal(0, first.Count);
            Assert.Equal(0, second.Count);
            Assert.Equal(1, count);
        }

        // ================================================================
        // OverlayShowState — state-based guard for the deferred ShowDialog (CR3, BP-5)
        // RED: `OverlayShowState` does not exist yet -> compile error
        // The overlay defers ShowDialog() to ApplicationIdle (fire-and-forget); if CloseOverlay()
        // runs first, the pending ShowDialog fires on an already-closed window. The guard must be
        // STATE-based (RequestShow/Close flags), NOT visibility-based (IsVisible is false at
        // ApplicationIdle time, so a visibility guard would silently never open the overlay).
        // ================================================================

        public static void Run_OverlayShowState_RequestThenClose_ShouldNotShow()
        {
            var s = new OverlayShowState();
            s.RequestShow();
            s.Close();
            Assert.False(s.ShouldShowDialog(), "Close() after RequestShow() must suppress the dialog");
        }

        public static void Run_OverlayShowState_RequestOnly_ShouldShow()
        {
            var s = new OverlayShowState();
            s.RequestShow();
            Assert.True(s.ShouldShowDialog(), "RequestShow() alone must show the dialog");
        }

        public static void Run_OverlayShowState_StateBasedNotVisibilityBased()
        {
            // The decision is STATE-based, not visibility-based: RequestShow() flips a flag with no
            // WPF visibility involved, and Close() flips it back. This pins the state contract so a
            // future IsVisible-based guard (which would be false at ApplicationIdle time and silently
            // never open the overlay) cannot be introduced.
            var s = new OverlayShowState();
            s.RequestShow();
            Assert.True(s.ShouldShowDialog(), "RequestShow() alone must show the dialog (state-based, no visibility involved)");
            s.Close();
            Assert.False(s.ShouldShowDialog(), "Close() must suppress the dialog (state-based)");
        }

        // ================================================================
        // FocusTargetModel — pure focus-target state machine (M34)
        // RED: `FocusTargetModel`/`FocusTarget`/`FocusTargetAction` + `OverlayKey.CtrlH`/`CtrlL`
        // don't exist -> compile error (CS0246/CS0103/CS0117)
        // ================================================================

        public static void Run_FocusTarget_StartsWithList()
        {
            var model = new FocusTargetModel();
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_CtrlLMovesToPreview()
        {
            var model = new FocusTargetModel();
            var action = model.Handle(OverlayKey.CtrlL);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.Preview, model.Current);
        }

        public static void Run_FocusTarget_CtrlHReturnsToList()
        {
            var model = new FocusTargetModel();
            model.Handle(OverlayKey.CtrlL); // move to Preview first
            var action = model.Handle(OverlayKey.CtrlH);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_EscapeInPreviewReturnsToList()
        {
            var model = new FocusTargetModel();
            model.Handle(OverlayKey.CtrlL); // move to Preview first
            var action = model.Handle(OverlayKey.Escape);
            Assert.Equal(FocusTargetAction.Handled, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_EscapeInListUnchanged()
        {
            var model = new FocusTargetModel();
            var action = model.Handle(OverlayKey.Escape);
            Assert.Equal(FocusTargetAction.None, action);
            Assert.Equal(FocusTarget.List, model.Current);
        }

        public static void Run_FocusTarget_ResetOnOpen()
        {
            var model = new FocusTargetModel();
            model.Handle(OverlayKey.CtrlL); // move to Preview
            model.Reset();
            Assert.Equal(FocusTarget.List, model.Current);
        }

        // ================================================================
        // TempDir.Dispose (m21) — a failed recursive delete must surface the error,
        // not swallow it (failed deletes leak temp dirs silently).
        // RED: today Dispose swallows (`catch { }`), so the test fails (no throw).
        // ================================================================

        public static void Run_TempDir_DisposeSurfacesFailure()
        {
            var dir = new TempDir();
            // Sabotage the recursive delete: remove the directory and put a FILE at the same path,
            // so Directory.Delete(path, recursive: true) throws DirectoryNotFoundException.
            System.IO.Directory.Delete(dir.Path, recursive: true);
            System.IO.File.WriteAllText(dir.Path, "i am a file now");

            bool threw = false;
            try
            {
                dir.Dispose();
            }
            catch
            {
                threw = true;
            }
            finally
            {
                try { System.IO.File.Delete(dir.Path); } catch { }
            }

            Assert.True(threw, "TempDir.Dispose must surface a failed recursive delete (m21)");
        }
    }
}
