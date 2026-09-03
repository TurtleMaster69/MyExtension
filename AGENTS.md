# AGENTS.md

Visual Studio extension (VSIX) implementing Cardinal-style window navigation and
leader-key keyboard bindings. Single project: `MyExtension/MyExtension.csproj`,
opened via `MyExtension.slnx`.

More detailed architecture lives in `.opencode/skills/vs-extension-dev/SKILL.md`;
read it before making changes. This file only covers what's easy to get wrong.

## Build & run

- `dotnet build` (or build in VS). This is a VSIX — a plain `dotnet run` does not work.
- **Test by running in the VS Experimental Instance**: F5 (or `Start` with the
  csproj) launches VS with the extension loaded. There is **no test project** and
  no `test`/`lint`/`typecheck` target.
- Debug log output: `Debug.WriteLine` plus a custom **"NeoVisual"** VS Output
  window pane (created in `GlobalKeyboardHook`). Look there for hook/input logs.

## Hard requirements that are easy to violate

- **UI-thread affinity is mandatory.** Nearly every `IVs*` / `DTE` / `EnvDTE` call
  must run on the main thread. Almost every method begins with
  `ThreadHelper.ThrowIfNotOnUIThread()`. The keyboard hook marshals to the UI
  thread via `ThreadHelper.JoinableTaskFactory.Run(...)` +
  `SwitchToMainThreadAsync()`. Never touch VS objects from a background thread;
  add `ThrowIfNotOnUIThread()` to any new VS API method.
- **Target framework is `net472`** (not modern .NET). Avoid .NET 5+/BCL-only APIs;
  the repo hand-rolls `DistinctBy` for this reason. `LangVersion` 14, `Nullable` enabled.
- `Microsoft.VisualStudio.SDK` is referenced with `ExcludeAssets="runtime"` — VS
  supplies it at load time; do not expect SDK assemblies in the build output.

## Key architecture / gotchas

- Data flow: `GlobalKeyboardHook` (Win32 `WH_KEYBOARD_LL`) → `InputHandler.HandleKey`
  → `_bindings` lookup → `WindowMatrix.NavigateInDirection`.
- **Leader key is Space by default**, and bindings are **user-configurable** via an
  external file at `%APPDATA%\MyExtension\keybindings.json`. It is **not created
  automatically** — it's only read if it exists, and merged over the built-in
  defaults in `MyExtension/default-keybindings.json` (embedded resource). Action
  names are resolved in `InputHandler.ResolveAction` (`navigate-left` etc.);
  `command:<VsCommandName>` runs any VS command by name (this is how the
  LazyVim-style leader bindings like `w`→save are wired). To add a *new built-in
  action*, add a case there and a line in `default-keybindings.json`.
- Handled keys are *blocked* from VS by returning `(IntPtr)1` from the hook callback.
- VsVim 2022 mode-awareness: `VimModeTracker` (a shared MEF part) tracks the focused
  editor's mode **event-driven** — no per-keystroke polling. It is an
  `IWpfTextViewCreationListener` (`[ContentType("text")]`), so VS calls `TextViewCreated`
  for every code view; it subscribes to each view's `Got/LostAggregateFocus`/`Closed` and
  to the focused buffer's `IVimBuffer.SwitchedMode` event, updating a cached
  `IsInTypingMode` boolean (`InputHandler.IsTyping()` reads it; Insert=2/Replace=7 gate
  the leader key). `InputHandler` resolves the same singleton from the MEF container
  (`IComponentModel.DefaultExportProvider.GetExportedValue<VimModeTracker>()`).
  Interop: resolve `Vim.IVim` via MEF contract `"Vim.IVim"` + reflection (assembly
  `Vim.Core.dll`; no compile-time dependency and no committed third-party binaries). Get
  the buffer with the **non-creating** `IVim.TryGetVimBuffer(view, out buffer)`; read mode
  via `get_ModeKind()`/`IMode.get_ModeKind()`; subscribe via `add_SwitchedMode`.
  Because VsVim implements its interfaces **explicitly**, reflection must resolve members
  via `Type.GetInterfaceMap` (plain `GetMethod(name)` returns null), and event delegates
  must be built from the interface's `EventHandlerType` (an `EventHandler<object>` won't
  bind to the add method). Do **not** rely on `IVim.FocusedBuffer` (not tracked reliably),
  `GetActiveView2(fMustHaveFocus:1)` (true even when a tool window has focus), or UI
  Automation (`AutomationElement.FocusedElement`, which throws "application is in a broken
  state").
- Editor list-navigation: `PopupNavigation` maps `Ctrl+N`/`Ctrl+P` to injected
  Down/Up arrow keys (which the completion/quick-action/peek lists consume — not
  `Edit.LineDown`, which moves the caret), gated on `_windowManager.IsToolWindow()`. To
  stop VS dimming the completion list while Ctrl is held, `InputHandler` swallows the
  Ctrl key-down itself when `IsCompletionActive()` (via MEF `ICompletionBroker`).
- Tool-window `hjkl` navigation: `ToolWindowNavigation` translates `j/k/h/l`→arrow
  keys (via `KeyInjection`/`keybd_event`). Focus is classified in-process with WPF
  `Keyboard.FocusedElement` (inject unless focus is a `TextBoxBase` text input or a
  VsVim editor). Never mid leader-sequence.
- **The hook runs on the UI thread** (`GlobalKeyboardHook`), NOT a dedicated thread.
  A cheap pre-filter (`IsInteresting`) skips the handler for plain typing keys;
  only interesting keys (modifier chords, leader/Escape, hjkl, Ctrl keys, or any key
  while `InputHandler.IsLeaderActive` is true) reach `HandleKey`, which runs inline
  on the UI thread. Do **not** move the hook off the UI thread: a dedicated-thread
  variant caused the injected arrow keys to interleave badly with the physical key
  stream (Solution Explorer type-ahead double-fired), and a marshal at default
  Dispatcher priority got starved by input (keys drained seconds behind typing).
  `IsLeaderActive` is a volatile bool read by the pre-filter, mutated only on the
  UI thread. `Log` uses `OutputStringThreadSafe` (pane created eagerly on the UI
  thread); never add per-key logging back to the callback.
- Namespaces: `MyExtension` (package/hook/handler) and `CardinalNavigation`
  (window logic). The source folder is spelled **`CardinalMovment`** (typo) — keep
  it consistent, do not "fix" it (breaks references).
- Two window APIs are used together: `IVsWindowFrame`/`IVsUIShell` for on-screen
  geometry (`GetWindowScreenRect`), `EnvDTE.Window` for activation
  (`window.Activate()`) and framing (`LinkedWindowFrame`). `WindowControlAdapter`
  pairs them; don't assume the DTE object identity matches the IVs frame.
- Navigation tolerance divides are DPI-scaled; tune the logical constants in
  `CardinalNavigationConstants`, not raw pixel values.
- VSIX install target: Visual Studio Community 17.14+ (amd64).
