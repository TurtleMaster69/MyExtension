using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Linq;
using System.Reflection;

namespace MyExtension
{
    /// <summary>
    /// Integrates with the VsVim 2022 extension to read the current Vim editing mode.
    ///
    /// <para/>
    /// <b>Why this exists:</b> our leader key is Space. In Vim normal mode Space is the leader,
    /// but in insert mode it must type a literal space. To decide, we ask VsVim what mode the
    /// focused editor buffer is in.
    ///
    /// <para/>
    /// <b>Interop model:</b> VsVim publishes its engine as a MEF export (the <c>Vim.IVim</c>
    /// interface, from assembly <c>Vim.Core.dll</c>). VS has one shared MEF container
    /// (accessed via <see cref="IComponentModel"/> / the SComponentModel service), so we can
    /// discover VsVim's service without any compile-time reference. We resolve it reflectively
    /// by contract string <c>"Vim.IVim"</c>, then read <c>IVimBuffer.ModeKind</c>.
    ///
    /// <para/>
    /// <b>Explicit-interface-implementation gotcha:</b> VsVim implements <c>IVim</c> explicitly,
    /// so its methods are private on the concrete type (e.g. <c>Vim.IVim.TryGetVimBuffer</c>) and
    /// <c>GetMethod("TryGetVimBuffer")</c> returns null. The correct way is
    /// <see cref="Type.GetInterfaceMap"/> on the concrete type, which maps each interface method
    /// to its private implementation. We cache those resolved <see cref="MethodInfo"/> handles
    /// (mapping every member of the interface is expensive, and this runs on every leader press).
    ///
    /// <para/>
    /// <b>Reliability note:</b> the active text view is obtained via
    /// <c>IVsTextManager2.GetActiveView2(fMustHaveFocus:0)</c> + the editor adapter (reliable),
    /// <i>not</i> <c>IVim.FocusedBuffer</c> (which VsVim does not keep updated — that road led to
    /// the insert-mode bug). Focus is determined with <c>ITextView.HasAggregateFocus</c>.
    ///
    /// <para/>
    /// <b>Threading:</b> everything here touches VS/MEF objects and must run on the UI thread.
    /// </summary>
    internal sealed class VsVimIntegration
    {
        // Values of the Vim.ModeKind enum (Vim.Core). We only distinguish typing modes.
        private const int Insert = 2;
        private const int Replace = 7;

        // MEF contract name ([Export(typeof(IVim))] -> full type name) and full interface
        // names used for InterfaceMapping.
        private const string VimContractName = "Vim.IVim";
        private const string IVimFullName = "Vim.IVim";
        private const string IVimBufferFullName = "Vim.IVimBuffer";

        private readonly AsyncPackage _package;

        // VsVim's engine, lazily resolved once and cached for the process lifetime.
        private object? _vim;
        private bool _resolved;

        // Cached MethodInfo handles (never re-run InterfaceMapping after first resolution).
        private MethodInfo? _tryGetVimBufferMethod;
        private MethodInfo? _getModeKindMethod;

        // Cached VS services (avoid a service lookup on each key press).
        private IVsTextManager2? _textManager;
        private IComponentModel? _componentModel;
        private IVsEditorAdaptersFactoryService? _editorAdapter;

        // Time-based cache of the last resolved active view. The active view only changes on a
        // real focus switch, yet GetActiveView2 is a COM round-trip that hjkl/Space/Ctrl+N/P run
        // on every press. A short TTL collapses that COM call during fast typing, with staleness
        // small enough (150ms) to be imperceptible. Environment.TickCount is an int (ms since
        // boot, wraps ~every 24.9 days); the unchecked delta keeps the TTL correct across wrap.
        private ITextView? _cachedView;
        private int _cachedViewTick;
        private bool _viewCached;

        // Cached IsInTypingMode() result. Every Space key consults the mode; the actual
        // computation (GetActiveView2 COM + two reflection Invokes into VsVim) is the expensive
        // part, so we memoize the boolean for the same TTL window. Insert mode changes only on
        // i/Esc/etc., making 150ms staleness effectively invisible.
        private bool _cachedTyping;
        private int _cachedTypingTick;
        private bool _typingCached;

        private const int ActiveViewCacheTtlMs = 150;

        public VsVimIntegration(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>True when the focused editor buffer is in Insert/Replace (typing) mode.</summary>
        public bool IsInTypingMode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Fast path: this runs on every Space press; only re-consult VsVim once per TTL
            // window. The cached value is fresh whenever the user is actively typing because
            // each Space refreshes it (cache-true stays true while in insert mode).
            int now = Environment.TickCount;
            if (_typingCached && unchecked(now - _cachedTypingTick) < ActiveViewCacheTtlMs)
            {
                return _cachedTyping;
            }

            bool result = ComputeTypingMode();

            _cachedTyping = result;
            _cachedTypingTick = now;
            _typingCached = true;
            return result;
        }

        /// <summary>Performs the full (expensive) mode consultation against VsVim.</summary>
        private bool ComputeTypingMode()
        {
            ITextView? view = GetActiveView();
            if (view == null)
            {
                return false; // no active editor view -> not typing in an editor
            }

            object? buffer = GetBufferForView(view);
            if (buffer == null)
            {
                return false; // VsVim has no buffer for this view -> not a VsVim editor
            }

            int? mode = GetModeKind(buffer);
            return mode == Insert || mode == Replace;
        }

        /// <summary>True when keyboard focus is in a code editor (used to gate hjkl / Ctrl+N/P).</summary>
        public bool IsInEditor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // HasAggregateFocus is true when the view (or an adornment like the completion list)
            // has focus — the reliable "am I editing right now" test.
            return GetActiveView()?.HasAggregateFocus == true;
        }

        /// <summary>Gets the active code editor as an ITextView (bridged from the IVs text view).</summary>
        private ITextView? GetActiveView()
        {
            // Fast path: reuse the view resolved within the last 150ms. This is on the hot path
            // (h/j/k/l and Space), so skipping the COM call here eliminates most of the per-key
            // latency. A null result is cached too, so "no editor open" doesn't re-query either.
            int now = Environment.TickCount;
            if (_viewCached && unchecked(now - _cachedViewTick) < ActiveViewCacheTtlMs)
            {
                return _cachedView;
            }

            IVsTextManager2 textManager = GetTextManager();
            ITextView? view = null;

            if (textManager != null)
            {
                // fMustHaveFocus:0 returns the last-active code view even when focus has moved to
                // a tool window — which is fine here; HasAggregateFocus (above) refines that.
                if (ErrorHandler.Succeeded(textManager.GetActiveView2(
                    fMustHaveFocus: 0,
                    pBuffer: null,
                    grfIncludeViewFrameType: (uint)_VIEWFRAMETYPE.vftCodeWindow,
                    ppView: out IVsTextView vsView)))
                {
                    // The editor adapter is the standard IVs <-> WPF bridge (returns IWpfTextView).
                    view = GetEditorAdapter()?.GetWpfTextView(vsView);
                }
            }

            _cachedView = view;
            _cachedViewTick = now;
            _viewCached = true;
            return view;
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

                object[] args = new object[] { view, null }; // { input, out-slot }
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
                IComponentModel componentModel = GetComponentModel();
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

        // ---- lazy-cached service accessors ----

        private IVsTextManager2? GetTextManager() =>
            _textManager ?? (_textManager = GetService(typeof(SVsTextManager)) as IVsTextManager2);

        private IComponentModel? GetComponentModel() =>
            _componentModel ?? (_componentModel = GetService(typeof(SComponentModel)) as IComponentModel);

        private IVsEditorAdaptersFactoryService? GetEditorAdapter() =>
            _editorAdapter ?? (_editorAdapter = GetComponentModel()?.GetService<IVsEditorAdaptersFactoryService>());

        // AsyncPackage surfaces VS services via IServiceProvider (explicitly implemented).
        private object? GetService(Type serviceType) =>
            ((System.IServiceProvider)_package).GetService(serviceType);
    }
}
