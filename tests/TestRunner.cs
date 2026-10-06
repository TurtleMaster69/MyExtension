using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace TestHarness
{
    /// <summary>
    /// Minimal, dependency-free test runner shared by the offline unit-test projects. Each public
    /// static method named <c>Run_*</c> on the project's <c>Tests</c> type is discovered and
    /// executed; any thrown exception fails that test. Exit code is the number of failures.
    ///
    /// <para/>
    /// <b>Why not xUnit/MSTest:</b> those frameworks are not in the local NuGet cache and pulling
    /// them needs network. This runner is hermetic and guaranteed to build/run offline, which
    /// suits a solo-iteration harness. Tests are exercised via <c>dotnet run --project tests/&lt;Project&gt;</c>.
    ///
    /// <para/>
    /// <b>Running a subset:</b> pass a substring filter as the first argument; only tests whose
    /// name contains it run (e.g. <c>dotnet run --project tests/Telescope.Tests -- KeyHandler</c>
    /// runs just the overlay key-handler tests). Pass <c>--list</c> to print the available tests.
    /// </summary>
    internal static class TestRunner
    {
        public static int Run(Type testsType, string[] args, int perTestTimeoutSeconds = 60)
        {
            var methods = testsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
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
            // n9 (BP-31): a per-test timeout so a deadlocking test is reported as FAILED
            // ("timed out after Ns") and the runner CONTINUES — it must never hang the whole
            // suite. A timed-out test leaves an abandoned thread that may mutate static state
            // (e.g. ThreadHelper.uiThreadDispatcher); that risk is documented and accepted
            // (killing the process would abort the whole suite). Each test runs on its own STA
            // thread (the test host is [STAThread] and WPF construction — e.g. a FrameworkElement
            // in the PaneHost fakes — requires STA; a Task.Run thread-pool thread is MTA and
            // throws). The timeout is a parameter (default 60s) so a test that itself runs a
            // nested runner (Run_TestRunner_Timeout) can pass a SHORTER nested timeout — the
            // nested run's duration must be strictly less than the outer test's own budget or the
            // outer runner would kill the observing test at the same moment the nested timeout
            // fires.
            foreach (var method in methods)
            {
                Exception? testError = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        object? result = method.Invoke(null, null);
                        // m62 (BP-62): await Task-returning methods instead of discarding the
                        // return value — a future async test's failure must not be silently
                        // swallowed.
                        if (result is Task task)
                        {
                            task.GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        testError = ex;
                    }
                });
                thread.IsBackground = true;
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                if (!thread.Join(TimeSpan.FromSeconds(perTestTimeoutSeconds)))
                {
                    Console.WriteLine($"FAIL  {method.Name}: timed out after {perTestTimeoutSeconds}s");
                    failed++;
                    continue;
                }
                if (testError != null)
                {
                    Console.WriteLine($"FAIL  {method.Name}: {Unwrap(testError).Message}");
                    failed++;
                    continue;
                }
                Console.WriteLine($"PASS  {method.Name}");
                passed++;
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

    internal static class Assert
    {
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception($"Expected [{expected}] but got [{actual}]");
            }
        }

        // n8 (BP-30): the inverse of Equal — throws when the values are equal, passes when they
        // differ. Replaces the Assert.False(a == b, ...) workaround for "must NOT be X" pins.
        public static void NotEqual<T>(T notExpected, T actual)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            {
                throw new Exception($"Expected a value different from [{notExpected}] but got [{actual}]");
            }
        }

        public static void True(bool condition)
        {
            if (!condition)
            {
                throw new Exception("Assert.True failed");
            }
        }

        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        public static void False(bool condition)
        {
            if (condition)
            {
                throw new Exception("Assert.False failed");
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

    /// <summary>
    /// A disposable unique temp directory (created on construction, deleted recursively on
    /// dispose). Replaces the hand-rolled <c>Path.Combine(Path.GetTempPath(), ...)</c> +
    /// <c>Directory.Delete(dir, recursive: true)</c> blocks in the test programs.
    /// </summary>
    public sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "neovisual_tests_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            // m21/BP-18: surface a failed recursive delete instead of swallowing it — a silent
            // failure leaks temp dirs and hides the error from the test runner.
            System.IO.Directory.Delete(Path, recursive: true);
        }
    }

    /// <summary>
    /// Shared test scaffolding: LogPath/DebugLogPath save-set-restore helpers (M29). These are
    /// deliberately NOT <c>Run_</c>-prefixed so the runner never discovers them as tests.
    /// </summary>
    public static class TestScaffold
    {
        /// <summary>
        /// Runs <paramref name="body"/> with <c>Telescope.Logging.LogFileWriter.LogPath</c> temporarily set to
        /// <paramref name="logPath"/>, restoring the previous value in a finally block.
        /// </summary>
        public static void WithLogPath(string logPath, Action body)
        {
            string original = Telescope.Logging.LogFileWriter.LogPath;
            try
            {
                Telescope.Logging.LogFileWriter.LogPath = logPath;
                body();
            }
            finally
            {
                Telescope.Logging.LogFileWriter.LogPath = original;
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> with <c>Telescope.Logging.LogFileWriter.DebugLogPath</c> temporarily set
        /// to <paramref name="debugPath"/>, restoring the previous value in a finally block.
        /// </summary>
        public static void WithDebugLogPath(string debugPath, Action body)
        {
            string original = Telescope.Logging.LogFileWriter.DebugLogPath;
            try
            {
                Telescope.Logging.LogFileWriter.DebugLogPath = debugPath;
                body();
            }
            finally
            {
                Telescope.Logging.LogFileWriter.DebugLogPath = original;
            }
        }
    }
}
