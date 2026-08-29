using CardinalNavigation;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MyExtension
{
    internal class InputHandler
    {
        private readonly AsyncPackage _package;

        // Current sequence being typed (after leader)
        private readonly List<Keys> _currentSequence = new();
        private bool _leaderActive = false;

        // Timeout for sequences (optional but recommended)
        private DateTime _lastKeyTime;
        private readonly TimeSpan _sequenceTimeout = TimeSpan.FromMilliseconds(1000);

        // Leader key
        private readonly Keys LeaderKey = Keys.Space;

        // All bindings: sequence → action
        // Example sequences:
        //   Ctrl+H
        //   Space, W, H
        private readonly Dictionary<string, Action> _bindings;

        public InputHandler(AsyncPackage package)
        {
            _package = package;

            _bindings = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                // === Simple shortcuts ===
                ["Ctrl+H"] = () => Navigate(CardinalNavigationConstants.LEFT),
                ["Ctrl+L"] = () => Navigate(CardinalNavigationConstants.RIGHT),
                ["Ctrl+K"] = () => Navigate(CardinalNavigationConstants.UP),
                ["Ctrl+J"] = () => Navigate(CardinalNavigationConstants.DOWN),

                // === Leader sequences ===
                ["W,H"] = () => Navigate(CardinalNavigationConstants.LEFT),
                ["W,L"] = () => Navigate(CardinalNavigationConstants.RIGHT),
                ["W,K"] = () => Navigate(CardinalNavigationConstants.UP),
                ["W,J"] = () => Navigate(CardinalNavigationConstants.DOWN),

                // More examples
                ["Q"] = () => { /* close window or something */ },
                ["F,F"] = () => { /* another action */ },
            };
        }

        /// <summary>
        /// Returns true if the key was handled (should be blocked by the hook)
        /// </summary>
        public bool HandleKey(Keys key, bool ctrl, bool shift, bool alt)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Reset sequence if timeout expired
            if (key == Keys.Escape)
            {
                ResetSequence();
                return false;
            }

            _lastKeyTime = DateTime.Now;

            // 1. Leader key pressed
            if (key == LeaderKey && !ctrl && !shift && !alt)
            {
                _leaderActive = true;
                _currentSequence.Clear();
                return true; // consume leader
            }

            // 2. Building a sequence after leader
            if (_leaderActive)
            {
                _currentSequence.Add(key);

                string sequence = string.Join(",", _currentSequence.Select(k => k.ToString()));

                // Exact match → execute
                if (_bindings.TryGetValue(sequence, out var action))
                {
                    action();
                    ResetSequence();
                    return true;
                }

                // Check if this sequence is a prefix of any binding
                bool isPrefix = _bindings.Keys.Any(k => k.StartsWith(sequence + ",", StringComparison.OrdinalIgnoreCase));

                if (!isPrefix)
                {
                    // Invalid sequence
                    ResetSequence();
                    return false;
                }

                // Still waiting for more keys
                return true;
            }

            // 3. Normal (non-leader) shortcuts
            string simple = BuildSimpleKey(key, ctrl, shift, alt);

            if (_bindings.TryGetValue(simple, out var simpleAction))
            {
                simpleAction();
                return true;
            }

            return false;
        }

        private void ResetSequence()
        {
            _leaderActive = false;
            _currentSequence.Clear();
        }

        private string BuildSimpleKey(Keys key, bool ctrl, bool shift, bool alt)
        {
            var parts = new List<string>();

            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (alt) parts.Add("Alt");
            parts.Add(key.ToString());

            return string.Join("+", parts);
        }

        private void Navigate(char direction)
        {
            var wm = new WindowMatrix(_package);
            wm.NavigateInDirection(direction);
        }
    }
}