# Code Review Findings — Implementation Plan (docs/code-review.md)

> **Lane: feature/bugfix program (unit-only, e2e deferred).** Source:
> `docs/code-review.md` (2026-09-29, whole-repo 10-round review of the
> post-consolidation codebase). 75 findings: 3 critical, 30 major, 30 minor,
> 12 nit. Research: 1 whole-repo `trailmark-recon` digest + 5 passes × 10
> `arch-auditor` slices (51 subagents), every finding verified against current
> code with proxy-aware Trailmark. e2e scenarios are DEFERRED to
> `e2e-queue.md` (status QUEUED) — this machine cannot boot the VS
> Experimental Instance. NO e2e RED/VERIFY step is planned; every fix is
> verified by unit tests (where a hermetic seam exists) + `dotnet build` +
> the existing unit suites, with the live behavior gated by the deferred e2e
> scenarios.

## Goal

Fix all 75 code-review findings in dependency order — the 3 consolidation
regressions/latent breaks first, then the hook hot-path contract, the finder
UI-stall cluster, the preview/motion correctness bugs, the duplication
merges, the missing testability seams, the harness hardening, and the
naming/convention sweep — with every behavior change proven by a RED unit
test (or, where no hermetic seam exists, by build + existing suites + the
deferred e2e gate).

## Approach

The research passes produced a verified verdict + fix direction + unit-test
seam for every finding. The plan is organized into 12 phases in dependency
order. Each phase lists the findings it covers, the verified fix approach,
the unit-test seam (which project + which tests prove RED), and any
diagnostic contract that must be preserved or added. Cross-cutting
constraints from the research:

- **Log-line-as-contract:** several fixes touch lines the e2e harness asserts
  on (`navigate direction=L/R/D/U`, `shortcut-binding executed:`,
  `leader-binding executed:`, `[Telescope] focus target=List|Preview`,
  `vim-mode=Insert|Normal|Replace`, `preview caret=... line=...`). Any fix
  that changes a signature or a log site MUST preserve the emitted token
  byte-identical (see M40, M31, M15, M34, M17, M7/M11).
- **Proxy traps:** cross-class calls land on `proxy.unresolved:<Type>.<Member>`;
  a bare `callers_of` returning 0 is SUSPECT, not dead code. Never report
  `KeyInjection.Press`, `NeoVisualLog.Close/Log/Debug`, `InstallDebugListener`,
  `TextMotionHelper.MapMotion`, `controller.TryMove/ExitInputMode` as dead.
- **Stale citations corrected by recon:** M34 `OnPreviewKeyDown` is at
  `TelescopeOverlay.cs:500-560` (not :75); M14 `BuildForest` is at
  `SolutionExplorerController.cs:335-362` (not :295).
- **No-seam findings** (VS/WPF/harness-coupled, no hermetic unit surface):
  M4 (WindowManager cache), M33 (InitializeAsync), M19/M28/M36/M37/m26-m29
  (harness), m2/m7 (VS-coupled), m16/m18 (WPF), n4/n8 (VS-coupled). For these
  the plan states the honest verification (build + existing suites + deferred
  e2e) and, where possible, extracts a small pure seam to make part of the
  behavior unit-testable.
- **m25 is documented-accepted** (spec.md §2.2 decision 2026-09-28: the host
  depends on Telescope for `NeoVisualLog`/`DiagnosticLog`) — leave as-is,
  note in the plan, do not "fix".

## Phase structure

### Phase 0 — Criticals (CR1, CR2, CR3)

- **CR1** (critical, CONFIRMED) — per-type controller loop overwrites
  `SolutionExplorerController`. `MyExtensionPackage.cs:87,92-101` +
  `WindowManager.cs:75-78`. Fix: extract a **static pure factory**
  `WindowManager.DefaultControllerFor(ToolWindowType)` (or
  `ToolWindowControllerFactory.Create`) returning the default controller and
  `null` for `SolutionExplorer`/`Unknown`; the package loop calls it and skips
  nulls; the package keeps registering only the specials. This removes the
  ordering dependency. Unit seam (NeoVisual.Tests): `Run_WindowManager_DefaultControllerFor`
  — `DefaultControllerFor(SolutionExplorer) == null`, `DefaultControllerFor(Unknown) == null`,
  `DefaultControllerFor(CommandWindow)` is a `TextInputToolWindowController`,
  `DefaultControllerFor(Toolbox)` is a `GeneralToolWindowController`. RED:
  method doesn't exist → compile error. NOTE: existing
  `Run_SolutionExplorer_ActionKeys`/`Run_ToolWindowMode_HjklMoves` give false
  confidence (no test constructs WindowManager) — the new test closes that gap.
- **CR2** (critical, CONFIRMED) — e2e runner reports PASS on a
  `$false`-returning scenario. `tools/test-e2e.ps1:1907-1911,1915`. Fix:
  after the try/catch, before the per-scenario print, add
  `if ($result -eq $false) { $failures += "$name : returned false" }`
  (key on `$result -eq $false`, NOT `-not $ok` — that would double-count the
  throw case). Verification (no VS): a standalone snippet replicating the gate
  — before fix prints PASS, after fix prints FAIL. No unit test in the C#
  suites (harness-only).
- **CR3** (critical, PARTIAL — defect real, Deactivated trigger unreachable;
  realistic triggers = `TelescopeController.Dispose`/`Close()` during the
  ApplicationIdle window) — deferred `ShowDialog` can fire after `CloseOverlay`.
  `TelescopeOverlay.cs:303,306-320`. Fix: guard the `BeginInvoke` lambda with
  `if (IsOpen) ShowDialog();` — NOT `IsVisible` (IsVisible is false at idle
  time; an IsVisible guard would silently never open the overlay). Optionally
  wrap `ShowDialog` in try/catch + `TelescopeLog.Log`. Unit seam
  (Telescope.Tests): extract a pure `OverlayShowState` (`RequestShow()`,
  `Close()`, `bool ShouldShowDialog() => requested && !closed`); tests:
  `RequestShow(); Close(); ShouldShowDialog() == false` (RED — class doesn't
  exist), `RequestShow(); ShouldShowDialog() == true`, and a test documenting
  the IsVisible trap (state-based, not visibility-based).

### Phase 1 — Hook hot-path contract (M1, M3, M4, m1)

- **M1** (major, CONFIRMED) — per-key `[Hook] key=` log write on the hook hot
  path. `GlobalKeyboardHook.cs:124`. No harness assertion depends on the
  per-key line (only `[Hook] installed` from the ctor). Fix: delete line 124.
  Verification: `Run_LogPrefixes_Pinned` (prefix pin) + the harness
  `[Hook] installed` assertion survive; no unit test needed (deletion).
- **M3** (major, CONFIRMED — undercount: ~4-16 `File.Exists`/key) —
  `IsTestStaleInjected` runs `File.Exists` per key. `WindowManager.cs:44-49`.
  **CRITICAL CORRECTION to the finding's fix direction:** the sentinel toggles
  with NO focus change (harness creates/removes it between scenarios), so
  "refresh only in OnWindowFocusChanged" would leave the cache stale and the
  fault would never inject. Fix: cache the sentinel result in a field and
  refresh **once per key-down** (entry of `IsKeyOfInterest`/`HandleKey`),
  reducing 4-16 syscalls to 1 while preserving semantics. Also add a positive
  diagnostic `[NeoVisual] stale-toolwindow sentinel active` so the deferred
  e2e scenario proves the fault was actually injected (the current absence
  scan is a false-positive risk). Unit seam (NeoVisual.Tests): pure
  `StaleToolWindowSentinel` (path + `Refresh()` + cached `IsStale`); tests:
  temp file → Refresh → IsStale true; delete → Refresh → IsStale false;
  delete WITHOUT Refresh → IsStale stays true (proves caching).
- **M4** (major, CONFIRMED — 5 sites not 3: `InputHandler.cs:76,93,283,396,423`)
  — `IsTextInputType` (complexity 19) up to 3× per key. Fix: cache
  `_isTextInputType` on `WindowManager`, set in `OnWindowFocusChanged` + ctor
  as `GeneralToolWindowController.IsTextInputType(_type)`, expose
  `IsTextInputType => IsTestStaleInjected() ? false : _isTextInputType`
  (SolutionExplorer is never text-input); replace all 5 sites with
  `_windowManager.IsTextInputType`. No-seam (WindowManager is VS-coupled) —
  honest verification: existing `Run_ToolWindowMode_*` classification tests +
  deferred e2e `neovisual-*` scenarios. Must respect the stale-toolwindow
  sentinel or `neovisual-explorer-move-editor-focus` breaks.
- **m1** (minor, CONFIRMED) — `BuildSimpleKey` allocates `List<string>` +
  `string.Join` per interesting key. `InputHandler.cs:451-461`. Fix: replace
  with a `StringBuilder` (single allocation). Unit seam (NeoVisual.Tests):
  extract a pure `KeyNameBuilder.Build(Keys, ctrl, shift, alt)`; test the
  format contract (`Ctrl+H`, `Shift+F4`, `Alt+X`, `Ctrl+Shift+Alt+Delete`).
  NOTE: the exact `"Ctrl+H"` format is a config-file contract — preserve it.

### Phase 2 — Finder path amortization (M5, M6, M7, M8)

- **M5** (major, CONFIRMED) — GrepFinder per-keystroke DTE walk + full file
  re-read bypassing `ProjectFileCache`. `GrepFinder.cs:80,130`. Fix: create
  ONE shared `ProjectFileCache` in `MyExtensionPackage`, inject into both
  `CodeIssuesFinder` and `GrepFinder`; `GrepFinder.GetCandidates` routes
  through `_fileCache.Get(() => ProjectFiles.Enumerate(dte))` with the same
  solution-`FullName` invalidation; optionally a per-file content cache keyed
  by `LastWriteTimeUtc` to kill the per-keystroke `File.ReadAllLines`. Do NOT
  cache the scan per query (the query changes every keystroke). Unit seam
  (Telescope.Tests): GrepFinder with an injected shared cache + counting
  enumerate delegate — `GetCandidates("a")` then `GetCandidates("b")`, assert
  enumerate ran once (RED today: no cache ctor param → compile error).
- **M6** (major, CONFIRMED) — fzf per-keystroke spawn, no timeout, silent
  fallback, `IsAvailable` never wired, `QuoteArg` trailing-backslash bug.
  `FzfFilter.cs:91,133,155`; `TelescopeOverlay.cs:374`. Fix: add a timeout via
  `Task.WhenAny(Task.WhenAll(outputTask, errorTask), Task.Delay(FilterTimeoutMs, token))`;
  on timeout `TryKill(p)` + fall back; log `[Telescope] fzf filter failed: ...`
  in the catch + timeout path; call `IsAvailable()` once at overlay open and
  log `[Telescope] fzf unavailable — showing unfiltered list`; fix `QuoteArg`
  (double trailing backslashes before the closing quote, or use
  `ProcessStartInfo.ArgumentList` — net472 supports it). Unit seam
  (Telescope.Tests): `FzfFilter(string? fzfPath)` injected path — nonexistent
  path → fallback + log; a stub exe that hangs → timeout+kill+fallback; a pure
  `QuoteArg` helper test for the trailing-backslash case. NOTE: M20's
  `Run_FzfFilter_FilterMatchesPrefix` silently passes when fzf is absent — the
  new tests must use the injected-path seam, not the real binary.
- **M7** (major, CONFIRMED — CORRECTION: the `preview caret=` log reads the
  pure TEXT model, so a `CaretToPointer` desync is e2e-INVISIBLE, worse than
  stated) — `CaretToPointer` O(n) re-walk per caret move.
  `PreviewRenderer.cs:122`. Fix: build a line-start index once in `SetContent`
  and binary-search it in `CaretToPointer`. Unit seam (Telescope.Tests):
  extract a pure `LineIndex` helper (text → line-start offsets; `LineOf(index)`
  via binary search); RED test: `LineIndex.LineOf(i)` ==
  `TextMotionNavigator.LineNumber` for a sweep of indices over multi-line
  inputs incl. trailing/leading `\n`, empty lines, `\r\n`. **Coordinate with
  M11** — both need the same pure index extraction; extract once and share.
- **M8** (major, PARTIAL — asymmetry confirmed; "fzf missing → unobserved
  exception" refuted because `FilterAsync` swallows internally; real escapes =
  the `OperationCanceledException` rethrow + faults in
  `RenderResults`/`PreviewRenderer.Show` inside the `Dispatcher.BeginInvoke`
  action) — fire-and-forget filter with unobserved exceptions.
  `TelescopeOverlay.cs:349,372-393`. Fix: wrap the `FilterAndUpdateAsync` body
  in try/catch and log `[Telescope] filter failed: {ex.Message}` (matching the
  sibling `query gather failed:` contract); catch `OperationCanceledException`
  separately and return silently. Unit seam (Telescope.Tests): extract a pure
  `FilterFailureLog.Format(Exception)` returning the exact
  `[Telescope] filter failed: <msg>` line (RED: no `filter failed` string
  exists today), OR introduce an `IFzfFilter` abstraction with a
  fault-injecting fake (stronger, requires the small refactor).

### Phase 3 — Preview/motion correctness (M9, M10, M11)

- **M9** (major, PARTIAL — described bug refuted: `i += 2` is in the
  backslash-escape branch, not "after a quote"; the quote branch does `i++`;
  REAL bug: an unterminated string ending in a backslash → `i += 2` pushes
  past the end → `ArgumentOutOfRangeException` → the whole preview fails with
  "preview load failed") — `SyntaxHighlighter.cs:198`. Fix: guard the escape
  advance — `i += (i + 1 < text.Length) ? 2 : 1;`. Unit seam (Telescope.Tests):
  `Run_Syntax_UnterminatedStringEndingInBackslash` — `Segment("var s = \"abc\\")`
  must not throw and must round-trip (RED today: throws).
- **M10** (major, CONFIRMED — trigger corrected: a leading blank line + `Up()`
  → `LastIndexOf('\n', -1)` → `ArgumentOutOfRangeException`; line 0 itself is
  guarded) — `TextMotionNavigator.cs:120`. Fix: clamp —
  `_text.LastIndexOf('\n', Math.Max(0, lineStart - 2))`. Unit seam
  (Telescope.Tests): `Run_Preview_UpFromSecondLineWithLeadingBlankLine` —
  `SetText("\nabc"); MoveToLine(2); Up();` must not throw and stay on line 2
  (RED today: throws).
- **M11** (major, CONFIRMED) — `CaretToPointer` returns null on a blank line →
  caret placement silently no-ops (the `preview caret=` log is still emitted,
  so e2e passes). `PreviewRenderer.cs:141`. Fix: at the top of each `Paragraph`
  block, `if (index == plain) return para.ContentStart;` (blank-line fallback
  to paragraph start). Unit seam (Telescope.Tests): `CaretToPointer` is private
  + WPF-coupled — first extract the pure index→(line, offset) mapping (shared
  with M7's `LineIndex`), then `Run_Preview_CaretOnBlankLine` asserts the blank
  line maps to its own start (not null/next line).

### Phase 4 — Navigation robustness (M2, M12)

- **M2** (major, CONFIRMED — low-end major: in-proc COM) — per-keystroke N+1
  `GetWindowScreenRect` COM calls. `WindowMatrix.cs:105-106`;
  `WindowAdapter.cs:33,120-127`. Fix: single-pass snapshot in
  `NavigateInDirection` — `List<RectCoordinate> rects = m_ActiveWindows.Select(w => w.Rect).ToList();`
  then `int activeIndex = m_ActiveWindows.IndexOf(m_activeWindow);` and
  `RectCoordinate active = rects[activeIndex];` (guard `activeIndex < 0` →
  no-op); pass `active` + `rects` to `SelectTarget`. Extract the snapshot
  derivation into a pure `NavigationSnapshot` helper. Cleanup: drop the
  write-only `_rect` field (`WindowAdapter.cs:20,125,126`) and fix the
  misleading `WindowAdapter.cs:30-31` comment. Unit seam (NeoVisual.Tests):
  `Run_NavigationSnapshot_ActiveComesFromSnapshot` — `snapshot.Active ==
  snapshot.Candidates[activeIndex]` (RED: helper doesn't exist → compile
  error). The 10 existing `Run_WindowNavigationEngine_*` tests prove
  `SelectTarget` behavior is unchanged.
- **M12** (major, CONFIRMED — worse than stated: `AutoHides()` outside the
  try/catch propagates through `InputHandler.Navigate` and
  `LeaderSequenceMatcher.action()` into the hook path) —
  `WindowMatrix.cs:98,106`; `WindowAdapter.cs:120-127`. Fix: cache
  `_frame4 = _frame as IVsWindowFrame4` in the ctor; `RefreshRect` null-checks
  it and returns `RectCoordinate.Empty` on null (the engine already excludes
  empty rects via `!c.IsEmpty`); wrap `AutoHides()` in the same per-frame
  try/catch. Unit seam (NeoVisual.Tests): extract
  `internal static RectCoordinate? TryGetScreenRect(IVsWindowFrame4? frame4)`
  on WindowAdapter — RED: `TryGetScreenRect(null)` returns null (current code
  throws InvalidCastException); plus assert an empty rect is excluded by
  `SelectTarget` (mirrors the existing hidden-zero-rect test).

### Phase 5 — Finder correctness (M13, M14)

- **M13** (major, CONFIRMED) — `Classify()` guesses kind by description
  substring instead of `ErrorItem.Severity`; unreachable by the test seam.
  `CodeIssuesFinder.cs:172,186-198`. Fix: replace `Classify(item.Description)`
  with `ClassifySeverity(item.Severity)` mapping `vsBuildErrorLevelHigh→Error`,
  `Medium→Warning`, `Low→Info` (default→Info); delete the old `Classify(string)`
  (only caller is :172). Unit seam (Telescope.Tests):
  `Run_Issues_SeverityMediumMapsToWarning` (a warning whose message contains
  "error" must still be Warning), `Run_Issues_SeverityHighMapsToError`,
  `Run_Issues_SeverityLowMapsToInfo` (RED: `ClassifySeverity` doesn't exist →
  compile error). `vsBuildErrorLevel` resolves in the test process
  (`Microsoft.VisualStudio.Interop` already referenced).
- **M14** (major, CONFIRMED — line corrected to `SolutionExplorerController.cs:335-362`)
  — `BuildForest` nested-folder expansion gap, DTE-coupled and untested. Fix:
  extract a pure `HierarchyForestBuilder.Build(IEnumerable<HierarchyItemInfo>,
  Dictionary<string,string> pathToItem)` mirroring `HierarchyResolver`; a thin
  DTE adapter in the controller maps `UIHierarchyItem` → `HierarchyItemInfo`
  (the only place `Kind`/`Name`/`FileNames[FileCount]` are read). Unit seam
  (NeoVisual.Tests): nested-folder recursion produces nested `HierarchyNode`
  children; `.cs` filter (OrdinalIgnoreCase — `.CS` included); full path flows
  through into `FilePath` + the path map; non-folder/non-file kinds skipped;
  empty children → empty forest (RED: logic buried in DTE code today).

### Phase 6 — Hook-path safety (M15, M16, M17)

- **M15** (major, CONFIRMED) — `action()` invoked with no try/catch inside the
  hook path. `LeaderSequenceMatcher.cs:63`. Fix: catch inside
  `LeaderSequenceMatcher.HandleKey` around `action()` and return a
  failure-carrying result (new `LeaderResultKind.Failed` + exception message on
  `LeaderResult`); `InputHandler.HandleKey` logs
  `[NeoVisual] leader-binding failed: {result.Sequence}: {ex.Message}` —
  matching the existing `leader-binding executed:` contract. **Sibling hole:**
  `InputHandler.cs:351` `simpleAction()` has the identical unguarded invocation
  — fix both (`[NeoVisual] shortcut-binding failed: ...`). Unit seam
  (NeoVisual.Tests): `Run_LeaderMatcher_ThrowingActionIsCaught` — bind
  `["F"] = () => throw new KeyNotFoundException("bad finder")`, drive Space then
  F, assert `result.Kind == Failed` and no exception escapes (RED today:
  throws at :63).
- **M16** (major, PARTIAL — the `isTextInputSurface` exemption is a deliberate
  D4 deviation required for `neovisual-textinput-motions`; REAL leak vector: a
  text-input surface in NORMAL mode while the editor holds focus → moves the
  editor's caret + `a`/`A`/`I` corrupt the unfocused controller's `_isInputMode`)
  — `FocusGuard.cs:26,35` + `InputHandler.cs:90-93`. Fix: the current signature
  cannot distinguish "text-input surface genuinely focused" from "editor
  genuinely focused" (both pass `editorFocused=true, isTextInputSurface=true`).
  Add a reliable `textInputSurfaceFocused` signal (track the tool-window
  frame's focus or WPF `Keyboard.FocusedElement` containment), add a
  `textInputSurfaceFocused` parameter to the guard, and re-derive BOTH
  `FocusGuard` truth tables AND `EditorFocusedVeto` in lockstep. Unit seam
  (NeoVisual.Tests): `HasToolWindowActionKeys(isToolWindow: true, isInputMode:
  false, actionKeyCount: 6, editorFocused: true, isTextInputSurface: true,
  textInputSurfaceFocused: false)` must be FALSE (currently TRUE); same for
  `ShouldRouteToolWindowKey`. MUST NOT regress the D4 deviation
  (`neovisual-textinput-motions`).
- **M17** (major, PARTIAL — 2/3 confirmed; the `(int)value` cast is refuted;
  effect direction corrected: both confirmed bugs make `_cachedTyping` wrongly
  FALSE → in insert mode Space starts a leader instead of typing a literal
  space) — `VimModeTracker.cs:189,209,488`. Fix: route all `_cachedTyping`
  writes through one owner method that owns the field + the `vim-mode=` log;
  clear on LostFocus/Closed only when `ReferenceEquals(_focusedView, view)`
  (mirror the `_editorFocused` guard); don't latch `_resolved` (set `_resolved
  = true` only on success). Unit seam (NeoVisual.Tests): extract a pure
  `VimModeState` owner (coordinate with M32); tests:
  `Run_VimModeState_OutOfOrderLostFocusKeepsTyping` (RED on :189),
  `Run_VimModeState_ClosedNonFocusedKeepsTyping` (RED on :209),
  `Run_VimModeState_ClassificationTruthTable` (Normal→false, Insert→true,
  Replace→true, null→false + names Normal/Insert/Replace/Unknown — pins the
  `vim-mode=` contract), `Run_VimModeState_ResolutionRetriesAfterFailure`
  (RED on :488).

### Phase 7 — Logging pipeline (M18, M27, M35, M43, m13, m20, m23, m24, m25)

- **M18** (major, CONFIRMED — corrected: the harness reads the log FILE not the
  pane, so the impact is the human-visible pane + diagnosability) —
  `NeoVisualLog.cs:120-123,145-148,152-155`. Fix: emit a ONE-TIME fallback line
  `[NeoVisual] output pane unavailable: <reason>` via `LogFileWriter.Write`
  (the file path — NEVER recurse into `Log`, which would re-enter
  `WriteToPane`); guard with a `bool _paneFailureLogged`; serialize
  `_pane`/`_paneInitTried` reads/writes with a `lock` (mirror
  `LogFileWriter.Sync`). Unit seam (Telescope.Tests): extract a pure
  `PaneFailureTracker` (returns the fallback message + whether to emit); test:
  two pane failures → exactly ONE fallback line lands in the temp `LogPath`
  file (RED today: zero fallback lines).
- **M27** (major, PARTIAL — `Debug` alias byte-identical confirmed; `Write`/
  `WriteDebug` structurally identical not byte-identical; `TraceListener.Write`/
  `WriteLine` byte-identical; 35 `Debug` call sites not 30) —
  `NeoVisualLog.cs:79`; `LogFileWriter.cs:103-134`; `NeoVisualTraceListener.cs:18-32`.
  Fix: add `private static void WriteTo(ref StreamWriter? writer, string path,
  string message)` holding the shared body; `Write`/`WriteDebug` delegate to it;
  delete `NeoVisualLog.Debug` and switch all 35 call sites to `NeoVisualLog.Log`
  (behavior-preserving — Debug is a pure alias; every call site already carries
  its `DiagnosticLog.*` prefix); collapse `NeoVisualTraceListener.WriteLine` to
  delegate to `Write`. Unit seam (Telescope.Tests): the existing
  `Run_LogFileWriter_WritesAndClearsFile` + `Run_LogFileWriter_Buffered_*`
  prove the `WriteTo` refactor preserves behavior; the Debug-alias deletion is
  compile-time verified (35 sites).
- **M35** (major, CONFIRMED) — `Write`/`WriteDebug` swallow every exception —
  the log pipeline is undiagnosable when it fails. `LogFileWriter.cs:112,129`.
  Fix: add `internal static int WriteFailureCount` incremented in each catch
  (under `lock(Sync)`); for the harness (which cannot read in-process static
  state), add a one-time marker-file fallback (`neovisual-write-failed`
  sentinel in the log dir) so the harness fails fast instead of timing out.
  Preserve the never-throw contract. Unit seam (Telescope.Tests):
  `Run_LogFileWriter_WriteFailureCountIncrements` (+ WriteDebug variant) — RED:
  `WriteFailureCount` doesn't exist → compile error; force failure by setting
  `LogPath` to a path whose parent is a file; assert no exception + count delta
  == 1. Must NOT call `Clear()` (once-per-process flag).
- **M43** (major, CONFIRMED) — 200ms flush timer fires forever.
  `LogFileWriter.cs:206`; `NeoVisualLog.cs:82`. Fix: **Option A** — one-shot
  `new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite)` re-armed
  on write (`_flushTimer?.Change(200, Timeout.Infinite)` after `GetWriter`).
  Option B (dispose on `Clear`) does NOT fix idle wakeups — reject it. Unit
  seam (Telescope.Tests): add an internal static `FlushCount` incremented in
  `FlushLocked`; test: write → wait ~250ms → `FlushCount >= 1`; wait ~500ms
  with no writes → `FlushCount` unchanged (RED today: periodic timer keeps
  climbing).
- **m13** (minor, PARTIAL — the literal prefix is centralized in
  `DiagnosticLog.Telescope`; 6 sites still bypass `TelescopeLog.Log` via
  `NeoVisualLog.Debug`) — `TelescopeOverlay.cs:746`, `TelescopeController.cs:56`,
  `FinderBase.cs:37,60`, `CodeIssuesFinder.cs:182`, `GrepFinder.cs:92`. Fix:
  replace `NeoVisualLog.Debug(...)` → `TelescopeLog.Log(...)` at all 6 sites,
  dropping the `DiagnosticLog.Telescope` concatenation (TelescopeLog.Log adds
  it); consolidate `FinderBase.OnSelected`'s double-log (one line per failure);
  change `GatherErrorLiteral` to return the UNPREFIXED message (avoid
  double-prefix). Unit seam (Telescope.Tests): RED test 1 — reuse the
  `Run_FinderBase_OpenErrorSwallowed` pattern, assert EXACTLY ONE log line per
  failure (RED today: 2); RED test 2 — prefix-consistency: every finder error
  line starts with `[Telescope] ` and matches the canonical format.
- **m20** (minor, CONFIRMED — 3 error-reporting mechanisms in one slice;
  bonus: `FinderBase.OnSelected` double-logs) — same fix as m13 (route through
  `TelescopeLog.Log`).
- **m23** (minor, CONFIRMED) — `Clear()` once-per-process vs `ShowOverlay`
  Clear-on-open — logs accumulate across opens. `LogFileWriter.cs:88`;
  `TelescopeOverlay.cs:265`. Fix: do NOT make Clear truncate per-open (that
  breaks the harness's fixed-log-baseline contract — `Reset-LogBaseline`
  records a line-count baseline; a mid-run truncation leaves
  `LogBaseline > LogCache.Count` and `GetRange` throws). Instead REMOVE the
  misleading `NeoVisualLog.Clear()` from `ShowOverlay` and fix the doc to
  "once per process". No unit seam (the fix removes a VS-coupled call).
- **m24** (minor, CONFIRMED) — `-main.log`/`-exp.log` suffix inversion.
  `NeoVisualLog.cs:60-61`. Fix: rename-or-document. Rename to unambiguous
  `-structured.log`/`-debug.log` (a coordinated 4-file change incl. harness
  `test-e2e.ps1:1834`, `iterate-telescope.ps1:45-46` + test literals
  `Program.cs:111-112`), OR document and fix the stale
  `MyExtensionPackage.cs:56-57` comment. No unit seam (string literals in the
  VS-coupled facade) — doc/rename-only.
- **m25** (minor, PARTIAL — documented-accepted per spec.md §2.2) —
  `DiagnosticLog.cs:9`. **Leave as-is**; note in the plan; do not "fix"
  (moving the constants contradicts the spec decision).

### Phase 8 — Duplication merges (M21-M30)

- **M21** (major, CONFIRMED — byte-identical, no drift yet; the shipped `"/"`
  default is a live contract) — `KeyToString` duplicated verbatim.
  `InputHandler.cs:469-478` + `LeaderSequenceMatcher.cs:97-106`. Fix: one
  shared pure `internal static class KeyNames { public static string
  ToString(Keys key) }` in the **MyExtension** namespace
  (`MyExtension/KeyNames.cs`); both copies call it. Unit seam (NeoVisual.Tests):
  `Run_KeyNames_PrintableMappings` (`OemQuestion→"/"`, `Oemplus→"+"`,
  `OemMinus→"-"`, `Keys.F→"F"` — RED: class doesn't exist → compile error);
  `Run_KeyNames_RoundTrip_LeaderSequence` (bind `"/"`→action, drive Space +
  OemQuestion → Execute, Sequence=="/" — pins the shipped default);
  `Run_KeyNames_RoundTrip_SimpleShortcut` (`Ctrl+/` via `LoadFromJson`).
- **M22** (major, CONFIRMED) — 3rd DTE walker: `FileFinder.CollectProjectFiles`
  + `FindFirstSourceFileInItems` duplicate `ProjectFiles.Enumerate`.
  `FileFinder.cs:103`; `MyExtensionPackage.cs:285`. Fix: `FileFinder.GatherHits`
  calls `ProjectFiles.Enumerate(_dteFactory())` + maps to `FileHit` (delete the
  private `CollectProjectFiles`/`CollectItems`); the package's
  `OpenFirstSourceFile` uses `ProjectFiles.Enumerate(dte).FirstOrDefault(p =>
  p.EndsWith(".cs", OrdinalIgnoreCase))` (feasible: `ProjectFiles` is internal
  + `InternalsVisibleTo`). Unit seam (Telescope.Tests): the pure-reuse fix
  alone is NOT offline-RED-provable (FileFinder's hermetic ctor short-circuits
  the DTE branch) — extract the tree-walk into a pure helper over a minimal
  abstraction (children + path accessors), consumed by `ProjectFiles.Enumerate`,
  `FileFinder.GatherHits`, and the package's first-`.cs`; RED tests prove
  solution-folder recursion, nested-item recursion, dedup, `File.Exists`
  filtering, first-`.cs` selection against a fake tree.
- **M23** (major, CONFIRMED — byte-identical) — `OpenReference`/
  `OpenImplementation`. `MyExtensionPackage.cs:442-466`. Fix: one
  `private void OpenHitAtLine(IFileLocation hit)` (ThrowIfNotOnUIThread +
  File.Exists guard + `OpenFileAtLine`); lambdas become `hit => OpenHitAtLine(hit)`
  (both hit models are implicitly convertible to `IFileLocation`). Unit seam
  (Telescope.Tests): extract a pure `HitOpener.OpenAtLine(IFileLocation,
  Action<string,int> openAtLine)`; tests: existing temp file → delegate invoked
  with (path,line); missing file → delegate not invoked; both a `ReferenceHit`
  and an `ImplementationHit` flow through the same helper (RED: helper doesn't
  exist → compile error).
- **M24** (major, CONFIRMED — `$` drift LIVE: Shift-gated in prompt, bare in
  preview) — vim key→motion dispatch triplicated.
  `TelescopeOverlay.cs:566,606`; `TextMotionHelper.cs:70`. Fix: one pure
  `TryDispatch(Key key, bool shift, TextMotionNavigator nav)` in the Telescope
  namespace beside `TextMotionNavigator` (returns handled; mutates nav; `shift`
  passed as a parameter — each surface keeps its own shift source: WPF
  `Keyboard.Modifiers` for prompt/preview, `GetAsyncKeyState` for tool-window);
  handle the union h/l/j/k/w/b/e/0/$/gg/G + a/A/I (return an insert-placement
  indicator). Each surface keeps ONLY its apply-caret step + its own diagnostic
  log line (`prompt-motion key=...`, `preview caret=...`, `text-motion key=...`
  / `textinput-enter-input ...` — logging-as-contract must NOT move into the
  shared function). Unit seam: rewrite the NeoVisual.Tests MapMotion groups to
  target `TryDispatch`; add Telescope.Tests RED tests for the unified `$`
  contract — `TryDispatch(Key.D4, shift:false, nav)` must NOT LineEnd (fails
  against current preview semantics).
- **M25** (major, CONFIRMED — 3 sites; 2 byte-identical factories) — block
  caret rendered in 3 places. `BlockCaretAdornment.cs:48-60`;
  `TextMotionHelper.cs:39,301-308`; `TelescopeOverlay.cs:62,64-71,593-603`.
  Fix: new `Telescope/BlockCaretStyle.cs` (static): pure constants
  `WhiteFill`/`GlyphColor`/`BlockRect(8,16)` + WPF factory
  `CreateBlockBrush()` returning the frozen white `DrawingBrush` (single shared
  static instance); `TelescopeOverlay` + `TextMotionHelper` consume it;
  `BlockCaretAdornment` consumes the color constants. Do NOT build a full
  "caret renderer" abstraction (the three mechanisms genuinely differ). Unit
  seam (Telescope.Tests): RED = test referencing `BlockCaretStyle.CreateBlockBrush()`
  fails to compile; post-refactor: brush frozen, fill white, rect 8x16, glyph
  black; stronger dedup RED (NeoVisual.Tests): `TextMotionHelper.BlockCaretBrush`
  is reference-equal to the shared instance.
- **M26** (major, CONFIRMED — already diverged: one injects Escapes, one
  doesn't) — focus-keeper `DispatcherTimer` idiom duplicated.
  `SolutionExplorerController.cs:114,241`. Fix: one `FocusKeeper.Run(TimeSpan
  interval, int durationMs, Action tick)` (internal static, new file
  `MyExtension/ToolWindows/FocusKeeper.cs`) owning the timer lifecycle
  (construction, interval, the `keeperRef` closure-capture workaround,
  `TickCount + durationMs` stop, Start, per-tick try/catch); callers supply
  only the divergent tick body (Site 1: Escape-injection + re-assert; Site 2:
  plain re-assert). Unit seam (NeoVisual.Tests): the timer lifecycle is
  WPF-coupled — extract a pure `FocusKeeperSchedule` returning
  `{InjectEscape, Reassert, Stop}` from `(bool searchBoxFocused, int elapsedMs,
  int escapeAttempts)`; RED: test references `FocusKeeperSchedule` (doesn't
  exist → compile error).
- **M28** (major, CONFIRMED — 5 helpers/22 sites, 9× SE-toggle loop, 3 wait
  helpers, 2× seed content) — harness duplication. `test-e2e.ps1:259,286,308,331,355`;
  `harness-common.ps1:134,154,175`. Fix: one `Open-TelescopeFinder -Vs -LogPath
  -Key -Finder` (keep the 3-attempt loop + focus discipline), one
  `Ensure-SolutionExplorerOpen` (4-iteration Space+E loop), one
  `Wait-LogLine -LogPath -Pattern -FromIndex -PollMs -MaxMs` (keep the three
  names as thin shims preserving each default), one `$script:SeedCanonical`
  hashtable shared by `Reset-ScratchSolution` + `Assert-SeedConsistent`. Do NOT
  fold `neovisual-explorer-toggle` into `Ensure-SolutionExplorerOpen` (it
  asserts open AND close). Verification seam (no VS): add a `-SelfCheck`/
  `-DryRun` switch — (1) `Wait-LogLine` against a temp log with `PollMs=1`;
  (2) `Reset-ScratchSolution` + `Assert-SeedConsistent` on a temp dir;
  (3) stub `Send-Tap`/`Bring-ToForeground` and assert the emitted VK sequence
  per finder.
- **M29** (major, CONFIRMED — 23× temp-dir, 8× LogPath save/restore, duplicate
  MapMotion groups) — test scaffolding copy-pasted. `Telescope.Tests/Program.cs:110,113`;
  `NeoVisual.Tests/Program.cs:262`. Fix: in shared `tests/TestRunner.cs`
  (already linked into both projects): `sealed class TempDir : IDisposable`
  (ctor builds a Guid temp dir, `CreateDirectory` idempotent, Dispose deletes
  recursively) + `WithLogPath(string logPath, Action body)` (save/set/run/
  restore in try/finally; add a DebugLogPath variant for the one site at
  113-114). Replace the 23 temp-dir blocks + 8 save/restore sites; delete the
  duplicate MapMotion groups (coordinate with M42 — consolidate once). Helper
  methods must avoid the `Run_` prefix (TestRunner discovers by prefix).
  Verification: `dotnet run --project tests/Telescope.Tests` → count unchanged
  (behavior-preserving); `tests/NeoVisual.Tests` → 81 → 79 (the −2 drop is the
  deterministic signal only the duplicates were removed).
- **M30** (major, CONFIRMED — no drift today; failure = `KeyNotFoundException`
  in the hook callback) — action-name→finder-name mapping split across
  `Actions.Registry` + `TelescopeLauncher.FinderNames`. `Actions.cs:22-26`;
  `TelescopeLauncher.cs:25-33`. Fix: make `FinderNames` the single source of
  truth and DERIVE the Registry telescope entries from it (iterate
  `FinderNames`, add `(_, l) => () => l.Open(finderName)`). Unit seam
  (NeoVisual.Tests): a consistency test asserting set-equality of Registry
  telescope keys ↔ map keys (RED: compile-error before the refactor); also
  strengthen `Run_ActionsRegistry_TelescopeMapsToFinder` from hard-coded values
  to set-equality (today it would NOT catch a 6th telescope action added
  without a FinderNames entry).

### Phase 9 — Testability seams (M31, M32, M33, M34)

- **M31** (major, CONFIRMED) — simple-shortcut matching buried in the
  VS-coupled `HandleKey`. `InputHandler.cs:345-353`. Fix: extract a
  dependency-free `SimpleShortcutMatcher` mirroring `LeaderSequenceMatcher`
  (`SimpleShortcutResult { Kind: PassThrough|Execute, Action, Sequence }`);
  move `BuildSimpleKey` + `KeyToString` into it, sharing ONE `KeyNames.ToString(Keys)`
  with `LeaderSequenceMatcher` (do NOT copy a third KeyToString — ties into
  M21); `InputHandler.HandleKey` shrinks to consume the result and log
  `shortcut-binding executed: {r.Sequence}` (the log stays in InputHandler —
  pure class stays VS-free). Unit seam (NeoVisual.Tests):
  `Run_SimpleShortcutMatcher_*` family mirroring `Run_LeaderMatcher_*` (RED:
  compile error): Ctrl+H executes + sequence "Ctrl+H"; no-modifier passes
  through; unbound chord passes through; modifier order Ctrl,Shift,Alt
  (Shift+F4); printable keys `/`,`+`,`-`; case-insensitive `ctrl+h` match.
- **M32** (major, CONFIRMED) — mode classifier + VsVim reflection interop not
  behind a pure seam. `VimModeTracker.cs:366-379,299-531`. Fix: extract a pure
  `VimModeClassifier.Classify(int? mode)` → `(bool IsTyping, string Name)`
  (constants Normal=1/Insert=2/Replace=7 move into it) + a narrow `IVimModeSource`
  seam (`int? GetModeKind(ITextView)`, `event Action<int?> ModeChanged`,
  attach/detach) with a fake for tests; `VimModeTracker` becomes a thin
  adapter (focus wiring + cached bools, delegating mode reads to the source,
  feeding the pure classifier, owning the `vim-mode=` log). Unit seam
  (NeoVisual.Tests): `Classify(2).IsTyping==true`, `Classify(7)==true`,
  `Classify(1)==false`, `Classify(null)==false`; names
  Normal/Insert/Replace/"99"/"Unknown" (RED: compile error); a fake
  `IVimModeSource` drives mode-change events and asserts the cached
  `IsInTypingMode` flips + the `vim-mode=` name contract. **Coordinate with
  M17** (same file — one combined refactor).
- **M33** (major, CONFIRMED) — `InitializeAsync` (blast radius 85) wraps ~10
  steps in ONE catch-all. `MyExtensionPackage.cs:116-119`. Fix: per-step
  try/catch emitting distinct diagnostics on the existing contract —
  `[MyExtension] init <step> ok` / `[MyExtension] init <step> failed:
  {ex.Message}` for each of the ~10 steps (telescope, finders, monitor-
  selection, window-manager, controllers, shell-wait, auto-open-solution, hook,
  command); replace the misleading "Failed to initialize keyboard hook" line;
  keep the outer catch as a last-resort net. Also protect the pre-try steps
  (:58-61 `ConfigureLogFile`/`Clear`/`InstallDebugListener`). Unit seam
  (Telescope.Tests or NeoVisual.Tests): extract the orchestration — a list of
  named `Func<Task>` steps, each wrapped in try/catch emitting a distinct
  diagnostic via an injected `Action<string>` log sink — into a dependency-free
  class; tests: a failing step logs its own named diagnostic and later steps
  still run; the diagnostic text names the step. The VS calls stay in the
  package.
- **M34** (major, CONFIRMED — line corrected to `TelescopeOverlay.cs:500-560`)
  — focus-target state machine buried in the WPF `OnPreviewKeyDown` override.
  Fix: extract a pure `FocusTargetModel` (mirror `OverlayKeyHandler`):
  `internal enum FocusTarget { List, Preview }` + `FocusTargetModel { Current;
  Reset(); Handle(OverlayKey key) → FocusTargetAction }`; add `CtrlH`/`CtrlL`
  to `OverlayKey` (mapped in `MapKey`, which already reads `Keyboard.Modifiers`);
  the model checks CtrlH/CtrlL first, then Escape-when-Preview (returns
  "handled" so the overlay skips the key handler), else None. The overlay
  delegates: feeds the mapped key to the model, applies `FocusTargetUi()` on
  the returned action, and logs `focus target={model.Current}` — preserving the
  `[Telescope] focus target=List|Preview` contract byte-identical. Unit seam
  (Telescope.Tests): `Run_FocusTarget_*` family (RED: compile error):
  StartsWithList, CtrlLMovesToPreview, CtrlHReturnsToList,
  EscapeInPreviewReturnsToList, EscapeInListUnchanged, ResetOnOpen.

### Phase 10 — Harness hardening (M19, M36, M37, m26, m27, m28, m29)

- **M19** (major, CONFIRMED — fix-direction correction: `Assert-Budget` is a
  checkpoint stopwatch that CANNOT interrupt a blocking COM call) —
  `tools/dte-command.ps1:80,89,92,93`. Fix: add a `-TimeoutSec` param (default
  ~60-120s; `Debug.Start` legitimately takes long) and wrap each COM call in a
  **background runspace** with a bounded wait (`[powershell]::Create()` +
  `BeginInvoke()` + `AsyncWaitHandle.WaitOne($TimeoutSec*1000)`; on timeout
  `$ps.Stop()` + throw a diagnostic + exit non-zero so the parent's teardown
  runs). The `GetActiveDocument` poll-loop call needs a short per-call timeout
  (~5s). Verification seam (no VS): a `-SelfTest` switch that runs the timeout
  wrapper against a deliberately-blocking stub (e.g. `Start-Sleep -Seconds 30`
  in the runspace), assert it aborts at ~2s with the timeout diagnostic and a
  non-zero exit.
- **M36** (major, CONFIRMED — also `:129,:262,:271` kill paths + `:106` env) —
  blanket `Get-Process devenv | Stop-Process -Force` kills the user's unrelated
  VS instances; USER-scope env mutation persists. `iterate-telescope.ps1:112,104`.
  Fix: port the `test-e2e.ps1` M-M5 pattern — track spawned PIDs,
  `Save-AllDocuments` before each kill, `Stop-SpawnedVs` on both exit paths,
  a `Stop-HarnessVs`-style title-scoped cleanup replacing the blanket kills;
  change `:104`/`:106` to Process scope (`NEOVISUAL_TEST_SOLUTION` must be
  Process-only; `NEOVISUAL_LOG_DIR` may stay User-scoped as a documented,
  opt-in decision). Verification seam (no VS): extract the kill into a shared
  function taking explicit PIDs; self-check: spawn two dummy processes, add one
  PID to the spawned list, run the kill, assert exactly one died; assert
  `NEOVISUAL_TEST_SOLUTION` User-scope is unchanged after the run.
- **M37** (major, CONFIRMED) — `$TimeoutSec = 300` declared but never enforced.
  `test-e2e.ps1:73`. Fix: before the scenario loop, start a budget stopwatch +
  `function Assert-Budget` (mirror `iterate-telescope.ps1:56`); call
  `Assert-Budget` at the TOP of each `foreach ($name in $selected)` iteration,
  BEFORE the `try` (inside the try it would be swallowed into `$failures` and
  degrade to a per-scenario FAIL); optionally wrap the loop in an outer
  try/catch printing `RESULT: TIMEOUT` + `exit 1`. Verification seam (no VS):
  extract `Assert-Budget` as a standalone function and unit-test it directly
  (fresh stopwatch → no throw; advanced past budget → throws with the expected
  message).
- **m26** (minor, CONFIRMED) + **m28** (minor, CONFIRMED — SAME root cause:
  cache-index vs fresh `Get-Content` mixing) — `test-e2e.ps1:831,938,1027`.
  Fix together: snapshot via the cache (`Update-LogCache $logPath;
  $script:LogCache.Count`) or add a `Get-LogCacheIndex` helper; scan
  `$script:LogCache` from `$script:LogBaseline` (both cache-side) in the
  editor-focus absence scan. Verification: deferred e2e
  (`neovisual-explorer-open(-o)`, `neovisual-explorer-move-editor-focus`).
- **m27** (minor, CONFIRMED) — `check-doc-refs.ps1:199-201,171-179`: missing
  doc silently skipped; `Test-ToolFunctionExists` regex-matches whole-file text.
  Fix: emit a warning issue for missing docs; match `^function\s+<token>`
  instead of whole-file regex. Verification: run the script against a doc
  referencing a deleted function.
- **m29** (minor, CONFIRMED) — `iterate-telescope.ps1:78`: scratch seeding gated
  on `Test-Path` — a stale scratch is reused. Fix: always reset (mirror
  `Reset-ScratchSolution`). Verification: run twice; second run must still be
  deterministic.

### Phase 11 — Test hermeticity (M20, M42, m30)

- **M20** (major, CONFIRMED — corrected refs: `Telescope.Tests/Program.cs:132-134,149-151`
  (not :774), `:286`, `NeoVisual.Tests/Program.cs:78`) — (a) LogFileWriter
  tests order-dependent on the once-per-process `_clearedThisProcess` flag;
  (b) `Run_FzfFilter_FilterMatchesPrefix` silently passes when fzf is absent;
  (c) `Run_Keybinding_DefaultFileHasTelescopeAndNav` reads the user's real
  `%APPDATA%\MyExtension\keybindings.json`. Fix: (a) make `Clear()` idempotent
  per-path — track cleared paths in a `HashSet<string>` and truncate each path
  only on its first `Clear()` for that path (preserves once-per-process for the
  real log paths while the test's unique Guid temp paths truncate regardless of
  order); (b) fail loudly when fzf is absent — throw from the test (or add a
  distinct "skipped" accounting to `TestRunner` so a skip is not counted as
  pass), using the `FzfFilter(string? fzfPath)` seam; (c) add
  `internal static KeybindingConfig LoadDefaults()` reading only the embedded
  resource (reuse `ReadEmbeddedDefault`) and have the test call it instead of
  `Load()`. Verification: `-- LogFileWriter` (7 tests), `-- Fzf` (now FAILS on
  a machine without fzf instead of PASS), `-- Keybinding` (passes even with a
  hostile user config). Count caveat: static count is 84 (Telescope)/81
  (NeoVisual) — AGENTS.md's 77/74 is stale; compare against `--list` output.
- **M42** (major, CONFIRMED) — test names lie: "StartInInsert/StartInNormal"
  only classify `IsTextInputType`; `TextInput_*` MapMotion tests duplicate
  `TextMotionEngine_*`. `NeoVisual.Tests/Program.cs:109,117,262,271`. Fix:
  rename 109 → `Run_ToolWindowMode_TextInputTypesClassified` and 117 →
  `Run_ToolWindowMode_NavigationTypesClassified`; delete
  `Run_TextInput_MapMotions` (262) + `Run_TextInput_MapInsertMotions` (271)
  (fully covered by the `TextMotionEngine_*` group). **Coordinate with M29**
  (same MapMotion group — consolidate once). Verification: NeoVisual.Tests
  81 → 79 (the −2 drop is the deterministic signal).
- **m30** (minor, CONFIRMED) — magic `actionKeyCount: 5` in FocusGuard tests.
  `NeoVisual.Tests/Program.cs:468,479,487,503,548,555`. Fix: add a named
  constant `private const int PositiveActionKeyCount = 5;` at the FocusGuard
  section header + a one-line comment "guard only checks > 0"; keep the `0`
  literal (the intentional zero case). Verification: NeoVisual.Tests count
  unchanged (81).

### Phase 12 — Naming/convention sweep (M38, M39, M40, M41, n1-n12)

- **M38** (major, CONFIRMED — only file without a namespace block; 7 code
  sites all use the simple name in `namespace MyExtension` — no reference
  changes needed) — `WindowManager.cs:9`. Fix: wrap in `namespace MyExtension`,
  drop the self-`using MyExtension;` (line 5), keep `using CardinalNavigation;`.
  Verification: `dotnet build` + NeoVisual.Tests.
- **M39** (major, CONFIRMED — perpendicular confirmed; 2 sites) —
  `Direction.cs:8` + `WindowNavigationEngine.cs:58`. Fix: rename `Axis()` →
  `PerpendicularAxis()` (keep the `Axis` enum name). No test touches the
  method. Verification: build + `-- WindowNavigationEngine` (the
  `Run_WindowNavigationEngine_*` tests pin the algorithm end-to-end).
- **M40** (major, CONFIRMED — single caller `InputHandler.Navigate` + 4
  `Actions.cs` sites) — `NavigateInDirection(char)` takes magic `'U'/'D'/'L'/'R'`.
  `WindowMatrix.cs:94`. Fix: change both signatures to `NavigateInDirection(Direction)`
  / `Navigate(Direction)`; delete `ToDirection` + the 4 char constants
  (`CardinalNavigationConstants.cs:5-8`); update the 4 `Actions.cs` sites to
  `Direction.Left/Right/Up/Down`. **CRITICAL log-line-as-contract:**
  `InputHandler.cs:483` logs `navigate direction={direction}` and the e2e
  harness asserts `navigate direction=L/R/D/U` — the fix MUST keep emitting a
  single-char token (derive `L/R/D/U` from `Direction`), or the deferred e2e
  `neovisual-window-nav` fails. Verification: build + `-- WindowNavigation`.
- **M41** (major, CONFIRMED — stub genuinely unreachable; `GatherHits()` is
  abstract in `FinderBase` so it can't be deleted) — `GrepFinder.cs:49`. Fix:
  `throw new NotSupportedException("GrepFinder is query-driven; call
  GetCandidates(query)")` in the stub (behavior-preserving — no callers today;
  converts a silent empty into a loud failure). Verification: build + `-- Grep`
  (all 9 Grep tests call `GetCandidates`, none touch `GatherHits`); a
  throw-assert test needs reflection (`typeof(GrepFinder).GetMethod("GatherHits",
  NonPublic|Instance)` → assert `NotSupportedException`) since GrepFinder is
  sealed.
- **n1** (nit, CONFIRMED) — `_keyboardLogger` holds a `GlobalKeyboardHook`.
  `MyExtensionPackage.cs:44`. Rename → `_keyboardHook`. Compiler-verified.
- **n2** (nit, PARTIAL — only `:693` `[Telescope]`→`[NeoVisual]` is safe; the
  `[MyExtension]` prefix at :61 is harness-pinned) — `MyExtensionPackage.cs:51,693`.
  Fix: `GetCaretOffset` :693 → `[NeoVisual]`; keep `[MyExtension]` for the
  harness-asserted auto-open lines. Verification: `Run_LogPrefixes_Pinned` +
  harness.
- **n3** (nit, CONFIRMED) — magic VK literals in the `toolwindow-move ... vk=40/38`
  log. `SolutionExplorerController.cs:44-45`. Fix: interpolate
  `(int)KeyInjection.VK_DOWN`/`VK_UP` (mirror `GeneralToolWindowController.cs:49`).
- **n4** (nit, CONFIRMED) — placeholder pane GUID + magic `CreatePane(..., 1, 1)`.
  `NeoVisualLog.cs:28,149`. Fix: real GUID constant + named args. No test seam
  (VS-coupled pane).
- **n5** (nit, CONFIRMED) — comments reference `neovascular-*` scenario names.
  `test-e2e.ps1:539,572,825,924,987`. Comment-only fix → `neovisual-*`.
- **n6** (nit, CONFIRMED) — ~125 raw hex VK codes with only 4 named constants.
  `test-e2e.ps1`. Fix: add named VK constants for the frequent codes and use
  them in `Send-Tap`. Verification: full e2e suite (deferred).
- **n7** (nit, CONFIRMED) — `$expHive` + `Find-VsWindow` dead.
  `iterate-telescope.ps1:48,68`. Delete both.
- **n8** (nit, CONFIRMED) — `_rect` field write-only. `WindowAdapter.cs:20,120-127`.
  Drop the field, return the local (also covered by M2's cleanup).
- **n9** (nit, CONFIRMED) — mixed field conventions. `WindowMatrix.cs:14,16,18`.
  Normalize to `_` prefix. Compiler-verified.
- **n10** (nit, PARTIAL — `LineStartHome` portmanteau confirmed; `InsertStart`/
  `InsertEnd` doc already says "text/line") — `TextMotionNavigator.cs:155,207,210`.
  Fix: rename `LineStartHome` → `LineStart`; either rename `InsertStart/End` →
  `TextStart/End` or leave (whole-text is likely intended for the preview).
  Verification: `-- Preview` pins caret positions.
- **n11** (nit, CONFIRMED) — `Keywords` PascalCase private field + `Segment(string)`
  verb/noun collision. `SyntaxHighlighter.cs:43,64`. Fix: `_keywords`; rename
  method → `Tokenize` (update `PreviewRenderer.cs:56` + Telescope.Tests call
  sites).
- **n12** (nit, CONFIRMED) — `segment.Text.Split('\n')` allocates a string[]
  per segment per render. `PreviewRenderer.cs:60`. Fix: scan with
  `IndexOf('\n')`/substring or `StringReader`. Verification: Telescope.Tests
  preview tests (behavior unchanged).

## Acceptance criteria

Every phase's acceptance criteria map to a diagnostic line and/or a unit test.
The master table (each row = a phase's gate):

| Phase | Gate (unit tests GREEN + build + existing suites) | Diagnostic contract preserved/added |
|-------|---------------------------------------------------|-------------------------------------|
| 0 | `Run_WindowManager_DefaultControllerFor`; CR2 gate snippet (no-VS); `Run_OverlayShowState_*` | none changed (CR1/CR3); CR2 is harness-only |
| 1 | `Run_StaleToolWindowSentinel_*`; `Run_KeyNameBuilder_*`; M1 deletion (build) | ADD `[NeoVisual] stale-toolwindow sentinel active`; DELETE per-key `[Hook] key=` |
| 2 | `Run_GrepFinder_Cache*`; `Run_FzfFilter_*` (injected path); `Run_LineIndex_*`; `Run_FilterFailureLog_*` | ADD `[Telescope] fzf filter failed: ...`, `[Telescope] fzf unavailable — showing unfiltered list`, `[Telescope] filter failed: ...` |
| 3 | `Run_Syntax_UnterminatedStringEndingInBackslash`; `Run_Preview_UpFromSecondLineWithLeadingBlankLine`; `Run_Preview_CaretOnBlankLine` | `preview caret=... line=...` unchanged |
| 4 | `Run_NavigationSnapshot_ActiveComesFromSnapshot`; `Run_WindowAdapter_TryGetScreenRect*` | `[NeoVisual] navigate direction=...` unchanged |
| 5 | `Run_Issues_Severity*`; `Run_HierarchyForestBuilder_*` | `[Telescope] opened issue: ...` unchanged |
| 6 | `Run_LeaderMatcher_ThrowingActionIsCaught`; `Run_FocusGuard_TextInputSurfaceFocused*`; `Run_VimModeState_*` | ADD `[NeoVisual] leader-binding failed: ...`, `[NeoVisual] shortcut-binding failed: ...`; `vim-mode=` unchanged |
| 7 | `Run_PaneFailureTracker_*`; `Run_LogFileWriter_*` (existing + WriteFailureCount + FlushCount); `Run_FinderBase_*` (single-log + prefix) | ADD `[NeoVisual] output pane unavailable: ...`; `[Telescope]`/`[NeoVisual]` prefixes unchanged |
| 8 | `Run_KeyNames_*`; `Run_HierarchyWalker_*`; `Run_HitOpener_*`; `Run_TryDispatch_*`; `Run_BlockCaretStyle_*`; `Run_FocusKeeperSchedule_*`; harness `-SelfCheck`; `Run_ActionsRegistry_*` (set-equality) | `prompt-motion key=...`, `preview caret=...`, `text-motion key=...` unchanged (per-surface logs stay) |
| 9 | `Run_SimpleShortcutMatcher_*`; `Run_VimModeClassifier_*`; `Run_InitSteps_*`; `Run_FocusTarget_*` | `shortcut-binding executed:` unchanged; `[Telescope] focus target=List|Preview` unchanged; ADD `[MyExtension] init <step> ok/failed` |
| 10 | harness `-SelfTest`/`-SelfCheck` (M19/M37/M36/m26-m29) | none changed |
| 11 | `-- LogFileWriter` (7), `-- Fzf` (fails without fzf), `-- Keybinding` (hermetic); NeoVisual 81→79 (M42) | none changed |
| 12 | build + `-- WindowNavigationEngine` + `-- Grep` + `-- Preview` | `navigate direction=L/R/D/U` MUST stay single-char (M40) |

## Unit test plan (summary)

- **tests/Telescope.Tests** (pure classes): `OverlayShowState` (CR3),
  `LineIndex` (M7/M11), `FilterFailureLog` (M8), `SyntaxHighlighter` (M9),
  `TextMotionNavigator` (M10), `PreviewRenderer` index mapping (M11),
  `CodeIssuesFinder.ClassifySeverity` (M13), `HitOpener` (M23),
  `TryDispatch` (M24), `BlockCaretStyle` (M25), `FocusTargetModel` (M34),
  `PaneFailureTracker` (M18), `LogFileWriter` WriteFailureCount/FlushCount/
  per-path Clear (M35/M43/M20), `FinderBase` single-log + prefix (m13/m20),
  `GrepFinder` cache + GatherHits throw (M5/M41), `FzfFilter` injected-path
  timeout/fallback/QuoteArg (M6), `HierarchyWalker` (M22).
- **tests/NeoVisual.Tests** (pure classes): `WindowManager.DefaultControllerFor`
  (CR1), `StaleToolWindowSentinel` (M3), `KeyNameBuilder` (m1),
  `NavigationSnapshot` (M2), `WindowAdapter.TryGetScreenRect` (M12),
  `HierarchyForestBuilder` (M14), `LeaderSequenceMatcher` Failed (M15),
  `FocusGuard` textInputSurfaceFocused (M16), `VimModeState`/`VimModeClassifier`
  (M17/M32), `KeyNames` (M21), `SimpleShortcutMatcher` (M31),
  `FocusKeeperSchedule` (M26), `InitSteps` (M33), `ActionsRegistry` set-equality
  (M30), `TempDir`/`WithLogPath` (M29), test renames (M42/m30).
- **No-seam** (build + existing suites + deferred e2e): M1 (deletion), M4,
  M19/M28/M36/M37/m26-m29 (harness — self-check seams), M33 (VS-coupled — pure
  orchestration seam), m2/m7, m16/m18, n4/n8, m23/m24 (doc/rename).

## Diagnostics (new/changed log lines)

- ADD `[NeoVisual] stale-toolwindow sentinel active` (M3).
- ADD `[NeoVisual] leader-binding failed: {sequence}: {ex.Message}` (M15).
- ADD `[NeoVisual] shortcut-binding failed: {sequence}: {ex.Message}` (M15 sibling).
- ADD `[NeoVisual] output pane unavailable: {reason}` (M18, one-time).
- ADD `[MyExtension] init <step> ok` / `[MyExtension] init <step> failed: {ex.Message}` (M33).
- ADD `[Telescope] fzf filter failed: {ex.Message}` (M6).
- ADD `[Telescope] fzf unavailable — showing unfiltered list` (M6, once at open).
- ADD `[Telescope] filter failed: {ex.Message}` (M8).
- DELETE per-key `[Hook] key=...` (M1).
- UNCHANGED (must stay byte-identical): `navigate direction=L/R/D/U` (M40),
  `shortcut-binding executed:` (M31), `leader-binding executed:` (M15),
  `[Telescope] focus target=List|Preview` (M34), `vim-mode=Insert|Normal|Replace`
  (M17/M32), `preview caret=... line=...` (M7/M11), `prompt-motion key=...`,
  `text-motion key=...` / `textinput-enter-input ...` (M24).

## Known-RED allowlist

None — no known-RED e2e scenario remains (per docs/progress.md). All fixes are
RED-proven by unit tests (or build + existing suites for no-seam items).

## E2E queue reference (deferred — see e2e-queue.md)

The following e2e scenarios are QUEUED (not created/executed until this plan is
GREEN in docs/progress.md and the user is on a VS-capable machine). Each asserts
the diagnostics listed above:

- **E2E-CR-1** (CR1): re-run all `neovisual-explorer-*` scenarios — prove
  `o`/`Enter`/`r`/`m`/`a`/`g`/`i` still route to `SolutionExplorerController`
  (`solution-explorer open/rename/move/add/select file=` lines).
- **E2E-CR-2** (CR2): a stub scenario returning `$false` → the run must exit 1
  with `RESULT: FAIL` (proves the gate catches `$false`).
- **E2E-CR-3** (CR3): open + immediately close the overlay repeatedly — no
  unhandled dispatcher exception; `overlay closed` still emitted.
- **E2E-M3** (M3): the `stale-toolwindow` sentinel scenario now asserts the
  positive `[NeoVisual] stale-toolwindow sentinel active` line (proves the
  fault was injected, not skipped).
- **E2E-M40** (M40): `neovisual-window-nav` — `navigate direction=L/R/D/U`
  single-char tokens unchanged after the `Direction` signature change.
- **E2E-M24** (M24): `telescope-prompt-motions` / `telescope-preview-motions` /
  `neovisual-textinput-motions` — per-surface log lines unchanged after the
  `TryDispatch` unification; the `$` drift fixed (bare `4` in preview no longer
  jumps).
- **E2E-M16** (M16): `neovisual-textinput-motions` (D4 deviation must not
  regress) + a new assertion that a text-input surface in normal mode while the
  editor holds focus does NOT move the editor's caret.
- **E2E-M34** (M34): `telescope-preview` — `[Telescope] focus target=List|Preview`
  unchanged after the `FocusTargetModel` extraction.
- **E2E-M17/M32** (M17/M32): `neovisual-editor-insert` + the focus-arrival
  signals — `vim-mode=Insert|Normal|Replace` unchanged.
- **E2E-M1/M4** (M1/M4): full suite — no `[Hook] key=` per-key lines; all
  `neovisual-*` scenarios still GREEN (hook hot-path contract restored).
- **E2E-M5/M6/M7/M8** (M5-M8): `telescope-grep` / `telescope-search` /
  `telescope-preview` — `grep hits=...`, `results count=...`, `preview caret=...`
  unchanged; fzf failure paths log `fzf filter failed:` / `filter failed:`.
- **E2E-M28/M36/M37/m26/m28** (harness): full suite + the `-SelfCheck`/
  `-SelfTest` seams; `seed-leak` still GREEN (no seed writes).
- **E2E-M20/M29/M42** (tests): full suite GREEN with the consolidated test
  scaffolding (counts 84/79 per the runner).

## Execution order note for neovim_hub

Execute phases in order 0 → 12. Phases 0-6 are behavior fixes (each RED-proven
by its unit tests); phases 7-9 are refactors with behavior-preserving unit
gates; phases 10-12 are harness/test/naming (self-check seams + build + suite
counts). The unit-only lane applies throughout — e2e is deferred to
`e2e-queue.md` (E2E-CR-1..3, E2E-M1..M42 above).

---

## Build Plan (per-phase; one implementation-planner agent per phase)

### Phase 0 - Criticals (CR1, CR2, CR3)

# Phase 0 Build Plan — Criticals (CR1, CR2, CR3)

> Source: `plans/plan.md` Phase 0 (code-review findings plan, session
> `neovim-planning-hub-20260929-085552`). Lane: **unit-only, e2e deferred** to
> `e2e-queue.md` (E2E-CR-1..3 QUEUED). Per the lane contract, **Verify-with and
> Fails-if reference unit test names + diagnostic formats ONLY** — no e2e
> scenario is used as a gate here. The deferred e2e gate is listed
> informationally at the end, never in a step's Verify-with/Fails-if.
>
> Phase 0 acceptance gate (from plan.md): `Run_WindowManager_DefaultControllerFor`
> (CR1); CR2 gate snippet (no-VS, harness-only); `Run_OverlayShowState_*` (CR3).
> **Diagnostic contract: none changed** (CR1/CR3 touch no log line; CR2 is
> harness-only). No type/method/file referenced by AGENTS.md / SKILL.md /
> docs/spec.md / docs/progress.md is renamed or removed, so no doc-ref update
> step is required (doc-ref lint `tools/check-doc-refs.ps1` must stay clean).

## Phase 0 — Criticals (CR1, CR2, CR3)

### BP-1 — CR1 RED test: `Run_WindowManager_DefaultControllerFor`

- **Files**: `tests/NeoVisual.Tests/Program.cs` (modify — add one test method in
  the tool-window controller section, after `Run_GeneralController_NoActionKeys`).
- **Change**: Add `public static void Run_WindowManager_DefaultControllerFor()`
  asserting the factory contract:
  - `WindowManager.DefaultControllerFor(ToolWindowType.SolutionExplorer) == null`
  - `WindowManager.DefaultControllerFor(ToolWindowType.Unknown) == null`
  - `WindowManager.DefaultControllerFor(ToolWindowType.CommandWindow) is TextInputToolWindowController`
  - `WindowManager.DefaultControllerFor(ToolWindowType.Toolbox) is GeneralToolWindowController`
  - RED: `WindowManager.DefaultControllerFor` does not exist → compile error
    (CS1061) — the whole NeoVisual.Tests project fails to compile until BP-2.
    This is the gap the plan calls out: the existing
    `Run_SolutionExplorer_ActionKeys` / `Run_ToolWindowMode_HjklMoves` construct
    controllers directly and never construct a `WindowManager`, so they give
    false confidence and cannot catch the overwrite.
- **Verify-with**: `dotnet run --project tests/NeoVisual.Tests --list` lists
  `Run_WindowManager_DefaultControllerFor`; the project is RED (does not
  compile) before BP-2 — expected.
- **Fails-if**: the test compiles and passes before BP-2 (the factory already
  exists or the test is wrong); or the test name is absent from `--list`.

### BP-2 — CR1 static pure factory `WindowManager.DefaultControllerFor`

- **Files**: `MyExtension/WindowManager.cs` (modify — add a static method; do not
  touch `RegisterController`/`GetController`).
- **Change**: Add
  `public static IToolWindowController? DefaultControllerFor(ToolWindowType type)`:
  - `if (type == ToolWindowType.SolutionExplorer || type == ToolWindowType.Unknown) return null;`
  - `return GeneralToolWindowController.IsTextInputType(type) ? new TextInputToolWindowController(type) : new GeneralToolWindowController(type);`
  - Pure static factory — no VS API, no `ThreadHelper.ThrowIfNotOnUIThread()`
    (the controller constructors are pure state). This is the single source of
    the "default controller for a type" decision; the package loop (BP-3) calls
    it and skips nulls, removing the registration-ordering dependency.
- **Verify-with**: `dotnet run --project tests/NeoVisual.Tests -- WindowManager`
  → `Run_WindowManager_DefaultControllerFor` passes (GREEN): SolutionExplorer
  and Unknown → null; CommandWindow → `TextInputToolWindowController`; Toolbox →
  `GeneralToolWindowController`.
- **Fails-if**: the test still fails to compile (wrong signature/namespace); or
  `DefaultControllerFor(SolutionExplorer)` returns a non-null controller (the
  overwrite bug persists).

### BP-3 — CR1 package rewiring (register only the specials)

- **Files**: `MyExtension/MyExtensionPackage.cs` (modify — replace the loop at
  lines 92-101; keep line 87 unchanged).
- **Change**: Replace the per-type loop body with the factory + null skip:
  ```csharp
  foreach (ToolWindowType type in Enum.GetValues(typeof(ToolWindowType)))
  {
      var controller = WindowManager.DefaultControllerFor(type);
      if (controller != null)
      {
          _windowManager.RegisterController(controller);
      }
  }
  ```
  Keep `_windowManager.RegisterController(new SolutionExplorerController(() => VsServices.Dte(this)));`
  (line 87) as the ONLY registration of the specialized SolutionExplorer
  controller. The loop now skips `SolutionExplorer` and `Unknown` (factory
  returns null), so the specialized controller is never overwritten by a
  `GeneralToolWindowController(SolutionExplorer)` regardless of registration
  order. The explicit `Unknown` `continue` (lines 94-97) is subsumed by the
  factory and may be dropped.
- **Verify-with**: `dotnet build` succeeds; `dotnet run --project tests/NeoVisual.Tests`
  → all existing tests pass (no regression), including
  `Run_SolutionExplorer_ActionKeys`, `Run_ToolWindowMode_HjklMoves`,
  `Run_ActionTable_*`, `Run_GeneralController_NoActionKeys`. No diagnostic
  change (CR1 changes no log line).
- **Fails-if**: build error (factory signature mismatch); or the loop still
  registers a `GeneralToolWindowController` for `SolutionExplorer` (overwrite
  persists — verify `DefaultControllerFor(SolutionExplorer)` is null and the
  loop skips it); or any existing NeoVisual test regresses.

### BP-4 — CR2 e2e runner false-PASS gate

- **Files**: `tools/test-e2e.ps1` (modify — insert after the try/catch at
  lines 1908-1910, before the per-scenario print at line 1911).
- **Change**: Add
  `if ($result -eq $false) { $failures += "$name : returned false" }`.
  Key on `$result -eq $false`, NOT `-not $ok` — `$ok` is `$false` in the throw
  case too, so `-not $ok` would double-count (exception message + "returned
  false"). `$result` is only assigned inside the `try` (line 1906), so it is
  `$null` (not `$false`) when an exception is caught → no double-count. This
  makes a `$false`-returning scenario land in `$failures`, so the final
  `if ($failures.Count -gt 0)` (line 1915) exits 1 instead of silently passing.
- **Verify-with**: standalone no-VS snippet replicating the gate (a stub
  scenario returning `$false`): before the fix it prints `RESULT: PASS` (exit
  0); after the fix it prints `RESULT: FAIL` (exit 1) with `$name : returned
  false` in the failure list. No unit test in the C# suites (harness-only).
- **Fails-if**: the snippet still prints `RESULT: PASS` for a `$false`-returning
  scenario (gate not catching it); or a throwing scenario adds BOTH the
  exception message AND "returned false" (double-count — means it was keyed on
  `-not $ok`).

### BP-5 — CR3 RED tests: `Run_OverlayShowState_*`

- **Files**: `tests/Telescope.Tests/Program.cs` (modify — add three test
  methods).
- **Change**: Add:
  - `Run_OverlayShowState_RequestThenClose_ShouldNotShow` —
    `var s = new OverlayShowState(); s.RequestShow(); s.Close(); Assert.False(s.ShouldShowDialog());`
  - `Run_OverlayShowState_RequestOnly_ShouldShow` —
    `var s = new OverlayShowState(); s.RequestShow(); Assert.True(s.ShouldShowDialog());`
  - `Run_OverlayShowState_StateBasedNotVisibilityBased` — documents the IsVisible
    trap: the decision is state-based (`RequestShow()` → true with no WPF
    visibility involved; `Close()` → false), NOT visibility-based (`IsVisible`
    is false at ApplicationIdle time, so a visibility guard would silently never
    open the overlay).
  - RED: `OverlayShowState` does not exist → compile error (CS0246) — the whole
    Telescope.Tests project fails to compile until BP-6.
- **Verify-with**: `dotnet run --project tests/Telescope.Tests --list` lists the
  three `Run_OverlayShowState_*` tests; the project is RED (does not compile)
  before BP-6 — expected.
- **Fails-if**: the tests compile and pass before BP-6 (the class already
  exists); or the test names are absent from `--list`.

### BP-6 — CR3 pure class `OverlayShowState`

- **Files**: `Telescope/OverlayShowState.cs` (new).
- **Change**: Add `internal sealed class OverlayShowState` in namespace
  `Telescope` (dependency-free — no WPF/VS; mirrors the
  `OverlayKeyHandler`/`TextMotionNavigator` pattern):
  ```csharp
  internal sealed class OverlayShowState
  {
      private bool _requested;
      private bool _closed;
      public void RequestShow() => _requested = true;
      public void Close() => _closed = true;
      public bool ShouldShowDialog() => _requested && !_closed;
  }
  ```
- **Verify-with**: `dotnet run --project tests/Telescope.Tests -- OverlayShowState`
  → all three `Run_OverlayShowState_*` tests pass (GREEN).
- **Fails-if**: `ShouldShowDialog()` returns true after `Close()` (the race
  persists); or the class does not compile.

### BP-7 — CR3 overlay wiring (guard the deferred `ShowDialog`)

- **Files**: `Telescope/TelescopeOverlay.cs` (modify — lines 297-320).
- **Change**:
  - Add field `private readonly OverlayShowState _showState = new();`
  - In `ShowOverlay` (line 297, alongside `IsOpen = true`): `_showState.RequestShow();`
  - In `CloseOverlay` (line 309, alongside `IsOpen = false`): `_showState.Close();`
  - Replace the `BeginInvoke` lambda (line 303) with a guarded one:
    `Dispatcher.BeginInvoke(new Action(() => { if (_showState.ShouldShowDialog()) ShowDialog(); }), DispatcherPriority.ApplicationIdle);`
    — guard with the state (equivalent to `IsOpen`), NOT `IsVisible` (IsVisible
    is false at ApplicationIdle time; an IsVisible guard would silently never
    open the overlay). Optionally wrap `ShowDialog()` in try/catch +
    `TelescopeLog.Log` (defensive only — Phase 0 adds no new asserted
    diagnostic). `IsOpen` and `_showState` are updated in the same two methods,
    so they cannot drift.
- **Verify-with**: `dotnet build` succeeds; `dotnet run --project tests/Telescope.Tests`
  → all existing tests pass (no regression), including the three
  `Run_OverlayShowState_*` tests. Diagnostic contract unchanged and
  byte-identical: `[Telescope] open finder=... candidates=...` (from
  `ShowOverlay`) and `[Telescope] overlay closed` (from `CloseOverlay`).
- **Fails-if**: the `BeginInvoke` lambda still calls `ShowDialog()` unconditionally
  (race persists); or the guard uses `IsVisible` (overlay silently never opens —
  the IsVisible trap); or the `[Telescope] open finder=` / `[Telescope] overlay
  closed` lines change.

## Verification Trace

| failing test / gate | implicated steps | expected pass signal |
|---|---|---|
| `Run_WindowManager_DefaultControllerFor` (RED: compile error — `WindowManager.DefaultControllerFor` missing) | BP-1, BP-2 | GREEN after BP-2: `DefaultControllerFor(SolutionExplorer) == null`, `DefaultControllerFor(Unknown) == null`, `DefaultControllerFor(CommandWindow) is TextInputToolWindowController`, `DefaultControllerFor(Toolbox) is GeneralToolWindowController` |
| `Run_SolutionExplorer_ActionKeys` / `Run_ToolWindowMode_HjklMoves` / `Run_ActionTable_*` / `Run_GeneralController_NoActionKeys` (existing — must not regress) | BP-3 | all pass after the package rewiring; no diagnostic change |
| CR2 gate snippet (no-VS, harness-only) | BP-4 | before fix: `RESULT: PASS` (exit 0); after fix: `RESULT: FAIL` (exit 1) with `$name : returned false` |
| `Run_OverlayShowState_RequestThenClose_ShouldNotShow` (RED: compile error — `OverlayShowState` missing) | BP-5, BP-6 | GREEN after BP-6: `RequestShow(); Close(); ShouldShowDialog() == false` |
| `Run_OverlayShowState_RequestOnly_ShouldShow` | BP-5, BP-6 | GREEN after BP-6: `RequestShow(); ShouldShowDialog() == true` |
| `Run_OverlayShowState_StateBasedNotVisibilityBased` | BP-5, BP-6 | GREEN after BP-6: decision is state-based, not visibility-based |
| `[Telescope] open finder=... candidates=...` / `[Telescope] overlay closed` (existing diagnostics — must stay byte-identical) | BP-7 | unchanged after the `BeginInvoke` guard |

## Known-RED allowlist

**None.** Per `docs/progress.md` and plan.md Phase 0, no known-RED e2e scenario
remains (`explorer-open-searchbox` GREened 2026-09-27; `telescope-implementation`
Enter-delivery fixed in `7c6569b`). The verification-agent must NOT flag any
Phase 0 unit-test failure as a pre-existing known-RED regression — every Phase 0
RED is a NEW test written for this phase.

## Deferred e2e gate (informational — NOT part of Verify-with/Fails-if)

QUEUED in `e2e-queue.md`; executed only after this plan is GREEN and a
VS-capable machine is available:
- **E2E-CR-1** (CR1): re-run all `neovisual-explorer-*` scenarios — `o`/`Enter`/
  `r`/`m`/`a`/`g`/`i` still route to `SolutionExplorerController`
  (`solution-explorer open/rename/move/add/select file=` lines).
- **E2E-CR-2** (CR2): a stub scenario returning `$false` → the run exits 1 with
  `RESULT: FAIL`.
- **E2E-CR-3** (CR3): open + immediately close the overlay repeatedly — no
  unhandled dispatcher exception; `[Telescope] overlay closed` still emitted.


### Phase 1 - Hook hot-path (M1, M3, M4, m1)

# Phase 1 Build Plan — Hook hot-path contract (M1, M3, M4, m1)

**Source:** `plans/plan.md` Phase 1 + Acceptance criteria + Unit test plan + Diagnostics sections.
**Lane:** unit-only, e2e deferred (e2e-queue.md). **Verify-with = unit test names + diagnostic formats ONLY** — no e2e scenario names in Verify-with/Fails-if.
**Scope:** M1 (delete per-key `[Hook] key=` log), M3 (sentinel cache + per-key-down refresh + positive diagnostic), M4 (cache `_isTextInputType` on WindowManager), m1 (KeyNameBuilder StringBuilder).

## Structural evidence (Trailmark 0.5.0, `language="c_sharp"`, proxy-aware)

- `_inputHandler.HandleKey` ← only `GlobalKeyboardHook.HookCallback` (:83); `_inputHandler.IsKeyOfInterest` ← only `GlobalKeyboardHook.IsInteresting` (:158) ← `HookCallback` (:120). **`IsKeyOfInterest` is the per-key entry; `HandleKey` is always preceded by it** → refreshing the sentinel at the entry of `IsKeyOfInterest` = exactly 1 `File.Exists` per key-down.
- `GeneralToolWindowController.IsTextInputType` callers: `InputHandler` :76, :93, :283, :396, :423 (the 5 M4 sites) + `MyExtensionPackage.InitializeAsync` :98 (registration-time, NOT per-key — leave) + `GeneralToolWindowController` ctor (self) + 2 tests.
- `InputHandler.BuildSimpleKey` ← only `HandleKey` (:346); `InputHandler.KeyToString` ← only `BuildSimpleKey`. Safe to delete both after m1.
- `_windowManager.IsToolWindow` readers: `InputHandler` :72, :283, :393, :423, :444 + `PopupNavigation` :44 (all read the cached property — behavior unchanged by M3).

---

## BP-1 — M1: delete the per-key `[Hook] key=` log write

- **Files:** `MyExtension/GlobalKeyboardHook.cs` (modify).
- **Change:** Delete line 124 — the per-key diagnostic inside the `IsInteresting` branch of `HookCallback`:
  `Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.Hook}key={key} ctrl={ctrl} shift={shift} alt={alt} leader={_inputHandler.IsLeaderActive}");`
  Keep the ctor's `[Hook] installed` line (:78) and `[Hook] INSTALL FAILED` line (:73) byte-identical. No other change to `HookCallback` (the `IsInteresting` gate, `HandleKey` dispatch, and `(IntPtr)1` swallow stay).
- **Verify-with:** `dotnet build` (deletion is compile-verified); existing `Run_LogPrefixes_Pinned` (tests/Telescope.Tests) still GREEN — pins `DiagnosticLog.Hook == "[Hook] "` unchanged. No new unit test (pure deletion).
- **Fails-if:** a `[Hook] key=` line still appears in the log (line not actually removed); the `[Hook] installed` / `[Hook] INSTALL FAILED` lines were accidentally deleted or altered.

## BP-2 — M3: pure `StaleToolWindowSentinel` class + RED tests

- **Files:** `MyExtension/StaleToolWindowSentinel.cs` (create); `tests/NeoVisual.Tests/Program.cs` (add tests).
- **Change:** New dependency-free class in `namespace MyExtension`:
  ```csharp
  internal sealed class StaleToolWindowSentinel
  {
      private readonly string? _path;
      private bool _isStale;
      public StaleToolWindowSentinel(string? path) { _path = path; }
      public bool IsStale => _isStale;
      public bool Refresh()   // returns true when IsStale changed
      {
          bool was = _isStale;
          _isStale = _path != null && System.IO.File.Exists(_path);
          return _isStale != was;
      }
  }
  ```
  No VS/WPF dependency. `Refresh()` returns "changed" so the caller can log the positive diagnostic on the false→true transition (BP-3).
- **Verify-with:** add to `tests/NeoVisual.Tests/Program.cs` (Tests class, `Run_` prefix):
  - `Run_StaleToolWindowSentinel_FileExistsAfterRefresh` — temp file → `Refresh()` → `IsStale == true`.
  - `Run_StaleToolWindowSentinel_DeletedAfterRefresh` — delete → `Refresh()` → `IsStale == false`.
  - `Run_StaleToolWindowSentinel_CachedWithoutRefresh` — delete WITHOUT `Refresh()` → `IsStale` stays `true` (proves caching).
  - `Run_StaleToolWindowSentinel_RefreshReportsChange` — create → `Refresh()` returns true; `Refresh()` again returns false; delete → `Refresh()` returns true (transition signal for the diagnostic).
  - `Run_StaleToolWindowSentinel_NullPathNeverStale` — `new StaleToolWindowSentinel(null)` → `Refresh()` → `IsStale == false` (production no-op when `NEOVISUAL_LOG_DIR` unset).
  RED: class doesn't exist → compile error. Run: `dotnet run --project tests/NeoVisual.Tests -- StaleToolWindowSentinel`.
- **Fails-if:** `Run_StaleToolWindowSentinel_*` fail to compile (class missing) or assert wrong (e.g. `IsStale` not cached, `Refresh()` not idempotent, null path returns true).

## BP-3 — M3: cache the sentinel on WindowManager + per-key-down refresh + positive diagnostic

- **Files:** `MyExtension/WindowManager.cs` (modify); `MyExtension/InputHandler.cs` (modify).
- **Change:**
  - `WindowManager`: replace the static `IsTestStaleInjected()` `File.Exists` with the cached instance:
    - Add field `private readonly StaleToolWindowSentinel _sentinel = new(TestStaleSentinelPath);`
    - `private bool IsTestStaleInjected() => _sentinel.IsStale;` (cached read — no `File.Exists`).
    - Add `public void RefreshStaleSentinel()`:
      ```csharp
      if (_sentinel.Refresh() && _sentinel.IsStale)
          Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}stale-toolwindow sentinel active");
      ```
      (transition-based: exactly one line per activation; re-arms on removal because `Refresh()` returns true again on the true→false transition).
    - `IsToolWindow` (:47) and `Type` (:49) keep their `IsTestStaleInjected()` calls — now cached reads.
  - `InputHandler.IsKeyOfInterest` (:364): call `_windowManager.RefreshStaleSentinel();` as the first statement (the hook's per-key entry — Trailmark-verified `IsKeyOfInterest` is called for every key-down before `HandleKey`). Do NOT also refresh in `HandleKey` (it is always preceded by `IsKeyOfInterest`; refreshing twice would double the syscall).
- **Verify-with:** `dotnet build`; `Run_StaleToolWindowSentinel_*` (BP-2) still GREEN (the wiring consumes the same cache semantics). Diagnostic contract ADDED: `[NeoVisual] stale-toolwindow sentinel active` (exact literal, `DiagnosticLog.NeoVisual` prefix) — emitted once when the sentinel file exists and the first key-down refreshes.
- **Fails-if:** `File.Exists` still runs per access (cache not wired — e.g. `IsTestStaleInjected` still calls `File.Exists`); the sentinel toggle is not picked up on the next key-down (refresh not called from `IsKeyOfInterest`); `[NeoVisual] stale-toolwindow sentinel active` never appears while the sentinel file exists, or spams per key-down (transition not used).

## BP-4 — M4: cache `_isTextInputType` on WindowManager + replace the 5 InputHandler sites

- **Files:** `MyExtension/WindowManager.cs` (modify); `MyExtension/InputHandler.cs` (modify).
- **Change:**
  - `WindowManager`:
    - Add field `private bool _isTextInputType;`
    - Ctor: after `RefreshCurrentWindow()`, `_isTextInputType = GeneralToolWindowController.IsTextInputType(_type);` (initial `_type` is `Unknown` → false).
    - `OnWindowFocusChanged`: set `_isTextInputType = GeneralToolWindowController.IsTextInputType(_type);` after `_type` is assigned in BOTH branches (tool branch after the guid resolution; else branch after `_type = ToolWindowType.Unknown`).
    - Add sentinel-aware property:
      ```csharp
      public bool IsTextInputType => IsTestStaleInjected() ? false : _isTextInputType;
      ```
      (SolutionExplorer is never text-input; the forced stale frame must not be treated as text-input — this is what keeps the FocusGuard veto correct under the fault.)
  - `InputHandler`: replace all 5 `GeneralToolWindowController.IsTextInputType(_windowManager.Type)` sites with `_windowManager.IsTextInputType`:
    - :76 (`HasToolWindowActionKeys` getter)
    - :93 (`EditorFocusedVeto`)
    - :283 (`HandleKey` `ShouldRouteToolWindowKey` call)
    - :396 (`IsKeyOfInterest` `ShouldRouteToolWindowKey` call)
    - :423 (`ExitToolWindowInputMode` `ShouldRouteToolWindowKey` call)
    Leave `MyExtensionPackage.cs:98` (registration-time, not per-key) and `GeneralToolWindowController` itself untouched.
- **Verify-with:** `dotnet build`; existing `Run_ToolWindowMode_TextInputTypesStartInInsert` + `Run_ToolWindowMode_NavigationTypesStartInNormal` (tests/NeoVisual.Tests) still GREEN — they pin the `IsTextInputType` classification semantics the cache stores; `Run_StaleToolWindowSentinel_*` still GREEN (the sentinel-aware property depends on the BP-3 cache). No new diagnostic.
- **Fails-if:** `_isTextInputType` not updated on focus change (stale classification — e.g. a Command Window focused after a Solution Explorer shows as non-text-input); `IsTextInputType` returns `true` while the stale sentinel is active (SolutionExplorer forced) — the FocusGuard veto would be wrong; any of the 5 sites still calls `GeneralToolWindowController.IsTextInputType(_windowManager.Type)` directly (per-key classification not eliminated).

## BP-5 — m1: pure `KeyNameBuilder` (StringBuilder) + RED tests + InputHandler wiring

- **Files:** `MyExtension/KeyNameBuilder.cs` (create); `tests/NeoVisual.Tests/Program.cs` (add tests); `MyExtension/InputHandler.cs` (modify).
- **Change:**
  - New dependency-free `internal static class KeyNameBuilder` in `namespace MyExtension`:
    ```csharp
    public static string Build(Keys key, bool ctrl, bool shift, bool alt)
    {
        var sb = new System.Text.StringBuilder();
        if (ctrl) sb.Append("Ctrl+");
        if (shift) sb.Append("Shift+");
        if (alt) sb.Append("Alt+");
        sb.Append(KeyToString(key));
        return sb.ToString();
    }
    ```
    with the printable-key mapping moved from `InputHandler.KeyToString` (`OemQuestion→"/"`, `Oemplus→"+"`, `OemMinus→"-"`, else `key.ToString()`). Single allocation (StringBuilder). The exact `"Ctrl+H"` format is a live config-file contract — preserve byte-identical.
  - `InputHandler`: delete `BuildSimpleKey` (:451-461) and `KeyToString` (:469-478); change :346 `string simple = BuildSimpleKey(key, ctrl, shift, alt);` → `string simple = KeyNameBuilder.Build(key, ctrl, shift, alt);`.
- **Verify-with:** add to `tests/NeoVisual.Tests/Program.cs`:
  - `Run_KeyNameBuilder_CtrlH` — `Build(Keys.H, true, false, false) == "Ctrl+H"`.
  - `Run_KeyNameBuilder_ShiftF4` — `Build(Keys.F4, false, true, false) == "Shift+F4"`.
  - `Run_KeyNameBuilder_AltX` — `Build(Keys.X, false, false, true) == "Alt+X"`.
  - `Run_KeyNameBuilder_CtrlShiftAltDelete` — `Build(Keys.Delete, true, true, true) == "Ctrl+Shift+Alt+Delete"`.
  - `Run_KeyNameBuilder_NoModifiers` — `Build(Keys.A, false, false, false) == "A"`.
  - `Run_KeyNameBuilder_PrintableKeys` — `Build(Keys.OemQuestion, false, false, false) == "/"` (pins the printable mapping moved into KeyNameBuilder).
  RED: class doesn't exist → compile error. Run: `dotnet run --project tests/NeoVisual.Tests -- KeyNameBuilder`.
- **Fails-if:** `Run_KeyNameBuilder_*` fail to compile (class missing) or assert wrong (modifier order, missing `+`, printable mapping lost); `Build` output differs from the `"Ctrl+H"` config-file contract; `InputHandler` still references `BuildSimpleKey`/`KeyToString` (compile error) or still allocates `List<string>`.

---

## Verification Trace

| failing test / gate | implicated steps | expected diagnostic |
|---|---|---|
| `Run_StaleToolWindowSentinel_FileExistsAfterRefresh` | BP-2 | none (pure class) |
| `Run_StaleToolWindowSentinel_DeletedAfterRefresh` | BP-2 | none |
| `Run_StaleToolWindowSentinel_CachedWithoutRefresh` | BP-2 | none |
| `Run_StaleToolWindowSentinel_RefreshReportsChange` | BP-2 | none |
| `Run_StaleToolWindowSentinel_NullPathNeverStale` | BP-2 | none |
| `Run_KeyNameBuilder_CtrlH` | BP-5 | none |
| `Run_KeyNameBuilder_ShiftF4` | BP-5 | none |
| `Run_KeyNameBuilder_AltX` | BP-5 | none |
| `Run_KeyNameBuilder_CtrlShiftAltDelete` | BP-5 | none |
| `Run_KeyNameBuilder_NoModifiers` | BP-5 | none |
| `Run_KeyNameBuilder_PrintableKeys` | BP-5 | none |
| M1 deletion (build gate) | BP-1 | DELETE `[Hook] key=...`; KEEP `[Hook] installed` |
| M3 wiring (build gate) | BP-3 | ADD `[NeoVisual] stale-toolwindow sentinel active` |
| M4 cache (build gate) | BP-4 | none new |
| `Run_LogPrefixes_Pinned` (existing, must stay GREEN) | BP-1 | `[Hook] ` prefix constant unchanged |

**Known-RED allowlist:** none — no known-RED e2e scenario remains (per docs/progress.md). The deferred e2e scenarios E2E-M1/M3/M4 (e2e-queue.md) are NOT part of this phase's gate; they are QUEUED until the plan is GREEN and a VS-capable machine is available.

## Cross-phase hazards (for the hub / later phases)

- **M3 ↔ stale-toolwindow e2e:** the sentinel toggles with NO focus change (harness creates/removes it between scenarios), so the refresh MUST be per-key-down (entry of `IsKeyOfInterest`), NOT in `OnWindowFocusChanged` — a focus-gated refresh would leave the cache stale and the fault would never inject. The positive `[NeoVisual] stale-toolwindow sentinel active` line is what the deferred E2E-M3 asserts.
- **M4 ↔ M3:** `WindowManager.IsTextInputType` must be sentinel-aware (`IsTestStaleInjected() ? false : _isTextInputType`) — a forced SolutionExplorer frame must never classify as text-input, or the FocusGuard veto breaks the deferred `neovisual-explorer-move-editor-focus` scenario. BP-4 depends on BP-3's cached sentinel.
- **m1 ↔ M21 (Phase 8):** m1 moves `InputHandler.KeyToString` into `KeyNameBuilder`; M21's citation `InputHandler.cs:469-478` will be stale. M21 must extract `KeyNames.ToString(Keys)` from `KeyNameBuilder` (or have it delegate) and update `LeaderSequenceMatcher` — do not re-add a third copy.
- **M4 ↔ M38 (Phase 12):** WindowManager stays in the global namespace for Phase 1 (M38 wraps it in `namespace MyExtension` later); `StaleToolWindowSentinel`/`KeyNameBuilder` are in `namespace MyExtension` and reachable via the existing `using MyExtension;`.


### Phase 2 - Finder amortization (M5, M6, M7, M8)

# Phase 2 Build Plan — Finder path amortization (M5, M6, M7, M8)

> **Lane: unit-only (e2e deferred).** Source: `plans/plan.md` Phase 2 +
> `docs/code-review.md` M5–M8. Every behavior change is RED-proven by a unit test
> (or, for the WPF/VS-coupled wiring, by the diagnostic-format contract + the
> existing unit suites). e2e scenarios are DEFERRED to `e2e-queue.md` — they are
> NOT referenced in any Verify-with/Fails-if. Verify-with = unit test names +
> diagnostic formats ONLY.
>
> **Diagnostics ADDED by this phase (byte-identical formats):**
> - `[Telescope] fzf filter failed: {ex.Message}` (M6, catch path)
> - `[Telescope] fzf filter failed: timeout after {FilterTimeoutMs}ms` (M6, timeout path)
> - `[Telescope] fzf unavailable — showing unfiltered list` (M6, once at overlay open; em-dash `—`)
> - `[Telescope] filter failed: {ex.Message}` (M8)
>
> **Diagnostics UNCHANGED (must stay byte-identical):** `grep hits=...`,
> `open finder=...`, `preview caret=... line=...`, `results count=...`.

## Build Plan

### BP-1 (M5a) — shared `ProjectFileCache` injected into `GrepFinder` + `CodeIssuesFinder`

- **Files:**
  - `Telescope/GrepFinder.cs` (modify)
  - `Telescope/CodeIssuesFinder.cs` (modify)
  - `MyExtension/MyExtensionPackage.cs` (modify)
  - `tests/Telescope.Tests/Program.cs` (add test)
- **Change:**
  - `GrepFinder`: add fields `private readonly ProjectFileCache _fileCache;` and
    `private string? _cachedSolutionName;`. Change the public ctor to
    `public GrepFinder(Func<DTE> dteFactory, ProjectFileCache fileCache)` (throw
    `ArgumentNullException` on a null cache). Add a test-only ctor
    `internal GrepFinder(ProjectFileCache cache, Func<IReadOnlyList<string>> enumerate, Action<GrepHit> opener)`
    that sets `_fileCache = cache`, `_testEnumerate = enumerate`,
    `_testOpener = opener`, `_dteFactory = () => null!`. In `GetCandidates`:
    - add a `_testEnumerate != null` branch that routes through
      `_fileCache.Get(_testEnumerate)` (with the `HitCap` break), logs
      `grep hits={hits.Count}`, returns `hits.Select(ToEntry).ToList()` (hermetic, no UI thread);
    - in the DTE branch, replace `ProjectFiles.Enumerate(dte)` with the
      solution-`FullName` invalidation + `_fileCache.Get(() => ProjectFiles.Enumerate(dte))`,
      mirroring `CodeIssuesFinder.GatherHits` exactly.
  - `CodeIssuesFinder`: change the public ctor to
    `public CodeIssuesFinder(Func<DTE> dteFactory, ProjectFileCache fileCache)`;
    remove the `= new ProjectFileCache()` field initializer and assign
    `_fileCache = fileCache` in the ctor. Keep the internal test-only ctor unchanged.
  - `MyExtensionPackage.InitializeAsync`: before the finder registrations create
    `var fileCache = new ProjectFileCache();` (ONE shared instance) and pass it to
    both `new CodeIssuesFinder(() => VsServices.Dte(this), fileCache)` and
    `new GrepFinder(() => VsServices.Dte(this), fileCache)`.
    (`ProjectFileCache` is `internal` in `Telescope`; `InternalsVisibleTo` already
    includes `MyExtension` — no csproj change.)
- **Verify-with:**
  - New unit test `Run_GrepFinder_CacheEnumeratesOnce` (RED: the
    `GrepFinder(ProjectFileCache, Func<IReadOnlyList<string>>, Action<GrepHit>)`
    ctor does not exist → compile error): temp dir with one file `A.cs`; `int count = 0;`
    `var cache = new ProjectFileCache();`
    `var finder = new GrepFinder(cache, () => { count++; return new[] { a }; }, _ => { });`
    then `finder.GetCandidates("NEEDLE"); finder.GetCandidates("NEEDLE2");` → assert `count == 1`
    (the enumerate delegate ran ONCE across two queries).
  - Existing `Run_GrepFinder_*` + `Run_GetCandidates_*` (8) and `Run_Issues_*` (3)
    still pass (behavior-preserving).
  - Diagnostic unchanged: `grep hits=...` still emitted per `GetCandidates`.
- **Fails-if:**
  - `Run_GrepFinder_CacheEnumeratesOnce` fails with `count == 2` → `GetCandidates`
    still calls the enumerate delegate per query (cache not routed).
  - Compile error persists → the cache ctor param was not added.
  - Any `Run_GrepFinder_*`/`Run_Issues_*` regresses → the cache routing changed scan behavior.

### BP-2 (M5b, OPTIONAL per plan.md "optionally a per-file content cache") — `FileContentCache` keyed by `LastWriteTimeUtc`

- **Files:**
  - `Telescope/FileContentCache.cs` (new)
  - `Telescope/GrepFinder.cs` (modify)
  - `tests/Telescope.Tests/Program.cs` (add tests)
- **Change:**
  - New `internal sealed class FileContentCache` in `Telescope`:
    - ctor `FileContentCache(Func<string, DateTime>? timestamp = null, Func<string, string[]>? reader = null)`
      defaulting to `File.GetLastWriteTimeUtc` / `File.ReadAllLines` (injected delegates keep the
      tests hermetic — no filesystem-timestamp flakiness);
    - `public string[] GetLines(string path)` — returns the cached lines when the file's
      `LastWriteTimeUtc` is unchanged, else re-reads and caches;
    - `public void Clear()`.
  - `GrepFinder`: add `private readonly FileContentCache _contentCache = new FileContentCache();`;
    change `ScanFile` from `private static` to an instance method and replace
    `File.ReadAllLines(path)` with `_contentCache.GetLines(path)`.
- **Verify-with:**
  - `Run_FileContentCache_CachedRead` (RED: class missing → compile error) — injected counting
    reader; `GetLines("a")` twice → reader invoked exactly once.
  - `Run_FileContentCache_InvalidatesOnTimestampChange` — injected timestamp dictionary; change
    the timestamp → `GetLines` re-reads (reader invoked twice).
  - Existing `Run_GrepFinder_*` still pass (returned content identical).
- **Fails-if:**
  - Second `GetLines` re-reads from disk (reader count > 1) → cache not hit.
  - A timestamp change is not detected → stale lines returned.

### BP-3 (M6a) — `FzfFilter` timeout + kill + fallback + failure log

- **Files:**
  - `Telescope/FzfFilter.cs` (modify)
  - `tests/Telescope.Tests/Program.cs` (add tests)
- **Change:**
  - Add `private const int DefaultFilterTimeoutMs = 3000;` and
    `internal int FilterTimeoutMs { get; set; } = DefaultFilterTimeoutMs;` (settable so the
    timeout test can shrink it).
  - In `FilterAsync`, after `var errorTask = p.StandardError.ReadToEndAsync();`, replace
    `await Task.WhenAll(outputTask, errorTask);` with:
    ```csharp
    var all = Task.WhenAll(outputTask, errorTask);
    var timeout = Task.Delay(FilterTimeoutMs, cancellationToken);
    var winner = await Task.WhenAny(all, timeout);
    if (cancellationToken.IsCancellationRequested) return lines; // silent — overlay discards
    if (winner == timeout)
    {
        TryKill(p);
        TelescopeLog.Log($"fzf filter failed: timeout after {FilterTimeoutMs}ms");
        return lines;
    }
    await all;
    ```
    (The `cancellationToken.IsCancellationRequested` guard is REQUIRED: `Task.Delay(ms, token)`
    faults with `OperationCanceledException` on cancellation, so `Task.WhenAny` would otherwise
    misreport a cancellation as a timeout.)
  - In the existing `catch (Exception ex)` (the silent fallback), add
    `TelescopeLog.Log($"fzf filter failed: {ex.Message}");` before `return lines;`. Keep
    `catch (OperationCanceledException) { throw; }` first (the overlay's M8 catch handles it).
- **Verify-with:**
  - `Run_FzfFilter_NonexistentPathFallsBackAndLogs` — `new FzfFilter(Path.Combine(dir, "missing-fzf.exe"))`;
    `FilterAsync(new[]{"alpha"}, "alp", CancellationToken.None)` returns the full list AND the log
    file (temp `LogFileWriter.LogPath` + `Flush()` + `ReadAllTextShared`) contains
    `[Telescope] fzf filter failed:`.
  - `Run_FzfFilter_TimeoutKillsAndFallsBack` — write a `.cmd` hang stub
    (`@ping -n 30 127.0.0.1 > nul`; verified to start with `UseShellExecute=false`);
    `new FzfFilter(cmdPath) { FilterTimeoutMs = 200 }`;
    `FilterAsync(...)` returns the full list within a bounded wall time (< 5s) AND the log
    contains `[Telescope] fzf filter failed: timeout`.
  - Existing `Run_FzfFilter_FilterMatchesPrefix` still passes when fzf is present (unchanged;
    M20 in Phase 11 hardens it — see Known-RED allowlist note).
- **Fails-if:**
  - `FilterAsync` hangs forever on a hung fzf → the timeout path is not wired.
  - No `[Telescope] fzf filter failed:` line on a missing/crashed fzf → the catch log is missing.
  - Cancellation now logs `timeout` → the `cancellationToken.IsCancellationRequested` guard is missing.

### BP-4 (M6b) — `QuoteArg` trailing-backslash fix

- **Files:**
  - `Telescope/FzfFilter.cs` (modify)
  - `tests/Telescope.Tests/Program.cs` (add test)
- **Change:**
  - Change `private static string QuoteArg(string value)` to `internal static string QuoteArg(string value)`.
  - Fix the body (double trailing backslashes before the closing quote so it is not escaped):
    ```csharp
    string escaped = value.Replace("\"", "\\\"");
    int trailing = escaped.Length - escaped.TrimEnd('\\').Length;
    return "\"" + escaped + new string('\\', trailing) + "\"";
    ```
- **Verify-with:**
  - `Run_FzfFilter_QuoteArg_TrailingBackslash` (RED: `QuoteArg` is private today → reflection-free
    call fails to compile) — `QuoteArg("foo\\")` == `"foo\\\\"` (foo + 2 backslashes + closing
    quote); `QuoteArg("foo")` == `"foo"`; `QuoteArg("a\"b")` == `"a\\\"b"`; `QuoteArg("")` == `""`.
- **Fails-if:**
  - `QuoteArg("foo\\")` returns `"foo\"` (single trailing backslash escaping the closing quote) →
    the bug is not fixed.

### BP-5 (M6c) — `IsAvailable()` wired once at overlay open

- **Files:**
  - `Telescope/TelescopeOverlay.cs` (modify)
  - `tests/Telescope.Tests/Program.cs` (add test)
- **Change:**
  - In `ShowOverlay`, immediately after the `open finder=...` log (currently line 266), add:
    ```csharp
    if (!_fzf.IsAvailable())
    {
        TelescopeLog.Log("fzf unavailable — showing unfiltered list");
    }
    ```
    (em-dash `—` preserved byte-identical; one call per overlay open, not per keystroke).
- **Verify-with:**
  - New unit test `Run_FzfFilter_IsAvailableFalseForMissingPath` —
    `new FzfFilter(Path.Combine(dir, "missing-fzf.exe")).IsAvailable() == false`.
  - Diagnostic format contract: `[Telescope] fzf unavailable — showing unfiltered list`
    (exact em-dash literal) emitted once per overlay open when fzf is missing.
- **Fails-if:**
  - `Run_FzfFilter_IsAvailableFalseForMissingPath` fails → missing-path detection is broken.
  - The `fzf unavailable — showing unfiltered list` line never appears when fzf is missing →
    the wiring is missing or the em-dash literal drifted.

### BP-6 (M7a) — pure `LineIndex` helper (shared with M11 in Phase 3)

- **Files:**
  - `Telescope/LineIndex.cs` (new)
  - `tests/Telescope.Tests/Program.cs` (add tests)
- **Change:**
  - New `internal sealed class LineIndex` in `Telescope`:
    - `public LineIndex(string text)` — computes line-start offsets (line 1 starts at 0; each
      `\n` at index `i` → the next line starts at `i + 1`);
    - `public int LineOf(int index)` — 1-based line number via binary search over the line starts
      (largest start ≤ index), index clamped to `[0, text.Length]`; MUST equal
      `TextMotionNavigator.LineNumber` semantics (count of `\n` in `text[0..index)` + 1);
    - `public int LineStart(int line)` — character offset of the start of the given 1-based line
      (clamped);
    - `public int LineCount` — number of lines.
  - Expose `LineOf`/`LineStart`/`LineCount` publicly (internal class) so M11 (Phase 3) reuses the
    same pure index→(line, offset) mapping for the blank-line fallback — do NOT bury it in
    `PreviewRenderer`.
- **Verify-with:**
  - `Run_LineIndex_LineOfMatchesNavigator` (RED: class missing → compile error) — for multi-line
    inputs (incl. leading/trailing `\n`, empty lines, `\r\n`), sweep every index `i` in
    `0..text.Length` and assert `new LineIndex(text).LineOf(i)` equals
    `new TextMotionNavigator { SetText(text); MoveTo(i); }.LineNumber`.
  - `Run_LineIndex_LineStartOffsets` — `LineStart(1) == 0`, `LineStart(2)` == index after the
    first `\n`, etc.
  - `Run_LineIndex_EdgeCases` — empty text (`LineOf(0) == 1`), text ending in `\n`, `\r\n` endings.
- **Fails-if:**
  - `LineOf(i)` disagrees with `TextMotionNavigator.LineNumber` for any swept index → the index
    semantics are wrong (this is the M7/M11 shared contract).

### BP-7 (M7b) — `PreviewRenderer` binary-search integration

- **Files:**
  - `Telescope/PreviewRenderer.cs` (modify)
- **Change:**
  - Add static fields `private static LineIndex? _lineIndex;` and
    `private static TextPointer[]? _linePointers;`.
  - In `SetContent`, after building the FlowDocument, build `_lineIndex = new LineIndex(content)`
    and `_linePointers` = one `TextPointer` per line start (walk the paragraphs once; paragraph
    `k`'s `ContentStart` is line `k+1`'s start).
  - Rewrite `CaretToPointer` to: `int line = _lineIndex.LineOf(index);` →
    `TextPointer lineStart = _linePointers[line - 1];` → walk ONLY that paragraph's runs from
    `lineStart`, accumulating run lengths, to place the caret at
    `index - _lineIndex.LineStart(line)` (clamped to the paragraph length; an index at a `\n`
    lands at the end of the line, matching the current behavior). This turns the O(n) whole-doc
    re-walk into O(runs-in-one-line).
- **Verify-with:**
  - Existing `Run_Preview_*` (8) still pass — caret positions pinned.
  - `Run_LineIndex_*` (BP-6) still pass.
  - Diagnostic unchanged: `preview caret={navigator.Caret} line={navigator.LineNumber}`.
- **Fails-if:**
  - Any `Run_Preview_*` caret-position assertion breaks → the binary-search mapping desynced from
    the paragraph-per-line layout.
  - `CaretToPointer` returns `doc.ContentEnd` for a valid index → the line/paragraph lookup missed.

### BP-8 (M8) — `FilterAndUpdateAsync` try/catch + `FilterFailureLog`

- **Files:**
  - `Telescope/FilterFailureLog.cs` (new)
  - `Telescope/TelescopeOverlay.cs` (modify)
  - `tests/Telescope.Tests/Program.cs` (add test)
- **Change:**
  - New `internal static class FilterFailureLog` in `Telescope`:
    `public static string Format(Exception ex)` → `DiagnosticLog.Telescope + "filter failed: " + ex.Message`
    (the exact `[Telescope] filter failed: <msg>` line).
  - In `TelescopeOverlay.FilterAndUpdateAsync`, wrap the WHOLE body (the
    `await _fzf.FilterAsync(...)` AND the `await Dispatcher.BeginInvoke(...)` action) in try/catch:
    - `catch (OperationCanceledException) { return; }` (silent — cancellation is normal);
    - `catch (Exception ex) { NeoVisualLog.Log(FilterFailureLog.Format(ex)); }`
      (the `await` on `Dispatcher.BeginInvoke` rethrows action faults, so
      `RenderResults`/`PreviewRenderer.Show` faults inside the action are caught too).
  - Use `NeoVisualLog.Log(FilterFailureLog.Format(ex))` — NOT `TelescopeLog.Log(...)` (that would
    double-prefix, since `Format` already includes `[Telescope] `).
- **Verify-with:**
  - `Run_FilterFailureLog_Format` (RED: no `filter failed` string exists today) —
    `FilterFailureLog.Format(new Exception("boom")) == "[Telescope] filter failed: boom"`.
  - Diagnostic format contract: `[Telescope] filter failed: {ex.Message}` emitted when the filter
    path faults; no unobserved task exception escapes `_ = FilterAndUpdateAsync(...)`.
- **Fails-if:**
  - `Run_FilterFailureLog_Format` fails → the exact `[Telescope] filter failed: ` line format drifted.
  - `FilterAndUpdateAsync` still lets an exception escape unobserved (no `filter failed:` line on a
    faulting `FilterAsync`) → the try/catch is missing or the OCE catch swallows non-OCE faults.

## Verification Trace

| failing test | implicated steps | expected diagnostic |
|---|---|---|
| `Run_GrepFinder_CacheEnumeratesOnce` (RED: no cache ctor) | BP-1 | `grep hits=...` unchanged; enumerate delegate runs once across two `GetCandidates` |
| `Run_FileContentCache_CachedRead` / `Run_FileContentCache_InvalidatesOnTimestampChange` (RED: class missing) | BP-2 | n/a (pure class) |
| `Run_FzfFilter_NonexistentPathFallsBackAndLogs` | BP-3 | `[Telescope] fzf filter failed: ...` |
| `Run_FzfFilter_TimeoutKillsAndFallsBack` | BP-3 | `[Telescope] fzf filter failed: timeout after ...ms` |
| `Run_FzfFilter_QuoteArg_TrailingBackslash` | BP-4 | n/a (pure helper) |
| `Run_FzfFilter_IsAvailableFalseForMissingPath` | BP-5 | `[Telescope] fzf unavailable — showing unfiltered list` (overlay wiring, build-verified) |
| `Run_LineIndex_LineOfMatchesNavigator` / `Run_LineIndex_LineStartOffsets` / `Run_LineIndex_EdgeCases` (RED: class missing) | BP-6 | n/a (pure class; shared with M11) |
| existing `Run_Preview_*` (8) | BP-7 | `preview caret=... line=...` unchanged |
| `Run_FilterFailureLog_Format` (RED: no `filter failed` string) | BP-8 | `[Telescope] filter failed: ...` |
| full `tests/Telescope.Tests` suite | BP-1..BP-8 | all existing `Run_*` pass; no `[Telescope]`/`[NeoVisual]` literal drift |

## Known-RED allowlist

- **None** (per `plans/plan.md` — no known-RED e2e scenario remains; `docs/progress.md` confirms
  the 35-scenario suite is GREEN).
- **Do NOT flag as a Phase 2 regression:** `Run_FzfFilter_FilterMatchesPrefix` silently passes when
  fzf is absent — that is a Phase 11 (M20) hardening item, not a Phase 2 defect. The new Phase 2
  fzf tests use the injected-path seam, not the real binary.

## Cross-phase hazards

- **M7's `LineIndex` is shared with M11 (Phase 3).** BP-6 must expose `LineOf`/`LineStart`/
  `LineCount` as an internal pure class in `Telescope`; M11's blank-line fallback reuses the same
  index→(line, offset) mapping. Do not make it private to `PreviewRenderer`.
- **M6's injected `fzfPath` seam (`FzfFilter(string? fzfPath)`) is used by M20 (Phase 11).**
  BP-3/BP-4 must keep the ctor signature and the `internal int FilterTimeoutMs` seam; M20 will
  change `Run_FzfFilter_FilterMatchesPrefix` to use the injected path.
- **M5's shared `ProjectFileCache` already exists (F15).** BP-1 only changes wiring; the
  `_cachedSolutionName` invalidation pattern is duplicated per-finder — keep it consistent with
  `CodeIssuesFinder.GatherHits`.
- **M8's `[Telescope] filter failed:` is a NEW diagnostic.** The format must be byte-identical
  (`[Telescope] filter failed: {ex.Message}`); the deferred e2e queue asserts it.


### Phase 3 - Preview/motion (M9, M10, M11)

# Build Plan — Phase 3: Preview/motion correctness (M9, M10, M11)

> Source: `plans/plan.md` Phase 3 (lines 185-209), Acceptance criteria row 3 (line 735),
> Unit test plan (lines 748-756), Diagnostics (lines 769-784). Lane: **unit-only, e2e
> deferred** (plan.md line 3-13). Verify-with = **unit test names + diagnostic formats ONLY**
> — no e2e scenario references in Verify-with/Fails-if. All three fixes are RED-proven by new
> `tests/Telescope.Tests` unit tests; no new diagnostics are added, one contract is preserved.

## Scope

Three code-review findings on the Telescope preview/motion path:

| Finding | File:line (verified) | Bug | Fix |
|---------|----------------------|-----|-----|
| M9 | `Telescope/SyntaxHighlighter.cs:198` | `ReadQuoted` does `i += 2` in the `\\` escape branch; an unterminated string ending in a backslash pushes `i` past `text.Length` → `ArgumentOutOfRangeException` → whole preview fails ("preview load failed") | `i += (i + 1 < text.Length) ? 2 : 1;` |
| M10 | `Telescope/TextMotionNavigator.cs:120` | `Up()` calls `LastIndexOf('\n', lineStart - 2)` with `lineStart - 2 == -1` (leading blank line) → `ArgumentOutOfRangeException` | `LastIndexOf('\n', Math.Max(0, lineStart - 2))` |
| M11 | `Telescope/PreviewRenderer.cs:141` | `CaretToPointer` misplaces/returns null on a blank line → caret placement silently no-ops (the `preview caret=` log is still emitted, so e2e passes) | `if (index == plain) return para.ContentStart;` at the top of each `Paragraph` block |

## Dependencies (cross-phase)

- **M11 depends on M7's `LineIndex` (Phase 2).** M7 (Phase 2) extracts the pure `LineIndex`
  helper (text → line-start offsets; `LineOf(index)` via binary search). M11's "pure
  index→(line, offset) mapping" **IS that same `LineIndex`** — extract once in Phase 2,
  consume here in Phase 3. **Do NOT re-extract a parallel index helper.** BP-3 verifies
  `LineIndex` exists before proceeding; if it is missing, STOP and report the Phase 2
  dependency (do not re-implement it).
- **Diagnostic contract:** `[Telescope] preview caret=... line=...` (spec.md §4, line 183)
  must remain byte-identical. None of M9/M10/M11 touch the emission site
  (`PreviewRenderer.Show`, line 33) — all three fixes are pure-logic.

## Known-RED allowlist

**None.** Per `plans/plan.md` "Known-RED allowlist" (lines 786-789): no known-RED e2e
scenario remains; e2e is deferred to `e2e-queue.md` (status QUEUED). The verification-agent
must NOT flag the deferred e2e scenarios (`telescope-preview`, `telescope-preview-motions`)
as regressions — they are QUEUED, not run in this lane.

## Build Plan steps

### BP-1 — M9: Guard the escape advance in `SyntaxHighlighter.ReadQuoted`

- **Files:** `Telescope/SyntaxHighlighter.cs` (modify); `tests/Telescope.Tests/Program.cs` (add test).
- **Change:** In `ReadQuoted` (lines 196-199), replace the unconditional `i += 2;` (line 198,
  the `\\` escape branch) with `i += (i + 1 < text.Length) ? 2 : 1;`. When the backslash is
  the last character (`i + 1 >= text.Length`), advance by 1 so `i` lands exactly on
  `text.Length` and the `while (i < text.Length)` loop exits cleanly; `text.Substring(start,
  i - start)` (line 208) then stays in range. **Do NOT touch the quote branch** (`i++` at
  line 203) — the plan's corrected citation places the bug in the escape branch only.
- **Verify-with:** Add `Run_Syntax_UnterminatedStringEndingInBackslash` to
  `tests/Telescope.Tests/Program.cs` (SyntaxHighlighter section, after
  `Run_Syntax_RoundTripsText` ~line 896): `var segs = SyntaxHighlighter.Segment("var s = \"abc\\");`
  (C# literal for `var s = "abc\`) must NOT throw, and
  `string.Concat(segs.Select(s => s.Text))` must equal the input (round-trip, mirroring
  `Run_Syntax_RoundTripsText`). Run: `dotnet run --project tests/Telescope.Tests -- Syntax`.
  RED before fix: throws `ArgumentOutOfRangeException`; GREEN after.
- **Fails-if:** The test throws `ArgumentOutOfRangeException` (fix not applied, or applied to
  the wrong branch — e.g. the `i++` quote branch instead of the `\\` escape branch); or the
  round-trip assertion fails (segment text does not reconstruct the input, meaning the escape
  advance consumed the wrong number of characters).

### BP-2 — M10: Clamp `startIndex` in `TextMotionNavigator.Up`

- **Files:** `Telescope/TextMotionNavigator.cs` (modify); `tests/Telescope.Tests/Program.cs` (add test).
- **Change:** In `Up()` (line 120), replace
  `int prevNewline = _text.LastIndexOf('\n', lineStart - 2);` with
  `int prevNewline = _text.LastIndexOf('\n', Math.Max(0, lineStart - 2));`. When the caret is
  on line 2 and line 1 is blank (`lineStart - 2 == -1`), the clamp makes the search start at
  index 0, finds the leading `\n`, `prevStart = 1`, and the caret stays on line 2 (column 0).
  **Do NOT touch the `lineStart == 0` early return** (lines 113-117) — line 0 is already
  guarded.
- **Verify-with:** Add `Run_Preview_UpFromSecondLineWithLeadingBlankLine` to
  `tests/Telescope.Tests/Program.cs` (TextMotionNavigator/Preview section, after
  `Run_Preview_InsertMotions` ~line 579): `var n = new TextMotionNavigator(); n.SetText("\nabc");
  n.MoveToLine(2); n.Up();` must NOT throw and `n.LineNumber` must stay `2` (caret stays at
  index 1). Run: `dotnet run --project tests/Telescope.Tests -- Preview`. RED before fix:
  throws `ArgumentOutOfRangeException`; GREEN after.
- **Fails-if:** The test throws `ArgumentOutOfRangeException` (clamp missing); or
  `n.LineNumber != 2` after `Up()` (clamp applied but the caret moved to the wrong line — e.g.
  clamped to 0 instead of searching from 0).

### BP-3 — M11a: Consume the shared `LineIndex` (Phase 2) — pure index→(line, offset) mapping + RED test

- **Files:** `Telescope/LineIndex.cs` (exists from Phase 2 — verify, do NOT re-extract);
  `tests/Telescope.Tests/Program.cs` (add test).
- **Change:** Confirm `Telescope/LineIndex.cs` exists with `LineOf(int index)` (binary search)
  + line-start offsets (Phase 2 M7). Add a pure `(int line, int offset)` mapping on top of it —
  either a `LineStart(int line)` accessor on `LineIndex` (offset = `index - LineStart(line)`)
  or a thin `Telescope/PreviewIndexMapper.cs` static helper `Map(int index) => (line, offset)` —
  consumed by `CaretToPointer` (BP-4) and by the test. **This is the SAME extraction as M7 —
  do not build a second index.** If `LineIndex` is absent, STOP and report the Phase 2
  dependency.
- **Verify-with:** Add `Run_Preview_CaretOnBlankLine` to `tests/Telescope.Tests/Program.cs`
  (TextMotionNavigator/Preview section): for `"abc\n\nxyz"` (line 2 blank), the pure mapping
  must give `LineOf(4) == 2` and `4 - LineStart(2) == 0` (the blank line maps to ITS OWN
  start, offset 0 — not null, not the next line); also assert `LineOf(5) == 3` and
  `5 - LineStart(3) == 0` (line 3 start). Run:
  `dotnet run --project tests/Telescope.Tests -- CaretOnBlankLine`. RED before: helper/accessor
  missing → compile error; GREEN after.
- **Fails-if:** Compile error (the shared `LineIndex`/accessor does not exist — either Phase 2
  did not land, or a parallel helper was built instead of reusing `LineIndex`); or `LineOf(4)`
  returns 3 (blank line skipped → "next line" bug); or `4 - LineStart(2) != 0` (blank line
  does not map to its own start).

### BP-4 — M11b: Blank-line fallback in `PreviewRenderer.CaretToPointer`

- **Files:** `Telescope/PreviewRenderer.cs` (modify).
- **Change:** In `CaretToPointer` (lines 122-154), at the top of the
  `if (block is Paragraph para)` block (line 132-133, BEFORE the
  `foreach (var inline in para.Inlines)` at line 134), add
  `if (index == plain) return para.ContentStart;`. This is the blank-line fallback: when the
  caret index equals the plain-text offset at the start of a paragraph (a blank line has no
  runs, so the inline loop would otherwise fall through and misplace the caret), return the
  paragraph's start. The `plain` counter already accumulates run lengths + 1 per non-last
  paragraph (lines 147-150), so `index == plain` exactly identifies a line start. No other
  branch changes; the `preview caret=... line=...` emission in `Show` (line 33) is untouched.
- **Verify-with:** `Run_Preview_CaretOnBlankLine` (BP-3) proves the pure precondition (blank
  line → own start, offset 0); this step applies it in the WPF walk. Gate: `dotnet build`
  succeeds + `dotnet run --project tests/Telescope.Tests -- Preview` (all existing
  `Run_Preview_*` + `Run_PromptMotion_*` tests unchanged) + `dotnet run --project
  tests/Telescope.Tests` (total 84 → 87). Diagnostic contract: `[Telescope] preview caret=...
  line=...` unchanged (no emission-site edit).
- **Fails-if:** Build error (syntax/scope — e.g. `para` not in scope at the insertion point);
  or an existing `Run_Preview_*`/`Run_PromptMotion_*` test regresses (the `index == plain`
  early-return changed a non-blank-line caret placement — verify the check is inside the
  Paragraph block, not before it); or the total test count is not 87 (a test was dropped or
  duplicated).

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic / assertion |
|---|---|---|
| `Run_Syntax_UnterminatedStringEndingInBackslash` | BP-1 | no exception; `string.Concat(segs.Select(s => s.Text)) == "var s = \"abc\\"` (round-trip) |
| `Run_Preview_UpFromSecondLineWithLeadingBlankLine` | BP-2 | no exception; `n.LineNumber == 2` after `SetText("\nabc"); MoveToLine(2); Up()` |
| `Run_Preview_CaretOnBlankLine` | BP-3, BP-4 | `LineOf(4) == 2` and `4 - LineStart(2) == 0` for `"abc\n\nxyz"` (blank line → own start, not next line) |
| existing `Run_Preview_*` / `Run_PromptMotion_*` / `Run_Syntax_*` (regression guard) | BP-1, BP-2, BP-4 | all pass unchanged; Telescope.Tests total 84 → 87 |
| `[Telescope] preview caret=... line=...` (contract) | BP-3, BP-4 | format byte-identical (no emission-site edit; deferred e2e `telescope-preview` QUEUED) |

**Known-RED allowlist (do NOT report as regressions):** none — no known-RED e2e scenario
remains (plan.md lines 786-789). Deferred e2e scenarios `telescope-preview`,
`telescope-preview-motions` are QUEUED in `e2e-queue.md`, not run in this lane.

## KEY DECISIONS

- **M11's pure index extraction is M7's `LineIndex` (Phase 2) — extract once, consume here.**
  BP-3 verifies `LineIndex` exists and reuses it; do NOT build a parallel index helper. If
  Phase 2 has not landed, stop and report (cross-phase hazard).
- **M9's guard is on the `\\` escape branch only** (`i += 2` at line 198), NOT the quote
  branch (`i++` at line 203) — the plan's corrected citation (the original finding
  misdescribed the bug location).
- **M10's clamp is `Math.Max(0, lineStart - 2)`** — the `lineStart == 0` early return (lines
  113-117) already guards line 0; do not touch it.
- **M11's fallback is `if (index == plain) return para.ContentStart;`** at the top of each
  Paragraph block — NOT a `GetPositionAtOffset` clamp; the `plain` counter's +1-per-non-last-
  paragraph model (lines 147-150) is the index contract and must stay in sync with
  `SetContent`'s paragraph-per-line layout.
- **Diagnostic contract `[Telescope] preview caret=... line=...` is untouched** — all three
  fixes are pure-logic; the emission site (`PreviewRenderer.Show` line 33) is not edited.


### Phase 4 - Navigation robustness (M2, M12)

# Phase 4 — Navigation robustness (M2, M12) — Build Plan

> Source: `plans/plan.md` Phase 4 (lines 211-238) + Acceptance criteria (line 736) +
> Unit test plan (lines 757-759) + Diagnostics (line 780). Lane: **unit-only, e2e
> deferred** — Verify-with = unit test names + diagnostic formats ONLY; no e2e
> scenario is referenced in any Verify-with/Fails-if.
>
> **Diagnostic contract for this phase:** `[NeoVisual] navigate direction=...`
> must stay **byte-identical** (emitted by `InputHandler.Navigate`,
> `InputHandler.cs:483`, which Phase 4 does NOT touch). Phase 4 adds NO new
> diagnostics. The harness asserts the single-char token `navigate direction=L/R/D/U`
> — any drift here is a Phase 4 regression.
>
> **Known-RED allowlist:** NONE (per `plans/plan.md` lines 786-789 — no known-RED
> e2e scenario remains; every Phase 4 fix is RED-proven by unit tests).

## Build Plan steps

### BP-1 — Create pure `NavigationSnapshot` helper (M2 seam)

- **Files:**
  - CREATE `MyExtension/CardinalMovment/NavigationSnapshot.cs`
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (add the `Run_NavigationSnapshot_*`
    tests in the `Tests` class, next to the `Run_WindowNavigationEngine_*` group)
- **Change:** new `sealed class NavigationSnapshot` in namespace
  `CardinalNavigation` (internal, no modifier — reachable via
  `InternalsVisibleTo("NeoVisual.Tests")`):
  - `public IReadOnlyList<RectCoordinate> Candidates { get; }`
  - `public int ActiveIndex { get; }`
  - `public RectCoordinate Active => Candidates[ActiveIndex];` — the active rect
    is **derived from the snapshot's candidate list**, never a separate fetch.
  - `public static NavigationSnapshot? Capture(IReadOnlyList<RectCoordinate> candidates, int activeIndex)`
    — returns `null` when `activeIndex < 0 || activeIndex >= candidates.Count`
    (the caller no-ops), else a snapshot. Private ctor.
  - net472-safe: `IReadOnlyList<T>` (NOT `IReadOnlySet<T>`); no LINQ beyond
    `Count`/indexer.
- **Verify-with:**
  - `Run_NavigationSnapshot_ActiveComesFromSnapshot` — build a
    `RectCoordinate[]` of ≥2 rects, `var snapshot = NavigationSnapshot.Capture(rects, 1);`
    then `Assert.Equal(snapshot.Candidates[1], snapshot.Active);` — proves the
    active rect is the candidate at the active index (single-pass, no N+1).
    **RED:** class doesn't exist → compile error.
  - `Run_NavigationSnapshot_ActiveIndexOutOfRange_ReturnsNull` —
    `Assert.Equal(null, NavigationSnapshot.Capture(rects, -1));` and
    `Assert.Equal(null, NavigationSnapshot.Capture(rects, rects.Length));` —
    pins the `activeIndex < 0` → no-op guard.
- **Fails-if:** compile error (helper missing) — RED; `snapshot.Active !=
  snapshot.Candidates[snapshot.ActiveIndex]`; `Capture` returns a non-null
  snapshot for an out-of-range index (the `NavigateInDirection` no-op guard
  would then index out of range).

### BP-2 — Single-pass rect snapshot in `WindowMatrix.NavigateInDirection` (M2)

- **Files:** MODIFY `MyExtension/CardinalMovment/WindowMatrix.cs`
  (`NavigateInDirection`, lines 102-111).
- **Change:** inside the existing `try`, replace
  ```csharp
  RectCoordinate active = m_activeWindow.Rect;
  List<RectCoordinate> candidates = m_ActiveWindows.Select(w => w.Rect).ToList();
  int? target = WindowNavigationEngine.SelectTarget(active, candidates, dir, _settings);
  ```
  with
  ```csharp
  List<RectCoordinate> rects = m_ActiveWindows.Select(w => w.Rect).ToList();
  NavigationSnapshot? snapshot = NavigationSnapshot.Capture(rects, m_ActiveWindows.IndexOf(m_activeWindow));
  if (snapshot == null) { return; }
  int? target = WindowNavigationEngine.SelectTarget(snapshot.Active, snapshot.Candidates, dir, _settings);
  ```
  This is the single pass: `w.Rect` (→ `RefreshRect` → `GetWindowScreenRect`
  COM) is called **exactly once per window**; the active rect is reused from the
  snapshot instead of a second `m_activeWindow.Rect` fetch. Do NOT change the
  `char direction` signature (that is M40/Phase 12) and do NOT touch
  `ToDirection` (M40 deletes it later).
- **Verify-with:**
  - `Run_NavigationSnapshot_*` (BP-1) pass.
  - All 10 existing `Run_WindowNavigationEngine_*` tests still pass
    (`-- WindowNavigationEngine`) — `SelectTarget` receives the same
    active/candidates values, so the algorithm is unchanged.
  - `[NeoVisual] navigate direction=...` byte-identical (emitted by
    `InputHandler.Navigate`, `InputHandler.cs:483`, untouched).
- **Fails-if:** any `Run_WindowNavigationEngine_*` regression (the snapshot
  changed the values handed to `SelectTarget`); `m_activeWindow.Rect` still
  called separately (N+1 remains — code review); the `snapshot == null` no-op
  path missing when `IndexOf` returns -1 (would throw `ArgumentOutOfRangeException`
  on `Candidates[ActiveIndex]`).

### BP-3 — Drop write-only `_rect` field + fix misleading comment (M2 cleanup / n8)

- **Files:** MODIFY `MyExtension/CardinalMovment/WindowAdapter.cs`
  (field line 20; `Rect` doc lines 29-32; `RefreshRect` lines 120-127).
- **Change:**
  - Delete `private RectCoordinate _rect;` (line 20).
  - `RefreshRect` returns the local directly (drop the `_rect = ...; return _rect;`
    write at lines 125-126).
  - Reword the `Rect` doc comment (lines 29-32) so it no longer claims the old
    N+1 behavior — state that the engine snapshots all rects in a single pass
    per navigation (see `NavigationSnapshot`), so `Rect` is read once per window
    per navigation.
- **Verify-with:** `dotnet build` (no lingering `_rect` reference) + the existing
  NeoVisual.Tests suite (no test references `_rect`; `-- WindowNavigationEngine`
  still green).
- **Fails-if:** build error from a lingering `_rect` reference; the comment still
  describes the pre-fix N+1 re-fetch.

### BP-4 — `RectCoordinate.Empty` + `WindowAdapter` `_frame4` cache + `TryGetScreenRect` (M12)

- **Files:**
  - MODIFY `MyExtension/CardinalMovment/RectCoordinate.cs` (add `Empty`)
  - MODIFY `MyExtension/CardinalMovment/WindowAdapter.cs` (ctor + `RefreshRect` + new static)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (add the `Run_WindowAdapter_TryGetScreenRect_*` tests)
- **Change:**
  - `RectCoordinate`: add `public static readonly RectCoordinate Empty = new RectCoordinate(0, 0, 0, 0);`
    (matches the existing `IsEmpty` predicate `X==0 && Y==0 && Width==0 && Height==0`).
  - `WindowAdapter`: add field `private readonly IVsWindowFrame4? _frame4;` set in
    the ctor as `_frame4 = frame as IVsWindowFrame4;` (safe cast, cached once —
    no per-access cast).
  - Add `internal static RectCoordinate? TryGetScreenRect(IVsWindowFrame4? frame4)`:
    `if (frame4 == null) { return null; }` then
    `frame4.GetWindowScreenRect(out int left, out int top, out int width, out int height);`
    and `return new RectCoordinate(left, top, width, height);`.
  - `RefreshRect` becomes `return TryGetScreenRect(_frame4) ?? RectCoordinate.Empty;`
    — a non-conforming frame yields `RectCoordinate.Empty`, which
    `WindowNavigationEngine` already excludes via the `!c.IsEmpty` pipeline
    predicate (no `InvalidCastException`).
- **Verify-with:**
  - `Run_WindowAdapter_TryGetScreenRect_NullFrameReturnsNull` —
    `Assert.Equal(null, WindowAdapter.TryGetScreenRect(null));` — **RED:** method
    doesn't exist → compile error; post-fix: null → null (the old code path
    `(IVsWindowFrame4)_frame` throws `InvalidCastException`).
  - `Run_WindowAdapter_TryGetScreenRect_EmptyRectExcludedBySelectTarget` —
    `SelectTarget(active, candidates-with-a-(0,0,0,0)-entry, Direction.Up, settings)`
    skips the Empty candidate and returns the real one — mirrors the existing
    `Run_WindowNavigationEngine_HiddenZeroRect_Excluded` (line 815) and proves
    the `RectCoordinate.Empty` fallback is safe end-to-end.
- **Fails-if:** `TryGetScreenRect(null)` throws `InvalidCastException` (old
  behavior); `RefreshRect` returns a non-Empty rect when `_frame4` is null;
  `SelectTarget` does not exclude the Empty candidate (the Empty fallback would
  poison navigation).

### BP-5 — Wrap `AutoHides()` in the per-frame try/catch (M12)

- **Files:** MODIFY `MyExtension/CardinalMovment/WindowMatrix.cs`
  (`NavigateInDirection`, guard lines 97-101).
- **Change:** split the guard — keep the cheap null/empty check outside the try:
  ```csharp
  if (m_activeWindow == null || m_ActiveWindows.Count == 0)
  {
      return;
  }
  try
  {
      if (m_activeWindow.AutoHides())
      {
          return;
      }
      // ... BP-2 snapshot body ...
  }
  catch (Exception ex) { /* existing log */ }
  ```
  so an `AutoHides()` exception (DTE window disposed/odd frame) is caught by the
  existing per-navigation catch and logged — it no longer propagates through
  `InputHandler.Navigate` → `LeaderSequenceMatcher.action()` into the hook path.
  Guard order must stay: null check first, then `Count == 0`, then `AutoHides()`.
- **Verify-with:** `dotnet build` + all 10 existing `Run_WindowNavigationEngine_*`
  tests still pass (the relocation is behavior-preserving — `AutoHides()` still
  short-circuits before `SelectTarget`); `[NeoVisual] navigate direction=...`
  byte-identical (InputHandler untouched).
- **Fails-if:** `AutoHides()` still outside the try (an exception escapes to the
  hook path — code review); the null/empty guard order changes (dereferencing
  `m_activeWindow` before the null check → `NullReferenceException`); a
  `Run_WindowNavigationEngine_*` regression.

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_NavigationSnapshot_ActiveComesFromSnapshot` (RED: compile error) | BP-1 | none (pure unit test); `[NeoVisual] navigate direction=...` unchanged |
| `Run_NavigationSnapshot_ActiveIndexOutOfRange_ReturnsNull` (RED: compile error) | BP-1 | none (pure unit test) |
| `Run_WindowAdapter_TryGetScreenRect_NullFrameReturnsNull` (RED: compile error) | BP-4 | none (pure unit test) |
| `Run_WindowAdapter_TryGetScreenRect_EmptyRectExcludedBySelectTarget` | BP-4 | none (pure unit test) |
| `Run_WindowNavigationEngine_*` (10 existing — must not regress) | BP-2, BP-5 | `[NeoVisual] navigate direction=...` unchanged (byte-identical) |
| `dotnet build` (no lingering `_rect`; `AutoHides()` inside try) | BP-3, BP-5 | none (build gate) |

**Known-RED allowlist (do NOT flag as regressions):** none — no known-RED e2e
scenario remains (per `plans/plan.md` lines 786-789). All Phase 4 fixes are
RED-proven by the unit tests above.

## Cross-phase hazards

- **M2 `_rect` cleanup overlaps n8 (Phase 12).** n8 ("drop the write-only `_rect`
  field", `docs/code-review.md:100`) is fully covered by BP-3. When Phase 12
  executes n8 it must be a no-op — do not re-report it.
- **M12 `TryGetScreenRect` lives on `WindowAdapter`** (`CardinalMovment/`), a
  VS-coupled type; the static helper is the pure seam. The test project resolves
  `IVsWindowFrame4` via `Microsoft.VisualStudio.Interop` (already referenced in
  `tests/NeoVisual.Tests/NeoVisual.Tests.csproj`) and reaches the internal type
  via `InternalsVisibleTo("NeoVisual.Tests")`.
- **M40 (Phase 12) rewrites the same `NavigateInDirection` signature**
  (`char` → `Direction`) and deletes `ToDirection`. Phase 4 (BP-2/BP-5) must NOT
  change the `char direction` signature or `ToDirection`; both phases must keep
  the `[NeoVisual] navigate direction=L/R/D/U` single-char token byte-identical.
- **No doc-ref updates required.** Phase 4 adds `NavigationSnapshot` /
  `TryGetScreenRect` / `RectCoordinate.Empty` and removes only the private
  `_rect` field — no source-of-truth doc (AGENTS.md, SKILL.md, docs/spec.md,
  docs/progress.md, .opencode/agent/*.md) references `_rect` or any renamed/
  removed symbol, so `tools/check-doc-refs.ps1` needs no change.


### Phase 5 - Finder correctness (M13, M14)

# Phase 5 Build Plan — Finder correctness (M13, M14)

> Source: `plans/plan.md` Phase 5 (lines 240-262) + Acceptance criteria row 5
> (line 737) + Unit test plan (lines 751, 760) + Diagnostics (line 780).
> Lane: **unit-only, e2e deferred** — Verify-with/Fails-if reference unit test
> names and diagnostic formats ONLY, never e2e scenarios.
> Known-RED allowlist for this item: **none** (per `plans/plan.md` line 786-789).

## Adjudicated corrections to the plan's citations (folded into the steps below)

1. **M13 property name — `item.Severity` is WRONG.** The plan says
   `ClassifySeverity(item.Severity)`, but the resolved type is
   `EnvDTE80.ErrorItem` (the merged `Microsoft.VisualStudio.Interop.dll` has no
   `EnvDTE.ErrorItem`), whose severity property is **`ErrorLevel`** (type
   `vsBuildErrorLevel`). `item.Severity` would NOT compile. All steps use
   `item.ErrorLevel`.
2. **M13 enum namespace — `vsBuildErrorLevel` is `EnvDTE80.vsBuildErrorLevel`**
   (defined in `Microsoft.VisualStudio.Interop.dll`), NOT
   `Microsoft.VisualStudio.Shell.Interop.vsBuildErrorLevel`. Values verified:
   `vsBuildErrorLevelLow = 1`, `vsBuildErrorLevelMedium = 2`,
   `vsBuildErrorLevelHigh = 4`. `CodeIssuesFinder.cs` already has
   `using EnvDTE80;` (line 2), so the production code resolves it; the test
   project references `Microsoft.VisualStudio.Interop` 17.14.40260, so it
   resolves in the test process. `tests/Telescope.Tests/Program.cs` needs
   `using EnvDTE80;` added (it currently lacks it).
3. **M13 testability — `ClassifySeverity` must be `internal static`** (not
   `private` like the deleted `Classify(string)`), because the tests call it
   directly and `Telescope.csproj` has `InternalsVisibleTo Include="Telescope.Tests"`.
4. **M14 mirrors `HierarchyResolver`** — `HierarchyForestBuilder` must reuse the
   existing `HierarchyResolver.PhysicalFolderKind` / `PhysicalFileKind` GUID
   constants and produce the existing `HierarchyNode` type; do NOT duplicate the
   Kind GUID literals.
5. **Doc-ref propagation** — `docs/progress.md:705` backticks `BuildForest`
   (a scanned doc per `tools/check-doc-refs.ps1`). Deleting `BuildForest`
   without updating that line fails the doc-ref lint. BP-6 handles it.

---

## Build Plan steps

### BP-1 — M13 RED: add `Run_Issues_Severity*` tests (compile-error RED)

- **Files:** `tests/Telescope.Tests/Program.cs` (modify — add a new section after
  the existing `CodeIssuesFinder` section, ~line 968).
- **Change:** add `using EnvDTE80;` to the file header (line 5 area). Add three
  `public static void` tests on the `Tests` class that call the not-yet-existing
  `CodeIssuesFinder.ClassifySeverity(EnvDTE80.vsBuildErrorLevel)`:
  - `Run_Issues_SeverityMediumMapsToWarning` — assert
    `CodeIssuesFinder.ClassifySeverity(vsBuildErrorLevelMedium) == CodeIssueKind.Warning`.
    Comment documents the semantic: a warning whose message contains "error" must
    still be Warning (classification is severity-based, not description-based).
  - `Run_Issues_SeverityHighMapsToError` — assert
    `ClassifySeverity(vsBuildErrorLevelHigh) == CodeIssueKind.Error`.
  - `Run_Issues_SeverityLowMapsToInfo` — assert
    `ClassifySeverity(vsBuildErrorLevelLow) == CodeIssueKind.Info`.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Issues` — RED:
  the project fails to compile because `ClassifySeverity` does not exist
  (compile error is the RED signal; the three tests cannot run yet).
- **Fails-if:** the tests compile and pass with NO production change (means the
  method already existed or the test references the wrong symbol); the tests
  reference `Microsoft.VisualStudio.Shell.Interop.vsBuildErrorLevel` (wrong
  namespace — must be `EnvDTE80.vsBuildErrorLevel`); the tests reference
  `item.Severity` anywhere (wrong property — must be `ErrorLevel`).

### BP-2 — M13 GREEN: implement `ClassifySeverity`, delete `Classify(string)`, rewire the call site

- **Files:** `Telescope/CodeIssuesFinder.cs` (modify).
- **Change:**
  - Replace the call site at line 172:
    `issues.Add(new CodeIssue(Classify(item.Description), fileName, item.Line, item.Description ?? string.Empty));`
    → `issues.Add(new CodeIssue(ClassifySeverity(item.ErrorLevel), fileName, item.Line, item.Description ?? string.Empty));`
    (`item` is `EnvDTE80.ErrorItem`; `ErrorLevel` is its `vsBuildErrorLevel` property).
  - Delete the private `Classify(string description)` method (lines 186-198) —
    its only caller was line 172.
  - Add `internal static CodeIssueKind ClassifySeverity(vsBuildErrorLevel severity)`
    (internal, not private, for the test seam):
    `vsBuildErrorLevelHigh → CodeIssueKind.Error`,
    `vsBuildErrorLevelMedium → CodeIssueKind.Warning`,
    `vsBuildErrorLevelLow → CodeIssueKind.Info`,
    default → `CodeIssueKind.Info`. `vsBuildErrorLevel` resolves via the existing
    `using EnvDTE80;` — no new using needed.
  - No change to `ToEntry` (the `ERR`/`WARN`/`TODO`/`INFO` marker mapping) or
    `OpenHit` (the `opened issue:` log) — the diagnostic contract is preserved.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Issues` — all
  three `Run_Issues_Severity*` tests PASS; `dotnet build` clean.
- **Fails-if:** `Run_Issues_SeverityMediumMapsToWarning` fails (Medium maps to
  Error/Info instead of Warning); `Run_Issues_SeverityHighMapsToError` fails;
  `Run_Issues_SeverityLowMapsToInfo` fails; compile error at the call site
  (e.g. `item.Severity` used instead of `item.ErrorLevel`); the old
  `Classify(string)` still present (dead code — the plan requires deletion).

### BP-3 — M14 RED: add `Run_HierarchyForestBuilder_*` tests (compile-error RED)

- **Files:** `tests/NeoVisual.Tests/Program.cs` (modify — add a new section after
  the existing `HierarchyResolver` section, ~line 250).
- **Change:** add five `public static void` tests on the `Tests` class that
  reference the not-yet-existing `MyExtension.HierarchyForestBuilder.Build` and
  `MyExtension.HierarchyItemInfo` (both `internal`, visible via
  `InternalsVisibleTo` in `MyExtension.csproj`):
  - `Run_HierarchyForestBuilder_NestedFoldersProduceNestedChildren` — a
    folder→folder→file tree yields a `HierarchyNode` whose `Children` contain a
    nested folder node whose `Children` contain the file node (assert
    `Children[0].Children[0].Kind == HierarchyResolver.PhysicalFileKind`).
  - `Run_HierarchyForestBuilder_CsFilterCaseInsensitive` — a file named
    `Program.CS` (uppercase extension) IS included (OrdinalIgnoreCase); a file
    named `App.config` is NOT.
  - `Run_HierarchyForestBuilder_FullPathFlowsThroughAndPathMap` — the file's
    `FullPath` lands in `HierarchyNode.FilePath` AND in the passed
    `Dictionary<string,string> pathToItem` (assert `pathToItem[fullPath] == fullPath`).
  - `Run_HierarchyForestBuilder_NonFolderNonFileKindsSkipped` — an item with an
    unknown Kind GUID (e.g. `"{00000000-0000-0000-0000-000000000000}"`) is
    skipped, not recursed, not added.
  - `Run_HierarchyForestBuilder_EmptyChildrenEmptyForest` — `Build(empty, map)`
    returns an empty `List<HierarchyNode>`.
  - Each test builds `HierarchyItemInfo` instances directly (pure DTO) and calls
    `HierarchyForestBuilder.Build(items, new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase))`.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- HierarchyForestBuilder` —
  RED: the project fails to compile because `HierarchyForestBuilder` /
  `HierarchyItemInfo` do not exist (compile error is the RED signal).
- **Fails-if:** the tests compile and pass with NO production change; the tests
  reference `Microsoft.VisualStudio.Shell.Interop` GUID literals instead of
  `HierarchyResolver.PhysicalFolderKind`/`PhysicalFileKind` (must reuse the
  existing constants); the tests construct `HierarchyNode` directly instead of
  going through `HierarchyForestBuilder.Build` (would not prove the builder).

### BP-4 — M14 GREEN: create the pure `HierarchyForestBuilder` + `HierarchyItemInfo`

- **Files:** `MyExtension/ToolWindows/HierarchyForestBuilder.cs` (new).
- **Change:** in `namespace MyExtension`, add:
  - `internal sealed class HierarchyItemInfo` — pure DTO mirroring
    `HierarchyNode`: `string Kind`, `string Name`, `string FullPath`,
    `IReadOnlyList<HierarchyItemInfo>? Children` (ctor sets all four).
  - `internal static class HierarchyForestBuilder` with
    `public static List<HierarchyNode> Build(IEnumerable<HierarchyItemInfo> items, Dictionary<string,string> pathToItem)`:
    - Physical folder (`item.Kind == HierarchyResolver.PhysicalFolderKind`) →
      recurse `Build(item.Children, pathToItem)`, add
      `new HierarchyNode(HierarchyResolver.PhysicalFolderKind, item.Name, "", children)`.
    - Physical file (`item.Kind == HierarchyResolver.PhysicalFileKind` AND
      `item.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)`) → add
      `new HierarchyNode(HierarchyResolver.PhysicalFileKind, item.Name, item.FullPath, null)`
      and record `pathToItem[item.FullPath] = item.FullPath` (identity — the pure
      builder cannot hold DTE objects; the DTE adapter owns the
      `Dictionary<string, UIHierarchyItem>` map).
    - Any other kind → skipped (no recursion, no node).
  - Reuse `HierarchyResolver.PhysicalFolderKind` / `PhysicalFileKind` — do NOT
    duplicate the GUID literals. net472-safe (no `IReadOnlySet<T>`; `IReadOnlyList<T>` only).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- HierarchyForestBuilder` —
  all five `Run_HierarchyForestBuilder_*` tests PASS.
- **Fails-if:** `Run_HierarchyForestBuilder_NestedFoldersProduceNestedChildren`
  fails (folder recursion broken); `Run_HierarchyForestBuilder_CsFilterCaseInsensitive`
  fails (`.CS` excluded or `.config` included); `Run_HierarchyForestBuilder_FullPathFlowsThroughAndPathMap`
  fails (FilePath or pathToItem not populated); `Run_HierarchyForestBuilder_NonFolderNonFileKindsSkipped`
  fails (unknown kind recursed/added); `Run_HierarchyForestBuilder_EmptyChildrenEmptyForest`
  fails (empty input throws or returns non-empty).

### BP-5 — M14 wiring: rewire `SolutionExplorerController` to the pure builder, delete `BuildForest`

- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs` (modify).
- **Change:**
  - Delete the private `BuildForest(EnvDTE.UIHierarchyItem, List<HierarchyNode>, Dictionary<string, EnvDTE.UIHierarchyItem>)`
    method (lines 335-362) — the only place `Kind`/`Name`/`FileNames[FileCount]`
    were read.
  - Add a thin DTE adapter (the ONLY place `pi.Kind` / `pi.Name` /
    `pi.FileNames[(short)pi.FileCount]` are read):
    `private static List<HierarchyItemInfo> MapChildren(EnvDTE.UIHierarchyItem item, Dictionary<string, EnvDTE.UIHierarchyItem> pathToItem)` —
    iterates `item.UIHierarchyItems`; for each `child.Object is EnvDTE.ProjectItem pi`,
    reads `pi.Kind`; physical folder → recurse `MapChildren(child, pathToItem)` and
    add `new HierarchyItemInfo(kind, pi.Name, "", children)`; physical file →
    `string fullPath = pi.FileNames[(short)pi.FileCount];` add
    `new HierarchyItemInfo(kind, pi.Name, fullPath, null)` and
    `pathToItem[fullPath] = child;` (the real path→UIHierarchyItem map).
  - Update `ResolveTreeItem` (lines 286-302): replace the
    `var forest = new List<HierarchyNode>(); var pathToItem = ...; BuildForest(projectNode, forest, pathToItem);`
    block with
    `var pathToItem = new Dictionary<string, EnvDTE.UIHierarchyItem>(StringComparer.OrdinalIgnoreCase);`
    `var items = MapChildren(projectNode, pathToItem);`
    `var forest = HierarchyForestBuilder.Build(items, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));`
    — the rest of `ResolveTreeItem` (the `pick(forest)` + `pathToItem.TryGetValue` lookup)
    is unchanged, so `SelectFirstSourceFile` / `ReturnFocusToTree` behavior and the
    `solution-explorer select file=...` / `select none` diagnostics are preserved.
  - `ThreadHelper.ThrowIfNotOnUIThread()` discipline unchanged (all DTE reads stay
    on the UI thread; the pure builder is called from the UI thread).
- **Verify-with:** `dotnet build` clean (no dangling `BuildForest` reference);
  `dotnet run --project tests/NeoVisual.Tests` — the existing
  `Run_HierarchyResolver_*`, `Run_SolutionExplorer_ActionKeys`,
  `Run_ActionTable_SolutionExplorer_*` tests still PASS (behavior-preserving
  rewire); `dotnet run --project tests/NeoVisual.Tests -- HierarchyForestBuilder`
  still GREEN.
- **Fails-if:** build error (a `BuildForest` reference remains, or `MapChildren`/
  `HierarchyItemInfo` signature mismatch); any existing `Run_SolutionExplorer_*` /
  `Run_ActionTable_SolutionExplorer_*` test fails (the rewire changed controller
  behavior); `Run_HierarchyResolver_*` fails (the forest shape changed).

### BP-6 — Doc-ref propagation: update `docs/progress.md` after removing `BuildForest`

- **Files:** `docs/progress.md` (modify).
- **Change:** line 705 backticks `BuildForest` in the change-summary
  `` `SelectFirstSourceFile`/`BuildForest`/`FindFirstProjectNode` ``. Replace the
  removed symbol so the doc-ref lint resolves: reword to
  `` `SelectFirstSourceFile`/`HierarchyForestBuilder`/`FindFirstProjectNode` ``
  (the new symbol exists after BP-4/BP-5). Do NOT touch any other line.
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` exits 0 (no `BuildForest`
  unresolved-symbol issue).
- **Fails-if:** `check-doc-refs.ps1` reports
  `docs/progress.md : `BuildForest` — no such symbol` (doc-ref drift — the
  acceptance gate for this removal step).

---

## Verification Trace

| failing test / gate | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_Issues_SeverityMediumMapsToWarning` | BP-1, BP-2 | unit test PASS; `[Telescope] opened issue: ...` format unchanged (M13 preserves `OpenHit`/`ToEntry`) |
| `Run_Issues_SeverityHighMapsToError` | BP-1, BP-2 | unit test PASS; `[Telescope] opened issue: ...` format unchanged |
| `Run_Issues_SeverityLowMapsToInfo` | BP-1, BP-2 | unit test PASS; `[Telescope] opened issue: ...` format unchanged |
| `Run_HierarchyForestBuilder_NestedFoldersProduceNestedChildren` | BP-3, BP-4 | unit test PASS (no diagnostic — pure builder) |
| `Run_HierarchyForestBuilder_CsFilterCaseInsensitive` | BP-3, BP-4 | unit test PASS |
| `Run_HierarchyForestBuilder_FullPathFlowsThroughAndPathMap` | BP-3, BP-4 | unit test PASS |
| `Run_HierarchyForestBuilder_NonFolderNonFileKindsSkipped` | BP-3, BP-4 | unit test PASS |
| `Run_HierarchyForestBuilder_EmptyChildrenEmptyForest` | BP-3, BP-4 | unit test PASS |
| `dotnet build` (M13/M14 compile gate) | BP-2, BP-5 | build clean; no `Classify(string)` / `BuildForest` references remain |
| existing `Run_HierarchyResolver_*` / `Run_SolutionExplorer_*` / `Run_ActionTable_SolutionExplorer_*` | BP-5 | unit tests PASS (behavior-preserving rewire) |
| `pwsh tools/check-doc-refs.ps1` (doc-ref drift gate) | BP-6 | exit 0 |

**Known-RED allowlist (excluded from regression reporting):** none — per
`plans/plan.md` lines 786-789 there is no known-RED e2e scenario remaining; all
Phase 5 fixes are RED-proven by the unit tests above (or build + existing suites
for the no-seam rewire). e2e scenarios are deferred and must NOT be run or
reported by the verification-agent.

## KEY DECISIONS (do not second-guess)

- **`item.ErrorLevel`, not `item.Severity`** — the resolved `EnvDTE80.ErrorItem`
  property is `ErrorLevel` (type `vsBuildErrorLevel`); `Severity` does not compile.
- **`vsBuildErrorLevel` is `EnvDTE80.vsBuildErrorLevel`** (values Low=1, Medium=2,
  High=4) — not `Microsoft.VisualStudio.Shell.Interop`; it resolves in the test
  process via the already-referenced `Microsoft.VisualStudio.Interop` package.
- **`ClassifySeverity` is `internal static`** (not `private`) so the tests can
  call it via `InternalsVisibleTo`.
- **`HierarchyForestBuilder` reuses `HierarchyResolver.PhysicalFolderKind` /
  `PhysicalFileKind` and produces the existing `HierarchyNode`** — no duplicated
  GUID literals, no new node type.
- **The pure builder's `pathToItem` is `Dictionary<string,string>` (path→path
  record of added `.cs` files)** — it cannot hold DTE objects; the DTE adapter
  (`MapChildren`) owns the real `Dictionary<string, UIHierarchyItem>` used by
  `ResolveTreeItem`'s final lookup.
- **`docs/progress.md:705` must be updated** (BP-6) or the doc-ref lint fails the
  `BuildForest` removal.


### Phase 6 - Hook-path safety (M15, M16, M17)

# Phase 6 Build Plan — Hook-path safety (M15, M16, M17)

> **Lane: unit-only, e2e deferred.** Source: `plans/plan.md` Phase 6 (lines 264-309),
> Acceptance criteria row 6 (line 738), Unit test plan (lines 757-764), Diagnostics
> (lines 769-784). Every fix is RED-proven by a unit test in `tests/NeoVisual.Tests`
> (or build + existing suites for the no-seam parts); the live behavior is gated by
> the deferred e2e scenarios in `docs/e2e-queue.md` (E2E-M16, E2E-M17/M32 — QUEUED).
> **Verify-with = unit test names + diagnostic formats ONLY** (no e2e references in
> Verify-with/Fails-if; deferred e2e scenarios appear only in the Verification Trace
> as future gates).

## Scope

- **M15** — `LeaderSequenceMatcher.cs:63` `action()` unguarded in the hook path; sibling
  `InputHandler.cs:351` `simpleAction()` identical hole. Fix: failure-carrying
  `LeaderResultKind.Failed` + `[NeoVisual] leader-binding failed:` /
  `[NeoVisual] shortcut-binding failed:` logs.
- **M16** — `FocusGuard.cs:26,35` + `InputHandler.cs:90-93`: the guard cannot tell
  "text-input surface genuinely focused" from "editor genuinely focused". Fix: a
  `textInputSurfaceFocused` signal + parameter, re-deriving BOTH `FocusGuard` truth
  tables AND `EditorFocusedVeto` in lockstep. MUST NOT regress the D4 deviation
  (`neovisual-textinput-motions`).
- **M17** — `VimModeTracker.cs:189,209,488`: `_cachedTyping` cleared unconditionally on
  LostFocus/Closed (out-of-order events); `_resolved` latches on failure. Fix: one pure
  `VimModeState` owner (single owner method + `vim-mode=` log), clear only when
  `ReferenceEquals(_focusedView, view)`, don't latch `_resolved`.

## Known-RED allowlist

**None** (per `plans/plan.md` line 786-789 and `docs/progress.md` — no known-RED e2e
scenario remains). The D4 deviation (`neovisual-textinput-motions`) is a
**MUST-NOT-REGRESS** contract, not an allowlist entry: the verification-agent must not
flag a regression there, and the build-agent must keep it passing.

## Build Plan

### BP-1 — `LeaderSequenceMatcher`: failure-carrying result (M15 core)

- **Files:** `MyExtension/LeaderSequenceMatcher.cs`
- **Change:**
  - `LeaderResultKind` (line 110-116): add `Failed`.
  - `LeaderResult` (line 122-139): add `public string? ErrorMessage { get; }`; extend the
    private ctor with an `errorMessage` param; add
    `public static LeaderResult Failed(string sequence, string errorMessage)`.
  - `HandleKey` (line 59-65): wrap `action()` in try/catch — `_active`/`_sequence` are
    already cleared before the call; on exception return
    `LeaderResult.Failed(sequence, ex.Message)`; on success return
    `LeaderResult.Execute(action, sequence)` unchanged.
- **Verify-with:** `Run_LeaderMatcher_ThrowingActionIsCaught` (NeoVisual.Tests) — bind
  `["F"] = () => throw new KeyNotFoundException("bad finder")`, drive Space then F, assert
  `result.Kind == LeaderResultKind.Failed`, `result.Sequence == "F"`,
  `result.ErrorMessage` contains `"bad finder"`, `matcher.IsActive == false`, and no
  exception escapes. **RED today:** `action()` throws at `:63` and escapes the test.
- **Fails-if:** the exception escapes `HandleKey`; `result.Kind != Failed`; `IsActive`
  stays true after a failed execution; `ErrorMessage` is null/empty.

### BP-2 — `InputHandler`: log leader/shortcut binding failures (M15 + sibling)

- **Files:** `MyExtension/InputHandler.cs`
- **Change:**
  - `switch (result.Kind)` (line 332-343): add
    `case LeaderResultKind.Failed:` → log
    `[NeoVisual] leader-binding failed: {result.Sequence}: {result.ErrorMessage}` and
    `return true` (the matched binding is consumed even though it failed — mirrors the
    `Execute` case which returns true).
  - `simpleAction()` (line 348-353): wrap in try/catch — on exception log
    `[NeoVisual] shortcut-binding failed: {simple}: {ex.Message}`; still `return true`.
  - The existing `leader-binding executed: {result.Sequence}` (line 339) and
    `shortcut-binding executed: {simple}` (line 350) lines stay byte-identical.
- **Verify-with:** diagnostic formats (exact, via `NeoVisualLog.Log` +
  `Telescope.DiagnosticLog.NeoVisual` prefix):
  `[NeoVisual] leader-binding failed: {result.Sequence}: {result.ErrorMessage}` and
  `[NeoVisual] shortcut-binding failed: {simple}: {ex.Message}`; `dotnet build` +
  existing NeoVisual.Tests suite (no hermetic seam — `InputHandler` is
  AsyncPackage/MEF/WindowManager-coupled; the pure matcher side is covered by BP-1).
- **Fails-if:** `leader-binding failed:` / `shortcut-binding failed:` never emitted; an
  exception from `simpleAction()` escapes `HandleKey`; the `executed:` lines change
  format.

### BP-3 — `FocusGuard`: `textInputSurfaceFocused` parameter + re-derived truth tables (M16 core)

- **Files:** `MyExtension/ToolWindows/FocusGuard.cs`
- **Change:**
  - `HasToolWindowActionKeys(bool isToolWindow, bool isInputMode, int actionKeyCount,
    bool editorFocused, bool isTextInputSurface, bool textInputSurfaceFocused)` (line
    24-26) →
    `isToolWindow && !isInputMode && actionKeyCount > 0 && (!editorFocused || (isTextInputSurface && textInputSurfaceFocused))`.
  - `ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused, bool isInputMode,
    bool isTextInputSurface, bool textInputSurfaceFocused)` (line 34-35) →
    `isToolWindow && !(editorFocused && !isInputMode && !(isTextInputSurface && textInputSurfaceFocused))`.
  - `IsTyping` (line 42-46) unchanged — its `editorFocused` argument is fed
    `EditorFocusedVeto`, re-derived in BP-5.
- **Verify-with:** RED tests (NeoVisual.Tests):
  - `Run_FocusGuard_TextInputSurfaceFocused_EditorFocusedNotFocusedSurface` —
    `HasToolWindowActionKeys(isToolWindow: true, isInputMode: false, actionKeyCount: 6,
    editorFocused: true, isTextInputSurface: true, textInputSurfaceFocused: false)` must
    be **FALSE** (currently TRUE); same for `ShouldRouteToolWindowKey`.
  - `Run_FocusGuard_TextInputSurfaceFocused_GenuinelyFocusedOwnsKeyboard` (D4
    preservation) — the same two calls with `textInputSurfaceFocused: true` must be
    **TRUE**.
  - **Update** the existing FocusGuard tests to the new signatures (compile error is the
    RED signal): `Run_FocusGuard_TruthTable_TextInputSurfaceOwnsKeyboard` (line 515) and
    `Run_FocusGuard_TruthTable_ActionKeysTextInputSurface` (line 545) now pass
    `textInputSurfaceFocused: true`; the other `Run_FocusGuard_*` tests (lines 460-557)
    pass `textInputSurfaceFocused: false`; all existing assertions unchanged.
- **Fails-if:** the RED case returns true; the D4 case returns false; any existing
  FocusGuard assertion flips.

### BP-4 — `WindowManager`: `TextInputSurfaceFocused` signal (M16 signal, no-seam)

- **Files:** `MyExtension/WindowManager.cs`
- **Change:** add
  `public bool TextInputSurfaceFocused` — true only when `IsToolWindow` AND the current
  tool-window frame's WPF content contains `Keyboard.FocusedElement` (walk the
  visual/logical tree from `Keyboard.FocusedElement` up to the frame's
  `VSFPROPID_DocView` `FrameworkElement`, mirroring `TextMotionHelper.GetParent`);
  false when not a tool window, no focused element, or the focused element is outside
  the frame (e.g. the editor's text view). UI-thread only
  (`ThreadHelper.ThrowIfNotOnUIThread()`). The guard (BP-3) ANDs this with
  `isTextInputSurface`, so the signal is the raw "the current tool window genuinely
  holds WPF focus" fact.
- **Verify-with:** `dotnet build` + existing NeoVisual.Tests suite (no hermetic seam —
  VS/WPF-coupled; the pure truth table is covered by BP-3). The signal feeds BP-5.
- **Fails-if:** returns true while the editor holds focus; returns false while the
  Command Window genuinely holds focus; throws off the UI thread.

### BP-5 — `InputHandler`: re-derive `EditorFocusedVeto` + wire the signal (M16 lockstep)

- **Files:** `MyExtension/InputHandler.cs`
- **Change:**
  - `EditorFocusedVeto` (line 90-93) →
    `_vsVim.IsEditorFocused && _windowManager.CurrentController?.IsInputMode != true && !(GeneralToolWindowController.IsTextInputType(_windowManager.Type) && _windowManager.TextInputSurfaceFocused)`.
  - All 4 `FocusGuard` call sites add `textInputSurfaceFocused: _windowManager.TextInputSurfaceFocused`:
    line 76 (`HasToolWindowActionKeys`), line 283 (`ShouldRouteToolWindowKey`),
    line 396 (`ShouldRouteToolWindowKey` in `IsKeyOfInterest`), line 423
    (`ShouldRouteToolWindowKey` in `ExitToolWindowInputMode`).
  - `IsTyping()` (line 441-448) unchanged — it already feeds `EditorFocusedVeto` as the
    `editorFocused` argument, so the re-derived veto propagates automatically.
- **Verify-with:** `dotnet build` + the BP-3 RED tests pass (the guard contract); the
  D4 deviation preserved by `Run_FocusGuard_TextInputSurfaceFocused_GenuinelyFocusedOwnsKeyboard`
  (`textInputSurfaceFocused: true` → TRUE); the leak closed by
  `Run_FocusGuard_TextInputSurfaceFocused_EditorFocusedNotFocusedSurface`
  (`textInputSurfaceFocused: false` → FALSE). Deferred e2e gates are listed in the
  Verification Trace only.
- **Fails-if:** `EditorFocusedVeto` still vetoes a genuinely-focused text-input surface;
  a text-input surface in normal mode while the editor holds focus still routes keys
  (the leak); a navigation tool window's action keys leak into a focused editor.

### BP-6 — `VimModeState`: pure owner of typing/mode state + focus guards + resolution latch (M17 core)

- **Files:** `MyExtension/VimModeState.cs` (new)
- **Change:** `internal sealed class VimModeState` (dependency-free, no VS types):
  - Constants `Normal = 1`, `Insert = 2`, `Replace = 7` (moved from `VimModeTracker`;
    M32 in Phase 9 lifts them into `VimModeClassifier` — do NOT duplicate).
  - `private volatile bool _isTyping; public bool IsTyping => _isTyping;` (volatile
    preserves the hook-path read contract).
  - `public string ModeName { get; private set; } = "Unknown";`
  - `public void SetMode(int? mode)` — `_isTyping = mode == Insert || mode == Replace;`
    `ModeName = mode switch { Normal => "Normal", Insert => "Insert", Replace =>
    "Replace", _ => mode?.ToString() ?? "Unknown" };` (the M32 classification seed).
  - `public void Clear() => SetMode(null);`
  - `public bool OnViewLostFocus(bool isFocusedView)` / `public bool OnViewClosed(bool
    isFocusedView)` — clear only when `isFocusedView`; return whether the typing flag
    changed (so the tracker logs only on change).
  - `public object? ResolveOnce(Func<object?> resolve, Action<Exception> onFailure)` —
    resolution retry latch: on success latch `_resolved = true`; on failure keep
    `_resolved = false` (retry next call) + invoke `onFailure`.
- **Verify-with:** RED tests (NeoVisual.Tests):
  - `Run_VimModeState_OutOfOrderLostFocusKeepsTyping` — `SetMode(Insert);
    OnViewLostFocus(isFocusedView: false); IsTyping == true` (**RED on :189**).
  - `Run_VimModeState_ClosedNonFocusedKeepsTyping` — `SetMode(Insert);
    OnViewClosed(isFocusedView: false); IsTyping == true` (**RED on :209**).
  - `Run_VimModeState_ClassificationTruthTable` — `SetMode(Normal).IsTyping == false &&
    ModeName == "Normal"`; `SetMode(Insert).IsTyping == true && ModeName == "Insert"`;
    `SetMode(Replace).IsTyping == true && ModeName == "Replace"`; `SetMode(null).IsTyping
    == false && ModeName == "Unknown"` (pins the `vim-mode=` name contract).
  - `Run_VimModeState_ResolutionRetriesAfterFailure` — first
    `ResolveOnce(() => throw new InvalidOperationException("MEF down"), _ => {})` returns
    null and does NOT latch; second `ResolveOnce(() => "vim", _ => {})` returns `"vim"`
    and latches; third call returns the latched value without re-invoking the resolver
    (**RED on :488**).
- **Fails-if:** any classification wrong; out-of-order LostFocus/Closed clears typing;
  `ResolveOnce` latches on failure (second call returns null without retrying).

### BP-7 — `VimModeTracker`: delegate to `VimModeState` (M17 tracker refactor)

- **Files:** `MyExtension/VimModeTracker.cs`
- **Change:**
  - Replace `_cachedTyping` (line 99), `_resolved` (line 76), `_vim` (line 75), and the
    `Normal/Insert/Replace` constants (line 63-65) with
    `private readonly VimModeState _state = new VimModeState();`.
  - `IsInTypingMode` (line 116) → `_state.IsTyping`.
  - `UpdateTypingFromMode(int? mode)` (line 366-379) becomes the **single owner method**:
    `_state.SetMode(mode);` then log `vim-mode={_state.ModeName}` (byte-identical
    format). Add a private `LogMode()` helper used by the focus handlers.
  - `OnViewLostFocus` (line 178-190): clear typing only when
    `ReferenceEquals(_focusedView, view)` — `if (sender is ITextView view) { bool
    isFocusedView = ReferenceEquals(_focusedView, view); if (isFocusedView) {
    _editorFocused = false; } if (_state.OnViewLostFocus(isFocusedView)) { LogMode(); } }`.
  - `OnViewClosed` (line 192-210): same guard — clear typing only when
    `ReferenceEquals(_focusedView, view)`.
  - `OnBufferClosed` (line 354-363): route the clear through `_state` (already guarded by
    `ReferenceEquals(sender, _currentBuffer)`).
  - `OnViewGotFocus` buffer-null case (line 173): `UpdateTypingFromMode(null)`.
  - `GetVim()` (line 481-512): `return _state.ResolveOnce(() => { ...existing MEF
    resolution...; return _vim; }, ex => NeoVisualLog.Debug($"{Telescope.DiagnosticLog.NeoVisual}VsVim integration resolve failed: {ex.Message}"));`
    — `_resolved` latches only on success.
- **Verify-with:** `dotnet build` + existing NeoVisual.Tests suite + the `Run_VimModeState_*`
  tests (BP-6) pass; the `vim-mode=` name contract pinned by
  `Run_VimModeState_ClassificationTruthTable` (Normal/Insert/Replace/Unknown). Deferred
  e2e gates are listed in the Verification Trace only.
- **Fails-if:** `vim-mode=` format changes; `_cachedTyping` written outside
  `UpdateTypingFromMode`; LostFocus/Closed still clear unconditionally; `GetVim()`
  latches on failure.

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_LeaderMatcher_ThrowingActionIsCaught` (RED) | BP-1, BP-2 | `[NeoVisual] leader-binding failed: F: bad finder` (BP-2 emits; BP-1 returns `Kind=Failed`) |
| `Run_FocusGuard_TextInputSurfaceFocused_EditorFocusedNotFocusedSurface` (RED) | BP-3, BP-5 | none (pure truth table) |
| `Run_FocusGuard_TextInputSurfaceFocused_GenuinelyFocusedOwnsKeyboard` (D4) | BP-3, BP-5 | none (pure truth table) |
| existing `Run_FocusGuard_*` (updated signatures, lines 460-557) | BP-3 | none (assertions unchanged) |
| `Run_VimModeState_OutOfOrderLostFocusKeepsTyping` (RED) | BP-6, BP-7 | `vim-mode=Insert` preserved (no spurious clear on out-of-order LostFocus) |
| `Run_VimModeState_ClosedNonFocusedKeepsTyping` (RED) | BP-6, BP-7 | `vim-mode=Insert` preserved (no spurious clear on non-focused Closed) |
| `Run_VimModeState_ClassificationTruthTable` (RED) | BP-6 | `vim-mode=Insert\|Normal\|Replace\|Unknown` name contract |
| `Run_VimModeState_ResolutionRetriesAfterFailure` (RED) | BP-6, BP-7 | none (pure latch) |
| deferred e2e `neovisual-textinput-motions` (D4, QUEUED) | BP-3, BP-4, BP-5 | `text-motion key=...` / `textinput-enter-input ...` unchanged |
| deferred e2e `neovisual-editor-insert` (QUEUED) | BP-6, BP-7 | `vim-mode=Insert\|Normal\|Replace` unchanged |
| deferred e2e `neovisual-explorer-move-editor-focus` (QUEUED) | BP-4, BP-5 | no `solution-explorer ...` line while the editor holds focus |

**Known-RED allowlist:** none (per `plans/plan.md` + `docs/progress.md`). The D4
deviation (`neovisual-textinput-motions`) is a MUST-NOT-REGRESS contract, not an
allowlist entry.

## KEY DECISIONS (build-agent must not second-guess)

- **M15's catch lives in the pure `LeaderSequenceMatcher`** (returns `Failed`); the log
  stays in `InputHandler` so the pure class stays VS-free. The sibling `simpleAction`
  fix is InputHandler-only (no hermetic seam — verified by the diagnostic format).
- **M16's `textInputSurfaceFocused` is a pure guard parameter** (unit-testable truth
  table) **+ a VS/WPF-coupled `WindowManager.TextInputSurfaceFocused` signal**
  (no-seam, honest verification = build + existing suites + deferred e2e). `IsTyping`
  is unchanged — it inherits the re-derived `EditorFocusedVeto`.
- **M17's `VimModeState` is the single pure owner** of the typing flag + mode name +
  classification + focus-guard decision + resolution latch; the `vim-mode=` log stays in
  the tracker's `UpdateTypingFromMode` (byte-identical). The classification inside
  `SetMode` is the seed for M32's `VimModeClassifier` (Phase 9) — do NOT duplicate it.
- **No MEF/DI wiring changes:** `VimModeState` is a plain internal class instantiated by
  `VimModeTracker`; `WindowManager.TextInputSurfaceFocused` rides the existing
  `_windowManager` singleton; `LeaderResultKind.Failed` is a pure enum member.
- **No doc-ref updates needed:** no referenced symbol is renamed/removed
  (`UpdateTypingFromMode`, `FocusGuard`, `VimModeTracker` names all survive; AGENTS.md /
  SKILL.md / spec.md references stay valid).

## Cross-phase hazards

- **M17's `VimModeState` is shared with M32 (Phase 9):** sequence the extraction once —
  BP-6 creates it with the classification inside `SetMode`; M32 lifts
  `Classify(int? mode)` into `VimModeClassifier` and `VimModeState.SetMode` delegates to
  it. Do not create a second classifier in Phase 9.
- **M16 must not regress the D4 deviation** (`neovisual-textinput-motions`): the
  `textInputSurfaceFocused: true` case must keep routing (BP-3's
  `Run_FocusGuard_TextInputSurfaceFocused_GenuinelyFocusedOwnsKeyboard` pins it).
- **M15's sibling `simpleAction` fix ties into M31 (Phase 9):** M31 extracts
  `SimpleShortcutMatcher` from `InputHandler.HandleKey` — the `shortcut-binding failed:`
  log must move with the matcher's result handling, not be lost.


### Phase 7 - Logging pipeline (M18, M27, M35, M43, m13, m20, m23, m24, m25)

# Phase 7 — Logging pipeline — Build Plan (BP-n) + Verification Trace

> **Source:** `plans/plan.md` Phase 7 (M18, M27, M35, M43, m13, m20, m23, m24, m25),
> cross-checked against `docs/code-review.md` M18/M27/M35/M43/m13/m20/m23/m24/m25 and the
> current source (`Telescope/NeoVisualLog.cs`, `Telescope/LogFileWriter.cs`,
> `Telescope/NeoVisualTraceListener.cs`, `Telescope/FinderBase.cs`, `Telescope/CodeIssuesFinder.cs`,
> `Telescope/GrepFinder.cs`, `Telescope/TelescopeController.cs`, `Telescope/TelescopeOverlay.cs`,
> `MyExtension/MyExtensionPackage.cs`, `tests/Telescope.Tests/Program.cs`).
> **Lane:** unit-only (e2e deferred). **Verify-with = unit test names + diagnostic formats ONLY**
> (no e2e scenario references). **Known-RED allowlist:** none (per `docs/progress.md` — no
> known-RED e2e scenario remains).
>
> **Execution:** one top-to-bottom pass by the build-agent. Steps are ordered so each
> `NeoVisualLog.Debug` call site is touched exactly once (BP-5 routes the 6 Telescope sites to
> `TelescopeLog.Log` BEFORE BP-6 deletes the `Debug` alias and switches the remaining 29 sites to
> `NeoVisualLog.Log`). Current suite counts: `tests/Telescope.Tests` = 84, `tests/NeoVisual.Tests`
> = 81 (AGENTS.md's 77/74 is stale — compare against `--list`).

## Build Plan

### BP-1 — M27 core: extract `WriteTo` in `LogFileWriter` (behavior-preserving)

- **Files:** `Telescope/LogFileWriter.cs` (modify).
- **Change:** add `private static void WriteTo(ref StreamWriter? writer, string path, string message)`
  holding the shared body currently duplicated in `Write` (`:103-117`) and `WriteDebug` (`:120-134`):
  `string line = FormatLine(message); lock (Sync) { try { GetWriter(ref writer, path).Write(line + Environment.NewLine); } catch { /* never throw */ } }`.
  `Write(string message)` → `WriteTo(ref _logWriter, LogPath, message)`; `WriteDebug(string message)`
  → `WriteTo(ref _debugWriter, DebugLogPath, message)`. No behavior change — the emitted bytes and
  the never-throw contract are identical.
- **Verify-with:** existing `Run_LogFileWriter_WritesAndClearsFile`,
  `Run_LogFileWriter_Buffered_NotFlushedYet`, `Run_LogFileWriter_Buffered_FlushWritesToDisk`,
  `Run_LogFileWriter_Buffered_ContentIdenticalToAppend`, `Run_LogFileWriter_Buffered_FlushOnClose`,
  `Run_LogFileWriter_Buffered_PathChangeReopens` (6 tests) all still pass — they pin the buffered
  write/flush/clear/path-reopen behavior the refactor must preserve.
- **Fails-if:** any of the 6 `Run_LogFileWriter_*` tests fail (behavior drift in the refactor);
  `Run_LogFileWriter_Buffered_NotFlushedYet` fails (a buffered write hit disk before `Flush()`);
  `Run_LogFileWriter_Buffered_PathChangeReopens` fails (writer not reopened on path change).

### BP-2 — M43: one-shot flush timer + `FlushCount` seam

- **Files:** `Telescope/LogFileWriter.cs` (modify).
- **Change:** in `EnsureTimer` (`:200-207`) replace the periodic `new Timer(_ => Flush(), null, 200, 200)`
  with a one-shot `new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite)`; re-arm it on
  write by calling `_flushTimer?.Change(200, Timeout.Infinite)` inside `GetWriter` (after the writer
  is created) so the timer fires ~200ms after the last write and then sleeps. Add
  `internal static int FlushCount` incremented at the top of `FlushLocked` (`:170-181`) as the test
  seam. Reject Option B (dispose on `Clear`) — it does not stop idle wakeups.
- **Verify-with:** new `Run_LogFileWriter_FlushTimer_OneShotFires` (write → wait ~250ms →
  `FlushCount >= 1`) and `Run_LogFileWriter_FlushTimer_IdleDoesNotFire` (after the first flush,
  wait ~500ms with no writes → `FlushCount` unchanged). RED today: the periodic timer keeps
  climbing during the idle wait. Both tests use a fresh temp `LogPath` and MUST NOT call `Clear()`
  (the once-per-process `_clearedThisProcess` flag is consumed by `Run_LogFileWriter_WritesAndClearsFile`).
- **Fails-if:** `FlushCount` doesn't exist (compile error); `Run_LogFileWriter_FlushTimer_IdleDoesNotFire`
  fails (timer still periodic); `Run_LogFileWriter_FlushTimer_OneShotFires` fails (timer never fires —
  re-arm missing). Timing note: the ~250ms wait is real-time; if the first flush is slow under load,
  poll up to ~1s for `FlushCount >= 1` before asserting.

### BP-3 — M35: `WriteFailureCount` + one-time marker-file fallback

- **Files:** `Telescope/LogFileWriter.cs` (modify).
- **Change:** add `internal static int WriteFailureCount`; increment it inside each catch in `WriteTo`
  (the shared body from BP-1 — covers both `Write` and `WriteDebug`) under `lock(Sync)`. Preserve the
  never-throw contract. Add a one-time marker-file fallback for the harness: on the FIRST write
  failure, best-effort (own try/catch, never throws) write a `neovisual-write-failed` sentinel file
  into the log directory (`Path.GetDirectoryName(LogPath)`), guarded by a `bool _failureMarkerWritten`
  so it is written once per process. The harness (deferred e2e) checks for this sentinel to fail fast
  instead of timing out.
- **Verify-with:** new `Run_LogFileWriter_WriteFailureCountIncrements` and
  `Run_LogFileWriter_WriteDebugFailureCountIncrements` — RED: `WriteFailureCount` doesn't exist →
  compile error. Force failure by setting `LogPath`/`DebugLogPath` to a path whose parent is a file
  (so `Directory.CreateDirectory`/`new StreamWriter` throws); assert no exception escapes and
  `WriteFailureCount` delta == 1. MUST NOT call `Clear()` (once-per-process flag).
- **Fails-if:** `WriteFailureCount` doesn't exist (compile error); the count does not increment
  (catch not reached — e.g. the failure-forcing path doesn't actually throw); an exception escapes
  (never-throw contract broken).

### BP-4 — M18: `PaneFailureTracker` + one-time pane-failure fallback + serialize

- **Files:** `Telescope/PaneFailureTracker.cs` (new, pure, dependency-free), `Telescope/NeoVisualLog.cs` (modify).
- **Change:** extract a pure `internal sealed class PaneFailureTracker` mirroring the
  `OverlayKeyHandler`/`TextMotionNavigator` pattern: `bool ShouldEmit()` (returns true once, then
  false forever) and `string FallbackMessage(string reason)` returning
  `DiagnosticLog.NeoVisual + "output pane unavailable: " + reason`. In `NeoVisualLog.WriteToPane`
  (`:113-124`), replace the empty catch with: on exception, if `_paneFailureTracker.ShouldEmit()`,
  call `LogFileWriter.Write(_paneFailureTracker.FallbackMessage(reason))` — the FILE path only,
  NEVER `Log` (which would re-enter `WriteToPane` → recursion). Serialize `_pane`/`_paneInitTried`
  reads/writes in `WriteToPane`/`EnsurePane` with a `private static readonly object PaneSync` lock
  (mirror `LogFileWriter.Sync`).
- **Verify-with:** new `Run_PaneFailureTracker_*` — drive `PaneFailureTracker` twice (two pane
  failures), writing `FallbackMessage("...")` via `LogFileWriter.Write` only when `ShouldEmit()`
  returns true, then `Flush()` and assert the temp `LogPath` file contains EXACTLY ONE
  `[NeoVisual] output pane unavailable: <reason>` line. RED today: `PaneFailureTracker` doesn't
  exist → compile error (and today zero fallback lines are ever emitted). Diagnostic format:
  `[NeoVisual] output pane unavailable: <reason>` (one-time).
- **Fails-if:** `PaneFailureTracker` doesn't exist (compile error); two failures emit two fallback
  lines (one-time guard broken); the fallback line is emitted via `NeoVisualLog.Log` instead of
  `LogFileWriter.Write` (recursion/double-write — grep the catch for `Log(`).

### BP-5 — m13/m20: route finder errors through `TelescopeLog.Log` + consolidate the double-log

- **Files:** `Telescope/FinderBase.cs`, `Telescope/ImplementationFinder.cs`,
  `Telescope/ReferencesFinder.cs`, `Telescope/TelescopeOverlay.cs`, `Telescope/TelescopeController.cs`,
  `Telescope/CodeIssuesFinder.cs`, `Telescope/GrepFinder.cs` (modify).
- **Change:** at all 6 sites that bypass `TelescopeLog.Log` via `NeoVisualLog.Debug`, switch to
  `TelescopeLog.Log(...)` and DROP the `DiagnosticLog.Telescope` concatenation (TelescopeLog.Log adds
  it):
  - `TelescopeOverlay.cs:746` → `TelescopeLog.Log($"OnSelected failed: {ex.Message}")`
  - `TelescopeController.cs:56` → `TelescopeLog.Log($"Unknown finder '{finderName}'.")`
  - `CodeIssuesFinder.cs:182` → `TelescopeLog.Log($"Error List read failed: {ex.Message}")`
  - `GrepFinder.cs:92` → `TelescopeLog.Log($"GrepFinder failed to enumerate: {ex.Message}")`
  - `FinderBase.cs:37` → `TelescopeLog.Log(GatherErrorLiteral(ex))` AND change `GatherErrorLiteral`
    (`:72`) to return the UNPREFIXED message `$"{GetType().Name} failed to enumerate: {ex.Message}"`
    (avoid double-prefix); update the two overrides `ImplementationFinder.cs:61` →
    `$"implementations gather failed: {ex.Message}"` and `ReferencesFinder.cs:63` →
    `$"references gather failed: {ex.Message}"` (drop their `DiagnosticLog.Telescope` prefixes).
  - `FinderBase.OnSelected` (`:59-60`): consolidate the double-log to ONE line per failure — keep
    `TelescopeLog.Log($"open {OpenErrorNoun} failed: {ex.Message}")`, DELETE the second
    `NeoVisualLog.Debug($"{Telescope.DiagnosticLog.Telescope}{FinderNameForErrors} failed to open ...")`
    line, and delete the now-dead `protected virtual string FinderNameForErrors` (`:76`, no subclass
    overrides it).
- **Verify-with:** new `Run_FinderBase_OpenErrorSingleLog` (reuse the `Run_FinderBase_OpenErrorSwallowed`
  pattern: a `TestFinder` whose `OpenHit` throws; set a fresh temp `LogPath`; call `OnSelected`;
  `Flush()`; assert the file contains EXACTLY ONE `[Telescope] ` line matching
  `[Telescope] open <noun> failed: <msg>` — RED today: 2 lines). New `Run_FinderBase_GatherErrorPrefixed`
  (a `TestFinder` whose gather throws; assert the single emitted line starts with `[Telescope] ` and
  matches `[Telescope] <TypeName> failed to enumerate: <msg>` — pins the prefix contract after the
  `GatherErrorLiteral` unprefixing). Existing `Run_FinderBase_OpenErrorSwallowed`,
  `Run_FinderBase_GatherErrorSwallowed`, `Run_FinderBase_GetCandidatesMapsGather`,
  `Run_FinderBase_OnSelectedOpensHit`, `Run_FinderBase_NonMatchingPayloadIgnored`,
  `Run_TelescopeLog_Prefix`, `Run_TelescopeLog_EmptyMessage` still pass.
- **Fails-if:** `Run_FinderBase_OpenErrorSingleLog` sees 2 lines (double-log not consolidated);
  a finder error line lacks the `[Telescope] ` prefix or double-prefixes (`[Telescope] [Telescope] ...`
  — a `GatherErrorLiteral` override was not unprefixed); `Run_TelescopeLog_Prefix` fails.

### BP-6 — M27: delete the `NeoVisualLog.Debug` alias + switch the remaining 29 call sites + collapse `NeoVisualTraceListener`

- **Files:** `Telescope/NeoVisualLog.cs` (delete `Debug`, `:79`), `Telescope/NeoVisualTraceListener.cs`
  (collapse `WriteLine`), and the 29 remaining `NeoVisualLog.Debug` call sites across `MyExtension/`
  and `Telescope/` (modify).
- **Change:** delete `public static void Debug(string message) => Log(message);` from `NeoVisualLog`.
  Switch every remaining `NeoVisualLog.Debug(...)` call site to `NeoVisualLog.Log(...)` — behavior-
  preserving (Debug is a pure alias; every site already carries its `DiagnosticLog.*` prefix). The 29
  sites (the 6 Telescope sites were already routed to `TelescopeLog.Log` in BP-5, and the
  `FinderBase.cs:60` double-log line was deleted): `MyExtension/GlobalKeyboardHook.cs:201`,
  `MyExtension/InputHandler.cs:139,160,233,517`, `MyExtension/KeybindingConfig.cs:87,101,104`,
  `MyExtension/MyExtensionPackage.cs:51,118,693`, `MyExtension/TelescopeLauncher.cs:47`,
  `MyExtension/VimModeTracker.cs:255,265,285,332,392,421,441,462,503,507`,
  `MyExtension/CardinalMovment/UtilityMethods.cs:58`, `MyExtension/CardinalMovment/WindowMatrix.cs:68,84,117`,
  `MyExtension/ToolWindows/SolutionExplorerController.cs:150,274,408`. Collapse
  `NeoVisualTraceListener.WriteLine` (`:26-32`) to delegate to `Write` (both bodies are identical).
- **Verify-with:** compile-time verified — `dotnet build` succeeds with ZERO `NeoVisualLog.Debug`
  references remaining (grep `NeoVisualLog\.Debug` across `MyExtension/` + `Telescope/` returns
  nothing). `Run_LogPrefixes_Pinned` still passes (prefix constants unchanged). All existing
  `Run_LogFileWriter_*` and `Run_FinderBase_*` tests still pass (behavior-preserving).
- **Fails-if:** any `NeoVisualLog.Debug` reference remains → compile error; `Run_LogPrefixes_Pinned`
  fails (a prefix constant drifted); any `Run_LogFileWriter_*`/`Run_FinderBase_*` test fails
  (a switched site changed its emitted text).

### BP-7 — m23: remove the misleading `Clear()` from `ShowOverlay` + fix the doc

- **Files:** `Telescope/TelescopeOverlay.cs` (remove `NeoVisualLog.Clear();` at `:265`),
  `Telescope/NeoVisualLog.cs` (fix the doc comment at `:18-20`).
- **Change:** delete the `NeoVisualLog.Clear();` call from `ShowOverlay` (logs accumulate across
  opens by design — the harness's fixed-log-baseline contract requires NO mid-run truncation; a
  per-open truncation would leave `LogBaseline > LogCache.Count` and break `GetRange`). Fix the
  `NeoVisualLog` class doc "call `Clear` at the start of each run (package init / overlay open)" →
  "once per process (package init only)". `LogFileWriter.Clear` (`:82-100`) is unchanged — it stays
  once-per-process.
- **Verify-with:** `dotnet build` succeeds; `Run_LogFileWriter_WritesAndClearsFile` still passes
  (Clear still truncates once); `pwsh tools/check-doc-refs.ps1` passes (doc updated). No unit seam
  (the fix removes a VS-coupled call).
- **Fails-if:** `NeoVisualLog.Clear()` still present in `ShowOverlay` (grep `TelescopeOverlay.cs`);
  the `NeoVisualLog` doc still says "overlay open" clears the file; `check-doc-refs.ps1` reports a
  stale doc ref.

### BP-8 — m24: document the `-main`/`-exp` suffix semantics + fix the stale comment

- **Files:** `Telescope/NeoVisualLog.cs` (doc on `ConfigureLogPath`, `:46-51`),
  `MyExtension/MyExtensionPackage.cs` (stale comment at `:56-57`).
- **Change:** document the suffix semantics so the inversion is unambiguous: `-exp.log` = the
  structured NeoVisual log (what the harness asserts on), `-main.log` = the raw debug-output stream.
  Fix the stale `MyExtensionPackage.cs:56-57` comment ("the exp vs main suffix is detected from this
  process's command line" — the suffix is hardcoded in `ConfigureLogPath`, not command-line-detected)
  to state the actual semantics. **Decision: document, do NOT rename** — a rename would touch 6
  harness sites (`test-e2e.ps1:1797,1801,1834-1835`, `iterate-telescope.ps1:45-46`) + test literals
  (`Program.cs:111-112` etc.) + `MyExtensionPackage.cs:339-340,391-392` + docs, and the unit-only
  lane cannot verify the harness rename (e2e deferred). Documenting is behavior-preserving and fully
  verifiable here.
- **Verify-with:** `dotnet build` succeeds; `pwsh tools/check-doc-refs.ps1` passes. No unit seam
  (string literals in the VS-coupled facade).
- **Fails-if:** the stale `MyExtensionPackage.cs:56-57` comment remains; `check-doc-refs.ps1`
  reports a stale doc ref.

### BP-9 — m25: leave as-is (documented-accepted)

- **Files:** none (no change).
- **Change:** NO code change. `Telescope/DiagnosticLog.cs:9` stays in the `Telescope` namespace per
  `docs/spec.md` §2.2 decision 2026-09-28 (the host legitimately depends on `Telescope` for
  `NeoVisualLog`/`DiagnosticLog`; moving the constants contradicts the spec decision). Do not "fix".
- **Verify-with:** `Run_LogPrefixes_Pinned` still passes (the five `DiagnosticLog` constants are
  unchanged); `dotnet build` succeeds.
- **Fails-if:** `Telescope/DiagnosticLog.cs` is moved or its constants change (someone "fixed" it) —
  this step is a guard against that.

## Verification Trace

| failing test / gate | implicated steps | expected diagnostic / pass signal |
|---|---|---|
| `Run_LogFileWriter_WritesAndClearsFile` (existing) | BP-1, BP-7 | structured + debug lines present after `Flush()`; `Clear()` truncates once |
| `Run_LogFileWriter_Buffered_NotFlushedYet` (existing) | BP-1, BP-2 | buffered write NOT on disk until `Flush()` |
| `Run_LogFileWriter_Buffered_FlushWritesToDisk` (existing) | BP-1 | `Flush()` writes the buffered line to disk |
| `Run_LogFileWriter_Buffered_ContentIdenticalToAppend` (existing) | BP-1 | buffered output byte-identical to per-call append |
| `Run_LogFileWriter_Buffered_FlushOnClose` (existing) | BP-1 | `Close()` flushes the buffered line |
| `Run_LogFileWriter_Buffered_PathChangeReopens` (existing) | BP-1 | path change closes A + reopens B; no shared lines |
| `Run_LogFileWriter_FlushTimer_OneShotFires` (NEW, M43) | BP-2 | write → ~250ms → `FlushCount >= 1` |
| `Run_LogFileWriter_FlushTimer_IdleDoesNotFire` (NEW, M43) | BP-2 | ~500ms idle → `FlushCount` unchanged (RED today: periodic timer climbs) |
| `Run_LogFileWriter_WriteFailureCountIncrements` (NEW, M35) | BP-3 | forced `Write` failure → no exception + `WriteFailureCount` delta == 1 |
| `Run_LogFileWriter_WriteDebugFailureCountIncrements` (NEW, M35) | BP-3 | forced `WriteDebug` failure → no exception + `WriteFailureCount` delta == 1 |
| `Run_PaneFailureTracker_*` (NEW, M18) | BP-4 | two pane failures → EXACTLY ONE `[NeoVisual] output pane unavailable: <reason>` line in the temp `LogPath` file |
| `Run_FinderBase_OpenErrorSingleLog` (NEW, m13/m20) | BP-5 | EXACTLY ONE `[Telescope] open <noun> failed: <msg>` line per failure (RED today: 2) |
| `Run_FinderBase_GatherErrorPrefixed` (NEW, m13/m20) | BP-5 | single line starts with `[Telescope] ` and matches `[Telescope] <TypeName> failed to enumerate: <msg>` |
| `Run_FinderBase_OpenErrorSwallowed` / `Run_FinderBase_GatherErrorSwallowed` (existing) | BP-5 | no exception propagates; empty candidates |
| `Run_FinderBase_GetCandidatesMapsGather` / `Run_FinderBase_OnSelectedOpensHit` / `Run_FinderBase_NonMatchingPayloadIgnored` (existing) | BP-5 | gather→entry mapping, payload open, non-matching payload ignored all unchanged |
| `Run_TelescopeLog_Prefix` / `Run_TelescopeLog_EmptyMessage` (existing) | BP-5 | `[Telescope] hello` / `[Telescope] ` prefix emitted |
| `Run_LogPrefixes_Pinned` (existing) | BP-6, BP-9 | five `DiagnosticLog` prefix constants unchanged |
| `dotnet build` (M27 Debug-alias deletion) | BP-6 | zero `NeoVisualLog.Debug` references remain (compile-time) |
| `pwsh tools/check-doc-refs.ps1` | BP-7, BP-8 | no stale doc refs after the doc fixes |

## Known-RED allowlist

None — no known-RED e2e scenario remains (per `docs/progress.md`; `explorer-open-searchbox` was
GREened 2026-09-27 and `telescope-implementation`'s Enter-delivery issue was fixed in `7c6569b`).
All Phase 7 fixes are RED-proven by the unit tests above (or build + existing suites for the
no-seam items m23/m24/m25). The verification-agent must NOT flag the deferred e2e scenarios
(`telescope-*`, `neovisual-*`) as regressions — they are QUEUED in `e2e-queue.md`, not run in this
lane.


### Phase 8 - Duplication merges (M21-M30)

# Phase 8 Build Plan — Duplication merges (M21–M30)

> Source: `plans/plan.md` Phase 8 (M21, M22, M23, M24, M25, M26, M28, M29, M30).
> Lane: unit-only, e2e DEFERRED. Verify-with = **unit test names + diagnostic formats ONLY** —
> no e2e scenario is referenced in any Verify-with/Fails-if. Every behavior change is proven by a
> RED unit test (or build + existing suites + the harness `-SelfCheck` seam for M28).
>
> **Baseline counts (verified 2026-09-29):** `tests/Telescope.Tests` = **84**, `tests/NeoVisual.Tests` = **81**
> (AGENTS.md's 77/74 is stale). Phase 8 net effect: Telescope **84 unchanged**, NeoVisual **81 → 79**
> (the −2 drop is the deterministic signal that ONLY the two duplicate `Run_TextInput_Map*` tests were
> removed — see BP-7).
>
> **Known-RED allowlist:** NONE — docs/progress.md records "No known-RED remains" (2026-09-27
> `explorer-open-searchbox` GREened; `neovisual-editor-insert` flake fixed). No Phase 8 scenario/test
> is allowlisted; the verification-agent must NOT treat any Phase 8 unit failure as a pre-existing bug.
>
> **Cross-phase hazards (must not be second-guessed):**
> - **M21 → M31 (Phase 9):** `KeyNames` is created `internal` in the `MyExtension` namespace so
>   Phase 9's `SimpleShortcutMatcher` can share the ONE `KeyNames.ToString(Keys)` (M31 moves
>   `BuildSimpleKey` + `KeyToString` into it — do NOT copy a third `KeyToString`). Also, if Phase 1's
>   `KeyNameBuilder` (m1) exists and calls a `KeyToString`, route it through `KeyNames` too (grep all
>   `KeyToString` call sites in BP-1).
> - **M24 ↔ M29 ↔ M42 (Phase 11):** the NeoVisual.Tests MapMotion test groups are touched by all
>   three. **Consolidation happens ONCE, in BP-7 (M29):** delete `Run_TextInput_MapMotions` +
>   `Run_TextInput_MapInsertMotions` (the duplicates, fully covered by `Run_TextMotionEngine_MapMotion_*`).
>   M42 (Phase 11) must NOT re-delete them — it then only renames 109/117 → `*Classified` + adds the
>   m30 constant. **Count correction:** the plan's M29 "81 → 77 (−4)" is inconsistent with M42's
>   "81 → 79 (−2)" for the SAME two tests; the corrected deterministic signal is **81 → 79 (−2)**.
> - **M24 (BP-4) documented deviation:** the tool-window surface (`TextMotionHelper.MapMotion(Keys, bool)`)
>   is NOT folded into `TryDispatch` (which is WPF-`Key`-based in the Telescope namespace). Reason:
>   (a) NeoVisual.Tests references only MyExtension (no Telescope, no WPF) so it cannot compile against
>   `TryDispatch`; (b) `Keys` (WinForms) vs `Key` (WPF) type boundary; (c) the `$` drift is
>   prompt-vs-preview only, which `TryDispatch` fixes. The shared motion MATH is already
>   `TextMotionNavigator`. The plan's "GetAsyncKeyState for tool-window" consumer is deferred; the
>   union contract (incl. a/A/I insert-placement) is still implemented + tested in `TryDispatch`.
> - **M28 (BP-9) ↔ Phase 10 harness hardening:** BP-9 consolidates helpers + adds `-SelfCheck`; it
>   must NOT fold `neovisual-explorer-toggle` into `Ensure-SolutionExplorerOpen` (that scenario asserts
>   open AND close), and must keep `Open-Telescope` as a thin shim so `docs/spec.md:269` and the 22
>   call sites stay valid. Phase 10's M19/M36/M37/m26-m29 build on top of the consolidated helpers.
> - **M22/M23 doc-ref propagation:** removing `FindFirstSourceFileInItems` (M22) and
>   `OpenReference`/`OpenImplementation` (M23) requires updating `docs/progress.md` (linted by
>   `pwsh tools/check-doc-refs.ps1`) — see BP-2/BP-3. `KeyToString`, `CollectProjectFiles`,
>   `CollectItems`, `Run_TextInput_Map*`, `Open-TelescopeIssues/References/Grep/Implementation` are
>   NOT referenced in any linted doc (verified by grep) — no doc update needed for those.

---

## BP-1 — M21: shared `KeyNames.ToString(Keys)`

- **Files:**
  - CREATE `MyExtension/KeyNames.cs`
  - MODIFY `MyExtension/InputHandler.cs` (delete private `KeyToString` :469-478; `BuildSimpleKey` :451-461 calls `KeyNames.ToString`)
  - MODIFY `MyExtension/LeaderSequenceMatcher.cs` (delete private `KeyToString` :97-106; `HandleKey` :57 calls `KeyNames.ToString`)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (add `Run_KeyNames_*`)
- **Change:** `internal static class KeyNames { public static string ToString(Keys key) }` in namespace `MyExtension` — the exact switch, byte-identical to both current copies: `case Keys.OemQuestion: return "/"; case Keys.Oemplus: return "+"; case Keys.OemMinus: return "-"; default: return key.ToString();`. Both call sites delegate to it. `internal` (not private) so Phase 9's `SimpleShortcutMatcher` (M31) can share it. **Grep every `KeyToString` call site in `MyExtension/`** (incl. any Phase-1 `KeyNameBuilder` from m1) and route all through `KeyNames.ToString` — do NOT leave a third copy.
- **Verify-with:**
  - `Run_KeyNames_PrintableMappings` (NeoVisual.Tests): `KeyNames.ToString(Keys.OemQuestion) == "/"`, `KeyNames.ToString(Keys.Oemplus) == "+"`, `KeyNames.ToString(Keys.OemMinus) == "-"`, `KeyNames.ToString(Keys.F) == "F"`. RED: class doesn't exist → compile error.
  - `Run_KeyNames_RoundTrip_LeaderSequence` (NeoVisual.Tests): bind `"/"` → action, drive Space then `Keys.OemQuestion` through `LeaderSequenceMatcher.HandleKey`, assert `result.Kind == Execute` and `result.Sequence == "/"` — pins the shipped `"/"` default.
  - `Run_KeyNames_RoundTrip_SimpleShortcut` (NeoVisual.Tests): `KeybindingConfig.LoadFromJson("{\"bindings\":{\"Ctrl+/\":\"navigate-left\"}}")` keeps the `"Ctrl+/"` key, and `KeyNames.ToString(Keys.OemQuestion)` produces the `"/"` that makes the `Ctrl+/` config key match.
  - Diagnostic: none changed (no log line touched).
- **Fails-if:** `Run_KeyNames_*` fail to compile or assert wrong mappings; `Run_LeaderMatcher_*` / `Run_Keybinding_*` regress (a missed `KeyToString` call site yields a wrong sequence string); `dotnet build` fails on an unresolved `KeyToString` reference.

## BP-2 — M22: pure `HierarchyWalker` + `ProjectFiles.Enumerate` reuse

- **Files:**
  - CREATE `Telescope/HierarchyWalker.cs` (pure walker + `IHierarchyNode` abstraction)
  - MODIFY `Telescope/ProjectFiles.cs` (adapt DTE → `IHierarchyNode`, delegate to `HierarchyWalker.EnumerateFiles`)
  - MODIFY `Telescope/FileFinder.cs` (`GatherHits` DTE branch calls `ProjectFiles.Enumerate(_dteFactory())` + maps to `FileHit`; delete `CollectProjectFiles`/`CollectItems`)
  - MODIFY `MyExtension/MyExtensionPackage.cs` (`OpenFirstSourceFile` uses `ProjectFiles.Enumerate(dte).FirstOrDefault(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))`; delete `FindFirstSourceFile`/`FindFirstSourceFileInItems`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_HierarchyWalker_*`)
  - MODIFY `docs/progress.md` (line 1365: drop the `FindFirstSourceFileInItems` backticked token — reword the m10 backlog entry, e.g. "m10 — the package's first-`.cs` DTE walker was a third hand-rolled walker; removed in M22 — reuse `ProjectFiles.Enumerate`")
- **Change:** `internal interface IHierarchyNode { IEnumerable<IHierarchyNode> Children; string? Path; }` + `internal static class HierarchyWalker { public static IReadOnlyList<string> EnumerateFiles(IEnumerable<IHierarchyNode> roots); public static string? FirstFileEndingWith(IEnumerable<IHierarchyNode> roots, string extension); }` — depth-first tree order, `HashSet<string>` OrdinalIgnoreCase dedup, `File.Exists` filter, `FirstFileEndingWith` compares `OrdinalIgnoreCase`. `ProjectFiles.Enumerate(dte)` keeps its public signature; internally adapts DTE `Project`/`ProjectItem` → `IHierarchyNode` (solution-folder GUID `{66A26720-8FB5-11D2-AA7E-00C04F688DDE}` → children = `SubProject`s; item → children = `ProjectItems`, `Path` = `Properties.Item("FullPath")?.Value`) and calls `HierarchyWalker.EnumerateFiles`, preserving the per-node try/catch swallow. `FileFinder.GatherHits` DTE branch: `foreach (string path in ProjectFiles.Enumerate(_dteFactory())) hits.Add(new FileHit(path, 0));`. `MyExtensionPackage.OpenFirstSourceFile`: `string? path = ProjectFiles.Enumerate(dte).FirstOrDefault(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));` (tree order preserved → same first `.cs` as the deleted walker).
- **Verify-with:**
  - `Run_HierarchyWalker_*` (Telescope.Tests) RED tests against a fake `IHierarchyNode` tree: solution-folder recursion (SubProject), nested-item recursion, dedup (same path twice → once), `File.Exists` filtering (nonexistent path dropped), `FirstFileEndingWith` returns the first `.cs` in tree order (`.CS` uppercase included — OrdinalIgnoreCase), empty tree → empty list / null. RED: `HierarchyWalker` doesn't exist → compile error.
  - Existing `Run_FileFinder_*` (hermetic ctor path) + `Run_GetCandidates_*` still pass (behavior-preserving).
  - `dotnet build` (FileFinder + package compile after the deletions).
  - Diagnostic: `opened file: {path}` / `open finder=Files candidates={count}` unchanged.
- **Fails-if:** `Run_HierarchyWalker_*` fail (walker logic wrong); `Run_FileFinder_EnumeratesCandidates` / `Run_GetCandidates_*` regress (FileFinder DTE branch broken); `dotnet build` fails (a `CollectProjectFiles`/`FindFirstSourceFileInItems` reference remains); `pwsh tools/check-doc-refs.ps1` exits 1 on `FindFirstSourceFileInItems` (doc update missed).

## BP-3 — M23: `HitOpener.OpenAtLine(IFileLocation, ...)` + `OpenHitAtLine`

- **Files:**
  - CREATE `Telescope/HitOpener.cs`
  - MODIFY `MyExtension/MyExtensionPackage.cs` (replace `OpenReference`/`OpenImplementation` with `OpenHitAtLine(IFileLocation)`; the `ReferencesFinder`/`ImplementationFinder` registration lambdas become `hit => OpenHitAtLine(hit)`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_HitOpener_*`)
  - MODIFY `docs/progress.md` (line 752 `OpenImplementation` → `OpenHitAtLine`; line 805 `OpenReference` → `OpenHitAtLine`)
- **Change:** `internal static class HitOpener { public static void OpenAtLine(IFileLocation hit, Action<string,int> openAtLine) }` in Telescope — `if (hit == null || !File.Exists(hit.FilePath)) return; openAtLine(hit.FilePath, hit.LineNumber);`. `MyExtensionPackage`: `private void OpenHitAtLine(IFileLocation hit) { ThreadHelper.ThrowIfNotOnUIThread(); HitOpener.OpenAtLine(hit, (path, line) => OpenFileAtLine(path, line)); }`. Both `ReferenceHit` and `ImplementationHit` are implicitly convertible to `IFileLocation` (both derive `FileLocation`). Delete `OpenReference`/`OpenImplementation`.
- **Verify-with:**
  - `Run_HitOpener_*` (Telescope.Tests): existing temp file → delegate invoked with `(path, line)`; missing file → delegate NOT invoked; both a `ReferenceHit` and an `ImplementationHit` flow through the same helper (delegate receives the right path/line). RED: `HitOpener` doesn't exist → compile error.
  - `dotnet build` (package compiles after the deletion).
  - Diagnostic: `opened reference: file=... line=... col=... access=read|write` / `opened implementation: file=... line=...` unchanged (emitted by the finders' `OnSelected`, not by the opener).
- **Fails-if:** `Run_HitOpener_*` fail; `dotnet build` fails (a `OpenReference`/`OpenImplementation` reference remains); `pwsh tools/check-doc-refs.ps1` exits 1 on `OpenReference`/`OpenImplementation` (doc update missed).

## BP-4 — M24: shared `TryDispatch(Key, shift, navigator)`

- **Files:**
  - CREATE `Telescope/TryDispatch.cs`
  - MODIFY `Telescope/TelescopeOverlay.cs` (`TryPromptMotion` :566-587 + `HandlePreviewKey` :606-632 delegate to `TryDispatch`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_TryDispatch_*`)
- **Change:** `internal static class TryDispatch { public static bool Handle(Key key, bool shift, TextMotionNavigator nav, out CaretPlacement? insertPlacement) }` in Telescope — the union h/l/j/k/w/b/e/0/$/gg/G + a/A/I: `H→Left`, `L→Right`, `J→Down`, `K→Up`, `W→NextWord`, `B→PrevWord`, `E→EndWord`, `D0→LineStartHome` (0), `D4→LineEnd` ONLY when `shift` ($), `G→Top` (bare) / `Bottom` (shift), `A→InsertAfter` (bare) / `InsertEnd` (shift), `I→InsertStart` (shift) / not handled (bare `i` is the generic insert). Returns true when handled; sets `insertPlacement` (`CaretPlacement.Current/End/Start`) for a/A/I. **`$` drift fix:** `TryDispatch.Handle(Key.D4, shift:false, nav, out _)` returns false and does NOT LineEnd. `TelescopeOverlay.TryPromptMotion(Key key)`: build a fresh navigator, `SetText(_promptBox.Text)`, `MoveTo(_promptBox.CaretIndex)`, `if (!TryDispatch.Handle(key, (Keyboard.Modifiers & ModifierKeys.Shift) != 0, nav, out _)) return false; _promptBox.CaretIndex = nav.Caret; TelescopeLog.Log($"prompt-motion key={key} caret={nav.Caret}"); return true;` — keeps its apply-caret + `prompt-motion key=...` log. `TelescopeOverlay.HandlePreviewKey(Key key)`: `return TryDispatch.Handle(key, (Keyboard.Modifiers & ModifierKeys.Shift) != 0, _previewNavigator, out _);` — the caller keeps its apply-caret + `preview caret=...` log. **Documented deviation:** the tool-window surface (`TextMotionHelper.MapMotion(Keys, bool)`) is NOT folded in (see cross-phase hazards) — it stays as the `Keys`-based adapter; the shared motion MATH is already `TextMotionNavigator`.
- **Verify-with:**
  - `Run_TryDispatch_*` (Telescope.Tests) RED tests: `TryDispatch.Handle(Key.D4, shift:false, nav, out _)` returns false and does NOT LineEnd (fails against current preview semantics — RED); `TryDispatch.Handle(Key.D4, shift:true, nav, out _)` LineEnds; H/L/W/B/E/J/K/D0/G/ShiftG map to the right navigator motions; `A`/`I` set `insertPlacement` (End/Start); bare `I` returns false.
  - Existing `Run_Preview_*` / `Run_PromptMotion_*` (Telescope.Tests) still pass (behavior-preserving for the non-`$` motions).
  - Diagnostic: `prompt-motion key={key} caret={caret}` and `preview caret={caret} line={line}` unchanged (per-surface logs stay in the overlay).
- **Fails-if:** `Run_TryDispatch_*` fail (esp. the `$` contract); `Run_Preview_*` / `Run_PromptMotion_*` regress (a motion mapping changed); `preview caret=...` / `prompt-motion key=...` log lines change format or disappear.

## BP-5 — M25: shared `BlockCaretStyle`

- **Files:**
  - CREATE `Telescope/BlockCaretStyle.cs`
  - MODIFY `Telescope/TelescopeOverlay.cs` (`PromptBlockCaretBrush` :62 + `CreatePromptBlockBrush` :64-71 → `BlockCaretStyle.CreateBlockBrush()`)
  - MODIFY `MyExtension/ToolWindows/TextMotionHelper.cs` (`BlockCaretBrush` :39 + `CreateBlockBrush` :301-308 → `BlockCaretStyle.CreateBlockBrush()`)
  - MODIFY `MyExtension/BlockCaretAdornment.cs` (`Brushes.White` :48 + `Brushes.Black` :51 → `BlockCaretStyle.WhiteFill`/`GlyphColor`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (add `Run_BlockCaretStyle_*`)
- **Change:** `internal static class BlockCaretStyle` in Telescope: `public static readonly Color WhiteFill = Colors.White; public static readonly Color GlyphColor = Colors.Black; public static readonly Rect BlockRect = new Rect(0, 0, 8, 16); public static DrawingBrush CreateBlockBrush()` — returns a single frozen white `DrawingBrush` (`new DrawingBrush(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(BlockRect)))`, `drawing.Freeze()`), cached in a static field so every call returns the SAME instance. `TelescopeOverlay.PromptBlockCaretBrush = BlockCaretStyle.CreateBlockBrush()` (delete `CreatePromptBlockBrush`). `TextMotionHelper.BlockCaretBrush = BlockCaretStyle.CreateBlockBrush()` (delete `CreateBlockBrush`). `BlockCaretAdornment`: `_white.Fill = new SolidColorBrush(BlockCaretStyle.WhiteFill)`; `_glyph.Foreground = new SolidColorBrush(BlockCaretStyle.GlyphColor)`. **Documented deviation:** the plan's "stronger dedup RED (NeoVisual.Tests): `TextMotionHelper.BlockCaretBrush` is reference-equal to the shared instance" is not executable — NeoVisual.Tests cannot reference Telescope. Replaced by a Telescope.Tests single-shared-instance test.
- **Verify-with:**
  - `Run_BlockCaretStyle_*` (Telescope.Tests): `BlockCaretStyle.CreateBlockBrush()` returns a frozen brush (`IsFrozen == true`), fill white, `BlockRect` is 8x16, `GlyphColor == Colors.Black`; two calls return reference-equal instances (single shared static instance). RED: `BlockCaretStyle` doesn't exist → compile error.
  - `dotnet build` (all three consumers compile).
  - Diagnostic: `block-caret active=True|False` unchanged (adornment log untouched).
- **Fails-if:** `Run_BlockCaretStyle_*` fail; `dotnet build` fails (a consumer reference missed); `block-caret active=...` log changes (adornment behavior altered).

## BP-6 — M26: `FocusKeeper.Run` + pure `FocusKeeperSchedule`

- **Files:**
  - CREATE `MyExtension/ToolWindows/FocusKeeper.cs` (both `FocusKeeper` + `FocusKeeperSchedule`)
  - MODIFY `MyExtension/ToolWindows/SolutionExplorerController.cs` (Site 1 `ReturnFocusToTree` :114-143 + Site 2 `SelectFirstSourceFile` :241-262 → `FocusKeeper.Run`)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (add `Run_FocusKeeperSchedule_*`)
- **Change:** `internal static class FocusKeeper { public static void Run(TimeSpan interval, int durationMs, Action<int> tick) }` — owns the `DispatcherTimer` lifecycle: construction at `DispatcherPriority.Normal`, `Interval`, the `keeperRef` closure-capture workaround, `Environment.TickCount + durationMs` stop, `Start()`, per-tick try/catch, `Stop()` when `TickCount >= stopAt`; the tick receives `elapsedMs`. `internal static class FocusKeeperSchedule { public enum Decision { InjectEscape, Reassert, Stop } public static Decision Decide(bool searchBoxFocused, int elapsedMs, int escapeAttempts, int durationMs) }` — pure: `searchBoxFocused && escapeAttempts < 4` → `InjectEscape`; `elapsedMs >= durationMs` → `Stop`; else `Reassert`. Site 1 (`ReturnFocusToTree`): `FocusKeeper.Run(TimeSpan.FromMilliseconds(100), 1500, elapsed => { var d = FocusKeeperSchedule.Decide(TextMotionHelper.FindFocusedTextBox() != null, elapsed, escapeAttempts, 1500); if (d == InjectEscape) { escapeAttempts++; KeyInjection.Press(KeyInjection.VK_ESCAPE); } else if (d == Reassert) { target?.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect); ExecuteCommand("View.SolutionExplorer"); } });` — the divergent Escape-injection tick body. Site 2 (`SelectFirstSourceFile`): `FocusKeeper.Run(TimeSpan.FromMilliseconds(100), 1500, _ => { keepItem.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect); ExecuteCommand("View.SolutionExplorer"); });` — the plain re-assert tick body. Both keep their per-tick try/catch (FocusKeeper.Run wraps the tick).
- **Verify-with:**
  - `Run_FocusKeeperSchedule_*` (NeoVisual.Tests) RED tests: `Decide(true, 0, 0, 1500)` → `InjectEscape`; `Decide(true, 0, 4, 1500)` → `Reassert` (escape bound); `Decide(false, 0, 0, 1500)` → `Reassert`; `Decide(false, 1500, 0, 1500)` → `Stop`; `Decide(true, 1500, 0, 1500)` → `Stop` (elapsed wins). RED: `FocusKeeperSchedule` doesn't exist → compile error.
  - `dotnet build` (SolutionExplorerController compiles).
  - Diagnostic: `solution-explorer select file={path}` / `select none` / `editor-view-opened file={path}` / `solution-explorer search-focus` unchanged.
- **Fails-if:** `Run_FocusKeeperSchedule_*` fail; `dotnet build` fails (a timer reference missed); the `solution-explorer select file=...` / `editor-view-opened file=...` diagnostics stop being emitted (Site 2's re-assert broken).

## BP-7 — M29: `TempDir` + `WithLogPath` in `tests/TestRunner.cs` + call-site replacement + MapMotion duplicate deletion

- **Files:**
  - MODIFY `tests/TestRunner.cs` (add `TempDir` + `WithLogPath` + `WithDebugLogPath`)
  - MODIFY `tests/Telescope.Tests/Program.cs` (replace the 23 temp-dir blocks + 8 LogPath save/restore sites)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (replace temp-dir/save-restore sites; delete `Run_TextInput_MapMotions` + `Run_TextInput_MapInsertMotions`)
- **Change:** in the `TestHarness` namespace (TestRunner.cs): `public sealed class TempDir : IDisposable` — ctor builds `Path.Combine(Path.GetTempPath(), "neovisual_tests_" + Guid.NewGuid().ToString("N"))`, `CreateDirectory` idempotent, `Dispose` deletes recursively (try/catch). `public static void WithLogPath(string logPath, Action body)` — save `LogFileWriter.LogPath`, set, run body, restore in try/finally. `public static void WithDebugLogPath(string debugPath, Action body)` — same for `DebugLogPath` (the one site at Telescope.Tests/Program.cs:113-114). **Helper methods must NOT use the `Run_` prefix** (TestRunner discovers by prefix). Replace the 23 temp-dir blocks (`Run_LogFileWriter_*`, `Run_FileFinder_*`, `Run_Issues_*`, `Run_GrepFinder_*`, `Run_GetCandidates_*`, `Run_TelescopeLog_*`) with `using (var dir = new TempDir()) { ... }` and the 8 LogPath save/restore sites with `WithLogPath(...)`. Delete `Run_TextInput_MapMotions` + `Run_TextInput_MapInsertMotions` (NeoVisual.Tests) — fully covered by `Run_TextMotionEngine_MapMotion_*`. **This is the "consolidate once" for M24/M29/M42** — M42 (Phase 11) must NOT re-delete them.
- **Verify-with:**
  - `dotnet run --project tests/Telescope.Tests` → count unchanged (**84**; behavior-preserving refactor).
  - `dotnet run --project tests/NeoVisual.Tests` → **81 → 79** (the −2 drop is the deterministic signal only the duplicates were removed).
  - `dotnet run --project tests/Telescope.Tests -- LogFileWriter` → all 7 LogFileWriter tests still pass (the `WithLogPath` refactor preserved isolation).
  - Diagnostic: none (test-only).
- **Fails-if:** a test count changes beyond the expected −2 (a test was accidentally deleted/renamed); `Run_LogFileWriter_*` / `Run_FileFinder_*` / `Run_GrepFinder_*` fail after the refactor (a save/restore or temp-dir replacement broke isolation); `dotnet build` fails (a `Run_`-prefixed helper got discovered as a test, or a helper name collides).

## BP-8 — M30: derive Registry telescope entries from `FinderNames`

- **Files:**
  - MODIFY `MyExtension/Actions.cs` (build `Registry` by iterating `TelescopeLauncher.FinderNames`)
  - MODIFY `tests/NeoVisual.Tests/Program.cs` (add set-equality test; strengthen `Run_ActionsRegistry_TelescopeMapsToFinder`)
- **Change:** `Actions.Registry` becomes a static dictionary built by a private `BuildRegistry()`: iterate `TelescopeLauncher.FinderNames` (the single source of truth) and add `[actionName] = (_, l) => () => l.Open(finderName)` for each of the 5 telescope entries; then add the 5 non-telescope entries (`navigate-left/right/up/down` → `h.Navigate(...)`, `toggle-solution-explorer` → `h.ToggleSolutionExplorer()`). `FinderNames` stays in `TelescopeLauncher` (unchanged). This removes the hand-synced 5 telescope entries from `Actions.cs:22-26` — a 6th telescope action added to `FinderNames` is now automatically a Registry entry (no `KeyNotFoundException` in the hook path).
- **Verify-with:**
  - `Run_ActionsRegistry_TelescopeKeysMatchFinderNames` (NeoVisual.Tests): set-equality of `Actions.Registry.Keys.Where(k => k.StartsWith("telescope"))` ↔ `TelescopeLauncher.FinderNames.Keys`. RED: compile-error before the refactor; a 6th telescope action added without a `FinderNames` entry is caught.
  - `Run_ActionsRegistry_ContainsAllBuiltins` (count == 10) + `Run_ActionsRegistry_TelescopeMapsToFinder` (strengthened to set-equality: each telescope action resolves to the finder name in `FinderNames`) still pass.
  - `dotnet build`.
  - Diagnostic: `leader-binding executed: {sequence}` unchanged.
- **Fails-if:** `Run_ActionsRegistry_*` fail (a telescope entry missing/mismatched); `dotnet build` fails (Registry build broken); a telescope action name with no `FinderNames` entry would throw `KeyNotFoundException` in the hook callback — the set-equality test now catches it.

## BP-9 — M28: harness consolidation + `-SelfCheck` seam

- **Files:**
  - MODIFY `tools/test-e2e.ps1` (consolidate `Open-Telescope*` → `Open-TelescopeFinder`; add `Ensure-SolutionExplorerOpen`; add `$script:SeedCanonical`; add `-SelfCheck` switch)
  - MODIFY `tools/harness-common.ps1` (add `Wait-LogLine` core; keep `Wait-NewLogLine`/`Wait-NewLogLineAfter`/`Wait-LogContains` as thin shims)
- **Change:**
  - `Open-TelescopeFinder -Vs -LogPath -Key -Finder` in test-e2e.ps1 — the consolidated 3-attempt loop + focus discipline (hammer Escape, `Space` + the `-Key` sequence, wait for `open finder=<Finder>` + `Focus prompt => True, mode=insert` + `Wait-OverlayForeground` + `Assert-OverlayFocused`). Delete `Open-TelescopeIssues`/`Open-TelescopeReferences`/`Open-TelescopeGrep`/`Open-TelescopeImplementation`; their call sites become `Open-TelescopeFinder -Vs $vs -LogPath $logPath -Key 'F,D' -Finder 'Issues'` (etc.). **Keep `Open-Telescope` as a thin shim** (`-Key 'F,T' -Finder 'Files'`) so the 22 existing call sites + `docs/spec.md:269` stay valid.
  - `Ensure-SolutionExplorerOpen -Vs -LogPath` — the 4-iteration Space+E loop (wait for `solution-explorer toggled open`). Do NOT fold `neovisual-explorer-toggle` into it (that scenario asserts open AND close).
  - `Wait-LogLine -LogPath -Pattern -FromIndex -PollMs -MaxMs` in harness-common.ps1 — the single core wait (searches `$script:LogCache` from `-FromIndex`, polls every `-PollMs`, bounded by `-MaxMs`, never advances the baseline). `Wait-NewLogLine` = `Wait-LogLine -FromIndex $script:LogBaseline -PollMs 300 -MaxMs 20000`; `Wait-NewLogLineAfter` = `-FromIndex $fromIndex -PollMs 200 -MaxMs 3000`; `Wait-LogContains` = `-FromIndex 0 -PollMs 300 -MaxMs 20000` — thin shims preserving each default.
  - `$script:SeedCanonical` hashtable in test-e2e.ps1 — the single canonical seed-content map shared by `Reset-ScratchSolution` (writes) + `Assert-SeedConsistent` (verifies); delete the duplicated inline `$canonical` in `Assert-SeedConsistent` and the inline writes in `Reset-ScratchSolution`.
  - `-SelfCheck` switch (no VS): (1) `Wait-LogLine` against a temp log with `PollMs=1` (write a line, assert found; assert a non-matching pattern times out); (2) `Reset-ScratchSolution` + `Assert-SeedConsistent` on a temp dir (assert it passes, then corrupt a seed file and assert `Assert-SeedConsistent` throws); (3) stub `Send-Tap`/`Bring-ToForeground` and assert the emitted VK sequence per finder (Space F T → `0x20,0x46,0x54`; F D → `0x20,0x46,0x44`; F R → `0x20,0x46,0x52`; F G → `0x20,0x46,0x47`; F I → `0x20,0x46,0x49`).
- **Verify-with:**
  - `pwsh tools/test-e2e.ps1 -SelfCheck` exits 0 (the no-VS seam proves the consolidated helpers).
  - `dotnet build` + existing unit suites (no C# change).
  - Diagnostic (unchanged, asserted by the deferred e2e queue): `open finder={name} candidates={count}`, `Focus prompt => {focused}, mode={mode}, focusedElement={...}`, `solution-explorer toggled open`.
- **Fails-if:** `-SelfCheck` fails (a consolidated helper is broken); a `Wait-NewLogLine`/`Wait-LogContains` shim changes a default (a wait times out); `Open-TelescopeFinder` emits a different `open finder=` wait pattern; `pwsh tools/check-doc-refs.ps1` exits 1 (a removed harness function still referenced in linted docs — `Open-Telescope` is kept as a shim so `docs/spec.md:269` stays valid).

---

## Verification Trace

| failing test / scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_KeyNames_*` (NeoVisual.Tests) | BP-1 | none (pure mapping); `leader-binding executed: /` unchanged |
| `Run_HierarchyWalker_*` (Telescope.Tests) | BP-2 | none (pure walker); `opened file: {path}` unchanged |
| `Run_FileFinder_*` / `Run_GetCandidates_*` (Telescope.Tests) | BP-2 | `opened file: {path}` / `open finder=Files candidates={count}` unchanged |
| `Run_HitOpener_*` (Telescope.Tests) | BP-3 | none (pure opener); `opened reference: file=... line=... col=... access=read\|write` / `opened implementation: file=... line=...` unchanged |
| `Run_TryDispatch_*` (Telescope.Tests) | BP-4 | `prompt-motion key={key} caret={caret}` / `preview caret={caret} line={line}` unchanged |
| `Run_Preview_*` / `Run_PromptMotion_*` (Telescope.Tests) | BP-4 | `preview caret={caret} line={line}` unchanged |
| `Run_BlockCaretStyle_*` (Telescope.Tests) | BP-5 | `block-caret active=True\|False` unchanged |
| `Run_FocusKeeperSchedule_*` (NeoVisual.Tests) | BP-6 | `solution-explorer select file={path}` / `editor-view-opened file={path}` unchanged |
| `-- LogFileWriter` (Telescope.Tests, 7 tests) | BP-7 | none (test-only) |
| Telescope.Tests full suite (count **84**) | BP-7 | none (behavior-preserving) |
| NeoVisual.Tests full suite (**81 → 79**) | BP-7 | none (the −2 drop = only the 2 duplicates removed) |
| `Run_ActionsRegistry_*` (NeoVisual.Tests) | BP-8 | `leader-binding executed: {sequence}` unchanged |
| harness `-SelfCheck` (no VS) | BP-9 | `open finder={name} candidates={count}` / `Focus prompt => {focused}, mode={mode}, focusedElement={...}` / `solution-explorer toggled open` (unchanged) |
| `pwsh tools/check-doc-refs.ps1` (lint gate) | BP-2, BP-3 | n/a (exit 0; `FindFirstSourceFileInItems`/`OpenReference`/`OpenImplementation` tokens removed from docs/progress.md) |

**Known-RED allowlist (carried into this plan):** NONE. docs/progress.md records no known-RED e2e scenario
remaining (`explorer-open-searchbox` GREened 2026-09-27; `neovisual-editor-insert` flake fixed in
`7c6569b`). The verification-agent must NOT flag any Phase 8 unit failure as a pre-existing bug, and
must NOT treat the NeoVisual.Tests 81 → 79 drop as a regression — it is the deterministic M29 signal.

**Deferred e2e (QUEUED, not executed — do not assert):** E2E-M24 (`telescope-prompt-motions` /
`telescope-preview-motions` / `neovisual-textinput-motions` — per-surface log lines unchanged after
`TryDispatch`; the `$` drift fixed), E2E-M28 (`-SelfCheck`/`-SelfTest` seams; `seed-leak` still GREEN),
E2E-M29/M42 (full suite GREEN with consolidated scaffolding, counts 84/79 per the runner).

## KEY DECISIONS (build-agent must not second-guess)

1. **M24 tool-window surface NOT folded into `TryDispatch`** (documented deviation): `TryDispatch` is
   WPF-`Key`-based in the Telescope namespace; NeoVisual.Tests cannot reference Telescope/WPF, and the
   `Keys`/`Key` boundary makes the tool-window fold a rabbit hole. The `$` drift is prompt-vs-preview
   only and is fixed by `TryDispatch`. `TextMotionHelper.MapMotion(Keys, bool)` stays as the tool-window
   adapter; the shared motion MATH is already `TextMotionNavigator`.
2. **MapMotion test consolidation happens ONCE, in BP-7 (M29):** delete `Run_TextInput_MapMotions` +
   `Run_TextInput_MapInsertMotions` (−2). M42 (Phase 11) must NOT re-delete them. The plan's M29 "−4"
   is corrected to **−2** (81 → 79); M42's "81 → 79" is the same deletion.
3. **`KeyNames` is `internal` in `MyExtension`** so Phase 9's `SimpleShortcutMatcher` (M31) shares the
   ONE `KeyNames.ToString(Keys)` — do NOT copy a third `KeyToString`.
4. **`Open-Telescope` is kept as a thin shim** over `Open-TelescopeFinder` so the 22 call sites and
   `docs/spec.md:269` stay valid; only the 4 `Open-TelescopeIssues/References/Grep/Implementation`
   helpers are deleted. `neovisual-explorer-toggle` is NOT folded into `Ensure-SolutionExplorerOpen`.
5. **M25's "reference-equality dedup RED" is replaced** by a Telescope.Tests single-shared-instance
   test (`CreateBlockBrush()` twice → reference-equal) — the NeoVisual.Tests variant is not executable
   (no Telescope reference).
6. **Doc-ref propagation (rule 5a):** M22 removes `FindFirstSourceFileInItems` and M23 removes
   `OpenReference`/`OpenImplementation` — both are backticked in the linted `docs/progress.md` and MUST
   be reworded in BP-2/BP-3 or `pwsh tools/check-doc-refs.ps1` fails. `KeyToString`, `CollectProjectFiles`,
   `CollectItems`, `Run_TextInput_Map*`, `Open-TelescopeIssues/References/Grep/Implementation` are NOT in
   any linted doc (grep-verified) — no doc update for those.


### Phase 9 - Testability seams (M31-M34)

# Phase 9 Build Plan — Testability seams (M31, M32, M33, M34)

**Source:** `plans/plan.md` Phase 9 + Acceptance criteria + Unit test plan + Diagnostics sections.
**Lane:** unit-only, e2e deferred (e2e-queue.md). **Verify-with = unit test names + diagnostic formats ONLY** — no e2e scenario names in Verify-with/Fails-if.
**Scope:** M31 (extract `SimpleShortcutMatcher`), M32 (extract `VimModeClassifier` + `IVimModeSource`), M33 (per-step init diagnostics + named-step orchestration), M34 (extract `FocusTargetModel`).

## Structural evidence (Trailmark 0.5.0, `language="c_sharp"`, proxy-aware)

- `InputHandler.BuildSimpleKey` ← only `InputHandler.HandleKey` (:346); `InputHandler.KeyToString` ← only `BuildSimpleKey`. Both are safe to move into `SimpleShortcutMatcher` (M21/Phase 8 already deleted `KeyToString` in favor of the shared `KeyNames.ToString(Keys)`).
- `VimModeTracker.UpdateTypingFromMode` ← only `OnSwitchedMode` + `SubscribeBuffer` (both internal to `VimModeTracker`) — the classification + `vim-mode=` log is fully internal, so the pure classifier + interop seam can be extracted without touching callers.
- `TelescopeOverlay.FocusTargetUi` ← only `TelescopeOverlay.OnPreviewKeyDown` (:500-560 — the plan's corrected citation). `OnPreviewKeyDown` is a WPF override (no in-repo callers). The focus-target state machine is fully contained in the overlay.
- `MyExtensionPackage.InitializeAsync` is an `AsyncPackage` override (no in-repo callers); its ~10 init steps are all in one try/catch (:116-119).

---

## Phase 1: M31 — SimpleShortcutMatcher

## BP-1 — M31: add `Run_SimpleShortcutMatcher_*` RED tests

- **Files:** `tests/NeoVisual.Tests/Program.cs` (add tests).
- **Change:** Add a `SimpleShortcutMatcher` test section mirroring the `Run_LeaderMatcher_*` group (Tests class, `Run_` prefix). Each test builds `new SimpleShortcutMatcher(bindings)` with a `Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)` and drives `HandleKey(Keys, ctrl, shift, alt)`:
  - `Run_SimpleShortcutMatcher_CtrlHExecutes` — bindings `{"Ctrl+H": () => executed++}`; `HandleKey(Keys.H, true, false, false)` → `SimpleShortcutResultKind.Execute`, `Sequence == "Ctrl+H"`, `executed == 1`.
  - `Run_SimpleShortcutMatcher_NoModifierPassesThrough` — `HandleKey(Keys.A, false, false, false)` → `PassThrough`, `executed == 0`.
  - `Run_SimpleShortcutMatcher_UnboundChordPassesThrough` — no `Ctrl+H` binding; `HandleKey(Keys.H, true, false, false)` → `PassThrough`.
  - `Run_SimpleShortcutMatcher_ModifierOrderShiftF4` — bindings `{"Shift+F4": ...}`; `HandleKey(Keys.F4, false, true, false)` → `Execute`, `Sequence == "Shift+F4"` (modifier order Ctrl,Shift,Alt).
  - `Run_SimpleShortcutMatcher_PrintableKeys` — bindings `{"/": ...}`; `HandleKey(Keys.OemQuestion, false, false, false)` → `Execute`, `Sequence == "/"`; same for `+` (`Keys.Oemplus`) and `-` (`Keys.OemMinus`).
  - `Run_SimpleShortcutMatcher_CaseInsensitiveMatch` — bindings `{"ctrl+h": ...}` (lowercase key); `HandleKey(Keys.H, true, false, false)` → `Execute` (the `OrdinalIgnoreCase` dictionary).
  - `Run_SimpleShortcutMatcher_AltX` — bindings `{"Alt+X": ...}`; `HandleKey(Keys.X, false, false, true)` → `Execute`, `Sequence == "Alt+X"`.
  RED: `SimpleShortcutMatcher`/`SimpleShortcutResult`/`SimpleShortcutResultKind` don't exist → compile error.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- SimpleShortcutMatcher` — RED (compile error) before BP-2, GREEN after.
- **Fails-if:** the tests fail to compile (class missing); a test asserts wrong (modifier order, printable mapping, case-insensitivity, unbound pass-through, no-modifier pass-through).

## BP-2 — M31: create `MyExtension/SimpleShortcutMatcher.cs`

- **Files:** `MyExtension/SimpleShortcutMatcher.cs` (create).
- **Change:** Dependency-free class in `namespace MyExtension` (mirror `LeaderSequenceMatcher`):
  ```csharp
  internal enum SimpleShortcutResultKind { PassThrough, Execute }

  internal readonly struct SimpleShortcutResult
  {
      public SimpleShortcutResultKind Kind { get; }
      public Action? Action { get; }
      public string? Sequence { get; }
      private SimpleShortcutResult(SimpleShortcutResultKind kind, Action? action, string? sequence) { ... }
      public static SimpleShortcutResult PassThrough => new(SimpleShortcutResultKind.PassThrough, null, null);
      public static SimpleShortcutResult Execute(Action action, string sequence) => new(SimpleShortcutResultKind.Execute, action, sequence);
  }

  internal sealed class SimpleShortcutMatcher
  {
      private readonly IReadOnlyDictionary<string, Action> _bindings;
      public SimpleShortcutMatcher(IReadOnlyDictionary<string, Action> bindings) { _bindings = bindings; }
      public SimpleShortcutResult HandleKey(Keys key, bool ctrl, bool shift, bool alt)
      {
          string name = BuildSimpleKey(key, ctrl, shift, alt);
          if (_bindings.TryGetValue(name, out var action)) return SimpleShortcutResult.Execute(action, name);
          return SimpleShortcutResult.PassThrough;
      }
      private static string BuildSimpleKey(Keys key, bool ctrl, bool shift, bool alt)
      {
          var parts = new List<string>();
          if (ctrl) parts.Add("Ctrl");
          if (shift) parts.Add("Shift");
          if (alt) parts.Add("Alt");
          parts.Add(KeyNames.ToString(key));   // shared with LeaderSequenceMatcher (M21/Phase 8) — do NOT copy
          return string.Join("+", parts);
      }
  }
  ```
  `BuildSimpleKey` moves here from `InputHandler` (:451-461) and calls the shared `KeyNames.ToString(Keys)` (created in M21/Phase 8) — do NOT create a third `KeyToString` copy. The exact `"Ctrl+H"` format is a live config-file contract — preserve byte-identical.
- **Verify-with:** `Run_SimpleShortcutMatcher_*` (BP-1) GREEN; `dotnet build`.
- **Fails-if:** `Run_SimpleShortcutMatcher_*` still fail (logic wrong); a third `KeyToString`/printable-mapping copy was added instead of using `KeyNames`; modifier order wrong.

## BP-3 — M31: wire `SimpleShortcutMatcher` into `InputHandler`

- **Files:** `MyExtension/InputHandler.cs` (modify).
- **Change:**
  - Add field `private readonly SimpleShortcutMatcher _simpleMatcher;`.
  - Ctor: after `_leaderMatcher = new LeaderSequenceMatcher(_leaderKey, _leaderBindings);` add `_simpleMatcher = new SimpleShortcutMatcher(_simpleBindings);`.
  - Replace the inline simple-shortcut block (:345-353) with:
    ```csharp
    var simpleResult = _simpleMatcher.HandleKey(key, ctrl, shift, alt);
    if (simpleResult.Kind == SimpleShortcutResultKind.Execute)
    {
        NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}shortcut-binding executed: {simpleResult.Sequence}");
        simpleResult.Action();
        return true;
    }
    return false;
    ```
  - Delete `BuildSimpleKey` (:451-461). (`KeyToString` was already deleted by M21/Phase 8.) The `shortcut-binding executed:` log stays in `InputHandler` — the pure class stays VS-free.
- **Verify-with:** `dotnet build`; `Run_SimpleShortcutMatcher_*` GREEN; existing `Run_Keybinding_*` + `Run_LeaderMatcher_*` GREEN (regression guard). Diagnostic contract UNCHANGED: `[NeoVisual] shortcut-binding executed: {sequence}` (exact literal, `DiagnosticLog.NeoVisual` prefix).
- **Fails-if:** `HandleKey` still calls `BuildSimpleKey` (compile error); the `shortcut-binding executed:` line format changed (sequence token differs); `_simpleBindings` still referenced directly in `HandleKey`; `Run_LeaderMatcher_*` regress (shared `KeyNames` broken).

---

## Phase 2: M32 — VimModeClassifier + IVimModeSource

## BP-4 — M32: add `Run_VimModeClassifier_*` + `Run_VimModeSource_Fake*` RED tests

- **Files:** `tests/NeoVisual.Tests/Program.cs` (add tests).
- **Change:** Add a `VimModeClassifier` test section + a fake-source test section:
  - `Run_VimModeClassifier_InsertIsTyping` — `VimModeClassifier.Classify(2).IsTyping == true`.
  - `Run_VimModeClassifier_ReplaceIsTyping` — `Classify(7).IsTyping == true`.
  - `Run_VimModeClassifier_NormalNotTyping` — `Classify(1).IsTyping == false`.
  - `Run_VimModeClassifier_NullNotTyping` — `Classify(null).IsTyping == false`.
  - `Run_VimModeClassifier_Names` — `Classify(1).Name == "Normal"`, `Classify(2).Name == "Insert"`, `Classify(7).Name == "Replace"`, `Classify(99).Name == "99"`, `Classify(null).Name == "Unknown"` (pins the `vim-mode=` name contract).
  - `Run_VimModeSource_FakeDrivesTypingFlag` — a local `FakeVimModeSource : IVimModeSource` (settable `Mode`; `GetModeKind(ITextView view) => Mode` ignoring the view; `Attach`/`Detach` no-ops; `RaiseModeChanged()` fires `ModeChanged`); `new VimModeTracker(source)`; `Mode = 2; RaiseModeChanged();` → `IsInTypingMode == true`; `Mode = 1; RaiseModeChanged();` → `false`. The fake passes `null!` for the `ITextView` (the fake ignores it).
  - `Run_VimModeSource_FakeLogsVimMode` — with `WithLogPath` (M29/Phase 8) redirecting the log to a temp file: `Mode = 2; RaiseModeChanged();` → the temp log contains `[NeoVisual] vim-mode=Insert`; `Mode = 1; RaiseModeChanged();` → `[NeoVisual] vim-mode=Normal`.
  RED: `VimModeClassifier`/`IVimModeSource` don't exist → compile error.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- VimModeClassifier` and `-- VimModeSource` — RED before BP-5, GREEN after.
- **Fails-if:** the tests fail to compile (classes missing); `Classify` truth table wrong; the fake doesn't flip `IsInTypingMode`; the `vim-mode=` line is missing or has the wrong name token.

## BP-5 — M32: create `VimModeClassifier` + `IVimModeSource` + `VsVimModeSource`

- **Files:** `MyExtension/VimModeClassifier.cs` (create); `MyExtension/VimModeSource.cs` (create).
- **Change:**
  - `VimModeClassifier` (pure static, `namespace MyExtension`):
    ```csharp
    internal static class VimModeClassifier
    {
        public const int Normal = 1;
        public const int Insert = 2;
        public const int Replace = 7;
        public static (bool IsTyping, string Name) Classify(int? mode)
        {
            bool isTyping = mode == Insert || mode == Replace;
            string name = mode switch
            {
                Normal => "Normal", Insert => "Insert", Replace => "Replace",
                _ => mode?.ToString() ?? "Unknown",
            };
            return (isTyping, name);
        }
    }
    ```
    The `Normal=1/Insert=2/Replace=7` constants move here from `VimModeTracker` (:63-65).
  - `IVimModeSource` (interface, `namespace MyExtension`):
    ```csharp
    internal interface IVimModeSource
    {
        int? GetModeKind(ITextView view);
        event Action<int?>? ModeChanged;
        void Attach(ITextView view);
        void Detach(ITextView view);
    }
    ```
  - `VsVimModeSource : IVimModeSource` — the VsVim reflection interop moves here from `VimModeTracker`: `GetVim`/`GetComponentModel`/`GetInterfaceMethod`/`GetBufferForView`/`GetTextBuffer`/`GetTextBufferModeKind`/`BuildSwitchedModeDelegate`/`OnSwitchedMode`/`GetModeKindFromEventArgs`/`SubscribeBuffer`/`UnsubscribeBuffer` + the `_vim`/`_resolved`/`_currentBuffer`/`_currentTextBuffer`/reflection-handle fields + the `VimContractName`/`IVim*FullName` constants. `OnSwitchedMode` keeps the `ReferenceEquals(sender, _currentTextBuffer)` guard and raises `ModeChanged?.Invoke(mode)`. `Attach(view)` = resolve buffer + `SubscribeBuffer`; `Detach(view)` = `UnsubscribeBuffer`. Keep the M17 fix: set `_resolved = true` only on success (do not latch on failure).
- **Verify-with:** `Run_VimModeClassifier_*` + `Run_VimModeSource_Fake*` (BP-4) GREEN; `dotnet build`.
- **Fails-if:** the tests still fail (classifier wrong / seam wrong); the reflection interop is duplicated in `VimModeTracker` instead of moved; the `_resolved` non-latch fix (M17) regressed.

## BP-6 — M32: refactor `VimModeTracker` to a thin adapter

- **Files:** `MyExtension/VimModeTracker.cs` (modify).
- **Change:**
  - Keep the MEF wiring unchanged: `[Export(typeof(IWpfTextViewCreationListener))]`, `[Export(typeof(VimModeTracker))]`, `[ContentType("text")]`, `[TextViewRole(PredefinedTextViewRoles.Editable)]` — `InputHandler.ResolveVimModeTracker` resolves the same singleton via `VsServices.Mef<VimModeTracker>`.
  - Add `private readonly IVimModeSource _source;` + ctor overload `internal VimModeTracker(IVimModeSource source)` (test seam) + default ctor `public VimModeTracker() : this(new VsVimModeSource())`; subscribe `_source.ModeChanged += OnModeChanged;` in the ctor.
  - `TextViewCreated` keeps the `editor-view-opened file=...` log + focus/closed handler attach + `_source.Attach(view)`.
  - `OnViewGotFocus` → `_focusedView = view; _editorFocused = true; UpdateTypingFromMode(_source.GetModeKind(view));`.
  - `OnViewLostFocus`/`OnViewClosed` keep the M17 `ReferenceEquals(_focusedView, view)` guards; `OnViewClosed` also calls `_source.Detach(view)`.
  - `OnModeChanged(int? mode)` → `UpdateTypingFromMode(mode)`.
  - `UpdateTypingFromMode(int? mode)` stays on the tracker (spec.md:171 references it by name) and OWNS the `vim-mode=` log:
    ```csharp
    private void UpdateTypingFromMode(int? mode)
    {
        var (isTyping, name) = VimModeClassifier.Classify(mode);
        _cachedTyping = isTyping;
        Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}vim-mode={name}");
    }
    ```
  - Delete the moved reflection interop + constants + `_vim`/`_resolved`/`_currentBuffer`/`_currentTextBuffer`/reflection-handle fields.
  - **Coordinate with M17 (Phase 6):** M17's `VimModeState` (already in place) delegates its classification to `VimModeClassifier` — do NOT keep a second copy of the truth table; `Run_VimModeState_ClassificationTruthTable` (M17) must stay GREEN.
- **Verify-with:** `dotnet build`; `Run_VimModeClassifier_*` + `Run_VimModeSource_Fake*` GREEN; existing `Run_VimModeState_*` (M17) GREEN. Diagnostic contract UNCHANGED: `[NeoVisual] vim-mode=Insert|Normal|Replace` (exact literal, `DiagnosticLog.NeoVisual` prefix) + `[NeoVisual] editor-view-opened file=...` (from `TextViewCreated`).
- **Fails-if:** the `vim-mode=` line format changed (name token differs); the `editor-view-opened` line is lost; the MEF exports are dropped (tracker no longer a shared MEF part); `IsInTypingMode`/`IsEditorFocused` semantics changed; `Run_VimModeState_*` regress.

---

## Phase 3: M33 — per-step init diagnostics + named-step orchestration

## BP-7 — M33: add `Run_InitSteps_*` RED tests

- **Files:** `tests/NeoVisual.Tests/Program.cs` (add tests).
- **Change:** Add an `InitSteps` test section. Each test builds `new InitSteps(logSink)` where `logSink` collects lines into a `List<string>`, and drives `RunAsync(steps)` with `IReadOnlyList<(string Name, Func<Task> Step)>`:
  - `Run_InitSteps_SuccessLogsOk` — steps `[("telescope", ok), ("hook", ok)]`; sink contains `[MyExtension] init telescope ok` and `[MyExtension] init hook ok`.
  - `Run_InitSteps_FailingStepLogsOwnDiagnosticAndContinues` — steps `[("telescope", ok), ("finders", () => throw new InvalidOperationException("boom")), ("hook", ok)]`; sink contains `[MyExtension] init telescope ok`, `[MyExtension] init finders failed: boom`, `[MyExtension] init hook ok` (later steps still run after a failure).
  - `Run_InitSteps_DiagnosticNamesTheStep` — a failing step named `"window-manager"` logs `[MyExtension] init window-manager failed: ...` (the diagnostic text names the step).
  RED: `InitSteps` doesn't exist → compile error.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- InitSteps` — RED before BP-8, GREEN after.
- **Fails-if:** the tests fail to compile (class missing); a failing step's line is missing or wrong; later steps do NOT run after a failure (orchestration aborts).

## BP-8 — M33: create `MyExtension/InitSteps.cs`

- **Files:** `MyExtension/InitSteps.cs` (create).
- **Change:** Dependency-free orchestrator in `namespace MyExtension`:
  ```csharp
  internal sealed class InitSteps
  {
      private readonly Action<string> _log;
      public InitSteps(Action<string> log) { _log = log; }
      public async Task RunAsync(IReadOnlyList<(string Name, Func<Task> Step)> steps)
      {
          foreach (var (name, step) in steps)
          {
              try { await step(); _log($"{Telescope.DiagnosticLog.MyExtension}init {name} ok"); }
              catch (Exception ex) { _log($"{Telescope.DiagnosticLog.MyExtension}init {name} failed: {ex.Message}"); }
          }
      }
  }
  ```
  No VS/WPF dependency. The `[MyExtension] ` prefix is `Telescope.DiagnosticLog.MyExtension` (a plain string constant). The diagnostic text names the step.
- **Verify-with:** `Run_InitSteps_*` (BP-7) GREEN; `dotnet build`.
- **Fails-if:** the tests still fail (a failure stops later steps, or the line format differs from `[MyExtension] init <step> ok` / `[MyExtension] init <step> failed: {ex.Message}`).

## BP-9 — M33: refactor `MyExtensionPackage.InitializeAsync` to per-step try/catch

- **Files:** `MyExtension/MyExtensionPackage.cs` (modify).
- **Change:**
  - Protect the pre-try steps (:58-61): wrap `ConfigureLogFile()`, `Telescope.NeoVisualLog.Clear()`, `Telescope.NeoVisualLog.InstallDebugListener()` each in its own try/catch logging `[MyExtension] init configure-log ok/failed: ...`, `[MyExtension] init clear-log ok/failed: ...`, `[MyExtension] init install-debug-listener ok/failed: ...` (never throw — logging must not break package load).
  - After `SwitchToMainThreadAsync`, build a named step list and run it through `InitSteps`:
    - `("telescope", ...)` — `_telescope = new TelescopeController(); _launcher = new TelescopeLauncher(this, _telescope);`
    - `("finders", ...)` — the 5 `RegisterFinder` calls.
    - `("monitor-selection", ...)` — `GetServiceAsync<SVsShellMonitorSelection, IVsMonitorSelection>` → local.
    - `("window-manager", ...)` — `_windowManager = new WindowManager(monitorSelection);`
    - `("controllers", ...)` — the per-type `RegisterController` loop.
    - `("shell-wait", ...)` — `WaitForShellInitializedAsync(cancellationToken)`.
    - `("auto-open-solution", ...)` — `TryAutoOpenSolutionAsync(cancellationToken)`.
    - `("hook", ...)` — `_keyboardLogger = new GlobalKeyboardHook(this, _telescope, _windowManager);`
    - `("command", ...)` — `RegisterTelescopeCommandAsync(cancellationToken)`.
    - `await new InitSteps(msg => NeoVisualLog.Log(msg)).RunAsync(steps);`
  - Replace the misleading catch-all `"Failed to initialize keyboard hook"` (:118) with a last-resort net: `NeoVisualLog.Log($"{Telescope.DiagnosticLog.MyExtension}init failed: {ex}")` (keep the outer try/catch as the final net).
  - Keep `[MyExtension] session started` (:61) and the harness-asserted `[MyExtension] shell initialized` / `[MyExtension] auto-opened solution: ...` lines byte-identical.
- **Verify-with:** `dotnet build`; `Run_InitSteps_*` GREEN (the package consumes the same orchestrator). Diagnostic contract ADDED: `[MyExtension] init <step> ok` / `[MyExtension] init <step> failed: {ex.Message}` (exact literal, `DiagnosticLog.MyExtension` prefix) for each of the ~9 named steps + the 3 pre-try steps.
- **Fails-if:** `[MyExtension] session started` / `[MyExtension] shell initialized` / `[MyExtension] auto-opened solution: ...` lines changed (harness-pinned); a step failure still aborts later steps (orchestrator not used); the `"Failed to initialize keyboard hook"` line still present; UI-thread discipline broken (VS calls not on the UI thread after `SwitchToMainThreadAsync`).

---

## Phase 4: M34 — FocusTargetModel

## BP-10 — M34: add `Run_FocusTarget_*` RED tests

- **Files:** `tests/Telescope.Tests/Program.cs` (add tests).
- **Change:** Add a `FocusTargetModel` test section (Tests class, `Run_` prefix):
  - `Run_FocusTarget_StartsWithList` — `new FocusTargetModel().Current == FocusTarget.List`.
  - `Run_FocusTarget_CtrlLMovesToPreview` — `model.Handle(OverlayKey.CtrlL)` → `FocusTargetAction.Handled`, `Current == FocusTarget.Preview`.
  - `Run_FocusTarget_CtrlHReturnsToList` — from Preview, `Handle(OverlayKey.CtrlH)` → `Handled`, `Current == List`.
  - `Run_FocusTarget_EscapeInPreviewReturnsToList` — from Preview, `Handle(OverlayKey.Escape)` → `Handled`, `Current == List`.
  - `Run_FocusTarget_EscapeInListUnchanged` — from List, `Handle(OverlayKey.Escape)` → `None`, `Current == List`.
  - `Run_FocusTarget_ResetOnOpen` — after CtrlL, `Reset()` → `Current == List`.
  RED: `FocusTargetModel`/`FocusTarget`/`FocusTargetAction` + `OverlayKey.CtrlH`/`CtrlL` don't exist → compile error.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FocusTarget` — RED before BP-12, GREEN after.
- **Fails-if:** the tests fail to compile (classes/enum members missing); a transition asserts wrong (CtrlH/L, Escape-in-preview, Escape-in-list, Reset).

## BP-11 — M34: add `CtrlH`/`CtrlL` to `OverlayKey` + map in `MapKey`

- **Files:** `Telescope/OverlayKeyHandler.cs` (modify); `Telescope/TelescopeOverlay.cs` (modify).
- **Change:**
  - `OverlayKey` enum: add `CtrlH` and `CtrlL` members (after `A`).
  - `TelescopeOverlay.MapKey` (:662-679): add
    ```csharp
    case Key.H when (Keyboard.Modifiers & ModifierKeys.Control) != 0: return OverlayKey.CtrlH;
    case Key.L when (Keyboard.Modifiers & ModifierKeys.Control) != 0: return OverlayKey.CtrlL;
    ```
    (`MapKey` already reads `Keyboard.Modifiers` for Shift+G.)
- **Verify-with:** `dotnet build`; existing `Run_KeyHandler_*` + `Run_CaretPlacement_*` (Telescope.Tests) GREEN (enum change is additive — no test asserts the `OverlayKey` member count). No new diagnostic.
- **Fails-if:** `Run_KeyHandler_*` regress (enum change broke a switch); `MapKey` maps Ctrl+H/L to `OverlayKey.Other` (the Ctrl check missing).

## BP-12 — M34: create `Telescope/FocusTargetModel.cs`

- **Files:** `Telescope/FocusTargetModel.cs` (create).
- **Change:** Dependency-free model in `namespace Telescope` (mirror `OverlayKeyHandler`):
  ```csharp
  internal enum FocusTarget { List, Preview }
  internal enum FocusTargetAction { None, Handled }

  internal sealed class FocusTargetModel
  {
      public FocusTarget Current { get; private set; } = FocusTarget.List;
      public void Reset() => Current = FocusTarget.List;
      public FocusTargetAction Handle(OverlayKey key)
      {
          switch (key)
          {
              case OverlayKey.CtrlL: Current = FocusTarget.Preview; return FocusTargetAction.Handled;
              case OverlayKey.CtrlH: Current = FocusTarget.List; return FocusTargetAction.Handled;
              case OverlayKey.Escape when Current == FocusTarget.Preview:
                  Current = FocusTarget.List; return FocusTargetAction.Handled;
              default: return FocusTargetAction.None;
          }
      }
  }
  ```
  The `FocusTarget` enum members `List`/`Preview` produce the exact `ToString()` tokens the `[Telescope] focus target=List|Preview` contract needs.
- **Verify-with:** `Run_FocusTarget_*` (BP-10) GREEN; `dotnet build`.
- **Fails-if:** the tests still fail (model logic wrong — e.g. Escape-in-list handled, CtrlH/L not checked first, Reset not restoring List).

## BP-13 — M34: refactor `TelescopeOverlay` to delegate to the model

- **Files:** `Telescope/TelescopeOverlay.cs` (modify).
- **Change:**
  - Delete the private `FocusTarget` enum (:75) + `_focusTarget` field (:76); add `private readonly FocusTargetModel _focusTargetModel = new();`.
  - `ShowOverlay` (:259): `_focusTarget = FocusTarget.List;` → `_focusTargetModel.Reset();`.
  - `OnPreviewKeyDown` (:500-560): replace the Ctrl+H/L block (:511-519) and the Escape-in-preview block (:524-531) with a single delegation at the top:
    ```csharp
    var focusAction = _focusTargetModel.Handle(MapKey(e.Key));
    if (focusAction != FocusTargetAction.None)
    {
        e.Handled = true;
        TelescopeLog.Log($"focus target={_focusTargetModel.Current}");
        FocusTargetUi();
        return;
    }
    ```
    Then the preview branch checks `_focusTargetModel.Current == FocusTarget.Preview` (instead of `_focusTarget`); the list branch is unchanged.
  - `FocusTargetUi` (:649-660): read `_focusTargetModel.Current` instead of `_focusTarget`.
  - The `focus target={...}` log emits `model.Current.ToString()` = `List`/`Preview` — byte-identical to the current contract.
- **Verify-with:** `dotnet build`; `Run_FocusTarget_*` GREEN; existing `Run_KeyHandler_*` + `Run_Preview_*` GREEN (regression guard). Diagnostic contract UNCHANGED: `[Telescope] focus target=List|Preview` (exact literal; `TelescopeLog` adds the `[Telescope] ` prefix).
- **Fails-if:** the `focus target=` token differs from `List`/`Preview` (e.g. lowercase or a different enum name); Ctrl+H/L no longer switch focus; Escape-in-preview no longer returns to list; `Run_KeyHandler_*`/`Run_Preview_*` regress.

---

## Verification Trace

| failing test / gate | implicated steps | expected diagnostic |
|---|---|---|
| `Run_SimpleShortcutMatcher_CtrlHExecutes` | BP-1, BP-2, BP-3 | `[NeoVisual] shortcut-binding executed: Ctrl+H` (unchanged) |
| `Run_SimpleShortcutMatcher_NoModifierPassesThrough` | BP-1, BP-2, BP-3 | none (pass-through) |
| `Run_SimpleShortcutMatcher_UnboundChordPassesThrough` | BP-1, BP-2, BP-3 | none |
| `Run_SimpleShortcutMatcher_ModifierOrderShiftF4` | BP-1, BP-2, BP-3 | `[NeoVisual] shortcut-binding executed: Shift+F4` |
| `Run_SimpleShortcutMatcher_PrintableKeys` | BP-1, BP-2, BP-3 | `[NeoVisual] shortcut-binding executed: /` |
| `Run_SimpleShortcutMatcher_CaseInsensitiveMatch` | BP-1, BP-2, BP-3 | `[NeoVisual] shortcut-binding executed: Ctrl+H` |
| `Run_SimpleShortcutMatcher_AltX` | BP-1, BP-2, BP-3 | `[NeoVisual] shortcut-binding executed: Alt+X` |
| `Run_VimModeClassifier_InsertIsTyping` | BP-4, BP-5, BP-6 | `[NeoVisual] vim-mode=Insert` (unchanged) |
| `Run_VimModeClassifier_ReplaceIsTyping` | BP-4, BP-5, BP-6 | `[NeoVisual] vim-mode=Replace` |
| `Run_VimModeClassifier_NormalNotTyping` | BP-4, BP-5, BP-6 | `[NeoVisual] vim-mode=Normal` |
| `Run_VimModeClassifier_NullNotTyping` | BP-4, BP-5, BP-6 | `[NeoVisual] vim-mode=Unknown` |
| `Run_VimModeClassifier_Names` | BP-4, BP-5, BP-6 | `[NeoVisual] vim-mode=Insert\|Normal\|Replace` |
| `Run_VimModeSource_FakeDrivesTypingFlag` | BP-4, BP-5, BP-6 | none (flag flip) |
| `Run_VimModeSource_FakeLogsVimMode` | BP-4, BP-5, BP-6 | `[NeoVisual] vim-mode=Insert` / `vim-mode=Normal` |
| `Run_InitSteps_SuccessLogsOk` | BP-7, BP-8, BP-9 | `[MyExtension] init <step> ok` (ADD) |
| `Run_InitSteps_FailingStepLogsOwnDiagnosticAndContinues` | BP-7, BP-8, BP-9 | `[MyExtension] init <step> failed: {ex.Message}` (ADD) |
| `Run_InitSteps_DiagnosticNamesTheStep` | BP-7, BP-8, BP-9 | `[MyExtension] init window-manager failed: ...` |
| `Run_FocusTarget_StartsWithList` | BP-10, BP-12 | `[Telescope] focus target=List` (unchanged) |
| `Run_FocusTarget_CtrlLMovesToPreview` | BP-10, BP-12, BP-13 | `[Telescope] focus target=Preview` |
| `Run_FocusTarget_CtrlHReturnsToList` | BP-10, BP-12, BP-13 | `[Telescope] focus target=List` |
| `Run_FocusTarget_EscapeInPreviewReturnsToList` | BP-10, BP-12, BP-13 | `[Telescope] focus target=List` |
| `Run_FocusTarget_EscapeInListUnchanged` | BP-10, BP-12, BP-13 | none |
| `Run_FocusTarget_ResetOnOpen` | BP-10, BP-12, BP-13 | `[Telescope] focus target=List` |
| existing `Run_LeaderMatcher_*` (M21 guard) | BP-3 | `[NeoVisual] leader-binding executed: ...` unchanged |
| existing `Run_Keybinding_*` (M31 guard) | BP-3 | `[NeoVisual] shortcut-binding executed: ...` unchanged |
| existing `Run_VimModeState_*` (M17 guard) | BP-5, BP-6 | `[NeoVisual] vim-mode=...` unchanged |
| existing `Run_KeyHandler_*` / `Run_Preview_*` (M34 guard) | BP-11, BP-13 | `[Telescope] focus target=List\|Preview` unchanged |
| `dotnet build` + full suites | all | no compile errors |

**Known-RED allowlist:** none — no known-RED e2e scenario remains (per docs/progress.md). The deferred e2e scenarios E2E-M31/M32/M34 (e2e-queue.md) are NOT part of this phase's gate; they are QUEUED until the plan is GREEN and a VS-capable machine is available.

## Cross-phase hazards (for the hub / later phases)

- **M31 ↔ M21 (Phase 8):** `SimpleShortcutMatcher` MUST use the shared `KeyNames.ToString(Keys)` (created in M21) — do NOT create a third `KeyToString`/printable-mapping copy. `InputHandler.KeyToString` was already deleted by M21; only `BuildSimpleKey` remains to move. The `"Ctrl+H"` format is a live config-file contract — preserve byte-identical.
- **M32 ↔ M17 (Phase 6):** `VimModeClassifier` is the same-file extraction as M17's `VimModeState` — the classification truth table + names must live in ONE place (`VimModeClassifier`); `VimModeState`/`VimModeTracker` delegate to it. Sequence once — do NOT create a second truth table. `Run_VimModeState_ClassificationTruthTable` (M17) pins the same contract and must stay GREEN.
- **M32 ↔ spec.md:171:** `[NeoVisual] vim-mode=Insert|Normal|Replace` is documented as "(from `VimModeTracker.UpdateTypingFromMode`)" — keep the method name `UpdateTypingFromMode` on the tracker (it owns the `vim-mode=` log). If the build-agent renames it, add a doc-update step + run `tools/check-doc-refs.ps1` (doc-ref lint is the acceptance gate).
- **M34 ↔ `[Telescope] focus target=List|Preview` contract:** the `FocusTarget` enum members must be exactly `List`/`Preview` so `model.Current.ToString()` emits the byte-identical token. The `focus target=` log stays in the overlay (WPF), NOT in the pure model.
- **M33 ↔ harness-pinned `[MyExtension]` lines:** `[MyExtension] session started`, `[MyExtension] shell initialized`, `[MyExtension] auto-opened solution: ...` must stay byte-identical; only the catch-all `"Failed to initialize keyboard hook"` line is replaced.
- **M32 ↔ MEF:** `VimModeTracker` keeps its `[Export]`/`[ContentType("text")]`/`[TextViewRole]` attributes — `InputHandler.ResolveVimModeTracker` resolves the same singleton via `VsServices.Mef<VimModeTracker>`; the `IVimModeSource` fake passes `null!` for `ITextView` (NeoVisual.Tests resolves `ITextView` via `Microsoft.VisualStudio.Shell.Framework`).


### Phase 10 - Harness hardening (M19, M36, M37, m26-m29)

# Build Plan — Phase 10: Harness hardening (M19, M36, M37, m26, m27, m28, m29)

> **Lane:** feature/bugfix program (unit-only, e2e deferred). Phase 10 is **harness-only**
> (PowerShell under `tools/`). No C# product code, no MEF/DI wiring, no net472/UI-thread
> concerns. Every step is verified by a **self-check / `-SelfTest` / `-SelfCheck` seam** and a
> **diagnostic format** — there are NO C# unit tests and NO e2e scenarios in this phase's
> Verify-with/Fails-if (e2e is deferred to `e2e-queue.md`).
>
> Source: `plans/plan.md` Phase 10 (lines 567-618) + `docs/code-review.md` M19/M36/M37/m26-m29.
> Corrected citations used verbatim (do not re-derive line numbers).

## Known-RED allowlist (carry into Verification Trace)

- **e2e:** None — no known-RED e2e scenario remains (per `docs/progress.md`).
- **Phase-10-specific (check-doc-refs.ps1):** the lint is **already RED before Phase 10** with
  **7 pre-existing unresolved references** (verified 2026-09-29): `FzfFinder` (progress.md),
  `FocusKeeper` ×2 (progress.md), `SafeHandle` ×2 (code-review-hub.md / code-review-worker.md),
  and 2 template paths (`.opencode/workspaces/code-review-hub/sessions/<session-id>/`,
  `.opencode/workspaces/neovim-planning-hub/sessions/<session-id>/`). These are NOT Phase 10
  regressions (they are proposed-symbol / allowlist gaps owned by other phases). Phase 10 must
  **not add new issues** and must **not be blamed for these 7**. The verification-agent must
  compare the real-doc-set issue count against the Phase-10-start baseline, not against 0.

---

## Phase 10A — M19: dte-command.ps1 COM-call timeout

Fix-direction correction (from the plan): `Assert-Budget` is a checkpoint stopwatch and CANNOT
interrupt a blocking COM call — the fix is a **background runspace with a bounded wait**.

### BP-1 — Add `-TimeoutSec` param + `Invoke-DteWithTimeout` runspace wrapper

- **Files:** `tools/dte-command.ps1` (modify).
- **Change:** Add `[int]$TimeoutSec = 90` to the `param()` block (default 60-120s; `Debug.Start`
  legitimately takes long). Add a helper `function Invoke-DteWithTimeout([scriptblock]$Block,
  [int]$TimeoutSec, [string]$What)` that runs `$Block` in a **background runspace**:
  `[powershell]::Create()` + `AddScript($Block.ToString())` + `BeginInvoke()` +
  `AsyncWaitHandle.WaitOne($TimeoutSec * 1000)`. On timeout: `$ps.Stop()` + `throw "dte-command
  timed out after $TimeoutSec s: $What"` (with `$ErrorActionPreference='Stop'` this terminates
  the script with a **non-zero exit** so the parent's teardown runs). On completion: emit the
  runspace's output and surface any terminating error. `$ps.Dispose()` in a `finally`.
- **Verify-with:** `-SelfTest` (BP-3) exercises the wrapper against a blocking stub and asserts
  the abort + diagnostic. Diagnostic format contract:
  `dte-command timed out after <n> s: <what>`.
- **Fails-if:** the helper is missing (parse error); the runspace never aborts (WaitOne returns
  false but no throw); the timeout diagnostic never appears; exit code is 0 on timeout.

### BP-2 — Wrap the three COM call sites in `Invoke-DteWithTimeout`

- **Files:** `tools/dte-command.ps1` (modify).
- **Change:** Wrap each COM call in the runspace wrapper:
  - `GetActiveDocument` branch (`:80` `$dte.ActiveDocument`) — **short per-call timeout ~5s**
    (add `[int]$QueryTimeoutSec = 5` param; the harness polls this in a loop, so a hung call must
    fail fast). `What = "GetActiveDocument"`.
  - `Solution.Open` (`:89` `$dte.Solution.Open($Arg)`) — default `-TimeoutSec`.
    `What = "Solution.Open '$Arg'"`.
  - `ExecuteCommand` (`:92-93`) — default `-TimeoutSec`. `What = "ExecuteCommand '$Command'"`.
  - The scriptblock passed to the wrapper must receive `$dte`/`$Command`/`$Arg` via
    `AddParameter` (a fresh runspace has no caller scope).
- **Verify-with:** `-SelfTest` (BP-3) proves both the default-timeout path and the short
  `GetActiveDocument` path abort a blocking stub. Diagnostic format:
  `dte-command timed out after <n> s: <what>`.
- **Fails-if:** any COM call still runs unbounded (no wrapper); the `GetActiveDocument` branch
  uses the long default instead of ~5s; a timeout exits 0 instead of non-zero.

### BP-3 — Add `-SelfTest` switch + blocking-stub self-check

- **Files:** `tools/dte-command.ps1` (modify).
- **Change:** Add `[switch]$SelfTest`. When set, run the wrapper against a deliberately-blocking
  stub (`Start-Sleep -Seconds 30` in the runspace) with a ~2s timeout and assert it aborts at
  ~2s with the timeout diagnostic and a non-zero exit; also exercise the short-timeout path (a
  5s stub with a 1s timeout aborts). Print `SELFTEST PASS` + `exit 0` on success,
  `SELFTEST FAIL` + `exit 1` on failure. Must not touch VS/DTE.
- **Verify-with:** `pwsh tools/dte-command.ps1 -SelfTest` → exit 0 + `SELFTEST PASS`; the
  timeout diagnostic `dte-command timed out after <n> s: <what>` appears in the output.
- **Fails-if:** `-SelfTest` exits 0 without exercising the wrapper; the blocking stub is not
  aborted; no timeout diagnostic; `SELFTEST FAIL`.

**Phase 10A mid-point verify:** `pwsh tools/dte-command.ps1 -SelfTest` → exit 0 + `SELFTEST PASS`.

---

## Phase 10B — M36 + m29: iterate-telescope.ps1 PID-scoped kills, Process env, always-reset

### BP-4 — Port the M-M5 helpers to iterate-telescope.ps1

- **Files:** `tools/iterate-telescope.ps1` (modify).
- **Change:** Port from `tools/test-e2e.ps1:89-140`: `$script:SpawnedVsPids` (a
  `List[int]`), `Add-SpawnedVs`, `Save-AllDocuments` (calls
  `dte-command.ps1 -Command 'File.SaveAll'` — now timeout-safe per BP-1/BP-2), `Stop-SpawnedVs`
  (kills ONLY the listed PIDs, `Save-AllDocuments` first), `Stop-HarnessVs` (title-scoped
  cleanup: only devenv whose `MainWindowTitle` matches `'MyExtension'` or `'Experimental'`).
  Extract the kill into a shared function taking **explicit PIDs** (e.g.
  `Stop-VsPids([int[]]$pids)`) so the self-check can drive it directly.
- **Verify-with:** `-SelfCheck` (BP-7) drives the kill function with explicit PIDs. The script
  dot-sources cleanly (no parse error).
- **Fails-if:** the script fails to parse (function-name typo); `Stop-SpawnedVs` /
  `Stop-HarnessVs` / `Stop-VsPids` missing; `Save-AllDocuments` still calls a non-timeout-safe
  dte-command.

### BP-5 — Replace blanket kills with `Stop-HarnessVs` + PID tracking; wire `Stop-SpawnedVs` into both exit paths

- **Files:** `tools/iterate-telescope.ps1` (modify).
- **Change:**
  - `:112` blanket `Get-Process devenv | Stop-Process -Force` → `Stop-HarnessVs`.
  - `:129` exp-instance kill → `Stop-HarnessVs` (title-scoped; never a blanket kill).
  - After `$mainVs` is found (`:125`) and `$vsProc` is found (`:151`), call `Add-SpawnedVs`.
  - `:263` (FAIL exit) and `:271` (PASS exit) `Get-Process devenv | Stop-Process -Force` →
    `Stop-SpawnedVs`.
- **Verify-with:** `-SelfCheck` (BP-7) proves the kill only touches listed PIDs. Grep-verifiable:
  no `Get-Process devenv` + `Stop-Process` blanket remains; `Stop-SpawnedVs` is called on both
  exit paths.
- **Fails-if:** any blanket `Get-Process devenv | Stop-Process` remains; `Stop-SpawnedVs` not
  called on one of the two exit paths; a spawned PID is not tracked.

### BP-6 — Change env scope to Process

- **Files:** `tools/iterate-telescope.ps1` (modify).
- **Change:** `:104`
  `[Environment]::SetEnvironmentVariable('NEOVISUAL_TEST_SOLUTION', $slnPath, 'User')` →
  **Process scope only** (drop the `'User'` target; keep `$env:NEOVISUAL_TEST_SOLUTION = $slnPath`
  so the spawned devenv inherits it). `:106` `NEOVISUAL_LOG_DIR` **may stay User-scoped** as a
  documented, opt-in decision — add a comment stating it is intentionally User-scoped (the user's
  own F5 runs keep writing to the same log folder) while `NEOVISUAL_TEST_SOLUTION` is Process-only.
- **Verify-with:** `-SelfCheck` (BP-7) asserts
  `[Environment]::GetEnvironmentVariable('NEOVISUAL_TEST_SOLUTION','User')` is unchanged (null)
  before and after the run.
- **Fails-if:** `NEOVISUAL_TEST_SOLUTION` is still written to User scope; the User-scope value
  persists after the run.

### BP-7 — Add `-SelfCheck` switch (kill scope + env scope)

- **Files:** `tools/iterate-telescope.ps1` (modify).
- **Change:** Add `[switch]$SelfCheck`. When set (no VS boot):
  1. Spawn two dummy processes (`Start-Process pwsh -ArgumentList '-NoProfile','-Command',
     'Start-Sleep -Seconds 30'`), add ONE PID to `$script:SpawnedVsPids`, run `Stop-SpawnedVs`,
     assert **exactly one** died (the listed one) and the other is still alive (then kill it).
  2. Assert `NEOVISUAL_TEST_SOLUTION` User-scope is unchanged (null) before and after.
  Print `SELFTEST PASS` + `exit 0` on success, `SELFTEST FAIL` + `exit 1` on failure.
- **Verify-with:** `pwsh tools/iterate-telescope.ps1 -SelfCheck` → exit 0 + `SELFTEST PASS`.
- **Fails-if:** both dummy processes die (blanket kill leaked); neither dies; the User-scope env
  var changed; `SELFTEST FAIL`.

### BP-8 — m29: always reset the scratch solution + extend `-SelfCheck` with seed determinism

- **Files:** `tools/iterate-telescope.ps1` (modify).
- **Change:** Replace the `if (-not (Test-Path $slnPath)) { ... }` seeding gate (`:78-87`) with an
  **always-reset** (mirror `Reset-ScratchSolution`, `tools/test-e2e.ps1:461`): delete `$scratch`
  if present, recreate, `dotnet new console -n Probe`, `dotnet new sln --format sln`,
  `dotnet sln add`, `dotnet build`. A stale scratch from a prior run must never be reused.
  Extend the `-SelfCheck` switch (BP-7) with a seed-determinism check: run the seeding twice and
  assert the second run is deterministic (same file set + same canonical content).
- **Verify-with:** `pwsh tools/iterate-telescope.ps1 -SelfCheck` → exit 0 + `SELFTEST PASS`
  (includes the run-twice determinism assertion).
- **Fails-if:** a stale edit survives into the second run (seeding still gated on `Test-Path`);
  the second seeding run throws; `SELFTEST FAIL`.

**Phase 10B mid-point verify:** `pwsh tools/iterate-telescope.ps1 -SelfCheck` → exit 0 + `SELFTEST PASS`.

---

## Phase 10C — M37 + m26/m28: test-e2e.ps1 budget + cache-index discipline

### BP-9 — Add a standalone `Assert-Budget` to harness-common.ps1

- **Files:** `tools/harness-common.ps1` (modify).
- **Change:** Add `function Assert-Budget([System.Diagnostics.Stopwatch]$Stopwatch, [int]
  $TimeoutSec)` that throws `"Timed out after $TimeoutSec s"` when
  `$Stopwatch.Elapsed.TotalSeconds -gt $TimeoutSec` (mirror `iterate-telescope.ps1:56`, but
  parameterized so it is directly unit-testable). NOTE: `iterate-telescope.ps1:56` keeps its
  local no-arg `Assert-Budget` (it shadows the shared one — harmless; M28 Phase 8 consolidates).
- **Verify-with:** `-SelfCheck` (BP-11) unit-tests it directly: fresh stopwatch → no throw;
  advanced past budget → throws `Timed out after <n> s`.
- **Fails-if:** the shared function is missing; it throws on a fresh stopwatch; it does not throw
  past the budget; the throw message differs from `Timed out after <n> s`.

### BP-10 — Enforce the budget in the scenario loop (BEFORE the try) + outer TIMEOUT catch

- **Files:** `tools/test-e2e.ps1` (modify).
- **Change:** Before the scenario loop (`:1900`), start `$script:BudgetStopwatch =
  [System.Diagnostics.Stopwatch]::StartNew()`. At the **TOP of each `foreach ($name in
  $selected)` iteration, BEFORE the `try`** (`:1903`), call
  `Assert-Budget -Stopwatch $script:BudgetStopwatch -TimeoutSec $TimeoutSec`. **Must be before
  the `try`** — inside the try the throw would be swallowed into `$failures` and degrade to a
  per-scenario FAIL instead of a suite timeout. Optionally wrap the loop in an outer try/catch
  that prints `RESULT: TIMEOUT` + `exit 1`.
- **Verify-with:** `-SelfCheck` (BP-11) proves the function; the placement is grep-verifiable
  (`Assert-Budget` appears before `try` in the loop body). Diagnostic format:
  `RESULT: TIMEOUT` (outer catch) / `Timed out after <n> s` (throw).
- **Fails-if:** `Assert-Budget` is called inside the `try` (swallowed into `$failures`); the
  stopwatch is never started; `$TimeoutSec` is still declared-but-unenforced.

### BP-11 — Extend the `-SelfCheck` switch with the Assert-Budget checks

- **Files:** `tools/test-e2e.ps1` (modify).
- **Change:** Extend the `-SelfCheck` switch (introduced by **M28 Phase 8**; if it does not yet
  exist when Phase 10 executes, create it) with: fresh stopwatch → `Assert-Budget` does not
  throw; a stopwatch advanced past the budget → `Assert-Budget` throws with the expected message
  `Timed out after <n> s`. Print `SELFTEST PASS` + `exit 0` on success, `SELFTEST FAIL` +
  `exit 1` on failure. Must not boot VS.
- **Verify-with:** `pwsh tools/test-e2e.ps1 -SelfCheck` → exit 0 + `SELFTEST PASS`.
- **Fails-if:** the Assert-Budget checks are absent from `-SelfCheck`; a fresh stopwatch throws;
  an over-budget stopwatch does not throw; `SELFTEST FAIL`.

### BP-12 — m26/m28: cache-index discipline (snapshot + absence scan both cache-side)

- **Files:** `tools/harness-common.ps1` (modify), `tools/test-e2e.ps1` (modify).
- **Change:** Add `function Get-LogCacheIndex([string]$logPath)` to harness-common.ps1:
  `Update-LogCache $logPath; return $script:LogCache.Count` (a cache-side snapshot). In
  test-e2e.ps1 replace the three mixed cache/file sites:
  - `:831` and `:938` `$preKey = if (Test-Path $logPath) { (Get-Content $logPath).Count } else
    { 0 }` → `$preKey = Get-LogCacheIndex $logPath` (the value is passed to
    `Wait-NewLogLineAfter` as a **cache offset**; a fresh `Get-Content` count can exceed the
    cache and make the wait time out, or lag it and match stale lines).
  - `:1027-1032` editor-focus absence scan: replace `$lines = Get-Content $logPath` +
    `for ($i = $script:LogBaseline; $i -lt $lines.Count; $i++) { if ($lines[$i] -match ...) }`
    with a **both-cache-side** scan: `Update-LogCache $logPath; for ($i = $script:LogBaseline;
    $i -lt $script:LogCache.Count; $i++) { if ($script:LogCache[$i] -match ...) }`.
- **Verify-with:** `-SelfCheck` (BP-13) proves `Get-LogCacheIndex` tracks the cache. Grep-verifiable:
  no `(Get-Content $logPath).Count` remains at the three sites; the absence scan indexes
  `$script:LogCache`, not a fresh array.
- **Fails-if:** `Get-LogCacheIndex` missing; any of the three sites still mixes a fresh
  `Get-Content` count/array with a cache index; the absence scan can silently pass on truncation.

### BP-13 — Extend `-SelfCheck` with the cache-index consistency check

- **Files:** `tools/test-e2e.ps1` (modify).
- **Change:** Extend the `-SelfCheck` switch (same switch as BP-11) with: write a temp log,
  `Update-LogCache`, assert `Get-LogCacheIndex` == `$script:LogCache.Count`; append a line,
  assert `Get-LogCacheIndex` increments by 1; truncate the file, assert the cache restarts from
  0 (the `Update-LogCache` truncation path). Print `SELFTEST PASS` + `exit 0` on success,
  `SELFTEST FAIL` + `exit 1` on failure.
- **Verify-with:** `pwsh tools/test-e2e.ps1 -SelfCheck` → exit 0 + `SELFTEST PASS`.
- **Fails-if:** `Get-LogCacheIndex` disagrees with the cache; the append does not increment the
  index; the truncation does not reset the cache; `SELFTEST FAIL`.

**Phase 10C mid-point verify:** `pwsh tools/test-e2e.ps1 -SelfCheck` → exit 0 + `SELFTEST PASS`.

---

## Phase 10D — m27: check-doc-refs.ps1 lint fixes

### BP-14 — Fix `Test-ToolFunctionExists` to match the function definition, not whole-file text

- **Files:** `tools/check-doc-refs.ps1` (modify).
- **Change:** In `Test-ToolFunctionExists` (`:171-179`), replace the whole-file regex
  `"\b$([regex]::Escape($token))\b"` with a **definition-anchored** regex
  `"(?m)^function\s+$([regex]::Escape($token))\b"` — **must use `(?m)` (Multiline)** so `^`
  anchors at line starts (verified: with Multiline, every Verb-Noun doc ref in the durable doc
  set resolves via `^function\s+<token>`; without it, `^` anchors at string start and nothing
  matches). Keep the `Get-Command` built-in-cmdlet check first.
- **Verify-with:** `-SelfCheck` (BP-16) runs the lint against a temp doc referencing a deleted
  function → exit 1 + `no such PowerShell function/cmdlet in tools/`. Regression: the real doc
  set reports **no new issues** vs the Phase-10-start baseline (see BP-16).
- **Fails-if:** the regex is not Multiline (nothing resolves); a doc referencing a deleted
  function still passes (false negative); the real doc set gains new issues.

### BP-15 — Emit a warning for missing docs instead of silently skipping

- **Files:** `tools/check-doc-refs.ps1` (modify).
- **Change:** In the doc loop (`:199-201`), replace the silent `if (-not (Test-Path -LiteralPath
  $docPath)) { continue }` with a **warning emission** (non-fatal): add the missing doc to a
  `$warnings` list and print `WARNING: <docRel> — doc file not found (skipped)` to stderr. Exit
  code stays 0 when only warnings exist (the `:201` comment "doc may legitimately not exist yet"
  is preserved — a missing doc is tolerated, just no longer silent).
- **Verify-with:** `-SelfCheck` (BP-16) runs with a nonexistent doc path and asserts the warning
  line appears + exit 0.
- **Fails-if:** a missing doc is still silently skipped (no warning); the warning makes the lint
  exit non-zero (it must stay non-fatal); the warning format differs from
  `WARNING: <docRel> — doc file not found (skipped)`.

### BP-16 — Add `-SelfCheck` switch + real-doc-set regression command

- **Files:** `tools/check-doc-refs.ps1` (modify).
- **Change:** Add `[switch]$SelfCheck`. When set:
  1. Create a temp doc referencing a deleted function (e.g. `` `Some-DeletedFunction` ``), run
     the lint against it (`-Docs <temp>`), assert exit 1 + the issue line
     `no such PowerShell function/cmdlet in tools/`.
  2. Run with a nonexistent doc path, assert the warning line
     `WARNING: <doc> — doc file not found (skipped)` appears + exit 0.
  Print `SELFTEST PASS` + `exit 0` on success, `SELFTEST FAIL` + `exit 1` on failure.
  **Regression (manual Verify-with command, not part of `-SelfCheck`):** capture the real-doc-set
  issue count BEFORE the m27 changes (`pwsh tools/check-doc-refs.ps1` → `DOC-REF DRIFT (n
  unresolved)`), apply BP-14/BP-15, re-run, and assert the count is **unchanged** (the pre-existing
  7 — see Known-RED allowlist — may be fewer if Phase 8 resolved `FocusKeeper`; the assertion is
  "no NEW issues", not "0 issues").
- **Verify-with:** `pwsh tools/check-doc-refs.ps1 -SelfCheck` → exit 0 + `SELFTEST PASS`;
  regression command → `DOC-REF DRIFT (n unresolved)` with `n` unchanged from the Phase-10-start
  baseline.
- **Fails-if:** `-SelfCheck` does not exercise the deleted-function case; the deleted-function
  doc does not fail; the missing-doc warning does not appear; the real-doc-set issue count
  increases (new drift introduced by the m27 fix).

**Phase 10D mid-point verify:** `pwsh tools/check-doc-refs.ps1 -SelfCheck` → exit 0 + `SELFTEST PASS`.

---

## Verification Trace

| failing check | implicated steps | expected diagnostic |
|---|---|---|
| `pwsh tools/dte-command.ps1 -SelfTest` — blocking stub not aborted | BP-1, BP-2, BP-3 | `dte-command timed out after <n> s: <what>` + `SELFTEST PASS` |
| `pwsh tools/iterate-telescope.ps1 -SelfCheck` — kill scope (blanket kill leaked) | BP-4, BP-5, BP-7 | exactly one dummy dies; `SELFTEST PASS` |
| `pwsh tools/iterate-telescope.ps1 -SelfCheck` — env scope | BP-6, BP-7 | `NEOVISUAL_TEST_SOLUTION` User-scope unchanged (null) |
| `pwsh tools/iterate-telescope.ps1 -SelfCheck` — seed determinism (m29) | BP-8 | second seeding run deterministic; `SELFTEST PASS` |
| `pwsh tools/test-e2e.ps1 -SelfCheck` — Assert-Budget (M37) | BP-9, BP-10, BP-11 | fresh → no throw; over-budget → `Timed out after <n> s` |
| `pwsh tools/test-e2e.ps1 -SelfCheck` — cache index (m26/m28) | BP-12, BP-13 | `Get-LogCacheIndex` tracks the cache (append +1, truncate → 0) |
| `pwsh tools/check-doc-refs.ps1 -SelfCheck` — deleted function (m27) | BP-14, BP-16 | `DOC-REF DRIFT (1 unresolved)` + `no such PowerShell function/cmdlet in tools/` |
| `pwsh tools/check-doc-refs.ps1 -SelfCheck` — missing doc warning (m27) | BP-15, BP-16 | `WARNING: <doc> — doc file not found (skipped)` + exit 0 |
| `pwsh tools/check-doc-refs.ps1` (real doc set) — no new drift (m27) | BP-14, BP-15 | `DOC-REF DRIFT (n unresolved)` with `n` unchanged from Phase-10-start baseline |

**Known-RED allowlist (do NOT report as regressions):** e2e — none. check-doc-refs.ps1 — the
pre-existing 7 unresolved references (`FzfFinder`, `FocusKeeper` ×2, `SafeHandle` ×2, 2 template
paths) are RED before Phase 10 and are owned by other phases; Phase 10 only asserts "no NEW
issues".

## Cross-phase hazards (for the hub)

- **M28 (Phase 8) touches the same helpers.** M28 consolidates harness helpers into
  `harness-common.ps1` (`Wait-LogLine`, `SeedCanonical`, `Open-TelescopeFinder`,
  `Ensure-SolutionExplorerOpen`) and adds the `-SelfCheck`/`-DryRun` switch to `test-e2e.ps1`.
  - BP-9/BP-12 add `Assert-Budget`/`Get-LogCacheIndex` to `harness-common.ps1` — no name
    collision with M28's named helpers, but the file is shared; coordinate edits.
  - BP-11/BP-13 **extend** M28's `-SelfCheck` switch — do NOT create a second switch. If M28 has
    not landed when Phase 10 executes, create the switch with the Phase 10 checks.
  - BP-12 targets `Wait-NewLogLineAfter`'s callers (`$preKey` snapshots). If M28 renames/
    consolidates `Wait-NewLogLineAfter`, target the post-M28 names.
  - `iterate-telescope.ps1:56`'s local `Assert-Budget` shadows the shared one (BP-9) — harmless;
    M28 should consolidate it later.
- **M37's `Assert-Budget` must be called BEFORE the `try`** in the scenario loop (BP-10) — inside
  the try it is swallowed into `$failures` and degrades to a per-scenario FAIL instead of a suite
  timeout.
- **M36 depends on M19** — `Save-AllDocuments` (BP-4) calls `dte-command.ps1`, which is only
  safe once BP-1/BP-2 give it a timeout. Execute Phase 10A before Phase 10B.
- **m27 regression baseline** — the check-doc-refs.ps1 lint is already RED (7 issues) before
  Phase 10; the regression assertion is "no NEW issues", never "0 issues".

## KEY DECISIONS (do not second-guess)

- **M19 uses a background runspace, not `Assert-Budget`** — a checkpoint stopwatch cannot
  interrupt a blocking COM call (plan's fix-direction correction).
- **`NEOVISUAL_TEST_SOLUTION` is Process-scope only; `NEOVISUAL_LOG_DIR` stays User-scoped** as a
  documented opt-in (M36).
- **m27's missing-doc warning is non-fatal** (exit 0) — the `:201` "doc may legitimately not
  exist yet" intent is preserved; only the silence is removed.
- **m27's `Test-ToolFunctionExists` regex MUST be Multiline** (`(?m)^function\s+<token>\b`) —
  without `(?m)`, `^` anchors at string start and nothing resolves (verified against the durable
  doc set).
- **No C#/MEF/DI changes in Phase 10** — harness-only; the net472/UI-thread/`CardinalMovment`
  constraints are untouched.


### Phase 11 - Test hermeticity (M20, M42, m30)

# Phase 11 — Test hermeticity (M20, M42, m30) — Build Plan

> Source: `plans/plan.md` Phase 11 + the Acceptance criteria / Unit test plan /
> Diagnostics sections. Lane: **unit-only, e2e deferred** — Verify-with = unit test
> names + suite counts ONLY; no e2e scenario is referenced anywhere in this plan.
> Acceptance row 11 gate: `-- LogFileWriter` (7), `-- Fzf` (fails without fzf),
> `-- Keybinding` (hermetic); NeoVisual 81→79 (M42). Diagnostics row 11: **none changed**.

## Scope recap (from the initial plan)

- **M20** (major, CONFIRMED — corrected refs `Telescope.Tests/Program.cs:132-134,149-151`
  (not :774), `:286`, `NeoVisual.Tests/Program.cs:78`):
  (a) LogFileWriter tests order-dependent on the once-per-process `_clearedThisProcess` flag;
  (b) `Run_FzfFilter_FilterMatchesPrefix` silently passes when fzf is absent;
  (c) `Run_Keybinding_DefaultFileHasTelescopeAndNav` reads the user's real
  `%APPDATA%\MyExtension\keybindings.json`.
- **M42** (major, CONFIRMED — `NeoVisual.Tests/Program.cs:109,117,262,271`): test names lie
  ("StartInInsert/StartInNormal" only classify `IsTextInputType`); `TextInput_*` MapMotion
  tests duplicate `TextMotionEngine_*`.
- **m30** (minor, CONFIRMED — `NeoVisual.Tests/Program.cs:468,479,487,503,548,555`): magic
  `actionKeyCount: 5` in FocusGuard tests.

## Current-state facts (verified against the working tree)

- `tests/Telescope.Tests/Program.cs` — **84** `Run_*` tests. LogFileWriter group = 6 tests
  (`Run_LogFileWriter_WritesAndClearsFile` :108, `Run_LogFileWriter_Buffered_NotFlushedYet`
  :154, `..._FlushWritesToDisk` :176, `..._ContentIdenticalToAppend` :196,
  `..._FlushOnClose` :230, `..._PathChangeReopens` :250). FzfFilter group = 1 test
  (`Run_FzfFilter_FilterMatchesPrefix` :282-298, silent-skip guard at :284-290).
- `tests/NeoVisual.Tests/Program.cs` — **81** `Run_*` tests.
  `Run_Keybinding_DefaultFileHasTelescopeAndNav` :74-85 (calls `KeybindingConfig.Load()` at :78).
  `Run_ToolWindowMode_TextInputTypesStartInInsert` :109, `Run_ToolWindowMode_NavigationTypesStartInNormal` :117.
  `Run_TextInput_MapMotions` :262-269, `Run_TextInput_MapInsertMotions` :271-280 (duplicates of
  `Run_TextMotionEngine_MapMotion_LeftRight` :287, `..._Words` :293, `..._InsertShift` :300).
  FocusGuard `actionKeyCount: 5` at :468, :479, :487, :503, :548, :555; the intentional
  `actionKeyCount: 0` at :495.
- `Telescope/LogFileWriter.cs` — `_clearedThisProcess` bool (:29), `Clear()` (:82-100) gates on
  it; `ClearFile(path)` (:223-238) truncates. No `using System.Collections.Generic;` today.
- `MyExtension/KeybindingConfig.cs` — `Load()` (:75-107, embedded defaults + user-file merge),
  `LoadFromJson` (:114-120), `ReadEmbeddedDefault` (:159-185). `internal sealed class` +
  `InternalsVisibleTo` → test-accessible.
- `tests/TestRunner.cs` — discovers `Run_*`; no skip accounting (a silent `return` in a test
  body is counted as PASS).

## Cross-phase hazards (must coordinate — do NOT second-guess)

1. **M42 ↔ M29 (Phase 8):** both delete the duplicate MapMotion groups — **consolidate ONCE**.
   M42 owns the deletion of `Run_TextInput_MapMotions` + `Run_TextInput_MapInsertMotions`
   (the −2 signal → 79). If M29 already removed them, M42's deletion is a no-op and the count
   is 79 → 79. **Always compare against `--list` output, never a hard-coded baseline.**
2. **M20(a) ↔ M43/M35 (Phase 7):** when Phase 11 runs, `LogFileWriter.cs` already carries M43's
   one-shot flush timer + M35's `WriteFailureCount` + M43's `FlushCount`. The per-path Clear
   change must preserve all three — do NOT reintroduce a periodic timer, do NOT remove the
   counters, do NOT touch `_flushTimer` in `Clear()`.
3. **M20(a) ↔ m23 (Phase 7):** m23 removes `NeoVisualLog.Clear()` from `TelescopeOverlay.cs:265`;
   after Phase 7 the only production Clear caller is `MyExtensionPackage.cs:59`. Per-path
   semantics preserve the once-per-process behavior for the real `%APPDATA%` log paths.

## Build Plan

### BP-1 — M20(a): `LogFileWriter.Clear()` per-path idempotency

- **Files:**
  - `Telescope/LogFileWriter.cs` (modify)
  - `tests/Telescope.Tests/Program.cs` (modify: add `Run_LogFileWriter_ClearPerPath`; reword the NOTE at :149-151)
- **Change:**
  - Add `using System.Collections.Generic;` to `LogFileWriter.cs`.
  - Replace `private static bool _clearedThisProcess;` (:29) with
    `private static readonly HashSet<string> _clearedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);`.
  - Rewrite `Clear()` (:82-100): drop the `_clearedThisProcess` guard; always
    `FlushLocked()` + `CloseWriter(ref _logWriter)` + `CloseWriter(ref _debugWriter)`, then
    truncate each path only on its first `Clear()` for that path:
    ```csharp
    public static void Clear()
    {
        lock (Sync)
        {
            FlushLocked();
            CloseWriter(ref _logWriter);
            CloseWriter(ref _debugWriter);
            ClearFileOnce(LogPath);
            ClearFileOnce(DebugLogPath);
        }
    }

    private static void ClearFileOnce(string path)
    {
        if (_clearedPaths.Add(path))
        {
            ClearFile(path);
        }
    }
    ```
  - Preserve the Phase 7 state: do NOT touch `_flushTimer` (M43 one-shot), `WriteFailureCount`
    (M35), `FlushCount` (M43). The `HH:mm:ss.fff <message>` format must stay byte-identical.
  - Reword the NOTE at `tests/Telescope.Tests/Program.cs:149-151` — the once-per-process
    rationale is obsolete; state "Clear() is per-path idempotent — each unique Guid temp path
    truncates on its first Clear regardless of test order."
- **Verify-with:**
  - New test `Run_LogFileWriter_ClearPerPath` (RED today): set BOTH `LogPath`/`DebugLogPath`
    to unique Guid paths (mirror `Run_LogFileWriter_WritesAndClearsFile` so `Clear()` never
    touches the real `%APPDATA%` files); write to pathA → `Clear()` → pathA empty; repoint
    `LogPath`/`DebugLogPath` to pathB → write → `Clear()` → pathB empty. Today the second
    `Clear()` no-ops (once-per-process flag) → pathB NOT empty → RED. After the fix → GREEN.
  - `-- LogFileWriter` = **7 tests** (6 existing + `Run_LogFileWriter_ClearPerPath`), all pass.
  - `Run_LogFileWriter_WritesAndClearsFile` passes regardless of test order (order-dependence removed).
- **Fails-if:**
  - `Run_LogFileWriter_ClearPerPath` fails (pathB not truncated after the second `Clear()`) →
    per-path tracking not implemented.
  - `Run_LogFileWriter_WritesAndClearsFile` fails when it runs after another `Clear()` → the
    once-per-process flag still gates truncation.
  - `Run_LogFileWriter_Buffered_ContentIdenticalToAppend` fails → the timestamped line format
    was altered (must stay byte-identical).
  - Compile error → missing `using System.Collections.Generic;`.

### BP-2 — M20(b): fzf fail-loud (no silent skip)

- **Files:**
  - `tests/Telescope.Tests/Program.cs` (modify `Run_FzfFilter_FilterMatchesPrefix`, :282-298)
- **Change:**
  - Replace the silent-skip guard (:284-290):
    ```csharp
    var fzf = new FzfFilter();
    if (!fzf.IsAvailable())
    {
        Console.WriteLine("      (skipped: fzf not on PATH)");
        return;
    }
    ```
    with a fail-loud throw:
    ```csharp
    var fzf = new FzfFilter();
    if (!fzf.IsAvailable())
    {
        throw new Exception("fzf is not on PATH — this test requires fzf (fail-loud, not a silent skip)");
    }
    ```
  - Keep the rest of the test unchanged (the `FilterAsync` assertion). Do NOT change the
    `FzfFilter(string? fzfPath)` ctor — it stays available for M6's injected-path tests (Phase 2).
- **Verify-with:**
  - `-- Fzf` — on a machine WITHOUT fzf, `Run_FzfFilter_FilterMatchesPrefix` now FAILS (throws)
    instead of PASS; on a machine WITH fzf it still PASSES.
  - Suite count unchanged (**84**) — the test is modified, not added/removed.
- **Fails-if:**
  - `Run_FzfFilter_FilterMatchesPrefix` still prints "(skipped: fzf not on PATH)" and returns →
    the silent-skip guard was not replaced.
  - The test throws even when fzf IS available → the guard logic was inverted.

### BP-3 — M20(c): `KeybindingConfig.LoadDefaults()` (hermetic defaults)

- **Files:**
  - `MyExtension/KeybindingConfig.cs` (modify)
  - `tests/NeoVisual.Tests/Program.cs` (modify `Run_Keybinding_DefaultFileHasTelescopeAndNav`, :78)
- **Change:**
  - Add to `KeybindingConfig` (reuse `ReadEmbeddedDefault`; NO user-file merge):
    ```csharp
    /// <summary>Test-only seam: builds a config from ONLY the embedded default-keybindings.json
    /// (no user %APPDATA% file). Internal so the offline NeoVisual test project can assert the
    /// shipped defaults hermetically.</summary>
    internal static KeybindingConfig LoadDefaults()
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var leaderKey = Keys.Space;
        ApplyJson(ReadEmbeddedDefault(), ref leaderKey, bindings);
        return new KeybindingConfig(leaderKey, bindings);
    }
    ```
  - Change `Run_Keybinding_DefaultFileHasTelescopeAndNav` :78 `var cfg = KeybindingConfig.Load();`
    → `var cfg = KeybindingConfig.LoadDefaults();`; update the comment (:76-77) to "embedded
    defaults only (hermetic — never reads the user's %APPDATA% file)".
- **Verify-with:**
  - `Run_Keybinding_DefaultFileHasTelescopeAndNav` — RED: `LoadDefaults()` doesn't exist →
    compile error; GREEN after the method is added.
  - `-- Keybinding` passes even with a hostile user config at
    `%APPDATA%\MyExtension\keybindings.json` (the test no longer reads it).
  - Suite count unchanged (**81** before M42).
- **Fails-if:**
  - Compile error: `KeybindingConfig.LoadDefaults()` not found.
  - The test still calls `KeybindingConfig.Load()` (:78 unchanged) → still reads the user's real config.
  - `LoadDefaults()` merges the user file → the hermeticity hole remains.

### BP-4 — M42: rename lying test names + delete duplicate MapMotion groups

- **Files:**
  - `tests/NeoVisual.Tests/Program.cs` (modify)
- **Change:**
  - Rename `Run_ToolWindowMode_TextInputTypesStartInInsert` (:109) →
    `Run_ToolWindowMode_TextInputTypesClassified` (the test only classifies `IsTextInputType`;
    the "StartInInsert" name lies).
  - Rename `Run_ToolWindowMode_NavigationTypesStartInNormal` (:117) →
    `Run_ToolWindowMode_NavigationTypesClassified`.
  - Delete `Run_TextInput_MapMotions` (:262-269) — fully covered by
    `Run_TextMotionEngine_MapMotion_LeftRight` (:287) + `Run_TextMotionEngine_MapMotion_Words` (:293).
  - Delete `Run_TextInput_MapInsertMotions` (:271-280) — fully covered by
    `Run_TextMotionEngine_MapMotion_InsertShift` (:300).
  - Coordinate with M29 (Phase 8): consolidate the MapMotion groups ONCE. If M29 already
    deleted these two, the deletion is a no-op.
- **Verify-with:**
  - `dotnet run --project tests/NeoVisual.Tests -- --list` — the two renamed tests appear under
    their new names; `Run_TextInput_MapMotions` / `Run_TextInput_MapInsertMotions` are gone.
  - NeoVisual.Tests count drops by exactly **2 (81 → 79)** IF M29 has not already removed them;
    if M29 already consolidated, the count is unchanged (79 → 79). Compare against `--list`
    output, never a hard-coded baseline.
  - `-- ToolWindowMode` (renamed tests) + `-- TextMotionEngine` (remaining coverage) all pass.
- **Fails-if:**
  - Count does not drop by 2 → the duplicates were not deleted (or M29 already deleted them —
    verify via `--list`).
  - `Run_TextInput_MapMotions` / `Run_TextInput_MapInsertMotions` still present in `--list` →
    deletion missed.
  - The renamed tests are missing from `--list` → rename missed.
  - `-- TextMotionEngine` fails after the deletion → the duplicates were NOT fully covered
    (deletion was unsafe).

### BP-5 — m30: named constant `PositiveActionKeyCount`

- **Files:**
  - `tests/NeoVisual.Tests/Program.cs` (modify)
- **Change:**
  - At the FocusGuard section header (after :458 `// FocusGuard — tool-window routing decision
    (pure seam)`), add:
    ```csharp
    // Guard only checks > 0 — any positive action-key count behaves identically.
    private const int PositiveActionKeyCount = 5;
    ```
  - Replace the 6 `actionKeyCount: 5` literals (:468, :479, :487, :503, :548, :555) with
    `actionKeyCount: PositiveActionKeyCount`.
  - KEEP the `0` literal at :495 (`actionKeyCount: 0` — the intentional zero case in
    `Run_FocusGuard_ZeroActionKeysBlocks`).
- **Verify-with:**
  - `-- FocusGuard` — all FocusGuard tests pass.
  - NeoVisual.Tests count unchanged relative to the post-BP-4 state (79 if M42's deletion
    applied, 81 if not) — m30 adds/removes no tests.
  - `rg "actionKeyCount: 5" tests/NeoVisual.Tests/Program.cs` returns nothing;
    `rg "actionKeyCount: 0"` still matches :495.
- **Fails-if:**
  - Any `actionKeyCount: 5` literal remains (grep finds it) → replacement missed.
  - The `0` literal was replaced by the constant → the intentional zero case is lost.
  - `-- FocusGuard` fails → the constant replacement changed behavior (should be impossible —
    same value).

## Verification Trace

| failing test / scenario | implicated steps | expected pass signal |
|---|---|---|
| `Run_LogFileWriter_ClearPerPath` (RED: second Clear on a different path no-ops) | BP-1 | GREEN after per-path Clear; both unique-Guid paths truncated |
| `Run_LogFileWriter_WritesAndClearsFile` (order-dependence on `_clearedThisProcess`) | BP-1 | GREEN regardless of test order |
| `-- LogFileWriter` (7 tests) | BP-1 | all 7 pass |
| `Run_FzfFilter_FilterMatchesPrefix` (silent pass without fzf) | BP-2 | FAILS (throws) on a machine without fzf; PASSES with fzf |
| `Run_Keybinding_DefaultFileHasTelescopeAndNav` (RED: `LoadDefaults()` missing) | BP-3 | GREEN via `LoadDefaults()`; passes with a hostile user config |
| `Run_ToolWindowMode_TextInputTypesClassified` (renamed from `...StartInInsert`) | BP-4 | present in `--list`; passes |
| `Run_ToolWindowMode_NavigationTypesClassified` (renamed from `...StartInNormal`) | BP-4 | present in `--list`; passes |
| NeoVisual.Tests count 81 → 79 (M42 −2) | BP-4 | −2 drop (duplicates removed); compare against `--list` |
| `-- FocusGuard` (m30) | BP-5 | all pass; count unchanged |

## Known-RED allowlist

None — no known-RED e2e scenario remains (docs/progress.md). All Phase 11 fixes are
RED-proven by unit tests (or compile-error RED for the new seams). The verification-agent
must NOT flag the M42 count drop (81 → 79) or the M20(b) fail-loud behavior as regressions —
they are the intended deterministic signals.

## KEY DECISIONS (build-agent must not second-guess)

1. **M42 owns the MapMotion deletion** (`Run_TextInput_MapMotions` + `Run_TextInput_MapInsertMotions`,
   the −2 signal → 79). M29 (Phase 8) must NOT delete them; if M29 already did, M42's deletion
   is a no-op and the count is 79 → 79 — always compare against `--list`.
2. **M20(b) uses the throw (fail-loud) option**, not TestRunner skip-accounting — per the plan's
   verification "`-- Fzf` now FAILS on a machine without fzf instead of PASS". The suite is
   expected to run on a machine with fzf installed.
3. **M20(a) preserves the Phase 7 LogFileWriter state** (M43 one-shot timer, M35
   `WriteFailureCount`, M43 `FlushCount`) — do not reintroduce a periodic timer or remove the
   counters; the `HH:mm:ss.fff <message>` format stays byte-identical.
4. **m30 keeps the `0` literal** (intentional zero case); only the `5` literals become the
   named constant.
5. **No diagnostic log-line changes in Phase 11** (acceptance row 11: "none changed").


### Phase 12 - Naming/convention (M38-M41, n1-n12)

# Build Plan — Phase 12: Naming/convention sweep (M38, M39, M40, M41, n1–n12)

> Source: `plans/plan.md` Phase 12 + Acceptance criteria / Unit test plan / Diagnostics
> sections. Lane: **unit-only, e2e deferred** — Verify-with = unit test names + diagnostic
> formats ONLY (no e2e scenario references). Every step is RED-proven by a unit test or by
> `dotnet build` + the existing suites.
>
> **Phase 12 gate (plan.md acceptance row 12):** `dotnet build` + `-- WindowNavigationEngine`
> + `-- Grep` + `-- Preview` all GREEN; the diagnostic contract
> `[NeoVisual] navigate direction=L/R/D/U` MUST stay single-char (M40).
>
> **Known-RED allowlist:** none (per plan.md — no known-RED e2e scenario remains; all fixes
> are RED-proven by unit tests or build + existing suites for no-seam items).

## Cross-phase hazards (read before executing)

1. **M40 log-line-as-contract is CRITICAL.** `InputHandler.Navigate` logs
   `navigate direction={direction}` and the harness asserts `navigate direction=L/R/D/U`.
   The `Direction` signature change MUST keep emitting a single-char token — derive it via a
   pure `DirectionExtensions.ToChar()` and pin it with `Run_Direction_ToChar`. A multi-char
   token (`direction=Left`) violates the contract.
2. **n8 overlaps M2 (Phase 4).** M2 already drops the write-only `_rect` field in
   `WindowAdapter`. BP-13 is CONDITIONAL: verify `_rect` is gone; drop it only if M2 was
   skipped.
3. **n11's `Segment`→`Tokenize` rename touches PreviewRenderer + Telescope.Tests call
   sites** — 1 production call site (`PreviewRenderer.cs:56`) + 7 test call sites. A missed
   call site is a compile error.
4. **M38's namespace wrap interacts with CR1's `DefaultControllerFor` factory.** CR1
   (Phase 0) adds `WindowManager.DefaultControllerFor` inside the class; M38 wraps the whole
   class in `namespace MyExtension` — the factory moves with it. Do NOT "fix" the namespace
   of the factory or its callers.
5. **Same-file ordering:** M40 (signature) before n9 (field renames) in `WindowMatrix.cs`;
   M39 (rename `Axis`→`PerpendicularAxis`) before M40 (add `ToChar`) in `Direction.cs`;
   n11 (rename call site) before n12 (`Split` optimization) in `PreviewRenderer.cs`.
6. **M41's `GrepFinder` is sealed** — the throw-assert test must use reflection
   (`GetMethod("GatherHits", NonPublic|Instance)`).
7. **n10's `LineStart` rename overloads the existing private `LineStart(int)`** — legal C#
   (different parameter lists); keep BOTH methods, do not delete the private helper.
8. **n2 must NOT touch the `[MyExtension]` auto-open lines** — that prefix is pinned by
   `Run_LogPrefixes_Pinned` and the harness.

---

## Phase 12A — Navigation naming sweep (M38, M39, M40, n9)

### BP-1 — M38: wrap `WindowManager` in `namespace MyExtension`

- **Files:** `MyExtension/WindowManager.cs`
- **Change:** `WindowManager` is the only file without a namespace block (class declared in
  the global namespace at line 9). Wrap the whole `public sealed class WindowManager :
  IDisposable` in `namespace MyExtension { ... }` (add the closing brace at end of file).
  Delete the self-`using MyExtension;` (line 5). Keep `using CardinalNavigation;` and the
  other usings. No reference changes — all 7 code sites (`MyExtensionPackage.cs`,
  `InputHandler.cs`) already use the simple name inside `namespace MyExtension`.
- **Verify-with:** `dotnet build` succeeds (the class resolves from `namespace MyExtension`);
  `dotnet run --project tests/NeoVisual.Tests` stays green (the Phase 0
  `Run_WindowManager_DefaultControllerFor` test still compiles/resolves).
- **Fails-if:** build error `CS0246`/`CS0234` "The type or namespace name 'WindowManager'
  could not be found" in `MyExtensionPackage.cs`/`InputHandler.cs`; a stray `using
  MyExtension;` left inside `namespace MyExtension` (redundant-using warning, not fatal); a
  misplaced closing brace so the file fails to parse.

### BP-2 — M39: rename `Axis()` → `PerpendicularAxis()`

- **Files:** `MyExtension/CardinalMovment/Direction.cs`,
  `MyExtension/CardinalMovment/WindowNavigationEngine.cs`
- **Change:** Rename `DirectionExtensions.Axis(this Direction d)` → `PerpendicularAxis(this
  Direction d)` (line 8; keep the `Axis` enum name). Update the single call site
  `WindowNavigationEngine.cs:58` `c.Adjacency(active, direction.Axis())` →
  `direction.PerpendicularAxis()`. No test touches the method directly.
- **Verify-with:** `dotnet build` succeeds; `dotnet run --project tests/NeoVisual.Tests --
  WindowNavigationEngine` — the 10 `Run_WindowNavigationEngine_*` tests pin the algorithm
  end-to-end (they call `SelectTarget` with `Direction`, which internally uses
  `PerpendicularAxis`).
- **Fails-if:** build error `CS1061` "'Direction' does not contain a definition for 'Axis'"
  at `WindowNavigationEngine.cs:58` (missed call site); `Run_WindowNavigationEngine_*`
  fails (adjacency computed on the wrong axis — behavior drift).

### BP-3 — M40: `NavigateInDirection(Direction)` + single-char `navigate direction=` token

- **Files:** `MyExtension/CardinalMovment/WindowMatrix.cs`, `MyExtension/InputHandler.cs`,
  `MyExtension/Actions.cs`, `MyExtension/CardinalMovment/CardinalNavigationConstants.cs`,
  `MyExtension/CardinalMovment/Direction.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:**
  - `WindowMatrix.NavigateInDirection(char direction)` → `NavigateInDirection(Direction
    direction)` (line 94); delete the private `ToDirection(char)` (lines 122–129) and the
    `Direction dir = ToDirection(direction);` line (104).
  - `InputHandler.Navigate(char direction)` → `Navigate(Direction direction)` (line 481).
    **CRITICAL:** the log at line 483 must keep emitting a single-char token — add a pure
    helper `DirectionExtensions.ToChar(this Direction d)` in `Direction.cs` returning
    `'L'/'R'/'U'/'D'` (Left→'L', Right→'R', Up→'U', Down→'D') and log
    `navigate direction={direction.ToChar()}`.
  - Delete the 4 char constants `LEFT/RIGHT/UP/DOWN` from
    `CardinalNavigationConstants.cs:5-8`.
  - Update the 4 `Actions.cs` sites (lines 18–21): `h.Navigate(CardinalNavigationConstants.LEFT)`
    → `h.Navigate(Direction.Left)`, `RIGHT`→`Direction.Right`, `UP`→`Direction.Up`,
    `DOWN`→`Direction.Down`.
  - Add unit test `Run_Direction_ToChar` in NeoVisual.Tests: `Direction.Left.ToChar()=='L'`,
    `Right→'R'`, `Up→'U'`, `Down→'D'` (RED: `ToChar` doesn't exist → compile error).
- **Verify-with:** `dotnet build` succeeds; `dotnet run --project tests/NeoVisual.Tests --
  WindowNavigationEngine` green (algorithm unchanged); NEW `Run_Direction_ToChar` green
  (pins the single-char token contract). Diagnostic contract:
  `[NeoVisual] navigate direction=L/R/D/U` — the emitted token must be byte-identical
  single-char.
- **Fails-if:** build error `CS1503` "cannot convert from 'char' to 'Direction'" at the
  `Actions.cs` sites or `InputHandler.Navigate`; the log line emits `navigate direction=Left`
  (multi-char) instead of `navigate direction=L` — violates the single-char token contract;
  `Run_Direction_ToChar` fails; `Run_WindowNavigationEngine_*` fails (signature change broke
  the engine call).

### BP-4 — n9: normalize `WindowMatrix` field conventions to `_` prefix

- **Files:** `MyExtension/CardinalMovment/WindowMatrix.cs`
- **Change:** Normalize the mixed field conventions (lines 14, 16, 18): `m_ActiveWindows` →
  `_activeWindows`, `m_activeWindow` → `_activeWindow` (all references within the file:
  ctor lines 49, 57–58, 64, 70–71; `NavigateInDirection` lines 98, 105–106, 110).
  `_settings` is already `_`-prefixed — leave it.
- **Verify-with:** `dotnet build` succeeds (compiler-verified — any missed reference is a
  `CS0103` error); `dotnet run --project tests/NeoVisual.Tests -- WindowNavigationEngine`
  green (behavior unchanged).
- **Fails-if:** build error `CS0103` "The name 'm_ActiveWindows'/'m_activeWindow' does not
  exist in the current context" (missed reference); navigation behavior changes (a field
  accidentally shadowed).

---

## Phase 12B — Finder + package nits (M41, n1, n2, n3, n4)

### BP-5 — M41: `GrepFinder.GatherHits()` throws `NotSupportedException`

- **Files:** `Telescope/GrepFinder.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Replace the `GatherHits()` stub (line 49) `=> Array.Empty<GrepHit>()` with
  `=> throw new NotSupportedException("GrepFinder is query-driven; call GetCandidates(query)")`.
  Behavior-preserving — no callers today (`FinderBase.GetCandidates` is overridden by
  GrepFinder's query-driven `GetCandidates(string)`); converts a silent empty into a loud
  failure. Add unit test `Run_GrepFinder_GatherHitsThrowsNotSupported` in Telescope.Tests
  using reflection (`typeof(GrepFinder).GetMethod("GatherHits", BindingFlags.NonPublic |
  BindingFlags.Instance)` → invoke → assert `NotSupportedException`) since GrepFinder is
  sealed.
- **Verify-with:** `dotnet build` succeeds; `dotnet run --project tests/Telescope.Tests --
  Grep` — the 6 existing `Run_GrepFinder_*` tests (all call `GetCandidates`, none touch
  `GatherHits`) stay green; NEW `Run_GrepFinder_GatherHitsThrowsNotSupported` green.
  Diagnostic contract: `[Telescope] grep hits=...` unchanged (GetCandidates path untouched).
- **Fails-if:** `Run_GrepFinder_*` fails (the throw leaks into `GetCandidates` — e.g. the
  throw was placed in the wrong method); the reflection test fails (method not found / wrong
  exception type); build error if the throw signature is wrong.

### BP-6 — n1: rename `_keyboardLogger` → `_keyboardHook`

- **Files:** `MyExtension/MyExtensionPackage.cs`
- **Change:** Rename the field `_keyboardLogger` → `_keyboardHook` (line 44) and its 3 uses
  (line 112 assignment; lines 736–737 in `Dispose`). Compiler-verified.
- **Verify-with:** `dotnet build` succeeds (compiler-verified — any missed reference is
  `CS0103`).
- **Fails-if:** build error `CS0103` "The name '_keyboardLogger' does not exist" (missed
  reference in `Dispose`).

### BP-7 — n2: `GetCaretOffset` failure log `[Telescope]` → `[NeoVisual]`

- **Files:** `MyExtension/MyExtensionPackage.cs`
- **Change:** In `GetCaretOffset` (line 693), change the failure-log prefix
  `Telescope.DiagnosticLog.Telescope` → `Telescope.DiagnosticLog.NeoVisual` (GetCaretOffset
  is an extension-level concern, not a Telescope finder). Keep `[MyExtension]` for the
  auto-open lines (:61, :143, :150, :198, :202, :214, :232, :248, :252, :256) — do NOT
  touch those.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- LogPrefixes` —
  `Run_LogPrefixes_Pinned` stays green (no prefix constant changed); `dotnet build` succeeds.
- **Fails-if:** `Run_LogPrefixes_Pinned` fails (a prefix constant was accidentally changed);
  an auto-open line's `[MyExtension]` prefix was retagged — violates the `[MyExtension]`
  prefix contract pinned by `Run_LogPrefixes_Pinned`.

### BP-8 — n3: interpolate VK constants in the `toolwindow-move` log

- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** In the `Keys.J`/`Keys.K` action lambdas (lines 44–45), replace the magic
  `vk=40`/`vk=38` literals with `(int)KeyInjection.VK_DOWN`/`(int)KeyInjection.VK_UP`
  interpolation (mirror `GeneralToolWindowController.cs:49`). The emitted token must stay
  byte-identical: `(int)KeyInjection.VK_DOWN == 40`, `(int)KeyInjection.VK_UP == 38`.
- **Verify-with:** `dotnet build` succeeds; the diagnostic format
  `[NeoVisual] toolwindow-move key=J -> arrow vk=40` / `key=K -> arrow vk=38` is preserved
  byte-identical (the interpolation evaluates to the same numbers).
- **Fails-if:** the log line emits a different number (e.g. `vk=38` for J or `vk=40` for K —
  swapped) — violates the `toolwindow-move ... vk=40/38` byte-identical contract; build
  error if `KeyInjection.VK_DOWN`/`VK_UP` are not accessible.

### BP-9 — n4: real pane GUID + named `CreatePane` args

- **Files:** `Telescope/NeoVisualLog.cs`
- **Change:** Replace the placeholder pane GUID (line 28) with a real, stable GUID constant
  (any fixed `Guid` literal — it only identifies the pane). Use named arguments for
  `CreatePane(ref PaneGuid, "NeoVisual", 1, 1)` (line 149) →
  `CreatePane(ref PaneGuid, "NeoVisual", fInitVisible: 1, fClearWithSolution: 1)`.
- **Verify-with:** `dotnet build` succeeds; `dotnet run --project tests/Telescope.Tests`
  stays green (no test touches the pane; the log-file tests are unaffected).
- **Fails-if:** build error (a malformed `Guid` literal); the pane GUID collides with another
  VS pane (not unit-detectable — VS-coupled, no seam).

---

## Phase 12C — Harness nits (n5, n6, n7)

### BP-10 — n5: fix `neovascular-*` → `neovisual-*` comments

- **Files:** `tools/test-e2e.ps1`
- **Change:** Fix the 5 comments referencing the non-existent `neovascular-*` scenario names
  → `neovisual-*` (lines 539, 572, 825, 924, 987). Comment-only — no code change.
- **Verify-with:** `rg "neovascular" tools/test-e2e.ps1` returns no matches (grep gate);
  `dotnet build` unaffected. No unit seam (harness-only).
- **Fails-if:** a `neovascular-` token remains in `tools/test-e2e.ps1`; a comment edit
  accidentally changed code (e.g. a scenario-name string used in a real assertion).

### BP-11 — n6: named VK constants for the frequent raw-hex codes

- **Files:** `tools/harness-common.ps1`, `tools/test-e2e.ps1`
- **Change:** Add named VK constants in `harness-common.ps1` beside the existing
  `$script:VkEscape/VkSpace/VkEnter/VkTab` (lines 18–21) for the frequent raw-hex codes:
  `$script:VkF = 0x46`, `$script:VkT = 0x54`, `$script:VkD = 0x44`, `$script:VkR = 0x52`,
  `$script:VkG = 0x47`, `$script:VkI = 0x49`, `$script:VkJ = 0x4A`, `$script:VkK = 0x4B`,
  `$script:VkL = 0x4C`, `$script:VkH = 0x48`, `$script:VkO = 0x4F`, `$script:VkA = 0x41`,
  `$script:VkM = 0x4D`, `$script:VkW = 0x57`, `$script:VkE = 0x45`. Replace the corresponding
  raw `Send-Tap 0xNN` calls in `test-e2e.ps1` with the named constants. Behavior-preserving
  (same VK values).
- **Verify-with:** `pwsh -NoProfile -Command "& tools/harness-common.ps1"` parses cleanly;
  `rg "Send-Tap 0x(46|54|44|52|47|49|4A|4B|4C|48|4F|41|4D|57|45)" tools/test-e2e.ps1`
  returns no matches for the replaced codes. No unit seam (harness-only).
- **Fails-if:** a `Send-Tap` call was changed to a DIFFERENT VK value (a typo in a constant);
  a constant name collides with an existing `$script:` variable; the harness file fails to
  parse.

### BP-12 — n7: delete dead `$expHive` + `Find-VsWindow`

- **Files:** `tools/iterate-telescope.ps1`
- **Change:** Delete the dead `$expHive = 'Exp'` (line 48) and the dead
  `function Find-VsWindow {...}` (lines 68–72). Both are defined but never used.
- **Verify-with:** `rg "expHive|Find-VsWindow" tools/iterate-telescope.ps1` returns no
  matches; `pwsh tools/check-doc-refs.ps1` exits 0 (no scanned doc references
  `Find-VsWindow`). No unit seam (harness-only).
- **Fails-if:** a live reference to `$expHive`/`Find-VsWindow` remains (would be a runtime
  error); `check-doc-refs.ps1` reports `Find-VsWindow` unresolved (if a scanned doc
  referenced it — none do today).

---

## Phase 12D — Preview/motion nits (n8, n10, n11, n12)

### BP-13 — n8: drop the write-only `_rect` field (CONDITIONAL on M2)

- **Files:** `MyExtension/CardinalMovment/WindowAdapter.cs`
- **Change:** CONDITIONAL — Phase 4 M2 already drops the write-only `_rect` field (lines 20,
  125–126) and fixes the misleading comment (lines 30–31). Verify `_rect` is absent; if M2
  was skipped, drop the field and return the local from `RefreshRect`
  (`return new RectCoordinate(left, top, width, height);`).
- **Verify-with:** `rg "_rect" MyExtension/CardinalMovment/WindowAdapter.cs` returns no
  matches; `dotnet build` succeeds; `dotnet run --project tests/NeoVisual.Tests --
  WindowNavigationEngine` green.
- **Fails-if:** `_rect` still present after the step (M2 was skipped and the drop was not
  performed); build error from a dangling `_rect` reference.

### BP-14 — n10: rename `LineStartHome` → `LineStart`

- **Files:** `Telescope/TextMotionNavigator.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Rename `LineStartHome()` → `LineStart()` (line 155). NOTE: this overloads with
  the existing `private int LineStart(int index)` — legal C# (different parameter lists);
  keep BOTH. Update the 2 test call sites (lines 534, 619) `n.LineStartHome()` →
  `n.LineStart()`. Leave `InsertStart`/`InsertEnd` as-is (the doc already says "text/line"
  and whole-text is intended for the preview — renaming would churn tests for no behavior
  gain).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Preview` — the
  `Run_Preview_*` + `Run_PromptMotion_*` tests pin caret positions (RED: `LineStartHome` no
  longer exists → compile error at the 2 call sites).
- **Fails-if:** build error `CS1061` "'TextMotionNavigator' does not contain a definition
  for 'LineStartHome'" (missed call site); `Run_Preview_LineStartEnd` /
  `Run_PromptMotion_LineStartEnd` fail (caret position drift).

### BP-15 — n11: `Keywords` → `_keywords`; `Segment` → `Tokenize`

- **Files:** `Telescope/SyntaxHighlighter.cs`, `Telescope/PreviewRenderer.cs`,
  `tests/Telescope.Tests/Program.cs`
- **Change:** Rename the private field `Keywords` → `_keywords` (line 43, used at line 161).
  Rename the method `Segment(string)` → `Tokenize(string)` (line 64). Update the call site
  `PreviewRenderer.cs:56` `SyntaxHighlighter.Segment(content)` →
  `SyntaxHighlighter.Tokenize(content)`. Update the 7 test call sites (lines 842, 852, 859,
  867, 874, 882, 893) `SyntaxHighlighter.Segment(...)` → `SyntaxHighlighter.Tokenize(...)`.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Syntax` — the 7
  `Run_Syntax_*` tests pin tokenizer behavior (RED: `Segment` no longer exists → compile
  error); `dotnet run --project tests/Telescope.Tests -- Preview` green (preview rendering
  unchanged). Diagnostic contract: `[Telescope] preview tokens=...` unchanged.
- **Fails-if:** build error `CS1061` "'SyntaxHighlighter' does not contain a definition for
  'Segment'" (missed call site in PreviewRenderer or tests); `Run_Syntax_*` fails (tokenizer
  behavior drift); `Run_Preview_*` fails.

### BP-16 — n12: replace `segment.Text.Split('\n')` with a scan

- **Files:** `Telescope/PreviewRenderer.cs`
- **Change:** In `SetContent` (line 60), replace `string[] lines = segment.Text.Split('\n');`
  (allocates a string[] per segment per render) with a scan using `IndexOf('\n')`/`Substring`
  (or `StringReader`) that yields the same line runs. Behavior-preserving.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Preview` — the
  `Run_Preview_*` tests + `Run_Syntax_RoundTripsText` stay green (behavior unchanged).
  Diagnostic contract: `[Telescope] preview tokens=...` unchanged.
- **Fails-if:** `Run_Preview_*` fails (line-splitting behavior changed — e.g. a trailing
  empty line dropped or an extra paragraph added); `Run_Syntax_RoundTripsText` fails.

---

## Phase 12E — Doc-ref propagation (rule 5a)

### BP-17 — update stale doc references to the renamed symbols

- **Files:** `.opencode/skills/vs-extension-dev/SKILL.md`, `docs/architecture-review.md`
- **Change:** Update the stale references to the renamed symbols (grep the docs for the old
  names):
  - SKILL.md:41,146 — `WindowMatrix.NavigateInDirection(direction)` → note the `Direction`
    signature (M40).
  - SKILL.md:158 — `direction.Axis()` → `direction.PerpendicularAxis()` (M39).
  - docs/architecture-review.md:366 — `SyntaxHighlighter.Segment` →
    `SyntaxHighlighter.Tokenize` (n11).
  - Verify `docs/spec.md:101` `NavigateInDirection` and `docs/architecture-review.md:348`
    `NavigateInDirection` still resolve (the method still exists — no change needed).
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` exits 0 (the acceptance gate for rename
  steps per rule 5a).
- **Fails-if:** `check-doc-refs.ps1` exits 1 with a DOC-REF DRIFT line naming a Phase 12
  symbol (a scanned doc still references a removed name); a doc edit accidentally changed a
  code snippet's meaning.

---

## Verification Trace

| failing test / gate | implicated steps | expected diagnostic |
|---|---|---|
| `dotnet build` (compile gate) | BP-1, BP-2, BP-3, BP-4, BP-5, BP-6, BP-7, BP-8, BP-9, BP-13, BP-14, BP-15, BP-16 | n/a — compiles clean |
| `Run_WindowNavigationEngine_*` (10 tests, NeoVisual.Tests) | BP-2, BP-3, BP-4 | `[NeoVisual] navigate direction=L/R/D/U` unchanged (single-char) |
| `Run_Direction_ToChar` (NEW, NeoVisual.Tests) | BP-3 | `navigate direction=L/R/D/U` — `ToChar()` returns the single-char token |
| `Run_GrepFinder_*` (6 tests, Telescope.Tests) | BP-5 | `[Telescope] grep hits=...` unchanged |
| `Run_GrepFinder_GatherHitsThrowsNotSupported` (NEW, Telescope.Tests) | BP-5 | n/a — `NotSupportedException` from `GatherHits()` |
| `Run_LogPrefixes_Pinned` (Telescope.Tests) | BP-7 | `[NeoVisual] ` / `[Telescope] ` / `[MyExtension] ` prefixes unchanged |
| `Run_Preview_*` + `Run_PromptMotion_*` (Telescope.Tests) | BP-14, BP-15, BP-16 | `[Telescope] preview tokens=...` unchanged |
| `Run_Syntax_*` (7 tests, Telescope.Tests) | BP-15 | n/a — tokenizer behavior pinned |
| `pwsh tools/check-doc-refs.ps1` | BP-17 | n/a — exits 0 |
| `rg "neovascular" tools/test-e2e.ps1` (grep gate) | BP-10 | n/a — no matches |
| `rg "expHive\|Find-VsWindow" tools/iterate-telescope.ps1` (grep gate) | BP-12 | n/a — no matches |
| `rg "_rect" MyExtension/CardinalMovment/WindowAdapter.cs` (grep gate) | BP-13 | n/a — no matches |

**Known-RED allowlist:** none (per plan.md — no known-RED e2e scenario remains; all Phase 12
fixes are RED-proven by unit tests or build + existing suites for the no-seam items n4/n5/n6/n7).

---

## Execution Log

### Phase 0 — Criticals (CR1, CR2, CR3) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_WindowManager_DefaultControllerFor` (NeoVisual.Tests) RED
  via CS0117 (`WindowManager.DefaultControllerFor` missing); `Run_OverlayShowState_*`
  (Telescope.Tests) RED via CS0246 (`OverlayShowState` missing); CR2 gate snippet prints
  `RESULT: PASS` (exit 0) for a `$false`-returning scenario before the fix. RIGHT-REASON
  confirmed (missing-symbol failures, not test-authoring typos).
- **BUILD (build-agent):** BP-2 `DefaultControllerFor` static factory; BP-3 package loop
  rewiring (factory + null skip, SolutionExplorerController kept as the only specialized
  registration); BP-4 CR2 gate `if ($result -eq $false) { $failures += ... }`; BP-6
  `OverlayShowState`; BP-7 overlay `BeginInvoke` guard (`ShouldShowDialog()`, not `IsVisible`).
  NeoVisual 82/82, Telescope 87/87, `dotnet build` pass, no DEVIATIONS.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved after the hub
  fixed pre-existing drift: `FzfFinder`/`FocusKeeper` → proposedSymbols, `SafeHandle` →
  externalAllowlist, concrete hub session-id paths → templatePaths). NeoVisual 82/82,
  Telescope 87/87, CR2 snippet catches `$false` (exit 1), diagnostic contract untouched.
  **DEVIATION flagged:** CR2 no-double-count property is order-dependent (`$result` not reset
  per iteration — a throw after a `$false`-returning scenario double-counts).
- **ADJUDICATE (hub):** DEVIATION CR2-gate → **REJECT** (plan's Fails-if names double-count as
  a failure; fix must hold unconditionally).
- **DEBUG (debug-agent):** one-line `$result = $null` reset at the top of each scenario-loop
  iteration (`tools/test-e2e.ps1:1903`). Standalone snippet proves all three orderings:
  false-returning → FAIL exit 1; throw-after-false → only exception message (no double-count);
  throw-first → only exception message. Counterfactual (fix removed) double-counts — snippet is
  sensitive to the fix. `test-e2e.ps1` NOT executed (user mandate: no e2e harness commands on
  this machine).
- **E2E queue:** E2E-CR-1..3 appended to `docs/e2e-queue.md` (QUEUED for a VS-capable machine).
- **Doc-ref lint:** PASS (28 docs, 0 unresolved).
- `delegations: 4 | VS boots: 0 | iterations: 0`

### Phase 1 — Hook hot-path contract (M1, M3, M4, m1) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_StaleToolWindowSentinel_*` (5) RED via CS0246 (`StaleToolWindowSentinel`
  missing); `Run_KeyNameBuilder_*` (6) RED via CS0103 (`KeyNameBuilder` missing); M1 per-key
  `[Hook] key=` line confirmed present at `GlobalKeyboardHook.cs:124` (read-only). RIGHT-REASON
  confirmed.
- **BUILD (build-agent):** BP-1 deleted the per-key `[Hook] key=` log (installed/INSTALL FAILED
  intact); BP-2 `StaleToolWindowSentinel`; BP-3 sentinel cache + `RefreshStaleSentinel()` +
  `[NeoVisual] stale-toolwindow sentinel active` on false→true transition + `IsKeyOfInterest`
  first-statement refresh; BP-4 `_isTextInputType` cache + sentinel-aware `IsTextInputType` +
  5 InputHandler sites replaced; BP-5 `KeyNameBuilder` (BuildSimpleKey/KeyToString deleted).
  NeoVisual 93/93, Telescope 87/87, build pass. **DEVIATION:** check-doc-refs flagged 2 stale
  `BuildSimpleKey` refs in `docs/architecture-review.md` (F22 records) — hub fixed them
  (`BuildSimpleKey`→`KeyNameBuilder`), lint PASS.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); NeoVisual 93/93,
  Telescope 87/87 (`Run_LogPrefixes_Pinned` GREEN); M1 deletion + M3/M4/m1 wiring confirmed
  read-only; ADDED `[NeoVisual] stale-toolwindow sentinel active` byte-identical; no other
  diagnostic drift; failure-log triage 6 read / 0 actionable.
- `delegations: 3 | VS boots: 0 | iterations: 0`

### Phase 2 — Finder path amortization (M5, M6, M7, M8) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_GrepFinder_CacheEnumeratesOnce` (CS1729 — no 3-arg ctor);
  `Run_FileContentCache_*` (CS0246); `Run_FzfFilter_NonexistentPathFallsBackAndLogs` (runtime — no
  `[Telescope] fzf filter failed:` log); `Run_FzfFilter_TimeoutKillsAndFallsBack` (CS0117 — no
  `FilterTimeoutMs`); `Run_FzfFilter_QuoteArg_TrailingBackslash` (CS0117 — private);
  `Run_FzfFilter_IsAvailableFalseForMissingPath` (PASSES already — anticipated); `Run_LineIndex_*`
  (CS0246); `Run_FilterFailureLog_Format` (CS0103). RIGHT-REASON confirmed.
- **BUILD (build-agent):** BP-1 shared `ProjectFileCache` injected into GrepFinder + CodeIssuesFinder
  (3-arg test ctor + `_fileCache.Get(_testEnumerate)` + DTE solution-FullName invalidation; ONE shared
  cache in MyExtensionPackage); BP-2 `FileContentCache` (LastWriteTimeUtc-keyed) + `ScanFile` instance
  method; BP-3 `FzfFilter` timeout/kill/fallback + `[Telescope] fzf filter failed:` logs; BP-4 `QuoteArg`
  internal + trailing-backslash fix; BP-5 `IsAvailable()` wired once at overlay open
  (`fzf unavailable — showing unfiltered list`); BP-6 `LineIndex` (shared with M11); BP-7
  `PreviewRenderer` binary-search `CaretToPointer`; BP-8 `FilterFailureLog` + `FilterAndUpdateAsync`
  try/catch. Telescope 98/98, NeoVisual 93/93, build pass.
  **3 DEVIATIONS adjudicated ACCEPT (hub):** (1) GrepFinder/CodeIssuesFinder ctors `internal` not
  `public` (ProjectFileCache is internal → CS0051; InternalsVisibleTo covers consumers); (2)
  `_fileCache` readonly dropped in CodeIssuesFinder so the test-only ctor compiles; (3)
  `_testFileSource`→`_testEnumerate` rename + 2-arg test ctor delegates to 3-arg. All behavior-preserving.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); Telescope 98/98 (11 new
  GREEN), NeoVisual 93/93; ADDED 4 diagnostics byte-identical (`fzf filter failed: {msg}`,
  `fzf filter failed: timeout after {ms}ms`, `fzf unavailable — showing unfiltered list`,
  `filter failed: {msg}`); UNCHANGED 4 confirmed (`grep hits=`, `open finder=`, `preview caret=`,
  `results count=`); all 6 wiring checks confirmed; failure-log triage 6 read / 0 actionable.
- `delegations: 3 | VS boots: 0 | iterations: 0`

### Phase 3 — Preview/motion correctness (M9, M10, M11) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_Syntax_UnterminatedStringEndingInBackslash` RED via
  `ArgumentOutOfRangeException` (unguarded `i += 2` escape advance past `text.Length`);
  `Run_Preview_UpFromSecondLineWithLeadingBlankLine` RED via `ArgumentOutOfRangeException`
  (`LastIndexOf('\n', lineStart - 2)` with `lineStart - 2 == -1`); `Run_Preview_CaretOnBlankLine`
  PASSES already (anticipated — `LineIndex` from Phase 2 is already correct). RIGHT-REASON confirmed.
- **BUILD (build-agent):** BP-1 `i += (i + 1 < text.Length) ? 2 : 1;` (escape branch only); BP-2
  `LastIndexOf('\n', Math.Max(0, lineStart - 2))` (line-0 early return untouched); BP-3 `LineIndex`
  reused (no parallel helper); BP-4 blank-line fallback in `CaretToPointer`. Telescope 101/101,
  NeoVisual 93/93, build pass.
  **1 DEVIATION adjudicated ACCEPT (hub):** BP-4's literal `if (index == plain)` adapted to
  `if (offset == plain)` — the M7-refactored `CaretToPointer` uses a per-paragraph `plain` counter
  (init 0 at the Paragraph block top) and `index` is the absolute caret index, so the literal would
  only fire for absolute index 0 and miss blank lines; `offset == plain` (== 0) correctly identifies
  a line start. Behavior-preserving; diagnostic contract unchanged.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); Telescope 101/101 (3 new
  GREEN), NeoVisual 93/93; `preview caret=... line=...` byte-identical (no emission-site edit); all 4
  fix checks confirmed; failure-log triage 6 read / 0 actionable.
- `delegations: 3 | VS boots: 0 | iterations: 0`

### Phase 4 — Navigation robustness (M2, M12) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_NavigationSnapshot_*` (2) RED via CS0103 (`NavigationSnapshot`
  missing); `Run_WindowAdapter_TryGetScreenRect_*` (2) RED via CS0117 (`TryGetScreenRect` missing).
  RIGHT-REASON confirmed.
- **BUILD (build-agent):** BP-1 `NavigationSnapshot` (pure, `Capture` null-guard, `Active` derived);
  BP-2 single-pass rect snapshot in `NavigateInDirection` (no N+1); BP-3 `_rect` field dropped +
  comment reworded (n8 covered); BP-4 `RectCoordinate.Empty` + `_frame4` cache + `TryGetScreenRect`
  + `RefreshRect` Empty fallback; BP-5 `AutoHides()` moved inside the try (guard order preserved).
  NeoVisual 97/97, Telescope 101/101, build pass. No DEVIATIONS.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); NeoVisual 97/97 (4 new
  GREEN + 10 WindowNavigationEngine GREEN), Telescope 101/101; `navigate direction=...` byte-identical;
  all 5 fix checks confirmed; failure-log triage 6 read / 0 actionable.
- `delegations: 3 | VS boots: 0 | iterations: 0`

### Phase 5 — Finder correctness (M13, M14) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_Issues_Severity*` (3) RED via CS0117 (`ClassifySeverity` missing);
  `Run_HierarchyForestBuilder_*` (5) RED via CS0246/CS0103 (`HierarchyForestBuilder`/`HierarchyItemInfo`
  missing). RIGHT-REASON confirmed. One test-authoring fix: enum members qualified as
  `vsBuildErrorLevel.vsBuildErrorLevelMedium/High/Low` (unqualified form would not compile even after
  the fix — invalid RED).
- **BUILD (build-agent):** BP-2 `ClassifySeverity(item.ErrorLevel)` + `Classify(string)` deleted +
  `internal static ClassifySeverity` (High→Error/Medium→Warning/Low→Info); BP-4 `HierarchyForestBuilder`
  + `HierarchyItemInfo` (reuses `HierarchyResolver` Kind constants, produces `HierarchyNode`); BP-5
  `BuildForest` deleted + `MapChildren` DTE adapter + `ResolveTreeItem` rewired; BP-6 doc-ref
  `BuildForest`→`HierarchyForestBuilder`. Telescope 104/104, NeoVisual 102/102, build pass.
  **3 DEVIATIONS adjudicated ACCEPT (hub):** (1) `docs/progress.md` backtick at line 719 (not 705);
  (2) dangling `<see cref="BuildForest"/>` cref → `HierarchyForestBuilder` (CS1574 avoidance); (3)
  defensive `if (items == null) return forest;` guard (no behavior change).
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); Telescope 104/104 (3 new
  GREEN), NeoVisual 102/102 (5 new GREEN + existing SolutionExplorer/ActionTable/HierarchyResolver
  GREEN); `opened issue: ...` unchanged; all fix checks confirmed; failure-log triage 6 read / 0
  actionable.
- `delegations: 3 | VS boots: 0 | iterations: 0`

### Phase 6 — Hook-path safety (M15, M16, M17) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_LeaderMatcher_ThrowingActionIsCaught` RED via CS0117/CS1061
  (`LeaderResultKind.Failed`/`LeaderResult.ErrorMessage` missing); `Run_FocusGuard_TextInputSurfaceFocused_*`
  + 11 updated `Run_FocusGuard_*` RED via CS1739 (`textInputSurfaceFocused` param missing);
  `Run_VimModeState_*` (4) RED via CS0246/CS0103 (`VimModeState` missing). RIGHT-REASON confirmed.
- **BUILD (build-agent):** BP-1 `LeaderResultKind.Failed` + `LeaderResult.ErrorMessage` + `action()`
  try/catch; BP-2 `leader-binding failed:` / `shortcut-binding failed:` logs (executed: lines
  byte-identical); BP-3 `textInputSurfaceFocused` param on both FocusGuard methods (leak case FALSE,
  D4 case TRUE); BP-4 `WindowManager.TextInputSurfaceFocused` signal (UI-thread); BP-5
  `EditorFocusedVeto` re-derived + 4 call sites wired; BP-6 `VimModeState` (pure owner, constants
  Normal/Insert/Replace, ResolveOnce latch-on-success); BP-7 `VimModeTracker` refactor (single
  `_state` owner, focus-guarded clears, `GetVim()` via ResolveOnce). NeoVisual 109/109, Telescope
  104/104, build pass.
  **3 DEVIATIONS adjudicated ACCEPT (hub):** (1) `GetVim()` returns the locally-resolved `vim`
  variable (the `_vim` field was removed per plan; latch semantics identical); (2) `OnViewGotFocus`
  buffer-null logs `vim-mode=Unknown` (exactly per plan; within the documented contract); (3)
  Telescope.Tests transient 103/1 on the build-agent's first run NOT reproducible across 7 subsequent
  runs (unrelated to Phase 6).
- **VERIFY (verification-agent):** GREEN (first dispatch returned empty — re-dispatched once fresh).
  check-doc-refs PASS (0 unresolved); NeoVisual 109/109 (7 new GREEN + LeaderMatcher 8/8 + FocusGuard
  15/15), Telescope 104/104 (no flake reproduced); `vim-mode=` + `executed:` lines byte-identical;
  `failed:` lines added; all fix checks confirmed; failure-log triage 6 read / 0 actionable.
- `delegations: 4 | VS boots: 0 | iterations: 0`

### Phase 7 — Logging pipeline (M18, M27, M35, M43, m13, m20, m23, m24, m25) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_LogFileWriter_FlushTimer_*` (2) RED via CS0117 (`FlushCount` missing);
  `Run_LogFileWriter_WriteFailureCount*` (2) RED via CS0117 (`WriteFailureCount` missing);
  `Run_PaneFailureTracker_*` (2) RED via CS0246 (`PaneFailureTracker` missing);
  `Run_FinderBase_OpenErrorSingleLog` RED at runtime (2 lines today — the double-log);
  `Run_FinderBase_GatherErrorPrefixed` PASSES today (a contract-pinning guard, not a RED — anticipated).
  RIGHT-REASON confirmed.
- **BUILD (build-agent):** BP-1 `WriteTo` shared body; BP-2 one-shot flush timer + `FlushCount`; BP-3
  `WriteFailureCount` + `neovisual-write-failed` sentinel; BP-4 `PaneFailureTracker` + one-time pane
  fallback via `LogFileWriter.Write` (never `Log`) + `PaneSync` lock; BP-5 6 finder-error sites routed
  to `TelescopeLog.Log` + `GatherErrorLiteral` unprefixed + `OnSelected` single-log + `FinderNameForErrors`
  deleted; BP-6 `NeoVisualLog.Debug` deleted + 29 sites switched to `Log` + `NeoVisualTraceListener.WriteLine`
  collapsed; BP-7 `Clear()` removed from `ShowOverlay` + doc fixed; BP-8 suffix semantics documented +
  stale comment fixed; BP-9 m25 no-op. Telescope 112/112, NeoVisual 109/109, build pass.
  **2 DEVIATIONS adjudicated ACCEPT (hub):** (1) stale line numbers vs mid-refactor source — all sites
  located by symbol, no diagnostic format changed; (2) `reason` literal `"pane unavailable"` (plan didn't
  specify the exact literal; the RED test pins only the format).
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); Telescope 112/112 (8 new
  GREEN), NeoVisual 109/109; `Run_LogPrefixes_Pinned` GREEN; `output pane unavailable:` added (one-time);
  all fix checks confirmed; failure-log triage 6 read / 0 actionable.
- `delegations: 3 | VS boots: 0 | iterations: 0`

### Phase 8 — Duplication merges (M21-M30) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_KeyNames_*` (3) RED via CS0103 (`KeyNames` missing);
  `Run_FocusKeeperSchedule_TruthTable` RED via CS0103 (`FocusKeeperSchedule` missing);
  `Run_TryDispatch_*` (4) RED via CS0103 (`TryDispatch` missing); `Run_BlockCaretStyle_*` (2) RED via
  CS0103 (`BlockCaretStyle` missing); `Run_HitOpener_*` (3) RED (`HitOpener` missing);
  `Run_HierarchyWalker_*` (6) RED via CS0246 (`IHierarchyNode` missing);
  `Run_ActionsRegistry_TelescopeKeysMatchFinderNames` PASSES today (behavior-preserving; real gate is
  build + strengthened test). RIGHT-REASON confirmed. Builder added WPF refs to Telescope.Tests.csproj
  (needed for `Colors`/`Key`).
- **BUILD (build-agent, 3 dispatches — 2 budget-exhausted with partial status, 1 garbage final message
  but work complete):** BP-1 `KeyNames` shared (KeyNameBuilder + LeaderSequenceMatcher route through it);
  BP-2 `HierarchyWalker` + `ProjectFiles.Enumerate` reuse (FileFinder + MyExtensionPackage; deleted
  FindFirstSourceFileInItems/CollectProjectFiles/CollectItems); BP-3 `HitOpener` + `OpenHitAtLine`
  (deleted OpenReference/OpenImplementation); BP-4 `TryDispatch` (incl. `$` drift fix) + overlay
  delegation; BP-5 `BlockCaretStyle` shared by 3 consumers; BP-6 `FocusKeeper` + `FocusKeeperSchedule`
  (elapsed-wins precedence) + SolutionExplorerController Sites 1+2; BP-7 `TempDir`/`WithLogPath`/
  `WithDebugLogPath` in TestRunner + 42 temp-dir blocks converted + MapMotion duplicates deleted (−2);
  BP-8 `Actions.Registry` built from `TelescopeLauncher.FinderNames`; BP-9 harness consolidation
  (`Open-TelescopeFinder` + shim + `Ensure-SolutionExplorerOpen` + `$script:SeedCanonical` + `-SelfCheck`)
  + `Wait-LogLine` core. Telescope 127/127, NeoVisual 112/112, build pass, `-SelfCheck` PASS.
  **4 DEVIATIONS adjudicated ACCEPT (hub):** (1) `Assert.True/False` overloads added to TestRunner;
  (2) `FocusKeeperSchedule.Decide` elapsed-wins precedence fixed; (3) `<InternalsVisibleTo
  Include="NeoVisual.Tests" />` added to Telescope.csproj (shared TestRunner helpers reference
  `LogFileWriter`); (4) plan's absolute counts (84/81) stale — actual baseline Telescope 127 / NeoVisual
  114, relative signals (unchanged / −2) confirmed.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved — hub fixed the m10 `.cs`
  backtick drift); `-SelfCheck` exit 0; Telescope 127/127, NeoVisual 112/112 (the −2 M29 signal); all
  targeted subsets GREEN (KeyNames 3 / FocusKeeperSchedule 1 / ActionsRegistry 5 / TryDispatch 4 /
  BlockCaretStyle 2 / HitOpener 3 / HierarchyWalker 6); no diagnostic drift; all fix checks confirmed;
  failure-log triage 6 read / 0 actionable.
- `delegations: 5 | VS boots: 0 | iterations: 0`

### Phase 9 — Testability seams (M31, M32, M33, M34) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_SimpleShortcutMatcher_*` (7) RED via CS0246/CS0103
  (`SimpleShortcutMatcher`/`SimpleShortcutResultKind` missing); `Run_VimModeClassifier_*` (5) +
  `Run_VimModeSource_Fake*` (2) RED via CS0246/CS0103/CS1729 (`VimModeClassifier`/`IVimModeSource`
  missing + `VimModeTracker` has no `IVimModeSource` ctor); `Run_InitSteps_*` (3) RED via CS0246
  (`InitSteps` missing); `Run_FocusTarget_*` (6) RED via CS0246/CS0103/CS0117 (`FocusTargetModel`/
  `FocusTarget`/`FocusTargetAction` missing + `OverlayKey.CtrlH/CtrlL` missing). RIGHT-REASON confirmed.
- **BUILD (build-agent, 2 dispatches — 1 garbage final message but work complete, 1 verification):**
  BP-2 `SimpleShortcutMatcher` (uses shared `KeyNames.ToString`); BP-3 `InputHandler` delegation +
  `BuildSimpleKey` deleted; BP-5 `VimModeClassifier` + `IVimModeSource` + `VsVimModeSource` (moved
  reflection interop, `_resolved` latch-on-success); BP-6 `VimModeTracker` thin adapter (MEF exports
  kept, `UpdateTypingFromMode` name kept, `vim-mode=` log owned by tracker, `VimModeState` delegates to
  `VimModeClassifier`); BP-8 `InitSteps`; BP-9 `MyExtensionPackage` per-step orchestration (`init <step>
  ok/failed:` + `init failed: {ex}` net; session started/shell initialized/auto-opened solution
  byte-identical); BP-11 `OverlayKey.CtrlH/CtrlL` + `MapKey`; BP-12 `FocusTargetModel`; BP-13
  `TelescopeOverlay` delegation. Telescope 133/133, NeoVisual 127/129 (2 `VimModeSource` failures).
  **1 DEVIATION adjudicated ACCEPT (hub):** `SimpleShortcutMatcher` adds a `Failed` kind + executes the
  action inside `HandleKey` — required by the plan's own BP-1 test (`executed == 1` after only
  `HandleKey`); the plan's BP-2/BP-3 snippets were internally inconsistent; `shortcut-binding executed:`
  format byte-identical.
- **DEBUG (debug-agent):** the 2 `Run_VimModeSource_Fake*` failures were a test-infra issue —
  `ITextView` lives in `Microsoft.VisualStudio.Text.UI.dll` (NOT in `Shell.Framework` as the plan
  claimed). Added `Microsoft.VisualStudio.Text.UI` + `Microsoft.VisualStudio.Text.UI.Wpf` 17.14.249 to
  `tests/NeoVisual.Tests/NeoVisual.Tests.csproj` (the sibling Wpf package was needed for
  `VimModeTracker`'s `IWpfTextViewCreationListener`). NeoVisual 129/129, Telescope 133/133.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); NeoVisual 129/129 (17 new
  GREEN + `Run_VimModeState_*` 4/4 GREEN), Telescope 133/133 (6 new GREEN); diagnostic contract
  byte-identical (`vim-mode=`, `shortcut-binding executed:`, `focus target=`) + `init <step>` added +
  session started/shell initialized/auto-opened solution unchanged; all fix checks confirmed;
  failure-log triage 6 read / 0 actionable.
- `delegations: 5 | VS boots: 0 | iterations: 0`

### Phase 10 — Harness hardening (M19, M36, M37, m26-m29) — attempt 1 GREEN

- **RED (e2e-test-builder, harness-only lane):** 10A `dte-command.ps1 -SelfTest` → param-not-found
  (no `-SelfTest` switch/`Invoke-DteWithTimeout`); 10B `iterate-telescope.ps1 -SelfCheck` → silently
  ignored (simple script) + ran the VS body + failed at DTE connect (no `-SelfCheck` switch, no
  `Stop-VsPids`/`Stop-SpawnedVs`/`Stop-HarnessVs`/`Add-SpawnedVs`/`Save-AllDocuments`, blanket kills,
  User-scope env write); 10C `Assert-Budget`/`Get-LogCacheIndex` absent from harness-common.ps1; 10D
  `check-doc-refs.ps1 -SelfCheck` → param-not-found (no `-SelfCheck` switch, whole-file regex, silent
  missing-doc skip). Real-doc-set baseline = **0 unresolved** (the plan's stale "7" was resolved by
  the hub in Phase 0). RIGHT-REASON confirmed. NOTE: the 10B RED inadvertently booted a main VS (killed
  + env restored by the builder).
- **BUILD (build-agent, 2 dispatches — 1 budget-exhausted with partial status, 1 completion):**
  BP-1/2/3 `dte-command.ps1` `Invoke-DteWithTimeout` runspace wrapper + 3 COM sites wrapped +
  `-SelfTest` (PASS); BP-4/5/6/7 `iterate-telescope.ps1` PID-scoped kills (`Stop-VsPids`/
  `Stop-SpawnedVs`/`Stop-HarnessVs`/`Add-SpawnedVs`/`Save-AllDocuments`) + Process-scope env +
  `-SelfCheck` (PASS); BP-8 always-reset seeding + seed determinism; BP-9/10/11 `Assert-Budget` +
  loop enforcement (before the `try`) + `RESULT: TIMEOUT` + `-SelfCheck`; BP-12/13 `Get-LogCacheIndex`
  + cache-side absence scan + `-SelfCheck`; BP-14/15 `check-doc-refs.ps1` definition-anchored regex +
  non-fatal missing-doc warning; BP-16 `-SelfCheck`. test-e2e.ps1 edited but NOT run (per user mandate).
  **3 DEVIATIONS adjudicated ACCEPT (hub):** BP-16's `Write-Step`→`Write-Host` one-liner was
  insufficient — 3 latent defects in the SelfCheck block fixed (undefined `Write-Pass` → self-contained
  `Write-Host`; the doc loop's `Join-Path $repoRoot $docRel` concatenating absolute paths → rooted-path
  handling). All within `check-doc-refs.ps1`, no diagnostic/doc-content change.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); `dte-command.ps1
  -SelfTest` PASS; `iterate-telescope.ps1 -SelfCheck` PASS (no devenv booted); `check-doc-refs.ps1
  -SelfCheck` PASS; harness-common standalone (Assert-Budget + Get-LogCacheIndex) all pass; all grep +
  parse checks confirmed; failure-log triage 6 read / 0 actionable.
- `delegations: 4 | VS boots: 0 | iterations: 0`

### Phase 11 — Test hermeticity (M20, M42, m30) — attempt 1 GREEN

- **RED (e2e-test-builder):** `Run_LogFileWriter_ClearPerPath` RED (pathB not empty after second
  Clear — once-per-process flag) + `Run_LogFileWriter_WritesAndClearsFile` RED in the full subset
  (order-dependence); `Run_Keybinding_DefaultFileHasTelescopeAndNav` → `LoadDefaults()` RED via CS0117;
  `Run_FzfFilter_FilterMatchesPrefix` fail-loud guard applied (fzf on PATH here → passes; RED is
  conditional on fzf absence); M42 renames applied + MapMotion deletion a no-op (M29 already removed
  them); m30 constant applied. RIGHT-REASON confirmed.
- **BUILD (build-agent):** BP-1 `LogFileWriter.Clear()` per-path idempotent (`_clearedPaths` HashSet +
  `ClearFileOnce`; Phase 7 state preserved — `_flushTimer` untouched, `WriteFailureCount`/`FlushCount`
  intact, `HH:mm:ss.fff` format byte-identical) + 2 obsolete NOTE blocks reworded; BP-2 fail-loud
  verified; BP-3 `KeybindingConfig.LoadDefaults()` (embedded-only, no user-file merge); BP-4 renames
  verified + deletion no-op; BP-5 constant verified. Telescope 134/134, NeoVisual 129/129, build pass.
  No DEVIATIONS.
- **VERIFY (verification-agent):** GREEN. check-doc-refs PASS (0 unresolved); Telescope 134/134 (11
  LogFileWriter GREEN + FzfFilter GREEN), NeoVisual 129/129 (Keybinding 9/9 + ToolWindowMode 3/3 +
  FocusGuard 15/15); no diagnostic literal changed; all fix checks confirmed; failure-log triage 6
  read / 0 actionable.
- `delegations: 3 | VS boots: 0 | iterations: 0`

### Phase 12 — Naming/convention sweep (M38-M41, n1-n12) — attempt 1 GREEN (FINAL GATE)

- **RED (e2e-test-builder):** `Run_Direction_ToChar` RED via CS1061 (`ToChar` missing);
  `Run_GrepFinder_GatherHitsThrowsNotSupported` RED at runtime (no `NotSupportedException`);
  `LineStartHome()`→`LineStart()` at 2 call sites RED via CS1061; `SyntaxHighlighter.Segment`→`Tokenize`
  at 8 call sites RED via CS0117. RIGHT-REASON confirmed.
- **BUILD (build-agent, 2 dispatches — 1 garbage final message but work complete, 1 verification):
  BP-1 `WindowManager` wrapped in `namespace MyExtension`; BP-2 `PerpendicularAxis`; BP-3
  `NavigateInDirection(Direction)` + `ToChar()` single-char token + 4 char constants deleted + Actions
  sites updated; BP-4 `_activeWindows`/`_activeWindow` renames; BP-5 `GatherHits()` throws
  `NotSupportedException`; BP-6 `_keyboardHook`; BP-7 `GetCaretOffset` `[NeoVisual]` prefix; BP-8 VK
  interpolation; BP-9 real pane GUID + named CreatePane args; BP-10 `neovascular`→`neovisual` comments;
  BP-11 named VK constants; BP-12 `expHive`/`Find-VsWindow` deleted; BP-13 `_rect` conditional (already
  gone — M2); BP-14 `LineStart()` + private `LineStart(int)` both kept; BP-15 `Tokenize` + `_keywords`;
  BP-16 `Split('\n')` scan; BP-17 doc-ref updates. NeoVisual 130/130, Telescope 135/135, build pass.
  No DEVIATIONS.
- **VERIFY (verification-agent, FINAL GATE):** GREEN. check-doc-refs PASS (0 unresolved);
  `dte-command.ps1 -SelfTest` PASS; `iterate-telescope.ps1 -SelfCheck` PASS (no devenv booted);
  `check-doc-refs.ps1 -SelfCheck` PASS; NeoVisual 130/130 (FULL), Telescope 135/135 (FULL); diagnostic
  contract byte-identical (`navigate direction=L/R/D/U` single-char, `grep hits=`, `preview tokens=`,
  `toolwindow-move vk=40/38`, `[MyExtension]` auto-open lines); all fix checks + grep gates confirmed;
  failure-log triage 6 read / 0 actionable.
- `delegations: 4 | VS boots: 0 | iterations: 0`

---

## ITEM COMPLETE — Code review findings (75 findings, 12 phases) — GREEN 2026-09-30

All 12 phases GREEN in the unit-only lane (e2e deferred to `docs/e2e-queue.md`). Final suite counts:
**Telescope.Tests 135, NeoVisual.Tests 130** (both 0 failed). `dotnet build` 0 errors. All harness-health
self-checks PASS (check-doc-refs 0 unresolved; dte-command -SelfTest; iterate-telescope -SelfCheck;
check-doc-refs -SelfCheck). No e2e harness commands were run (user mandate — VS-capable machine required;
the correct machine runs VS 2026, this machine has VS 2022).



