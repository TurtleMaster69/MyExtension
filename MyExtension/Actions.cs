using CardinalNavigation;
using System;
using System.Collections.Generic;

namespace MyExtension
{
    /// <summary>
    /// The action registry: maps the built-in action names (kept in sync with
    /// default-keybindings.json) to the delegates that run them. The registry stores
    /// <c>Func&lt;InputHandler, TelescopeLauncher, Action&gt;</c> factories so it can be a static
    /// field while the delegates capture the handler/launcher instances.
    /// </summary>
    internal static class Actions
    {
        internal static readonly IReadOnlyDictionary<string, Func<InputHandler, TelescopeLauncher, Action>> Registry =
            BuildRegistry();

        private static IReadOnlyDictionary<string, Func<InputHandler, TelescopeLauncher, Action>> BuildRegistry()
        {
            var registry = new Dictionary<string, Func<InputHandler, TelescopeLauncher, Action>>(StringComparer.OrdinalIgnoreCase)
            {
                ["navigate-left"] = (h, _) => () => h.Navigate(Direction.Left),
                ["navigate-right"] = (h, _) => () => h.Navigate(Direction.Right),
                ["navigate-up"] = (h, _) => () => h.Navigate(Direction.Up),
                ["navigate-down"] = (h, _) => () => h.Navigate(Direction.Down),
                ["toggle-solution-explorer"] = (h, _) => () => h.ToggleSolutionExplorer(),
            };

            // The telescope entries are derived from TelescopeLauncher.FinderNames (the single
            // source of truth): a 6th telescope action added there is automatically a Registry
            // entry, so the hook path can never throw KeyNotFoundException on a telescope name.
            foreach (var kvp in TelescopeLauncher.FinderNames)
            {
                string actionName = kvp.Key;
                string finderName = kvp.Value;
                registry[actionName] = (_, l) => () => l.Open(finderName);
            }

            return registry;
        }

        internal static Action? Resolve(string lowerName, InputHandler handler, TelescopeLauncher launcher)
            => Registry.TryGetValue(lowerName, out var f) ? f(handler, launcher) : null;
    }
}
