# Plan — Feature 7: the overlay PANE architecture + 3-pane focus (Ctrl+H/J/K/L + left-click)

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

- **BP-A1** — `[STAThread]` on the Telescope.Tests Main (the WPF-construction enabler for the
  pane fakes).
- **BP-A2** — evolve `FocusTargetModel` (the Input token, `PaneFocusKey`, the spatial map, the
  K-cycle + the pinned wrap Input→Preview, the click `Focus`, `ExitsInsert`) + 20 machine tests.
- **BP-A3** — `IPane`+`PaneChrome` / `PaneHost` (the registry, the activation order, the
  left-click seam) + 6 contract tests.
- **BP-A4** — `PromptPane`/`ListPane`(+`ListKeyMap`)/`PreviewPane`/`PaneSelectionSync` + 4 tests.
- **BP-A5** — the overlay composition (14 numbered edits: the panes built, the clicks wired,
  every focus path through the machine, `FocusTargetUi` retired).
- **BP-A6** — the routing dispatch by the focused pane + the native-arrow delta-replay sync +
  the Tab swallow.
- **BP-A7** — the gate: build + the full suite.
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
  e2e, the doc-ref lint).

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| the 20 evolved/new machine tests | BP-A2 | RED → GREEN: the spatial map, the K-cycle + the wrap, the click `Focus`, `ExitsInsert` |
| the 6 pane-contract tests | BP-A3 | RED → GREEN: the registry, the activation order, the left-click seam |
| the 4 pane tests | BP-A4 | RED → GREEN: the three panes + `PaneSelectionSync` |
| the M-M7 harness migration | BP-B1 | the 4 sites → three-state patterns |
| e2e `telescope-focus-panes` (RED then GREEN) | BP-B3, BP-A5/A6 | RED before the source lands; GREEN: the directional sequences (Ctrl+J→Input, Ctrl+H→List from the Preview, Ctrl+L→Preview from the List, Ctrl+K→the pinned tie-break target from the Input) + the logged no-ops |
| e2e telescope-preview/-motions (M-M7) | BP-B1 | the updated three-state assertions GREEN |
| e2e the modal guarantee | BP-A5/A6 | the overlay never deactivates on pane switches |
| unit gate | BP-A7, BP-B12 | Telescope 239 (`<ACTUAL>`); the build 0 errors |
| lints + `-List` | BP-B12 | 0 unresolved; PASS; 42 scenarios |

**Known-RED allowlist: NONE plan-new.** CARRIED (the verifier must not flag): the
`neovisual-window-management` flake (**×2 cumulative — ONE more flake = the 3rd-strike
upgrade**, the W6 rule); the stale harness comment; the in-flight plans' count drift (re-reads).
Expected RED = the new/evolved unit tests + the new e2e scenario before the source lands. The
left-click path is unit-pinned + manual (not injectable).
