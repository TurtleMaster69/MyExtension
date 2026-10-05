# Plan — Gap 4: recent-files finder (Telescope-style, `Name="Recent"`)

> **HANDOFF (2026-10-05, neovim_hub):** gate-APPROVED in the planning-hub session (G3:
> REVISE (the false-GREEN) → APPROVE; the AC5 patch applied; the handoff USER APPROVED) —
> plan verbatim from
> `.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-143017/plans/plan-gap4.md`.
> Handoff corrections (stale era data; the contract unchanged):
> 1. **The leader binding is CONFIRMED:** the user chose **`f,e`** (2026-10-05, via the
>    question tool) — BP-6's "PROPOSED — flagged at handoff" flag is DISCHARGED; pin
>    `"f,e": "telescope-recent"`.
> 2. **Queue position:** ALL FOUR predecessors are GREEN (columns `492c6c9`, goto `90e6245`,
>    gap 11 `c862357`, feature 7 `14b0446`) — THIS plan is FIRST in queue; the column-model
>    dependency is satisfied (the pane architecture also landed — the finder pipeline is
>    host-agnostic, unaffected).
> 3. **Counts:** the CURRENT baselines are Telescope.Tests **258** (not 199/209) → +10 =
>    **268** expected at GREEN (the `<ACTUAL>` re-read at the gate is the safety net);
>    NeoVisual.Tests **191** (unchanged); e2e **43 → 44** (the new `telescope-recent`).
> 4. **The known-RED allowlist is NONE**; per-scenario flaky counts start at **0**, EXCEPT
>    `telescope-goto` carries a base of **1** (pass-on-retry in run 172 — a pre-existing
>    caret/Roslyn race; the 3rd strike = the M-M2 regression upgrade). The stale
>    `neovisual-window-management` ×2 note in the era docs is FIXED (runs 168-179 flake-free).

> **Lane: feature (e2e ENABLED).** New finder + likely new diagnostics → **M-M7** (the new
> literals are declared). Full feature-lane loop.
>
> **Source:** the queue's Gap 4 (the user's 2026-09-28 BUILD decision: "a Telescope-style
> recent-files finder").
>
> **Research:** the primary data source is `EnvDTE._DTE.RecentFiles` (the automation MRU —
> `RecentFile.Path` / `Open()`; MEDIUM confidence — the MS-Learn page 404'd, so the planner
> verifies the API shape against the installed interop via LSP). The registry hive is FRAGILE
> (VS 2017+ keeps the MRU in a private registry — do NOT read it). Fallback: an
> extension-maintained MRU from the extension's own `[Telescope] opened file:` events (only
> knows files opened since load). PIN: `DTE.RecentFiles` primary; the MRU fallback documented
> as a future enhancement, not built.
>
> **Ground truth (recon):** the finder skeleton is established — `IFinder`/`FinderEntry`
> (TelescopeFinder.cs:15-67), `FinderBase<THit>` (FinderBase.cs:16-79: the guarded gather,
> ToEntry, OpenHit), the exemplar `ReferencesFinder.cs:25-65` (the ctor-injected gatherer
> `Func` + opener `Action`, Name, display/payload, open), the registration chain
> (MyExtensionPackage.cs:105-110 → TelescopeController.RegisterFinder → FinderNames
> TelescopeLauncher.cs:26-35 + the derived Actions.Registry entry + the equality fixture).
> The goto plan (SECOND in queue) adds DefinitionFinder the same way — Gap 4 follows that
> precedent (FIFTH in queue: columns → goto → gap 11 → feature 7 → gap 4).
>
> **Leader key:** `f,r` is TAKEN (references). PROPOSED: **`f,e`** (rEcent) — flagged for the
> user at handoff (the alternatives: `f,m` most-recent, `f,h` history).
>
> **Column model dependency:** the columns plan (FIRST in queue) builds the per-finder column
> sets for the 6 EXISTING finders; a NEW finder needs its own column set (File | Dir — the
> Files finder's shape) added to `FinderColumns` — a small post-columns addition THIS plan
> owns.

## Goal

A Telescope-style recent-files finder: `Space+f,e` opens an overlay listing the solution's
recently-opened files (the VS MRU), filterable, previewable, Enter opens the file — the
established finder pipeline end to end.

## Approach

**D1 — `RecentFileHit` + `RecentFilesFinder`.** NEW `Telescope/Finders/RecentFileHit.cs`
(extends `FileLocation`; maybe `Title`? — the MRU entries are paths; PIN: path only, the
columns model derives name/dir). NEW `Telescope/Finders/RecentFilesFinder.cs`
(`Name="Recent"`): the ctor-injected gather seam `Func<IReadOnlyList<string>>` (the MRU paths,
most-recent-first) + the opener (the `DteFileOpener` pattern) — hermetic-testable. The
display: the file name (+ the dir via the column model).

**D2 — The gatherer.** `MyExtension/Package/RoslynGatherers.cs`-adjacent (or a small
`MyExtension/Package/Utils/RecentFilesGatherer.cs`): `dte.RecentFiles` → the paths
(most-recent-first; skip non-existent files — `File.Exists` filter; the count cap ~200 like
the query finders). UI thread. The planner verifies the `RecentFiles`/`RecentFile` API shape
(`EnvDTE` interop — LSP against the installed assembly) and pins it.

**D3 — Registration + the leader binding.** `MyExtensionPackage` registers the finder;
`TelescopeLauncher.FinderNames["telescope-recent"] = "Recent"` (auto-derives the
`telescope-recent` registry entry — the equality fixture stays green); the leader binding
`"f,e": "telescope-recent"` in `default-keybindings.json`. The registry count +1 (an
`<ACTUAL>` re-read).

**D4 — The column set.** `FinderColumns` gains the Recent catalog (File ON, Dir ON — the
Files finder's shape; Full path off) — a post-columns addition (the dependency binds).

**D5 — Diagnostics (M-M7).** The finder pattern's gather summary + open lines — PIN:
`[Telescope] recent files gathered count=...` (the gather summary) + the open reuses the
EXISTING `[Telescope] opened file: ...` (the FileFinder's open line — the same HitOpener
path) — the planner verifies which open line fires and pins the minimal new-literal set.

**D6 — Tests.** `tests/Telescope.Tests`: `Run_RecentFilesFinder_*` (the hermetic gather/
display/open/preview-jump/determinism — the DefinitionFinder test pattern) + the column-set
test. RED CS0246. Suite delta: +N (an `<ACTUAL>` re-read).

**D7 — e2e (ENABLED).** A NEW scenario `telescope-recent`: `Space+f,e` → the overlay opens
with the MRU (the harness's earlier scenarios opened files, so the MRU is populated) → type a
query → Enter opens. Created RED; executed at VERIFY.

**D8 — Docs.** spec.md (§2.2 the key-files row, §3 the binding, §4 the literals, §5 counts,
§7 a feature bullet), AGENTS.md, SKILL.md, progress.md (the item → DONE at GREEN).

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | `Space+f,e` opens the recent-files overlay with the MRU | `open finder=Recent candidates=...` | unit `Run_RecentFilesFinder_*`; e2e `telescope-recent` |
| AC2 | The results are most-recent-first, existing files only | the gather summary | unit (the order + the filter pinned) |
| AC3 | Enter opens the file; the preview jumps | the existing `opened file:` line | unit; e2e |
| AC4 | The column set (File/Dir) renders in the columned list | `results columns=file,dir` | unit (the Recent catalog) |
| AC5 | The registry/leader wiring is complete | `leader-binding executed: f,e` | e2e is the proof (the registry count is an observed effect, not asserted — `Run_ActionsRegistry_TelescopeMapsToFinder` derives both sides and cannot detect a missing BP-5 entry) |

## Files to be touched

- **Created:** `Telescope/Finders/RecentFileHit.cs`, `Telescope/Finders/RecentFilesFinder.cs`,
  the gatherer (placement the planner pins).
- **Modified:** `MyExtension/Package/MyExtensionPackage.cs`, `MyExtension/Package/Utils/TelescopeLauncher.cs`,
  `MyExtension/Package/Utils/Actions.cs` (derived — verify), the column model's `FinderColumns`
  (post-columns), `MyExtension/Resources/default-keybindings.json`, `tests/Telescope.Tests/Program.cs`,
  `tools/harness/test-e2e.ps1`, the docs.
- **Not touched:** the overlay (the finder pipeline is host-agnostic), the in-flight plans' files.

## Open risks

1. **`DTE.RecentFiles` shape (medium).** The doc 404'd — the planner verifies via LSP against
   the installed EnvDTE interop; if the API differs, the gatherer adapts (the seam isolates it).
2. **The MRU semantics (low).** `DTE.RecentFiles` is VS's MRU (across solutions) — the finder
   shows what VS shows in File▸Recent; the planner pins whether to filter to the current
   solution's files (RECOMMEND: no filter — show the MRU as-is, matching VS).
3. **The queue position (process).** Fifth — the counts are re-reads; the column model lands
   first.

## Build Plan

> **Aggregated (Stage 3)** from `artifacts/gap4-section-{a,b}.md` — the authoritative full
> detail (the finder/gatherer code, the test code, the scenario body, the doc edits) lives
> there; the steps below are the contract. **e2e ENABLED.**
>
> **Pinned corrections (binding):** (1) **`EnvDTE.RecentFiles` does NOT exist in the installed
> 17.x interop** (envdte.dll is pure type-forwarding → Microsoft.VisualStudio.Interop, zero
> Recent types) — the gatherer uses a REFLECTION PROBE on the live DTE COM object
> (`InvokeMember("RecentFiles",…)`, best-effort, a failure swallowed by design) + a
> **session-MRU fallback** (DocumentEvents-driven) as the guaranteed floor — both behind the
> unchanged `Func<IReadOnlyList<string>>` seam; (2) NO `Actions.cs` edit (the registry entry
> is derived from FinderNames); (3) the column tests are named `Run_ResultsColumns_Recent_*`
> (the existing family); (4) the MRU-timing strategy: the scenario opens `Models/Order.cs`
> first, then fires `f,e` and asserts it is the TOP match; (5) the M-M7 literals:
> `[Telescope] recent files gathered count={n}` (NEW) + the REUSED `[Telescope] opened file:
> {path}` + `[Telescope] recent files gather failed: {msg}` / `[Telescope] open file failed:
> {msg}`.

> **RE-PLAN (2026-10-05, implementation-planner — the VERIFY final gate FAILED twice: run 181
> fresh boot + run 182 retry, identical deterministic signatures):** two REAL regressions,
> both fixed by REV 2 steps below. Everything else GREEN: Telescope.Tests 268/0 (the 10 new
> tests), the harness health (44 registered, both lints, `-SelfCheck`), the other 42
> scenarios, the seed guards; code inspection verified BP-2's order contract, the M-M7
> literals byte-exact, BP-4/BP-7 correct — "the ONLY source defect is BP-3's lazy event
> hookup".
> 1. **Regression 1 — `Run_ActionsRegistry_ContainsAllBuiltins`** (NeoVisual.Tests):
>    `Assert.Equal(17, Actions.Registry.Count)` → "Expected [17] but got [18]". BP-5's
>    derived-registry growth (17→18) was real; the pinned count test was never updated (the
>    test file has NO git diff). PLAN GAP: the Verification Trace's BP-5 row only owned
>    `Run_ActionsRegistry_TelescopeMapsToFinder` staying green (it did) — no step owned the
>    ContainsAllBuiltins count pin. FIX: **BP-5 rev 2** owns the test edit. GATE-TRUST
>    FINDING: the build-agent's "NeoVisual.Tests 191/0" self-report was FALSE (fresh output:
>    190/1) — flagged per audit-verification-gates; **BP-B6 rev 2** pins the
>    runner's-own-summary rule.
> 2. **Regression 2 — `telescope-recent` (its FIRST live execution):** `recent files gathered
>    count=0` + `open finder=Recent candidates=0` DESPITE the scenario following the pinned
>    MRU-timing strategy exactly (`opened file: ...Order.cs` present; `leader-binding
>    executed: f,e` present — BP-6 wiring works). ROOT CAUSE: the session-MRU "guaranteed
>    floor" hooks `DocumentEvents.DocumentOpened` LAZILY inside `Gather()`
>    (RecentFilesGatherer.cs:45 → :108-131) — it records only files opened AFTER the first
>    gather → structurally empty for the open-FIRST strategy. The reflection probe
>    contributed nothing on this experimental instance (best-effort, swallowed by design —
>    no degradation diagnostic existed). Fail-twice with an identical signature =
>    deterministic. FIX: **BP-3 rev 2** — the EAGER event hookup at gatherer construction +
>    the merge + the once-only probe diagnostic.
> 3. **Flake — `telescope-goto`:** pass-on-retry; cumulative count **2** (base 1 + 1). ONE
>    more flake = the 3rd-strike M-M2 upgrade to a REGRESSION. Do NOT plan a fix (record
>    only) — the NEXT item's VERIFY must carry count 2. See the allowlist.
> 4. **M-M7 contract ADDITION (owned):** ONE new literal —
>    `[Telescope] recent files probe unavailable: {msg}` (logged ONCE per gatherer instance).
>    The run-181 silence proved a silent probe is undebuggable (DECIDED: YES). BP-3 rev 2
>    owns the doc rows (AGENTS.md's diagnostics list + spec.md §4).

> **RE-PLAN 2 (2026-10-05, implementation-planner — the VERIFY final gate FAILED again:
> iteration 2, run 183 fresh boot + run 184 retry):** the PRODUCT CODE IS GREEN — both
> remaining blockers are HARNESS-LAYER, and the plan owns both scenario files.
> 1. **BP-3 rev 2 VERIFIED WORKING** (the run-181 signature is GONE — not escalated): run
>    183: `recent files probe unavailable: Unknown name. (DISP_E_UNKNOWNNAME)` ONCE →
>    `recent files gathered count=15` → `preview file=...Order.cs` (the Step-2 unfiltered
>    render) → `open finder=Recent candidates=15` + `leader-binding executed: f,e`. Run 184:
>    identical structure (count=6, most-recent-first ✓, no duplicates, no probe spam). Units
>    268/0 + 191/0 (fresh runner output; `Run_ActionsRegistry_ContainsAllBuiltins` PASS). The
>    Gap 4 FEATURE is verified GREEN by the log evidence — NO product-code change in this
>    re-plan.
> 2. **Blocker 1 — `telescope-recent` (fail-twice, deterministic, HARNESS-LAYER):** Step 3's
>    `preview file=.*Order\.cs` assertion NEVER fires during Step-3 typing, because
>    `PreviewEditorHost.Show` (PreviewEditorHost.cs:93-97) REUSES the view/document while
>    path+mtime are unchanged and `preview file=` is logged ONLY in `RebuildView`
>    (PreviewEditorHost.cs:169 — the cache-MISS path). The pinned MRU-timing strategy
>    guarantees Order.cs is the top of BOTH the unfiltered (Step 2) AND the 'Order'-filtered
>    (Step 3) lists → Step 3's Show is a cache HIT → no line → the assertion is structurally
>    unsatisfiable whenever the feature's most-recent-first ordering WORKS (it can only pass
>    if the ordering is wrong — inverted vs the pinned strategy). **HUB ADJUDICATION: ACCEPT**
>    — a plan-owned scenario edit (**BP-B1 rev 2**): attribute the top-match preview proof to
>    the STEP-2 OPEN (the unfiltered render's preview IS the most-recent-first proof —
>    asserted there with the snapshot attribution), and Step 3 asserts the filtered top match
>    via `results count=1 selected=0` + the Step-4 Enter-open (unchanged).
> 3. **Blocker 2 — `telescope-goto` (the M-M2 3rd-strike UPGRADE):** the cumulative flaky
>    count is now **3** (base 1 run-172 + 1 runs-181/182 + 1 run-183) → per the M-M2 budget
>    it is RECLASSIFIED A REGRESSION and feeds this re-plan loop — it can NO LONGER be
>    allowlisted as flaky. Failure signature: `definitions gathered count=0` →
>    `open finder=Definition candidates=0` (the Part-1 caret-symbol gather race; the retry
>    passed with the full contract — flake-not-break, but the budget is exhausted). The goto
>    item's Done entry documented the sibling fix (Part 4's order-dependent caret state → a
>    `gg` caret normalization, test-e2e.ps1:1927-1937). **BP-B7** pins the equivalent fix for
>    Part 1 + the audit of the other parts. NO product-code change — the goto feature is
>    GREEN (runs 169/170/174/179/182/184 all passed it).
> 4. **Everything else GREEN:** the harness health (44 registered, both lints, `-SelfCheck`),
>    the other 42 scenarios, the seed guards, the M-M7 literals byte-exact (the probe literal
>    fired exactly once per run — the trace row satisfied).
> 5. **Allowlist change:** `telescope-goto` is REMOVED from the flaky allowlist — it is a
>    FIXED REGRESSION at the next gate (it must be GREEN, not allowlisted). The in-scenario
>    re-walk (BP-B7) is IN-CONTRACT: a pass after it is a PASS, not a runner-level flake.

- **BP-1** — `RecentFileHit` (NEW — distinct from `FileHit` for the column type-disjointness).
- **BP-2** — `RecentFilesFinder` (NEW — the dedupe→`File.Exists`→`Take(200)` order contract;
  the finder-owned open log).
- **BP-3** — **REV 2 (2026-10-05 — full detail in "BP-3 rev 2" below):** `RecentFilesGatherer`
  — the EAGER `DocumentOpened` hookup at construction + the probe→session MERGE + the
  once-only `[Telescope] recent files probe unavailable: {msg}` diagnostic.
- **BP-4** — the package "finders"-step registration + the `OpenRecentFile` opener (never logs).
- **BP-5** — **REV 2 (2026-10-05 — full detail in "BP-5 rev 2" below):** `FinderNames["telescope-recent"]="Recent"`
  (LANDED; Actions.cs untouched — derived) **+ the plan-owned NeoVisual count-test edit**
  (`Run_ActionsRegistry_ContainsAllBuiltins`: 17→18 + the `names[]` entry).
- **BP-6** — the keybinding `"f,e": "telescope-recent"` (PROPOSED — flagged at handoff).
- **BP-7** — `FinderColumns.Recent()` + the switch case (the full-dir cells, no root trim).
- **BP-8** — the ten tests + the `WidthKinds` extension (RED CS0246).
- **BP-B1** — **REV 2 (2026-10-05 — full detail in "BP-B1 rev 2" below):** the e2e scenario
  `telescope-recent` (the header line + the registration after `telescope-results-columns`,
  before `seed-reset`) — the Step-2/Step-3 assertion set revised: the top-match preview proof
  moves to Step 2 (snapshot-attributed), Step 3 asserts the filtered top match.
- **BP-B7** — **NEW (2026-10-05 — full detail in "BP-B7" below):** the `telescope-goto`
  harness regression fix (the M-M2 3rd strike, run 183) — the `gg` caret normalization in
  Parts 1-3 (mirroring Part 4's landed fix) + Part 1's single bounded re-walk on the
  0-gather signature. Executed with BP-B1 rev 2, BEFORE the BP-B6 gate.
- **BP-B2..B5** — the docs: spec (§2.2, §3, §4, §5, §7, §8); AGENTS.md; SKILL.md; progress.md
  at GREEN (the DOC-66-3 attribution constraint pinned).
- **BP-B6** — **REV 3 (2026-10-05 — full detail in "BP-B6 rev 3" below; supersedes rev 2's
  ledger + gate step 9 ONLY):** the suite arithmetic UNCHANGED (Telescope 268; NeoVisual 191;
  e2e 44) + the trust rule UNCHANGED; the `telescope-goto` flaky ledger is CLOSED (count 3 →
  the M-M2 upgrade fired → fixed by BP-B7) and the gate's pass-on-retry allowance for it is
  REMOVED — it must be GREEN outright.

### BP-3 rev 2 (2026-10-05) — the EAGER event hookup + the merge + the probe diagnostic

**Files:** `MyExtension/Package/Utils/RecentFilesGatherer.cs` (MODIFY — the ONLY source file
this rev touches), `AGENTS.md` + `docs/spec.md` (ONE literal row each — the M-M7 addition).

**Change (four code edits to the LANDED gatherer + the doc-comment sync):**

1. **The EAGER hookup (the regression-2 fix):** the CONSTRUCTOR calls `HookSessionEvents()` —
   the session-MRU floor is maintained from CONSTRUCTION time, not from the first gather.
   The construction site is the package "finders" step (MyExtensionPackage.cs:116), which
   runs on the UI thread (after `SwitchToMainThreadAsync`, MyExtensionPackage.cs:79) — the
   COM event sinking is legal there. NO package edit (the construction line is unchanged).
2. **The connection-point discipline (unchanged, re-verified):** `_documentEvents` stays a
   FIELD (a GC'd COM connection point drops the subscription); the handler keeps the
   session-MRU list updated from construction time; NO unhook/dispose — the package owns the
   gatherer's lifetime (`_recentFilesGatherer`, MyExtensionPackage.cs:59). `DocumentOpened`
   is the ONLY event needed (the floor's contract is files opened since load,
   most-recent-first via the move-to-front dedupe).
3. **`Gather()` only READS the maintained list + the MERGE (adjudicated order):** the probe's
   MRU FIRST (VS's most-recent-first), then the session floor's ADDITIONS (files the probe's
   MRU lacks — case-insensitive), each list's internal order preserved; the finder's
   `Distinct → File.Exists → Take(200)` chain (BP-2, UNCHANGED) is the final
   dedupe/filter/cap. `HookSessionEvents()` STAYS in `Gather()` as an idempotent RETRY only
   (a no-op once the eager hookup succeeded; it covers a null-DTE-at-construction edge).
4. **The probe failure is OBSERVABLE (DECIDED: YES):** ONE new M-M7 literal —
   `[Telescope] recent files probe unavailable: {msg}` — logged ONCE per gatherer instance
   (`_probeFailureLogged` guard; the M18 one-time-fallback precedent). Emitted via
   `Telescope.Logging.TelescopeLog.Log` (the `[Telescope] ` prefix is stamped by TelescopeLog
   itself — `DiagnosticLog.Telescope + message`; callable from MyExtension per
   `InternalsVisibleTo Include="MyExtension"`, Telescope.csproj:32 — the same surface
   PreviewEditorHost.cs:89 uses).
5. **UI-thread discipline:** `HookSessionEvents()` gains `ThreadHelper.ThrowIfNotOnUIThread()`
   (it touches `dte.Events` — a VS API; both call sites — the ctor and `Gather` — are
   UI-thread).

The exact diffs against the LANDED file:

```csharp
// (A) the field — ADD one guard:
private DocumentEvents? _documentEvents;   // HOLD the reference: a GC'd COM connection point drops the subscription
private bool _eventsHooked;
private bool _probeFailureLogged;          // REV 2: the probe failure logs ONCE per gatherer

// (B) the constructor — ADD the eager hookup:
public RecentFilesGatherer(Func<DTE?> dteFactory)
{
    _dteFactory = dteFactory ?? throw new ArgumentNullException(nameof(dteFactory));
    HookSessionEvents();   // REV 2: EAGER — the session MRU is maintained from construction time
}

// (C) Gather() — the probe's catch logs once-only; the return MERGES:
public IReadOnlyList<string> Gather()
{
    ThreadHelper.ThrowIfNotOnUIThread();
    HookSessionEvents();   // idempotent RETRY only — a no-op once the eager hookup succeeded
    IReadOnlyList<string> probed;
    try
    {
        probed = ProbeRecentFiles();
    }
    catch (Exception ex)
    {
        // REV 2: best-effort by design, but OBSERVABLE — logged ONCE per gatherer instance
        // (the M18 one-time-fallback precedent). Run 181's silence proved a silent probe is
        // undebuggable.
        probed = Array.Empty<string>();
        if (!_probeFailureLogged)
        {
            _probeFailureLogged = true;
            Telescope.Logging.TelescopeLog.Log($"recent files probe unavailable: {ex.Message}");
        }
    }

    // REV 2 MERGE: the probe's MRU first (VS's most-recent-first), then the session floor's
    // additions (files opened since extension load that the probe's MRU lacks), each list's
    // internal order preserved. The finder's Distinct → File.Exists → Take(200) chain
    // (BP-2, unchanged) is the final dedupe/filter/cap policy.
    var merged = new List<string>(probed);
    var seen = new HashSet<string>(probed, StringComparer.OrdinalIgnoreCase);
    foreach (string p in _sessionMru)
    {
        if (seen.Add(p))
        {
            merged.Add(p);
        }
    }
    return merged;
}

// (D) HookSessionEvents() — ADD the UI-thread assert (the body is UNCHANGED):
private void HookSessionEvents()
{
    ThreadHelper.ThrowIfNotOnUIThread();   // REV 2: touches dte.Events (a VS API)
    if (_eventsHooked) { return; }
    try
    {
        DTE? dte = _dteFactory();
        if (dte == null) { return; }
        _documentEvents = dte.Events.DocumentEvents;
        _documentEvents.DocumentOpened += OnDocumentOpened;
        _eventsHooked = true;
    }
    catch { /* the session MRU is best-effort too; the probe may still serve */ }
}
```

(E) **The class doc-comment sync:** the failure-discipline paragraph's "no diagnostic literal
is added for the degradation" sentence is REPLACED by: "the probe failure logs
`[Telescope] recent files probe unavailable: {msg}` ONCE per gatherer instance (best-effort —
the session MRU floor serves); a gather that throws anyway is caught by FinderBase and logged
as `[Telescope] recent files gather failed: {msg}`."

**Doc rows (the M-M7 addition):** AGENTS.md's diagnostics list + spec.md §4 gain, next to the
`recent files gathered count=...` row:
`[Telescope] recent files probe unavailable: {msg}` (recent-files gatherer — the DTE
reflection probe failed; logged ONCE per instance; the session-MRU floor serves).

**Verify-with:**
- Unit: `dotnet run --project tests/Telescope.Tests` → **268 passed, 0 failed** (the gatherer
  is VS-coupled, NOT unit-tested — its correctness surfaces through the seam tests + the live
  e2e).
- e2e `telescope-recent`: `[Telescope] recent files gathered count=(\d+)` with **n≥1** emitted
  AFTER Step 1's `[Telescope] opened file: ...Order.cs` — the eager hookup means the session
  MRU holds Order.cs from CONSTRUCTION time.
- The probe diagnostic: on an instance where the probe throws,
  `[Telescope] recent files probe unavailable: {msg}` appears ONCE at the first Recent gather.

**Fails-if:**
- The run-181 signature RECURS (`gathered count=0` + `candidates=0` despite Step 1's open)
  AND no probe-unavailable line → the EAGER hookup no-oped (`VsServices.Dte(this)` returned
  null at construction — the retry only helps gathers AFTER the first). Verify with a
  temporary log at the ctor's hookup; if confirmed, ESCALATE to the hub (a second literal
  `recent files events unavailable: {msg}` is a plan decision — do NOT silently self-heal).
- `recent files probe unavailable:` appears on EVERY gather (the once-only guard broke — log
  spam).
- The merged list is oldest-first or drops session files (the merge order inverted — the
  probe must come FIRST; the session additions appended).
- A `COMException` escapes to `[Telescope] recent files gather failed:` (the probe's
  try/catch misplaced — the catch must cover ONLY `ProbeRecentFiles()`).
- The MRU shows duplicates (the merge's HashSet dedupe broke; the finder's `Distinct` is the
  guard, not the primary).

### BP-5 rev 2 (2026-10-05) — the FinderNames entry + the NeoVisual count-test edit

**Files:** `MyExtension/Package/Utils/TelescopeLauncher.cs` (LANDED — UNCHANGED),
`tests/NeoVisual.Tests/Program.cs` (MODIFY — the plan-owned test edit).

**Change:** BP-5's original change is LANDED and verified (`FinderNames["telescope-recent"]="Recent"`
at TelescopeLauncher.cs:36; `Actions.BuildRegistry` derives the 18th entry — Actions.cs:38-43).
REV 2 adds the plan-owned edit to the STALE COUNT PIN the landed change exposed (the
M34-replacement precedent — the plan supersedes; the test is NOT byte-frozen against THIS
edit). In `Run_ActionsRegistry_ContainsAllBuiltins` (tests/NeoVisual.Tests/Program.cs:1849-1870),
exactly three hunks:

1. The leading comment: "exactly the 17 built-in action names" → "exactly the **18** built-in
   action names", and the history sentence gains: `; Gap 4 adds the derived
   "telescope-recent" action (the Recent finder)`.
2. `Assert.Equal(17, Actions.Registry.Count);` → `Assert.Equal(18, Actions.Registry.Count);`
3. The `names[]` array gains `"telescope-recent",` after `"telescope-definition",` (the
   FinderNames order):

```csharp
    "telescope-definition", "telescope-recent",
```

**Verify-with:** `dotnet run --project tests/NeoVisual.Tests` → **191 passed, 0 failed** (the
count STAYS 191 — an EDIT, not an addition); `Run_ActionsRegistry_ContainsAllBuiltins` GREEN;
`Run_ActionsRegistry_TelescopeMapsToFinder` stays GREEN (unchanged).

**Fails-if:** "Expected [18] but got [17]" → the LANDED BP-5 entry was reverted
(TelescopeLauncher.cs:36 missing); "Expected [18] but got [19]+" → a SECOND untracked
finder/action landed (re-read `FinderNames` + the registry, then re-pin the count — never
hand-wave); the names[] loop fails on 'telescope-recent' → the array edit was skipped
(hunk 3).

### BP-B6 rev 2 (2026-10-05) — the suite arithmetic + the final gate (revised)

**Files:** none (verification-only step; executed by the verification-agent at VERIFY).

**Change (the revised arithmetic + the trust rule + the gate):**

1. **Arithmetic (the LANDED state, re-read at the gate):** Telescope.Tests = **268** passed,
   0 failed; NeoVisual.Tests = **191** passed, 0 failed (the count STAYS 191 — BP-5 rev 2's
   test EDIT adds no test); e2e = **44** registered (43 previously GREEN + `telescope-recent`).
2. **THE SELF-REPORT TRUST RULE (the run-181 gate-trust finding):** the suite counts are read
   from the RUNNER'S OWN final summary output of a FRESH `dotnet run` at the gate — NEVER
   from a build-agent's self-report. (Run 181: the build-agent reported "NeoVisual.Tests
   191/0"; the fresh output was 190/1 — the ContainsAllBuiltins failure was reported GREEN.)
   The verification-agent re-runs both suites itself, STAGGERED (W11 — never parallel; the
   Defender CS2012 lock).
3. **The flaky ledger:** `telescope-goto` cumulative flaky count = **2** (base 1, run 172 +
   1, run 181/182). Record only — NO fix planned. ONE more flake = the 3rd-strike M-M2
   upgrade to a REGRESSION. The NEXT queue item's VERIFY MUST carry count 2.
4. **The gate (the same 9 steps, revised expectations):** (1) `dotnet build` → 0 errors;
   (2) Telescope.Tests → 268/0; (3) NeoVisual.Tests → 191/0 read from the runner's summary,
   STAGGERED with (2); (4) `-List` → 44 with `telescope-recent`; (5) `-SelfCheck` → PASS;
   (6) doc-refs lint → 0 unresolved; (7) doc-content lint → PASS; (8)
   `-Tests telescope-recent -TimeoutSec 900` → GREEN; (9) the full 44-scenario fresh boot
   (NO `-NoBootstrap`) `-TimeoutSec 3600` → GREEN — `telescope-goto` allowed ONE
   pass-on-retry at count 2; a THIRD strike is a regression, not a flake.

**Verify-with:** the gate outputs above; `RESULT: PASS (all 44 scenario(s))`.

**Fails-if:** `RESULT: TIMEOUT` → the `-TimeoutSec` too small (harness-invocation issue —
re-run with a larger budget). A NeoVisual count ≠ 191 → the BP-5 rev 2 edit was mis-applied
(a duplicate/missing test) or an untracked test landed — re-read, never copy. Any scenario
OTHER than `telescope-recent`/`telescope-goto` failing → a Gap 4 regression (check the trace).

### BP-B1 rev 2 (2026-10-05) — the telescope-recent Step-2/Step-3 assertion set

**Files:** `tools/harness/test-e2e.ps1` (MODIFY — the `telescope-recent` scenario body ONLY:
the two assertion hunks below + their comments; the registration, the header comment, and
Steps 1/4/5 are UNCHANGED).

**Root cause (why Step 3 could never pass):** `PreviewEditorHost.Show`
(PreviewEditorHost.cs:93-97) REUSES the hosted view/document while `path + LastWriteTimeUtc`
are unchanged, and the `preview file=` diagnostic is logged ONLY in `RebuildView`
(PreviewEditorHost.cs:169) — the cache-MISS path. The pinned MRU-timing strategy guarantees
Order.cs is the top of BOTH the Step-2 unfiltered list AND the Step-3 'Order'-filtered list
→ Step 3's `Show` is a cache HIT → no line → the assertion was structurally unsatisfiable
whenever the feature's most-recent-first ordering WORKS (runs 183/184: deterministic RED at
Step 3 while the Step-2 render had already emitted `preview file=...Order.cs`).

**Change — exactly two hunks:**

**Hunk 1 — Step 2 GAINS the top-match preview proof** (INSERT immediately after the existing
`results columns=file,dir$` assertion, still inside the Step-2 block — snapshot-attributed to
the existing `$preOpen`, taken before `Open-TelescopeFinder`):

```powershell
    # REV 2 (the top-match proof MOVED here from Step 3): the unfiltered render previews
    # the selected row 0 — the MOST-RECENT MRU entry. Step 1 just opened Models/Order.cs,
    # so row 0 IS Order.cs (most-recent-first) and this line is the AC2 proof. It must be
    # asserted HERE: PreviewEditorHost.Show reuses the view while path+mtime are unchanged
    # and logs 'preview file=' ONLY in RebuildView (the cache-MISS path) — Step 3's filtered
    # Show is a cache HIT (Order.cs tops BOTH lists) and can never emit the line.
    Assert-NewLogLineAfter $logPath $preOpen "$($script:PfxTel)preview file=.*Order\.cs" 'the unfiltered render previews the TOP (most-recent) match — Models/Order.cs'
```

**Hunk 2 — Step 3 LOSES the preview assertion, GAINS the exact filtered-top-match form**
(REPLACE the Step-3 comment block and the two assertions after `Send-Text 'Order'`; the
`$preQuery` snapshot and the `promptChanged` assertion are unchanged in form):

```powershell
    # Step 3: the query filters the MRU to the single Order.cs match, selection on row 0
    # (the filtered TOP match). REV 2: the preview proof lives in Step 2 (the unfiltered
    # render) — here the filtered top match is proven by the per-keystroke results line:
    # 'Order' is a unique name match in the scratch solution (Step 1's Files-finder render
    # over the FULL seed universe pinned count=1), the MRU candidates are a subset of the
    # seeded files, and the filter is monotone (more chars never add matches), so
    # count=1 selected=0 IS the filtered-top-match proof. NO 'preview file=' assertion
    # here: the filtered Show is a PreviewEditorHost cache HIT (path+mtime unchanged) and
    # the line only fires on a rebuild — structurally unemittable (runs 183/184).
    $preQuery = Get-LogCacheIndex $logPath
    Send-Text 'Order'
    Assert-NewLogLineAfter $logPath $preQuery "promptChanged query='Order'" 'typed query reached the recent prompt'
    Assert-NewLogLineAfter $logPath $preQuery "$($script:PfxTel)results count=1 selected=0" 'query filtered the MRU to the single Order.cs match, selected row 0' 10000
```

(The old line 2355 `results count=[1-9]\d* selected=0` 'query filtered the MRU to >=1 match'
and old line 2356 `preview file=.*Order\.cs` 'the just-opened file is the top (selected)
match' are DELETED — replaced by the single tightened assertion above.)

**Step 4 UNCHANGED:** the `$preEnter` snapshot + `Assert-NewLogLineAfter $logPath $preEnter
"$($script:PfxTel)opened file: .*Order\.cs" 'Enter opened the recent file' 20000` — the
Enter-open proof (AC3) is untouched.

**Verify-with:**
- `pwsh tools/harness/test-e2e.ps1 -Tests telescope-recent -TimeoutSec 900` → GREEN: the
  Step-2 line `[Telescope] preview file=...Order.cs` appears AFTER `$preOpen` (runs 183/184
  already emit it there — the assertion only pins what the verified feature already does);
  the Step-3 line `[Telescope] results count=1 selected=0` AFTER `$preQuery`; the Step-4
  `[Telescope] opened file: ...Order.cs` AFTER `$preEnter`.
- The full 44-scenario fresh-boot gate (BP-B6 rev 3, step 9).

**Fails-if:**
- Step 2's `preview file=.*Order\.cs` never fires → the unfiltered render did NOT preview
  Order.cs → the most-recent-first ordering is BROKEN (a REAL BP-2/BP-3 regression — check
  the merge order in `RecentFilesGatherer.Gather` and the move-to-front dedupe), NOT a
  scenario defect. Do NOT loosen the assertion.
- Step 3 never reaches `count=1` (only `count=[2-9]...` lines) → a foreign MRU entry
  fuzzy-matches 'Order' (possible ONLY via the reflection probe on a future instance — the
  session floor holds seeded files only) → ESCALATE to the hub before re-pinning (a plan
  decision: relax to `results count=[1-9]\d* selected=0` + a payload-keyed top-match proof).
- Step 3 regresses to `count=0` → the query did not filter (a finder-pipeline regression,
  not a scenario defect).

### BP-B7 (2026-10-05) — the telescope-goto regression fix (the M-M2 3rd strike)

**Files:** `tools/harness/test-e2e.ps1` (MODIFY — the `telescope-goto` scenario body ONLY:
the three hunks below). **NO product-code change** — the goto feature is GREEN (runs
169/170/174/179/182/184 all passed it).

**Root cause:** Part 1's j/w walk assumes a FRESH open of Reader.cs (the caret at line 1
col 0). In a full-suite run Reader.cs may already be open (an earlier scenario, or the
runner's scenario retry) — re-opening an open tab RESTORES the last caret, so the walk from
the stale position lands off `Shared` → the gather resolves nothing →
`definitions gathered count=0` (DefinitionFinder.cs:50) → the 0-hits rule opens the overlay
(`open finder=Definition candidates=0`) instead of the direct jump. Exactly the Part-4 race
(runs 165/166) that Part 4 already fixed with the `gg` normalization (test-e2e.ps1:1927-1937)
— Part 1 never received the equivalent.

**The audit (all four parts, the same race):**
- **Part 1** (Reader.cs, j×4 + w×2 → Shared): the race — THE 3rd-strike signature (run 183).
  FIX: `gg` normalization + the single bounded re-walk.
- **Part 2** (GotoProbe.cs, w×2 → GotoProbe): the same fresh-open assumption (the NOTE at
  ~:1849-1853 pins "the caret after opening is line 1 col 0"). FIX: `gg` normalization only.
- **Part 3** (Shared.cs, j×2 + w×4 → Value): the same race — Part 1's direct jump ALREADY
  opened Shared.cs, and a full-suite earlier scenario (e.g. `telescope-references`) may have
  left the caret elsewhere. FIX: `gg` normalization only.
- **Part 4** (IShape.cs): ALREADY FIXED (the `gg` normalization, ~:1936-1937) — UNCHANGED.

**The mechanism (PINNED — one mechanism, the hub's RECOMMENDation):** the `gg` caret
normalization in Parts 1-3 (mirroring Part 4's proven fix) + a SINGLE bounded re-walk in
Part 1 ONLY, triggered by the observed 0-gather signature. The re-walk covers the
Roslyn-readiness residual (the semantic model not yet computed for the freshly opened file —
the same gather can return 0 with the caret CORRECT). Parts 2-3 get the normalization only:
it fixes their root cause deterministically, and re-walks everywhere would triple the
harness surface and mask real regressions.

**Change — three hunks:**

**Hunk 1 — Part 1: REPLACE the walk+fire block** (from the `Enter-NormalContext $vs` after
Part 1's `Close-Telescope` through the `goto line=1$` assertion) with the
normalize+fire+re-walk loop:

```powershell
    # BP-B7 (the M-M2 3rd-strike regression fix — run 183): normalize the caret to line 1
    # col 0 before the walk (Part 4's proven fix: re-opening an ALREADY-OPEN tab restores
    # the last caret, so the j/w walk from a stale position lands off the symbol and the
    # gather returns 0 -> 'definitions gathered count=0' -> 'open finder=Definition
    # candidates=0' instead of the direct jump), then fire; if the gather STILL returns 0
    # (the Roslyn-readiness residual), re-walk ONCE. `g` is not hook-interesting, so gg
    # falls through to VsVim like w/j/k (Part 4's comment). The re-walk is IN-CONTRACT: a
    # pass after it is a PASS, not a runner-level flake.
    foreach ($attempt in 1..2) {
        Enter-NormalContext $vs
        Assert-VsFocused $vs 'goto-definition caret positioning'
        Send-Tap $script:VkG; Start-Sleep -Milliseconds 200   # gg -> line 1, first non-blank (col 0)
        Send-Tap $script:VkG; Start-Sleep -Milliseconds 200
        Send-Tap $script:VkJ; Start-Sleep -Milliseconds 200   # j -> line 2
        Send-Tap $script:VkJ; Start-Sleep -Milliseconds 200   # j -> line 3
        Send-Tap $script:VkJ; Start-Sleep -Milliseconds 200   # j -> line 4
        Send-Tap $script:VkJ; Start-Sleep -Milliseconds 200   # j -> line 5
        Send-Tap $script:VkW; Start-Sleep -Milliseconds 200   # w -> return
        Send-Tap $script:VkW; Start-Sleep -Milliseconds 200   # w -> Shared

        $idx = Get-LogCacheIndex $logPath
        & $dteCmd -DevenvPid $vs.Id -Command $gotoDefCmd | Out-Null
        # BOUNDED NON-THROWING wait (harness-common.ps1 Wait-NewLogLineAfter) for the
        # direct jump; on the observed 0-gather race signature, re-walk ONCE; any other
        # failure (or a second consecutive failure) falls through to the asserts below,
        # which throw with the real signature.
        if (Wait-NewLogLineAfter $logPath $idx "$($script:PfxTel)goto-direct finder=\S+ file=.*Shared\.cs line=1$" 15000) { break }
        if ($attempt -eq 2) { break }
        if (-not (Wait-NewLogLineAfter $logPath $idx "$($script:PfxTel)definitions gathered count=0" 2000)) { break }
    }
    Assert-NewLogLineAfter $logPath $idx "$($script:PfxTel)goto-direct finder=\S+ file=.*Shared\.cs line=1$" 'goto-definition single hit jumped directly' 15000
    Assert-NewLogLineAfter $logPath $idx "$($script:PfxTel)goto line=1$" 'the direct jump opened Models/Shared.cs at line 1' 15000
    $doc = Wait-ActiveDocumentMatch $vs.Id 'Shared\.cs' 8000
    if (-not $doc) { throw 'goto-definition direct jump did not activate Models/Shared.cs' }
```

(PowerShell's `foreach` creates no variable scope — `$idx` after the loop is the LAST
attempt's snapshot, so the asserts attribute to the final fire; a break on success
attributes to the successful attempt. Worst-case added latency on the race path: ~15s wait +
~2s signature check + one re-walk — far inside the `-TimeoutSec` budgets.)

**Hunk 2 — Part 2: INSERT after `Assert-VsFocused $vs 'goto-definition multi-hit caret
positioning'`** (before the first `w` tap):

```powershell
    # BP-B7 audit: the same restored-caret race as Part 1 — the NOTE's "caret after opening
    # is line 1 col 0" assumption only holds for a FRESH open; gg makes it hold for a
    # re-opened tab too.
    Send-Tap $script:VkG; Start-Sleep -Milliseconds 200   # gg -> line 1, first non-blank (col 0)
    Send-Tap $script:VkG; Start-Sleep -Milliseconds 200
```

**Hunk 3 — Part 3: INSERT after `Assert-VsFocused $vs 'goto-references caret positioning'`**
(before the first `j` tap):

```powershell
    # BP-B7 audit: the same restored-caret race (Part 1's direct jump already opened
    # Shared.cs; a full-suite earlier scenario may have left the caret elsewhere) —
    # normalize first.
    Send-Tap $script:VkG; Start-Sleep -Milliseconds 200   # gg -> line 1, first non-blank (col 0)
    Send-Tap $script:VkG; Start-Sleep -Milliseconds 200
```

**Verify-with:**
- `pwsh tools/harness/test-e2e.ps1 -Tests telescope-goto -TimeoutSec 900` → GREEN: Part 1's
  contract on the FIRST walk (`goto-direct finder=\S+ file=.*Shared\.cs line=1$` +
  `goto line=1$` + the active document Shared.cs); Parts 2-4 unchanged and GREEN.
- The full 44-scenario fresh-boot gate (BP-B6 rev 3, step 9) — `telescope-goto` GREEN
  outright, NO pass-on-retry allowance.
- The re-walk is observable in the log only as TWO `definitions gathered` lines within
  Part 1 (attempt 1's `count=0` + attempt 2's `count=1`) — a pass either way; the
  runner-level flaky ledger does NOT count it.

**Fails-if:**
- `definitions gathered count=0` on BOTH attempts (the asserts throw) → NOT the caret race
  anymore (gg normalized it) — the residual is a real gather failure: inspect the Roslyn
  workspace state at the fire. If it persists across runs, ESCALATE to the hub (a
  product-side retry in `DefinitionFinder` is a PLAN decision — do NOT self-heal).
- The re-walk fires on EVERY run (two `definitions gathered` lines every time) → the
  normalization did NOT fix the primary race — re-audit the walk (is `gg` landing on line 1?
  does Reader.cs still match the comment's pinned content?) — do NOT add a third attempt.
- Part 2/3 fail on the walk after the gg insertion → the gg taps did not reach VsVim (check
  `Enter-NormalContext` — the editor must be in VsVim NORMAL mode; `g` falls through only
  then — the same precondition as Part 4's proven usage). Do NOT substitute `1G`/other
  motions without a hub decision.

### BP-B6 rev 3 (2026-10-05) — the gate (the ledger closed; the allowance removed)

**Files:** none (verification-only step; executed by the verification-agent at VERIFY).

**Change (supersedes BP-B6 rev 2's items 3-4 ONLY; the arithmetic + the trust rule are
UNCHANGED):**

1. **Arithmetic (unchanged):** Telescope.Tests **268**/0; NeoVisual.Tests **191**/0 (the
   runner's OWN summary, staggered — never a self-report); e2e **44** registered.
2. **The flaky ledger — CLOSED:** `telescope-goto` cumulative count hit **3** (base 1, run
   172 + 1, runs 181/182 + 1, run 183) → the M-M2 3rd-strike upgrade FIRED → RECLASSIFIED A
   REGRESSION → fixed by BP-B7. The pass-on-retry allowance is REMOVED: at the next gate
   `telescope-goto` must be GREEN on its FIRST run. The in-scenario re-walk (BP-B7) is
   in-contract and never counts as a flake.
3. **The gate (the same 9 steps; step 9's expectation revised):** (1) `dotnet build` → 0
   errors; (2) Telescope.Tests → 268/0; (3) NeoVisual.Tests → 191/0 (the runner's summary,
   staggered with (2)); (4) `-List` → 44 with `telescope-recent`; (5) `-SelfCheck` → PASS;
   (6) doc-refs lint → 0 unresolved; (7) doc-content lint → PASS; (8)
   `-Tests telescope-recent,telescope-goto -TimeoutSec 900` → BOTH GREEN; (9) the full
   44-scenario fresh boot (NO `-NoBootstrap`) `-TimeoutSec 3600` → GREEN — **`telescope-goto`
   included, NO pass-on-retry allowance** (a failure is a regression → the debug-agent, not
   the ledger).

**Verify-with:** the gate outputs above; `RESULT: PASS (all 44 scenario(s))`.

**Fails-if:** `telescope-goto` failing at step 8/9 → the BP-B7 edit is wrong or incomplete
(check BP-B7's Fails-if list) — a REGRESSION, never allowlisted. Any scenario OTHER than
`telescope-recent`/`telescope-goto` failing → a Gap 4 regression (check the trace).

## Verification Trace

> REV 2 (2026-10-05): the three new rows (the registry count pin, the eager hookup, the probe
> diagnostic) are from the failed VERIFY (runs 181/182); the GREEN rows' stale counts
> corrected (Telescope 268, e2e 44).
>
> REV 3 (2026-10-05, iteration 2): two NEW rows from the second failed VERIFY (runs 183/184) —
> the `telescope-recent` Step-3 structural impossibility (→ BP-B1 rev 2) and the
> `telescope-goto` M-M2 3rd-strike REGRESSION (→ BP-B7, moved OUT of the allowlist). The
> product-code rows are unchanged (BP-3 rev 2 VERIFIED WORKING — the run-181 signature gone).

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_RecentFilesFinder_*` (the hermetic set) | BP-1/2, BP-8 | GREEN (landed): the gather/display/open/preview-jump/determinism — 268/0 |
| `Run_ResultsColumns_Recent_*` | BP-7, BP-8 | GREEN (landed): the Recent catalog (file,dir) |
| `Run_ActionsRegistry_TelescopeMapsToFinder` (stays GREEN) | BP-5 | the derived entry keeps the equality fixture green |
| `Run_ActionsRegistry_ContainsAllBuiltins` (**RED at run 181: "Expected [17] but got [18]"**) | **BP-5 rev 2** | GREEN: `Assert.Equal(18, Actions.Registry.Count)` + the `names[]` array contains `"telescope-recent"` (a unit count pin — no log line); the suite count STAYS 191 |
| e2e `telescope-recent` — `recent files gathered count=0` + `open finder=Recent candidates=0` (**RED ×2 at runs 181/182, deterministic**) | **BP-3 rev 2** (the eager hookup) | GREEN: `[Telescope] recent files gathered count=(\d+)` with **n≥1** AFTER Step 1's `[Telescope] opened file: ...Order.cs` — the session MRU holds Order.cs from CONSTRUCTION time |
| the probe degradation observability (**NEW contract**) | **BP-3 rev 2** | `[Telescope] recent files probe unavailable: {msg}` logged ONCE when the reflection probe throws; its ABSENCE during a probe failure is itself a BP-3 rev 2 defect |
| e2e `telescope-recent` Step 3 — the `preview file=.*Order\.cs` assertion NEVER fires during typing (**RED ×2 at runs 183/184, deterministic — STRUCTURAL: the Step-3 Show is a PreviewEditorHost cache HIT and the line only fires on a rebuild; it could only pass if the most-recent-first ordering were WRONG**) | **BP-B1 rev 2** (the plan-owned scenario edit — HUB ADJUDICATION: ACCEPT) | GREEN: Step 2 (snapshot `$preOpen`) asserts `[Telescope] preview file=.*Order\.cs` — the unfiltered render's top-match preview IS the most-recent-first proof; Step 3 (snapshot `$preQuery`) asserts `[Telescope] results count=1 selected=0` — the filtered top match; Step 4 unchanged (`[Telescope] opened file: ...Order.cs`) |
| e2e `telescope-goto` Part 1 — `definitions gathered count=0` → `open finder=Definition candidates=0` (**REGRESSION — the M-M2 3rd strike, run 183; cumulative count 3; REMOVED from the allowlist**) | **BP-B7** (the `gg` caret normalization Parts 1-3 + Part 1's single bounded re-walk; NO product-code change) | GREEN on the first walk: `[Telescope] goto-direct finder=\S+ file=.*Shared\.cs line=1$` + `[Telescope] goto line=1$` + the active document Shared.cs; the in-scenario re-walk (on the 0-gather signature) is IN-CONTRACT — a pass after it is a PASS, not a runner-level flake |
| e2e `telescope-recent` (the full scenario) | BP-B1 rev 2, BP-1..7, **BP-3 rev 2** | GREEN: `open finder=Recent candidates=...` (≥1) + `results columns=file,dir` + the Order.cs top match (`preview file=.*Order\.cs`, asserted at Step 2 per BP-B1 rev 2) + `results count=1 selected=0` (Step 3) + `opened file:` (Step 4) |
| unit gate | BP-8, **BP-5 rev 2**, BP-B6 rev 3 | Telescope **268**/0; NeoVisual **191**/0 (the runner's OWN summary — never a self-report); the build 0 errors |
| lints + `-List` | BP-B6 rev 3 | 0 unresolved; PASS; **44** scenarios |

**Known-RED allowlist (REV 3, 2026-10-05):**

- **`telescope-goto` — REMOVED from the allowlist (REV 3).** The cumulative flaky count hit
  **3** (base 1, run 172 + 1, runs 181/182 + 1, run 183) → the M-M2 3rd-strike upgrade FIRED:
  it is a RECLASSIFIED REGRESSION, fixed by BP-B7 (the harness-layer `gg` normalization +
  the bounded re-walk). At the next gate it MUST be GREEN on its first run — it is NOT
  allowlisted as flaky, and a failure there routes to the debug-agent, not the ledger. The
  in-scenario re-walk is in-contract (a pass is a PASS).
- **The gate-trust finding (not a test failure):** the build-agent's run-181 "NeoVisual.Tests
  191/0" self-report was FALSE (fresh output: 190/1). The verifier re-runs both suites fresh
  and reads the runner's own summary (BP-B6 rev 2's trust rule) — a self-report is never
  evidence.
- The verifier must NOT flag the reflection-probe's best-effort nature (the session-MRU floor
  is the guaranteed path — now EAGER) or the probe's once-only diagnostic as spam.
- The verifier must NOT flag the `telescope-recent` Step-3 assertion-set change (BP-B1 rev 2)
  as a weakened contract: the top-match preview proof was MOVED to Step 2 (where the
  PreviewEditorHost cache-MISS path actually emits it), not deleted — the Step-2 line plus
  the Step-3 `results count=1 selected=0` plus the Step-4 `opened file:` together cover the
  original Step-2/3/4 contract.
- The in-flight plans' count drift: all counts above are the LANDED state re-reads at the
  gate (Telescope 268 / NeoVisual 191 / e2e 44).

## Execution Log

### Attempt 1 — RED + BUILD + VERIFY FAIL (iteration 1) (2026-10-05)

- **RED (e2e-test-builder): RED-CONFIRMED** — 10 unit tests (`Run_RecentFilesFinder_*` ×8 +
  `Run_ResultsColumns_Recent_*` ×2; + the `RecentTestDir` helper + the `WidthKinds` extension)
  → 20 CS0246 errors, ONLY the two planned symbols (`RecentFilesFinder` BP-2, `RecentFileHit`
  BP-1); the e2e scenario `telescope-recent` created (44 registered; ONE VS boot: `Telescope
  finder 'Recent' did not open` — the pinned RED form; the Step-1 MRU-populate control PASSED).
- **BUILD (build-agent, 2 dispatches — the first hit its step cap):** BP-1..BP-7 done
  (`RecentFileHit`/`RecentFilesFinder`/`RecentFilesGatherer` — the reflection probe + the
  session-MRU floor / the package registration + `OpenRecentFile` / `FinderNames["telescope-recent"]`
  / the `f,e` binding (USER-CONFIRMED) / `FinderColumns.Recent()` with the FULL ctor) + BP-B2
  (~95%) + (dispatch 2) the §8 residual + BP-B3 (AGENTS.md 6 edits) + BP-B4 (SKILL.md 4 edits);
  build 0 errors; Telescope **268/0**; both lints PASS. Deviations adjudicated (hub): the BP-5
  placement (the anchor drift) -> ACCEPT; the opener style -> ACCEPT; the §5.2/§8 corrections
  -> ACCEPT; the spec §7 stale tail fixed by the hub.
- **VERIFY (verification-agent) — FAIL (iteration 1):** 42/44 (runs 181/182). TWO real
  regressions: (1) `Run_ActionsRegistry_ContainsAllBuiltins` — the pinned count 17 vs the
  derived 18 (a PLAN GAP: no step owned the count pin; ALSO a GATE-TRUST FINDING — the
  build-agent's "191/0" self-report was FALSE, fresh output 190/1); (2) `telescope-recent`
  gathered count=0/candidates=0 — the session-MRU floor hooked `DocumentOpened` LAZILY inside
  `Gather()` → structurally empty for the open-FIRST strategy. Plus `telescope-goto` flaky
  count 2 (pass-on-retry).
- **Cost:** delegations: 5 | VS boots: 2 | iterations: 1

### Attempt 2 — RE-PLAN + BUILD + VERIFY FAIL (iteration 2) (2026-10-05)

- **RE-PLAN (implementation-planner):** BP-3 rev 2 (the EAGER `HookSessionEvents()` at
  construction + the merge order probe-first-then-floor + the NEW M-M7 literal
  `[Telescope] recent files probe unavailable: {msg}` logged ONCE + `ThrowIfNotOnUIThread`);
  BP-5 rev 2 (the PLAN-OWNED count-test edit: 17 → 18 + the names[] entry — the M34
  supersession precedent); BP-B6 rev 2 (the runner's-own-summary trust rule). 4a RE-APPROVED
  (1 minor: the untested mixed-path merge — filed as a hardening candidate; 2 nits).
- **BUILD (build-agent): BP-3 rev 2 + BP-5 rev 2 + the doc rows done** — both suites GREEN on
  FRESH runner output (Telescope 268/0, NeoVisual 191/0 — the count-test regression fixed);
  both lints PASS. No deviations.
- **VERIFY (verification-agent) — FAIL (iteration 2):** 42/44 (runs 183/184). **BP-3 rev 2
  VERIFIED WORKING** (the run-181 signature GONE — the probe literal once, count=15/6, the
  Order.cs top match, the open works; the product code is GREEN). TWO harness-layer blockers:
  (1) `telescope-recent`'s Step-3 `preview file=` assertion STRUCTURALLY UNSATISFIABLE (the
  PreviewEditorHost cache-HIT path never logs it when the most-recent-first ordering WORKS —
  inverted vs the pinned strategy; the hub ADJUDICATED ACCEPT — a plan-owned scenario edit);
  (2) `telescope-goto`'s 3rd flake → the M-M2 3rd-strike UPGRADE to a REGRESSION (the flaky
  ledger closed).
- **Cost:** delegations: 9 | VS boots: 4 | iterations: 2

### Attempt 3 — RE-PLAN + DEBUG-BUILD + VERIFY PASS — GREEN (2026-10-05)

- **RE-PLAN (implementation-planner, iteration 2):** BP-B1 rev 2 (the Step-2 snapshot-attributed
  `preview file=.*Order\.cs` top-match proof — the only place the cache-MISS line can fire — +
  the Step-3 tightening to `results count=1 selected=0`, sound by the uniqueness argument);
  NEW BP-B7 (the `telescope-goto` harness fix: the `gg` normalization Parts 1-3 + Part 1's
  SINGLE bounded re-walk on the 0-gather signature — in-contract pass, never a flake);
  BP-B6 rev 3 (the counts unchanged; the `telescope-goto` flaky ledger CLOSED — GREEN outright
  at the next gate). 4a RE-APPROVED (3 nits, none blocking; the risk-scan concerns verified
  SOUND against the source — the per-overlay-open host lifetime kills the cache-HIT risk).
- **BUILD (build-agent): BP-B1 rev 2 + BP-B7 done** (the five hunks; the parse OK; build
  0 errors; both suites fresh-GREEN 268/0 + 191/0). No deviations (the timeout nit folded in).
- **VERIFY (verification-agent) — the FINAL GATE: PASS.** Harness-health first: `-SelfCheck`
  PASS, `-List` 44, both lints PASS. Units staggered, fresh summaries: Telescope **268/0**,
  NeoVisual **191/0**. Full e2e FRESH boot (`-TimeoutSec 2400`, run 185): **44/44 GREEN** —
  `telescope-recent` verified end-to-end (the probe literal ONCE, `gathered count=16`, the
  Step-2 top-match preview proof, the Step-3 tightened assertion, the Step-4 open);
  `telescope-goto` GREEN OUTRIGHT (the first-walk direct jump; the re-walk NEVER fired — the
  gg normalization fixed the primary race deterministically); `seed-leak`/`seed-reset` PASS.
  Zero deviations, zero new flakes.
- **Failure-log sweep:** 10 entries read, 0 fixed, 0 queued, 0 annotated (all entries already
  carry FIXED resolutions).
- **Cost:** delegations: 14 | VS boots: 6 | iterations: 2
