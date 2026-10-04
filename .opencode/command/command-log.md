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
| `QueryEngine(<raw CodeGraph>)` — e.g. `engine = QueryEngine(parse_directory(...))` | the ctor stores the raw graph AS the store with no type check; every store-reaching method then AttributeErrors (`_graph` / `nodes_by_complexity` / `subgraph` / `find_node_id` missing from `CodeGraph`) | misuse | `QueryEngine.from_directory(dir, language="c_sharp")` or `QueryEngine.from_graph(graph)` (both wrap in `GraphStore`); 0.5.0 is the LATEST release — no upgrade exists or is needed; `preanalysis`/`summary`/`complexity_hotspots`/`subgraph`/`to_json` then all work | no |

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
| inventory/search under `.opencode/` (dot-directory) | `read` tool (directory) or `grep` tool; `rg --no-ignore` for content | the `glob` tool does not traverse dot-directories — returns "No files found" for existing `.opencode/...` paths (verified 2026-10-04: `glob .opencode/agent/*.md` → no files; `grep` on the same dir works) |
| search ONE known file | `grep` tool with `path` = the PARENT DIRECTORY + `include` = the file name | the `grep` tool's `path` is directory-scoped; a file path silently falls back to the parent and returns OTHER files' matches (verified 2026-10-04: `path=harness-common.ps1` bled matches from `test-e2e.ps1` + `iterate-telescope.ps1`) |
| disprove a suspected LSP false negative (newly created file, or `write`-tool diagnostics on untouched files) | `dotnet build` — 0 errors proves the symbol exists | the Roslyn index lags new files until the next build, and the `write` tool's LSP pass reports stale errors on files it did not touch (2026-10-04: build-agent + implementation-planner entries) |

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
- ALTERNATIVE: **SUPERSEDED 2026-10-04 — see the hub-creator CORRECTION entry at the bottom (constructor misuse, not a version issue; nothing is "unavailable").** Original: use `parse_directory(...)` + `len(graph.nodes)`/`len(graph.edges)` for counts; `callers_of` for callers; LSP for everything symbol-level; complexity/blast-radius/subgraph answers unavailable until trailmark is upgraded
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

### 2026-10-04 — skill-researcher (trailmark 0.5.0 QueryEngine API research)
- CMD: `uv run --with trailmark python -c '...'` (runtime confirmation: `from_directory` → `summary`/`complexity_hotspots`/`preanalysis`/`subgraph`/`to_json` on this repo)
- RESULT: permission denied — this agent's bash policy is `{"*": deny, "rg *": allow}`
- REASON: permission
- ALTERNATIVE: GitHub v0.5.0-tag source (raw.githubusercontent.com/trailofbits/trailmark/v0.5.0/src/trailmark/query/api.py) — conclusive without a runtime run: the five methods DO work on 0.5.0; the failing sessions passed the raw `parse_directory` CodeGraph to `QueryEngine(...)`. Correct construction: `QueryEngine.from_directory(...)` or `QueryEngine.from_graph(graph)`. A user-run snippet would upgrade source-proof to runtime-proof.
- NEEDS-PERMISSION: yes — allow `bash uv *` (i.e. `uv run --with trailmark python *`) to enable runtime verification
- AGENT: skill-researcher
- DATE: 2026-10-04

### 2026-10-04 — skill-researcher (trailmark installed-source lookup)
- CMD: `rg --files -g 'trailmark/query/api.py' -g 'trailmark/storage/graph_store.py' C:\Users\lojze\AppData\Local\uv\cache`
- RESULT: no matches — the installed wheel is not unpacked as loose source under the default uv cache root (likely stored as `.whl` zips or under a different env layout); further local search skipped per bounded-research protocol
- REASON: other
- ALTERNATIVE: none needed — the GitHub v0.5.0 tag source was used instead (PyPI 0.5.0 was built from the same verified release commit 6ee0f22)
- NEEDS-PERMISSION: no
- AGENT: skill-researcher
- DATE: 2026-10-04

### 2026-10-04 — hub-creator — CORRECTION (root cause) to the 2026-10-02 arch-auditor + 2026-10-04 trailmark-recon trailmark entries
- CMD: (no new command — source analysis) GitHub trailmark v0.5.0 tag: `src/trailmark/query/api.py`, `src/trailmark/storage/graph_store.py`, `src/trailmark/analysis/preanalysis.py` (fetched by skill-researcher; PyPI 0.5.0 was built from this exact release commit 6ee0f22)
- RESULT: the five "broken" methods are NOT broken and 0.5.0 is the LATEST release (2026-07-17; zero post-0.5.0 commits touch `trailmark/query` — no upgrade exists or is needed). Root cause of every logged AttributeError: the engine was constructed as `QueryEngine(<raw CodeGraph from parse_directory>)`. `QueryEngine.__init__(store)` performs NO type validation, so the raw graph became `_store`; every method reaching `self._store._graph` / `nodes_by_complexity` / `subgraph` / `find_node_id` then raised exactly the observed errors. This is the SAME root cause `.opencode/AGENT-FAILURES.md` fixed on 2026-09-29 ("the canonical entry point is `QueryEngine.from_directory`") — it regressed because this index's Known-bad row still recommended "upgrade trailmark" (now corrected above).
- REASON: misuse — wrong engine construction, not a broken wheel
- ALTERNATIVE: build the engine ONLY via `QueryEngine.from_directory(r"<repoRoot>", language="c_sharp")` or `QueryEngine.from_graph(graph)` (both wrap the graph in the `GraphStore` the query methods require; `from_graph` also runs `ensure_proxy_nodes`, matching this proxy-heavy graph). Then `preanalysis()` → `summary()` / `complexity_hotspots(threshold=8)` / `subgraph("high_blast_radius")` / `to_json()` all work on 0.5.0. **Runtime-proof: CONFIRMED 2026-10-04 (user-run snippet, exact form in the log's chat record): `trailmark 0.5.0`; `summary()` → `{'total_nodes': 2261, 'functions': 949, 'classes': 126, 'proxies': 898, 'call_edges': 4350, 'dependencies': ['Microsoft', 'System', 'MyExtension', 'Telescope', 'EnvDTE', 'EnvDTE80', 'TestHarness'], 'entrypoints': 0}`; `complexity_hotspots(threshold=8)` → 24; `subgraph("high_blast_radius")` → 89; `to_json()` → 3758925 bytes; `preanalysis()` silent-success. `entrypoints: 0` matches AGENTS.md trap 3 (VSIX — no detected entrypoints): signal, not failure.**
- NEEDS-PERMISSION: no (the fix itself is a snippet change); runtime-proof needs `bash uv *` for a spawnable agent or a user-run snippet
- AGENT: hub-creator
- DATE: 2026-10-04

### 2026-10-04 — hub-creator (runtime-verification attempt denied)
- CMD: `trailmark --version` + `uv run --with trailmark python -c '...'` (version check + runtime confirmation of the corrected construction)
- RESULT: permission denied — hub-creator's bash policy is `{"*": deny, "rg *": allow}` (same class as skill-researcher's 2026-10-04 entry above)
- REASON: permission
- ALTERNATIVE: delegate the runtime proof to a uv-capable agent (trailmark-recon — requires a `task: "trailmark-recon": allow` rule in hub-creator's permission block + restart), or the user runs the snippet from the CORRECTION entry above
- NEEDS-PERMISSION: yes — `bash "uv *"` / `"trailmark *"` allow for hub-creator, OR `task: "trailmark-recon": allow`
- AGENT: hub-creator
- DATE: 2026-10-04

### 2026-10-04 — docs-reviewer (Feature 7 re-review)
- CMD: `grep` tool, path = session dir (plans/ + artifacts/), include `*.md`, pattern `239|\b230\b|\+40|199`
- RESULT: 8 matches — ONLY plans/*.md; the two artifacts (feature7-section-a.md ~1720 lines, -b.md ~684 lines) were silently SKIPPED despite containing 7 matches (proven by a re-run scoped to the artifacts dir) — no error surfaced
- REASON: other — the grep tool's directory-scoped search silently omits large files; same silent-wrong-result class as the 2026-10-04 implementation-planner entry
- ALTERNATIVE: scope `path` to the subdirectory holding the targets (verified working: artifacts dir + include `feature7-*.md` returned all 7 matches); treat a too-quiet directory-scoped grep as SUSPECT and re-run scoped
- NEEDS-PERMISSION: no
- AGENT: docs-reviewer
- DATE: 2026-10-04

### 2026-10-04 — e2e-test-builder (columns-ux compile-RED, test-authoring)
- CMD: `dotnet build tests/Telescope.Tests/Telescope.Tests.csproj` with NEW tests referencing a PLANNED missing type (`ResultColumnTruncation`) in method SIGNATURES (a parameter type + a default parameter value)
- RESULT: csc reported ONLY the 3 signature-level errors (CS0246 param types + CS0103 default value) and ABORTED before method-body binding — the body errors (CS0103 for the ~50 expression-position type references, CS1061 missing members, CS1729 ctor arity, CS1501 overload arity) never surfaced, so the RED error list looked 20x thinner than the plan's pinned expectation
- REASON: misuse — Roslyn compiler phases: ANY error in the declaration phase (unknown type in a signature: param/return/base type, or an error-typed default parameter value, which cannot be encoded as a metadata constant) makes csc skip method-body binding entirely
- ALTERNATIVE: in compile-RED tests, keep every missing-type reference OUT of signatures — body positions only (local vars, call arguments, expression member accesses). Where a helper NEEDS the missing type as a parameter, type it `object` (boxed-enum equality via `Assert.Equal<object>`) or drop the parameter and hard-code the kind in the body. Verified: signatures clean → the full 63-error list surfaces (CS0103/CS1729/CS1061/CS1501)
- NEEDS-PERMISSION: no
- AGENT: e2e-test-builder
- DATE: 2026-10-04

### 2026-10-04 — build-agent (columns-ux BUILD) — `-Docs` array binding + `-File` arg semantics
- CMD: `pwsh tools/lint/check-doc-refs.ps1 -Docs docs/spec.md,.opencode/skills/vs-extension-dev/SKILL.md` (comma form via the bash tool); then `pwsh -File <temp>.ps1` whose body was `pwsh -NoProfile -File tools/lint/check-doc-refs.ps1 -Docs @('a','b')`
- RESULT: (1) the comma form arrived as ONE string → "doc file not found (skipped)" for the joined path; (2) the `-File` nested call failed with "A positional parameter cannot be found" — `pwsh -File` passes arguments as LITERAL strings (no expression evaluation), so `@('a','b')` is never an array
- REASON: misuse — same outer-shell/quoting class as the known-bad `pwsh -Command "..."` row, plus the `-File` literal-args nuance
- ALTERNATIVE: inside a temp `.ps1`, invoke with the CALL OPERATOR, not `-File`: `& '.\tools\lint\check-doc-refs.ps1' -Docs @('a','b')` — verified working (2 docs, 0 unresolved)
- NEEDS-PERMISSION: no
- AGENT: build-agent
- DATE: 2026-10-04

### 2026-10-04 — build-agent (preview-buffer BUILD) — the plan-pinned `Document.GetTextBuffer()` does NOT exist on the Roslyn 4.14 compile closure
- CMD: `dotnet build` with `PreviewEditorHost.TryGetWorkspaceBuffer` calling `document?.GetTextBuffer()` (the plan-preview-buffer pinned call); then metadata decode of the NuGet compile assets (`System.Reflection.Metadata` over `Microsoft.CodeAnalysis.Workspaces.dll` 4.14.0 netstandard2.0, `Microsoft.CodeAnalysis.EditorFeatures.Text.dll` 4.14.0, `Microsoft.VisualStudio.LanguageServices.dll` 4.14.0 net472)
- RESULT: `error CS1061: 'Document' does not contain a definition for 'GetTextBuffer'` — REAL (the compiler, not a stale LSP index; the LSP reported the identical error first and was RIGHT). Metadata proof: Workspaces.dll has NO `GetTextBuffer*` name at all; the only `GetTextBuffer` in the closure is `Microsoft.CodeAnalysis.Text.Extensions.GetTextBuffer(this SourceTextContainer) -> ITextBuffer` (public, `[Extension]`, in `Microsoft.CodeAnalysis.EditorFeatures.Text` — a transitive dep of `Microsoft.VisualStudio.LanguageServices` 4.14.0) plus `IVsTextBufferProvider.GetTextBuffer()` (COM interop). No `Document`-receiver overload exists anywhere in the compile-time reference set.
- REASON: other — the plan's pinned API name differs on the referenced Roslyn build (the plan's own BP-3 Fails-if STOP condition; the artifact's "NO new using is required" claim was wrong in a way that does not matter — no using fixes it)
- ALTERNATIVE: STOP and escalate to the hub per the plan (do NOT substitute a `GetTextAsync` + `CreateTextBuffer` text-clone — loses the workspace attachment; do NOT unilaterally substitute `GetTextSynchronously(...).TryGetTextBuffer()` either — that is a pinned-decision change only the hub/planner may make). NOTE: `dotnet run --project tests/Telescope.Tests` still works (it references ONLY Telescope.csproj, not MyExtension.csproj) — usable to isolate MyExtension-only build failures.
- NEEDS-PERMISSION: no — needs a PLAN DECISION, not a permission
- AGENT: build-agent
- DATE: 2026-10-04

### 2026-10-04 — build-agent (preview-buffer BUILD) — temp-script API notes (System.Reflection.Metadata in pwsh 7)
- CMD: `BlobReader.ReadTypeEntityReferenceToken()` / `ReadTypeEntityHandle()`; a PowerShell function called before its definition line
- RESULT: `does not contain a method named 'ReadTypeEntityReferenceToken'` / `'ReadTypeEntityHandle'`; `The term 'Read-SigType' is not recognized`
- REASON: misuse — this runtime's `BlobReader` exposes `ReadTypeHandle()` (returns `EntityHandle` directly, no `TypeHandle` struct); and PowerShell functions must be DEFINED before the runtime call site (top-to-bottom execution)
- ALTERNATIVE: `$sig.ReadSignatureTypeCode()` → `SignatureTypeCode.TypeHandle` → `$sig.ReadTypeHandle()` → resolve the `EntityHandle` (Kind = TypeReference/TypeDefinition); put helper functions at the TOP of the temp `.ps1`
- NEEDS-PERMISSION: no

### 2026-10-04 — implementation-planner (preview-buffer RE-PLAN) — NuGet-DLL reflection probe (the EASIER alternative to raw metadata decode)
- CMD: (1) a temp `.ps1` probe using `[System.Reflection.Assembly]::LoadFile` over the NuGet cache DLLs with an `AppDomain.AssemblyResolve` handler — WORKS and is far simpler than the System.Reflection.Metadata decode above; it verified the whole Roslyn 4.14 chain (`TextDocument.TryGetText(out SourceText)` public + inherited by `Document` (BaseType `TextDocument`), `SourceText.Container` public, `Extensions.GetTextBuffer/TryGetTextBuffer(SourceTextContainer)` public static with NULLABLE return and NO out param). (2) The handler initially referenced `$using:pkgRoot`
- RESULT: (2) failed — `A Using variable cannot be retrieved. A Using variable can be used only with Invoke-Command, Start-Job, or InlineScript` (repeated per resolution attempt), cascading into `Could not load file or assembly 'Microsoft.VisualStudio.Text.Data...'` when method `ToString()` needed parameter types
- REASON: misuse — `$using:` is remoting/job syntax only; a plain script-block delegate must close over the variable normally (or inline the literal path)
- ALTERNATIVE: hard-code the path inside the delegate (or reference the script-scope variable directly); the working probe pattern is in `C:\Users\lojze\AppData\Local\Temp\opencode\probe-roslyn-414-b.ps1` (session temp — recreate if pruned)
- NEEDS-PERMISSION: no
- AGENT: implementation-planner
- DATE: 2026-10-04
- AGENT: build-agent
- DATE: 2026-10-04

### 2026-10-04 — docs-reviewer (preview-buffer re-plan gate) — NuGet-cache + Roslyn-source path guesses
- CMD: (1) `rg -a -c "..." C:\Users\lojze\.nuget\packages\microsoft.codeanalysis.workspaces\4.14.0\lib\netstandard2.0\Microsoft.CodeAnalysis.Workspaces.dll` (guessed package id); (2) `glob` pattern `**/*.dll` with path=`...packages\microsoft.codeanalysis.workspaces\4.14.0` (nonexistent dir); (3) webfetch `raw.githubusercontent.com/dotnet/roslyn/<commit>/src/Workspaces/Core/Portable/TextDocument.cs` and `.../Document.cs`
- RESULT: (1) rg IO error os error 3 — the Workspaces assembly lives in package `microsoft.codeanalysis.workspaces.common`, not `microsoft.codeanalysis.workspaces`; (2) glob on a nonexistent directory fails with "ripgrep execution failed" (NOT a clean "No files found" — treat that error as "path does not exist"); (3) 404 ×2 — at the Roslyn 17.14 tag the document model lives under `src/Workspaces/Core/Portable/Workspace/Solution/` (`TextDocument.cs`, `Document.cs`, `Solution.cs`), not the Portable root; `Workspace_Editor.cs` is under `.../Portable/Workspace/`. The commit hash itself was valid (root `README.md` fetched fine)
- REASON: misuse — guessed paths
- ALTERNATIVE: validate a remote commit with a root file before guessing paths; list the parent dir via `api.github.com/repos/<org>/<repo>/contents/<dir>?ref=<commit>` to find real file paths; binary-presence grep pattern that works: `rg -a -c "<MethodName>" <dll>` (metadata #Strings heap is UTF-8; exit 1 + no output = absent)
- NEEDS-PERMISSION: no
- AGENT: docs-reviewer
- DATE: 2026-10-04

### 2026-10-04 — verification-agent (Gap 11 final gate)
- CMD: `rg -n "Team\.Git|\"g," MyExtension/Resources/default-keybindings.json` (double-quoted pwsh arg containing `\"`)
- RESULT: NO output, NO error — silent empty result. In pwsh a backslash does NOT escape a quote inside a double-quoted string (`"` terminates it), so the pattern arrived truncated (`Team\.Git|\`) plus a bogus bareword `g,` file arg; rg matched nothing in a file that contains `Team.Git` on lines 30-33
- REASON: misuse — the same outer-shell quoting class as the known-bad `pwsh -Command "..."` row, manifested as a SILENT empty rg result (worst kind: looks like a true negative)
- ALTERNATIVE: single-quote the whole rg pattern (`rg -n 'Team\.Git|"g,' <file>`), or use the `read`/`grep` tools — the `read` tool resolved it (BP-1 verified: 38 bindings, the three git pairs present, no `Team.Git.Branches`)
- NEEDS-PERMISSION: no
- AGENT: verification-agent
- DATE: 2026-10-04

### 2026-10-05 — docs-reviewer (Gap 11 build-plan re-plan gate)
- CMD: two `dotnet run --project tests/...` builds launched IN PARALLEL (NeoVisual.Tests + Telescope.Tests)
- RESULT: the Telescope build failed `CS2012: Cannot open 'Telescope\obj\Debug\net472\Telescope.dll' for writing -- being used by another process; file may be locked by 'Microsoft Defender Antivirus Service'` — transient; the sequential retry passed (224/0)
- REASON: misuse — concurrent MSBuild over the same repo's obj/bin (plus Defender scanning the fresh DLL) locks the output; repo `dotnet` builds must run SEQUENTIALLY
- ALTERNATIVE: run `dotnet run --project tests/...` one at a time; on a CS2012 Defender lock, one sequential retry suffices (do not misread it as a code failure)
- NEEDS-PERMISSION: no
- AGENT: docs-reviewer
- DATE: 2026-10-05

### 2026-10-05 — debug-agent (Feature 7 BP-A5 rev 2 verify) — e2e runtime-log location
- CMD: `Get-ChildItem '<%TEMP%>\telescope_scratch\log' -Filter '*-neovisual-exp.log'`; then a `Split-Path` chain off the script FILE path → `tools\log`
- RESULT: `Cannot find path ... because it does not exist` (×2) — the runtime log is NOT under the scratch solution and a Split-Path chain from the script file path is off by one level vs the harness's `$PSScriptRoot` chain
- REASON: misuse — guessed paths (same class as the 2026-10-04 docs-reviewer entry)
- ALTERNATIVE: the harness (`test-e2e.ps1:110-111`) computes `$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)` → the REPO ROOT, so the log dir is `<repoRoot>\log` (`C:\Projects\MyExtension\log`); newest run = `Get-ChildItem <repoRoot>\log -Filter '*-neovisual-exp.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1` — verified working (run 178)
- NEEDS-PERMISSION: no
- AGENT: debug-agent
- DATE: 2026-10-05

### 2026-10-05 — verification-agent (Feature 7 BP-B12 final gate)
- CMD: `rg -n 'pattern' Telescope/Overlay/TelescopeOverlay.cs Telescope/Overlay/Utils/Panes/*.cs` (a `*.cs` glob wildcard inside a path argument)
- RESULT: `rg: ...os error 123 (The filename, directory name, or volume label syntax is incorrect)` — the glob wildcard in the path arg was not expanded (passed literally to the filesystem on Windows)
- REASON: misuse — glob wildcards belong in rg's `-g`/`--glob` flag or the pattern, not in a literal path argument on Windows
- ALTERNATIVE: pass the DIRECTORY (`rg -n 'pattern' Telescope/Overlay/Utils/Panes`) — rg recurses it natively; verified working
- NEEDS-PERMISSION: no
- AGENT: verification-agent
- DATE: 2026-10-05
