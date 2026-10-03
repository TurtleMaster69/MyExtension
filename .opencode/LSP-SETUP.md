# LSP setup (MyExtension)

opencode's `lsp` tool is the primary code-navigation tool for this repo. Two things must be
true for it to work:

1. **LSP enabled in config** — opencode disables LSP unless `lsp` is set. This repo sets
   `"lsp": true` in `opencode.jsonc` (project) and `~/.config/opencode/opencode.jsonc` (global).
2. **The `lsp` tool enabled** — the tool is experimental and is only registered when the
   environment variable `OPENCODE_EXPERIMENTAL_LSP_TOOL=true` is set. There is **no config
   field** for it.

## 1. Install the C# language server

opencode's built-in `csharp` server uses **`roslyn-language-server`** for `.cs`/`.csx`.
It is not installed on this machine. Install it once (requires the .NET SDK):

```powershell
dotnet tool install --global roslyn-language-server --prerelease
```

Verify:

```powershell
roslyn-language-server --version
```

If the command is not found, add the .NET tools dir to PATH:
`%USERPROFILE%\.dotnet\tools` (opencode also checks this path directly, so a PATH entry is
not strictly required).

> opencode can auto-install the server on first use (`dotnet tool install --global
> roslyn-language-server --prerelease`). Installing it yourself makes the first LSP call
> fast and avoids a mid-session download. Set `OPENCODE_DISABLE_LSP_DOWNLOAD=true` to
> forbid auto-download.

## 2. Enable the experimental `lsp` tool

Set a persistent user environment variable (Windows), then restart the terminal and opencode:

```powershell
setx OPENCODE_EXPERIMENTAL_LSP_TOOL true
```

Alternatively, for the current PowerShell session only:

```powershell
$env:OPENCODE_EXPERIMENTAL_LSP_TOOL = "true"
```

`OPENCODE_EXPERIMENTAL=true` also enables it (along with every other experimental feature);
prefer the narrow variable.

## 3. Restart opencode

Config and environment variables are read once at startup. Quit and restart opencode, then
verify the `lsp` tool is available (it should appear in the tool list). If it does not, check
that `OPENCODE_EXPERIMENTAL_LSP_TOOL` is visible to the opencode process.

## What the tool does

`lsp` operations (all need `filePath`, `line`, `character`, 1-based; `workspaceSymbol` also
takes `query`): `goToDefinition`, `findReferences`, `hover`, `documentSymbol`,
`workspaceSymbol`, `goToImplementation`, `prepareCallHierarchy`, `incomingCalls`,
`outgoingCalls`.

Use LSP for symbol-level navigation and **direct** callers/callees. Use Trailmark only for
graph-level questions LSP cannot answer (transitive paths, blast radius, taint, complexity,
entry points, structural diffs). Full method: `.opencode/skills/using-lsp/SKILL.md`.

> The project `opencode.jsonc` is git-excluded (`.git/info/exclude`), so other clones do not
> inherit LSP enablement; the global `~/.config/opencode/opencode.jsonc` covers this machine.

## Troubleshooting

- **`lsp` denied** — if your config sets a restrictive `permission` block, ensure `lsp` is
  allowed (the hub agents already set `lsp: allow`).
- **"No LSP server available for this file type"** — the server is not running. Confirm
  `roslyn-language-server` is installed and `"lsp": true` is in the merged config.
- **`lsp` tool missing entirely** — `OPENCODE_EXPERIMENTAL_LSP_TOOL=true` is not set for the
  opencode process (restart the terminal after `setx`).
- **No diagnostics / slow first call** — the server is loading the solution; the first call
  can take a few seconds.

## Failed `lsp` calls are logged

Every agent is instructed to append a failed `lsp` operation (error, no server, or a wrong/empty
result) to the Failure log in `.opencode/command/command-log.md` — OPERATION + params, RESULT, REASON
(`misuse` | `server` | `other`), ALTERNATIVE, AGENT, DATE. This is how misuse (bad `line`/`character`,
wrong `filePath`, wrong operation) gets collected and fixed later. See the protocol at the top of that
file.
