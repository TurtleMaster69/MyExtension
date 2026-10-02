using System;

namespace MyExtension.Vim
{
    /// <summary>
    /// Pure owner of the Vim typing/mode state and the focus-guard decision. Dependency-free so
    /// it is unit-testable (the <c>OverlayKeyHandler</c>/<c>HierarchyResolver</c> pattern). No VS
    /// types are touched here.
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
        private volatile bool _isTyping;

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

        /// <summary>
        /// Clears the mode (no buffer / not typing). Returns whether the mode changed, so the
        /// tracker can gate the <c>vim-mode=Unknown</c> log on change (m41).
        /// </summary>
        public bool Clear() => SetMode(null);

        /// <summary>
        /// Handles a view losing focus. Clears the mode only when the view is the focused one;
        /// returns whether the MODE changed (not just the typing flag), so the tracker emits
        /// <c>vim-mode=Unknown</c> on focus loss even from Normal mode (m41).
        /// </summary>
        public bool OnViewLostFocus(bool isFocusedView)
        {
            if (!isFocusedView)
            {
                return false;
            }

            return Clear();
        }

        /// <summary>
        /// Handles a view closing. Clears the mode only when the view is the focused one;
        /// returns whether the MODE changed (not just the typing flag), so the tracker emits
        /// <c>vim-mode=Unknown</c> when a focused Normal-mode view closes (m41).
        /// </summary>
        public bool OnViewClosed(bool isFocusedView)
        {
            if (!isFocusedView)
            {
                return false;
            }

            return Clear();
        }
    }
}
