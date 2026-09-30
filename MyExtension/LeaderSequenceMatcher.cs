using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MyExtension
{
    /// <summary>
    /// The pure leader-key state machine: leader-key start, sequence building, binding match,
    /// prefix detection, and abort. Extracted from <see cref="InputHandler"/> so the leader
    /// routing is unit-testable without AsyncPackage/MEF/WindowManager. Dependency-free — it
    /// never touches VS state; the caller passes the typing flag in.
    /// </summary>
    internal sealed class LeaderSequenceMatcher
    {
        private readonly Keys _leaderKey;
        private readonly IReadOnlyDictionary<string, Action> _bindings;
        private bool _active;
        private readonly List<Keys> _sequence = new List<Keys>();

        public LeaderSequenceMatcher(Keys leaderKey, IReadOnlyDictionary<string, Action> bindings)
        {
            _leaderKey = leaderKey;
            _bindings = bindings;
        }

        /// <summary>Whether a leader sequence is currently in progress.</summary>
        public bool IsActive => _active;

        /// <summary>
        /// Routes a single key through the leader state machine. Returns how the caller should
        /// treat the key: pass through, consume (swallow), execute a matched binding, or abort
        /// (the sequence matched nothing and is cleared).
        /// </summary>
        public LeaderResult HandleKey(Keys key, bool ctrl, bool shift, bool alt, bool isTyping)
        {
            // 1. Leader key pressed: begin a sequence, unless the user is typing — then the
            //    leader key types a literal character.
            if (key == _leaderKey && !ctrl && !shift && !alt)
            {
                if (isTyping)
                {
                    return LeaderResult.PassThrough;
                }

                _active = true;
                _sequence.Clear();
                return LeaderResult.Consume;
            }

            // 2. Building a leader sequence: append the key and match against the bindings,
            //    using prefix detection to keep waiting for multi-key sequences like "f f".
            if (_active)
            {
                _sequence.Add(key);

                string sequence = string.Join(",", _sequence.Select(KeyNames.ToString));

                if (_bindings.TryGetValue(sequence, out var action))
                {
                    _active = false;
                    _sequence.Clear();
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        return LeaderResult.Failed(sequence, ex.Message);
                    }
                    return LeaderResult.Execute(action, sequence);
                }

                bool isPrefix = _bindings.Keys.Any(k => k.StartsWith(sequence + ",", StringComparison.OrdinalIgnoreCase));

                if (!isPrefix)
                {
                    // Typed something that matches no sequence and prefixes none: abort.
                    _active = false;
                    _sequence.Clear();
                    return LeaderResult.Abort;
                }

                return LeaderResult.Consume; // still waiting for more keys in the sequence
            }

            // 3. Not the leader key and no sequence in progress.
            return LeaderResult.PassThrough;
        }

        /// <summary>Clears any in-progress sequence.</summary>
        public void Reset()
        {
            _active = false;
            _sequence.Clear();
        }
    }

    /// <summary>The outcome of routing a key through <see cref="LeaderSequenceMatcher"/>.</summary>
    internal enum LeaderResultKind
    {
        PassThrough,
        Consume,
        Execute,
        Failed,
        Abort,
    }

    /// <summary>
    /// The result of a <see cref="LeaderSequenceMatcher.HandleKey"/> call: how the key should be
    /// treated, plus the matched action and sequence when a binding executed.
    /// </summary>
    internal readonly struct LeaderResult
    {
        public LeaderResultKind Kind { get; }
        public Action? Action { get; }
        public string? Sequence { get; }
        public string? ErrorMessage { get; }

        private LeaderResult(LeaderResultKind kind, Action? action, string? sequence, string? errorMessage)
        {
            Kind = kind;
            Action = action;
            Sequence = sequence;
            ErrorMessage = errorMessage;
        }

        public static LeaderResult PassThrough => new(LeaderResultKind.PassThrough, null, null, null);
        public static LeaderResult Consume => new(LeaderResultKind.Consume, null, null, null);
        public static LeaderResult Execute(Action action, string sequence) => new(LeaderResultKind.Execute, action, sequence, null);
        public static LeaderResult Failed(string sequence, string errorMessage) => new(LeaderResultKind.Failed, null, sequence, errorMessage);
        public static LeaderResult Abort => new(LeaderResultKind.Abort, null, null, null);
    }
}
