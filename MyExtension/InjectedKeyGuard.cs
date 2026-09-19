using System;
using System.Collections.Generic;

namespace MyExtension
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

        // The per-VK pending press counter.
        private readonly Dictionary<int, int> _pending = new Dictionary<int, int>();

        /// <summary>Records one pending press of the VK.</summary>
        public void Record(int vk)
        {
            _pending.TryGetValue(vk, out int count);
            _pending[vk] = count + 1;
        }

        /// <summary>
        /// Consumes ONE pending record of the VK and returns true; returns false if no record
        /// for that VK is pending.
        /// </summary>
        public bool TryConsume(int vk)
        {
            if (_pending.TryGetValue(vk, out int count) && count > 0)
            {
                if (count == 1)
                {
                    _pending.Remove(vk);
                }
                else
                {
                    _pending[vk] = count - 1;
                }
                return true;
            }
            return false;
        }
    }
}