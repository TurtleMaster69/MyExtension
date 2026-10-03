# Command Knowledge Base — shared across ALL hubs and subagents

> **Purpose:** stop wasting time on commands that fail or are the wrong tool. Every agent in this repo
> (hubs and subagents) follows this protocol. This file is the single shared memory for "which commands
> work, which don't, and why" — so no agent re-discovers the same failure.
>
> **Edit rule:** append-only for the Failure log; the curated index (Known-good / Known-bad / Correct
> tool per task) is updated by any agent that finds a repeatable finding. Read-merge-write: re-read
> before writing; if another agent updated it since your last read, merge your entry into the current
> content — never overwrite. Keep the curated index under ~100 lines; the Failure log may grow and be
> pruned from the tail.

## Protocol (every agent) — MANDATORY

1. **MUST READ this file's curated index before running ANY shell command.** It is the command list +
   recommendations (Known-good / Known-bad / Correct tool per task). Use the correct tool for the task
   (e.g. LSP/trailmark for code navigation, not grep) and never retry a command already logged as
   known-bad with a working alternative. Skipping this read is a violation — it wastes time on
   known-failing commands.
2. **Try the command if you think it's the optimal tool** — if it's not in the index and seems like the
   right tool, run it once. One attempt is cheap; the failure data is valuable.
3. **If it fails** (permission denied, error, wrong output) — whether a **shell command** or a **tool
   call** (especially the `lsp` tool) — append an entry to the **Failure log** (bottom) with
   CMD/OPERATION, RESULT, REASON, ALTERNATIVE, NEEDS-PERMISSION, AGENT, DATE — then move on to the
   alternative. Never retry the same failing command/call repeatedly in one session. For an `lsp`
   failure, record the exact operation + params (e.g. `lsp incomingCalls file=... line=... char=...`)
   and classify REASON as `misuse` (bad params/position) vs `server` (no server / crash) so misuse can
   be fixed later.
4. **The user decides on permissions.** The Failure log is reviewed by the user, who grants permissions
   for commands that are the optimal tool and fixes or rejects the rest. Do not grant yourself
   permissions — log and move on.
5. **Use the right tool.** Check the **Correct tool per task** table before choosing how to do a task
   (e.g. LSP/trailmark for code navigation, not grep). If you KNOW a different tool is clearly better,
   use it directly instead of trying the wrong one.
6. **Failure reason** is exactly one of:
   - `permission` — the tool/command is denied for you (flag NEEDS-PERMISSION with the exact permission
     that would make it work, if it makes sense).
   - `misuse` — you used it wrong (bad args/flags/order). Record the correct usage.
   - `wrong-tool` — a different tool does this better. Record the alternative.
   - `server` — the tool's backing service failed (e.g. the `lsp` tool reports no server, or the
     language server crashed). Record the operation + params.
   - `other` — anything else (missing binary, environment, etc.).
7. **Prefer the opencode tools over bash** for file work: every bash invocation pays a pwsh spawn on
   this machine before the command runs, while the opencode `read`/`glob`/`grep`/`list` tools run
   in-process. `read` also supports `offset`/`limit` (reads only the slice of a large file). Use bash
   only when a tool cannot reach the target (e.g. `rg --no-ignore` for gitignored paths the `grep`
   tool skips), or for things the tools can't do (Measure-Command, piping, arbitrary logic).

## Known-good commands (verified working)

| command | what it does | notes |
|---|---|---|
| `dotnet build` | build the VSIX solution | allowed; this is a VSIX — a plain `dotnet run` does not work |
| `dotnet run --project tests/Telescope.Tests` | offline Telescope unit tests (172) | allowed; supports a substring filter as the first arg and `--list` |
| `dotnet run --project tests/NeoVisual.Tests` | offline NeoVisual unit tests (168) | allowed; supports a substring filter as the first arg and `--list` |
| `pwsh tools/harness/test-e2e.ps1` | live E2E suite (36 scenarios, boots VS Experimental) | allowed; slow — use `-Tests <subset>` during a loop, full suite only as the final gate |
| `pwsh tools/harness/test-e2e.ps1 -Tests <names>` | run a subset of e2e scenarios | allowed; `-NoBootstrap` reuses an already-booted instance (same code state only) |
| `pwsh tools/harness/test-e2e.ps1 -List` | list registered scenarios | allowed; cheap no-VS parse check |
| `pwsh tools/lint/check-doc-refs.ps1` | doc-reference lint (unresolved backticked refs) | allowed; ~2s, no VS |
| `lsp` tool (opencode) | symbol navigation (definition/references/hover/symbols/implementations/direct callers) | requires `"lsp": true` in config + `OPENCODE_EXPERIMENTAL_LSP_TOOL=true`; see `.opencode/LSP-SETUP.md` |
| `trailmark --version` / `uv run trailmark --version` | boot Trailmark (code graph) | allowed; if missing, install with `uv tool install trailmark` |
| `uv run --with trailmark python -` | run a Trailmark query snippet | allowed; always parse with `language="c_sharp"` (the CLI default `python` yields an empty graph here) |
| `git status` / `git diff` / `git log` / `git show` | inspect repo state | allowed (read-only) |
| `git add <paths>` / `git commit -m "<msg>"` | stage + commit the GREEN change set (neovim_hub's atomic-commit policy) | allowed; push/merge/pull remain denied |
| `rg --no-ignore -n <pattern> <path>` | search including gitignored paths | allowed; the `grep` tool cannot reach gitignored paths |
| `Get-ChildItem ...` / `ls ...` / `dir ...` | list files/folders | permission-dependent — agents with `bash: {"*": deny, "rg *": allow}` are denied it; use the `read` (directory) or `glob` tool instead |

## Known-bad commands (do not retry — use the alternative)

| command | why it fails | reason | alternative | needs-permission |
|---|---|---|---|---|
| `grep ...` (bash) | `grep` is NOT installed on this machine — only `rg` (ripgrep) exists | other | `rg` or the `grep` tool | no |
| `find ... -name ...` | Windows `find.exe` is a string-search tool, not GNU find — rejects `-name` | misuse | `glob` tool or `Get-ChildItem -Recurse -Filter` | no |
| `git push` / `git merge` / `git pull` | denied by policy | permission | never push/merge/pull; ask the user | no — deliberate policy |
| `rm ...` / `del ...` / `Remove-Item ...` / `rmdir ...` | denied by policy | permission | never delete; ask the user | no — deliberate policy |
| `curl ...` / `wget ...` / `Invoke-WebRequest ...` / `irm ...` / `iwr ...` | prompts (ask) and is usually the wrong tool | wrong-tool | `webfetch` tool, or delegate to `skill-researcher` | no |
| `dotnet run` (no `--project`) | this is a VSIX — a plain `dotnet run` does not work | misuse | `dotnet build`, or `dotnet run --project tests/<Project>` for the offline suites | no |
| `dotnet test ...` / `vstest.console ...` | this repo's tests are hermetic `dotnet run` projects, not vstest | wrong-tool | `dotnet run --project tests/Telescope.Tests` / `tests/NeoVisual.Tests` | no |
| `grep -rn <symbol> <dir>` to find where a symbol is defined/called | wasteful — scans every file | wrong-tool | LSP `lsp` tool: `goToDefinition` (defined) / `incomingCalls` (called) / `findReferences` | no |
| `cat <file>` (bash) | bash denied; `read` tool is better | wrong-tool | `read` tool | no |
| `head ...` (bash) | `head` is NOT installed on this machine (Unix tool) | other | `Select-Object -First N` (pwsh) | no |
| `tail ...` (bash) | `tail` is NOT installed on this machine (Unix tool) | other | `Select-Object -Last N` (pwsh) | no |
| `pwsh tools/harness/test-e2e.ps1 -Tests a,b,c` | the `[string[]]` array does not bind through the native `pwsh` boundary (arrives as one string → "Unknown scenario(s)") | misuse | `& tools/harness/test-e2e.ps1 -Tests a,b,c` (call operator) | no |
| `pwsh -Command "<script with $vars>"` (double-quoted) | the OUTER shell interpolates the inner script's `$vars`/`$_` before the inner pwsh sees them → the inner script arrives mangled → `ParserError` | misuse | single-quote the `-Command` argument (`pwsh -Command '...'`) so the outer shell does not interpolate; or write a temp `.ps1` and `-File` it | no |
| `pwsh -Command '... [ref]$null ...'` (ParseFile tokens ref) | `InvalidOperation: [ref] cannot be applied to a variable that does not exist` — `[ref]$null` is invalid; the ParseFile tokens ref needs a real variable | misuse | use the harness's built-in `pwsh tools/harness/test-e2e.ps1 -SelfCheck` (parse + helper invariants + Assert-SeedConsistent), or assign `$tokens = $null` first | no |

## Correct tool per task (avoid wrong-tool waste)

| task | correct tool | why not grep/bash |
|---|---|---|
| find where a symbol is defined / referenced / implemented; type info; file/workspace symbols | LSP `lsp` tool (`goToDefinition`, `findReferences`, `hover`, `documentSymbol`, `workspaceSymbol`, `goToImplementation`) | Roslyn-resolved; grep scans every file and misses overloads/partials |
| **direct** callers/callees of a symbol | LSP `lsp` tool (`incomingCalls`/`outgoingCalls`) | Roslyn-resolved; dodges Trailmark's `proxy.unresolved` trap |
| map transitive call paths / blast radius / taint / complexity / entry points | `trailmark` (code graph) | graph-level query LSP cannot answer (LSP call hierarchy is one hop) |
| list files / inventory a directory | `read` tool (directory) or `glob` tool | no shell spawn; `glob` avoids PowerShell's slow object pipeline |
| search file contents | `grep` tool | allowed for every agent; bash `grep` is not installed (use `rg` if bash needed) |
| read a file (or a slice) | `read` tool (with `offset`/`limit`) | no shell spawn; `offset`/`limit` reads only the slice |
| read gitignored paths (e.g. `log/`, `bin/`, `obj/`) | `rg --no-ignore` (bash) | the `grep` tool cannot reach gitignored paths |
| build / compile | `dotnet build` | allowed |
| run offline unit tests | `dotnet run --project tests/Telescope.Tests` / `tests/NeoVisual.Tests` | hermetic, no VS needed; substring filter + `--list` |
| run live e2e tests | `pwsh tools/harness/test-e2e.ps1` | boots VS Experimental; use `-Tests <subset>` during a loop |
| doc-reference lint | `pwsh tools/lint/check-doc-refs.ps1` | ~2s, no VS; unresolved backticked refs are blocking findings |
| web research | `webfetch` tool, or delegate to `skill-researcher` | bash curl/wget prompts and is the wrong tool |

## Failure log (append-only; newest at bottom)

### 2026-10-01 — hub-creator
- CMD: `Get-ChildItem -LiteralPath ".opencode" -Recurse -File`
- RESULT: permission denied (bash `*` → deny; no allow matches)
- REASON: wrong-tool
- ALTERNATIVE: `read` tool on the directory path, or `glob` for file patterns
- NEEDS-PERMISSION: no — wrong tool, not missing permission

### 2026-10-01 — trailmark-recon
- CMD: `uv run --with trailmark python - <<'EOF'` (bash-style heredoc)
- RESULT: PowerShell `ParserError: Missing file specification after redirection operator` (shell is pwsh, not bash)
- REASON: misuse
- ALTERNATIVE: pipe a PowerShell here-string — `$code = @'...'@; $code | uv run --with trailmark python -`
- NEEDS-PERMISSION: no — misuse, not missing permission

### 2026-10-02 — arch-auditor (slice B)
- CMD: `uv run --with trailmark python -` (parse_directory + QueryEngine.callers_of / to_json / preanalysis)
- RESULT: `ImportError: cannot import name 'from_directory'` (API is `parse_directory`); then `AttributeError: 'CodeGraph' object has no attribute '_graph'` (preanalysis + to_json); then `AttributeError: 'CodeGraph' object has no attribute 'find_node_id'` (callers_of on proxy ids)
- REASON: other — the installed trailmark package's `CodeGraph` lacks `_graph`/`find_node_id`, so the documented QueryEngine API (preanalysis, to_json, callers_of) is broken on this install; parsing works but querying does not
- ALTERNATIVE: `parse_directory` parses, but QueryEngine is unusable here — use grep/read for literal structural lookups until the trailmark version is reconciled with the vendored skill
- NEEDS-PERMISSION: no

### 2026-10-02 — code-review-hub (verification) — CORRECTION to the entry above
- CMD: `uv run --with trailmark python -` with `from trailmark.query import QueryEngine` + `QueryEngine.from_directory(r"C:\Users\hribara\MyExtension", language="c_sharp")` + `engine.callers_of(...)`
- RESULT: **WORKS** — returned the FocusGuard.HasToolWindowActionKeys caller list (proxy-aware). The correct import is `from trailmark.query import QueryEngine` (NOT `from trailmark.parse import QueryEngine`, which raises `ImportError: cannot import name 'QueryEngine' from 'trailmark.parse'`). `QueryEngine.from_directory` + `callers_of` are functional on this install.
- REASON: misuse — the entry above used the wrong import path (`trailmark.parse`) and/or a stale API shape; the documented `trailmark.query.QueryEngine` API is usable
- ALTERNATIVE: use `from trailmark.query import QueryEngine` for all structural queries; do NOT fall back to grep/read for call-graph questions
- NEEDS-PERMISSION: no

### 2026-10-03 — verification-agent (fzf final gate)
- CMD: `pwsh -NoProfile -Command "$errs=$null; ... [ref]$errs ... if ($errs) {...}"` (double-quoted `-Command` arg)
- RESULT: `ParserError: Missing condition in if statement after 'if ('` — the OUTER pwsh interpolated `$errs`/`$toks`/`$_` before the inner pwsh saw them, so the inner script arrived mangled
- REASON: misuse — nested pwsh quoting; a double-quoted `-Command` string is expanded by the calling shell
- ALTERNATIVE: single-quote the `-Command` argument (`pwsh -NoProfile -Command '...'`) so the outer shell does not interpolate; or write a temp `.ps1` and `-File` it
- NEEDS-PERMISSION: no

### 2026-10-03 — e2e-test-builder (Feature 6 RED)
- CMD: `rg -n "..." tests/NeoVisual.Tests/Program.cs | tail -80`
- RESULT: `tail: The term 'tail' is not recognized as a name of a cmdlet, function, script file, or executable program.`
- REASON: other — `tail` is a Unix tool, not installed on this Windows machine (same class as the known-bad `head`)
- ALTERNATIVE: `Select-Object -Last N` (pwsh), or omit the pipe and read the file with the `read` tool
- NEEDS-PERMISSION: no

### 2026-10-04 — verification-agent (Feature 6 final gate)
- CMD: `pwsh -NoProfile -Command '$null = [System.Management.Automation.Language.Parser]::ParseFile("tools/harness/test-e2e.ps1", [ref]$null, [ref]$errs); ...'`
- RESULT: `InvalidOperation: [ref] cannot be applied to a variable that does not exist.`
- REASON: misuse — `[ref]$null` is invalid; the ParseFile tokens ref needs a real variable
- ALTERNATIVE: use the harness's built-in `pwsh tools/harness/test-e2e.ps1 -SelfCheck` (parse + helper invariants + Assert-SeedConsistent), or assign `$tokens = $null` first
- NEEDS-PERMISSION: no
