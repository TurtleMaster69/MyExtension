---
name: debugging-and-error-recovery
description: Use when an e2e scenario, unit test, or build fails after implementation — reproduce the failure, isolate WHICH layer failed (overlay / hook / controller / VsVim mode / harness), and root-cause it before fixing. Adapted from addyosmani debugging-and-error-recovery. Fits the debug-agent's post-e2e role.
license: MIT
compatibility: opencode
---
# Debugging & error recovery (pointer)

Systematic root-cause debugging for a failing e2e scenario / unit test / build. Adapted from `addyosmani` `debugging-and-error-recovery`. Fits `debug-agent`'s job: find WHERE and WHAT caused a failure.

## When to use
- A `tools/harness/test-e2e.ps1` scenario or offline unit test fails (verify-time debug).
- A build breaks (build-time debug).

## Core method (Stop-the-Line)
1. **Make it fail reliably** — if you can't reproduce, you can't fix. Re-run the affected `-Tests <scenario>` subset.
2. **Isolate WHICH layer failed** — for this repo: the WPF overlay (`TelescopeOverlay`), the keyboard hook (`GlobalKeyboardHook`/`InputHandler`), a tool-window controller, VsVim mode detection (`VimModeTracker`), or the harness itself (focus, baseline, `Key.Return`). Read the `[Telescope]`/`[NeoVisual]` log around the failure — it pins the layer.
3. **Classify the failure** — state-dependent (stale overlay, leaked tool-window mode), timing-dependent (injected-key interleaving), environment (fzf missing, VS Experimental state), or a real logic bug.
4. **Root-cause** with evidence (file:line + the failing diagnostic), then fix the cause — never mask it with sleeps/retries/weakened assertions.
5. **Prove it** — re-run the affected scenario; it must pass, and the pre-existing suite must stay green.

## Source
Full triage checklist in `addyosmani/agent-skills` `debugging-and-error-recovery`. Load only if the method above is insufficient.
