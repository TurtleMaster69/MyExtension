using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;

namespace MyExtension.Vim
{
    /// <summary>
    /// Tracks the Vim editing mode of the focused code editor so that the leader-key routing
    /// never has to query VsVim on a keystroke.
    ///
    /// <para/>
    /// <b>Why this exists:</b> our leader key is Space. In Vim normal mode Space is the
    /// leader, but in insert mode it must type a literal space. We need to know the focused
    /// editor's mode without paying the cost of asking VsVim on every Space press.
    ///
    /// <para/>
    /// <b>Event-driven model (no polling):</b> this type is a MEF
    /// <see cref="IWpfTextViewCreationListener"/> (exported for the <c>text</c> content type),
    /// so VS instantiates it and calls <see cref="TextViewCreated"/> for every new code text
    /// view. For each view we attach <c>GotAggregateFocus</c>/<c>LostAggregateFocus</c> and
    /// delegate the VsVim interop to an <see cref="IVimModeSource"/> (the production
    /// <see cref="VsVimModeSource"/>; tests inject a fake). Mode changes arrive as events and
    /// update a cached <see cref="IsInTypingMode"/> boolean. <see cref="IsInTypingMode"/> is then
    /// a pure in-memory read — no COM, no reflection, no TTL window.
    ///
    /// <para/>
    /// <b>Threading:</b> every editor/VsVim event here fires on the UI thread, and the cached
    /// state is only ever touched on the UI thread. <see cref="IsInTypingMode"/> is a volatile
    /// read so it can be consumed from the hook path without a marshal.
    ///
    /// <para/>
    /// <b>Degradation:</b> if VsVim is not installed, or a view has no Vim buffer, the mode is
    /// reported as "not typing" (false), which is the safe default for the leader key.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [Export(typeof(VimModeTracker))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class VimModeTracker : IWpfTextViewCreationListener
    {
        private readonly IVimModeSource _source;

        // The code view that currently holds aggregate focus, if any. Mutated on the UI thread
        // only (focus events); used to make the editor-focus flag robust to out-of-order focus
        // transitions (a lost-focus event from a non-focused view must not clear it).
        private ITextView? _focusedView;

        // True while a code editor text view holds keyboard focus. Volatile because the keyboard
        // path reads it from the hook thread while focus events mutate it on the UI thread.
        private volatile bool _editorFocused;

        // M17: single owner of the typing/mode state (the pure VimModeState). IsTyping is a
        // volatile read so it can be consumed from the hook path without a marshal.
        private readonly VimModeState _state = new();

        public VimModeTracker() : this(new VsVimModeSource())
        {
        }

        internal VimModeTracker(IVimModeSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _source.ModeChanged += OnModeChanged;
        }

        /// <summary>True when the focused editor buffer is in Insert/Replace (typing) mode.</summary>
        public bool IsInTypingMode => _state.IsTyping;

        /// <summary>
        /// True while a code editor text view holds keyboard focus. Sourced event-driven from each
        /// view's <c>GotAggregateFocus</c>/<c>LostAggregateFocus</c>, so it cannot go stale like a
        /// cached window-frame selection.
        ///
        /// <para/>
        /// <b>Fail-open risk (C5 — documented, NOT broadened):</b> this flag is deliberately NOT
        /// broadened to track the Telescope preview view (a hosted read-only editor view that is
        /// deliberately non-Editable — tracking it would flip the flag while the overlay preview
        /// holds focus). The accepted consequence: a non-text editor with a stale
        /// <c>IsToolWindow</c> fails the FocusGuard OPEN (tool-window action keys could leak into
        /// the editor). The FocusGuard's editor-focus veto is best-effort; the tool-window
        /// controllers' own input-mode gating is the real guard.
        /// </summary>
        public bool IsEditorFocused => _editorFocused;

        /// <summary>
        /// Called by the editor for every new code text view (UI thread). We attach focus and
        /// closed handlers and hand the view to the mode source so it can resolve/subscribe its
        /// Vim buffer.
        /// </summary>
        public void TextViewCreated(IWpfTextView view)
        {
            // Diagnostic for the E2E harness: a code editor view opened (via Telescope selection,
            // Solution Explorer Enter, a leader command, ...). Name the file when we can.
            try
            {
                string? path = null;
                if (view.TextBuffer.Properties.TryGetProperty(
                        typeof(Microsoft.VisualStudio.Text.ITextDocument),
                        out Microsoft.VisualStudio.Text.ITextDocument doc))
                {
                    path = doc.FilePath;
                }
                EditorViewOpenedLog.Emit(path);
            }
            catch
            {
                // logging must never break view creation
            }

            view.GotAggregateFocus += OnViewGotFocus;
            view.LostAggregateFocus += OnViewLostFocus;
            view.Closed += OnViewClosed;
            _source.Attach(view);
        }

        private void OnViewGotFocus(object sender, EventArgs e)
        {
            // A view gained focus. Seed the cached typing state from its current mode.
            // TryGetVimBuffer is non-creating; if VsVim hasn't created a buffer for this view
            // yet it returns null and we simply report not-typing until the next focus change.
            if (sender is ITextView view)
            {
                _focusedView = view;
                _editorFocused = true;
                UpdateTypingFromMode(_source.GetModeKind(view));
            }
        }

        private void OnViewLostFocus(object sender, EventArgs e)
        {
            // Focus left a code editor (e.g. to a tool window): the leader key is safe, so the
            // user is not "typing" in an editor. The tool-window input-mode path is handled
            // separately by InputHandler. Only clear the editor-focus flag and the typing state
            // when the view losing focus is the one we believe is focused (an out-of-order event
            // must not clear them).
            if (sender is ITextView view)
            {
                bool isFocusedView = ReferenceEquals(_focusedView, view);
                if (isFocusedView)
                {
                    _editorFocused = false;
                }
                if (_state.OnViewLostFocus(isFocusedView))
                {
                    Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}vim-mode=Unknown");
                }
            }
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            // Editor view closed: detach handlers so nothing leaks. Only clear the typing state
            // when the closed view is the one we believe is focused.
            if (sender is ITextView view)
            {
                bool isFocusedView = ReferenceEquals(_focusedView, view);
                if (isFocusedView)
                {
                    _focusedView = null;
                    _editorFocused = false;
                }
                if (_state.OnViewClosed(isFocusedView))
                {
                    Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}vim-mode=Unknown");
                }

                view.GotAggregateFocus -= OnViewGotFocus;
                view.LostAggregateFocus -= OnViewLostFocus;
                view.Closed -= OnViewClosed;
                _source.Detach(view);
            }
        }

        private void OnModeChanged(int? mode)
        {
            UpdateTypingFromMode(mode);
        }

        /// <summary>Updates the cached typing flag from a ModeKind value and logs the mode name.</summary>
        private void UpdateTypingFromMode(int? mode)
        {
            // M16/M17: emit `vim-mode=` only when the mode actually changes — a focus gain/loss
            // with the same mode emits nothing. The emitted token stays byte-identical.
            if (_state.SetMode(mode))
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}vim-mode={_state.ModeName}");
            }
        }
    }
}
