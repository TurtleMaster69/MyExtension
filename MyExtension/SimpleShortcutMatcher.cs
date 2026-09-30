using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MyExtension
{
    /// <summary>
    /// The outcome of routing a key through <see cref="SimpleShortcutMatcher"/>: whether the key
    /// was a bound simple shortcut (execute) or should pass through, plus the matched action and
    /// the canonical shortcut string (e.g. <c>Ctrl+H</c>) when one executed.
    /// </summary>
    internal enum SimpleShortcutResultKind
    {
        PassThrough,
        Execute,
        Failed,
    }

    /// <summary>
    /// The result of a <see cref="SimpleShortcutMatcher.HandleKey"/> call: how the key should be
    /// treated, plus the matched action and sequence when a binding executed (or the handler's
    /// message when it threw).
    /// </summary>
    internal readonly struct SimpleShortcutResult
    {
        public SimpleShortcutResultKind Kind { get; }
        public Action? Action { get; }
        public string? Sequence { get; }
        public string? ErrorMessage { get; }

        private SimpleShortcutResult(SimpleShortcutResultKind kind, Action? action, string? sequence, string? errorMessage)
        {
            Kind = kind;
            Action = action;
            Sequence = sequence;
            ErrorMessage = errorMessage;
        }

        public static SimpleShortcutResult PassThrough => new(SimpleShortcutResultKind.PassThrough, null, null, null);
        public static SimpleShortcutResult Execute(Action action, string sequence) => new(SimpleShortcutResultKind.Execute, action, sequence, null);
        public static SimpleShortcutResult Failed(string sequence, string errorMessage) => new(SimpleShortcutResultKind.Failed, null, sequence, errorMessage);
    }

    /// <summary>
    /// The pure simple-shortcut matcher: a key plus Ctrl/Shift/Alt is built into its canonical
    /// shortcut string (e.g. <c>Ctrl+H</c>) and looked up in the bindings dictionary. Extracted
    /// from <see cref="InputHandler"/> so the shortcut routing is unit-testable without
    /// AsyncPackage/MEF/WindowManager. Dependency-free — it never touches VS state.
    /// </summary>
    internal sealed class SimpleShortcutMatcher
    {
        private readonly IReadOnlyDictionary<string, Action> _bindings;

        public SimpleShortcutMatcher(IReadOnlyDictionary<string, Action> bindings)
        {
            _bindings = bindings;
        }

        public SimpleShortcutResult HandleKey(Keys key, bool ctrl, bool shift, bool alt)
        {
            string name = BuildSimpleKey(key, ctrl, shift, alt);
            if (_bindings.TryGetValue(name, out var action))
            {
                try
                {
                    action();
                    return SimpleShortcutResult.Execute(action, name);
                }
                catch (Exception ex)
                {
                    return SimpleShortcutResult.Failed(name, ex.Message);
                }
            }
            return SimpleShortcutResult.PassThrough;
        }

        private static string BuildSimpleKey(Keys key, bool ctrl, bool shift, bool alt)
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (alt) parts.Add("Alt");
            parts.Add(KeyNames.ToString(key));   // shared with LeaderSequenceMatcher (M21/Phase 8) — do NOT copy
            return string.Join("+", parts);
        }
    }
}
