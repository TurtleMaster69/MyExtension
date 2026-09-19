---
name: review-duplication
description: Use when auditing a codebase for duplicated logic, reinvented wheels, or missed reuse — the arch-auditor's core duplication check. Structured duplication-investigation method. Adapted from google-gemini review-duplication.
license: MIT
compatibility: opencode
---
# Review duplication

Structured workflow to find duplicated functionality and missed reuse during a review. Adapted from `google-gemini` `review-duplication`.

## When to use
- Auditing a slice for duplication (arch-auditor).
- Before accepting new code that might re-implement an existing helper.

## Core method
1. **Extract core logic** — identify the algorithms/utilities/data structures the new code introduces (beyond the business logic).
2. **Search the codebase for existing** — ask "where is this centralized?" for each (e.g. "where is caret styling done?", "which class owns key→arrow mapping?"). This repo has real duplication: vim-motion dispatch 4×, DTE walker in FileFinder vs ProjectFiles, GetWindowRect P/Invoke twice, KeyToArrowVk twice, GetAsyncKeyState twice, 3 caret renderers.
3. **Check reuse / architectural fit** — is there a canonical helper that should be used instead?
4. **Refactoring guidance** — explain how the new code could use what exists; prefer deleting a near-duplicate over polishing it.

## Source
Full vectors in `google-gemini/gemini-cli` `review-duplication`. Load only if the method above is insufficient.
