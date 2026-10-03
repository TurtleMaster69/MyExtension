---
description: Shared authoritative copy of the hub prompt rule. NOT an agent — disabled so it does not appear in the agent list; read this file directly.
disable: true
---

# Prompt rule (MANDATORY) — shared by all hubs

This is the single authoritative copy of the prompt rule. Both `neovim_hub.md` and
`neovim_review_hub.md` reference this file; do NOT duplicate the rule inline in a
hub (a divergence would silently change both). If the rule changes, change it here
and update the one-line reference in each hub.

- **Only hubs prompt.** You are a hub, so you may prompt — but ONLY through the
  `question` tool, with concrete selectable options AND a custom/own-answer option.
- **NEVER ask the user a question in plain text** (no trailing "which findings
  should I file?", "shall I proceed?", etc.).
- Your subagents have `question: deny` and must never prompt the user. Decide for
  them.
- **LSP is primary for symbol-level navigation; Trailmark is mandatory for graph-level
  structural questions.** Use the `lsp` tool (`goToDefinition`, `findReferences`,
  `hover`, `documentSymbol`, `workspaceSymbol`, `goToImplementation`, and **direct**
  `incomingCalls`/`outgoingCalls`) before `grep`/`read`/Trailmark. Use the vendored
  Trailmark skills (`.opencode/skills/trailmark`) for transitive call paths,
  reachability, blast radius, and complexity — instead of `grep`/glob/manual reading.
  `grep` is only for literal text and non-source files. Never silently fall back to
  manual code reading when LSP or Trailmark can answer (see AGENTS.md and the
  `trailmark` skill's "Rationalizations to Reject" table).
  **Scope to this repo:** this VSIX has no detected entrypoints, so the security
  passes — taint, privilege boundaries, attack surface, `trailmark-finding-triage`,
  and `trailmark-review-gate` — carry no signal here; do not load or run them.
  **The full per-repo how-to (boot, `language="c_sharp"`, proxy traps, `to_json()`
  shape) is single-sourced at `.opencode/agent/trailmark-guidance.md` — read it
  there, do not re-derive it here.**
