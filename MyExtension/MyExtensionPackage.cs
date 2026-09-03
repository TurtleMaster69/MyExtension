using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Telescope;

namespace MyExtension
{
    /// <summary>
    /// The VS package class — the entry point VS loads from the VSIX. A "package" is VS's unit of
    /// an extension: the class under <c>AsyncPackage</c> gets initialized when its auto-load
    /// context triggers, and is where we install the global keyboard hook.
    ///
    /// <para/>
    /// <b>Package registration attributes:</b>
    ///   - <c>[PackageRegistration(AllowsBackgroundLoading = true)]</c> lets VS initialize us on a
    ///     background thread instead of blocking the UI thread during startup.
    ///   - <c>[ProvideAutoLoad(NoSolution, BackgroundLoad)]</c> loads us even with no solution open,
    ///     again on a background thread.
    ///   - <c>[Guid]</c> gives the package a stable identity used in the .pkgdef / shell registration.
    ///
    /// <para/>
    /// <b>Async initialization + UI-thread switch:</b> <see cref="InitializeAsync"/> runs on a
    /// background thread. The keyboard hook and its callback must live on the UI thread (they
    /// marshal to VS objects), so after the base init we <c>await SwitchToMainThreadAsync</c>
    /// before constructing the hook. <see cref="JoinableTaskFactory"/> is the VS-provided
    /// async/threading coordinator that makes this transition correctly (never block on
    /// <c>.Result</c>/<c>.Wait()</c> — that deadlocks the VS UI thread).
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(MyExtensionPackage.PackageGuidString)]
    public sealed class MyExtensionPackage : AsyncPackage
    {
        /// <summary>Stable package identity used by the registration attributes.</summary>
        public const string PackageGuidString = "2f73bf14-6619-47e7-850c-29e95557f429";

        private GlobalKeyboardHook? _keyboardLogger;
        private WindowManager? _windowManager;
        private TelescopeController? _telescope;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Debug.WriteLine("=== Global Keyboard Logger Package STARTED ===");
            await base.InitializeAsync(cancellationToken, progress);

            // The hook must be installed on the UI thread (its callback touches VS objects and
            // runs as part of the UI thread's message pump). Switch off the background init thread.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Wrapped so a bad state (e.g. missing VsVim) can't take the whole package down; the
            // extension degrades to a no-op and we log the reason.
            try
            {
                _telescope = new TelescopeController();
                _telescope.RegisterFinder(new FileFinder(() => CardinalNavigation.UtilityMethods.GetDTE(this)));

                _keyboardLogger = new GlobalKeyboardHook(this, _telescope);
                var monitorSelection = await GetServiceAsync<SVsShellMonitorSelection, IVsMonitorSelection>(throwOnFailure: true, cancellationToken);
                _windowManager = new WindowManager(monitorSelection);

                await RegisterTelescopeCommandAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MyExtension] Failed to initialize keyboard hook: {ex}");
            }
        }

        /// <summary>
        /// Registers the <c>Telescope.Show</c> VS command so the picker can also be invoked from
        /// the command palette / menus, in addition to the leader-key binding.
        /// </summary>
        private async Task RegisterTelescopeCommandAsync(CancellationToken cancellationToken)
        {
            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                return;
            }

            var cmdId = new CommandID(GuidList.CommandSet, CommandList.TelescopeShow);
            var menuItem = new OleMenuCommand((_, _) => OpenTelescope(), cmdId);
            commandService.AddCommand(menuItem);
        }

        private void OpenTelescope()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = CardinalNavigation.UtilityMethods.GetDTE(this);
                var centerRect = GetWindowRect(dte.MainWindow.HWnd);
                _telescope?.Open("Files", centerRect);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MyExtension] Failed to open Telescope: {ex}");
            }
        }

        private static System.Drawing.Rectangle? GetWindowRect(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !GetWindowRectNative(hwnd, out var rect))
            {
                return null;
            }
            return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRectNative(IntPtr hWnd, out NativeRect rect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keyboardLogger?.Dispose();
                _keyboardLogger = null;
                _windowManager?.Dispose();
                _windowManager = null;
                _telescope?.Dispose();
                _telescope = null;
            }

            base.Dispose(disposing);
        }
    }
}
