<#
check-doc-refs.ps1 — mechanical doc-reference lint.

Guarantees the backticked symbol / file / function references in the durable
workflow docs stay resolvable against the actual source tree. This is the
detection layer for doc drift: a renamed or removed class, harness function, or
file that the docs reference is reported here instead of silently desyncing
(the `PreviewNavigator` phantom-name incident was exactly this failure mode).

Scoped to durable docs only (auto-includes every agent file):
   - .opencode/agent/*.md
   - .opencode/skills/vs-extension-dev/SKILL.md
   - AGENTS.md
   - docs/spec.md, docs/progress.md
   - docs/architecture-review.md (holds the live code + workflow reports, incl. the
     F1-F46 code findings and the W1-W21 workflow findings)
Excluded by design:
  - docs/implementation_plan.md — transient per-item file; its verbatim code
    snippets contain snippet-local identifiers (false positives), and its
    identifiers are validated by the build itself.

Usage:
  pwsh tools/check-doc-refs.ps1             # lint the default doc set
  pwsh tools/check-doc-refs.ps1 -Docs a.md  # lint specific files
Exit 0 = all references resolve. Exit 1 = drift found (lines printed to stderr).

Classification of a backticked token:
  - all-caps (e.g. `SEVERITY`, `DTE`, `EOL`)        -> constant/abbreviation, skipped
  - contains % (env-var path, e.g. `%TEMP%`)        -> skipped
  - `Verb-Noun`                                     -> PowerShell: built-in cmdlet
                                                       (skip) or must exist in tools/*.ps1
  - .cs/.json/.ps1/.slnx/.md suffix or path separator -> file path: must exist
                                                       (strips a trailing :<line> ref)
  - otherwise PascalCase symbol                     -> must be greppable in the source
                                                       roots, or be on the external
                                                       allowlist / `IVs` prefix / key-name list
#>
[CmdletBinding()]
param(
    [string[]]$Docs = $null,
    [switch]$SelfCheck
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Docs) {
    $Docs = @(
        'AGENTS.md',
        '.opencode/skills/vs-extension-dev/SKILL.md',
        'docs/spec.md',
        'docs/progress.md',
        'docs/architecture-review.md'
    ) + (Get-ChildItem (Join-Path $repoRoot '.opencode/agent') -Filter '*.md' |
        ForEach-Object { '.opencode/agent/' + $_.Name })
}

# Source roots where C# symbols must resolve (excludes derived output).
$sourceRoots = @('MyExtension', 'Telescope', 'tests')
$sourceFiles = Get-ChildItem $sourceRoots -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs)\\' }
$sourceText = ($sourceFiles | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"

# External / non-symbol PascalCase tokens that legitimately appear in the docs.
$externalAllowlist = @(
    # WPF / BCL / VS-SDK types
    'Key', 'Keys', 'KeyEventArgs', 'KeyBinding', 'Window', 'Dispatcher',
    'TextBox', 'TextBoxBase', 'RichTextBox', 'IReadOnlyCollection', 'IReadOnlySet',
    'ICompletionBroker', 'IWpfTextView', 'IWpfTextViewCreationListener',
    'ThreadHelper', 'AsyncPackage', 'JoinableTaskFactory', 'EventHandler',
    'EventHandlerType', 'IVim', 'IVimBuffer', 'IMode', 'DTE', 'EnvDTE',
    'SwitchToMainThreadAsync', 'ThrowIfNotOnUIThread',
    # C# / MSBuild language features (not symbols)
    'LangVersion', 'Nullable', 'InternalsVisibleTo',
    # Intentional folder typo — a directory, never a symbol
    'CardinalMovment',
    # VS editor SDK / WPF event / external package types
    'AdornmentLayerDefinition', 'KeyDown', 'MessagePack',
    # Roslyn symbols referenced from host gatherers
    'ISymbol', 'SymbolFinder', 'FindImplementationsAsync', 'DeclaringSyntaxReferences', 'Location',
    # Key names used in binding notation
    'Space', 'Return', 'Escape', 'Enter', 'Ctrl', 'Shift', 'Alt', 'Tab',
    'PageUp', 'PageDown', 'Home', 'End',
    # Symbols named in a "code smells / recently removed" context: the reports cite
    # the OLD gating (F14: the removed ICompletionBroker.IsCompletionActive check) and
    # a proposed extraction (F18/F21: RectCoordinate.Right/Bottom/IsEmpty/Intersects),
    # and the net472-compliance check lists FORBIDDEN .NET 5+ APIs to prove their
    # absence (F46 verified-clean) — none of these are expected to exist in the source.
    'IsCompletionActive', 'IsEmpty', 'Intersects', 'HashCode', 'MaxBy', 'MinBy',
    # Trailmark graph-export docs (W23) cite the Python builtin exceptions raised by
    # the wrong to_json() usage — external runtime names, not C# symbols.
    'TypeError', 'KeyError',
    # .NET BCL interop types cited by the code-review-hub / code-review-worker agent
    # docs (2026-09-29) in the P/Invoke review context — external runtime types.
    'SafeHandle'
)

# File paths the docs mention that are intentionally absent (documented-absent).
$intentionallyAbsent = @(
    '.opencode/PROGRESS.md'   # superseded by docs/progress.md; must NOT be recreated
)

# Hub-created runtime artifacts (not in the repo until the loop creates them).
# NOTE: log/tools-hash.txt is allowlisted here because it is absent on a fresh clone
# (before the first harness run) — its existence is enforced by test-e2e.ps1's
# Write-ToolsHash, which materializes it at the bootstrap of every real run (M-C1),
# not by this lint. A missing hash on a fresh clone is NOT drift.
# log/seed-expected/ IS allowlisted: the expected-result tree is written by test-e2e.ps1 only
# during a real bootstrap (Write-SeedExpected) and consumed by the seed-leak end-of-run guard —
# it is a per-run artifact, absent on a fresh clone, and its absence is NOT drift.
$runtimeArtifacts = @(
    'log',              # per-run log dir (gitignored; absent on a fresh clone)
    'log/seed-expected',
    'log/tools-hash.txt'
)

# Bare filenames that legitimately have no repo counterpart: user-config file and
# scratch-solution seed files (seeded at runtime into %TEMP%\telescope_scratch).
$bareNameAllowlist = @(
    'keybindings.json', 'Motions.cs', 'TodoProbe.cs', 'Program.cs',
    'Alpha.cs', 'Beta.cs', 'Gamma.cs', 'Delta.cs', 'Epsilon.cs',
    'Service.cs', 'User.cs', 'Order.cs', 'AuthService.cs',
    'Probe.cs', 'TelescopeTest.sln',
    'Shared.cs', 'Reader.cs', 'Writer.cs',   # references-finder seed files (Models/Shared.cs + readers/writers)
    'GrepProbe.cs',                         # grep-finder seed file (Probe/GrepProbe.cs, GREPME marker)
    'IShape.cs', 'Shape.cs'                 # implementation-finder seed files (Models/IShape.cs + Shape.cs)
)

# Proposed-future symbols / files named in the backlog's fix suggestions — they
# deliberately do not exist yet (e.g. F22's extracted state machine, F37's shared
# harness module). When the item lands, the real name must replace the proposal.
$proposedSymbols = @(
    'LeaderSequenceMatcher', 'SimpleKeyBuilder'          # F22 — proposed extraction
    'FzfFinder', 'FocusKeeper'                          # code-review backlog — proposed finder / M26 helper
)
$proposedPaths = @(
    'tools/harness-common.ps1'                           # F37 — proposed shared module
)

# Template / placeholder paths referenced by the hub-creation ecosystem agents
# (hub-creator, hub-reviewer, skill-researcher, skill-verifier). These META agents
# describe creating hubs, workspaces, and skills, so they legitimately cite paths
# that do not exist in this repo: workspace template files (state.md/tasks.md/
# log.md/skills.md), alternative opencode spellings (.opencode/agents/), global
# config paths outside the repo (~/.config/...), and illustrative template paths
# (.opencode/workspaces/<hub>/sessions/<session-id>/, .opencode/skills/<name>/,
# .opencode/plugin/compaction.ts). These are NOT drift — they are the very files the
# hub-creator is instructed to create. Entries are in Test-PathRef's trimmed form
# (trailing '/' stripped). Verified 2026-09-28: none of these tokens appear in the
# non-meta scanned docs (AGENTS.md / spec / progress / architecture-review / SKILL.md).
$templatePaths = @(
    'README.md',                                        # generic docs-index reference (no README at repo root)
    '~/.config/opencode/opencode.json',                 # global opencode config, outside the repo
    '.opencode/workspaces/implementation-hub',          # example workspace the hub-creator would create
    '.opencode/agents',                                 # valid plural spelling (this repo uses singular agent/)
    'log.md', 'skills.md', 'state.md', 'tasks.md',      # workspace template files the hub-creator creates
    '.opencode/workspaces',                             # hub workspaces root (created at runtime)
    '.opencode/plugin/compaction.ts',                   # optional compaction plugin hook (knowledge-base §6)
    '.opencode/workspaces/<hub>/sessions/<session-id>', # session-scoped workspace template (§9)
    '.opencode/workspaces/code-review-hub/sessions/<session-id>',       # concrete hub workspace template (code-review-hub agent docs)
    '.opencode/workspaces/neovim-planning-hub/sessions/<session-id>',   # concrete hub workspace template (neovim-planning-hub agent docs)
    '.opencode/skills/<name>'                           # skill discovery-path template
)

$tokenRegex    = [regex]'(?<=`)[^`\r\n]+(?=`)'  # inline backticked content (single-line; triple-backtick fences can't pair with inline spans)
$allCapsRegex  = [regex]'^[A-Z0-9_]{2,}$'
$pascalRegex   = [regex]'^[A-Z][A-Za-z0-9_]{2,}$'
$verbNounRegex = [regex]'^[A-Z][a-z]+-[A-Z][a-zA-Z]*$'
$pathSuffixRe  = [regex]'\.(cs|json|ps1|slnx|md)$'

$issues = @()
$warnings = @()
$refCount = 0

function Test-SymbolExists([string]$token) {
    return [regex]::IsMatch($sourceText, "\b$([regex]::Escape($token))\b")
}

function Test-ToolFunctionExists([string]$token) {
    if (Get-Command -Name $token -ErrorAction SilentlyContinue) { return $true }  # built-in cmdlet
    foreach ($f in Get-ChildItem (Join-Path $repoRoot 'tools') -Filter '*.ps1') {
        # m27: definition-anchored match — `(?m)^function\s+<token>\b` resolves only a real function
        # definition at a line start, not any whole-file occurrence of the token. MUST use (?m)
        # (Multiline) so ^ anchors at line starts; without it ^ anchors at string start and nothing
        # resolves.
        if ([regex]::IsMatch([System.IO.File]::ReadAllText($f.FullName), "(?m)^function\s+$([regex]::Escape($token))\b")) {
            return $true
        }
    }
    return $false
}

function Test-PathRef([string]$token) {
    $p = $token -replace ':[\d,]+$', ''       # strip "file.cs:123" / "file.cs:806,910" line refs
    $p = $p -replace ':\d+-\d+$', ''          # strip "file.cs:102-115" line RANGE refs
    $p = $p.TrimEnd('/') -replace '\\', '/'
    if ($intentionallyAbsent -contains $p) { return $true }
    if ($runtimeArtifacts -contains $p) { return $true }
    if ($proposedPaths -contains $p) { return $true }
    if ($templatePaths -contains $p) { return $true }
    $full = Join-Path $repoRoot $p
    if (Test-Path -LiteralPath $full) { return $true }
    $name = Split-Path $p -Leaf
    if ($bareNameAllowlist -contains $name) { return $true }
    # filename fallback: any file with this name anywhere in the repo
    $hit = Get-ChildItem (Join-Path $repoRoot '.') -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs|node_modules)\\' } | Select-Object -First 1
    return [bool]$hit
}

if ($SelfCheck) {
    # No-VS seam (m27): prove the lint catches a deleted-function reference (exit 1 + issue line)
    # and warns non-fatally on a missing doc (exit 0), WITHOUT touching the real doc set. Re-invokes
    # this script as a child process against temp docs so the stderr diagnostics are captured.
    try {
        Write-Host '==> SelfCheck (no VS)' -ForegroundColor Cyan
        $tmpDir = Join-Path $env:TEMP ("docrefs_selfcheck_" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
        try {
            # (1) deleted-function doc -> exit 1 + the issue line.
            $badDoc = Join-Path $tmpDir 'bad.md'
            [System.IO.File]::WriteAllText($badDoc, "This references ``Some-DeletedFunction`` which does not exist.`n")
            $out = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $PSCommandPath -Docs $badDoc 2>&1
            $code = $LASTEXITCODE
            $joined = ($out | Out-String)
            if ($code -ne 1) { throw "SelfCheck: deleted-function doc exited $code (expected 1)" }
            if ($joined -notmatch 'no such PowerShell function/cmdlet in tools/') { throw 'SelfCheck: deleted-function issue line missing' }
            Write-Host '    PASS: SelfCheck: deleted-function doc fails with the expected issue line' -ForegroundColor Green

            # (2) nonexistent doc path -> warning line + exit 0.
            $missingDoc = Join-Path $tmpDir 'does-not-exist.md'
            $out2 = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $PSCommandPath -Docs $missingDoc 2>&1
            $code2 = $LASTEXITCODE
            $joined2 = ($out2 | Out-String)
            if ($code2 -ne 0) { throw "SelfCheck: missing-doc run exited $code2 (expected 0)" }
            if ($joined2 -notmatch 'WARNING: .*doc file not found \(skipped\)') { throw 'SelfCheck: missing-doc warning line missing' }
            Write-Host '    PASS: SelfCheck: missing-doc warning emitted and exit stays 0' -ForegroundColor Green
        } finally {
            if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue }
        }
        Write-Host 'SELFTEST PASS' -ForegroundColor Green
        exit 0
    } catch {
        Write-Host "SELFTEST FAIL: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

foreach ($docRel in $Docs) {
    # m27/BP-16: accept an absolute doc path (the -SelfCheck temp docs live in %TEMP%);
    # Join-Path would concatenate a rooted path onto $repoRoot into a garbage path.
    $docPath = if ([System.IO.Path]::IsPathRooted($docRel)) { $docRel } else { Join-Path $repoRoot $docRel }
    if (-not (Test-Path -LiteralPath $docPath)) {
        # m27: a missing doc is tolerated (it may legitimately not exist yet) but no longer silent —
        # emit a non-fatal warning. Exit code stays 0 when only warnings exist.
        $warnings += $docRel
        [Console]::Error.WriteLine("WARNING: $docRel — doc file not found (skipped)")
        continue
    }
    $text = [System.IO.File]::ReadAllText($docPath)
    foreach ($m in $tokenRegex.Matches($text)) {
        $token = $m.Value.Trim()
        if ($token.Length -eq 0) { continue }
        if ($token -match '%') { continue }                   # env-var path
        if ($allCapsRegex.IsMatch($token)) { continue }       # constant / abbreviation
        $refCount++

        if ($verbNounRegex.IsMatch($token)) {
            if (-not (Test-ToolFunctionExists $token)) {
                $issues += "$docRel : ``$token`` — no such PowerShell function/cmdlet in tools/"
            }
        }
        elseif (($pathSuffixRe.IsMatch($token) -or
                ($token -cmatch '^(MyExtension|Telescope|tests|tools|docs|\.opencode|log)/')) -and
                $token -notmatch '[\s=]' -and $token -notmatch '\.\.\.') {
            # file-path reference: has a code extension, OR is a repo-root-relative
            # path; must be a bare token (no spaces / `=` / `...` — those are log
            # lines and command strings, e.g. `preview file=...TodoProbe.cs`)
            if (-not (Test-PathRef $token)) {
                $issues += "$docRel : ``$token`` — no such file (relative to repo root)"
            }
        }
        elseif ($pascalRegex.IsMatch($token)) {
            if ($externalAllowlist -contains $token) { continue }
            if ($proposedSymbols -contains $token) { continue }
            if ($token -match '^IVs') { continue }             # VS SDK interface prefix
            if (-not (Test-SymbolExists $token)) {
                $issues += "$docRel : ``$token`` — no such symbol in $($sourceRoots -join ', ')"
            }
        }
        # everything else (lowercase, key combos, log lines, ...) is not a symbol reference
    }
}

if ($issues.Count -gt 0) {
    $msg = "DOC-REF DRIFT ($($issues.Count) unresolved):`n" +
        (($issues | ForEach-Object { "  $_" }) -join "`n") +
        "`nDocs scanned: $($Docs.Count) | backticked references checked: $refCount"
    [Console]::Error.WriteLine($msg)
    exit 1
}
Write-Output "[PASS] $($Docs.Count) docs scanned, $refCount backticked references checked, 0 unresolved."
exit 0