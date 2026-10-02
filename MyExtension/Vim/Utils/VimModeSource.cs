using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Text.Editor;
using MyExtension.Package;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MyExtension.Vim
{
    /// <summary>
    /// The seam that supplies the focused editor's Vim mode to <see cref="VimModeTracker"/>.
    /// The tracker consumes <see cref="GetModeKind"/> and the <see cref="ModeChanged"/> event and
    /// never touches VsVim itself; the production implementation (<see cref="VsVimModeSource"/>)
    /// owns the VsVim reflection interop, and tests inject a fake.
    /// </summary>
    internal interface IVimModeSource
    {
        int? GetModeKind(ITextView view);
        event Action<int?>? ModeChanged;
        void Attach(ITextView view);
        void Detach(ITextView view);
    }

    /// <summary>
    /// Production <see cref="IVimModeSource"/>: resolves VsVim's <c>IVim</c> export reflectively,
    /// subscribes to the focused buffer's <c>SwitchedMode</c> event, and raises
    /// <see cref="ModeChanged"/> with the new ModeKind. The VsVim reflection interop moved here
    /// from <see cref="VimModeTracker"/> so the tracker stays a thin adapter.
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
    /// <b>Threading:</b> every editor/VsVim event here fires on the UI thread.
    ///
    /// <para/>
    /// <b>Degradation:</b> if VsVim is not installed, or a view has no Vim buffer, the mode is
    /// reported as null (the tracker's safe "not typing" default).
    /// </summary>
    internal sealed class VsVimModeSource : IVimModeSource
    {
        // MEF contract name ([Export(typeof(IVim))] -> full type name) and full interface
        // names used for interface dispatch.
        private const string VimContractName = "Vim.IVim";
        private const string IVimFullName = "Vim.IVim";
        private const string IVimBufferFullName = "Vim.IVimBuffer";
        private const string IVimTextBufferFullName = "Vim.IVimTextBuffer";

        // Cached MethodInfo / delegate handles for reflection dispatch.
        private MethodInfo? _tryGetVimBufferMethod;          // IVim.TryGetVimBuffer
        private MethodInfo? _addSwitchedModeMethod;          // IVimTextBuffer.add_SwitchedMode
        private MethodInfo? _removeSwitchedModeMethod;       // IVimTextBuffer.remove_SwitchedMode
        private MethodInfo? _getTextBufferModeKindMethod;    // IVimTextBuffer.get_ModeKind
        private MethodInfo? _addClosedMethod;                // IVimBuffer.add_Closed

        private Delegate? _switchedModeDelegate;             // EventHandler<SwitchModeKindEventArgs>
        private readonly EventHandler _bufferClosedDelegate; // EventHandler (System)

        private IComponentModel? _componentModel;

        // The resolved VsVim IVim export and its resolution latch. The latch is set ONLY on a
        // successful resolution (M17 fix) — a failure leaves it unset so the next call retries.
        private object? _vim;
        private bool _resolved;

        // The IVimBuffer we resolved for the focused view (used for Closed cleanup; UI thread only).
        private object? _currentBuffer;

        // The IVimTextBuffer currently subscribed for SwitchedMode (UI thread only). Vim mode is a
        // per-ITextBuffer concept, so this is what the SwitchedMode event's sender is.
        private object? _currentTextBuffer;

        // CR2: per-view subscription map. Each editor view's Vim buffer is tracked independently so
        // Detach(view) can unsubscribe ONLY that view's buffer without killing the focused view's
        // SwitchedMode subscription (the CR2 bug: closing a non-focused view unsubscribed the single
        // global _currentTextBuffer, leaving the focused view's _cachedTyping stale).
        private readonly VimBufferSubscriptions _subscriptions = new VimBufferSubscriptions();

        // Guards against double-subscribing the same buffer's events when a view is attached and
        // then focused (Attach + GetModeKind both call SubscribeBuffer). Reference-equality sets.
        private readonly HashSet<object> _subscribedTextBuffers = new HashSet<object>();
        private readonly HashSet<object> _subscribedBuffers = new HashSet<object>();

        // m40: reference count of attached views per shared text buffer. Two views can share one
        // ITextBuffer (split views), so Detach must not unsubscribe a text buffer another view
        // still uses. Incremented in Attach, decremented in Detach; unsubscribed only at 0.
        private readonly Dictionary<object, int> _textBufferRefCounts = new Dictionary<object, int>();

        public VsVimModeSource()
        {
            _bufferClosedDelegate = OnBufferClosed;
        }

        /// <summary>Raised with the new ModeKind whenever the focused buffer's mode changes.</summary>
        public event Action<int?>? ModeChanged;

        /// <summary>Returns the current ModeKind for the view's Vim buffer, or null.</summary>
        public int? GetModeKind(ITextView view)
        {
            object? buffer = GetBufferForView(view);
            if (buffer == null)
            {
                return null;
            }
            // On focus gain, make this view's buffer the focused one and (re-)subscribe it (CR2):
            // the focused view's SwitchedMode subscription must survive a non-focused view close.
            // makeCurrent: true — a focus gain is the ONLY path that re-points the focused buffer
            // (m39: a background/peek view must not hijack the focused mode state).
            SubscribeBuffer(buffer, makeCurrent: true);
            return GetTextBufferModeKind(_currentTextBuffer);
        }

        /// <summary>Resolves the view's Vim buffer (if any) and subscribes to its mode events.</summary>
        public void Attach(ITextView view)
        {
            object? buffer = GetBufferForView(view);
            if (buffer != null)
            {
                _subscriptions.Attach(view, buffer);
                // makeCurrent: false — a newly created view is not necessarily focused; subscribe
                // its buffer's events but do NOT re-point the focused buffer (m39).
                SubscribeBuffer(buffer, makeCurrent: false);
                object? textBuffer = GetTextBuffer(buffer);
                if (textBuffer != null)
                {
                    _textBufferRefCounts[textBuffer] = _textBufferRefCounts.TryGetValue(textBuffer, out int c) ? c + 1 : 1;
                }
            }
        }

        /// <summary>Unsubscribes ONLY <paramref name="view"/>'s buffer (CR2: per-view detach).</summary>
        public void Detach(ITextView view)
        {
            object? buffer = _subscriptions.BufferFor(view);
            if (buffer != null)
            {
                object? textBuffer = GetTextBuffer(buffer);
                if (textBuffer != null && _textBufferRefCounts.TryGetValue(textBuffer, out int count))
                {
                    if (count <= 1)
                    {
                        // Last view sharing this text buffer: unsubscribe its SwitchedMode event.
                        _textBufferRefCounts.Remove(textBuffer);
                        UnsubscribeBuffer(textBuffer);
                    }
                    else
                    {
                        // Another view still shares this text buffer — keep the subscription (m40).
                        _textBufferRefCounts[textBuffer] = count - 1;
                    }
                }
            }
            _subscriptions.Detach(view);
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
            ModeChanged?.Invoke(GetModeKindFromEventArgs(e));
        }

        private void OnBufferClosed(object sender, EventArgs e)
        {
            // The buffer is gone; make sure we no longer reference or subscribe to it.
            if (ReferenceEquals(sender, _currentBuffer))
            {
                _currentBuffer = null;
                _currentTextBuffer = null;
            }
            _subscribedBuffers.Remove(sender);
            object? textBuffer = GetTextBuffer(sender);
            if (textBuffer != null)
            {
                _subscribedTextBuffers.Remove(textBuffer);
            }
        }

        /// <summary>
        /// Subscribes to a buffer's SwitchedMode event (and its Closed event for cleanup). Vim
        /// mode is a per-ITextBuffer concept in VsVim, so we subscribe to the
        /// <c>IVimTextBuffer.SwitchedMode</c> event (reached through the <c>IVimBuffer</c> we
        /// resolved for the focused view), which hands us the new ModeKind directly.
        /// </summary>
        private void SubscribeBuffer(object buffer, bool makeCurrent)
        {
            // The IVimTextBuffer is the per-buffer object whose SwitchedMode event we consume.
            object? textBuffer = GetTextBuffer(buffer);

            // Only a focus gain (makeCurrent) re-points the focused buffer; a background/peek
            // view's Attach must not hijack the focused mode state (m39).
            if (makeCurrent)
            {
                _currentBuffer = buffer;
                _currentTextBuffer = textBuffer;
            }
            if (textBuffer == null)
            {
                return;
            }

            // CR2: each view's buffer is subscribed independently — do NOT unsubscribe the previous
            // buffer here (that was the bug). Guard against double-subscribing the same buffer when
            // a view is attached and then focused.
            if (!_subscribedTextBuffers.Add(textBuffer))
            {
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
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim SwitchedMode subscribe failed: {ex.Message}");
            }

            if (!_subscribedBuffers.Add(buffer))
            {
                return;
            }

            try
            {
                _addClosedMethod ??= GetInterfaceMethod(buffer, IVimBufferFullName, "add_Closed");
                _addClosedMethod?.Invoke(buffer, new object[] { _bufferClosedDelegate });
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim Closed subscribe failed: {ex.Message}");
            }
        }

        private void UnsubscribeBuffer(object? textBuffer)
        {
            if (textBuffer == null || _switchedModeDelegate == null)
            {
                return;
            }

            _subscribedTextBuffers.Remove(textBuffer);

            try
            {
                _removeSwitchedModeMethod ??= GetInterfaceMethod(textBuffer, IVimTextBufferFullName, "remove_SwitchedMode");
                _removeSwitchedModeMethod?.Invoke(textBuffer, new object[] { _switchedModeDelegate });
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim SwitchedMode unsubscribe failed: {ex.Message}");
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

                MethodInfo onSwitchedMode = typeof(VsVimModeSource).GetMethod(
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
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim SwitchedMode delegate build failed: {ex.Message}");
                return null;
            }
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
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim SwitchedMode read failed: {ex.Message}");
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
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}TryGetVimBuffer failed: {ex.Message}");
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
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim VimTextBuffer read failed: {ex.Message}");
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
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim ModeKind read failed: {ex.Message}");
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

            try
            {
                IComponentModel? componentModel = GetComponentModel();
                if (componentModel?.DefaultExportProvider == null)
                {
                    return null;
                }

                // Get by MEF contract string "Vim.IVim"; object avoids a Vim.Core.dll reference.
                object? vim = componentModel.DefaultExportProvider
                    .GetExportedValues<object>(VimContractName)
                    .FirstOrDefault();

                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim integration: {(vim != null ? "detected" : "not found")}");
                // m38: latch ONLY on a successful resolution — a "not found" (null) must not
                // permanently latch "no VsVim", so a later resolution can still succeed.
                if (vim != null)
                {
                    _resolved = true;
                    _vim = vim;
                }
                return vim;
            }
            catch (Exception ex)
            {
                Telescope.Logging.NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}VsVim integration resolve failed: {ex.Message}");
                return null;
            }
        }

        private IComponentModel? GetComponentModel()
        {
            if (_componentModel != null)
            {
                return _componentModel;
            }

            try
            {
                _componentModel = VsServices.GlobalComponentModel();
            }
            catch
            {
                _componentModel = null;
            }

            return _componentModel;
        }
    }
}
