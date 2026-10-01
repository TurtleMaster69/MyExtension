using System;

namespace MyExtension.Vim
{
    /// <summary>
    /// Pure owner of the Vim typing/mode state, the focus-guard decision, and the VsVim
    /// resolution latch. Dependency-free so it is unit-testable (the
    /// <c>OverlayKeyHandler</c>/<c>HierarchyResolver</c> pattern). No VS types are touched here.
    ///
    /// <para/>
    /// The typing flag is volatile because the keyboard hook path reads it while focus/mode
    /// events mutate it on the UI thread. <see cref="SetMode"/> is the single owner of the
    /// classification (the seed for M32's <c>VimModeClassifier</c>); the focus handlers clear
    /// only when the view losing focus/closed is the one believed focused, so out-of-order
    /// events cannot spuriously clear typing.
    /// </summary>
    internal sealed class VimModeState
    {
        // Values of the Vim.ModeKind enum (Vim.Core): Normal=1, Insert=2, Replace=7. The values
        // and the classification truth table live in VimModeClassifier (the single owner, M32);
        // these aliases keep the M17 test surface (VimModeState.Normal/Insert/Replace) compiling.
        internal const int Normal = VimModeClassifier.Normal;
        internal const int Insert = VimModeClassifier.Insert;
        internal const int Replace = VimModeClassifier.Replace;

        private volatile bool _isTyping;
        private bool _resolved;
        private object? _resolvedValue;

        /// <summary>True when the focused editor buffer is in Insert/Replace (typing) mode.</summary>
        public bool IsTyping => _isTyping;

        /// <summary>The friendly mode name used by the <c>vim-mode=</c> diagnostic.</summary>
        public string ModeName { get; private set; } = "Unknown";

        /// <summary>
        /// Sets the mode from a ModeKind value (null = no buffer / unknown). Returns whether the
        /// mode changed, so the tracker can gate the <c>vim-mode=</c> log on change (M16's
        /// log-on-change contract).
        /// </summary>
        public bool SetMode(int? mode)
        {
            var (isTyping, name) = VimModeClassifier.Classify(mode);
            bool changed = isTyping != _isTyping || name != ModeName;
            _isTyping = isTyping;
            ModeName = name;
            return changed;
        }

        /// <summary>Clears the mode (no buffer / not typing).</summary>
        public void Clear() => SetMode(null);

        /// <summary>
        /// Handles a view losing focus. Clears typing only when the view is the focused one;
        /// returns whether the typing flag changed (so the tracker logs only on change).
        /// </summary>
        public bool OnViewLostFocus(bool isFocusedView)
        {
            if (!isFocusedView)
            {
                return false;
            }

            bool wasTyping = _isTyping;
            Clear();
            return wasTyping != _isTyping;
        }

        /// <summary>
        /// Handles a view closing. Clears typing only when the view is the focused one;
        /// returns whether the typing flag changed (so the tracker logs only on change).
        /// </summary>
        public bool OnViewClosed(bool isFocusedView)
        {
            if (!isFocusedView)
            {
                return false;
            }

            bool wasTyping = _isTyping;
            Clear();
            return wasTyping != _isTyping;
        }

        /// <summary>
        /// Resolves a value once and latches it for the process lifetime. On failure the latch is
        /// NOT set, so the next call retries the resolver; <paramref name="onFailure"/> is invoked
        /// with the exception. On success the resolver is never invoked again.
        /// </summary>
        public object? ResolveOnce(Func<object?> resolve, Action<Exception> onFailure)
        {
            if (_resolved)
            {
                return _resolvedValue;
            }

            try
            {
                object? value = resolve();
                _resolved = true;
                _resolvedValue = value;
                return value;
            }
            catch (Exception ex)
            {
                onFailure(ex);
                return null;
            }
        }
    }
}
