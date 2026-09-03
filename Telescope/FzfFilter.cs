using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Telescope
{
    /// <summary>
    /// Runs the <c>fzf</c> CLI in non-interactive <c>--filter</c> mode to rank/filter a set of
    /// candidate lines against a query string. This mirrors how several IDEs back a
    /// Telescope-style fuzzy finder with a real matcher without reimplementing scoring.
    ///
    /// <para/>
    /// <b>Why <c>fzf --filter</c> (not plain <c>fzf</c>):</b> interactive <c>fzf</c> renders a
    /// full-screen TUI and blocks until the user picks, which is useless for a live, in-VS overlay.
    /// <c>fzf --filter &lt;query&gt;</c> instead reads the candidate list on stdin, prints the
    /// matches (ranked) on stdout, and exits — a one-shot, non-TUI call we can run on every
    /// keystroke.
    ///
    /// <para/>
    /// <b>Cost:</b> because filter mode is one-shot, we spawn a short-lived process per query.
    /// That is acceptable for a first milestone; if latency matters later, switch to fzf's
    /// <c>--listen</c> mode (a persistent HTTP server) or an in-process matcher.
    ///
    /// <para/>
    /// <b>Threading:</b> this class performs no VS API calls — it only talks to the fzf
    /// subprocess — so it can be invoked from a background task. Callers marshal the result back
    /// to the WPF dispatcher themselves.
    /// </summary>
    internal sealed class FzfFilter
    {
        private readonly string _fzfPath;

        /// <summary>
        /// Creates a filter that resolves <c>fzf</c> from the system PATH. If <paramref name="fzfPath"/>
        /// is non-empty it is used verbatim instead (e.g. from config).
        /// </summary>
        public FzfFilter(string? fzfPath = null)
        {
            _fzfPath = string.IsNullOrWhiteSpace(fzfPath) ? "fzf" : fzfPath!;
        }

        /// <summary>
        /// Returns true when the configured <c>fzf</c> executable can be found and runs. Used to
        /// degrade gracefully (show the unfiltered list + a warning) when fzf is missing.
        /// </summary>
        public bool IsAvailable()
        {
            try
            {
                using var p = new Process();
                p.StartInfo.FileName = _fzfPath;
                p.StartInfo.Arguments = "--version";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Filters <paramref name="candidates"/> against <paramref name="query"/> using fzf and
        /// returns the matching lines in fzf's rank order. An empty query returns all candidates
        /// in their original order. Cancellation aborts the running process.
        /// </summary>
        public async Task<IReadOnlyList<string>> FilterAsync(
            IEnumerable<string> candidates,
            string query,
            CancellationToken cancellationToken)
        {
            var lines = candidates as IReadOnlyList<string> ?? candidates.ToList();

            if (string.IsNullOrWhiteSpace(query))
            {
                return lines;
            }

            try
            {
                using var p = new Process();
                p.StartInfo.FileName = _fzfPath;
                p.StartInfo.Arguments = $"--filter {QuoteArg(query)} --no-sort";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                p.StartInfo.StandardErrorEncoding = Encoding.UTF8;

                var started = p.Start();
                if (!started)
                {
                    return lines;
                }

                // Feed candidates on stdin, then close it so fzf knows the input is complete.
                p.StandardInput.NewLine = "\n";
                foreach (var line in lines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    p.StandardInput.WriteLine(line);
                }
                p.StandardInput.Close();

                var outputTask = p.StandardOutput.ReadToEndAsync();
                var errorTask = p.StandardError.ReadToEndAsync();
                using (cancellationToken.Register(() => TryKill(p)))
                {
                    await Task.WhenAll(outputTask, errorTask);
                }

                var output = await outputTask;
                return output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // fzf missing/crashed: fall back to the full candidate list.
                return lines;
            }
        }

        private static void TryKill(Process p)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                }
            }
            catch
            {
                // already gone
            }
        }

        private static string QuoteArg(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
