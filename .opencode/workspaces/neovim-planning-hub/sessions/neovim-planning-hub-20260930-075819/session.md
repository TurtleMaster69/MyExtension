# Session Manifest — neovim-planning-hub-20260930-075819

- **Date:** 2026-09-30
- **Objective:** Produce ONE combined unit-only plan for (A) the 67 code-review
  findings in `docs/code-review.md` (2026-09-30) and (B) a full repository
  restructure (folders + namespaces + tools + docs), fixes first then restructure.
- **User decisions (via question, 2026-09-30):**
  1. Namespace strategy: **Folders + namespaces** — move files AND rename
     namespaces to match the new folders.
  2. Plan structure: **One combined plan** — code-review fixes as early phases,
     restructure as the final phase.
  3. Scope: **Source + tools + docs** — group C# source in MyExtension/ and
     Telescope/, plus tools/*.ps1 and docs/*.md.
- **Lane:** feature/bugfix program + refactor (unit-only, e2e deferred).
- **Status:** ACTIVE
