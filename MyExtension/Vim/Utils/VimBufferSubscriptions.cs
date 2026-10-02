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
    /// </summary>
    internal sealed class VimBufferSubscriptions
    {
        private readonly Dictionary<ITextView, object> _map = new Dictionary<ITextView, object>();
        private ITextView? _focusedView;

        /// <summary>Records that <paramref name="view"/> is subscribed to <paramref name="buffer"/>.</summary>
        public void Attach(ITextView view, object buffer)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }
            _map[view] = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _focusedView = view;
        }

        /// <summary>Unsubscribes ONLY <paramref name="view"/>'s buffer. No-op for a non-attached view.</summary>
        public void Detach(ITextView view)
        {
            if (view == null)
            {
                return;
            }
            _map.Remove(view);
            if (ReferenceEquals(_focusedView, view))
            {
                _focusedView = null;
            }
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

        /// <summary>The view that most recently attached (the focused view), or null.</summary>
        public ITextView? FocusedView => _focusedView;

        /// <summary>The buffer of the focused view, or null.</summary>
        public object? FocusedBuffer => _focusedView != null && _map.TryGetValue(_focusedView, out var b) ? b : null;
    }
}
