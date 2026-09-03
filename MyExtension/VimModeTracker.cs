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
    /// VsVim has a buffer for the view, subscribe to <c>IVimBuffer.SwitchedMode</c>. Mode
    /// changes arrive as events and update a cached <see cref="IsInTypingMode"/> boolean.
    /// <see cref="IsInTypingMode"/> is then a pure in-memory read — no COM, no reflection, no
    /// TTL window.
    ///
    /// <para/>
    /// <b>Interop model:</b> VsVim publishes its engine as a MEF export (the <c>Vim.IVim</c>
    /// interface, assembly <c>Vim.Core.dll</c>). We resolve it reflectively by contract string
    /// <c>"Vim.IVim"</c>, then read <c>IVimBuffer</c> members via <see cref="Type.GetInterfaceMap"/>.
    /// VsVim implements its interfaces explicitly, so plain <c>GetMethod(name)</c> returns null;
    /// events are reached as <c>add_&lt;Name&gt;</c>/<c>remove_&lt;Name&gt;</c> interface methods,
    /// and event delegates are built from the interface's <c>EventHandlerType</c>. We cache the
    /// resolved <see cref="MethodInfo"/> and delegate handles.
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
        // Values of the Vim.ModeKind enum (Vim.Core). We only distinguish typing modes.
        private const int Insert = 2;
        private const int Replace = 7;

        // MEF contract name ([Export(typeof(IVim))] -> full type name) and full interface
        // names used for InterfaceMapping.
        private const string VimContractName = "Vim.IVim";
        private const string IVimFullName = "Vim.IVim";
        private const string IVimBufferFullName = "Vim.IVimBuffer";
        private const string IModeFullName = "Vim.IMode";

        // VsVim's engine, lazily resolved once and cached for the process lifetime.
        private object? _vim;
        private bool _resolved;

        // Cached MethodInfo / delegate handles (never re-run InterfaceMapping after first use).
        private MethodInfo? _tryGetVimBufferMethod;
        private MethodInfo? _addSwitchedModeMethod;
        private MethodInfo? _removeSwitchedModeMethod;
        private MethodInfo? _getModeKindMethod;             // IVimBuffer.ModeKind
        private MethodInfo? _addClosedMethod;               // IVimBuffer.add_Closed
        private MethodInfo? _getIModeKindMethod;            // IMode.ModeKind

        private Delegate? _switchedModeDelegate;            // EventHandler<SwitchModeEventArgs>
        private readonly EventHandler _bufferClosedDelegate; // EventHandler (System)

        private IComponentModel? _componentModel;

        // The buffer we are currently subscribed to (UI thread only).
        private object? _currentBuffer;

        // The single cached answer. Volatile because it is read by the keyboard path while
        // events mutate it on the UI thread.
        private volatile bool _cachedTyping;

        public VimModeTracker()
        {
            _bufferClosedDelegate = OnBufferClosed;
        }

        /// <summary>True when the focused editor buffer is in Insert/Replace (typing) mode.</summary>
        public bool IsInTypingMode => _cachedTyping;

        /// <summary>
        /// Called by the editor for every new code text view (UI thread). We attach focus and
        /// closed handlers; the vim buffer is resolved lazily on focus so VsVim has a chance to
        /// create it first.
        /// </summary>
        public void TextViewCreated(IWpfTextView view)
        {
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
            // separately by InputHandler.
            _cachedTyping = false;
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            // Editor view closed: detach handlers so nothing leaks. The buffer's own Closed
            // event (handled in OnBufferClosed) detaches the SwitchedMode subscription.
            if (sender is ITextView view)
            {
                view.GotAggregateFocus -= OnViewGotFocus;
                view.LostAggregateFocus -= OnViewLostFocus;
                view.Closed -= OnViewClosed;
            }

            _cachedTyping = false;
        }

        /// <summary>Subscribes to a buffer's SwitchedMode event (and its Closed event for cleanup).</summary>
        private void SubscribeBuffer(object buffer)
        {
            if (ReferenceEquals(buffer, _currentBuffer))
            {
                // Already subscribed to this buffer; just refresh the cached mode.
                UpdateTypingFromMode(GetModeKind(buffer));
                return;
            }

            // Unsubscribe from any previous buffer before moving to the new one.
            UnsubscribeBuffer(_currentBuffer);

            _currentBuffer = buffer;

            try
            {
                _addSwitchedModeMethod ??= MapInterfaceMethod(buffer, IVimBufferFullName, "add_SwitchedMode");
                if (_addSwitchedModeMethod != null)
                {
                    _switchedModeDelegate ??= BuildSwitchedModeDelegate(buffer);
                    if (_switchedModeDelegate != null)
                    {
                        _addSwitchedModeMethod.Invoke(buffer, new object[] { _switchedModeDelegate });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] VsVim SwitchedMode subscribe failed: {ex.Message}");
            }

            try
            {
                _addClosedMethod ??= MapInterfaceMethod(buffer, IVimBufferFullName, "add_Closed");
                _addClosedMethod?.Invoke(buffer, new object[] { _bufferClosedDelegate });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] VsVim Closed subscribe failed: {ex.Message}");
            }

            UpdateTypingFromMode(GetModeKind(buffer));
        }

        private void UnsubscribeBuffer(object? buffer)
        {
            if (buffer == null || _switchedModeDelegate == null)
            {
                return;
            }

            try
            {
                _removeSwitchedModeMethod ??= MapInterfaceMethod(buffer, IVimBufferFullName, "remove_SwitchedMode");
                _removeSwitchedModeMethod?.Invoke(buffer, new object[] { _switchedModeDelegate });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] VsVim SwitchedMode unsubscribe failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds a delegate of the exact type the buffer's SwitchedMode event expects
        /// (<c>EventHandler&lt;SwitchModeEventArgs&gt;</c>), bound to our handler. This is
        /// required because reflection invocation must hand the add method a delegate of the
        /// precise handler type.
        /// </summary>
        private Delegate? BuildSwitchedModeDelegate(object buffer)
        {
            try
            {
                Type? interfaceType = buffer.GetType().GetInterfaces()
                    .FirstOrDefault(t => t.FullName == IVimBufferFullName);
                System.Reflection.EventInfo? ev = interfaceType?.GetEvent("SwitchedMode");
                if (ev?.EventHandlerType == null)
                {
                    return null;
                }

                return Delegate.CreateDelegate(ev.EventHandlerType, this, nameof(OnSwitchedMode));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] VsVim SwitchedMode delegate build failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Raised by VsVim whenever the buffer's mode changes (UI thread).</summary>
        private void OnSwitchedMode(object sender, EventArgs e)
        {
            int? mode = GetCurrentModeKindFromEventArgs(e);
            UpdateTypingFromMode(mode);
        }

        private void OnBufferClosed(object sender, EventArgs e)
        {
            // The buffer is gone; make sure we no longer reference or subscribe to it.
            if (ReferenceEquals(sender, _currentBuffer))
            {
                _currentBuffer = null;
                _cachedTyping = false;
            }
        }

        /// <summary>Updates the cached typing flag from a ModeKind value.</summary>
        private void UpdateTypingFromMode(int? mode)
        {
            _cachedTyping = mode == Insert || mode == Replace;
        }

        /// <summary>Extracts the new mode from a SwitchedMode event's args.</summary>
        private int? GetCurrentModeKindFromEventArgs(EventArgs e)
        {
            try
            {
                // SwitchModeEventArgs.CurrentMode (public property on the sealed class) -> IMode.
                object? currentMode = e.GetType().GetProperty("CurrentMode")?.GetValue(e);
                if (currentMode == null)
                {
                    return null;
                }

                // IMode is an explicitly-implemented interface; ModeKind must be mapped.
                _getIModeKindMethod ??= MapInterfaceMethod(currentMode, IModeFullName, "get_ModeKind");
                object? mode = _getIModeKindMethod?.Invoke(currentMode, null);
                return mode == null ? (int?)null : Convert.ToInt32(mode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] VsVim SwitchedMode read failed: {ex.Message}");
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
                _tryGetVimBufferMethod ??= MapInterfaceMethod(vim, IVimFullName, "TryGetVimBuffer");
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
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] TryGetVimBuffer failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Reads a buffer's ModeKind enum value, or null if it can't be determined.</summary>
        private int? GetModeKind(object buffer)
        {
            if (buffer == null)
            {
                return null;
            }

            try
            {
                _getModeKindMethod ??= MapInterfaceMethod(buffer, IVimBufferFullName, "get_ModeKind");
                if (_getModeKindMethod == null)
                {
                    return null;
                }

                object mode = _getModeKindMethod.Invoke(buffer, null);
                return mode == null ? (int?)null : Convert.ToInt32(mode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] VsVim ModeKind read failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Resolves the concrete implementation of a named interface method (explicit impl).</summary>
        private static MethodInfo? MapInterfaceMethod(object target, string interfaceFullName, string methodName)
        {
            Type? interfaceType = target.GetType().GetInterfaces()
                .FirstOrDefault(t => t.FullName == interfaceFullName);
            if (interfaceType == null)
            {
                return null;
            }

            // GetInterfaceMap pairs each interface MethodInfo with the concrete (often private)
            // method that implements it. We match by the interface method's name.
            var map = target.GetType().GetInterfaceMap(interfaceType);
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (map.InterfaceMethods[i].Name == methodName)
                {
                    return map.TargetMethods[i];
                }
            }

            return null;
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

                System.Diagnostics.Debug.WriteLine($"[NeoVisual] VsVim integration: {(_vim != null ? "detected" : "not found")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NeoVisual] VsVim integration resolve failed: {ex.Message}");
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
