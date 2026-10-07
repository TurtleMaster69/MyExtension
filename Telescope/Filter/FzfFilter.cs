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
    /// <b>Keep-subprocess decision (D8/BP-7):</b> the per-keystroke spawn is deliberately kept —
    /// it already runs off the UI thread (the spawn + stdin write happen inside
    /// <see cref="Task.Run"/>) and is e2e-GREEN. <c>--listen</c>/in-process is deferred until
    /// measurement proves a bottleneck (perf-investigation), not adopted speculatively.
    ///
    /// <para/>
    /// <b>Threading:</b> this class performs no VS API calls — it only talks to the fzf
    /// subprocess — so it can be invoked from a background task. Callers marshal the result back
    /// to the WPF dispatcher themselves.
    /// </summary>
    internal sealed class FzfFilter : IFzfEngine
    {
        private const int DefaultFilterTimeoutMs = 3000;

        // m11 (BP-4): a filter that completes a few ms AFTER the timeout fires is not genuinely
        // hung — the timeout-vs-completion race. Before killing, wait this long for `all` to
        // complete; if it does, fall through to the boundary fast path (the filtered output, no
        // spurious `fzf filter failed: timeout` line).
        private const int FilterTimeoutGraceMs = 250;

        // m16: a hung `fzf --version` must not block the UI up to 3s — the availability probe
        // waits only this long before reporting unavailable.
        private const int IsAvailableTimeoutMs = 500;

        private readonly string _fzfPath;

        // m50: the availability probe (a bounded `fzf --version` subprocess) is cached once per
        // session so it does not run on every overlay open. D11/BP-6: `volatile` is illegal on
        // `bool?`, so the probe-once state is a `volatile bool _probed` + a plain `bool _value`
        // (the volatile write of `_probed` publishes the preceding `_value` write).
        private volatile bool _probed;
        private bool _value;

        // n7 (BP-19): the probe interlock — two CONCURRENT IsAvailableAsync callers must run the
        // bounded probe ONCE. The first caller acquires the gate and probes; concurrent callers
        // wait on the gate, then re-check `_probed` (double-checked) and return the cached value.
        private readonly SemaphoreSlim _probeGate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Timeout for a single fzf <c>--filter</c> run; a hung subprocess is killed and the filter
        /// falls back to the full candidate list. Settable so the timeout test can shrink it.
        /// </summary>
        internal int FilterTimeoutMs { get; set; } = DefaultFilterTimeoutMs;

        /// <summary>
        /// M7 (BP-60): number of pipe-read tasks the timeout path has arranged to be observed
        /// (fault-only continuation). The timeout test asserts on this instead of GC-polling for
        /// <c>UnobservedTaskException</c> — a deterministic seam, no wall-clock + GC-poll.
        /// </summary>
        internal int AwaitedReadCount { get; private set; }

        /// <summary>
        /// D15/BP-8: number of dedicated timeout timers still pending. The fast path cancels the
        /// dedicated timeout CTS, so this is 0 after a normal filter (no per-keystroke pending
        /// timer survives).
        /// </summary>
        internal int PendingTimeoutCount { get; private set; }

        /// <summary>
        /// BP-D18 (M10): injectable delay factory routing BOTH <c>Task.Delay</c> calls (the
        /// timeout + the grace) so the timeout-vs-completion boundary is deterministic in tests
        /// (no wall-clock <c>ping</c> race). Null in production.
        /// </summary>
        internal Func<int, CancellationToken, Task>? DelayFactory;

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
        /// N38/BP-52: the bounded <c>fzf --version</c> probe runs inside <see cref="Task.Run"/> so
        /// the UI thread is not blocked; the result is cached once per session.
        /// </summary>
        public async Task<bool> IsAvailableAsync()
        {
            if (_probed)
            {
                return _value;
            }
            // n7 (BP-19): interlock the probe — concurrent callers wait on the gate, then re-check
            // `_probed` (double-checked) so the bounded probe runs exactly once.
            await _probeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_probed)
                {
                    return _value;
                }
                bool result = await Task.Run(ProbeIsAvailable).ConfigureAwait(false);
                _value = result;
                _probed = true;
                return _value;
            }
            finally
            {
                _probeGate.Release();
            }
        }

        private bool ProbeIsAvailable()
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
        public async Task<IReadOnlyList<string>?> FilterAsync(
            IEnumerable<string> candidates,
            string query,
            CancellationToken cancellationToken)
        {
            var lines = candidates as IReadOnlyList<string> ?? candidates.ToList();

            if (string.IsNullOrWhiteSpace(query))
            {
                return lines;
            }

            // N39/BP-53: when the cached availability is false, return the unfiltered list WITHOUT
            // spawning fzf (today every keystroke attempted p.Start() -> Win32Exception + a
            // `fzf filter failed` log when fzf is missing).
            if (_probed && !_value)
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

                // D3/BP-5: register the kill callback BEFORE the spawn/write so cancellation can
                // kill fzf during the blocking stdin write (a hung child that never reads stdin
                // blocks the write once the 64KB pipe buffer fills). The `using var p` scope covers
                // the registration.
                using (cancellationToken.Register(() => TryKill(p)))
                {
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
                    var all = Task.WhenAll(outputTask, errorTask);

                    // D15/BP-8: a dedicated timeout CTS (Task.Delay returns a Task, NOT IDisposable —
                    // "dispose" is wrong). The fast path cancels it so no per-keystroke pending
                    // timer survives a normal filter.
                    using var timeoutCts = new CancellationTokenSource();
                    PendingTimeoutCount++;
                    var timeout = DelayFactory?.Invoke(FilterTimeoutMs, timeoutCts.Token) ?? Task.Delay(FilterTimeoutMs, timeoutCts.Token);
                    var winner = await Task.WhenAny(all, timeout);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        timeoutCts.Cancel();
                        PendingTimeoutCount--;
                        // R23: observe the pending ReadToEndAsync tasks on cancellation (mirror the
                        // timeout path's fault-only continuation) so overlay close mid-filter produces
                        // no UnobservedTaskException noise. The killed process's pipe reads may stay
                        // pending, so do NOT await them — attach a fault-only continuation instead.
                        _ = all.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        AwaitedReadCount += 2;
                        return lines; // silent — overlay discards
                    }
                    if (winner == timeout)
                    {
                        // m11 (BP-4): the timeout and the completion can race — if the filter
                        // actually completed at the boundary, fall through to the fast path (the
                        // filtered output, NO spurious `fzf filter failed: timeout` line).
                        if (all.IsCompleted)
                        {
                            timeoutCts.Cancel();
                            PendingTimeoutCount--;
                            await all;
                            var boundaryOutput = await outputTask;
                            return boundaryOutput
                                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .ToList();
                        }
                        // m11 (BP-4): a filter completing a few ms after the timeout fires is NOT
                        // genuinely hung — give it a short grace period before killing. If it
                        // completes within the grace, fall through to the SAME boundary fast path.
                        // m9 (BP-9): the grace delay carries the caller's cancellation token so a
                        // cancelled gather returns promptly instead of waiting out the grace.
                        var graceWinner = await Task.WhenAny(all, DelayFactory?.Invoke(FilterTimeoutGraceMs, cancellationToken) ?? Task.Delay(FilterTimeoutGraceMs, cancellationToken));
                        if (cancellationToken.IsCancellationRequested)
                        {
                            // m8 (BP-9): a cancelled gather must NOT log the spurious timeout line —
                            // mirror the existing cancellation path (silent return, counters observed).
                            timeoutCts.Cancel();
                            PendingTimeoutCount--;
                            _ = all.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                            AwaitedReadCount += 2;
                            return lines;
                        }
                        if (graceWinner == all)
                        {
                            timeoutCts.Cancel();
                            PendingTimeoutCount--;
                            await all;
                            var boundaryOutput = await outputTask;
                            return boundaryOutput
                                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .ToList();
                        }
                        PendingTimeoutCount--;
                        TryKill(p);
                        TelescopeLog.Log($"fzf filter failed: timeout after {FilterTimeoutMs}ms");
                        // M3: observe the faulted ReadToEndAsync tasks WITHOUT blocking the return.
                        // A killed process's pipe reads do not fault promptly (a child holding the
                        // pipe open keeps them pending), so `await all` here would block for the
                        // child's lifetime. Attach a fault-only continuation that reads the
                        // exception — the faulted tasks are observed (no unobserved-task noise) and
                        // the filter returns immediately.
                        _ = all.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        // M7 (BP-60): record that both pipe-read tasks were arranged to be observed
                        // (the deterministic seam the timeout test asserts on).
                        AwaitedReadCount += 2;
                        // M3 (BP-1): a genuine timeout signals failure distinctly — return null (never
                        // the full candidate list). FzfFinder treats null as the literal fallback.
                        return null;
                    }
                    // Fast path: all completed first — cancel the dedicated timeout timer.
                    timeoutCts.Cancel();
                    PendingTimeoutCount--;
                    await all;

                    var output = await outputTask;
                    return output
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // fzf missing/crashed: M3 (BP-1) — signal failure distinctly (return null, never the
                // full candidate list). FzfFinder treats null as the literal fallback.
                TelescopeLog.Log($"fzf filter failed: {ex.Message}");
                return null;
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

        /// <summary>
        /// Quotes a single argument for the Windows command line (N45/BP-59). Backslashes are
        /// literal unless they precede a quote: a backslash run immediately before a quote is
        /// doubled, then the quote is escaped with one more backslash. Trailing backslashes are
        /// doubled before the closing quote. net472-compatible (no
        /// <c>ProcessStartInfo.ArgumentList</c>).
        /// </summary>
        internal static string QuoteArg(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(c);
                    backslashes = 0;
                }
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
