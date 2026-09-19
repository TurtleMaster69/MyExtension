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
