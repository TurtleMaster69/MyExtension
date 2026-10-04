# SESSION STATE — neovim-planning-hub-20261004-124602

Status: ACTIVE · Objective: plan the **Telescope results migration — text rows → columned
list** (per-finder columns + header column chooser) for neovim_hub, two-stage fan-out tactic.
Unit-only lane; e2e deferred.

## HANDOFF CONSTRAINT (user instruction 2026-10-04)

neovim_hub is implementing Gap 3 RIGHT NOW. **Do NOT change any file he is using.** The plan
lives in THIS session workspace; the handoff writes (`docs/implementation_plan.md`,
`docs/progress.md`, both e2e queues) are **DEFERRED until Gap 3 is GREEN** in
`docs/progress.md`. Research is read-only (source untouched).

## Scope (user decisions 2026-10-04)

1. Migrate the overlay results from a text block (read-only TextBox, `> `-marked rows) to a
   real multi-column list (WPF ListView+GridView, headers VISIBLE).
2. Implement ALL cataloged columns per finder; the user-marked subset is default-ON; the rest
   exist but default-OFF.
3. Right-click a column header → chooser menu toggles any column (VS Error List pattern).
4. Default-ON marks: Files=File name+Directory; Issues=Kind+File+Message; References=Access+
   File; Grep=File+Line+Line text; Fzf=File+Line+Line text; Implementation=Kind+File.
   (Issues/References/Implementation leave Line default-OFF; References leaves
   Symbol/Column/Line-text off; Implementation leaves Symbol/Line off; Files leaves Full-path off.)

## Key research facts

- Results host = read-only WPF `TextBox _resultsBox` (TelescopeOverlay.cs:162-178); pipeline =
  hits → `FinderEntry{Display, Payload}` → fzf filters Display strings → `ResultMapper.MapBack`
  → `RenderResults()` writes `ResultsFormatter.ToText` into `_resultsBox.Text` (:452);
  selection/preview/Enter read Payload by INDEX (display-independent).
- References rows ALREADY show `{Symbol} ({access}) {basename}:{line}:{col} — {text}`
  (ReferencesFinder.cs:51-52) — inline text, not columns.
- Hit attributes: FileHit=path only; CodeIssue=Kind+Text; ReferenceHit=Column+IsWrite+Symbol+
  LineText; GrepHit/FzfHit=LineText; ImplementationHit=SymbolName+Kind; all extend
  FileLocation{FilePath,LineNumber}.
- Blast radius: fzf input = Display strings; MapBack display-keyed (duplicate-safe);
  harness pins `results count=N selected=M` (~20 regexes) incl. `boxText=…Length`; unit tests
  pin exact display strings; overlay is WPF-only.
- Telescope.nvim reference: entry_maker → {value, ordinal, display} decoupled;
  entry_display fixed-width columns + one remaining=true; `…` truncation; VS Error List
  columns + Find All References' Read/Write Kind column.

## Status line

Stage 1 (general plan) DONE → `plans/plan.md`. Stage 2 (parallel section planners A–E) IN
FLIGHT. Stage 3 (aggregate) pending. Gate pending. **Handoff DEFERRED until Gap 3 GREEN.**

## Files

- Plan: `sessions/neovim-planning-hub-20261004-124602/plans/plan.md`
- Section outputs: `artifacts/section-{a,b,c,d,e}.md`
- Handoff targets (DEFERRED): `docs/implementation_plan.md`, `docs/progress.md`,
  workspace `e2e-queue.md` + canonical `docs/e2e-queue.md` (E2E-RC-1..2).
