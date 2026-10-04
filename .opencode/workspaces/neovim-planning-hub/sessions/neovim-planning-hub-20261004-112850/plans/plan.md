# Plan — Gap 3: Diagnostics navigation (`]`/`[` prefix — next/prev diagnostic, error, warning)

> **Lane: feature (unit-only, e2e deferred).** Adds new capability AND new `[NeoVisual]`
> diagnostics for the custom severity-filtered navigator → **M-M7 trigger** (feature lane,
> full pipeline). E2E is DEFERRED to the e2e queue (user mandate 2026-10-04: never boot the
> VS Experimental Instance until the user says so). RED is proven at the unit level only.
>
> **Source:** `docs/progress.md` item 7 (Gap 3, triage EXTEND/REUSE native 2026-09-28) + the
> user's 2026-10-04 scope answer: *"check if there is a way we can have ]w [w (for warnings)
> and [e ]e (for errors) if not try to find a way to create that functionality"* — native VS
> has NO severity-specific commands (researcher-verified), so the severity pairs are BUILT.
>
> **Ground truth:** repo GREEN after Gap 1 (2026-10-04). Unit suites: `tests/Telescope.Tests`
> **172 passed**, `tests/NeoVisual.Tests` **177 passed**. Live E2E: **38 registered** (37 GREEN
> + `neovisual-window-management` registered-unexecuted, queued as E2E-GAP1-1). This plan adds
> ONE new registered scenario (`neovisual-diagnostic-nav`) → **39 registered** at GREEN
> (37 GREEN + 2 registered-unexecuted). No existing scenario is reworked (additive only).
>
> **Research:** `feature-researcher` verified from Microsoft Learn default-keyboard-shortcuts
> (HIGH confidence): `Edit.GotoNextIssueinFile` (Alt+PgDn) / `Edit.GotoPreviousIssueinFile`
> (Alt+PgUp) navigate the next/previous issue (error or warning squiggle) **in the current
> file** — the closest native analog of LazyVim `]d`/`[d`. `Edit.GoToNextLocation` (F8) /
> `Edit.GoToPrevLocation` (Shift+F8) navigate Error List/Output entries solution-wide.
> **`Edit.NextError`/`Edit.PreviousError` (the backlog's 2026-09-28 assumption) are NOT
> verifiable in modern VS** — treat as legacy, do not bind. NO severity-specific commands
> exist natively (no next-warning/next-error-only). `trailmark-recon` verified: `]`
> (`Keys.OemCloseBrackets`) / `[` (`Keys.OemOpenBrackets`) have NO `KeyNames` case today
> (they build `"OemCloseBrackets"`/`"OemOpenBrackets"` → abort), ZERO collisions anywhere
> (bindings, harness assertions, test fixtures), the pre-filter passes any key while a leader
> sequence is active (`InputHandler.IsKeyOfInterest` :435-438), and the two-arg shift-aware
> overload delegates non-letters shift-insensitively — so `]`/`[` need only single-arg cases.

## Goal

Give the editor LazyVim-style diagnostics navigation under a `]`/`[` leader prefix:
`],d`/`[,d` next/previous diagnostic (any severity, in-file — native VS commands), and
`],e`/`[,e` + `],w`/`[,w` next/previous **error**/**warning** (severity-filtered, in-file —
a custom navigator built on the repo's established Error-List pattern, since native VS has no
severity-specific commands). `],w`-for-messages is out of scope.

## Approach

### Part 1 — Bracket key names + the six bindings (additive)

**D1 — `KeyNames` gains two printable cases.** In the single-arg `ToString(Keys)` switch
(current cases: `OemQuestion`→`/`, `Oemplus`→`+`, `OemMinus`→`-`, `OemPipe`→`|`), add:

```csharp
case Keys.OemCloseBrackets: return "]";   // 221 (0xDD); Shift+OemCloseBrackets types '}'
case Keys.OemOpenBrackets: return "[";    // 219 (0xDB); Shift+OemOpenBrackets types '{'
```

The two-arg overload needs NO change (it delegates non-letters to the single-arg form,
shift-insensitively — recon-verified). Known ambiguity (same class as `|`/`\` from Gap 1,
documented in KeyNames): an unshifted-vs-shifted bracket pair collapses to one sequence name;
no `}`/`{` bindings exist or are planned.

**D2 — Six new leader bindings in `default-keybindings.json`** (additive; 30 → 36 bindings):

```json
"],d": "command:Edit.GotoNextIssueinFile",
"[,d": "command:Edit.GotoPreviousIssueinFile",
"],e": "next-error",
"[,e": "prev-error",
"],w": "next-warning",
"[,w": "prev-warning"
```

`],d`/`[,d` are pure `command:` bindings (native, zero code). `],e`/`[,e`/`],w`/`[,w` are NEW
built-in actions (D3-D5). The `]`/`[` keys are prefixes (no bare `]`/`[` binding is added —
the matcher's complete-match-before-prefix check means a bare binding would shadow the pairs).

### Part 2 — Custom severity-filtered navigation (the BUILD)

**D3 — Pure seam `DiagnosticNavigator`** (the `CloseWindowCommand`/`OverlayKeyHandler`
pattern — dependency-free, unit-testable). New file
`MyExtension/Input/Utils/DiagnosticNavigator.cs`:

```csharp
namespace MyExtension.Input
{
    /// <summary>One navigable diagnostic entry (file path + 1-based line).</summary>
    internal readonly record struct DiagnosticEntry(string FilePath, int Line);

    /// <summary>
    /// Pure severity-filtered diagnostics navigation (LazyVim `]e`/`[e`/`]w`/`[,w`): given the
    /// diagnostic entries for ONE severity in the CURRENT file, ordered by line, find the next
    /// (or previous) entry relative to the caret line. In-file only (LazyVim buffer-local
    /// semantics); NO wrap — returns null at the end so the caller logs a no-op.
    /// </summary>
    internal static class DiagnosticNavigator
    {
        internal static DiagnosticEntry? Next(IReadOnlyList<DiagnosticEntry> entries, int caretLine)
            => Select(entries, caretLine, forward: true);

        internal static DiagnosticEntry? Prev(IReadOnlyList<DiagnosticEntry> entries, int caretLine)
            => Select(entries, caretLine, forward: false);

        private static DiagnosticEntry? Select(IReadOnlyList<DiagnosticEntry> entries, int caretLine, bool forward)
        {
            // entries are pre-sorted ascending by Line (the caller's gather contract).
            if (forward)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Line > caretLine) return entries[i];
                }
                return null;
            }
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Line < caretLine) return entries[i];
            }
            return null;
        }
    }
}
```

(net472 + C# 14: `readonly record struct` is available — LangVersion 14; if the planner finds
record structs unavailable in this project's settings, fall back to a plain readonly struct
with the same shape. Entries with `Line <= 0` are dropped by the caller's gather.)

**D4 — VS-coupled gather + action.** `InputHandler` gains ONE method with two captured
parameters wired through the `Actions` registry (the established factory pattern):

```csharp
/// <summary>
/// Severity-filtered diagnostics navigation (Gap 3): gathers the Error List entries for the
/// requested severity in the ACTIVE document, asks the pure DiagnosticNavigator for the
/// next/previous entry relative to the caret line, and opens it at its line. Logs the
/// `[NeoVisual] diagnostic-nav ...` contract line (outcome diagnostic, m47-style).
/// </summary>
internal void NavigateDiagnostic(bool forward, bool severityError)
{
    ThreadHelper.ThrowIfNotOnUIThread();
    // gather (Error List via the CodeIssuesFinder-established DTE API) + active doc/caret
    // + DiagnosticNavigator.Select + open-at-line + diagnostic-nav log line
}
```

- **Gatherer:** a thin VS-coupled helper (new `MyExtension/Package/Utils/ErrorListGatherer.cs`
  or inline in InputHandler — the planner decides) reading
  `dte2.ToolWindows.ErrorList.ErrorItems` — the EXACT API `CodeIssuesFinder.CollectErrorList`
  already uses (Telescope/Finders/CodeIssuesFinder.cs ~:170-205) — filtering to the active
  document's path + `vsErrorSeverity` (`vsErrorTypeError` / `vsErrorWarning`), returning
  `DiagnosticEntry` list sorted ascending by Line. The planner verifies the exact
  `ErrorItem` property names (`File`, `Line`, `Severity`) from the CodeIssuesFinder source.
- **Active position:** DTE active document (`dte.ActiveDocument.FullName`) + caret line
  (`TextSelection.ActivePoint.Line`) — UI thread.
- **Open-at-line:** the established pattern (the finders' `HitOpener`/`TextSelection.GotoLine`
  flow) — reuse or mirror; the planner picks the smallest reuse.
- **Registry:** 4 new entries in `Actions.BuildRegistry()` (12 → 16), closures capturing the
  two booleans:
  ```csharp
  ["next-error"]    = (h, _) => () => h.NavigateDiagnostic(forward: true,  severityError: true),
  ["prev-error"]    = (h, _) => () => h.NavigateDiagnostic(forward: false, severityError: true),
  ["next-warning"]  = (h, _) => () => h.NavigateDiagnostic(forward: true,  severityError: false),
  ["prev-warning"]  = (h, _) => () => h.NavigateDiagnostic(forward: false, severityError: false),
  ```

**D5 — NEW diagnostic contract (M-M7).** The action logs an outcome diagnostic (m47-style —
fired vs no-op and why), exactly one new literal family:

```
[NeoVisual] diagnostic-nav direction=next|prev severity=error|warning target=<file> line=<n>
[NeoVisual] diagnostic-nav no-op: <reason>        (reason: no-entries | at-end | no-active-document)
```

The gather failure path reuses the EXISTING `[NeoVisual] Command '...' failed: {msg}`-style
swallow discipline (never crashes the hook) — the planner pins the exact failure line format
(a `diagnostic-nav failed: {msg}` variant of the same family, or reuse of an existing failure
line — decide and document; M-M7 covers any new literal).

### Part 3 — Tests, harness, docs

**D6 — Unit tests (`tests/NeoVisual.Tests`; Telescope untouched).** RED-first:
`Run_DiagnosticNavigator_*` (pure seam: next/prev, boundaries, empty, at-end, unsorted-input
contract), `Run_Keybinding_DefaultFileHasDiagnosticNav` (the six bindings + values),
`Run_KeyNames_PrintableMappings` extend (`]`/`[`), `Run_KeyNames_RoundTrip_DiagnosticNav`
(matcher round-trip for all six sequences), `Run_ActionsRegistry_ContainsAllBuiltins`
(12 → 16). Expected suite total: 177 + new tests (the planner computes the exact count).

**D7 — Harness: ONE new registered scenario `neovisual-diagnostic-nav`** (specified, never
executed — queued as E2E-GAP3-1). Asserts the six `leader-binding executed:` lines; the
custom-nav assertions depend on the new `diagnostic-nav` lines (the seeded scratch solution
plus a deliberate build/analysis state determines Error List contents — the scenario asserts
the diagnostic lines, not specific line numbers, to stay deterministic). No existing scenario
changes (additive only). No-VS gates: `-SelfCheck` + `-List` (39 scenarios).

**D8 — Docs sync.** `docs/spec.md` (§3 bindings + §4 new diagnostic lines + §5 counts +
§7 feature bullet), `AGENTS.md` (keybindings bullet + scenario list/count + unit count),
`SKILL.md` (binding list + counts), `docs/progress.md` (item 7 → DONE at GREEN). The
DOC-66-3 lint reconciliation from the Gap 1 handoff note is carried by the build loop.

## Acceptance criteria (each mapped to a diagnostic + a test)

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | `Space ] d` fires next-diagnostic (native in-file squiggle nav) | `[NeoVisual] leader-binding executed: ],d` | unit `Run_Keybinding_DefaultFileHasDiagnosticNav` + `Run_KeyNames_RoundTrip_DiagnosticNav`; e2e `neovisual-diagnostic-nav` (QUEUED) |
| AC2 | `Space [ d` fires prev-diagnostic | `[NeoVisual] leader-binding executed: [,d` | same as AC1 |
| AC3 | `Space ] e` navigates to the next ERROR in the active file (severity-filtered, in-file, no wrap) | `[NeoVisual] leader-binding executed: ],e` + `[NeoVisual] diagnostic-nav direction=next severity=error target=<file> line=<n>` | unit `Run_DiagnosticNavigator_*` + `Run_ActionsRegistry_ContainsAllBuiltins`; e2e (QUEUED) |
| AC4 | `Space [ e` navigates to the previous ERROR | `... executed: [,e` + `diagnostic-nav direction=prev severity=error ...` | same as AC3 |
| AC5 | `Space ] w` / `Space [ w` navigate to the next/previous WARNING | `... executed: ],w`/`[,w` + `diagnostic-nav ... severity=warning ...` | same as AC3 |
| AC6 | At the end of the list (or no entries) the custom nav is a NO-OP that logs why (never crashes, never wraps) | `[NeoVisual] diagnostic-nav no-op: at-end` / `no-op: no-entries` | unit `Run_DiagnosticNavigator_*` (at-end/empty cases) |
| AC7 | The `]`/`[` keys are representable and unambiguous with existing bindings | (unit-only — KeyNames mapping) | unit `Run_KeyNames_PrintableMappings` (extend) + `Run_KeyNames_RoundTrip_DiagnosticNav` |
| AC8 | Existing bindings/scenarios are unregressed (additive change) | existing lines unchanged | unit full-suite gate; e2e full-suite re-run (QUEUED, E2E-GAP3-2) |

## Unit test plan

**Project: `tests/NeoVisual.Tests`** (Telescope untouched). The planner expands with exact
assertions; expected new/updated: `Run_DiagnosticNavigator_Next/Prev/Boundaries/Empty`
(new, RED — the type doesn't exist → CS0246), `Run_Keybinding_DefaultFileHasDiagnosticNav`
(new, RED — bindings absent), `Run_KeyNames_PrintableMappings` (extend, RED — `]`/`[` cases
absent), `Run_KeyNames_RoundTrip_DiagnosticNav` (new, RED), `Run_ActionsRegistry_ContainsAllBuiltins`
(update 12→16, RED). Suite arithmetic: 177 + N new = the planner pins the exact total.

## Diagnostics

**New literal family (M-M7):** `[NeoVisual] diagnostic-nav direction=... severity=... target=... line=...`
and `[NeoVisual] diagnostic-nav no-op: <reason>` (+ the planner-pinned failure variant).
Everything else reuses existing lines (`leader-binding executed:`, `Keybindings loaded: N binding(s)`).
The log-literal diff gate sees EXACTLY the new `diagnostic-nav` family — the plan must list
every new literal so the gate can approve them.

## Known-RED allowlist

**None.** Both unit suites GREEN (172/177), all 37 live e2e GREEN. Expected RED = the new
unit tests before the change exists. The new harness scenario is REGISTERED-unexecuted
(queued), not RED.

## E2E queue reference (deferred — queued at handoff)

| ID | Scenarios | What each asserts | Diagnostics |
|----|-----------|-------------------|-------------|
| E2E-GAP3-1 | `neovisual-diagnostic-nav` (**new**) | the six `leader-binding executed:` lines (`],d`/`[,d`/`],e`/`[,e`/`],w`/`[,w`) + the `diagnostic-nav` outcome lines for the custom pairs | `leader-binding executed: …` + `diagnostic-nav …` |
| E2E-GAP3-2 | full-suite re-run (39 registered) | no regression from the additive bindings | all existing lines unchanged |

## Files to be touched (initial estimate)

- **Modified:** `MyExtension/Input/Utils/KeyNames.cs` (D1), `MyExtension/Resources/default-keybindings.json`
  (D2), `MyExtension/Input/InputHandler.cs` (D4), `MyExtension/Package/Utils/Actions.cs` (D4),
  `tools/harness/test-e2e.ps1` (D7), `tests/NeoVisual.Tests/Program.cs` (D6), `docs/spec.md`,
  `AGENTS.md`, `.opencode/skills/vs-extension-dev/SKILL.md`, `docs/progress.md` (D8).
- **Created:** `MyExtension/Input/Utils/DiagnosticNavigator.cs` (D3), the gatherer (D4 —
  file placement the planner decides).
- **Not touched:** `LeaderSequenceMatcher.cs`, `KeybindingConfig.cs`, `KeyNameBuilder.cs`
  (Gap 1's matcher refactor already supports everything needed), `Telescope/**` (unless the
  planner reuses `CodeIssuesFinder`'s gather — read-only reuse of its API pattern, no Telescope
  edits), `GlobalKeyboardHook.cs`.

## Open risks / uncertainty

1. **Error List population (medium).** The custom nav reads the Error List, which is
   populated by builds/IntelliSense analysis — a fresh file's squiggles may not appear until
   analysis runs. The no-op diagnostic (`no-op: no-entries`) makes this observable, not
   silent. The native `],d` (squiggle-based) does not have this caveat — the asymmetry is
   documented.
2. **`ErrorItem` API shape (low).** The planner verifies `File`/`Line`/`Severity` property
   names from the CodeIssuesFinder source before writing the gatherer.
3. **`Edit.GotoNextIssueinFile` availability (low, HIGH-confidence verified).** If the command
   name were wrong, `ExecuteVsCommand` logs the existing `Command '...' failed` line and the
   key is swallowed — never a crash; the queued e2e gate proves it live.
4. **Bracket shift ambiguity (low).** `}`/`{` (Shift+brackets) build the same sequence names
   as `]`/`[` (non-letters are shift-insensitive) — no `}`/`{` bindings exist; documented in
   KeyNames.
5. **record struct on net472 (low).** LangVersion 14 supports it; the planner falls back to a
   plain readonly struct if the project settings disagree.

## Build Plan

> **Lane: feature (unit-only, e2e deferred).** Aggregated (Stage 3) from the five Stage-2
> section artifacts — the authoritative full detail (ground-truth tables, complete code,
> per-test snippets, enumerations) lives in
> `.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-112850/artifacts/section-{a,b,c,d,e}.md`;
> every step below carries its executable contract. **No RED evidence exists yet** —
> Verify-with cites unit test names + diagnostic formats only; e2e is QUEUED, never executed.
>
> **Plan-claim corrections folded in (binding — do NOT follow the plan's D3/D4 verbatim where
> corrected):** (1) `ErrorItem` surface is **`FileName`/`Line`/`ErrorLevel`/`Column`/
> `Description`** with **`vsBuildErrorLevel`** (`Low=1, Medium=2, High=4`) — NOT
> `File`/`Severity`/`vsErrorSeverity` (LSP-verified against decompiled interop 17.14; errors =
> `High`, warnings = `Medium`, per `CodeIssuesFinder.ClassifySeverity`); (2) `DiagnosticEntry`
> is a plain **`readonly struct`** (net472 has no `IsExternalInit`; zero repo record precedent)
> — NOT a record struct; (3) suite total pinned **187** (177 + 10 new); (4) scenario count
> **38 → 39 registered**.

### Execution order + mid-plan expectations (binding for the build agent)

1. Execute **A → B → C → D → E** top-to-bottom in one pass. B is buildable independently
   (BP-B1→B2→B3→B4 strictly in order — each references the previous step's symbols).
2. **Mid-plan inert states are EXPECTED, not defects:** after A alone, the four built-in
   bindings are dropped at load with the EXISTING `[NeoVisual] Unknown action 'next-error'
   for binding '],e' - ignored.` line (InputHandler.cs:179) until B's registry entries land;
   after B alone the registry entries are inert until A's JSON lands. At the aggregated GREEN
   all six are live and that line is GONE.
3. **Never run the e2e harness against VS** (user mandate). The only allowed harness
   invocations are no-VS `-SelfCheck` and `-List`.
4. `LoadDefaults()` (the unit seam) does NOT emit the `Keybindings loaded:` line — only
   `Load()` does at package init; the unit proof is the `Bindings.Count` assertion, the
   runtime proof is the load line (`KeybindingConfig.cs:105`).

### Phase 1 — Bracket keys + binding table (BP-A1 … BP-A3)

**BP-A1 — `KeyNames`: two printable bracket cases.**
- **Files:** `MyExtension/Input/Utils/KeyNames.cs`
- **Change:** in the single-arg `ToString(Keys)` switch, insert between the `OemPipe` case
  (:20) and `default:` (:21):
  ```csharp
  case Keys.OemCloseBrackets: return "]";   // 221 (0xDD); Shift+OemCloseBrackets types '}'
  case Keys.OemOpenBrackets: return "[";    // 219 (0xDB); Shift+OemOpenBrackets types '{'
  ```
  The two-arg overload (:33-41) needs NO change (delegates non-letters at :40).
- **Verify-with:** `Run_KeyNames_PrintableMappings` (extend, C): `ToString(OemCloseBrackets)=="]"`,
  `ToString(OemOpenBrackets)=="["`; `Run_KeyNames_RoundTrip_DiagnosticNav` (new, C). Diagnostic:
  `[NeoVisual] leader-binding executed: ],d` (etc.).
- **Fails-if:** the default branch is hit (`"OemCloseBrackets"`) → case missing/wrong
  overload/misspelled enum (CS0117); the round-trip ends `Abort`.

**BP-A2 — `default-keybindings.json`: six new leader bindings (30 → 36).**
- **Files:** `MyExtension/Resources/default-keybindings.json`
- **Change:** replace the file tail (lines 33-35) — ADD the comma after `"command:Debug.Start"`,
  then append:
  ```json
      "b,b": "command:Build.BuildSolution",
      "b,r": "command:Debug.Start",
      "],d": "command:Edit.GotoNextIssueinFile",
      "[,d": "command:Edit.GotoPreviousIssueinFile",
      "],e": "next-error",
      "[,e": "prev-error",
      "],w": "next-warning",
      "[,w": "prev-warning"
    }
  ```
  Hard constraints: the LAST entry has NO trailing comma (JavaScriptSerializer rejects it →
  `Failed to load built-in keybindings` and the ENTIRE default set is lost); the four action
  values are EXACTLY Section B's registry keys; the two `command:` values are the
  researcher-verified native names; **NO bare `]`/`[` binding** (the matcher's
  complete-match-before-prefix check would shadow every pair); keys lowercase; additive-only
  diff.
- **Verify-with:** `Run_Keybinding_DefaultFileHasDiagnosticNav` (new, C): `LoadDefaults()`
  count 36 + the six exact pairs + the bare-`]`/`[` prefix-trap guard;
  `Run_Keybinding_DefaultFileHasTelescopeAndNav`/`...WindowManagement` stay GREEN. Runtime:
  `[NeoVisual] Keybindings loaded: 36 binding(s), leader = Space (user file: none)`.
- **Fails-if:** the load line still reads 30 (stale build — EmbeddedResource, rebuild);
  `Failed to load built-in keybindings` (JSON syntax); the Unknown-action line persists AFTER
  B (name contract broken); the count is 30/35 (edit landed outside the object).

**BP-A3 — Integration gate (read-only): the A ↔ B name contract.**
- **Files:** none. Cross-check: the four JSON values ≡ `Actions.BuildRegistry()` keys
  character-for-character; the two `command:` values exact; no bare `]`/`[` key; `KeyNames`
  has exactly the two new cases + the four originals + one `default` + untouched two-arg
  overload.
- **Verify-with:** `Run_ActionsRegistry_ContainsAllBuiltins` (16) +
  `Run_Keybinding_DefaultFileHasDiagnosticNav`; runtime: all six `leader-binding executed:`
  lines and NO `Unknown action ...` line at load.
- **Fails-if:** the Unknown-action line persists after B (fix whichever side drifted); the
  lines never appear though both unit tests pass (a user `%APPDATA%` override is shadowing —
  environment, not a code defect).

### Phase 2 — Custom severity-filtered navigator (BP-B1 … BP-B5)

**BP-B1 — NEW pure seam `DiagnosticNavigator` (+ `DiagnosticEntry`).**
- **Files:** `MyExtension/Input/Utils/DiagnosticNavigator.cs` (NEW)
- **Change:** create with EXACTLY (plain `readonly struct` — pinned; namespace
  `MyExtension.Input` like `CloseWindowCommand.cs`):
  ```csharp
  using System.Collections.Generic;

  namespace MyExtension.Input
  {
      /// <summary>
      /// One navigable diagnostic entry (file path + 1-based line). A plain readonly struct (NOT
      /// a record struct): net472 has no IsExternalInit, record positional properties are
      /// init-only, and this repo has no IsExternalInit polyfill (section-b.md, plan-claim
      /// correction 2).
      /// </summary>
      internal readonly struct DiagnosticEntry
      {
          internal DiagnosticEntry(string filePath, int line)
          {
              FilePath = filePath;
              Line = line;
          }

          /// <summary>Full path of the file the diagnostic points at.</summary>
          internal string FilePath { get; }

          /// <summary>1-based line number (always &gt; 0 — the gatherer drops Line &lt;= 0).</summary>
          internal int Line { get; }
      }

      /// <summary>
      /// Pure severity-filtered diagnostics navigation (LazyVim <c>]e</c>/<c>[e</c>/<c>]w</c>/<c>[,w</c>):
      /// given the diagnostic entries for ONE severity in the CURRENT file, ordered by line, find
      /// the next (or previous) entry relative to the caret line. In-file only (LazyVim
      /// buffer-local semantics); NO wrap — returns null at the end so the caller logs a no-op.
      /// Dependency-free static seam (the CloseWindowCommand/OverlayKeyHandler pattern): the
      /// VS-coupled caller delegates to it so the selection logic stays unit-testable.
      /// </summary>
      internal static class DiagnosticNavigator
      {
          /// <summary>
          /// Returns the first entry strictly AFTER the caret line, or null when none. CONTRACT:
          /// <paramref name="entries"/> are pre-sorted ascending by Line (the caller's gather
          /// contract — this seam does NOT sort; on unsorted input it scans in list order).
          /// </summary>
          internal static DiagnosticEntry? Next(IReadOnlyList<DiagnosticEntry> entries, int caretLine)
              => Select(entries, caretLine, forward: true);

          /// <summary>
          /// Returns the last entry strictly BEFORE the caret line, or null when none. Same
          /// pre-sorted contract as <see cref="Next"/>.
          /// </summary>
          internal static DiagnosticEntry? Prev(IReadOnlyList<DiagnosticEntry> entries, int caretLine)
              => Select(entries, caretLine, forward: false);

          private static DiagnosticEntry? Select(IReadOnlyList<DiagnosticEntry> entries, int caretLine, bool forward)
          {
              if (forward)
              {
                  for (int i = 0; i < entries.Count; i++)
                  {
                      if (entries[i].Line > caretLine)
                      {
                          return entries[i];
                      }
                  }
                  return null;
              }

              for (int i = entries.Count - 1; i >= 0; i--)
              {
                  if (entries[i].Line < caretLine)
                  {
                      return entries[i];
                  }
              }
              return null;
          }
      }
  }
  ```
  Semantics pinned: STRICT comparisons (`>`/`<`) — standing ON a diagnostic and pressing `]e`
  moves to the NEXT one (vim repeat); caret above the first → `Next` returns it; caret below
  the last → `Prev` returns it; at/after the last (Next) and at/before the first (Prev) →
  null (`no-op: at-end`); empty list → null; NO internal sort (the gatherer owns sorting —
  pinned by the unsorted-contract test).
- **Verify-with:** the 8 `Run_DiagnosticNavigator_*` tests (Section C; RED = CS0246 until this
  file exists). No diagnostic literal (pure seam).
- **Fails-if:** CS0246/CS0234 (file missing / wrong namespace / dropped `using`); the CURRENT
  line returned (`>=` instead of strict `>`); wrap-around; a sort inside the seam; any
  `ThreadHelper`/DTE reference (must stay dependency-free).

**BP-B2 — NEW VS-coupled gatherer `ErrorListGatherer`.**
- **Files:** `MyExtension/Package/Utils/ErrorListGatherer.cs` (NEW; placement per the
  `Package/Utils` VS-coupled-helper convention)
- **Change:** create with EXACTLY (the `CodeIssuesFinder.CollectErrorList` API shape —
  `FileName`/`Line`/`ErrorLevel`/`vsBuildErrorLevel`):
  ```csharp
  using EnvDTE;
  using EnvDTE80;
  using Microsoft.VisualStudio.Shell;
  using MyExtension.Input;
  using System;
  using System.Collections.Generic;
  using System.Linq;

  namespace MyExtension.Package
  {
      /// <summary>
      /// VS-coupled gatherer for the severity-filtered diagnostics navigation (Gap 3): reads the
      /// VS Error List through the exact DTE2 API the Telescope CodeIssuesFinder established
      /// (Telescope/Finders/CodeIssuesFinder.cs:170-205), filters to ONE file + ONE severity, and
      /// returns DiagnosticEntry rows under the gather contract DiagnosticNavigator relies on:
      /// 1. severity filter — ErrorItem.ErrorLevel (vsBuildErrorLevel): errors =
      ///    vsBuildErrorLevelHigh, warnings = vsBuildErrorLevelMedium (messages/Low excluded);
      /// 2. file filter — ErrorItem.FileName equals the requested path (OrdinalIgnoreCase);
      /// 3. entries with Line &lt;= 0 dropped;
      /// 4. sorted ascending by Line (stable), duplicate lines collapsed to the FIRST Error List
      ///    item — repeated navigation never re-lands on the same line.
      /// Per-item read failures are skipped (the CodeIssuesFinder discipline); an outer read
      /// failure PROPAGATES so the caller logs <c>diagnostic-nav failed: {msg}</c>.
      /// <para/>
      /// <b>Threading:</b> UI thread only (DTE/COM).
      /// </summary>
      internal static class ErrorListGatherer
      {
          public static List<DiagnosticEntry> Gather(DTE dte, string filePath, bool severityError)
          {
              ThreadHelper.ThrowIfNotOnUIThread();

              var entries = new List<DiagnosticEntry>();
              var dte2 = dte as DTE2;
              ErrorItems? items = dte2?.ToolWindows.ErrorList.ErrorItems;
              if (items == null)
              {
                  return entries;
              }

              var wanted = severityError
                  ? vsBuildErrorLevel.vsBuildErrorLevelHigh
                  : vsBuildErrorLevel.vsBuildErrorLevelMedium;

              int count = items.Count;
              for (int i = 1; i <= count; i++)
              {
                  try
                  {
                      ErrorItem item = items.Item(i);
                      if (item.ErrorLevel != wanted)
                      {
                          continue;
                      }
                      string fileName = item.FileName ?? string.Empty;
                      if (fileName.Length == 0 ||
                          !string.Equals(fileName, filePath, StringComparison.OrdinalIgnoreCase))
                      {
                          continue;
                      }
                      if (item.Line <= 0)
                      {
                          continue;
                      }
                      entries.Add(new DiagnosticEntry(fileName, item.Line));
                  }
                  catch
                  {
                      // skip an item that can't be read (same discipline as CodeIssuesFinder)
                  }
              }

              return entries
                  .OrderBy(e => e.Line)
                  .GroupBy(e => e.Line)
                  .Select(g => g.First())
                  .ToList();
          }
      }
  }
  ```
- **Verify-with:** `dotnet build` 0 errors (proves the corrected `ErrorItem` API claims);
  runtime proof is the QUEUED E2E-GAP3-1; indirect unit proof via the seam's gather contract.
  No offline unit test (COM-coupled by design — same policy as `VsServices`).
- **Fails-if:** CS0117/CS0103 on `File`/`Severity`/`vsErrorSeverity` (the WRONG plan-D4 names
  were used); `OrderBy`/`GroupBy` build error (`using System.Linq;` dropped); CS0246
  `DiagnosticEntry` (BP-B1 not first); runtime `no-op: no-entries` with a populated Error List
  (severity enum swapped / case-sensitive path compare); the same line revisited on repeated
  `]e` (dedup dropped); `failed:` on EVERY press (DTE2 cast or ErrorList chain wrong).

**BP-B3 — `InputHandler.NavigateDiagnostic(bool forward, bool severityError)`.**
- **Files:** `MyExtension/Input/InputHandler.cs`
- **Change:** insert ONE method immediately AFTER `CloseWindow()` (between :570 and :571),
  EXACTLY (no new `using` — `EnvDTE.TextSelection` and `Telescope.Finders.DteFileOpener` are
  fully qualified per the file's style; `DiagnosticEntry`/`DiagnosticNavigator` are in this
  namespace; `ErrorListGatherer`/`VsServices` via the existing `using MyExtension.Package;`;
  `NeoVisualLog` via `using Telescope.Logging;`):
  ```csharp
  /// <summary>
  /// Severity-filtered diagnostics navigation (Gap 3, LazyVim <c>]e</c>/<c>[e</c>/<c>]w</c>/<c>[,w</c>):
  /// gathers the Error List entries for the requested severity in the ACTIVE document
  /// (<see cref="ErrorListGatherer"/> — the CodeIssuesFinder-established DTE2 API), asks
  /// the pure <see cref="DiagnosticNavigator"/> for the next/previous entry relative to
  /// the caret line, and opens it at its line (the shared DteFileOpener.OpenAtLine).
  /// Logs the <c>[NeoVisual] diagnostic-nav ...</c> outcome contract (m47-style: fired vs
  /// no-op and why). Never crashes the hook: any gather/open failure is logged as
  /// <c>diagnostic-nav failed: {msg}</c> and swallowed.
  /// </summary>
  internal void NavigateDiagnostic(bool forward, bool severityError)
  {
      ThreadHelper.ThrowIfNotOnUIThread();

      try
      {
          var dte = VsServices.Dte(_package);
          var document = dte?.ActiveDocument;
          if (dte == null || document == null)
          {
              NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}diagnostic-nav no-op: no-active-document");
              return;
          }

          string activePath = document.FullName;
          int caretLine = document.Selection is EnvDTE.TextSelection selection
              ? selection.ActivePoint.Line
              : 0;

          var entries = ErrorListGatherer.Gather(dte, activePath, severityError);
          if (entries.Count == 0)
          {
              NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}diagnostic-nav no-op: no-entries");
              return;
          }

          var target = forward
              ? DiagnosticNavigator.Next(entries, caretLine)
              : DiagnosticNavigator.Prev(entries, caretLine);
          if (target is not DiagnosticEntry hit)
          {
              NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}diagnostic-nav no-op: at-end");
              return;
          }

          Telescope.Finders.DteFileOpener.OpenAtLine(dte, hit.FilePath, hit.Line);
          NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}diagnostic-nav direction={(forward ? "next" : "prev")} severity={(severityError ? "error" : "warning")} target={hit.FilePath} line={hit.Line}");
      }
      catch (Exception ex)
      {
          NeoVisualLog.Log($"{Telescope.Logging.DiagnosticLog.NeoVisual}diagnostic-nav failed: {ex.Message}");
      }
  }
  ```
  Behavior pinned: exactly ONE of {success, no-op, failed} line per call — never multiple,
  never silent; `caretLine` falls back to 0 when `Selection` is not a `TextSelection`
  (deterministic: `Next` → first entry, `Prev` → `at-end`); the success line is logged AFTER
  the open (a failed open surfaces as `failed:`; the open also emits the EXISTING
  `[Telescope] goto line={n}`).
- **Verify-with:** the BP-B5 literals (QUEUED E2E-GAP3-1 asserts them); unit via the registry
  (BP-B4); `dotnet build` 0 errors.
- **Fails-if:** `leader-binding executed: ],e` with NO `diagnostic-nav` line (registry not
  wired / wrong class); `failed:` on every press (read `{msg}`); `at-end` when the caret is
  ABOVE the first diagnostic (caret misread / inverted comparison); `no-entries` with a
  populated Error List (gather filter); a hook crash/VS freeze (try/catch or
  `ThrowIfNotOnUIThread` dropped — must never happen); a SECOND log line per navigation.

**BP-B4 — `Actions` registry: 4 new entries (12 → 16).**
- **Files:** `MyExtension/Package/Utils/Actions.cs`
- **Change:** insert four lines immediately AFTER the `["close-window"]` entry (between :28
  and :29 `};`), matching the existing factory shape:
  ```csharp
                ["close-window"] = (h, _) => () => h.CloseWindow(),
                ["next-error"] = (h, _) => () => h.NavigateDiagnostic(forward: true, severityError: true),
                ["prev-error"] = (h, _) => () => h.NavigateDiagnostic(forward: false, severityError: true),
                ["next-warning"] = (h, _) => () => h.NavigateDiagnostic(forward: true, severityError: false),
                ["prev-warning"] = (h, _) => () => h.NavigateDiagnostic(forward: false, severityError: false),
            };
  ```
  (The `close-window` line shown for placement context is NOT changed. Total: 6 explicit +
  6 telescope-derived + 4 new = **16**.) Wiring NOT needed (do not invent): no MEF export, no
  package/WindowManager registration, no `ResolveAction` case (it resolves registry names
  generically), no JSON/KeyNames change (Sections A own those).
- **Verify-with:** `Run_ActionsRegistry_ContainsAllBuiltins` (C updates 12→16, RED before):
  all 16 keys present, the 12 old keys intact; `Run_Keybinding_DefaultFileHasDiagnosticNav`
  GREEN only when A+B both landed; runtime: NO `Unknown action 'next-error' ...` at load.
- **Fails-if:** the count test sees 12; a MISSING old key (an entry overwritten — must be
  purely additive); the Unknown-action line persists after A+B (JSON value ≠ registry key);
  CS1061 `NavigateDiagnostic` (BP-B3 not first / not `internal`).

**BP-B5 — M-M7 diagnostic contract pin (declaration — NO edits).**
- **NEW literals (exactly 3 — the complete M-M7 set):**
  1. `[NeoVisual] diagnostic-nav direction=next|prev severity=error|warning target=<full file path> line=<1-based int>` (exactly one per successful navigation; byte-exact call-site template in BP-B3).
  2. `[NeoVisual] diagnostic-nav no-op: no-active-document` / `no-op: no-entries` / `no-op: at-end` (exactly one per no-op).
  3. `[NeoVisual] diagnostic-nav failed: {msg}` (exactly one per exception; never crashes the hook).
- **REUSED literals (NOT new — the diff gate must not flag):** `[Telescope] goto line={n}`
  (the reused `DteFileOpener.OpenAtLine`, DteFileOpener.cs:19); `leader-binding executed: …`;
  `Keybindings loaded: 36 binding(s), …` (Section A's count); `Command '...' failed: {msg}`
  (only `],d`/`[,d` can hit it).
- **Fails-if:** any `diagnostic-nav` line whose format differs from the three pinned shapes
  (e.g. `direction=True`, empty `target=`) — fix the call site, not the assertion.

### Phase 3 — Unit tests (BP-C1 … BP-C7)

> **Files: ONLY `tests/NeoVisual.Tests/Program.cs`.** Full per-test code with exact anchors:
> `artifacts/section-c.md`. Execution order is BOTTOM-UP by file position (BP-C1 → BP-C6
> insert/replace in descending line order) so every cited line number stays valid. Suite
> arithmetic PINNED: **177 + 10 new − 0 removed = 187**.

- **BP-C1** — NEW `DiagnosticNavigator` section + 4 core tests (insert before the
  `// Helpers — WindowRect` banner, :1819): `Run_DiagnosticNavigator_Next_PicksFirstBelowCaret`,
  `_Prev_PicksLastAboveCaret`, `_Next_AtEndReturnsNull`, `_Prev_AtStartReturnsNull` — exact
  assertion tables per BP-B1's semantics (entries `[10,20,30]`: caret 5→10, 15→20; Prev 25→20,
  40→30; NO wrap at both ends). RED: **CS0246 compile error** until BP-B1.
- **BP-C2** — 4 edge-case tests appended to the same section:
  `_EmptyReturnsNull`, `_CaretOnDiagnosticSkipsIt` (strict inequality — never re-selects the
  entry at the caret), `_SingleEntry_BothDirections`, `_UnsortedInput_ListOrderContract`
  (entries `[(30),(10)]`, caret 5 → Next returns **30** — pins that the seam does NOT sort).
  RED: CS0246.
- **BP-C3** — UPDATE `Run_ActionsRegistry_ContainsAllBuiltins` (:1749-1766): count 12→16 +
  append `"next-error", "prev-error", "next-warning", "prev-warning"` to the names array.
  RED: `Expected [16] but got [12]`.
- **BP-C4** — NEW `Run_KeyNames_RoundTrip_DiagnosticNav` (insert before the
  ToolWindowTypeResolver banner, :343): all six sequences round-trip through
  `LeaderSequenceMatcher` (Space → bracket → letter → Execute with the exact sequence string;
  the bracket alone is a live prefix → Consume) + the shift-insensitivity pin
  (`ToString(OemCloseBrackets, true)=="]"`, `ToString(OemOpenBrackets, true)=="["`).
  RED: `Expected [Consume] but got [Abort]` (the bracket builds the enum name).
- **BP-C5** — EXTEND `Run_KeyNames_PrintableMappings` (:250-260): insert
  `Assert.Equal("]", KeyNames.ToString(Keys.OemCloseBrackets));` +
  `Assert.Equal("[", KeyNames.ToString(Keys.OemOpenBrackets));` after the `|` line; the 5
  existing assertions UNCHANGED. RED: `Expected []] but got [OemCloseBrackets]`.
- **BP-C6** — NEW `Run_Keybinding_DefaultFileHasDiagnosticNav` (insert before
  `Run_KeybindingConfig_IsSimpleShortcut`, :230): the six ContainsKey + exact-value assertions
  + the bare-`]`/`[` prefix-trap guard (`Assert.False(ContainsKey("]"))`/`("[")`).
  RED: `],d -> next-diagnostic binding present`.
- **BP-C7** — GATE: `dotnet run --project tests/NeoVisual.Tests` → exactly
  `187 passed, 0 failed, 187 total.` (exit 0); `--list` → 187 `Run_*` lines; subset
  spot-checks: `-- DiagnosticNavigator` → 8, `-- Keybinding` → 12, `-- KeyNames` → 6,
  `-- ActionsRegistry` → 4. A failing PRE-EXISTING test name is a REGRESSION — trace the
  owning section, never weaken an assertion.

**Existing tests audited — NONE can break** (additive change): the lowercase-leader loop in
`Run_Keybinding_DefaultFileHasTelescopeAndNav` passes (all six new keys are non-simple +
lowercase); `...WindowManagement` reads only `w,*`; `Run_KeyNames_CaseEncodesShift` touches
letters+OemMinus only; the telescope-registry test filters by the `telescope` prefix (the 4
new names don't match); no test pins the defaults' 30-count.

### Phase 4 — Harness scenario registration (BP-D1 … BP-D4)

> **Files: ONLY `tools/harness/test-e2e.ps1`.** `harness-common.ps1` cited read-only (its
> `Send-Text` punct table maps `'[' = @(0xDB, $false)` / `']' = @(0xDD, $false)` UNSHIFTED —
> harness-common.ps1:119; the matcher's non-letter path is shift-insensitive, so the sequence
> names are exactly `],d`/`[,d`/`],e`/`[,e`/`],w`/`[,w`). Full verbatim blocks:
> `artifacts/section-d.md`. E2E DEFERRED — the scenario is REGISTERED, never executed.

- **BP-D1** — Header scenario-list line: insert ONE line between :28 and :29
  (`#   neovisual-diagnostic-nav  Space ]d/[d native + ]e/[e/]w/[w severity nav (leader-binding + diagnostic-nav)`).
  The m63 `-TotalCount 60` window holds (the ordering/self-seed words move :47→:48).
- **BP-D2** — REGISTER `neovisual-diagnostic-nav` (insert the scenario block between :768 and
  :770 — after `neovisual-window-management`, before `neovisual-toolwindow`; registration
  position 9; `seed-leak` stays LAST): scaffold (`Reset-LogBaseline`/`Enter-NormalContext`/
  `Assert-VsFocused`) + the two NATIVE pairs — `Send-Tap VkSpace`, `Send-Text ']'`,
  `Send-Tap VkD` → assert `leader-binding executed: \],d`; same for `[,d`; then
  `Assert-VsFocused` (the native commands must not have moved focus). Regex-escape the
  brackets (`\],d` / `\[,d` — unescaped `[,d` is a character class). NO new `$script:Vk*`
  variable.
- **BP-D3** — EXTEND the scenario with the four CUSTOM pairs (`],e`/`[,e`/`],w`/`[,w`): per
  pair — leader, bracket, OWN `$preNav = Get-LogCacheIndex $logPath` snapshot BEFORE the final
  key tap, letter tap, assert `leader-binding executed: \<seq>`, then
  `Assert-NewLogLineAfter $logPath $preNav "$($script:PfxNeo)diagnostic-nav (direction=next severity=error |no-op: )"`-style
  TOLERANT assertion (the exact per-pair patterns pin `direction=`+`severity=` KEY-DERIVED and
  accept the target form OR the `no-op: ` form; `failed:` is deliberately NOT accepted — a
  gather failure on a healthy instance is a defect the live gate must surface). The snapshot
  is MANDATORY per pair (the no-op form carries no direction/severity — a stale snapshot would
  let the PREVIOUS pair's no-op line satisfy the pattern = false PASS).
- **BP-D4** — No-VS gates: `pwsh tools/harness/test-e2e.ps1 -SelfCheck` → `SelfCheck: PASS`
  (exit 0); `pwsh tools/harness/test-e2e.ps1 -List` → **39** names with
  `neovisual-diagnostic-nav` between `neovisual-window-management` and `neovisual-toolwindow`,
  `seed-leak` LAST. NEVER run the harness without these flags (no VS boot).

**No-collision enumeration (COMPLETE):** all 8 existing `leader-binding executed:` assertion
sites (:704/:707/:731/:739/:749/:767/:781/:989) start with `w`/`e`/`b`; all six new sequences
start with `]`/`[` — zero overlap, zero prefix relations; no scenario sends `[`/`]` today;
`diagnostic-nav` appears nowhere. **No existing scenario needs any change.**

### Phase 5 — Docs sync + e2e queue (BP-E1 … BP-E11)

> **Files: ONLY `docs/spec.md`, `AGENTS.md`, `.opencode/skills/vs-extension-dev/SKILL.md`,
> `docs/progress.md`, `.opencode/workspaces/neovim-planning-hub/e2e-queue.md`.** Full
> before/after text for every edit: `artifacts/section-e.md`. `<ACTUAL>` = the NeoVisual total
> PRINTED by the real suite run (expected 187; never write the estimate). Executed LAST
> (documents symbols Sections A–D create).

- **BP-E1** — spec.md §3: the ResolveAction action list gains `next-error`, `prev-error`,
  `next-warning`, `prev-warning` (L184-187); the built-in-defaults paragraph gains the six
  `Space+]…`/`Space+[…` bindings + the severity-nav description (in-file, NO wrap, no-op
  logged — L196-200). Do NOT touch the case-sensitivity sentence or the `w`-prefix sentences.
- **BP-E2** — spec.md §4: ONE new bullet after L246 for the `diagnostic-nav` family —
  byte-identical to Section B's pinned literals (incl. the `failed: {msg}` variant Section B
  pinned). Never invent a literal form here.
- **BP-E3** — spec.md §5+§8: NeoVisual count → `<ACTUAL>` (L271); the coverage tail gains
  `DiagnosticNavigator` (L282-283); `# all 38 scenarios` → 39 (L297); the §5.2 preamble →
  `39 registered` with BOTH registered-unexecuted scenarios named (L302-304); the enumeration
  gains `neovisual-diagnostic-nav` (L312); §8's stale `(171)` (pre-existing Gap-1 drift) →
  `(<ACTUAL>)` + the 39-registered form (L428-432). Telescope 172 stays.
- **BP-E4** — spec.md §7: ONE feature bullet after the Fzf finder bullet (the diagnostics-nav
  description; says QUEUED, never "live test passes"; no backticked `Run_DiagnosticNavigator_*`
  wildcard — doc-ref-lint risk).
- **BP-E5** — AGENTS.md: the NeoVisual enumeration gains `DiagnosticNavigator` (L111-112);
  the unit count → `<ACTUAL>` (L116); Telescope 172 verify-unchanged (L100).
- **BP-E6** — AGENTS.md: `# all 38 scenarios` → 39 (L132); the scenario preamble → 39
  registered with both queued scenarios named (L138-141); the new scenario bullet inserted
  after the `neovisual-window-management` bullet (L166), NOT at the list end.
- **BP-E7** — AGENTS.md: the keybindings gotcha example gains
  `],d`→`command:Edit.GotoNextIssueinFile` (L370-371); the diagnostics run-on list gains the
  `diagnostic-nav` family (after L208, keep the comma chain intact); the §7-mirror feature
  bullet (byte-identical to BP-E4's).
- **BP-E8** — SKILL.md: the AGENTS-pointer note → 39 registered (L14-15); the action list
  gains the four actions (L174-177); the NeoVisual count → `<ACTUAL>` (L249-250); the
  scenario count → 39 (L252-254).
- **BP-E9** — progress.md item 7 → DONE (L485-487) — **execute ONLY at GREEN**; the DONE form
  records the falsified `Edit.NextError`/`Edit.PreviousError` assumption + the shipped
  bindings + `e2e gates queued (E2E-GAP3-1..2)`. HARD SCOPE RULE: exactly ONE edit — the
  Status line, SESSION HANDOFF, Current state, Decisions, Baseline, In-progress, the Done
  section, and the FIRST-ITEM blocks are hub-owned.
- **BP-E10** — workspace `e2e-queue.md`: READ-MERGE-WRITE append of rows **E2E-GAP3-1..2**
  (status QUEUED) after the E2E-GAP1-5 row, before `## Rules`; pipes inside cells escaped
  (`direction=next\|prev` is the trap); existing rows byte-identical. (`docs/e2e-queue.md` —
  the repo-docs copy — is OUT of this section's scope; the hub mirrors the gates there at
  handoff per the Gap-1 precedent.)
- **BP-E11** — Final gate: (1) `check-doc-refs.ps1` → 0 unresolved (`DiagnosticNavigator`
  resolves once BP-B1 lands; the `command:`-prefixed VS commands follow the established
  style); (2) `check-doc-content.ps1` → PASS (12 assertions) — **DOC-66-3 carry-forward**: if
  the hub re-attributes the Baseline to Gap 3 at GREEN, the attribution must either keep an
  accepted phrase or the build loop reconciles the lint expectation in
  `tools/lint/check-doc-content.ps1` (the Gap-1 BP-E7 precedent — record it as a DEVIATION
  either way; never silently ignore a DOC-66-3 failure); (3) the count-consistency sweep (same
  `<ACTUAL>` + 39 everywhere; no `38 registered`/`177 tests`/`(171)` remnant in current-state
  lines); (4) `-List` → 39.

### Phase 6 — Build + unit gate (BP-G1)

- **Files:** none (verification only).
- **Change:** `dotnet build MyExtension.slnx` (0 errors); `dotnet run --project
  tests/NeoVisual.Tests` → **187 passed, 0 failed**; `dotnet run --project
  tests/Telescope.Tests` → **172 passed, 0 failed** (STAGGERED — never simultaneous, W11
  policy); then BP-D4's `-SelfCheck` + `-List` and BP-E11's lints + sweep.
- **Verify-with:** all of the above green. NO e2e run (deferred — E2E-GAP3-1..2).
- **Fails-if:** any unit failure (map through the Verification Trace); a build error;
  `-List` ≠ 39; a lint failure.

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_DiagnosticNavigator_*` (8 new) | BP-B1, BP-C1, BP-C2 | RED compile CS0246 → GREEN per the BP-B1 assertion tables (strict comparisons, no wrap, no internal sort) |
| `Run_ActionsRegistry_ContainsAllBuiltins` (update) | BP-B4, BP-C3 | RED `Expected [16] but got [12]` → GREEN 16 keys, 12 old intact |
| `Run_KeyNames_PrintableMappings` (extend) | BP-A1, BP-C5 | RED `Expected []] but got [OemCloseBrackets]` → GREEN `]`/`[` |
| `Run_KeyNames_RoundTrip_DiagnosticNav` (new) | BP-A1, BP-C4 | RED `Expected [Consume] but got [Abort]` → GREEN all six sequences Execute; bracket alone = Consume; shift-insensitivity pinned |
| `Run_Keybinding_DefaultFileHasDiagnosticNav` (new) | BP-A2, BP-B4, BP-C6 | RED `],d -> next-diagnostic binding present` → GREEN 36 bindings + six exact values + no bare `]`/`[` |
| Load-time diagnostic | BP-A2, BP-B4 | `[NeoVisual] Keybindings loaded: 36 binding(s), leader = Space ...`; NO `Unknown action '...' for binding '...' - ignored.` after A+B |
| Runtime diagnostics (queued e2e) | BP-B2/B3/B4, BP-D2/D3 | `[NeoVisual] diagnostic-nav direction=next\|prev severity=error\|warning target=<path> line=<n>` / `no-op: <reason>` / `failed: {msg}` (BP-B5 shapes, byte-exact) + `leader-binding executed: ],d`/`[,d`/`],e`/`[,e`/`],w`/`[,w` |
| AC6 no-op (never wraps) | BP-B1, BP-C1, BP-C2 | `no-op: at-end` / `no-op: no-entries` pinned by the at-end/empty tests |
| e2e `neovisual-diagnostic-nav` (QUEUED, E2E-GAP3-1) | BP-D2, BP-D3 | the six leader lines + one tolerant `diagnostic-nav` outcome per custom pair, snapshot-attributed |
| e2e full-suite re-run (QUEUED, E2E-GAP3-2) | all (additive-only) | all existing lines unchanged; the only new family is `diagnostic-nav` (+ the 36-binding count line) |
| Full-suite count gate | BP-C7, BP-G1 | `187 passed, 0 failed` (NeoVisual); `172 passed, 0 failed` (Telescope, staggered) |
| No-VS harness gates | BP-D4, BP-G1 | `-SelfCheck` PASS; `-List` = 39 (new scenario between window-management and toolwindow; seed-leak last) |
| Lint + consistency gates | BP-E11, BP-G1 | check-doc-refs 0 unresolved; check-doc-content PASS (DOC-66-3 carry-forward procedure); count sweep 0 remnants |

**Known-RED allowlist: NONE.** Both unit suites GREEN pre-change (172/177), all 37 executed
e2e GREEN. Expected RED = the new Section-C tests before Sections A/B land (CS0246 for the
seam; assertion failures for the rest). The verification-agent must NOT flag: the mid-plan
inert states (Unknown-action load line between A and B), the REGISTERED-unexecuted scenarios
(`neovisual-window-management` pre-existing E2E-GAP1-1; `neovisual-diagnostic-nav` new
E2E-GAP3-1), the queued full-suite re-run, or the pre-existing spec.md §8 `(171)` drift
(repaired by BP-E3.6).

## Hub handoff steps (Step 7, on user approval — hub-owned, not build-agent steps)

1. Write the assembled plan to `docs/implementation_plan.md`.
2. **Canonical-queue reconciliation:** append the E2E-GAP3-1..2 gates to `docs/e2e-queue.md`
   (the repo-canonical queue) as list items — NEVER as `## E2E-` headers (DOC-64-1). The
   workspace-queue rows (BP-E10) are the hub's tracking copy; both must exist.
3. `docs/progress.md` hub-owned edits: the new plan as the FIRST pending item with explicit
   unit-only-lane + defer-e2e instructions; the Done entry at GREEN; Baseline re-attribution
   at GREEN (keep a DOC-66-3-accepted phrase or reconcile the lint — BP-E11's carry-forward);
   "Current state"/"Next up"/SESSION HANDOFF refresh; a Decisions entry recording the user's
   2026-10-04 scope answer (build severity-filtered `]e`/`]w` since native lacks them) + the
   falsified `Edit.NextError`/`Edit.PreviousError` backlog assumption.
4. Workspace `e2e-queue.md`: the E2E-GAP3 rows flip QUEUED → READY only when this plan is
   GREEN in `docs/progress.md` (Step 8 reconciliation).
