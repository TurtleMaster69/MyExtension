---
name: dotnet-pinvoke
description: Use when writing, reviewing, or debugging P/Invoke / native interop in this repo. Covers function signatures, string/struct marshalling, memory lifetime, SafeHandle. Deep reference at dotnet/skills dotnet-pinvoke.
license: MIT
compatibility: opencode
---
# .NET P/Invoke (pointer)

Correct native interop for THIS repo's heavy P/Invoke surface. Deep reference: `dotnet/skills` `plugins/dotnet/skills/dotnet-pinvoke`.

## When to use
- Writing/reviewing a `[DllImport]` (net472 → always `DllImport`, never `LibraryImport`).
- Debugging `AccessViolationException`, `DllNotFoundException`, or silent corruption at the managed/native boundary.

## Core method
- **Signature** — match the C function's argument types exactly (use `uint` for unsigned int, correct calling convention). Wrong types = silent corruption.
- **Marshalling** — set `CharSet`, `SetLastError`, correct `MarshalAs`; verify string encoding (this repo: `keybd_event`, `GetAsyncKeyState` (returns short — mask `& 0x8000`), `GetWindowRect` struct layout, `SetWindowsHookEx` delegate kept as a field so the GC can't collect it).
- **Lifetime** — keep the hook delegate alive (GC), `GC.KeepAlive`, `SafeHandle` for handles, never free borrowed memory.
- **Delegates** — callbacks passed to unmanaged code must be rooted and not garbage-collected mid-call.

## Source
Load `dotnet/skills` `dotnet-pinvoke` only if the method above needs the full checklist.
