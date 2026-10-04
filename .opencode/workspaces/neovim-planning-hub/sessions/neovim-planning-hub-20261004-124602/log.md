# Log — session neovim-planning-hub-20261004-124602

## 2026-10-04 — Request + research

- REQUEST (user): neovim_hub is implementing Gap 3 — do NOT change any file he is using; IN
  ADVANCE, plan the Telescope results migration: search hits from txt to an actual list
  (multi-column rows); check whether references already display file name + kind (read/write)
  beside it — if not, add it; for other finders catalog the displayable attributes so the user
  can decide.
- Handoff constraint recorded: plan in the session workspace; docs writes DEFERRED until Gap 3
  GREEN. (LSP stale-index false negatives observed again — CloseWindowCommand/DiagnosticNavigator/
  ErrorListGatherer/DiagnosticEntry "not found" while Gap 3 executes; the growing line numbers
  confirm neovim_hub is mid-Gap-3. Ignored — not my files.)
- Research dispatched (parallel): feature-researcher (Telescope.nvim entry_display column
  anatomy + VS Error List / Find-All-References Kind column) + trailmark-recon (the results
  pipeline + the full hit-attribute catalog). Key finding: references rows ALREADY display
  file name + access — but as inline text, not columns; the migration makes them real columns.
- USER DECISIONS (question tool): implement ALL catalog columns; the marked subset default-ON
  (Files: name+directory; Issues: kind+file+message; References: access+file; Grep/Fzf:
  file+line+text; Implementation: kind+file); right-click a column header toggles any column;
  headers VISIBLE.
- Stage 1 (general plan) WRITTEN by the hub at `plans/plan.md` (same Stage-1-author deviation
  as Gap 1/Gap 3, noted for the user; the fan-out robustness lives in Stage 2).
- NEXT: dispatch Stage 2 (Sections A–E, parallel implementation-planners).

## 2026-10-04 — Scope additions, e2e mandate change, gates, HANDOFF COMPLETE

- USER ADDITIONS (in order): (1) the preview pane → a REAL editor view with edit mode disabled
  (research: FEASIBLE high-confidence — `IWpfTextView` embeds anywhere; excluding the
  `Editable` role means VsVim never attaches; classifier highlighting free; the recon REFUTED
  the user's color-leak report — the SyntaxHighlighter is purely local to the preview's
  RichTextBox); (2) abbreviated column values (write→W, read→R, error→err, implementation→imp,
  function→func, interface→inf…); (3) the gd/gI/gr remap — wiring decided by the user:
  **VS commands + their own VsVim mapping** (they have mapped VS commands in VsVim before);
  (4) **e2e ENABLED** — the deferral is lifted (the machine boots VS); plans carry e2e inline.
- Stage 2 round 1 (columns) + a REV-1 round (abbreviations + the preview section P + the
  C/D/E revisions) + the goto plan's Stage 1 + its 3-section fan-out — all returned.
- GATES: Plan A — REVISE (3 cross-section contract failures) → REVISE (the c-rev1 remnants) →
  **APPROVE** (round 3). Plan B — REVISE (the GotoDispatcher placement + the DefinitionHit
  shape) → **APPROVE** (round 2). The revisions were routed back to implementation-planner per
  the gate policy; two continuation dispatches finished the step-budget-exhausted revisions.
- HANDOFF (user-approved): Gap 3 verified GREEN (the precondition — the build loop had landed
  the code/docs and run the full 39-scenario e2e suite GREEN, discharging the GAP1+GAP3 gates).
  Plan A → `docs/implementation_plan.md` (hash match). Plan B → the SECOND pending item
  (session path; written to implementation_plan.md when it becomes first). progress.md: the
  "Next up" bullet + the FIRST/SECOND ITEM blockquotes (e2e ENABLED framing). The e2e queues:
  the GAP1/GAP3 gates were already marked RUN-GREEN by the build loop (its vocabulary kept);
  E2E-RC-1..2 + E2E-GOTO-1..2 appended to both queues. doc-refs PASS 0 unresolved (the
  not-yet-existing goto types de-backticked — they re-backtick at GREEN); doc-content PASS
  12/12.
- NEXT STEP for the user: tell neovim_hub to execute the FIRST pending item (Telescope
  columns + preview-as-editor, e2e enabled), then the SECOND (goto commands).
