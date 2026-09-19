# Implementation Plan — Item: Fix the 4 known-RED backlog items (gate the 26-scenario suite green)

> **Lane: bugfix** — existing behavior is broken (Enter-storm) plus three wrong
> harness assertions in `tools/test-e2e.ps1`. NO `[Telescope]`/`[NeoVisual]`
> diagnostic line or diagnostic-format contract is added or changed (M-M7 does
> not apply — the storm fix and the assertion fixes all assert on EXISTING
> diagnostics). Lighter lane: no initial-plan REVIEW (2a); RED at the unit level
> for the new guard logic, plus the documented regression-pair VS boot for the
> storm (harness-only sub-lane); VERIFY runs the 5 affected scenarios + the
> affected unit project.

---

**Goal:** Make the full 26-scenario e2e suite GREEN by fixing the 4 documented
known-RED backlog items from `docs/progress.md`:
1. `telescope-prompt-motions` — wrong expected caret for `e` (assertion bug).
2. `telescope-open-file-normal` — wrong key name `key=Enter` vs `key=Return`
   (assertion bug).
3. `telescope-issues` — fragile `results count=1` (Error List noise accumulates
   during a full run).
4. `neovisual-explorer-open` + `neovisual-explorer-open-o` — the **Enter-storm**
   re-injection loop (extension bug, architecture-review **F1**, severity critical).

---

## Root cause (verified 2026-09-19)

### Bug 4 — Enter-storm (extension, F1)

`SolutionExplorerController` maps `Enter` (and `o`) to `OpenSelected()`, which
injects a native `VK_RETURN` via `KeyInjection.Press` so the tree opens the item.
The injected Return **re-enters the global hook** (`Enter` is in the controller's
`ActionKeys`, so `IsInteresting` returns true) and is re-routed to
`OpenSelected()` again, which injects another Return — an unbounded re-injection
storm (`solution-explorer open` fires ~30x in ~100ms; verified in
`docs/progress.md` backlog item 4).

**Why a blanket `LLKHF_INJECTED` bail is NOT viable:** the harness injects EVERY
test key via `keybd_event` (`KbInject.TapVk` in `tools/test-e2e.ps1`), so all
harness keys carry `LLKHF_INJECTED` at the hook. Bailing on the flag would break
every scenario. The fix must distinguish OUR OWN process's injection from the
harness's — an **in-flight guard in `KeyInjection.Press`** (F1's second option).

**Fix mechanism (in-flight guard):** `KeyInjection.Press(vk)` records the VK it
is about to inject (per-VK pending counter). `GlobalKeyboardHook.HookCallback`,
at the top of the `isKeyDown` branch, asks the guard whether THIS key is one we
just injected; if so it passes the event through (`CallNextHookEx`) without
handling or swallowing it. The injected Return then reaches the tree natively
(the file opens — the current storm's eventual opens prove the tree consumes a
native Return), but it never re-triggers `OpenSelected()`. Event ordering makes
this deterministic: `keybd_event` queues the event NOW, and the harness's next
key is ≥60ms away, so the injected event is always the next same-VK event the
hook sees (consume-once semantics). NOTE: the Build Plan (BP-1) mandates a
strict per-VK counter with NO clock/timestamp — do not add a ~250ms window;
the fallback is explicitly not chosen.

`o` (VK_O) has the same storm path (it calls `OpenSelected()` too), so the guard
fixes both `neovisual-explorer-open` and `neovisual-explorer-open-o`.

### Bugs 1-3 — wrong harness assertions (tools-only, F16)

1. `telescope-prompt-motions` (lines ~977-989): after the three `b` taps the
   caret is at 0. EndWord from 0 stops at `i=3` (last char `'d'` of `find`),
   then `MoveTo(i+1)` = **4**, not 5. The current assertions after `e` are all
   one word-shift off and there is one `w` tap too few. Documented correct
   sequence (from `docs/progress.md` backlog item 1):
   - `e` → `prompt-motion key=E caret=4`
   - `w` → `key=W caret=5`
   - `w` → `key=W caret=8`
   - `w` → `key=W caret=12`
   - `0` → `key=D0 caret=0`
   - `$` → `key=D4 caret=12`
   (Earlier `h=11`, `l=12`, `b=8`, `b=5`, `b=0` are correct and unchanged.)
2. `telescope-open-file-normal` (line ~1083): asserts `key=Enter mode=normal
   handled=True`, but WPF `Key` for Enter is `Key.Return`, so the actual line is
   `key=Return mode=normal handled=True`. The file DID open — only the log-name
   assertion is wrong. `telescope-open-file` (insert) does NOT assert a
   `key=Enter` line — it only asserts `opened file: ...` — no change needed.
3. `telescope-issues` (line ~937): asserts `results count=1 selected=0`, but the
   VS Error List accumulates warnings/errors during a session so `results count`
   is non-deterministic. Relax to `results count=\d+ selected=0`. The seeded
   TODO is still ranked first, and the `preview file=...TodoProbe.cs` +
   `preview caret=... line=1` + `opened issue: ...TodoProbe.cs line=...`
   assertions already prove the right issue is selected/opened.

---

## Approach

1. **Harness fixes (1-3) in `tools/test-e2e.ps1`** — correct the three
   assertions as specified above. No extension change.
2. **Storm fail-fast (F1)** in `tools/test-e2e.ps1` — in both
   `neovisual-explorer-open` and `neovisual-explorer-open-o`, after the walk
   loop, count `[NeoVisual] solution-explorer open` lines post-baseline and
   FAIL if the count exceeds the loop's max legitimate presses (bound ≈ 10); a
   storm produces ~30 in milliseconds. Also add an `Assert-VsFocused` before
   each `Enter`/`o` press in the walk loop (F16 — addresses the `-open-o` focus
   flakiness).
3. **In-flight injection guard (extension)** — new pure class
   `InjectedKeyGuard` in the `MyExtension` project (dependency-free, per-VK
   pending counter with consume-once semantics; injectable clock only if a
   time-window variant is chosen). Wire it:
   - `KeyInjection.Press(vk)` records the VK before `keybd_event`.
   - `GlobalKeyboardHook.HookCallback` consumes/checks it at the top of the
     `isKeyDown` branch and returns `CallNextHookEx` (pass through) when the key
     is one we just injected — before `IsInteresting`/`HandleKey`.
   - Thread-safety note: `Press` and the hook callback both run on the UI thread
     (hook is UI-thread; `HandleKey` runs inline), so plain fields suffice; keep
     the guard written/read on the same thread.
4. **Unit tests** in `tests/NeoVisual.Tests` (the MyExtension test project) for
   the pure guard — RED at the unit level (missing symbol compile failure)
   before build-agent implements it.

---

## Acceptance criteria (each maps to a diagnostic line + a test)

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | `telescope-prompt-motions` passes | `[Telescope] prompt-motion key=E caret=4`, `key=W caret=5/8/12`, `key=D0 caret=0`, `key=D4 caret=12` | e2e scenario |
| A2 | `telescope-open-file-normal` passes | `[Telescope] key=Return mode=normal handled=True` + `opened file: .*Program\.cs` | e2e scenario |
| A3 | `telescope-issues` passes (full-run stable) | `[Telescope] results count=\d+ selected=0` + `preview file=...TodoProbe\.cs` + `opened issue: ...TodoProbe\.cs line=\d+` | e2e scenario |
| A4 | `neovisual-explorer-open` + `-open-o` pass with NO storm | `[NeoVisual] solution-explorer open` exactly once per press (fail-fast if count > bound); `[NeoVisual] editor-view-opened file=...` | e2e scenarios |
| A5 | Guard logic unit-tested | n/a (pure logic) | `Run_InjectedKeyGuard_*` in NeoVisual.Tests |
| A6 | No diagnostic format changed; full 26-scenario suite green; both unit suites green | — | full-suite final gate |

---

## Tests

### Offline unit tests (tests/NeoVisual.Tests, new — bugfix RED at unit level)

The guard is pure, dependency-free logic (the `OverlayKeyHandler` pattern). Add a
`Run_InjectedKeyGuard_*` family (exact names at the builder's discretion, e.g.):
- **Consume-once:** `Record(13)` then `TryConsume(13)` → true; `TryConsume(13)` again → false.
- **Wrong VK not consumed:** `Record(13)`; `TryConsume(0x28)` → false; `TryConsume(13)` → true (still pending).
- **No record:** `TryConsume(13)` → false.
- **Multiple records:** two `Record(13)` → two consumes true, third false (per-Press counting).

RED proof: these tests reference a class that does not exist yet → the
NeoVisual.Tests build fails (missing symbol). That is the right-reason unit RED.

### E2E scenarios (affected — existing scenarios, no new ones required)

| Scenario | What it asserts (after fix) | Diagnostics depended on |
|----------|------------------------------|--------------------------|
| `telescope-prompt-motions` | corrected caret sequence per A1 | `[Telescope] prompt-motion key=... caret=...` |
| `telescope-open-file-normal` | `key=Return mode=normal handled=True` + file opens | `[Telescope] key=Return ...`, `[Telescope] opened file: ...` |
| `telescope-issues` | `results count=\d+ selected=0` + TODO preview/opened | `[Telescope] results count=...`, `preview file=...`, `opened issue: ...` |
| `neovisual-explorer-open` | walk loop opens a file; exactly-one `solution-explorer open` per press (fail-fast) | `[NeoVisual] solution-explorer open`, `[NeoVisual] editor-view-opened file=...` |
| `neovisual-explorer-open-o` | same via `o` | same |

---

## RED evidence plan (e2e-test-builder)

The builder owns `tools/test-e2e.ps1` + the new unit tests. Procedure:

1. **Apply the harness changes** (assertion fixes 1-3 + the storm fail-fast +
   `Assert-VsFocused` in the walk loops). Leave the extension UNCHANGED.
2. **Unit RED (no VS boot):** `dotnet run --project tests/NeoVisual.Tests -- --list`
   (or build) FAILS — `InjectedKeyGuard` does not exist. This is the right-reason
   unit RED for A5.
3. **Regression-pair VS boot (ONE boot, harness-only sub-lane):** run
   `pwsh tools/test-e2e.ps1 -Tests telescope-prompt-motions,telescope-open-file-normal,telescope-issues,neovisual-explorer-open,neovisual-explorer-open-o`
   against the UNCHANGED extension:
   - The first 3 scenarios must PASS with the corrected assertions — proving the
     assertion fixes are correct (the extension behavior was already right; the
     documented pre-fix failures in `docs/progress.md` backlog items 1-3 / F16
     are the RED evidence for those).
   - `neovisual-explorer-open` / `-open-o` must FAIL with the fail-fast — the
     Enter-storm is still present (multiple `solution-explorer open` lines
     post-baseline). **RED for the right reason** (A4).
4. Report: the exact failing assertion + the observed `solution-explorer open`
   count + the unit build failure, capped per the delegation contract.

The builder does NOT implement the extension guard (that is build-agent's job).

---

## Known-RED allowlist (for VERIFY)

- **None of the 5 affected scenarios** are allowlisted — they are this item's
  targets and must be GREEN.
- After this item, the full suite has NO known-RED scenarios. If the full-suite
  final gate surfaces a failure in an unrelated scenario, the verification-agent
  classifies it (flaky → retry once; real regression → re-plan), per the loop.
- `log/tools-hash.txt` is refreshed by any harness run (bootstrap `Write-ToolsHash`);
  the harness-health self-checks (parse, `-List` = 26 scenarios, seed consistency,
  `check-doc-refs.ps1`) run before trusting any e2e result.

---

## Execution Log

_To be appended by the hub on each attempt: attempt #, per-BP-step status,
debug/verifier verdict, capped evidence, and the cost line_
`delegations: N | VS boots: M | iterations: K`.

---

## Build Plan

> Scope: this plan implements ONLY the missing extension logic — the
> `InjectedKeyGuard` in-flight injection guard (F1). It does **NOT** touch
> `tools/test-e2e.ps1` (harness assertion fixes + storm fail-fast are already
> applied and validated by the test-builder) and does **NOT** change any
> `[Telescope]`/`[NeoVisual]` diagnostic format (M-M7 does not apply). The guard
> is pure, dependency-free logic (the `OverlayKeyHandler` pattern), unit-tested
> RED-first. Both `KeyInjection.Press` and the hook callback run on the UI
> thread, so the guard is a plain-field per-VK counter with NO locking and NO
> time window (deterministic given strict event ordering).

### Contract the build-agent must NOT second-guess

The test-builder's RED tests in `tests/NeoVisual.Tests/Program.cs`
(`Run_InjectedKeyGuard_*`, lines 276–306, namespace `NeoVisual.Tests`,
`using MyExtension;`) call the EXACT API below. The build-agent must match it
verbatim — member names, parameter type (`int`), and return type (`bool`):

- `new InjectedKeyGuard()` — public parameterless constructor on a class whose
  **namespace is `MyExtension`** (the test file already has `using MyExtension;`).
- `void Record(int vk)` — records one pending press of the VK.
- `bool TryConsume(int vk)` — consumes ONE pending record of the VK and returns
  true; returns false if no pending record for that VK.

Per-test semantics that MUST hold (from the tests):
- Consume-once: `Record(13); TryConsume(13)→true; TryConsume(13)→false`.
- Wrong VK: `Record(13); TryConsume(0x28)→false; TryConsume(13)→true` (still pending).
- No record: `TryConsume(13)→false`.
- Multiple records (per-Press counting): two `Record(13)` → two `true`, third `false`.

### BP-1 — Add the pure `InjectedKeyGuard` class

- **What to change:** create `MyExtension/InjectedKeyGuard.cs`, namespace
  `MyExtension`. A dependency-free, `internal sealed class InjectedKeyGuard` with:
  - `private readonly Dictionary<int, int> _pending = new Dictionary<int, int>();`
    (the per-VK pending counter).
  - `public void Record(int vk)` → increments that VK's counter (default 0 → 1).
  - `public bool TryConsume(int vk)` → if the VK has a pending count `> 0`,
    decrement it and return `true`; otherwise return `false`.
  - `internal static readonly InjectedKeyGuard Instance = new InjectedKeyGuard();`
    — a single shared instance the extension wiring uses (both `KeyInjection` and
    `GlobalKeyboardHook` are in the same assembly and read/write it on the UI
    thread). The tests do NOT use `Instance`; they `new`-up their own instances.
  - NO locking, NO clock/timestamp, NO thread-affinity assertion (it is pure —
    must be constructible and callable with zero VS/DTE/windowset dependencies so
    the NeoVisual.Tests runner can exercise it without a running VS).
  - net472-safe: `Dictionary<int,int>` only; no `IReadOnlySet<T>`.
  - File header comment mirroring the `KeyInjection.cs` doc style: explain that
    this is the Enter-storm fix — `KeyInjection.Press` records the VK it is about
    to synthesize, and `HookCallback` consumes it so our own injected key passes
    through the hook untouched instead of re-triggering the controller action.
- **Verify-with:** `dotnet build` succeeds and `dotnet run --project
  tests/NeoVisual.Tests -- -- InjectedKeyGuard` runs the 4
  `Run_InjectedKeyGuard_*` tests and all 4 PASS (this is also the A5 gate).
- **Fails-if:** compile error `CS0246: The type or namespace name
  'InjectedKeyGuard' could not be found` persists; or any of the 4 tests fails
  (e.g. `second consume of the same VK fails (consume-once)` assertion throws
  because the counter was not decremented, or `a different VK does not consume
  the pending record` throws because `TryConsume` matched the wrong VK).

### BP-2 — Wire `KeyInjection.Press(vk)` to record before injecting

- **What to change:** in `MyExtension/KeyInjection.cs`, method
  `public static void Press(int vk)`, add
  `InjectedKeyGuard.Instance.Record(vk);` as the FIRST statement, BEFORE the
  `keybd_event` down/up calls. This records every synthesized key the extension
  injects (arrows, F2, Return) so the very next same-VK key-down the hook sees
  can be recognized as ours. No other change to `KeyInjection`.
- **Verify-with:** build compiles. Functionally proven end-to-end by BP-3 +
  the e2e scenarios — the injected `VK_RETURN` from `OpenSelected()` will be the
  next `VK_RETURN` down the hook sees, and the hook (BP-3) will pass it through
  instead of re-triggering `OpenSelected()`. Unit-level: the guard API is already
  covered by BP-1; this step is pure wiring.
- **Fails-if:** the injected Return still re-enters `HandleKey` (storm persists in
  `neovisual-explorer-open` even after BP-3 is applied), OR the injected key
  never reaches the tree natively (file does not open — `[NeoVisual]
  editor-view-opened file=...` never appears), which would mean `Record` ran but
  the hook consumed the event instead of passing it through.

### BP-3 — Wire `GlobalKeyboardHook.HookCallback` to pass through own injections

- **What to change:** in `MyExtension/GlobalKeyboardHook.cs`,
  `HookCallback`, as the FIRST statement inside the `if (isKeyDown) {` block
  (i.e. immediately after the `if (isKeyDown)` opening brace — NOT before the
  `if`), BEFORE the `GetAsyncKeyState` modifier reads and the
  `IsInteresting` pre-filter. This placement is inside the key-down branch, so
  key-up events never hit the guard. Add:
  ```csharp
  // Enter-storm guard (F1): if this key-down is one we just synthesized in
  // KeyInjection.Press, pass it through untouched so it reaches the focused
  // tree/control natively instead of re-triggering the controller action.
  if (InjectedKeyGuard.Instance.TryConsume(vkCode))
  {
      return CallNextHookEx(_hookId, nCode, wParam, lParam);
  }
  ```
  `vkCode` is the `int` already read from `lParam` at ~line 97
  (`int vkCode = Marshal.ReadInt32(lParam);`). Return
  `CallNextHookEx(...)` (NOT `(IntPtr)1`) so the injected key is passed through,
  never swallowed. This is before `IsInteresting`/`HandleKey`, so our injected
  `VK_RETURN` never reaches `OpenSelected()` again.
- **Verify-with:** e2e `neovisual-explorer-open` and `neovisual-explorer-open-o`
  both pass: the harness's `Assert-VsFocused` + storm fail-fast (≤10
  `[NeoVisual] solution-explorer open` lines post-baseline) does NOT fire, and
  `[NeoVisual] editor-view-opened file=...` appears (the file opened via the
  natively-passed-through Return).
- **Fails-if:** the storm fail-fast still fires (`Enter-storm: N
  'solution-explorer open' lines post-baseline (bound 10)`), meaning `TryConsume`
  did not run / did not match before `IsInteresting` → `OpenSelected` re-fired; OR
  every scenario that relies on the hook handling harness keys now no-ops (the
  guard pass-through is consuming harness keys — which it must NOT, since the
  harness's `keybd_event` presses are never `Record`ed by OUR process) → this
  would signal the guard mistakenly bails on `LLKHF_INJECTED` semantics or matches
  keys it never recorded.

### BP-4 — Build + run the guard unit tests

- **What to change:** nothing (verification step only).
- **Verify-with:** `dotnet build` (whole solution, exit 0), then
  `dotnet run --project tests/NeoVisual.Tests -- -- InjectedKeyGuard` → exit 0
  and 4/4 `Run_InjectedKeyGuard_*` tests reported passing. Also run the full
  unit suite to catch regressions: `dotnet run --project tests/NeoVisual.Tests`
  (must remain 21 + 4 = 25 passing) and `dotnet run --project
  tests/Telescope.Tests` (must remain 42 passing).
- **Fails-if:** any build error, any of the 4 guard tests failing, or any
  previously-passing unit test now failing (the guard is additive and pure, so a
  regression here means an unrelated edit slipped in).

---

## Verification Trace

| failing test/scenario | implicated steps | expected diagnostic / proof |
|-----------------------|------------------|-----------------------------|
| `Run_InjectedKeyGuard_ConsumeOnce` | BP-1 (+ BP-2/BP-3 wiring exercised via build) | unit PASS: `Record(13); TryConsume(13)→true; TryConsume(13)→false` |
| `Run_InjectedKeyGuard_WrongVkNotConsumed` | BP-1 | unit PASS: `Record(13); TryConsume(0x28)→false; TryConsume(13)→true` |
| `Run_InjectedKeyGuard_NoRecord` | BP-1 | unit PASS: `TryConsume(13)→false` |
| `Run_InjectedKeyGuard_MultipleRecords` | BP-1 | unit PASS: two `Record(13)` → two `true`, third `false` |
| `neovisual-explorer-open` (RED: Enter-storm) | BP-1 + BP-2 + BP-3 | `[NeoVisual] solution-explorer open` exactly once per press (harness fail-fast ≤10 post-baseline does NOT fire); `[NeoVisual] editor-view-opened file=...` appears |
| `neovisual-explorer-open-o` (RED: Enter-storm via `o`) | BP-1 + BP-2 + BP-3 | same as above, via `o` |
| `telescope-prompt-motions` / `telescope-open-file-normal` / `telescope-issues` (harness-fix scenarios) | NOT affected by BP-n (tools-only fixes) | remain GREEN — `[Telescope] prompt-motion key=E caret=4` … , `[Telescope] key=Return mode=normal handled=True` + `opened file: .*Program\.cs`, `[Telescope] results count=\d+ selected=0` + `opened issue: ...TodoProbe\.cs line=\d+` |

### Known-RED allowlist (for VERIFY)

- **None of the 5 affected scenarios** are allowlisted — `neovisual-explorer-open`,
  `neovisual-explorer-open-o`, `telescope-prompt-motions`,
  `telescope-open-file-normal`, `telescope-issues` are this item's targets and
  must be GREEN at final verify.
- After this item the full suite has NO known-RED scenarios. Any unrelated
  scenario failing at the final full-suite gate is classified by the
  verification-agent (flaky → retry once; real regression → re-plan).