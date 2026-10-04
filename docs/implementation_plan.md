# Plan — Feature 7: the overlay PANE architecture + 3-pane focus (Ctrl+H/J/K/L + left-click)

> **HANDOFF (2026-10-04, neovim_hub):** gate-APPROVED in the planning-hub session (G2:
> APPROVE; the handoff USER APPROVED) — plan verbatim from
> `.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-143017/plans/plan-feature7.md`.
> Handoff corrections (stale era data; the contract unchanged):
> 1. **Queue position:** ALL THREE predecessors are GREEN (columns `492c6c9`, goto `90e6245`,
>    gap 11 `c862357`) — THIS plan is FIRST in queue; the post-columns dependency is satisfied.
> 2. **Counts:** the CURRENT baselines are Telescope.Tests **224** (not 199/212) → +34 net =
>    **258** expected at GREEN (the `<ACTUAL>` re-read at the gate is the safety net);
>    NeoVisual.Tests **191** (unchanged); e2e **42 → 43** (the new `telescope-focus-panes`).
> 3. **The stale flake note (the footer):** the `neovisual-window-management` ×2-cumulative
>    note is STALE — that 3rd-strike regression was FIXED 2026-10-04 and runs 168-174 were
>    flake-free for it. The known-RED allowlist is **NONE**; per-scenario flaky counts start
>    at **0**, EXCEPT `telescope-goto` carries a base of **1** (pass-on-retry in run 172 — a
>    pre-existing caret/Roslyn race; the 3rd strike = the M-M2 regression upgrade).
> 4. **The harness `focus target=` sites:** the plan's :1228/:1234/:1688/:1732 cites PREDATE
>    the git-bindings scenario insertion (test-e2e.ps1:902-1056) — re-locate by CONTENT (the
>    established anchor-by-text convention; the patterns are byte-stable).

> **Lane: feature (e2e ENABLED).** **M-M7 HARD TRIGGER** — the `focus target=` diagnostic
> contract changes (`List|Preview` → `Input|List|Preview`), so the `telescope-preview` /
> `telescope-preview-motions` scenario assertions UPDATE.
>
> **Source:** the queue's Feature 7 + the user's 2026-10-04 ARCHITECTURE DIRECTIVE (superseding
> the earlier logical-target choice): *"if its better for long term architecture. make the
> telescope windows focusable by left click also. if for that they need to have separate focus
> so be it. no need to just make it logical if that is gona cause problems later. all i want in
> the end is that this is just one overlay. but it is modular. easy to add panes. so it can be
> later reused to create lazygit... and anything else we need."*
>
> **Design (user-decided):** REAL focus movement between panes (not a logical target) — each
> pane is focusable (real WPF focus), left-click focuses a pane, and **Ctrl+H/J/K/L = focus
> left/down/up/right** — the CARDINAL spatial mapping (the same keys and mental model as the
> extension's window navigation, one level down). Because the panes are actual focusable
> windows with layout geometry, the move is GEOMETRIC: check which pane lies in the requested
> direction of the focused pane and focus it — the same pipeline as the
> `WindowNavigationEngine` (in-direction → aligned → closest/adjacent), reused over the pane
> rects. The overlay is refactored into a **modular pane host**: the prompt, the results list,
> and the preview become panes behind one composable contract — the reusable core the
> deferred lazygit overlay (and any future surface) builds on.
>
> **Research:** telescope.nvim is prompt-centric (no built-in focus switch) — our pane model
> is a deliberate superset (real focus + spatial keys + left-click), which telescope does not
> offer; that is fine — the user's directive is the spec.
>
> **Ground truth (recon, file:line):** `_focusTargetModel` (TelescopeOverlay.cs:79-82); the
> pure machine `FocusTargetModel` (Telescope/Overlay/Utils/FocusTargetModel.cs:32-55) —
> `FocusTarget{List,Preview}` (:9-13, the member names ARE the diagnostic tokens), Handle:
> CtrlL→Preview :42-44, CtrlH→List :45-47, Esc-in-preview→List :48-50; the switch path
> MapKey CtrlH/CtrlL :704-705 → OnPreviewKeyDown :557-564 (log :561, `FocusTargetUi()` :562);
> `FocusTargetUi()` :676-687; the harness `focus target=` asserts: test-e2e.ps1:1226,1232,1681,1725.
> **Post-columns dependency (binding):** the columns plan (FIRST in queue) swaps the results
> host (TextBox→ListView) and the preview host (RichTextBox→an IWpfTextView host) — THIS PLAN
> EXECUTES AFTER IT (fourth in queue: columns → goto → gap 11 → feature 7) and wraps those
> hosts in panes (no rewrite of the hosts themselves — the pane layer composes them).
>
> **Deferred (user decision 2026-10-04):** the lazygit overlay — "one of the last things we
> do… bonus feature when the core of the extension is finished". THIS plan delivers the pane
> host it will reuse; the lazygit panels are future panes.

## Goal

Refactor the Telescope overlay into a **modular pane host**: the prompt, the results list, and
the preview become panes behind one composable contract (`IPane` + `PaneHost`), each focusable
with REAL WPF focus (left-click focuses; **Ctrl+H/J/K/L = focus left/down/up/right** — the
Cardinal directional mapping over the pane geometry, the same mental model as the window
navigation one level down; no wrap — a direction with no pane is a logged no-op). The overlay
stays one modal window; adding a pane = implementing the contract + registering it — the reuse
path for the lazygit overlay.

## Approach

**D1 — The pane contract.** NEW `Telescope/Overlay/Utils/Panes/` (the exact file split the
planner pins):
- `IPane` — `Id` (the diagnostic token: `Input|List|Preview`), the content `FrameworkElement`,
  `IsFocusable`, `Activate()`/`Deactivate()` (the pane's focus-entry/exit behavior: the caret
  style, the selection visibility), a key-routing participation hook (see D3), and its LAYOUT
  RECT (the geometry the directional move needs — the pane's bounds within the overlay).
- `PaneHost` — the ordered pane registry + the focused-pane tracking + the DIRECTIONAL focus
  movement + left-click normalization (a pane is focused by clicking it — the natural WPF
  focus PLUS a `PaneHost` notification so the diagnostic + the active-pane visuals update).
- **The directional move (the user's 2026-10-04 revision): Ctrl+H/J/K/L = focus
  left/down/up/right** — GEOMETRIC, not a fixed key→pane map: the machine checks which pane
  lies in the requested direction of the focused pane's rect and focuses it — the SAME
  pipeline as the `WindowNavigationEngine` (drop empty → in-direction → aligned → the
  closest gap + the largest adjacency within the divide), reused/adapted over the pane rects
  (the planner decides: generalize `WindowNavigationEngine` or mirror its pipeline in a pure
  `PaneNavigationEngine` — RECOMMEND mirror, keeping the window engine untouched). The layout:
  the Input at the bottom (full width), the List left, the Preview right — so Ctrl+H from the
  Preview → the List; Ctrl+L from the List → the Preview; Ctrl+J from the List/Preview → the
  Input; Ctrl+K from the Input → up (the List/Preview tie — PIN the tie-break: the
  Cardinal's last-in-list order, i.e. the Preview, OR the last-focused — the planner pins ONE
  and tests it). **NO wrap**: a directional move with no pane in that direction is a NO-OP
  (the target stays; the m47-style outcome is the diagnostic — the planner pins whether a
  no-op logs a distinct reason or simply does not log a change).
- The pure, testable core: the directional decision (the pane rects + the focused pane + the
  direction → the target or null) extracted dependency-free — the `FocusTargetModel`
  successor (the planner decides evolve-vs-replace; the geometry-based machine REPLACES the
  fixed key→pane map).

**D2 — The three panes.** `PromptPane` (the TextBox — naturally focusable), `ListPane` (the
ListView — becomes **Focusable=true**; today it is Focusable=false), `PreviewPane` (the editor
host — the columns plan's BP-P4 shape; the IWpfTextView host is focusable). The overlay's XAML
composition wraps each host in its pane.

**D3 — Key routing (the regression surface — design carefully).** The overlay's
`OnPreviewKeyDown` interceptor runs FIRST (tunneling) and dispatches by the FOCUSED pane:
Input → the prompt/insert-mode keys; List → the selection keys (j/k/gg/G/Enter); Preview →
the preview motions. Keys a pane does not consume fall through to the focused control's own
handling (e.g. the ListView's native keys) — the planner pins the consume-vs-fallthrough
contract per pane and pins it in tests (the j/k swallow; the ListView's native Up/Down still
works when the overlay does not claim it — decide: the overlay claims j/k/gg/G; the native
arrows remain live). The prompt keeps insert/normal mode semantics (the existing
`OverlayKeyHandler` state machine is UNTOUCHED — the pane layer routes TO it).

**D4 — The modal guarantee.** The overlay window keeps overall focus (`Deactivated`→close
unchanged); pane focus never escapes to VS (the panes are the overlay's own visual tree —
WPF focus within one window cannot leave it). The planner pins a test/e2e assertion
(the overlay never deactivates on pane switches).

**D5 — Diagnostics (M-M7).** `focus target=Input|List|Preview` — logged on every focus change
(keys AND left-click). The harness's 4 two-state assertion sites UPDATE (re-verified
post-columns). The initial pane on open: **Input** (the prompt focused in insert mode — the
initial-target change from List; the planner verifies every `focus target=` consumer incl.
`telescope-open`).

**D6 — Tests.** `tests/Telescope.Tests`: the pane-focus machine tests (the directional cases:
H from Preview→List, L from List→Preview, J from List/Preview→Input, K from Input→the
tie-break target, the no-ops at the edges, the alignment/adjacency tie cases) + the
pane-contract tests — RED-first. The existing M34 `FocusTargetModel` tests are replaced by the
geometry-based machine's tests. Suite delta: +40 (an `<ACTUAL>` re-read; the columns plan moves
Telescope.Tests to 199 first → 239).

**D7 — e2e (ENABLED).** The M-M7 assertion updates + a NEW scenario `telescope-focus-panes`:
the directional sequences (Ctrl+J from the List→Input, Ctrl+H from the Preview→List,
Ctrl+L from the List→Preview, Ctrl+K from the Input→the tie-break target, the no-op cases) —
asserted from the log; the left-click path is NOT keyboard-injectable (the harness injects
keys, not mouse) — unit-pinned + manually verified; the e2e covers the key path. Created RED;
executed at VERIFY.

**D8 — Docs.** spec.md §2.5 (the pane architecture) + §4 (the contract change) + §5 counts;
AGENTS.md; SKILL.md (the overlay description + the pane-host reuse note for the lazygit
future); progress.md (the item → DONE at GREEN).

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | Ctrl+J focuses the pane BELOW (the Input from the List/Preview) | `focus target=Input` | unit pane-focus tests; e2e `telescope-focus-panes` |
| AC2 | Ctrl+H focuses the pane to the LEFT (the List from the Preview); Ctrl+L to the RIGHT (the Preview from the List) | `focus target=List` / `=Preview` | unit; e2e |
| AC3 | Ctrl+K focuses the pane ABOVE (from the Input — the List/Preview tie-break PINNED); a direction with no pane is a NO-OP (no wrap) | the tie-break's target line; the no-op behavior | unit (the tie-break + the no-op pinned) |
| AC4 | Left-click focuses a pane (real WPF focus) | `focus target=<pane>` on click | unit (the normalization); manual (not injectable) |
| AC5 | The overlay stays modal (pane focus never reaches VS) | the overlay never deactivates | e2e (the existing close-on-deactivation guard stays green) |
| AC6 | The key routing dispatches by the focused pane (j/k on List; motions on Preview; typing on Input) | the existing per-pane lines | unit + e2e (the existing scenarios stay green) |
| AC7 | The two-state assertions are migrated (M-M7) | the updated harness lines | e2e telescope-preview/-motions at VERIFY |
| AC8 | The pane host is the reusable core (the lazygit path) | (architectural — the contract + the registry) | unit (the pane-contract tests) |

## Files to be touched

- **Created:** the pane files under `Telescope/Overlay/Utils/Panes/` (the contract + the host +
  the three panes — the exact split the planner pins).
- **Modified:** `Telescope/Overlay/TelescopeOverlay.cs` (the composition + the routing +
  the diagnostic — POST-COLUMNS state), `Telescope/Overlay/Utils/FocusTargetModel.cs` (evolve
  into the pane-focus machine), `tools/harness/test-e2e.ps1` (D5/D7), `tests/Telescope.Tests/Program.cs`
  (D6), `docs/spec.md`, `AGENTS.md`, `SKILL.md`, `docs/progress.md` (D8).
- **Not touched:** the hosts themselves (the columns plan's ListView/editor host — wrapped, not
  rewritten), `OverlayKeyHandler`, `TextMotionNavigator`, the hook/InputHandler.

## Open risks

1. **The key-routing regression surface (HIGH — the plan's core risk).** Real focus means the
   focused control's own key handling competes with the overlay's routing. Mitigation: the
   PreviewKeyDown tunneling interceptor dispatches first (D3); the consume-vs-fallthrough
   contract is pinned per pane in tests; the existing scenarios (telescope-navigate/-mode/
   -preview-motions/-prompt-motions) are the regression net at VERIFY.
2. **The post-columns dependency (medium).** Fourth in queue — the hosts are the columns
   plan's output; the pane layer composes them.
3. **The ListView focusable change (medium).** `Focusable=true` on the ListView may change
   the selection visuals/focus visuals — the planner pins the style (the active-pane indicator
   = the WPF focus visual, kept subtle).
4. **The initial-target change (low).** List→Input at open — the consumers verified (D5).

## Build Plan

> **Aggregated (Stage 3)** from `artifacts/feature7-section-{a,b}.md` — the authoritative full
> detail (the pane code, the routing table, the 14 composition edits, the doc edits) lives
> there; the steps below are the contract. **e2e ENABLED.**
>
> **Pinned contracts:** the routing table R0-R5 (the focus machine first via tunneling;
> Input = typing falls through; List claims j/k/gg/G/Enter/q/Esc/i/a/A/I, the arrows stay live
> + adopted; Preview claims the motions; Tab swallowed); EVOLVE `FocusTargetModel` in place
> (the M34 history carries); **7 pane files** under `Panes/` (incl. the pure
> `PaneNavigationEngine` — the mirrored Cardinal pipeline), flat `namespace Telescope.Overlay`;
> the M-M7 tokens `Input|List|Preview` + the NEW `[Telescope] focus no-op: no pane {direction}
> from {pane}` literal; the harness's 4 `focus target=` sites are at :1228/:1234/:1688/:1732
> (the recon's cites had drifted — Section B re-verified); the initial-target consumers (14)
> need NO edits (none reads the initial label — BP-B2's audit; NOTE: the two preview scenarios'
> Ctrl+L sites DO gain the focus preamble per BP-B1 rev 2); the suite delta **+40 gross / +34
> net** (6 replaced M34 tests; the `<ACTUAL>` re-read at the gate is the safety net; the
> columns plan lands first → 199 → 233).
>
> **RE-PLAN 2026-10-05 (VERIFY final gate FAILED — fail-twice regression, ONE shared root
> cause; iteration 1 for this item):** runs 176 (fresh boot) + 177 (isolated fresh-boot
> retry) — 3 real REDs: `telescope-focus-panes` ("never saw: Ctrl+K from open moved UP to the
> Preview... `focus target=Preview`" — actual `[Telescope] focus no-op: no pane up from
> Input`), and `telescope-preview` + `telescope-preview-motions` (both fail at their Ctrl+K
> preamble — the same expected/actual). The ENTIRE run-176 log has ZERO `focus target=List`
> and ZERO `focus target=Preview` lines — no Ctrl-chord pane switch ever succeeded live.
> **Root cause:** BP-A5's landed composition (artifact E2 implemented verbatim) KEPT the
> prompt docked `Dock.Top` (the pre-Feature-7 layout) instead of the plan-D1/§1.2-pinned
> **bottom-Input geometry** ("the Input at the bottom (full width), the List left, the
> Preview right" — pinned in this plan's Approach AND landed spec.md §2.5:204). With Input at
> top, the geometric engine CORRECTLY finds no pane ABOVE the Input → consumed NoOp → the
> logged no-op. The vertical axis is INVERTED live vs pinned (Ctrl+J from List/Preview→Input
> would equally no-op — the scenario never reached it). **Why the units passed:** the machine
> tests push SYNTHETIC rects matching the PINNED layout (`tests/Telescope.Tests/Program.cs`
> :3992-3994: List=(0,0,100,60), Preview=(100,0,200,60), Input=(0,60,300,40)) — a
> composition-level layout deviation is invisible to the pure machine;
> `PaneNavigationEngine.cs:83-87`'s own deviation comment documents the bottom-Input
> assumption the composition did not implement. **Decision:** RE-COMPOSE the pane layout to
> the pinned Input-bottom geometry (BP-A5 rev 2 below) — the plan-D1-conformant option; the
> alternative (re-pinning to Input-at-top) would change the approved contract + the unit
> tests' synthetic rects + the scenario + the M-M7 preambles — a much bigger blast radius.
> The M-M7 harness migration is CORRECT (the preambles wait for the right thing — Ctrl+K →
> `focus target=Preview`); only the source layout was wrong. Everything else GREEN at the
> gate: units (Telescope 258/0, NeoVisual 191/0), the harness health (43 registered, both
> lints, -SelfCheck), the other 40 scenarios, the seed guards, AC4's left-click wiring + unit
> pins, the initial pane = Input (LIVE-proven by the no-op line's "from Input"), the subtle
> chrome. **Gate expectations UNCHANGED by this re-plan:** Telescope.Tests **258**, NeoVisual
> Tests **191**, e2e **43** — the re-compose is composition-only, ZERO test/harness edits.
> **Ripple checks (each pinned — all clear):** (a) the centering math — `ApplyWindowWidth()`
> runs before the centering (:391) and the outer size is fixed (760×420,
> `SizeToContent.Manual`); the DockPanel re-arrangement is internal, `ShowOverlayAsync`
> untouched; (b) the `SizeChanged`→`RefreshPaneLayout` path — the pane rects recompute from
> the visual tree (:1181-1182), so the NEW layout self-reports (Input bottom, List/Preview
> above); (c) the preview's minimum width — the bottom Grid's ColumnDefinitions (the 260
> fixed `_resultsColumnDef` + the star) untouched; (d) `telescope-results-columns` — its
> assertions are log-based (`results columns={ids}`, `results count=...`); the ListView's
> host moves up but its `SizeChanged`→`ApplyComputedColumnWidths` (:270) and the render
> logic are untouched; (e) the opening animation/positioning — none exists (ShowDialog at
> the computed Left/Top :414-445); (f) the initial focus — `FocusInitialPane`→`FocusPrompt`
> unchanged (`Focus prompt => True, mode=insert` byte-unchanged); (g) the modal guard —
> `Deactivated`→`CloseOverlay` (:348-363) untouched.

- **BP-A1** — `[STAThread]` on the Telescope.Tests Main (the WPF-construction enabler for the
  pane fakes).
- **BP-A2** — evolve `FocusTargetModel` (the Input token, `PaneFocusKey`, the spatial map, the
  K-cycle + the pinned wrap Input→Preview, the click `Focus`, `ExitsInsert`) + 20 machine tests.
- **BP-A3** — `IPane`+`PaneChrome` / `PaneHost` (the registry, the activation order, the
  left-click seam) + 6 contract tests.
- **BP-A4** — `PromptPane`/`ListPane`(+`ListKeyMap`)/`PreviewPane`/`PaneSelectionSync` + 4 tests.
- **BP-A5** — the overlay composition (14 numbered edits: the panes built, the clicks wired,
  every focus path through the machine, `FocusTargetUi` retired). **REV 2 (2026-10-05
  re-plan — the LAYOUT RE-COMPOSE; SUPERSEDES artifact E2's `Dock.Top`):** the prompt pane's
  content docks **BOTTOM** (full width) and the List|Preview Grid fills the region ABOVE it —
  the plan-D1/§1.2-pinned geometry the unit tests' synthetic rects pin (List=(0,0,100,60),
  Preview=(100,0,200,60), Input=(0,60,300,40)); the real pixel proportions follow the
  overlay's EXISTING sizing logic (the prompt strip = its `Padding(10,6)` + the 14pt line ≈
  31 DIPs tall; the List/Preview region = the remaining height under the title bar; the List
  column = the existing 260 fixed `_resultsColumnDef`, the Preview = the star column — the
  engine's decisions depend on the TOPOLOGY, not the proportions). The exact edits in
  `Telescope/Overlay/TelescopeOverlay.cs`:
  (1) **:202** `DockPanel.SetDock(_promptPane.Content, Dock.Top)` →
  `DockPanel.SetDock(_promptPane.Content, Dock.Bottom)` — the child ADD ORDER stays
  (titleBar :178, prompt :203, bottom Grid :309): DockPanel docks titleBar top, prompt
  bottom, and the LAST child (the Grid — `LastChildFill` default true) fills the MIDDLE, so
  the List (col 0) + Preview (col 1) sit ABOVE the full-width Input;
  (2) **:186** promptHost `BorderThickness = new Thickness(0, 0, 0, 1)` →
  `new Thickness(0, 1, 0, 0)` — the separator line moves to the prompt's TOP edge (between
  the prompt and the list above; visual-only, no test/harness assertion touches it);
  (3) the stale comments :180-181 and :205-206 ("The bottom area is a Grid") → the Grid is
  the MIDDLE region above the bottom-docked prompt. NO other composition edit changes
  (E1/E3-E9 stand as landed); `RefreshPaneLayout` (:1176-1188) is UNTOUCHED — it measures the
  visual tree (`TransformToVisual` → `PaneRect`), so the re-composed layout automatically
  feeds the corrected rects on `SizeChanged` (:307) + `ContentRendered` (:334-341).
- **BP-A6** — the routing dispatch by the focused pane + the native-arrow delta-replay sync +
  the Tab swallow.
- **BP-A7** — the gate: build + the full suite. **REV 2:** expectations UNCHANGED by the
  layout re-compose (composition-only): Telescope **258**, NeoVisual **191**, 0 failed.
- **BP-B1** — the M-M7 migration of the 4 `focus target=` sites (the patterns byte-identical;
  the comments → three-state).
- **BP-B2** — the initial-target consumer audit (14 consumers, all NO-EDIT verdicts + the
  regression net).
- **BP-B3** — the NEW `telescope-focus-panes` scenario (RED at creation, VERIFY execution;
  the exact body + the insertion :1734/:1736).
- **BP-B4** — the harness header comment.
- **BP-B5..B10** — the docs: spec §2.5 (the pane architecture), §2.2 (the key-files rows), §4
  (the contract line), §5 (the counts + the scenario list); AGENTS.md (9 edits incl. the :245
  diagnostic line); SKILL.md (6 edits incl. the lazygit pane-host reuse note).
- **BP-B11** — progress.md item 7 → DONE (AT GREEN only).
- **BP-B12** — the suite arithmetic + the final gate (the build, both unit suites, the full
  e2e, the doc-ref lint). **REV 2:** expectations UNCHANGED by the layout re-compose
  (composition-only): 258/191 units, 43 scenarios, 0 unresolved lints; the three
  fail-twice REDs (`telescope-focus-panes`, `telescope-preview`, `telescope-preview-motions`)
  must be GREEN, and the modal-guarantee re-verification (below) must pass.

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| the 20 evolved/new machine tests | BP-A2 | RED → GREEN: the spatial map, the K-cycle + the wrap, the click `Focus`, `ExitsInsert` |
| the 6 pane-contract tests | BP-A3 | RED → GREEN: the registry, the activation order, the left-click seam |
| the 4 pane tests | BP-A4 | RED → GREEN: the three panes + `PaneSelectionSync` |
| the M-M7 harness migration | BP-B1 | the 4 sites → three-state patterns |
| e2e `telescope-focus-panes` (RED then GREEN) | BP-B3, BP-A5/A6 | RED before the source lands; GREEN: the directional sequences (Ctrl+J→Input, Ctrl+H→List from the Preview, Ctrl+L→Preview from the List, Ctrl+K→the pinned tie-break target from the Input) + the logged no-ops |
| e2e `telescope-focus-panes` + `telescope-preview` + `telescope-preview-motions` — the 2026-10-05 fail-twice REDs (the layout re-compose) | **BP-A5 rev 2** | at each Ctrl+K preamble: `[Telescope] focus target=Preview` (Input→UP→the Preview — the larger-adjacency pick, 200 vs 100); at the Ctrl+H preambles: `focus target=List`; the run-176 symptom `[Telescope] focus no-op: no pane up from Input` must be GONE at those sites (that no-op survives ONLY at the pinned edges: `no pane up from List` :2165, `no pane left from Input` :2177) |
| e2e telescope-preview/-motions (M-M7) | BP-B1 | the updated three-state assertions GREEN |
| e2e the modal guarantee | BP-A5/A6 | the overlay never deactivates on pane switches — **PARTIAL at the 2026-10-05 gate** (the successful pane switches never executed live — the inverted layout consumed every Ctrl-chord as a no-op); **RE-VERIFIED after BP-A5 rev 2** (the pane switches now actually execute; the `overlay closed` line must NOT appear during the focus-panes scenario) |
| unit gate | BP-A7, BP-B12 | Telescope 239 (`<ACTUAL>`); the build 0 errors |
| lints + `-List` | BP-B12 | 0 unresolved; PASS; 42 scenarios |

**Known-RED allowlist: NONE plan-new.** CARRIED (the verifier must not flag): the
`neovisual-window-management` flake (**×2 cumulative — ONE more flake = the 3rd-strike
upgrade**, the W6 rule); the stale harness comment; the in-flight plans' count drift (re-reads).
Expected RED = the new/evolved unit tests + the new e2e scenario before the source lands. The
left-click path is unit-pinned + manual (not injectable).

**RE-PLAN 2026-10-05 allowlist notes (the verification-agent must not misreport):**
- The 3 fail-twice REDs (`telescope-focus-panes`, `telescope-preview`,
  `telescope-preview-motions`) are the REGRESSION this re-plan fixes (BP-A5 rev 2) — at the
  next gate they must be GREEN, NOT allowlisted.
- The modal guarantee was PARTIAL at the failed gate (the successful pane switches never
  executed live) — it is RE-VERIFIED after BP-A5 rev 2, not carried as satisfied.
- The unit suites (258/191) passed at the failed gate and are UNTOUCHED by the re-compose
  (the synthetic rects already pin the bottom-Input topology) — they must stay 258/191, 0
  failed; a count change means an out-of-plan edit.
- The harness M-M7 migration (BP-B1) is CORRECT as landed — NO harness edit is part of this
  re-plan; a harness diff at the next gate is a deviation.

## Execution Log

### Attempt 1 — RED + BUILD + VERIFY FAIL (iteration 1) (2026-10-04/05)

- **RED (e2e-test-builder): RED-CONFIRMED** — 40 gross / +34 net tests staged (258 `Run_`
  methods = the exact expected GREEN count; the 6 M34 tests replaced by 23 evolved machine
  tests; +7 `Run_PaneNavEngine_*`, +6 `Run_PaneHost_*`, +3 `Run_ListKeyMap_*`,
  +`Run_PaneSelectionSync_Steps`); `[STAThread]` on Main (BP-A1); the e2e scenario
  `telescope-focus-panes` created (43 registered; ONE VS boot: the three-state contract +
  directional keys absent — 0 `focus target=` lines, Ctrl+K falls through unhandled). Unit
  RED: 155 planned missing-symbol errors (staged to surface the full mix) + the `IPane` CS0246.
- **BUILD (build-agent, 2 dispatches — the first hit its step cap mid-docs):** Section A
  COMPLETE (BP-A2..A7: the evolved `FocusTargetModel`, the pure `PaneNavigationEngine`, the 7
  pane files, the composition E1-E14, the routing R0-R5, `FocusTargetUi` retired; build
  0 errors; Telescope **258/0**) + BP-B1 (the M-M7 migration of the 4 harness sites, re-located
  by content) + BP-B2 (the 14-consumer audit, all NO-EDIT) + BP-B4..B7 + (dispatch 2)
  BP-B8..B10 (the spec §5/AGENTS/SKILL counts + lists; both lints PASS). Deviations
  adjudicated (hub): `PreviewPane.cs using System.Windows.Controls -> ACCEPT (the artifact's
  snippet omitted it; minimal artifact-bug fix)`, `spec §2.5 anchor drift -> ACCEPT
  (content-equivalent; the columns text preserved)`.
- **VERIFY (verification-agent) — FAIL (iteration 1):** 40/43 (run 176 fresh boot + the run-177
  isolated retry). Units GREEN (258/0, 191/0); the harness-health all PASS. **3 real REDs
  (fail-twice), ONE shared root cause:** BP-A5's composition kept the prompt docked TOP (the
  ARTIFACT's own E2 contradiction of plan D1) instead of the pinned bottom-Input geometry →
  every vertical Ctrl-chord geometrically no-ops live (`focus no-op: no pane up from Input`);
  the units passed on synthetic rects pinning the correct topology (the composition deviation
  invisible to the pure machine). `telescope-goto` passed first-try (its flaky count stays at
  base 1).
- **Cost:** delegations: 5 | VS boots: 3 | iterations: 1

### Attempt 2 — RE-PLAN + DEBUG + VERIFY PASS (2026-10-05)

- **RE-PLAN (implementation-planner):** BP-A5 rev 2 pinned — the ONE-LINE dock flip
  (`DockPanel.SetDock(_promptPane.Content, Dock.Bottom)`), the separator to the prompt's TOP
  edge, the comment fixes; the ripple checks all verified (the centering math, the
  SizeChanged→RefreshPaneLayout self-correction, the preview width, the results-columns
  scenario, no opening animation, the initial focus + the modal guard untouched);
  `DEVIATIONS RESOLVED: artifact E2's Dock.Top -> SUPERSEDED by BP-A5 rev 2 (the build-agent
  must apply rev 2, not re-derive E2)`. Trace-level re-plan (the approach = the already-approved
  D1 pin) — NO 4a gate needed.
- **DEBUG (debug-agent): FIXED** — BP-A5 rev 2 applied exactly (TelescopeOverlay.cs:204/:188/
  :180-183/:207-209); build 0 errors; Telescope 258/0; the affected subset (run 178, ONE VS
  boot): `telescope-focus-panes` + `telescope-preview` + `telescope-preview-motions` all GREEN
  — the directional sequences LIVE (Ctrl+K from open → `focus target=Preview`, the pinned
  tie-break), the two pinned no-op edges the only no-ops, the modal guarantee re-verified.
- **VERIFY (verification-agent) — the FINAL GATE: PASS.** Harness-health first: `-SelfCheck`
  PASS, `-List` 43, both lints PASS. Units staggered: Telescope **258/0**, NeoVisual **191/0**.
  Full e2e FRESH boot (`-TimeoutSec 2400`, run 179): **43/43 GREEN**, zero new flakes; the
  run-176 symptom appears NOWHERE; the M-M7 sites show the live three-state sequences;
  `seed-leak`/`seed-reset` PASS; the manual visual pass verified (the AC4 left-click chain +
  the unit pins; the initial pane = Input proven by the Ctrl+K-from-open move; the subtle
  chrome; the modal guarantee RE-VERIFIED live). Zero deviations.
- **Failure-log sweep:** 12 entries read, 0 fixed, 0 queued, 0 annotated (all entries already
  carry FIXED resolutions).
- **Cost:** delegations: 9 | VS boots: 6 | iterations: 1
