---
name: vs-extension-dev
description: Use when working on this Visual Studio extension (VSIX) project — "MyExtension", "CardinalNavigation", window navigation, VS window matrix, global keyboard hook, leader key bindings, InputHandler, IVsWindowFrame, DTE, IVsUIShell. Covers the architecture and conventions of the MyExtension codebase.
---

# MyExtension (VSIX) Development

This is a Visual Studio extension (VSIX) built with the C#/.NET SDK-style project
format. It implements **Cardinal-style window navigation** and a **leader-key
keyboard binding system** for Visual Studio.

## Architecture

The extension is a single `AsyncPackage` (`MyExtensionPackage`) that installs a
Win32 low-level keyboard hook on load and disposes it on package dispose.

Data flow (key press → window move):

```
GlobalKeyboardHook (Win32 LL hook)
  → InputHandler.HandleKey(key, ctrl, shift, alt)
      → _bindings[sequence] → Navigate(direction)
          → WindowMatrix.NavigateInDirection(direction)
              → reduce/filter candidate windows
              → activate best match
```

### Key files

| File | Responsibility |
|------|----------------|
| `MyExtensionPackage.cs` | `AsyncPackage` entry point. Installs/disposes the keyboard hook. |
| `GlobalKeyboardHook.cs` | Win32 low-level keyboard hook (`WH_KEYBOARD_LL`). Owns the P/Invoke surface and output-window logging. |
| `InputHandler.cs` | Maps key sequences to actions. Where you add/change key bindings. |
| `CardinalMovment/WindowMatrix.cs` | Core navigation algorithm: filters windows by direction, alignment, adjacency, and closest distance. |
| `CardinalMovment/WindowControlAdapter.cs` | Bridges an `IVsWindowFrame` (IVs shell) to an `EnvDTE.Window` (DTE automation). |
| `CardinalMovment/IVsFrameView.cs` | Wraps `IVsWindowFrame` (+ `IVsWindowFrame4`) for screen-rect / visibility queries. |
| `CardinalMovment/IVsUIWindowFrameExtractor.cs` | Enumerates tool + document window frames from `IVsUIShell`. |
| `CardinalMovment/UtilityMethods.cs` | DTE / `IVsUIShell` service access and window comparison/linking helpers. |
| `CardinalMovment/CardinalNavigationConstants.cs` | Direction chars, DPI/divide tuning constants, repeated strings. |
| `CardinalMovment/RectCoordinate.cs` | Simple int `x, y, width, height` rect value object. |
| `CardinalMovment/LinqExtensionMethods.cs` | `DistinctBy` LINQ helper (used because target framework lacks it). |

## Non-obvious facts & gotchas

- **Two window APIs are used together.** The IVs shell API (`IVsWindowFrame`,
  `IVsUIShell`) provides precise on-screen geometry via `GetWindowScreenRect`;
  the DTE automation (`EnvDTE.Window`) provides activation (`window.Activate()`)
  and framing (`LinkedWindowFrame`). `WindowControlAdapter` pairs them.
- **Thread affinity is mandatory.** Almost every IVs/DTE call must be on the UI
  thread. The hook callback marshals to the main thread with
  `ThreadHelper.JoinableTaskFactory.Run(...)` + `SwitchToMainThreadAsync()`, and
  nearly every method starts with `ThreadHelper.ThrowIfNotOnUIThread()`. Keep
  this discipline — add it to any new VS API method. Never call VS objects from
  a background thread.
- **`Coordinates` is recomputed each access** via `GetScreenDisplayCoordinates()`
  — it is not a cached snapshot. Reading it repeatedly reflects live window
  positions.
- **Hidden/tabbed windows are filtered out** before distance computation
  (`RemoveHiddenOrTabbedWindows`), since their screen rect reads `0,0,0,0`.
- **DPR/DPI matters.** `SetWindowDivideSelectionSizes()` scales the
  "divide" tolerance constants by the system DPI scale factor
  (`DpiAwareness.SystemDpiX / DefaultLogicalDpi`). Tune the logical constants in
  `CardinalNavigationConstants` (`DefaultLogicalXWindowDivide`,
  `DefaultLogicalYWindowDivide`, `DefaultLogicalTabPaneDivide`,
  `DefaultLogicalSelectorScale`), not the raw pixel values.
- **The leader key is Space.** `InputHandler.LeaderKey = Keys.Space`. Flat
  (`KeyDown`) handlers consume the key by making `HookCallback` return `(IntPtr)1`
  — that **blocks the key** from reaching VS. Returning `0`/`CallNextHookEx`
  lets it fall through.
- **`IsVisualStudioFocused()`** gates the hook: it only processes keys when the
  foreground process belongs to this VS instance.

## Adding a key binding

Bindings live in `InputHandler._bindings` (a
`Dictionary<string, Action>` with `OrdinalIgnoreCase` comparison). Two forms:

```csharp
// Plain single key with modifiers — matched via BuildSimpleKey() → "Ctrl+H" etc.
["Ctrl+H"] = () => Navigate(CardinalNavigationConstants.LEFT),

// Leader sequence — after Space, keys joined with "," → "W,H" etc.
["W,L"] = () => Navigate(CardinalNavigationConstants.RIGHT),
```

Directions come from `CardinalNavigationConstants`: `LEFT`(L), `RIGHT`(R),
`UP`(U), `DOWN`(D). To add a new action, add its `Action` to the dictionary keyed
by the sequence string. Sequence timeout / prefix-resolution logic is already
handled in `HandleKey`.

## The navigation algorithm (WindowMatrix)

`NavigateInDirection` → `ReduceWindowsAndSelectActive`, which, in order:

1. `RemoveHiddenOrTabbedWindows()` — drop windows at rect `0,0,0,0`.
2. `RemoveWindowsInWrongDirection(direction)` — keep only windows strictly in the
   requested direction.
3. `RemoveWindowsNotAligned(direction)` — axis overlap with the active window.
4. `RemoveWindowsByClosestAdjacency(direction)` — nearest window within the DPI
   divide.
5. `SortByLargestAdjacency(direction)` — tie-break by largest shared edge.
6. Activate `m_ActiveWindows.First()`.

All four "Remove..." steps are direction-parameterized with a local
`filterFunction`. When adding a filter, follow that pattern and guard with
`ThreadHelper.ThrowIfNotOnUIThread()` inside the predicate.

> The algorithm currently takes only the **closest** window. It does not
> implement "jump" or chained-movement behavior — extend `WindowMatrix` here if
> needed.

## Build / toolchain

- **Target framework:** `net472` (.NET Framework 4.7.2) — modern .NET APIs may be
  unavailable; watch for missing LINQ/BCL members (hence the hand-rolled
  `DistinctBy`).
- **LangVersion:** `14`, `Nullable: enable`.
- **Packages:** `Microsoft.VisualStudio.SDK` (17.14), `Microsoft.VSSDK.BuildTools`
  (18.8), `MessagePack` (3.1.8). `ExcludeAssets="runtime"` on the SDK package
  (VS provides the runtime at load time).
- **VSIX target:** Visual Studio Community 17.14+ (see
  `source.extension.vsixmanifest`).
- **Namespaces:** `MyExtension` (package/hook/handler) and `CardinalNavigation`
  (window logic). Note the typo `CardinalMovment` (folder + namespace files) is
  intentional/legacy — keep it consistent with the existing folders.

## Testing the extension

There is no unit-test project. Verification happens by running the VSIX:
build, then F5 to launch an Experimental Instance of Visual Studio, install the
hook, and exercise bindings. Watch the **"NeoVisual" output window pane** (see
`GlobalKeyboardHook` / `InputHandler` logging). Debug logging also goes to
`Debug.WriteLine` (visible under VS > Debug > Output or a debugger).
