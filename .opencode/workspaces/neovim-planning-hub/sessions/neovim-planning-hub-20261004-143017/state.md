# SESSION STATE — neovim-planning-hub-20261004-143017

Status: ACTIVE · Objective: plan the NEXT queue items while neovim_hub executes the columns
plan. Five plans produced, ALL gate-APPROVED. Handoff DEFERRED until the columns plan is GREEN
(the no-clobber constraint — neovim_hub is using docs/progress.md + docs/implementation_plan.md
+ the source tree).

## HANDOFF CONSTRAINT (user instruction)

Do NOT change any file that could interfere with neovim_hub's work. All research read-only;
the plans live in THIS session workspace; the handoff writes are deferred.

## Plans produced (all gate-APPROVED; handoff order after the columns plan GREENs)

1. **plan-columns-ux.md** — the columns UX bugfix (JUMPS THE QUEUE): no h-scroll, all columns
   visible (per-column min/max + the priority distribution + the exact-total invariant),
   logical shortening (Tail for path-like — the front removed, the end folder + file name
   survive; End for text), the overlay width scales with the visible column count (capped by
   the work area), the selection contrast. Gate: REVISE (the test pins) → APPROVE.
2. **plan-preview-buffer.md** — the preview's buffer source → the workspace buffer (FULL Roslyn
   highlighting for solution files — the Peek model; the standalone fallback otherwise;
   CodeLens SKIPPED — documented; the document-window embedding REJECTED — documented).
   Research verdict: a standalone csharp buffer is syntactic-ONLY (the semantic tagger bails
   without a workspace Document). Gate: APPROVE.
3. **plan-gap11.md** — the git bindings: `g,d` diff / `g,b` blame (branches DROPPED — the
   user's decision) / `g,h` history; `g,g`/`g,c` unchanged until the deferred lazygit overlay
   ships. Gate: APPROVE.
4. **plan-feature7.md** — the overlay PANE architecture (the user's directive: REAL focus,
   left-click focusable, modular panes, reusable for the lazygit overlay) + the 3-pane focus
   (Ctrl+H=List, Ctrl+L=Preview, Ctrl+J=Input, Ctrl+K=cycle-up). M-M7. Gate: APPROVE.
5. **plan-gap4.md** — the recent-files finder (`Name="Recent"`, `f,e` PROPOSED — flagged);
   CRITICAL finding: `EnvDTE.RecentFiles` does NOT exist in the 17.x interop → the reflection
   probe + the session-MRU fallback. Gate: REVISE (the false-GREEN scenario) → APPROVE.

## Deferred (user decisions)

- **The lazygit overlay** (`g,g`) — "one of the last things we do… bonus feature when the core
  of the extension is finished". The pane architecture (Feature 7) delivers its host.
- **Gap 5 (symbols finder)** — next batch (reuses the Gap 4 finder pattern).

## Handoff order (on the columns plan GREEN)

columns-ux → preview-buffer → gap 11 → feature 7 → gap 4 (then gap 5, the goto plan is
already second in the main queue).
