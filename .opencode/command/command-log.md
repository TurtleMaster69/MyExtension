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
| `dotnet run --project tests/Telescope.Tests` | offline Telescope unit tests | allowed; supports a substring filter as the first arg and `--list` |
| `dotnet run --project tests/NeoVisual.Tests` | offline NeoVisual unit tests | allowed; supports a substring filter as the first arg and `--list` |
| `pwsh tools/harness/test-e2e.ps1` | live E2E suite (boots VS Experimental) | allowed; slow — use `-Tests <subset>` during a loop, full suite only as the final gate |
| `pwsh tools/harness/test-e2e.ps1 -Tests <names>` | run a subset of e2e scenarios | allowed; `-Tests` accepts ANY form (comma-joined, array, or repeated flags); `-NoBootstrap` reuses an already-booted instance (same code state only) |
| `pwsh tools/harness/test-e2e.ps1 -List` | list registered scenarios | allowed; cheap no-VS parse check |
| `pwsh tools/lint/check-doc-refs.ps1` | doc-reference lint (unresolved backticked refs) | allowed; ~2s, no VS |
| `pwsh tools/lint/check-doc-content.ps1` | doc-content lint (12 fixed-state assertions incl. DOC-66-3 baseline attribution) | allowed; ~1s, no VS; `-List` prints the assertions without running |
| `lsp` tool (opencode) | symbol navigation (definition/references/hover/symbols/implementations/direct callers) | requires `"lsp": true` in config + `OPENCODE_EXPERIMENTAL_LSP_TOOL=true`; see `.opencode/LSP-SETUP.md` |
| `trailmark --version` / `uv run trailmark --version` | boot Trailmark (code graph) | allowed; if missing, install with `uv tool install trailmark` |
| `uv run --with trailmark python -` | run a Trailmark query snippet | allowed; always parse with `language="c_sharp"` (the CLI default `python` yields an empty graph here) |
| `git status` / `git diff` / `git log` / `git show` | inspect repo state | permission-dependent — allowed for agents with default bash; agents with `bash: {"*": deny, "rg *": allow}` are denied it (read-only) |
| `git add <paths>` / `git commit -m "<msg>"` | stage + commit the GREEN change set (neovim_hub's atomic-commit policy) | permission-dependent (same caveat); push/merge/pull remain denied |
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
| `pwsh -Command "<script with $vars>"` (double-quoted) | the OUTER shell interpolates the inner script's `$vars`/`$_` before the inner pwsh sees them → the inner script arrives mangled → `ParserError` | misuse | single-quote the `-Command` argument (`pwsh -Command '...'`) so the outer shell does not interpolate; or write a temp `.ps1` and `-File` it | no |
| `pwsh -Command '... [ref]$null ...'` (ParseFile tokens ref) | `InvalidOperation: [ref] cannot be applied to a variable that does not exist` — `[ref]$null` is invalid; the ParseFile tokens ref needs a real variable | misuse | use the harness's built-in `pwsh tools/harness/test-e2e.ps1 -SelfCheck` (parse + helper invariants + Assert-SeedConsistent), or assign `$tokens = $null` first | no |
| `engine.summary()` / `complexity_hotspots()` / `subgraph()` / `preanalysis()` (trailmark 0.5.0 QueryEngine) | AttributeError on this install (`_graph` / `nodes_by_complexity` / `subgraph` missing from `CodeGraph`) | other | `parse_directory` + `len(graph.nodes)`/`len(graph.edges)` for counts; `callers_of` for callers; LSP for symbol-level; upgrade trailmark for the rest | no |

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
- ALTERNATIVE: **SUPERSEDED — see the CORRECTION entry below.** The QueryEngine API works with the correct import (`from trailmark.query import QueryEngine`); do NOT fall back to grep/read for call-graph questions.
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

### 2026-10-04 — implementation-planner (Section A build plan)
- OPERATION: `glob` tool, pattern `.opencode/workspaces/.../artifacts/section-a.md` (file confirmed to exist via `read`)
- RESULT: "No files found" — false negative; the `glob` tool does not reach paths under `.opencode/` (likely hidden/gitignored traversal)
- REASON: wrong-tool
- ALTERNATIVE: `read` the directory path (lists entries), or `rg --no-ignore` for content search under `.opencode/`
- NEEDS-PERMISSION: no

### 2026-10-04 — trailmark-recon (structural digest)
- CMD: `uv run --with trailmark python -` — `from trailmark.query import QueryEngine` + `engine.summary()` / `engine.complexity_hotspots(threshold=8)` / `engine.subgraph("high_blast_radius")` (correct import per the 2026-10-02 CORRECTION; parse_directory succeeds, 0.4s)
- RESULT: `summary()` → `AttributeError: 'CodeGraph' object has no attribute '_graph'`; `complexity_hotspots()` → `no attribute 'nodes_by_complexity'`; `subgraph("high_blast_radius")` → `no attribute 'subgraph'` — trailmark 0.5.0's QueryEngine query surface is broken beyond the already-logged preanalysis/to_json. Raw `graph.nodes`/`graph.edges` (len 2037/4961) and `callers_of` (per 10-02 correction) still work.
- REASON: other — installed trailmark 0.5.0 QueryEngine internally accesses `store._graph`/`nodes_by_complexity`/`subgraph`, absent from `CodeGraph`
- ALTERNATIVE: use `parse_directory(...)` + `len(graph.nodes)`/`len(graph.edges)` for counts; `callers_of` for callers; LSP for everything symbol-level; complexity/blast-radius/subgraph answers unavailable until trailmark is upgraded
- NEEDS-PERMISSION: no — a trailmark version bump (`uv tool install trailmark` / pinned `--with trailmark==<newer>`) would likely fix it
- AGENT: trailmark-recon
- DATE: 2026-10-04

### 2026-10-04 — build-agent (Gap 1), logged by neovim_hub
- OPERATION: `lsp` symbol queries (e.g. `workspaceSymbol`) against NEWLY CREATED files (e.g. `MyExtension/Input/Utils/CloseWindowCommand.cs` right after creation)
- RESULT: stale-index false errors — the LSP reported the new symbol as non-existent until the next rebuild
- REASON: server — the Roslyn language server's index lags newly created files until a build refreshes it
- ALTERNATIVE: run `dotnet build` first (or re-query after the build); disprove a suspected LSP false negative with the compiler (`dotnet build` 0 errors), not the LSP index
- NEEDS-PERMISSION: no

### 2026-10-04 — skill-researcher (Gap 3 diagnostics-nav research)
- CMD: `rg -n "..." <file> | Select-Object -First 60` (bash)
- RESULT: permission denied — this agent's bash policy is `{"*": deny, "rg *": allow}`; the pipe to `Select-Object` broke the `rg *` pattern match
- REASON: permission
- ALTERNATIVE: run pure `rg` commands only (no pipes/other cmdlets); take the first N matches by reading the output, or use `rg -m <N>` to cap matches
- NEEDS-PERMISSION: no — pure `rg` is allowed and sufficient

### 2026-10-04 — implementation-planner (Section D rev 1)
- OPERATION: `grep` tool with `path` set to a FILE (`tools/harness/harness-common.ps1` / `iterate-telescope.ps1`)
- RESULT: no error, but the tool returned test-e2e.ps1's matches (wrong file) — silent wrong-result
- REASON: wrong-tool — the grep tool's `path` is directory-scoped; a file path is not honored
- ALTERNATIVE: `path` = the parent directory + `include` = the file name (verified working: PfxTel sanity hit in harness-common.ps1; true-negatives then trustworthy)
- NEEDS-PERMISSION: no
- AGENT: implementation-planner
- DATE: 2026-10-04

### 2026-10-04 — trailmark-recon (preview-pipeline digest)
- OPERATION: `lsp incomingCalls file=Telescope/Overlay/Utils/PreviewRenderer.cs line=42 char=19` and `lsp findReferences file=MyExtension/Adornments/BlockCaretAdornment.cs line=92 char=37`
- RESULT: "No results found" for both — but the symbols DO have callers (PreviewRenderer.Show ← TelescopeOverlay.cs:474; BlockCaretAdornment.Attach ← TextMotionHelper.cs:347)
- REASON: misuse — the character position pointed at `void` (method return type, char 19) and at the type name `BlockCaretAdornment` (char 37), not at the method identifier. char is 1-based and must land INSIDE the identifier.
- ALTERNATIVE: re-aim at the identifier: `incomingCalls ... char=21` ("Show") and `findReferences ... char=43` ("Attach") — both then returned correct results.
- NEEDS-PERMISSION: no

### 2026-10-04 — verification-agent (Gap 3 final gate)
- CMD: inline JSON check using `-like '[*` / `-like ']*'` wildcard patterns on binding names
- RESULT: `WildcardPatternException: The specified wildcard character pattern is not valid: [*` (repeated) — `[` opens a wildcard character class in PowerShell `-like`; the needed outputs (count/values) still printed
- REASON: misuse — unescaped `[`/`]` in a `-like` pattern
- ALTERNATIVE: use `-eq`/`-contains` for exact key checks, or escape with backticks (``-like '`[*``); `ConvertFrom-Json` + `.PSObject.Properties.Name` for the count
- NEEDS-PERMISSION: no
- AGENT: verification-agent
- DATE: 2026-10-04

### 2026-10-04 — debug-agent (Gap 1 e2e repair)
- CMD: `rg -n "Save-AllDocuments \$vs\.Id|Enter-NormalContext \$vs" tools/harness/test-e2e.ps1` (DOUBLE-quoted bash-tool arg)
- RESULT: `rg: regex parse error: (?:Save-AllDocuments \\.Id|...) — unclosed group` — the OUTER pwsh interpolated `$vs` to empty before rg saw the pattern (same mechanism as the known-bad `pwsh -Command "..."` row, via a plain double-quoted argument)
- REASON: misuse — double-quoted shell argument containing `$var`
- ALTERNATIVE: single-quote the whole rg pattern (`rg -n '...\$vs\.Id...'`) — verified working
- NEEDS-PERMISSION: no
- AGENT: debug-agent
- DATE: 2026-10-04

### 2026-10-04 — docs-reviewer (plan gate)
- CMD: `rg -n '...' tests/Telescope.Tests/Program.cs | rg -n 'public static' | head -0`
- RESULT: `head: The term 'head' is not recognized...` — pipe failed; the rg output before the pipe printed fine
- REASON: misuse — piped to `head`, already a Known-bad Unix tool in this index (same class as the 2026-10-03 e2e-test-builder `tail` entry)
- ALTERNATIVE: omit the pipe; cap with `rg -m <N>` or read the output directly
- NEEDS-PERMISSION: no
- AGENT: docs-reviewer
- DATE: 2026-10-04

### 2026-10-04 — skill-researcher (Gap 11 git-bindings research)
- OPERATION: `grep` tool (ripgrep-backed) on a saved webfetch tool-output file (`C:\Users\lojze\.local\share\opencode\tool-output\tool_*.md` — single giant line)
- RESULT: `Ripgrep JSON record exceeded 65536 bytes` — the file's lines exceed ripgrep's max record size, so the grep tool errors instead of matching
- REASON: other — long-line file (minified/single-line webfetch dump), not a tool bug
- ALTERNATIVE: bash `rg -o -N --max-columns 120 '<pattern>' <file>` — verified working (prints only matches, truncates long lines)
- NEEDS-PERMISSION: no
- AGENT: skill-researcher
- DATE: 2026-10-04

### 2026-10-04 — build-agent (results-columns docs resume)
- CMD: multi-line inline PowerShell (function definition + calls) passed as one bash-tool command
- RESULT: `ParserError: Unexpected token` — the multi-line script was mangled (same class as the known-bad `pwsh -Command "..."` row: the outer shell joins/interpolates before the inner pwsh parses)
- REASON: misuse
- ALTERNATIVE: write a temp `.ps1` and run `pwsh -NoProfile -File <temp>.ps1` — verified working
- NEEDS-PERMISSION: no
- AGENT: build-agent
- DATE: 2026-10-04

### 2026-10-04 — build-agent (results-columns docs resume)
- CMD: added a bare-leaf entry `'SyntaxHighlighter.cs'` to `$intentionallyAbsent` in check-doc-refs.ps1 to silence a deleted-file ref
- RESULT: lint still FAILed — `Test-PathRef` compares the FULL cited path (`$intentionallyAbsent -contains $p` where `$p` is the doc's trimmed path, e.g. `Overlay/Utils/SyntaxHighlighter.cs`), so a bare-leaf entry never matches a directory-prefixed citation
- REASON: misuse
- ALTERNATIVE: add the entry in the EXACT form the doc cites it (the archives cite `Overlay/Utils/SyntaxHighlighter.cs` without the `Telescope/` prefix) — verified working; note the M23 `LinqExtensionMethods.cs` entry only works because that doc cites it as a bare filename
- NEEDS-PERMISSION: no
- AGENT: build-agent
- DATE: 2026-10-04

### 2026-10-04 — implementation-planner (preview-buffer artifact)
- OPERATION: `write` tool (a Markdown artifact under `.opencode/workspaces/.../artifacts/`) — its automatic LSP pass then reported CS0246-style errors in UNTOUCHED files (`InputHandler.cs` CloseWindowCommand/ErrorListGatherer/DiagnosticNavigator, `TelescopeOverlay.cs` ResultColumn/ColumnVisibilityModel/IPreviewEditor, `TelescopeController.cs`/`MyExtensionPackage.cs` IPreviewEditor, `tests/NeoVisual.Tests/Program.cs`)
- RESULT: stale-index false errors — every named type EXISTS in the repo (IPreviewEditor.cs, DiagnosticNavigator, ResultColumn read directly; the 2026-10-04 baseline is all-GREEN per docs/progress.md) and no source file was modified (a .md artifact only)
- REASON: server — same stale-index class as the 2026-10-04 build-agent entry, surfaced via the write tool's LSP side-channel on files that were not touched
- ALTERNATIVE: disprove with the compiler (`dotnet build` 0 errors), not the LSP index; ignore write-tool LSP diagnostics on untouched files when the baseline is GREEN
- NEEDS-PERMISSION: no
- AGENT: implementation-planner
- DATE: 2026-10-04
