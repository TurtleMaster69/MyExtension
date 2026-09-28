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
            new Dictionary<string, Func<InputHandler, TelescopeLauncher, Action>>(StringComparer.OrdinalIgnoreCase)
            {
                ["navigate-left"] = (h, _) => () => h.Navigate(CardinalNavigationConstants.LEFT),
                ["navigate-right"] = (h, _) => () => h.Navigate(CardinalNavigationConstants.RIGHT),
                ["navigate-up"] = (h, _) => () => h.Navigate(CardinalNavigationConstants.UP),
                ["navigate-down"] = (h, _) => () => h.Navigate(CardinalNavigationConstants.DOWN),
                ["telescope"] = (_, l) => () => l.Open(TelescopeLauncher.FinderNames["telescope"]),
                ["telescope-issues"] = (_, l) => () => l.Open(TelescopeLauncher.FinderNames["telescope-issues"]),
                ["telescope-references"] = (_, l) => () => l.Open(TelescopeLauncher.FinderNames["telescope-references"]),
                ["telescope-implementation"] = (_, l) => () => l.Open(TelescopeLauncher.FinderNames["telescope-implementation"]),
                ["telescope-grep"] = (_, l) => () => l.Open(TelescopeLauncher.FinderNames["telescope-grep"]),
                ["toggle-solution-explorer"] = (h, _) => () => h.ToggleSolutionExplorer(),
            };

        internal static Action? Resolve(string lowerName, InputHandler handler, TelescopeLauncher launcher)
            => Registry.TryGetValue(lowerName, out var f) ? f(handler, launcher) : null;
    }
}
