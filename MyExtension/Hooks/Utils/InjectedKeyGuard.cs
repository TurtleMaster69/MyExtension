using System;
using System.Collections.Generic;

namespace MyExtension.Hooks
{
    /// <summary>
    /// Per-VK pending counter that lets the global keyboard hook recognize the keys
    /// <see cref="KeyInjection.Press"/> has synthesized, so those injected keys pass through
    /// the hook untouched instead of re-triggering the controller action that injected them.
    ///
    /// <para/>
    /// <b>Why (the Enter-storm fix):</b> <c>SolutionExplorerController</c> maps Enter (and o) to
    /// <c>OpenSelected()</c>, which injects a native <c>VK_RETURN</c> via <see cref="KeyInjection"/>.
    /// That injected Return re-enters the global hook (Enter is in the controller's action keys, so
    /// the hook's pre-filter treats it as interesting) and is re-routed to <c>OpenSelected()</c>
    /// again, which injects another Return — an unbounded re-injection storm. A blanket
    /// <c>LLKHF_INJECTED</c> bail is not viable because the e2e harness injects every test key via
    /// <c>keybd_event</c> too; the fix must distinguish OUR process's injection from the harness's.
    ///
    /// <para/>
    /// <b>Mechanism:</b> <see cref="KeyInjection.Press"/> records the VK it is about to synthesize
    /// (<see cref="Record"/>); the hook's key-down branch consumes it (<see cref="TryConsume"/>) and
    /// passes the event through (<c>CallNextHookEx</c>) before any handling, so our injected key
    /// reaches the focused tree/control natively. Event ordering makes this deterministic:
    /// <c>keybd_event</c> queues the event NOW, and the harness's next key is ≥60ms away, so the
    /// injected event is always the next same-VK event the hook sees (consume-once semantics).
    ///
    /// <para/>
    /// <b>Threading:</b> both <see cref="KeyInjection.Press"/> and the hook callback run on the UI
    /// thread, so the guard is a plain-field per-VK counter with NO locking and NO time window.
    /// </summary>
    internal sealed class InjectedKeyGuard
    {
        // Single shared instance the extension wiring uses (KeyInjection + GlobalKeyboardHook both
        // read/write it on the UI thread). Tests new-up their own instances.
        internal static readonly InjectedKeyGuard Instance = new InjectedKeyGuard();

        // A pending record older than this is treated as absent (m43): a stale record must not
        // consume the next physical key-down. The injected event is queued by keybd_event NOW and
        // the harness's next physical key is >=60ms away, so a 1s TTL is generous.
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(1);

        // The per-VK pending press counter, keyed by VK.
        private readonly Dictionary<int, PendingRecord> _pending = new Dictionary<int, PendingRecord>();
        private readonly TimeSpan _ttl;
        private readonly Func<DateTime> _clock;

        /// <summary>Default guard: a real clock and a sensible TTL.</summary>
        public InjectedKeyGuard()
            : this(DefaultTtl, () => DateTime.UtcNow)
        {
        }

        /// <summary>
        /// Test-only seam: a guard with an injected clock and TTL so the stale-record expiry is
        /// deterministic. Internal so the offline NeoVisual test project can exercise the TTL
        /// hermetically.
        /// </summary>
        internal InjectedKeyGuard(TimeSpan ttl, Func<DateTime> clock)
        {
            _ttl = ttl;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Records one pending press of the VK.</summary>
        public void Record(int vk)
        {
            _pending.TryGetValue(vk, out PendingRecord record);
            // R12: the TTL is checked at RECORD time, not consume time — a >1s UI stall between
            // Press's Record and the injected key-down would otherwise expire the record on consume
            // and cause a re-injection storm. A stale record (older than the TTL) is dropped when a
            // new Record arrives: the new Record starts fresh at count 1 instead of carrying the
            // stale record's count forward.
            int count = record.Count > 0 && _clock() - record.Timestamp <= _ttl ? record.Count + 1 : 1;
            _pending[vk] = new PendingRecord(count, _clock());
        }

        /// <summary>
        /// m63 (BP-D16): test-only reset seam — clears all pending records so a test sharing the
        /// static <see cref="Instance"/> is order-independent (a prior test's injected state cannot
        /// leak into it). No production behavior change.
        /// </summary>
        internal void Reset() => _pending.Clear();

        /// <summary>
        /// Consumes ONE pending record of the VK and returns true; returns false if no record
        /// for that VK is pending. A record older than the TTL is treated as absent (removed
        /// without consuming), so a stale record never swallows the next physical key-down.
        ///
        /// <para/>
        /// <b>Accepted risk (C4):</b> matching is keyed by VK + TTL only — the guard cannot
        /// distinguish an injected event from a physical one. A dropped injected event could
        /// therefore consume the next PHYSICAL same-VK key-down within the 1s TTL. This is
        /// theoretical: <c>keybd_event</c> queues the injected event synchronously, so the
        /// injected key-down is always the next same-VK event the hook sees. No behavior change.
        /// </summary>
        public bool TryConsume(int vk)
        {
            if (_pending.TryGetValue(vk, out PendingRecord record) && record.Count > 0)
            {
                if (_clock() - record.Timestamp > _ttl)
                {
                    _pending.Remove(vk);
                    return false;
                }
                if (record.Count == 1)
                {
                    _pending.Remove(vk);
                }
                else
                {
                    _pending[vk] = new PendingRecord(record.Count - 1, record.Timestamp);
                }
                return true;
            }
            return false;
        }

        private readonly struct PendingRecord
        {
            public readonly int Count;
            public readonly DateTime Timestamp;

            public PendingRecord(int count, DateTime timestamp)
            {
                Count = count;
                Timestamp = timestamp;
            }
        }
    }
}