# Agent command failures (append-only log)

Purpose: when an agent runs a command that fails, record it here so we can tell
whether the failure is an **agent-side mistake** (wrong syntax / wrong API shape /
hallucinated flag — fix the prompt/skill) or a **tool-side bug** (the tool is
genuinely broken — fix or work around the tool). Tools are only useful if used
correctly; a recurring agent-side error is a prompt/skill defect, not a tool defect.

**Rules for agents (all hubs and subagents):**
- On a command that fails (non-zero exit, exception, unexpected empty result), report
  it in your FINAL MESSAGE (the exact command, the exact error line, and your one-line
  guess at the category) — do not fix it silently and do not repeat the same broken
  command. The hub appends it to this log (read-only agents cannot edit files; the hub
  is the single writer of this shared log).
- Keep it terse: the exact command, the exact error line, and your one-line guess at
  the category.
- **Do NOT** log expected/negative results that are part of a test (e.g. a RED test
  that is supposed to fail, a `-Tests` scenario that is intentionally red). Log only
  UNINTENDED failures.
- Read the existing entries first — if your exact failure is already listed with a
  fix, apply the fix instead of reporting a duplicate.

**Rules for the hub (the sweep — `neovim_hub.md` LOOP step 11):**
- When a subagent reports a command failure in its final message, append ONE entry to
  this log (in the format below) at the next natural checkpoint (step 9 Execution Log
  or the final-gate sweep).
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

## 2026-09-29 | code-review-worker (S9) | agent-syntax
COMMAND: `uv run --with trailmark python -` with `trailmark.parse.parse_directory(..., language="c_sharp")` + `trailmark.query.QueryEngine`
ERROR: `AttributeError: 'CodeGraph' object has no attribute 'find_node_id'` (and `preanalysis()` -> `'CodeGraph' object has no attribute '_graph'`); tried 3 variants (default, `--with trailmark==0.5.0`, no preanalysis) — all failed identically.
FIX: use the documented `QueryEngine.from_directory(dir, language="c_sharp")` entry point (as `trailmark-recon` does) instead of `trailmark.parse.parse_directory` + `trailmark.query.QueryEngine`; the wheel's `QueryEngine` expects a store shape the `parse_directory`-returned `CodeGraph` lacks. The worker fell back to the shared RECON digest + direct reading — no finding was lost. FIXED 2026-09-29 (guidance: the canonical entry point is `QueryEngine.from_directory`).

## 2026-09-29 | arch-auditor (R8-S3) | environment
COMMAND: a Python Trailmark query script run via `uv run --with trailmark python -` (QueryEngine.from_directory + preanalysis)
ERROR: `UnicodeEncodeError: 'charmap' codec can't encode character '\u2192'` (cp1250 console encoding) — the query script printed a `→` character to a cp1250 console; the query still returned the needed caller data before failing on the last method.
FIX: set `PYTHONIOENCODING=utf-8` (or avoid non-ASCII in query-script output) when running Trailmark query scripts on this machine's cp1250 console. No repo impact; the worker completed its findings. FIXED 2026-09-29 (workaround noted).

## 2026-09-29 | arch-auditor (R10-S2) | environment
COMMAND: repeated `uv run --with trailmark python -` Trailmark query invocations (QueryEngine.from_directory + preanalysis)
ERROR: the first identical query pattern returned data, but subsequent runs produced EMPTY stdout even with `2>&1` — no exception, no output. Category guess: `uv run` stdin-pipe flakiness on repeat invocations.
FIX: run each Trailmark query in a fresh `uv run` process (or batch all queries into one script invocation) rather than re-invoking `uv run` repeatedly in the same shell; the worker had enough evidence from the first successful query + direct reads and completed its findings. No repo impact. FIXED 2026-09-29 (workaround noted).

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

## 2026-10-02 | verification-agent | tool-bug
COMMAND: `pwsh tools/harness/test-e2e.ps1` (full-suite bootstrap)
ERROR: `tools/harness/dte-command.ps1:42` -> "You cannot call a method on a null-valued expression." The runspace scriptblock's `param($dte,$command,$arg)` were unbound because `Invoke-DteWithTimeout` used `$ps.AddParameter($p)` (which binds each value as a parameter NAME) instead of `$ps.AddArgument($p)`.
FIX: `tools/harness/dte-command.ps1:33` changed `AddParameter` -> `AddArgument` (comment at :29 updated). Introduced in `f4450cb` (2026-09-30) and never e2e-exercised since e2e was deferred; the `-SelfCheck` seam missed it because its stubs pass no parameters. FIXED 2026-10-02 (this item).

## 2026-10-02 | verification-agent | agent-syntax
COMMAND: `rg ... | head -50`
ERROR: `head: The term 'head' is not recognized as a name of a cmdlet, function, script file, or executable program.`
FIX: use `Select-Object -First N` (PowerShell) instead of the Unix `head`. Added to the command-log known-bad index.

## 2026-10-02 | verification-agent + debug-agent | agent-syntax
COMMAND: `pwsh tools/harness/test-e2e.ps1 -Tests a,b,c`
ERROR: the comma list arrived as one string -> "Unknown scenario(s)" (the `[string[]]` array did not bind through the native `pwsh` boundary).
FIX: use the call operator `& tools/harness/test-e2e.ps1 -Tests a,b,c` (or `pwsh -Command "& ... -Tests a,b,c"`). Added to the command-log known-bad index.

## 2026-10-03 | verification-agent | agent-syntax
COMMAND: `pwsh -NoProfile -Command "$errs=$null; ... [ref]$errs ... if ($errs) {...}"` (a double-quoted `-Command` argument containing an inner pwsh script).
ERROR: `ParserError: Missing condition in if statement after 'if ('` — the OUTER pwsh interpolated `$errs`/`$toks`/`$_` before the inner pwsh parsed them, so the inner `if ($errs)` arrived empty.
FIX: pass the inner script as a SINGLE-quoted `-Command` argument (or a script file) so the outer shell does not interpolate the inner script's variables. Recovered with a single-quoted `-Command`. FIXED 2026-10-03 (workaround noted).

## 2026-10-05 | feature-researcher | agent-syntax
COMMAND: (1) `webfetch` of the fzf man page `man/man1/fzf.1.md` (github blob + raw) and (2) `webfetch` of `VsVim/Src/VimCore/ModeKind.fs`; (3) `grep` tool on the tool-output dir; (4) `Add-Content` to `.opencode/command/command-log.md`.
ERROR: (1)+(2) `404` — guessed remote paths (the fzf CHANGELOG/README are the right sources; the VsVim ModeKind enum lives inside another file — the repo's reflection-verified comment suffices); (3) "No files found" — the known directory-scoped silent-skip class; (4) `permission denied` — this agent's bash policy is `{"*": deny, "rg *": allow}`, so it could not append the failure-log entry itself (`edit` is also denied for it).
FIX: (1)+(2) use the CHANGELOG/README + the repo's own reflection-verified comments (recovered — all four research questions answered); (3) known class, no action; (4) the hub (single writer) appends the entry — this entry. FIXED 2026-10-05 (hub-wired).

## 2026-10-07 | verification-agent (CR107 final gate) | agent-syntax
COMMAND: `git diff -U3 tests/Telescope.Tests/Program.cs | rg -n '...|imp\"'` (DOUBLE-quoted pwsh arg containing `\"`)
ERROR: `imp\: The term 'imp\' is not recognized...` — the `\"` terminated the double-quoted string early; the trailing `imp\` became a bareword command. RECURRENCE of the indexed 2026-10-04 verification-agent Gap 11 class (double-quoted pwsh arg with `\"` → silent/truncated rg pattern).
FIX: single-quote the whole rg pattern (`rg -n '...'`) — the command-log known-bad row already documents this class (2026-10-04 Gap 11 entry); the diff analysis then succeeded. FIXED 2026-10-07 (recurrence annotated; the known-bad row is the durable fix).
