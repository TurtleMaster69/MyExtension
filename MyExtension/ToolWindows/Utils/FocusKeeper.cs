using System;
using System.Windows.Threading;

namespace MyExtension.ToolWindows
{
    /// <summary>
    /// Owns the <see cref="DispatcherTimer"/> lifecycle for the Solution Explorer focus-keeper: a
    /// ~100ms timer that re-asserts tree focus/selection for ~1.5s to defeat VS's hover-preview
    /// focus steal. The per-tick decision is delegated to the pure <see cref="FocusKeeperSchedule"/>;
    /// the tick receives the elapsed milliseconds and returns false to stop the keeper early
    /// (e.g. stop-on-close when the window is no longer visible).
    /// </summary>
    internal static class FocusKeeper
    {
        // R8: the current keeper timer, cancelled when a new Run starts (stacked keepers would
        // otherwise race — g then i→Esc re-selects the wrong node).
        private static DispatcherTimer? _current;

        public static IDisposable Run(TimeSpan interval, int durationMs, Func<int, bool> tick)
        {
            _current?.Stop();
            var keeper = new DispatcherTimer(DispatcherPriority.Normal);
            keeper.Interval = interval;
            // Monotonic clock (m8): Environment.TickCount (int) wraps every ~24.9 days; net472 has
            // no TickCount64, so use a Stopwatch (high-resolution monotonic counter) instead.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            keeper.Tick += (_, _) =>
            {
                bool keepRunning = true;
                try
                {
                    keepRunning = tick((int)stopwatch.ElapsedMilliseconds);
                }
                catch
                {
                    // selection/focus re-assert must never break the handler
                }
                if (!keepRunning || stopwatch.ElapsedMilliseconds >= durationMs)
                {
                    keeper.Stop();
                    if (ReferenceEquals(_current, keeper))
                    {
                        _current = null;
                    }
                }
            };
            _current = keeper;
            keeper.Start();
            return new KeeperHandle(keeper);
        }

        private sealed class KeeperHandle : IDisposable
        {
            private DispatcherTimer? _timer;

            public KeeperHandle(DispatcherTimer timer)
            {
                _timer = timer;
            }

            public void Dispose()
            {
                if (_timer == null)
                {
                    return;
                }
                _timer.Stop();
                if (ReferenceEquals(_current, _timer))
                {
                    _current = null;
                }
                _timer = null;
            }
        }
    }

    /// <summary>
    /// Pure focus-keeper tick decision: inject Escape while the search box still has focus (bounded
    /// to 4 attempts), stop once the duration elapses (elapsed wins), otherwise re-assert the
    /// selection.
    /// </summary>
    internal static class FocusKeeperSchedule
    {
        /// <summary>Maximum Escape injections while the search box still has focus (bounds the loop).</summary>
        private const int MaxEscapeAttempts = 4;

        public enum Decision
        {
            InjectEscape,
            Reassert,
            Stop,
        }

        public static Decision Decide(bool searchBoxFocused, int elapsedMs, int escapeAttempts, int durationMs)
        {
            if (elapsedMs >= durationMs)
            {
                return Decision.Stop;
            }
            if (searchBoxFocused && escapeAttempts < MaxEscapeAttempts)
            {
                return Decision.InjectEscape;
            }
            return Decision.Reassert;
        }
    }
}
