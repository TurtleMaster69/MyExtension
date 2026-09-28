using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        public static int Run(Type testsType, string[] args)
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
