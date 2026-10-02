# sessions/

Per-session workspace directory. ONE SESSION = ONE WORKSPACE:

```
.opencode/workspaces/neovim-planning-hub/sessions/<session-id>/
  session.md      manifest (id, started, objective, status)
  state.md        HUB STATE (status line + decisions)
  tasks.md        Task Ledger
  log.md          Progress Ledger (append-only)
  artifacts/      per-task worker findings
  plans/          per-objective plan drafts (Step 4)
```

Session ID: `neovim-planning-hub-<YYYYMMDD>-<HHMMSS>`. New objective = new
session dir; old sessions are archived (never deleted), resumable by name.
