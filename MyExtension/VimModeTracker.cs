using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;

namespace MyExtension
{
    /// <summary>
    /// Tracks the Vim editing mode of the focused code editor using VsVim's own
    /// <c>SwitchedMode</c> event, so that the leader-key routing never has to query
    /// VsVim on a keystroke.
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
    /// view. For each view we attach <c>GotAggregateFocus</c>/<c>LostAggregateFocus</c> and, once
    /// VsVim has a buffer for the view, subscribe to the buffer's <c>SwitchedMode</c> event.
    /// Mode changes arrive as events and update a cached <see cref="IsInTypingMode"/> boolean.
    /// <see cref="IsInTypingMode"/> is then a pure in-memory read — no COM, no reflection, no
    /// TTL window.
    ///
    /// <para/>
    /// <b>Interop model:</b> VsVim publishes its engine as a MEF export (the <c>Vim.IVim</c>
    /// interface, assembly <c>Vim.Core.dll</c>). We resolve it reflectively by contract string
    /// <c>"Vim.IVim"</c>, then get the focused view's <c>IVimBuffer</c> via <c>TryGetVimBuffer</c>
    /// and read its <c>IVimTextBuffer</c>. Mode is a per-ITextBuffer concept, so we subscribe to
    /// <c>IVimTextBuffer.SwitchedMode</c>, whose args (<c>SwitchModeKindEventArgs</c>) expose the
    /// new <c>ModeKind</c> as a plain public enum property — no <c>IMode</c> / explicit-impl
    /// reflection is needed to read it. VsVim implements its interfaces explicitly, so interface
    /// members are reached by invoking the interface's <c>MethodInfo</c> directly (which the CLR
    /// dispatches to the concrete private implementation). We cache the resolved
    /// <see cref="MethodInfo"/> and delegate handles.
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
        // Values of the Vim.ModeKind enum (Vim.Core): Normal=1, Insert=2, Replace=7 (verified by
        // reflecting over Vim.Core.dll). Normal is named for the diagnostic log the E2E harness
        // asserts on; typing modes are Insert/Replace.
        private const int Normal = 1;
        private const int Insert = 2;
        private const int Replace = 7;

        // MEF contract name ([Export(typeof(IVim))] -> full type name) and full interface
        // names used for interface dispatch.
        private const string VimContractName = "Vim.IVim";
        private const string IVimFullName = "Vim.IVim";
        private const string IVimBufferFullName = "Vim.IVimBuffer";
        private const string IVimTextBufferFullName = "Vim.IVimTextBuffer";

        // VsVim's engine, lazily resolved once and cached for the process lifetime.
        private object? _vim;
        private bool _resolved;

        // Cached MethodInfo / delegate handles for reflection dispatch.
        private MethodInfo? _tryGetVimBufferMethod;          // IVim.TryGetVimBuffer
        private MethodInfo? _addSwitchedModeMethod;          // IVimTextBuffer.add_SwitchedMode
        private MethodInfo? _removeSwitchedModeMethod;       // IVimTextBuffer.remove_SwitchedMode
        private MethodInfo? _getTextBufferModeKindMethod;    // IVimTextBuffer.get_ModeKind
        private MethodInfo? _addClosedMethod;                // IVimBuffer.add_Closed

        private Delegate? _switchedModeDelegate;             // EventHandler<SwitchModeKindEventArgs>
        private readonly EventHandler _bufferClosedDelegate; // EventHandler (System)

        private IComponentModel? _componentModel;

        // The IVimBuffer we resolved for the focused view (used for Closed cleanup; UI thread only).
        private object? _currentBuffer;

        // The IVimTextBuffer currently subscribed for SwitchedMode (UI thread only). Vim mode is a
        // per-ITextBuffer concept, so this is what the SwitchedMode event's sender is.
        private object? _currentTextBuffer;

        // The single cached answer. Volatile because it is read by the keyboard path while
        // events mutate it on the UI thread.
        private volatile bool _cachedTyping;

        // The code view that currently holds aggregate focus, if any. Mutated on the UI thread
        // only (focus events); used to make the editor-focus flag robust to out-of-order focus
        // transitions (a lost-focus event from a non-focused view must not clear it).
        private ITextView? _focusedView;

        // True while a code editor text view holds keyboard focus. Volatile because the keyboard
        // path reads it from the hook thread while focus events mutate it on the UI thread.
        private volatile bool _editorFocused;

        public VimModeTracker()
        {
            _bufferClosedDelegate = OnBufferClosed;
        }

        /// <summary>True when the focused editor buffer is in Insert/Replace (typing) mode.</summary>
        public bool IsInTypingMode => _cachedTyping;

        /// <summary>
        /// True while a code editor text view holds keyboard focus. Sourced event-driven from each
        /// view's <c>GotAggregateFocus</c>/<c>LostAggregateFocus</c>, so it cannot go stale like a
        /// cached window-frame selection.
        /// </summary>
        public bool IsEditorFocused => _editorFocused;

        /// <summary>
        /// Called by the editor for every new code text view (UI thread). We attach focus and
        /// closed handlers; the vim buffer is resolved lazily on focus so VsVim has a chance to
        /// create it first.
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
                Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}editor-view-opened file={path}");
            }
            catch
            {
                // logging must never break view creation
            }

            view.GotAggregateFocus += OnViewGotFocus;
            view.LostAggregateFocus += OnViewLostFocus;
            view.Closed += OnViewClosed;
        }

        private void OnViewGotFocus(object sender, EventArgs e)
        {
            // A view gained focus. Resolve its Vim buffer (if any), subscribe to its
            // SwitchedMode event, and seed the cached typing state from its current mode.
            // TryGetVimBuffer is non-creating; if VsVim hasn't created a buffer for this view
            // yet it returns null and we simply report not-typing until the next focus change.
            if (sender is ITextView view)
            {
                _focusedView = view;
                _editorFocused = true;

                object? buffer = GetBufferForView(view);
                if (buffer != null)
                {
                    SubscribeBuffer(buffer);
                }
                else
                {
                    _cachedTyping = false;
                }
            }
        }

        private void OnViewLostFocus(object sender, EventArgs e)
        {
            // Focus left a code editor (e.g. to a tool window): the leader key is safe, so the
            // user is not "typing" in an editor. The tool-window input-mode path is handled
            // separately by InputHandler. Only clear the editor-focus flag when the view losing
            // focus is the one we believe is focused (an out-of-order event must not clear it).
            if (sender is ITextView view && ReferenceEquals(_focusedView, view))
            {
                _editorFocused = false;
            }

            _cachedTyping = false;
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            // Editor view closed: detach handlers so nothing leaks. The buffer's own Closed
            // event (handled in OnBufferClosed) detaches the SwitchedMode subscription.
            if (sender is ITextView view)
            {
                if (ReferenceEquals(_focusedView, view))
                {
                    _focusedView = null;
                    _editorFocused = false;
                }

                view.GotAggregateFocus -= OnViewGotFocus;
                view.LostAggregateFocus -= OnViewLostFocus;
                view.Closed -= OnViewClosed;
            }

            _cachedTyping = false;
        }

        /// <summary>
        /// Subscribes to a buffer's SwitchedMode event (and its Closed event for cleanup). Vim
        /// mode is a per-ITextBuffer concept in VsVim, so we subscribe to the
        /// <c>IVimTextBuffer.SwitchedMode</c> event (reached through the <c>IVimBuffer</c> we
        /// resolved for the focused view), which hands us the new ModeKind directly.
        /// </summary>
        private void SubscribeBuffer(object buffer)
        {
            if (ReferenceEquals(buffer, _currentBuffer))
            {
                // Already subscribed to this buffer; just refresh the cached mode.
                UpdateTypingFromMode(GetTextBufferModeKind(GetTextBuffer(buffer)));
                return;
            }

            // Unsubscribe from any previous text buffer before moving to the new one.
            UnsubscribeBuffer(_currentTextBuffer);

            _currentBuffer = buffer;

            // The IVimTextBuffer is the per-buffer object whose SwitchedMode event we consume.
            object? textBuffer = GetTextBuffer(buffer);
            _currentTextBuffer = textBuffer;
            if (textBuffer == null)
            {
                UpdateTypingFromMode(null);
                return;
            }

            try
            {
                _addSwitchedModeMethod ??= GetInterfaceMethod(textBuffer, IVimTextBufferFullName, "add_SwitchedMode");
                if (_addSwitchedModeMethod != null)
                {
                    _switchedModeDelegate ??= BuildSwitchedModeDelegate(textBuffer);
                    if (_switchedModeDelegate != null)
                    {
                        _addSwitchedModeMethod.Invoke(textBuffer, new object[] { _switchedModeDelegate });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim SwitchedMode subscribe failed: {ex.Message}");
            }

            try
            {
                _addClosedMethod ??= GetInterfaceMethod(buffer, IVimBufferFullName, "add_Closed");
                _addClosedMethod?.Invoke(buffer, new object[] { _bufferClosedDelegate });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim Closed subscribe failed: {ex.Message}");
            }

            UpdateTypingFromMode(GetTextBufferModeKind(textBuffer));
        }

        private void UnsubscribeBuffer(object? textBuffer)
        {
            if (textBuffer == null || _switchedModeDelegate == null)
            {
                return;
            }

            try
            {
                _removeSwitchedModeMethod ??= GetInterfaceMethod(textBuffer, IVimTextBufferFullName, "remove_SwitchedMode");
                _removeSwitchedModeMethod?.Invoke(textBuffer, new object[] { _switchedModeDelegate });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim SwitchedMode unsubscribe failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds a delegate of the exact type the text buffer's SwitchedMode event expects
        /// (<c>EventHandler&lt;SwitchModeKindEventArgs&gt;</c>). The event's handler signature is
        /// <c>(object, SwitchModeKindEventArgs)</c>; we can't name that concrete args type at
        /// compile time (no Vim.Core.dll reference), so a plain
        /// <c>Delegate.CreateDelegate</c> to our <c>(object, EventArgs)</c> handler fails to
        /// bind. Instead we build a delegate of the exact handler type via an expression tree
        /// that converts the concrete args up to <see cref="EventArgs"/> and forwards to
        /// <see cref="OnSwitchedMode"/>.
        /// </summary>
        private Delegate? BuildSwitchedModeDelegate(object textBuffer)
        {
            try
            {
                Type? interfaceType = textBuffer.GetType().GetInterfaces()
                    .FirstOrDefault(t => t.FullName == IVimTextBufferFullName);
                System.Reflection.EventInfo? ev = interfaceType?.GetEvent("SwitchedMode");
                if (ev?.EventHandlerType == null)
                {
                    return null;
                }

                MethodInfo onSwitchedMode = typeof(VimModeTracker).GetMethod(
                    nameof(OnSwitchedMode),
                    BindingFlags.NonPublic | BindingFlags.Instance)!;

                var invoke = ev.EventHandlerType.GetMethod("Invoke")!;
                var invokeParams = invoke.GetParameters();

                var sender = System.Linq.Expressions.Expression.Parameter(invokeParams[0].ParameterType, "sender");
                var args = System.Linq.Expressions.Expression.Parameter(invokeParams[1].ParameterType, "e");
                var call = System.Linq.Expressions.Expression.Call(
                    System.Linq.Expressions.Expression.Constant(this),
                    onSwitchedMode,
                    sender,
                    System.Linq.Expressions.Expression.Convert(args, typeof(EventArgs)));

                return System.Linq.Expressions.Expression
                    .Lambda(ev.EventHandlerType, call, sender, args)
                    .Compile();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim SwitchedMode delegate build failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Raised by VsVim whenever the buffer's mode changes (UI thread).</summary>
        private void OnSwitchedMode(object sender, EventArgs e)
        {
            // Only trust the mode of the text buffer we currently consider focused. During a
            // focus switch the previous buffer may still fire SwitchedMode (or a stale event may
            // arrive after we moved on); ignore anything not from _currentTextBuffer so we don't
            // overwrite the focused buffer's cached state.
            if (!ReferenceEquals(sender, _currentTextBuffer))
            {
                return;
            }

            // SwitchModeKindEventArgs.ModeKind is a plain public enum property on the concrete
            // event-args class — no IMode / explicit-impl reflection needed to read it.
            UpdateTypingFromMode(GetModeKindFromEventArgs(e));
        }

        private void OnBufferClosed(object sender, EventArgs e)
        {
            // The buffer is gone; make sure we no longer reference or subscribe to it.
            if (ReferenceEquals(sender, _currentBuffer))
            {
                _currentBuffer = null;
                _currentTextBuffer = null;
                _cachedTyping = false;
            }
        }

        /// <summary>Updates the cached typing flag from a ModeKind value.</summary>
        private void UpdateTypingFromMode(int? mode)
        {
            _cachedTyping = mode == Insert || mode == Replace;

            // Diagnostic for the E2E harness (mode switches are rare, never per-keystroke).
            string name = mode switch
            {
                Normal => "Normal",
                Insert => "Insert",
                Replace => "Replace",
                _ => mode?.ToString() ?? "Unknown",
            };
            Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}vim-mode={name}");
        }

        /// <summary>Extracts the new mode from a SwitchedMode event's args.</summary>
        private static int? GetModeKindFromEventArgs(EventArgs e)
        {
            try
            {
                // SwitchModeKindEventArgs.ModeKind is a public property on the sealed args class.
                object? mode = e.GetType().GetProperty("ModeKind")?.GetValue(e);
                return mode == null ? (int?)null : Convert.ToInt32(mode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim SwitchedMode read failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Returns this view's Vim buffer via <c>IVim.TryGetVimBuffer</c>, or null.</summary>
        private object? GetBufferForView(ITextView view)
        {
            object? vim = GetVim();
            if (vim == null)
            {
                return null;
            }

            try
            {
                // Vim.IVim.TryGetVimBuffer(ITextView, out IVimBuffer) -> bool  (non-creating).
                _tryGetVimBufferMethod ??= GetInterfaceMethod(vim, IVimFullName, "TryGetVimBuffer");
                if (_tryGetVimBufferMethod == null)
                {
                    return null;
                }

                object?[] args = new object?[] { view, null }; // { input, out-slot }
                bool ok = (bool)_tryGetVimBufferMethod.Invoke(vim, args);
                return ok ? args[1] : null; // args[1] is the out buffer, filled by the invoke
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}TryGetVimBuffer failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Returns the <c>IVimTextBuffer</c> for a view's <c>IVimBuffer</c>, or null.</summary>
        private object? GetTextBuffer(object buffer)
        {
            if (buffer == null)
            {
                return null;
            }

            try
            {
                MethodInfo? get = GetInterfaceMethod(buffer, IVimBufferFullName, "get_VimTextBuffer");
                return get?.Invoke(buffer, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim VimTextBuffer read failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Reads an IVimTextBuffer's ModeKind enum value, or null if it can't be determined.</summary>
        private int? GetTextBufferModeKind(object? textBuffer)
        {
            if (textBuffer == null)
            {
                return null;
            }

            try
            {
                _getTextBufferModeKindMethod ??= GetInterfaceMethod(textBuffer, IVimTextBufferFullName, "get_ModeKind");
                object? mode = _getTextBufferModeKindMethod?.Invoke(textBuffer, null);
                return mode == null ? (int?)null : Convert.ToInt32(mode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim ModeKind read failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolves an interface method on the target. This works for explicit interface
        /// implementations: invoking the interface's MethodInfo dispatches to the concrete
        /// (often private) implementation. Resolution is cheap, so it is safe to call per event
        /// without caching a concrete-method handle across different runtime types.
        /// </summary>
        private static MethodInfo? GetInterfaceMethod(object target, string interfaceFullName, string methodName)
        {
            Type? interfaceType = target.GetType().GetInterfaces()
                .FirstOrDefault(t => t.FullName == interfaceFullName);
            return interfaceType?.GetMethod(methodName);
        }

        /// <summary>Lazily resolves VsVim's IVim export; null (-> all checks false) when absent.</summary>
        private object? GetVim()
        {
            if (_resolved)
            {
                return _vim;
            }

            _resolved = true; // only attempt resolution once, even if it fails

            try
            {
                IComponentModel? componentModel = GetComponentModel();
                if (componentModel?.DefaultExportProvider == null)
                {
                    return _vim = null;
                }

                // Get by MEF contract string "Vim.IVim"; object avoids a Vim.Core.dll reference.
                _vim = componentModel.DefaultExportProvider
                    .GetExportedValues<object>(VimContractName)
                    .FirstOrDefault();

                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim integration: {(_vim != null ? "detected" : "not found")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}VsVim integration resolve failed: {ex.Message}");
                _vim = null;
            }

            return _vim;
        }

        private IComponentModel? GetComponentModel()
        {
            if (_componentModel != null)
            {
                return _componentModel;
            }

            try
            {
                _componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            }
            catch
            {
                _componentModel = null;
            }

            return _componentModel;
        }
    }
}
