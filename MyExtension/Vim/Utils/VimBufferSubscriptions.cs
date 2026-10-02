using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text.Editor;

namespace MyExtension.Vim
{
    /// <summary>
    /// Pure per-view Vim-buffer subscription map (CR2). Tracks which Vim buffer each editor view
    /// is subscribed to, so closing a non-focused view can unsubscribe ONLY that view's buffer
    /// without killing the focused view's <c>SwitchedMode</c> subscription (the CR2 bug: today
    /// <see cref="VsVimModeSource.Detach"/> ignores the view and unsubscribes the single global
    /// <c>_currentTextBuffer</c>, so closing a non-focused view kills the focused view's mode
    /// subscription and <c>_cachedTyping</c> goes stale).
    ///
    /// <para/>
    /// This is the extracted seam for <see cref="VsVimModeSource"/>'s per-view subscription
    /// tracking. <see cref="VsVimModeSource"/> wires it in: <c>Attach</c> records the view's
    /// buffer and <c>Detach</c> unsubscribes ONLY that view's buffer. The class is
    /// dependency-free and unit-tested hermetically.
    ///
    /// <para/>
    /// <b>R3 lifecycle bookkeeping:</b> the map also owns the Closed-subscription set (which
    /// <c>IVimBuffer</c> objects have a <c>Closed</c> event subscribed) and the per-shared-text-buffer
    /// reference count (m40), so <see cref="VsVimModeSource"/>'s <c>UnsubscribeBuffer</c> /
    /// <c>OnBufferClosed</c> / <c>Detach</c> paths coordinate through one pure owner and cannot
    /// double-decrement or leak a <c>Closed</c> subscription.
    /// </summary>
    internal sealed class VimBufferSubscriptions
    {
        private readonly Dictionary<ITextView, object> _map = new Dictionary<ITextView, object>();

        // R3: buffer (IVimBuffer) -> text buffer (IVimTextBuffer), for refcount + Closed cleanup.
        private readonly Dictionary<object, object> _bufferToTextBuffer = new Dictionary<object, object>();

        // R3: buffers whose Closed event is currently subscribed (the IVimBuffer objects).
        private readonly HashSet<object> _closedSubscribed = new HashSet<object>();

        // R3: reference count of attached views per shared text buffer (m40). Two views can share
        // one ITextBuffer (split views), so the SwitchedMode subscription is dropped only at 0.
        private readonly Dictionary<object, int> _textBufferRefCounts = new Dictionary<object, int>();

        /// <summary>Records that <paramref name="view"/> is subscribed to <paramref name="buffer"/>.</summary>
        public void Attach(ITextView view, object buffer, object? textBuffer = null)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }
            _map[view] = buffer ?? throw new ArgumentNullException(nameof(buffer));
            if (textBuffer != null)
            {
                _bufferToTextBuffer[buffer] = textBuffer;
                _textBufferRefCounts[textBuffer] = _textBufferRefCounts.TryGetValue(textBuffer, out int c) ? c + 1 : 1;
            }
        }

        /// <summary>
        /// Unsubscribes ONLY <paramref name="view"/>'s buffer. No-op for a non-attached view.
        /// Returns true when the buffer's text-buffer refcount reached 0 (the caller should
        /// unsubscribe the SwitchedMode event). R3: a buffer already removed by
        /// <see cref="OnBufferClosed"/> is not double-decremented.
        /// </summary>
        public bool Detach(ITextView view)
        {
            if (view == null)
            {
                return false;
            }
            object? buffer = _map.TryGetValue(view, out var b) ? b : null;
            _map.Remove(view);
            if (buffer != null && _closedSubscribed.Remove(buffer))
            {
                return DecrementRefCount(buffer);
            }
            return false;
        }

        /// <summary>Returns the buffer <paramref name="view"/> is subscribed to, or null.</summary>
        public object? BufferFor(ITextView view)
        {
            if (view == null)
            {
                return null;
            }
            return _map.TryGetValue(view, out var buffer) ? buffer : null;
        }

        /// <summary>R3: records that the buffer's Closed event is subscribed. Returns false when it
        /// was already subscribed (guards double-subscribing).</summary>
        public bool MarkClosedSubscribed(object buffer) => _closedSubscribed.Add(buffer);

        /// <summary>
        /// R3: the buffer's Closed event fired — removes the Closed subscription and decrements the
        /// refcount. Returns true when the refcount reached 0 (the caller should unsubscribe the
        /// SwitchedMode event).
        /// </summary>
        public bool OnBufferClosed(object buffer)
        {
            if (_closedSubscribed.Remove(buffer))
            {
                return DecrementRefCount(buffer);
            }
            return false;
        }

        /// <summary>
        /// R3: removes the Closed subscription + decrements the refcount (the UnsubscribeBuffer
        /// path). Returns true when the refcount reached 0.
        /// </summary>
        public bool UnsubscribeBuffer(object buffer)
        {
            if (_closedSubscribed.Remove(buffer))
            {
                return DecrementRefCount(buffer);
            }
            return false;
        }

        private bool DecrementRefCount(object buffer)
        {
            if (!_bufferToTextBuffer.TryGetValue(buffer, out object? textBuffer))
            {
                return false;
            }
            if (_textBufferRefCounts.TryGetValue(textBuffer, out int count))
            {
                if (count <= 1)
                {
                    _textBufferRefCounts.Remove(textBuffer);
                    return true;
                }
                _textBufferRefCounts[textBuffer] = count - 1;
            }
            return false;
        }
    }
}
