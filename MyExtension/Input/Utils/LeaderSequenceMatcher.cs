using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MyExtension.Input
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
        private volatile bool _active;

        // M7: the sequence string is maintained incrementally (append each key's name to a
        // StringBuilder) instead of rebuilding it with string.Join + Select per key.
        private readonly StringBuilder _sequenceBuilder = new StringBuilder();

        // M7: every proper prefix of every binding sequence, precomputed once at construction so
        // the per-key prefix check is a set lookup instead of a StartsWith scan over all bindings.
        private readonly HashSet<string> _prefixSet;

        public LeaderSequenceMatcher(Keys leaderKey, IReadOnlyDictionary<string, Action> bindings)
        {
            _leaderKey = leaderKey;
            _bindings = bindings;
            _prefixSet = BuildPrefixSet(bindings);
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
                _sequenceBuilder.Clear();
                return LeaderResult.Consume;
            }

            // 2. Building a leader sequence: append the key and match against the bindings,
            //    using prefix detection to keep waiting for multi-key sequences like "f f".
            if (_active)
            {
                // Modifier keys (Shift/Ctrl/Alt/Win — down AND up) are TRANSPARENT while a
                // sequence is in progress: consumed without being appended and without
                // aborting. Typing a shifted sequence member (e.g. '|' = Shift+0xDC for the
                // "w,|" binding) sends the Shift key-down while the prefix is pending; appending
                // it built "w,Shift" and aborted, so the shifted key could never complete the
                // binding. The hook dispatches only key-downs, but the matcher treats key-ups
                // the same so the pure machine stays coherent for any caller.
                if (IsModifierKey(key))
                {
                    return LeaderResult.Consume;
                }

                if (_sequenceBuilder.Length > 0)
                {
                    _sequenceBuilder.Append(',');
                }
                _sequenceBuilder.Append(KeyNames.ToString(key, shift));

                string sequence = _sequenceBuilder.ToString();

                if (_bindings.TryGetValue(sequence, out var action))
                {
                    _active = false;
                    _sequenceBuilder.Clear();
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        return LeaderResult.Failed(sequence, ex.Message);
                    }
                    return LeaderResult.Execute(sequence);
                }

                if (!_prefixSet.Contains(sequence))
                {
                    // Typed something that matches no sequence and prefixes none: abort.
                    _active = false;
                    _sequenceBuilder.Clear();
                    return LeaderResult.Abort;
                }

                return LeaderResult.Consume; // still waiting for more keys in the sequence
            }

            // 3. Not the leader key and no sequence in progress.
            return LeaderResult.PassThrough;
        }

        /// <summary>
        /// True for the physical modifier virtual-keys (Shift/Ctrl/Alt/Win, left and right
        /// variants) — the bare key-downs the hook delivers while a chord is being typed
        /// (e.g. Shift+0xDC types '|'). These are never sequence members.
        /// </summary>
        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey
                || key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey
                || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu
                || key == Keys.LWin || key == Keys.RWin;
        }

        /// <summary>Clears any in-progress sequence.</summary>
        public void Reset()
        {
            _active = false;
            _sequenceBuilder.Clear();
        }

        /// <summary>
        /// Builds the set of every proper prefix of every binding sequence (at key boundaries),
        /// e.g. <c>"f"</c> for the binding <c>"f,f"</c>. A sequence is a live prefix iff it is in
        /// this set — equivalent to the old <c>_bindings.Keys.Any(k => k.StartsWith(sequence + ","))</c>.
        /// </summary>
        private static HashSet<string> BuildPrefixSet(IReadOnlyDictionary<string, Action> bindings)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string binding in bindings.Keys)
            {
                string[] keys = binding.Split(',');
                for (int i = 1; i < keys.Length; i++)
                {
                    set.Add(string.Join(",", keys, 0, i));
                }
            }
            return set;
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
    /// treated, plus the matched sequence when a binding executed.
    /// </summary>
    internal readonly struct LeaderResult
    {
        public LeaderResultKind Kind { get; }
        public string? Sequence { get; }
        public string? ErrorMessage { get; }

        private LeaderResult(LeaderResultKind kind, string? sequence, string? errorMessage)
        {
            Kind = kind;
            Sequence = sequence;
            ErrorMessage = errorMessage;
        }

        public static LeaderResult PassThrough => new(LeaderResultKind.PassThrough, null, null);
        public static LeaderResult Consume => new(LeaderResultKind.Consume, null, null);
        public static LeaderResult Execute(string sequence) => new(LeaderResultKind.Execute, sequence, null);
        public static LeaderResult Failed(string sequence, string errorMessage) => new(LeaderResultKind.Failed, sequence, errorMessage);
        public static LeaderResult Abort => new(LeaderResultKind.Abort, null, null);
    }
}
