using System;
using System.Windows.Threading;

namespace MyExtension
{
    /// <summary>
    /// Owns the <see cref="DispatcherTimer"/> lifecycle for the Solution Explorer focus-keeper: a
    /// ~100ms timer that re-asserts tree focus/selection for ~1.5s to defeat VS's hover-preview
    /// focus steal. The per-tick decision is delegated to the pure <see cref="FocusKeeperSchedule"/>;
    /// the tick receives the elapsed milliseconds.
    /// </summary>
    internal static class FocusKeeper
    {
        public static void Run(TimeSpan interval, int durationMs, Action<int> tick)
        {
            var keeper = new DispatcherTimer(DispatcherPriority.Normal);
            keeper.Interval = interval;
            DispatcherTimer keeperRef = keeper;
            int startTick = Environment.TickCount;
            int stopAt = startTick + durationMs;
            keeper.Tick += (_, _) =>
            {
                try
                {
                    tick(Environment.TickCount - startTick);
                }
                catch
                {
                    // selection/focus re-assert must never break the handler
                }
                if (Environment.TickCount >= stopAt)
                {
                    keeperRef.Stop();
                }
            };
            keeper.Start();
        }
    }

    /// <summary>
    /// Pure focus-keeper tick decision: inject Escape while the search box still has focus (bounded
    /// to 4 attempts), stop once the duration elapses (elapsed wins), otherwise re-assert the
    /// selection.
    /// </summary>
    internal static class FocusKeeperSchedule
    {
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
            if (searchBoxFocused && escapeAttempts < 4)
            {
                return Decision.InjectEscape;
            }
            return Decision.Reassert;
        }
    }
}
