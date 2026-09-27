# Implementation Plan — Item: `telescope-implementation` intermittent injected-Enter loss

> **Lane: bugfix.** Existing behaviour is intermittently broken (a registered live
> scenario fails 2/2 in some runs, passes in others). The fix changes **NO**
> `[Telescope]`/`[NeoVisual]` diagnostic line and **NO** diagnostic-format contract
> (M-M7 NOT triggered) — it is verified through the EXISTING contract
> (`implementations gathered count=N` → `open finder=Implementation candidates=N` →
> `opened implementation: file=… line=…`).
>
> Lighter lane: skip the initial-plan REVIEW (2a); RED has no NEW unit surface (see
> RED evidence); VERIFY runs the affected e2e scenario(s) + the affected unit project.

---

**Goal:** `telescope-implementation` must pass **deterministically**, not ~50% of the
time. The gather + preview are correct every run; the failure is that the injected
**Enter never reaches the overlay** (`[Telescope] opened implementation:` never fires,
and usually no `[Telescope] key=Return mode=insert handled=True` appears at all).

---

## Root cause (CONFIRMED by the DEBUG step — layer: **HARNESS**, not a product bug)

The overlay's `[Telescope] Focus prompt => True, mode=insert` line is logged from WPF
`Activated` / `ContentRendered` (WPF **logical** focus — `TelescopeOverlay.cs:210-221` →
`FocusPrompt()`), but at that instant the overlay's **HWND is not yet the OS foreground
window**. `Assert-OverlayFocused` (`tools/test-e2e.ps1:230-234`) only checks that the
foreground window belongs to the VS **process id** — it cannot tell the overlay HWND
from the VS main-window HWND (same PID) — so it passes, and the harness injects
`VK_RETURN` before OS activation completes. The key is consumed by the VS main window
and never reaches the overlay's `PreviewKeyDown`.

**Proof** (temporary probe, since reverted): immediately before the Step-5 Enter the
foreground HWND title was the VS main window (`TelescopeTest - IShape.cs - …
Experimental Instance`); ~800 ms AFTER the Enter it became `Telescope` (the overlay).
So Enter was delivered to the wrong window.

**Why `telescope-implementation` specifically:** it is the only finder that injects
Enter with **no intervening typing/wait** after the overlay opens. Other finders type a
query first (~360 ms of `Send-Text`), which masks the activation race.

Implicated sites (harness side):
- `tools/test-e2e.ps1:230-234` — `Assert-OverlayFocused` is PID-only (the defect).
- `tools/test-e2e.ps1:358-379` (`Open-TelescopeImplementation`) and the sibling
  `Open-Telescope*` helpers — they `return` right after the two `Wait-NewLogLine`s and a
  PID-only `Assert-OverlayFocused`, so the caller's Enter races OS activation.
- `tools/test-e2e.ps1:1533-1540` — the scenario's Step-5 Enter.

Product-side (unchanged, context only): `Telescope/TelescopeOverlay.cs:210-221`
(`Activated`/`ContentRendered` → `FocusPrompt`; focus log `:562`, key log `:931-934`)
and `MyExtension/GlobalKeyboardHook.cs:215-222` (`IsVisualStudioFocused` is also
PID-only — the hook correctly acts on any window of *this* VS process; the overlay's own
`PreviewKeyDown` consumes the key once the overlay is foreground). Trailmark
(`callers_of`) confirms `FocusPrompt` is invoked from the WPF `Activated`/`ContentRendered`
handlers in the `TelescopeOverlay` ctor and from `ApplyAction`, and that
`IsVisualStudioFocused` is called only by `HookCallback` — no product change is warranted.

**Plan corrections folded in (found by the debug-agent):**
1. `[Hook] key=Return …` **cannot exist** — `Keys.Return` is not in
   `GlobalKeyboardHook.IsInteresting` (`:174-189`). The real existing pass line is
   `[Telescope] key=Return mode=insert handled=True` (`TelescopeOverlay.cs:931-934`).
   A2 uses that line.
2. The earlier approach text ("reuse the existing `Assert-OverlayFocused` semantics")
   overestimated that helper — it only checks the PID. The fix must **strengthen** it
   (or add a real overlay-window check).

## Approach

**Chosen (minimal, harness-only): make the overlay-focus gate assert the ACTUAL overlay
window, and gate the Enter on that *materialised* state.**

`Assert-OverlayFocused` / the `Open-Telescope*` helpers must wait (positive, bounded —
the same pattern as the existing `Wait-NewLogLine`) until the foreground HWND is the
overlay: same process id **AND** foreground window text `Telescope` (the overlay's
`Title = "Telescope"`, `TelescopeOverlay.cs:83` — set even with `WindowStyle.None`).
Only then may a helper `return` / the scenario inject Enter. This is a wait for an
observable **state change** (foreground title), never an absence timer and never a bare
`Start-Sleep`.

**Rejected (second choice): product-side `SetForegroundWindow` in `TelescopeOverlay`.**
It changes product behaviour, would need its own deterministic assertion, and does not
establish the harness gate that proves the state; the harness path is sufficient and
leaves the product untouched (M-M7 not triggered).

**Diagnostics:** UNCHANGED — no new/changed `[Telescope]`/`[NeoVisual]` literal. The fix
is entirely in `tools/test-e2e.ps1` and is verified through the EXISTING contract
(`implementations gathered count=N` → `open finder=Implementation candidates=N` →
`[Telescope] key=Return mode=insert handled=True` → `opened implementation: file=…`).

---

## Acceptance criteria

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | `telescope-implementation` passes **deterministically across ≥3 sequential runs** (not one) | `opened implementation: file=.*Shape\.cs line=2` | e2e `telescope-implementation` ×3 |
| A2 | The Enter is actually delivered (not flaky) | `[Telescope] key=Return mode=insert handled=True` (`TelescopeOverlay.cs:934`; the old `[Hook] key=Return` can never exist — Return is not in `IsInteresting`) | e2e `telescope-implementation` |
| A3 | No neighbour regresses (other finders' Enter) | existing `opened file:`/`opened issue:`/`opened reference:`/`opened grep:` lines | affected VERIFY set |
| A4 | Both unit suites stay green | — | NeoVisual 38, Telescope 56 |
| A5 | NO diagnostic added/changed | diff shows no new `[Telescope]`/`[NeoVisual]` literal; only `tools/test-e2e.ps1` touched | code review at VERIFY |
| A6 | The gate is a materialised-state wait, not a timer | foreground HWND is the overlay (PID + window text `Telescope`) before Enter | code review + log-absent `[Telescope] key=Return` immediately after Enter |

---

## Tests

### E2E (the regression scenario — existing)
- **`telescope-implementation`** — already registered; its assertions ARE the contract.
  The defect is intermittent, so GREEN proof must be **≥3 sequential runs** of
  `pwsh tools/test-e2e.ps1 -Tests telescope-implementation`, not one. (The debug-agent
  reproduced 3/3 before the fix.)
- **Regression neighbours (A3):** `telescope-open-file`, `telescope-issues`,
  `telescope-references`, `telescope-grep`, `telescope-open-file-searchbox`, and every
  other `Open-Telescope*` consumer — all now go through the strengthened gate.

### Offline unit tests
None expected (harness focus/injection is live-only). If a genuine pure seam emerges, add
it to `tests/Telescope.Tests`. The gate change is PowerShell-only; the acceptance oracle
is the existing live contract, not a new unit test.

### Harness self-check (no VS)
`Assert-OverlayFocused` / `Wait-OverlayForeground` are pure Win32 probes. A no-VS check
is the ≥3-run loop; a targeted probe helper (`Get-ForegroundTitle`) is validated in-line
by the debug probe evidence (foreground title flips to `Telescope`).

---

## RED evidence plan
1. Builder boots VS and runs `pwsh tools/test-e2e.ps1 -Tests telescope-implementation`
   **repeatedly** (≥3 runs) until the intermittent failure reproduces (it failed 2/2 in
   prior full runs); capture the post-`Focus prompt => True` tail showing the missing
   `[Telescope] key=Return mode=insert handled=True` / `opened implementation`.
   (Note: `[Hook] key=Return` cannot appear — `Keys.Return` is not in
   `GlobalKeyboardHook.IsInteresting`; the debug-agent confirmed the harness focus race.)
2. State the no-unit-test reason.
3. No new diagnostic/scenario required.

---

## Known-RED allowlist (for VERIFY)
- `neovisual-editor-insert` — pre-existing flake (retry-once).
- `telescope-references` — newly recorded flake (x1); report if it recurs.
- Everything else must stay GREEN; `telescope-implementation` is this item's target.

---

## Build Plan

> **Lane: bugfix — HARNESS only (`tools/test-e2e.ps1`).** **No** `[Telescope]`/`[NeoVisual]`
> diagnostic added/changed (M-M7 NOT triggered). The product
> (`Telescope/TelescopeOverlay.cs`, `MyExtension/GlobalKeyboardHook.cs`) is **untouched**.
> All steps are PowerShell; no `net472`/UI-thread code changes. `Root cause`/`A2`/`Tests`
> above reflect the debug-agent's confirmed harness-race diagnosis.

**BP-1 — Add the overlay-foreground probe + bounded wait helper.**
- **Files:** `tools/test-e2e.ps1` — `Win32.Fg` `Add-Type` member definition (line 1863) and
  a new helper near `Assert-OverlayFocused` (line 230).
- **Change:**
  - Extend the `Win32.Fg` member definition (keep the existing three imports) with
    `[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);`
    (add `GetWindowTextLength` only if needed).
  - Add `Get-ForegroundTitle([object]$vs)`: returns the foreground HWND's window text
    (a `[System.Text.StringBuilder]`, capacity 256 → `[Win32.Fg]::GetWindowText($h,$sb,$sb.Capacity)`),
    or `''` when `GetForegroundWindow()` is `IntPtr.Zero`.
  - Add `Wait-OverlayForeground([object]$vs, [int]$maxMs = 5000)`: a **positive bounded
    poll** (Stopwatch loop + `Start-Sleep -Milliseconds 100`) returning `$true` when the
    foreground HWND's PID `-eq $vs.Id` **AND** `Get-ForegroundTitle` `-eq 'Telescope'`
    (the overlay's `Title = "Telescope"`, `TelescopeOverlay.cs:83`); `$false` on timeout.
    This waits for a **state change** (foreground title), never an absence timer and never
    a fixed sleep.
  - **Trailmark grounding:** `callers_of("FocusPrompt")` →
    `TelescopeOverlay` ctor (WPF `Activated`/`ContentRendered`) + `ApplyAction`;
    `callers_of("IsVisualStudioFocused")` → `HookCallback` only. Both are PID/logical-focus
    signals — confirming the harness, not the product, is where the window identity is missing.
- **Verify-with:** the debug probe's observed transition — foreground title is the VS main
  window at logical focus, then flips to `Telescope` (~800 ms). After BP-1,
  `Wait-OverlayForeground $vs` returns `$true` within its bound on a live boot; a bad
  foreground reports `$false`. (Harness-only: no `[Telescope]`/`[NeoVisual]` literal is added.)
- **Fails-if:** `GetWindowText`/`Wait-OverlayForeground` throws (`Add-Type` compile error /
  marshal signature wrong); `Get-ForegroundTitle` returns `''` on the overlay (window text
  not retrievable — wrong HWND); or `Wait-OverlayForeground` returns `$true` while the VS
  main window is still foreground (the title comparison is missing — PID-only again).

**BP-2 — Strengthen `Assert-OverlayFocused` to require the overlay HWND (not just the PID).**
- **Files:** `tools/test-e2e.ps1:230-234`.
- **Change:** keep the signature `Assert-OverlayFocused([object]$vs)` (all call sites
  unchanged). Body becomes: keep `Assert-VsFocused $vs '…'` (PID check), then require the
  foreground window **text** to be `Telescope` — implement via `Wait-OverlayForeground $vs`
  so the assert is a **materialised-state gate**, throwing a specific error that names the
  observed `Get-ForegroundTitle` value on timeout. Update the doc comment: the overlay is
  NOT "any window of the VS PID" — it must be the actual overlay HWND (a PID-only check
  passes for the VS main window, the exact defect).
- **Verify-with:** after a passing `Open-Telescope*`,
  `Assert-OverlayFocused $vs` succeeds and the scenario proceeds to
  `[Telescope] key=Return mode=insert handled=True` → `opened implementation:`.
- **Fails-if:** the thrown message reports a foreground title equal to the VS main-window
  title (`… Experimental Instance`) while the overlay is logically focused — i.e. the race
  reproduced and the gate did not wait / is still PID-only.

**BP-3 — Gate every `Open-Telescope*` helper's success return on the materialised overlay.**
- **Files:** `tools/test-e2e.ps1` — `Open-Telescope` (`270-293`), `Open-TelescopeIssues`
  (`295-313`), `Open-TelescopeReferences` (`315-334`), `Open-TelescopeGrep` (`336-356`),
  `Open-TelescopeImplementation` (`358-379`).
- **Change:** in each helper, replace the `Assert-OverlayFocused $vs; return` success branch
  with `if (Wait-OverlayForeground $vs 5000) { Assert-OverlayFocused $vs; return }`, and let
  a failed wait fall through to the existing retry/`throw`. A helper may now return ONLY
  once the overlay HWND is the real OS foreground window, so the caller's next injected key
  (Enter) cannot race OS activation. (Between the logical-focus `Wait-NewLogLine` and
  `Wait-OverlayForeground` the helper simply blocks on the observable state — no new
  arbitrary delay.)
- **Verify-with:** the scenario's Enter now reaches the overlay:
  `[Telescope] key=Return mode=insert handled=True` (`TelescopeOverlay.cs:934`) appears
  after the gate, followed by `opened implementation: file=.*Shape\.cs line=2`.
- **Fails-if:** a helper returns while `Get-ForegroundTitle` is still the VS main window
  (gate placed before the `Wait-NewLogLine`s / bypassed); or `telescope-implementation`
  still shows no `[Telescope] key=Return` post-baseline across ≥3 runs.

**BP-4 — Gate the `telescope-implementation` Step-5 Enter explicitly.**
- **Files:** `tools/test-e2e.ps1:1533-1540` (the Step-5 comment + `Send-Tap $script:VkEnter`).
- **Change:** insert `Assert-OverlayFocused $vs` (now materialised, BP-2) immediately before
  `Send-Tap $script:VkEnter`, and extend the comment to say the Enter is gated on the
  overlay being the real foreground window (not just the VS PID). This makes the trace
  self-contained and covers the no-typing race window of this specific scenario.
- **Verify-with:** identical pass chain, on **≥3 sequential runs**:
  `implementations gathered count=1` → `open finder=Implementation candidates=1` →
  `preview file=.*Shape\.cs` / `preview caret=\d+ line=2` →
  `[Telescope] key=Return mode=insert handled=True` →
  `opened implementation: file=.*Shape\.cs line=2`.
- **Fails-if:** no `[Telescope] key=Return` post-baseline and no `opened implementation`
  even though the gate assert passed — re-open the product hypothesis (the gate is not the
  culprit); or the assert throws with the VS main-window title (BP-2/BP-3 not materialised).

## Verification Trace

| Failing test/scenario (RED) | Implicated steps | Expected diagnostic / pass signal |
|---|---|---|
| e2e `telescope-implementation` — missing `opened implementation` (intermittent, debug-repro 3/3) | BP-1, BP-2, BP-3, BP-4 | gate: foreground HWND == overlay (PID + window text `Telescope`) BEFORE Enter → `implementations gathered count=1` → `open finder=Implementation candidates=1` → `preview file=.*Shape\.cs` / `preview caret=\d+ line=2` → `[Telescope] key=Return mode=insert handled=True` → `opened implementation: file=.*Shape\.cs line=2`; held across **≥3 sequential runs** |
| e2e Enter lost with foreground still `… Experimental Instance` (race) | BP-2, BP-3 | `Assert-OverlayFocused` error names the observed foreground title; `Wait-OverlayForeground` returns `$true` only after title `Telescope` |
| neighbours (A3) — other finders' Enter | BP-1, BP-2, BP-3 | existing `opened file:` / `opened issue:` / `opened reference:` / `opened grep:` lines unchanged (same chain, now via the stronger gate) |
| product contract unchanged (A5) | — (no product step) | `[Telescope] Focus prompt => True, mode=insert` and `[Telescope] key=Return mode=insert handled=True` literals byte-identical; `git diff` touches only `tools/test-e2e.ps1` |
| unit suites (A4) | — (harness-only) | NeoVisual 38/38, Telescope 56/56 (run staggered, W11) |

**Known-RED allowlist (carried from `docs/progress.md` — do NOT report as regressions):**
- `neovisual-editor-insert` — pre-existing flake (IntelliSense autocomplete); retry-once.
- `telescope-references` — recorded flake (x1); report if it recurs.

`telescope-implementation` MUST go GREEN (≥3 sequential runs) — it is this item's target and
is **not** allowlisted. `neovisual-explorer-move` (the `m`-key leak, backlog #1) is a separate
item and remains outside this change set.

---

## Execution Log

### Attempt 1 — GREEN (final gate PASS)

Lane: `bugfix` (harness-only). RED: the debug-agent reproduced the
`telescope-implementation` failure **3/3 standalone** and isolated the layer as the
**harness**, not a product key-handler bug — `Assert-OverlayFocused` was PID-only, so it
passed while the OS foreground window was still the VS **main** window; Enter was injected
before the overlay became foreground (probe: foreground was the main window before Enter,
the overlay ~800ms after). Fix: strengthen `Assert-OverlayFocused` to require the actual
overlay window (PID **and** title `Telescope`) and add the positive `Wait-OverlayForeground`
gate to all five `Open-Telescope*` helpers + before the Step-5 Enter. Product untouched; no
new diagnostic literal (M-M7 not triggered).
`delegations: 3 | VS boots: 5 | iterations: 1`.

### PLAN CORRECTIONS (adjudicated during the item)

- **A2 diagnostic corrected:** `[Hook] key=Return …` is impossible (`Keys.Return` ∉
  `GlobalKeyboardHook.IsInteresting`); the real pass line is
  `[Telescope] key=Return mode=insert handled=True` (`TelescopeOverlay.cs:934`).
- **Approach corrected:** the original text ("reuse the existing `Assert-OverlayFocused`
  semantics") overestimated the PID-only helper; BP-2 now strengthens it.
- **Rejected alternative recorded:** the product `SetForegroundWindow` one-liner was
  rejected as the primary fix (harness path judged sufficient and it avoids changing
  product behaviour without its own deterministic assertion).
