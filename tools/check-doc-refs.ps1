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
    [string[]]$Docs = $null
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Docs) {
    $Docs = @(
        'AGENTS.md',
        '.opencode/skills/vs-extension-dev/SKILL.md',
        'docs/spec.md',
        'docs/progress.md'
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
    # Key names used in binding notation
    'Space', 'Return', 'Escape', 'Enter', 'Ctrl', 'Shift', 'Alt', 'Tab',
    'PageUp', 'PageDown', 'Home', 'End'
)

# File paths the docs mention that are intentionally absent (documented-absent).
$intentionallyAbsent = @(
    '.opencode/PROGRESS.md'   # superseded by docs/progress.md; must NOT be recreated
)

# Hub-created runtime artifacts (not in the repo until the loop creates them).
# NOTE: log/tools-hash.txt is NOT allowlisted — it must exist (test-e2e.ps1's Write-ToolsHash
# materializes it every run), so a missing hash is a lint failure (M-C1).
$runtimeArtifacts = @()

# Bare filenames that legitimately have no repo counterpart: user-config file and
# scratch-solution seed files (seeded at runtime into %TEMP%\telescope_scratch).
$bareNameAllowlist = @(
    'keybindings.json', 'Motions.cs', 'TodoProbe.cs', 'Program.cs',
    'Alpha.cs', 'Beta.cs', 'Gamma.cs', 'Delta.cs', 'Epsilon.cs',
    'Service.cs', 'User.cs', 'Order.cs', 'AuthService.cs',
    'Probe.cs', 'TelescopeTest.sln'
)

# Proposed-future symbols / files named in the backlog's fix suggestions — they
# deliberately do not exist yet (e.g. F22's extracted state machine, F37's shared
# harness module). When the item lands, the real name must replace the proposal.
$proposedSymbols = @(
    'LeaderSequenceMatcher', 'SimpleKeyBuilder'          # F22 — proposed extraction
)
$proposedPaths = @(
    'tools/harness-common.ps1'                           # F37 — proposed shared module
)

$tokenRegex    = [regex]'(?<=`)[^`\r\n]+(?=`)'  # inline backticked content (single-line; triple-backtick fences can't pair with inline spans)
$allCapsRegex  = [regex]'^[A-Z0-9_]{2,}$'
$pascalRegex   = [regex]'^[A-Z][A-Za-z0-9_]{2,}$'
$verbNounRegex = [regex]'^[A-Z][a-z]+-[A-Z][a-zA-Z]*$'
$pathSuffixRe  = [regex]'\.(cs|json|ps1|slnx|md)$'

$issues = @()
$refCount = 0

function Test-SymbolExists([string]$token) {
    return [regex]::IsMatch($sourceText, "\b$([regex]::Escape($token))\b")
}

function Test-ToolFunctionExists([string]$token) {
    if (Get-Command -Name $token -ErrorAction SilentlyContinue) { return $true }  # built-in cmdlet
    foreach ($f in Get-ChildItem (Join-Path $repoRoot 'tools') -Filter '*.ps1') {
        if ([regex]::IsMatch([System.IO.File]::ReadAllText($f.FullName), "\b$([regex]::Escape($token))\b")) {
            return $true
        }
    }
    return $false
}

function Test-PathRef([string]$token) {
    $p = $token -replace ':[\d,]+$', ''       # strip "file.cs:123" / "file.cs:806,910" line refs
    $p = $p.TrimEnd('/') -replace '\\', '/'
    if ($intentionallyAbsent -contains $p) { return $true }
    if ($runtimeArtifacts -contains $p) { return $true }
    if ($proposedPaths -contains $p) { return $true }
    $full = Join-Path $repoRoot $p
    if (Test-Path -LiteralPath $full) { return $true }
    $name = Split-Path $p -Leaf
    if ($bareNameAllowlist -contains $name) { return $true }
    # filename fallback: any file with this name anywhere in the repo
    $hit = Get-ChildItem (Join-Path $repoRoot '.') -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs|node_modules)\\' } | Select-Object -First 1
    return [bool]$hit
}

foreach ($docRel in $Docs) {
    $docPath = Join-Path $repoRoot $docRel
    if (-not (Test-Path -LiteralPath $docPath)) { continue }   # doc may legitimately not exist yet
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