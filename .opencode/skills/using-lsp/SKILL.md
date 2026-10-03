---
name: using-lsp
description: Use when navigating C# code in this repo — finding where a symbol is defined, all references/usages, type/signature info, a file's symbol outline, implementations/overrides, or DIRECT callers/callees. Covers the opencode `lsp` tool (goToDefinition, findReferences, hover, documentSymbol, workspaceSymbol, goToImplementation, incomingCalls, outgoingCalls) and the exact LSP-vs-Trailmark split. Use before grep/read/trailmark for any symbol-level question.
---

# Using LSP (primary code navigation)

## Overview

The `lsp` tool is the **primary** way to navigate this C# codebase. It is backed by
`roslyn-language-server` (opencode's built-in `csharp` server), so it answers from
Roslyn's resolved compilation model — not text matching. `grep` finds text; LSP finds
meaning.

Trailmark (the static code graph) is **secondary**: use it only for the graph-level
questions LSP cannot answer. This skill defines the split so the two never compete.

## When to use

Use LSP for anything symbol-level:

- where a symbol is defined / declared
- every reference or usage of a symbol
- a symbol's resolved type, signature, or docs
- the symbol outline of a file
- finding a symbol by name across the workspace
- implementations / overrides of an interface or virtual member
- **direct** callers / callees of a specific symbol

Do NOT use LSP for: transitive call paths, blast radius, taint, complexity, entry
points, structural diffs, or whole-repo overview — those are Trailmark's job (below).

## The `lsp` tool

All operations require `filePath`, `line`, `character` (1-based, as shown in editors).
`workspaceSymbol` also takes `query` (empty string = all symbols).

| operation | answers | notes |
|---|---|---|
| `goToDefinition` | where a symbol is defined | follows imports / partial classes |
| `findReferences` | every reference / usage | type-aware; excludes comments/strings |
| `hover` | resolved type / signature / docs | |
| `documentSymbol` | the symbol outline of one file | |
| `workspaceSymbol` | find a symbol by name across the workspace | pass `query` |
| `goToImplementation` | implementations / overrides | interface + virtual members |
| `incomingCalls` | **direct** callers of the symbol at the position | call hierarchy |
| `outgoingCalls` | **direct** callees of the symbol at the position | call hierarchy |

`prepareCallHierarchy` resolves the call-hierarchy item at a position; `incomingCalls` /
`outgoingCalls` then walk one hop from it.

## LSP vs Trailmark — the split

| question | tool | why |
|---|---|---|
| definition / references / hover / symbols / implementations | **LSP** | needs the resolved type model |
| **direct** callers/callees of a symbol you can point at | **LSP** `incomingCalls`/`outgoingCalls` | Roslyn resolves cross-class calls; Trailmark's parser graph turns them into `proxy.unresolved:<Type>.<Member>` nodes, so `callers_of` can return 0 for a member that has callers |
| transitive call paths (`paths_between`) | **Trailmark** | LSP call hierarchy is one hop; no path enumeration |
| blast radius / transitive reach (`ancestors_of`/`reachable_from`) | **Trailmark** | whole-repo transitive closure |
| taint, privilege boundaries, complexity hotspots, entry points, attack surface | **Trailmark** | LSP has no dataflow/security/complexity model |
| structural diff between commits | **Trailmark** | LSP is a live-editor protocol, not a snapshot differ |
| whole-repo, language-agnostic overview | **Trailmark** | one parse, no per-language server |

**The proxy trap:** a bare Trailmark `callers_of("Type.Member")` returning 0 is
SUSPECT — cross-class calls attach to `proxy.unresolved:Type.Member`. Prefer LSP
`incomingCalls` for direct callers; if you must use Trailmark, query the proxy id.
Never report "dead code / no callers" from a bare `callers_of` 0.

## Fallback order

1. `lsp` tool (symbol-level).
2. Trailmark (graph-level, per the table above).
3. `grep` / `glob` / `read` only for literal text/strings, non-source files, or when the
   `lsp` tool reports no server/result.

If the `lsp` tool errors with "No LSP server available for this file type", the server
is not running — fall back to Trailmark/grep and note it; do not silently hand-trace.

## Rationalizations to reject

| excuse | reality |
|---|---|
| "grep is faster" | grep scans every file and misses overloads/partials; LSP resolves the symbol exactly. |
| "Trailmark already gives callers" | Trailmark's parser graph has the proxy trap; LSP `incomingCalls` is Roslyn-resolved. |
| "LSP might not be running" | Try it once; if it errors, fall back and note it. Do not assume it is down. |
| "I only need a rough idea" | A wrong caller list is worse than none — use the exact tool. |

## Common mistakes

- Using `grep` to find a definition or callers when `lsp` can answer it exactly.
- Using Trailmark `callers_of` for a direct caller question and reporting a false 0.
- Asking LSP for transitive paths / blast radius (it is one hop only).
- Forgetting `line` / `character` are 1-based.
- Using `workspaceSymbol` without a `query` when you want a specific symbol.
