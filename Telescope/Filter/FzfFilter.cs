using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telescope.Logging;

namespace Telescope.Filter
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
        private const int DefaultFilterTimeoutMs = 3000;

        // m16: a hung `fzf --version` must not block the UI up to 3s — the availability probe
        // waits only this long before reporting unavailable.
        private const int IsAvailableTimeoutMs = 500;

        private readonly string _fzfPath;

        /// <summary>
        /// Timeout for a single fzf <c>--filter</c> run; a hung subprocess is killed and the filter
        /// falls back to the full candidate list. Settable so the timeout test can shrink it.
        /// </summary>
        internal int FilterTimeoutMs { get; set; } = DefaultFilterTimeoutMs;

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
                // m16: bound the wait so a hung fzf --version cannot block the UI up to 3s.
                if (!p.WaitForExit(IsAvailableTimeoutMs))
                {
                    TryKill(p);
                    return false;
                }
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

                // M3: spawn + write the candidate list off the UI thread (the write must not block
                // the UI thread on a large candidate list).
                bool started = await Task.Run(() =>
                {
                    bool s = p.Start();
                    if (s)
                    {
                        // Feed candidates on stdin, then close it so fzf knows the input is
                        // complete. The bytes are written explicitly as UTF-8: the StreamWriter's
                        // default ANSI encoding would mangle non-ASCII display text (e.g. the
                        // em-dash in code-issue rows), which would break the display-keyed lookup
                        // back to the original entry downstream.
                        var inputBytes = Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
                        p.StandardInput.BaseStream.Write(inputBytes, 0, inputBytes.Length);
                        p.StandardInput.BaseStream.Flush();
                        p.StandardInput.Close();
                    }
                    return s;
                });
                if (!started)
                {
                    return lines;
                }

                var outputTask = p.StandardOutput.ReadToEndAsync();
                var errorTask = p.StandardError.ReadToEndAsync();
                using (cancellationToken.Register(() => TryKill(p)))
                {
                    var all = Task.WhenAll(outputTask, errorTask);
                    var timeout = Task.Delay(FilterTimeoutMs, cancellationToken);
                    var winner = await Task.WhenAny(all, timeout);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return lines; // silent — overlay discards
                    }
                    if (winner == timeout)
                    {
                        TryKill(p);
                        TelescopeLog.Log($"fzf filter failed: timeout after {FilterTimeoutMs}ms");
                        // M3: observe the faulted ReadToEndAsync tasks WITHOUT blocking the return.
                        // A killed process's pipe reads do not fault promptly (a child holding the
                        // pipe open keeps them pending), so `await all` here would block for the
                        // child's lifetime. Attach a fault-only continuation that reads the
                        // exception — the faulted tasks are observed (no unobserved-task noise) and
                        // the filter returns immediately.
                        _ = all.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        return lines;
                    }
                    await all;
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
            catch (Exception ex)
            {
                // fzf missing/crashed: fall back to the full candidate list.
                TelescopeLog.Log($"fzf filter failed: {ex.Message}");
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

        internal static string QuoteArg(string value)
        {
            string escaped = value.Replace("\"", "\\\"");
            int trailing = escaped.Length - escaped.TrimEnd('\\').Length;
            return "\"" + escaped + new string('\\', trailing) + "\"";
        }
    }
}
