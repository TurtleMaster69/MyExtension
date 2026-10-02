# Code Review Hub — Task Ledger (tasks.md)

> Plan + assigned subtasks + known facts. The hub is the ONLY writer. Runtime
> sessions keep their own ledger under
> `.opencode/workspaces/code-review-hub/sessions/<session-id>/tasks.md`.

| id | objective | assignee | status | artifact | verification |
|----|-----------|----------|--------|----------|--------------|
| T001 | Build the code-review-hub agent + 3 subagents + workspace | hub-creator | done | `.opencode/agent/code-review-hub.md` + `.opencode/agent/code-review-worker.md` + `.opencode/agent/test-quality-reviewer.md` + `.opencode/agent/docs-accuracy-reviewer.md` | skill-verifier ALL-PASS + hub-reviewer APPROVE |
| T002 | Curate the skill set for the hub + subagents | hub-creator | done | `.opencode/workspaces/code-review-hub/skills.md` | skill-verifier ALL-PASS |
| T003 | Install approved skills (test-gap-analysis, grade-tests, find-untested-sources) + drop testability-obstacle | hub-creator | done | `.opencode/skills/test-gap-analysis/` + `.opencode/skills/grade-tests/` + `.opencode/skills/find-untested-sources/` | skill-verifier ALL-PASS |
