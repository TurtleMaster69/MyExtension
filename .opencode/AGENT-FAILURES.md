# Agent command failures (append-only log)

Purpose: when an agent runs a command that fails, record it here so we can tell
whether the failure is an **agent-side mistake** (wrong syntax / wrong API shape /
hallucinated flag — fix the prompt/skill) or a **tool-side bug** (the tool is
genuinely broken — fix or work around the tool). Tools are only useful if used
correctly; a recurring agent-side error is a prompt/skill defect, not a tool defect.

**Rules for agents (all hubs and subagents):**
- On a command that fails (non-zero exit, exception, unexpected empty result), append
  ONE entry in the format below — do not fix it silently and do not repeat the same
  broken command.
- Keep it terse: the exact command, the exact error line, and your one-line guess at
  the category.
- **Do NOT** log expected/negative results that are part of a test (e.g. a RED test
  that is supposed to fail, a `-Tests` scenario that is intentionally red). Log only
  UNINTENDED failures.
- Read the existing entries first — if your exact failure is already listed with a
  fix, apply the fix instead of appending a duplicate.

**Rules for the hub (the sweep — `neovim_hub.md` LOOP step 11):**
- At every item's **final gate** (after the last VERIFY, before the GREEN commit), read
  this file and process EVERY entry whose FIX is empty or `unknown`.
- `agent-syntax` → fix the prompt/skill/agent file so the mistake cannot recur, then
  annotate the entry with the fix + date (`FIXED <date>: <file/change>`).
- `tool-bug` → file a `docs/progress.md` queue item and annotate the entry.
- `environment` → annotate with the workaround; escalate only if it recurs.
- This log is **append-only**: never delete an entry. Annotate it in place.
- Record the sweep in the item's Execution Log:
  `failure-log sweep: N entries read, M fixed, K queued, J annotated`.
- A sweep that finds nothing new is a valid result — record it and move on.

## Entry format

```
## YYYY-MM-DD | <agent> | category
COMMAND: <the exact command run>
ERROR: <the exact error line / exception>
FIX: <what actually worked, or "unknown — needs triage">
```

`category` is one of:
- `agent-syntax` — wrong call syntax / wrong API shape / hallucinated flag → fix the
  prompt, skill, or agent file so no future agent repeats it.
- `tool-bug` — the tool is genuinely broken at this version → track it and work around.
- `environment` — transient (lock, missing dependency, VS not booted) → note the workaround.
- `unknown` — not yet triaged.

## Log

## 2026-09-27 | arch-auditor (+ other subagents) | agent-syntax
COMMAND: `engine.to_json()['nodes']` and `json.loads(engine.to_json())['nodes'][0]`
ERROR: `TypeError: string indices must be integers, not 'str'` (indexing the JSON
string returned by `to_json()`), and after parsing, `KeyError: 0` because `nodes` is
an id-keyed dict, not a list.
FIX: `import json; j = json.loads(engine.to_json())` — `to_json()` returns a **str**;
`j['nodes']` is an **id-keyed dict** (`{node_id: node_dict}`), `j['edges']` is a **list**.
For caller questions, do NOT enumerate nodes — address the proxy id directly:
`engine.callers_of('proxy.unresolved:controller.TryMove')` -> `['HandleKey']`.
Documented in `.opencode/skills/trailmark/SKILL.md` ("Graph export shapes"),
`references/query-patterns.md` ("Graph export shapes"),
`references/preanalysis-passes.md`, `.opencode/skills/trailmark-structural/SKILL.md`,
and `.opencode/agent/{arch-auditor,trailmark-recon}.md`. Verified 2026-09-27.
FIXED 2026-09-27: documentation fix landed in the six files listed above (the sweep's
first pass) — this entry stays as the record of the recurring agent-syntax defect.

## 2026-09-27 | verification-agent | agent-syntax
COMMAND: `pwsh tools/test-e2e.ps1 -Tests seed-leak` — an ISOLATED retry of the
end-of-run leak guard, applied as the flaky-retry policy.
RESULT: the isolated run PASSED and the agent classified the full-run failure as
`flaky`. That classification is WRONG: `seed-leak` asserts over the WHOLE run's
side effects, and an isolated re-run re-bootstraps/reseeds the scratch first, so it
cannot reproduce a leak caused by earlier scenarios. A pass-on-retry is evidence of
flakiness only for state-INDEPENDENT scenarios. The real failure was genuine: the
`Get-SeedFiles` guard counted `Probe/obj/**/*.cs` build artifacts as "added" seeds
and `neovisual-editor-insert`'s intentional `Beta.cs` save as "modified"
(user-confirmed real). (Also discovered: the W22 `$AllowLeak` mechanism itself was
broken — deleting allowlisted entries from the snapshot re-reported them as "added".)
FIX: `.opencode/agent/verification-agent.md` step 6 now forbids isolated retries of
end-of-run aggregate/guard scenarios — such a failure is a real harness finding
regardless of an isolated retry. The guard defect itself was fixed in
`tools/test-e2e.ps1` (exclude `obj/`+`bin/`; `Assert-NoSeedLeak` takes the allowlist
and skips allowlisted files instead of mutating the snapshot). FIXED 2026-09-27.

## 2026-09-27 | hub (meta) | agent-syntax
COMMAND: repeated `pwsh tools/test-e2e.ps1` runs over several sessions; two scenarios
carried on the `known-RED allowlist` as "pre-existing flake" (`neovascular-editor-insert`,
`telescope-implementation`).
RESULT: both were REAL, deterministically-fixable defects the allowlist masked:
  1. `neovascular-editor-insert` - 2/2 RED, not intermittent. The scenario pressed `i` at
     document position 0 (code context), so C# IntelliSense popped and the injected Space
     committed `HandleInheritability`, corrupting the marker. A TEST ARTIFACT (fix: move the
     caret into the `// Beta.cs` comment first). Commit fd18315.
  2. `telescope-implementation` - 3/3 RED standalone, not intermittent. `Assert-OverlayFocused`
     was PID-only, so Enter was injected before the overlay became the OS foreground window.
     A HARNESS RACE (fix: require the overlay window, PID + title `Telescope`). Commit 7c6569b.
FIX: treat a scenario that fails CONSISTENTLY across runs as a defect, not a flake. The
allowlist is for documented, understood, OUT-OF-SCOPE pre-existing failures - never for an
unexamined "flake" (an unexamined flake is just a bug with a nicer name). Both allowlist
entries are now closed; the full 35-scenario suite is GREEN. FIXED 2026-09-27.
