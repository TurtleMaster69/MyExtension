using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyExtension.Package
{
    /// <summary>
    /// Dependency-free named-step orchestrator for package initialization. Each step runs in its
    /// own try/catch and logs a per-step diagnostic (<c>[MyExtension] init &lt;step&gt; ok</c> /
    /// <c>[MyExtension] init &lt;step&gt; failed: {ex.Message}</c>); a failing step never aborts
    /// the later steps. No VS/WPF dependency so it is unit-testable.
    /// </summary>
    internal sealed class InitSteps
    {
        private readonly Action<string> _log;

        public InitSteps(Action<string> log)
        {
            _log = log;
        }

        public async Task RunAsync(IReadOnlyList<(string Name, Func<Task> Step)> steps)
        {
            foreach (var (name, step) in steps)
            {
                try
                {
                    await step();
                    _log($"{Telescope.Logging.DiagnosticLog.MyExtension}init {name} ok");
                }
                catch (Exception ex)
                {
                    _log($"{Telescope.Logging.DiagnosticLog.MyExtension}init {name} failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Runs a single synchronous step with the same per-step try/catch +
        /// <c>[MyExtension] init &lt;name&gt; ok</c> / <c>[MyExtension] init &lt;name&gt; failed:
        /// {ex.Message}</c> contract as <see cref="RunAsync"/>. Never throws.
        /// </summary>
        public static void RunSync(string name, Action step, Action<string> log)
        {
            try
            {
                step();
                log($"{Telescope.Logging.DiagnosticLog.MyExtension}init {name} ok");
            }
            catch (Exception ex)
            {
                log($"{Telescope.Logging.DiagnosticLog.MyExtension}init {name} failed: {ex.Message}");
            }
        }
    }
}
