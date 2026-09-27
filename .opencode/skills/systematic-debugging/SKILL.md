---
name: systematic-debugging
description: Use when a bug is hard to reproduce, the cause is unclear, or a fix keeps failing. Structured bug investigation — reproduce, isolate, root-cause, fix, regression test. Adapted from obra/superpowers.
license: MIT
compatibility: opencode
---
# Systematic debugging

Structured bug investigation so a fix sticks and never regresses. Adapted from `obra/superpowers`.

## When to use
- A bug is intermittent, hard to reproduce, or reappears after a fix.
- The failure is silent (no diagnostic) — add the diagnostic first.
- More than one fix attempt has failed.

## Core method
1. **Reproduce** deterministically — if you can't reproduce, instrument (log a `[NeoVisual]`/`[Telescope]` line) until you can. In this repo, silent failures (fzf missing, VsVim reflection, swallowed view-read) are the ones that hide — log them.
2. **Isolate** — narrow to the smallest failing path; bisect the input or the call chain (e.g. keyboard hook → `InputHandler` → controller → injection).
3. **Root-cause** — state the mechanism in one sentence (e.g. "injected Return re-enters the hook and re-triggers OpenSelected") before changing code.
4. **Fix** the cause, not the symptom; add a regression test that fails on the old code and passes on the new.
5. **Verify** the regression test AND the surrounding behavior stay green.

## Source
Full patterns in `obra/superpowers` (systematic-debugging). Load only if the method above is insufficient.
