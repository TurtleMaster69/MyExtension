# Implementation Plan — Item: `neovisual-editor-insert` marker corruption (test artifact)

> **Lane: bugfix (harness-only).** The scenario failed 2/2 in full runs. Root cause is a
> **test artifact, not a product bug** — the `seed-leak` expected-result `finally`
> mechanism (`:1336-1345`) is unaffected. **M-M7 NOT triggered** — no
> `[Telescope]`/`[NeoVisual]` diagnostic line or format changes; the fix is
> `tools/test-e2e.ps1` only.

**Goal:** `neovisual-editor-insert` must pass deterministically while still proving its
original intent — *insert-mode typing reaches the editor and is not swallowed*.

---

## Root cause (verified live 2026-09-27)

`i` was pressed with the caret at **document position 0** (code context, before the
seeded `// Beta.cs` comment). VS C# IntelliSense therefore auto-popped statement
completion on the first identifier character, and the injected `Space` committed the
camel-case match **`HandleInheritability`**, replacing the marker's `hi`:

```
FAIL: "typed text was swallowed/not inserted (file lacks marker 'hi jk 127').
       Content: HandleInheritability jk 127// Beta.cs"
```

The hook had **correctly routed every key** (`vim-mode=Insert` → `key=H/I/Space/J/K/Space`
→ `Esc` → `vim-mode=Normal` → `Space,W` → `leader-binding executed: W`). The corruption
was buffer-context, not delivery. Layer: **harness (scenario)**.

## Fix

Move the caret into the `// Beta.cs` comment before entering insert mode: two `w`
normal-mode motions (`Send-Tap 0x57`) at `tools/test-e2e.ps1:1313-1329`. The typed marker
then lands in **comment** context, where C# IntelliSense completion never triggers.

**Why this does not weaken the test:** the marker string (`hi jk $run`) and the exact
INSERT-mode keystrokes (`h i Space j k Space`) are unchanged — only the insertion POINT
moved. The assertion (`$content.Contains($marker)`) still requires the FULL marker, so a
genuinely swallowed `Space`/`h`/`j`/`k`/`i` would still fail the scenario.

---

## Acceptance criteria

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | `neovisual-editor-insert` passes deterministically | file content `Contains("hi jk $run")` | e2e `neovisual-editor-insert` (≥2 sequential runs) |
| A2 | The hook still routes every marker key | `vim-mode=Insert` → `vim-mode=Normal` | e2e `neovisual-editor-insert` |
| A3 | `seed-leak` still passes (expected-result `finally`) | `seed-leak: no seeded file was modified` | e2e `seed-leak` |
| A4 | Both unit suites stay green | — | NeoVisual 38, Telescope 56 |
| A5 | NO diagnostic added/changed | diff shows no new `[Telescope]`/`[NeoVisual]` literal | code review at VERIFY |

## Tests
- **`neovisual-editor-insert`** — existing scenario, run ≥2 times sequentially (it was
  deterministically RED).
- Neighbours: `telescope-open-file` (the other Beta.cs opener), `seed-leak`, `seed-reset`.

## RED evidence plan
Reproduced 2/2 in full runs (and a fresh standalone repro); the `[Hook]`/`vim-mode` log
tail proves delivery, and the on-disk `Beta.cs` proves the completion-token corruption.
No unit surface (live VsVim/IntelliSense interaction).

## Known-RED allowlist (for VERIFY)
- `telescope-references` — recorded flake (x1); report if it recurs.
- No other scenario allowlisted; `neovisual-editor-insert` is the target and MUST be GREEN.

---

## Build Plan

> **Lane: bugfix (harness-only).** No `[Telescope]`/`[NeoVisual]` diagnostic added or
> changed (M-M7 NOT triggered). Product code (`Telescope/`, `MyExtension/`) untouched.

**BP-1 — Move the caret into the `// Beta.cs` comment before entering insert mode.**
- **Files:** `tools/test-e2e.ps1` (`neovisual-editor-insert`, the block before `Send-Tap 0x49`).
- **Change:** send two `w` normal-mode motions (`Send-Tap 0x57`) with the scenario's
  existing per-key settle delays, so the caret sits inside the comment; update the
  comment to document the mechanism. Marker string + keystroke sequence unchanged.
- **Verify-with:** `pwsh tools/test-e2e.ps1 -Tests neovisual-editor-insert` GREEN, file
  content contains `hi jk <run>` verbatim.
- **Fails-if:** the marker is still corrupted (completion still fires — the caret is not
  in comment context) or a marker key is genuinely swallowed.

**BP-2 — Confirm the test's meaning is preserved and no side effects.**
- **Files:** none changed.
- **Change:** confirm the marker/keystrokes are byte-identical to HEAD (only the
  insertion point moved), and that `seed-leak`'s expected-result `finally` still writes
  the observed `Beta.cs`.
- **Verify-with:** `git diff` shows only the `w` motions + comment; `seed-leak` GREEN;
  NeoVisual 38/38, Telescope 56/56.
- **Fails-if:** any marker-assertion was loosened, or `seed-leak` regresses.

## Verification Trace

| Failing test/scenario (RED) | Implicated steps | Expected diagnostic / pass signal |
|---|---|---|
| e2e `neovisual-editor-insert` — `file lacks marker 'hi jk N'` (2/2) | BP-1 | `vim-mode=Insert` → `key=H/I/Space/J/K/Space` → `vim-mode=Normal` → `Space,W`; file content contains `hi jk N` |
| e2e `seed-leak` (A3) | BP-2 | `seed-leak: no seeded file was modified during the run` |
| unit suites (A4) | BP-2 | NeoVisual 38/38, Telescope 56/56 |
| no diagnostic added (A5) | BP-2 | `git diff -- MyExtension` empty |

**Known-RED allowlist:** `telescope-references` (flake x1). `neovisual-editor-insert` is
the target and MUST go GREEN — not allowlisted.

---

## Execution Log

### Attempt 1 — GREEN (final gate PASS)

Lane: `bugfix` (harness-only). RED: 2/2 deterministic failure (`file lacks marker 'hi jk
127'`, buffer `HandleInheritability jk 127// Beta.cs`). Debug isolated a **test artifact**:
`i` was pressed at document position 0 (code context), so C# IntelliSense popped and the
injected Space committed `HandleInheritability`, replacing `hi`. Every marker key WAS
routed (`vim-mode=Insert` + the `[Hook] key=H/I/Space/J/K/Space` tail). Fix: two `w`
motions move the caret into the `// Beta.cs` comment first (marker/keystrokes unchanged —
insertion point only). Verified: `neovisual-editor-insert` **3/3 sequential GREEN** (was
2/2 RED), full suite **35/35**, `seed-leak` GREEN, NeoVisual 38/38, Telescope 56/56. No
product code changed; no new diagnostic literal (M-M7 not triggered).
`delegations: 2 | VS boots: 5 | iterations: 1`.

### DEVIATION / coverage note (hub)

- The verifier flagged a plan-coverage gap: this plan file had documented the PREVIOUS
  item (`telescope-implementation`), not this one. Corrected here (this revision). The
  fix is harness-only, so no Verification Trace existed for it during the VERIFY; the
  trace above now records it. Not a diagnostic-format drift.