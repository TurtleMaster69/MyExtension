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
    ///
    /// <para/>
    /// C2: per-controller (an instance, not a shared static) — each controller owns its own
    /// <c>_current</c>, so one controller's <see cref="Run"/> can never stop another's keeper.
    /// </summary>
    internal sealed class FocusKeeper
    {
        // R8: the current keeper timer, cancelled when a new Run starts (stacked keepers would
        // otherwise race — g then i→Esc re-selects the wrong node).
        private DispatcherTimer? _current;

        public IDisposable Run(TimeSpan interval, int durationMs, Func<int, bool> tick)
        {
            _current?.Stop();
            var keeper = new DispatcherTimer(DispatcherPriority.Normal);
            keeper.Interval = interval;
            // Monotonic clock (m8): Environment.TickCount (int) wraps every ~24.9 days; net472 has
            // no TickCount64, so use a Stopwatch (high-resolution monotonic counter) instead.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            keeper.Tick += (_, _) => InvokeTick(keeper, durationMs, stopwatch, tick);
            _current = keeper;
            keeper.Start();
            return new KeeperHandle(this, keeper);
        }

        /// <summary>
        /// m64 (BP-D17): the shared tick body — the timer's Tick handler and the test seam both
        /// call it. A superseded keeper's queued tick is a no-op (the ReferenceEquals guard): the
        /// tick handler must not call <c>tick(...)</c> for a keeper that was already stopped by a
        /// newer Run (the race: a tick queued in the dispatcher before <c>_current?.Stop()</c> at
        /// the top of <see cref="Run"/>).
        /// </summary>
        internal void InvokeTick(DispatcherTimer keeper, int durationMs, System.Diagnostics.Stopwatch stopwatch, Func<int, bool> tick)
        {
            if (!ReferenceEquals(_current, keeper))
            {
                return;
            }
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
        }

        private sealed class KeeperHandle : IDisposable
        {
            private readonly FocusKeeper _owner;
            private DispatcherTimer? _timer;

            public KeeperHandle(FocusKeeper owner, DispatcherTimer timer)
            {
                _owner = owner;
                _timer = timer;
            }

            public void Dispose()
            {
                if (_timer == null)
                {
                    return;
                }
                _timer.Stop();
                if (ReferenceEquals(_owner._current, _timer))
                {
                    _owner._current = null;
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

        public static Decision Decide(bool searchBoxFocused, int elapsedMs, int escapeAttempts, int durationMs, bool editorFocused)
        {
            // m16 (BP-15): the user moved to the editor — stop re-asserting (keys typed in that
            // window must not be routed to the tree and lost). Editor focus wins over the escape loop.
            if (editorFocused)
            {
                return Decision.Stop;
            }
            if (elapsedMs >= durationMs)
            {
                return Decision.Stop;
            }
            if (searchBoxFocused && escapeAttempts < MaxEscapeAttempts)
            {
                return Decision.InjectEscape;
            }
            // N26: after MaxEscapeAttempts with the box still focused, stop — do not fight the user.
            if (searchBoxFocused)
            {
                return Decision.Stop;
            }
            return Decision.Reassert;
        }
    }
}
