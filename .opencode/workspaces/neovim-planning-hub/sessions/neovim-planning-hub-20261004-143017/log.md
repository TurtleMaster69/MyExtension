# Log — session neovim-planning-hub-20261004-143017

## 2026-10-04 — Request + research + five plans

- REQUEST (user): plan the next queue items while neovim_hub works; do not change any file
  that could interfere. Clarifying questions answered: Feature 7 = REAL focus + left-click +
  a MODULAR pane architecture (the user's directive — reusable for the lazygit overlay);
  Gap 11 = `g,d`/`g,b`/`g,h` only (branches DROPPED; the lazygit overlay DEFERRED — "one of
  the last things"); the columns UX defects reported (no h-scroll, all columns visible,
  min/max widths, logical shortening — paths truncate from the FRONT, the window width scales
  with the columns, the selection contrast); the preview must highlight for ALL languages
  ("everything the document editor can show").
- Research dispatched: the git commands, the focus conventions + the finder APIs, the focus
  model recon, the preview-highlighting verdict (the standalone buffer is syntactic-ONLY; the
  workspace buffer gives FULL Roslyn; the document-window embedding REJECTED; CodeLens
  SKIPPED).
- FIVE plans produced + gate-APPROVED (see state.md): columns-ux, preview-buffer, gap11,
  feature7 (the pane architecture), gap4. The gates caught and fixed: the Gap 4 scenario's
  false-GREEN (the per-key snapshots), the columns-ux test pins (the off-by-one), the goto
  plan's placement/shape contradictions (the previous session's plans — already resolved).
- CRITICAL findings recorded: `EnvDTE.RecentFiles` does NOT exist in the 17.x interop (the
  probe + the session-MRU design); the standalone csharp buffer is syntactic-only (the
  workspace-buffer swap is the fix).
- HANDOFF: DEFERRED until the columns plan is GREEN (the no-clobber constraint). The handoff
  order: columns-ux → preview-buffer → gap 11 → feature 7 → gap 4.
- NEXT: on the columns plan GREEN (or the user's word), present the handoff (5 plans + the
  queue changes) via question; write them progressively to docs/implementation_plan.md +
  progress.md + the e2e queues.

## 2026-10-04 — HANDOFF COMPLETE (five plans)

- USER APPROVED the handoff ("u can hand it off but look first what the neovim hub did").
- neovim_hub's state verified FIRST: the columns plan AND the goto plan both GREEN (the full
  41-scenario e2e suite + both unit suites; the 3rd-strike window-management regression fixed
  in-item); the loop's Done entry for the columns plan documents the preview's
  workspace-detached-buffer limitation and OWNERS the fix to the planning hub — that fix IS
  plan-preview-buffer.md; the loop's "Next up" was Gap 11 (its plan exists — reconciled into
  the handed-off order); docs/implementation_plan.md held the DONE goto plan (safe to
  overwrite).
- WROTE `docs/implementation_plan.md` ← the columns-ux plan (hash match).
- UPDATED `docs/progress.md`: the "Next up" bullet (the columns-ux first, then the ordered
  queue) + the FIRST ITEM blockquote (columns-ux) + the THEN-2..5 blockquotes (preview-buffer,
  gap 11, feature 7, gap 4 — each with its plan path + the key pinned decisions).
- APPENDED the e2e queues (both): E2E-GIT-1, E2E-PANES-1, E2E-RECENT-1, E2E-CUX-1 (no new
  scenario — the manual visual pass), E2E-PBUF-1 (no new scenario — the manual visual pass).
- VERIFIED: doc-refs PASS 0 unresolved (the not-yet-existing IPane/PaneHost de-backticked —
  they re-backtick at GREEN per the Feature 7 plan's doc steps); doc-content PASS 12/12.
- NEXT STEP for the user: tell neovim_hub to execute the FIRST pending item (the columns UX
  bugfix), then the ordered queue (preview-buffer → gap 11 → feature 7 → gap 4).

## 2026-10-04 — Feature 7 revised to the DIRECTIONAL focus map (user) + re-gated

- USER REVISION: Ctrl+H/J/K/L = focus LEFT/DOWN/UP/RIGHT (the Cardinal spatial mapping — the
  same keys/mental model as the window navigation, one level down), GEOMETRIC (check which
  pane lies in the direction of the focused pane's rect), NOT the fixed key→pane map.
- plan-feature7.md revised (the Goal/D1/D6/D7 + the AC table + the pinned contracts + the
  trace); the artifacts revised (section-a COMPLETE: the pure `PaneNavigationEngine` mirroring
  the `WindowNavigationEngine` pipeline, the tie-break PINNED = the Cardinal's last-in-list
  rule → Ctrl+K from Input = Preview, the no-op PINNED = logged
  `[Telescope] focus no-op: no pane {direction} from {pane}`; section-b: the scenario
  rewritten directional).
- GATE round 2: REVISE — CRITICAL caught: the existing preview scenarios fire Ctrl+L as the
  FIRST focus key after open; from the new initial Input pane that is a pinned no-op edge →
  both scenarios would RED at VERIFY. FIXED: BP-B1 rewritten as REAL edits — the focus preamble
  (Ctrl+K→Preview, Ctrl+H→List) before each Ctrl+L; all 4 assertions snapshot-attributed. Plus
  the totals (239), the typo (left/down/up/right), the allowlist carry.
- GATE round 3: **APPROVE** (2 minors applied: the net-delta note +34; the stale one-liners).
- The handoff state: the queue order stands (columns-ux → preview-buffer → gap 11 → feature 7
  → gap 4); progress.md's THEN-blockquote item 4's key description should be read as the
  DIRECTIONAL map (the plan file is authoritative).
