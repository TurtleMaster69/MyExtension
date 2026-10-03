---
description: Shared authoritative copy of the per-repo Trailmark guidance for MyExtension. NOT an agent — disabled so it does not appear in the agent list; read this file directly.
disable: true
---

# Trailmark guidance (MANDATORY) — shared by all hubs and subagents

This is the single authoritative copy of the per-repo Trailmark guidance. Every
agent/hub file that does structural work references this file; do NOT duplicate the
guidance inline in an agent (a divergence would silently change all of them). If the
guidance changes, change it here and update the one-line reference in each agent.

## When Trailmark is mandatory

**LSP is primary for symbol-level navigation** — use the `lsp` tool (`goToDefinition`,
`findReferences`, `hover`, `documentSymbol`, `workspaceSymbol`, `goToImplementation`,
and **direct** `incomingCalls`/`outgoingCalls`) before `grep`/`read`/Trailmark. It is
Roslyn-resolved, so it dodges the `proxy.unresolved` trap for cross-class callers. See
`.opencode/skills/using-lsp/SKILL.md`.

AGENTS.md makes Trailmark mandatory for **graph-level** structural questions: call paths
(`paths_between`), transitive reach (`ancestors_of`/`reachable_from`), blast radius,
taint, privilege boundaries, complexity hotspots, entry points, structural diffs,
"what reaches Y" / "what breaks if I change Z". Use Trailmark (vendored under
`.opencode/skills/trailmark`) for these — do NOT hand-trace call graphs with `grep`.
`grep`/`glob`/`read` are only for literal text, non-source files, and single-file
lookups where a graph adds nothing. Never silently fall back to manual code reading
(the `trailmark` skill's "Rationalizations to Reject" table forbids it).

## Boot

- `trailmark --version` (or `uv run trailmark --version`; install with
  `uv tool install trailmark` if missing).
- Run Python snippets via `uv run --with trailmark python -` (a `uv tool` env is not
  importable).
- Always run `engine.preanalysis()` before consuming blast-radius / taint /
  privilege-boundary / subgraph data.

## Repo-specific traps (verified 2026-09-27 — do not report these as findings)

1. **Always parse with `language="c_sharp"`.** The CLI defaults `--language` to
   `python`, so a bare `trailmark analyze`/`trailmark diff` silently returns an empty
   graph/diff on this repo.
2. **Cross-class calls become `proxy` nodes.** 485 of 1083 nodes are
   `proxy.unresolved:<Type>.<Member>`; callers attach to the proxy, not the real
   method. `callers_of("KeyInjection.Press")` returns **0** even though 6 in-repo
   callers exist. A `0`-caller result on a public/static member is **SUSPECT** — query
   the `proxy.unresolved:<Type>.<Member>` id, or cross-check `callees_of` from the
   caller side. **Never report "dead code / no callers" from a bare `callers_of` 0.**
3. **No detected entrypoints.** This is a VSIX, so `entrypoints`,
   `entrypoint_paths_to`, `tainted`, `privilege_boundary`, and `attack_surface` are all
   empty. Do NOT load `trailmark-finding-triage` / `trailmark-review-gate` /
   `graph-evolution` expecting signal here. Use `callers_of`/`callees_of`,
   `paths_between`, `reachable_from`, `complexity_hotspots`, and blast radius.

## Graph export shapes (`to_json()`)

`engine.to_json(indent=2) -> str` returns a JSON **string** — indexing it directly
raises `TypeError: string indices must be integers, not 'str'`, so `json.loads` it
first. After parsing, **`nodes` is an id-keyed dict** (`{node_id: node_dict}`, so
`['nodes'][0]` is a `KeyError`) while **`edges` is a list**. Never enumerate
`to_json()` nodes to answer a caller question — use `callers_of`/`callees_of` on the
proxy id:

```python
engine.callers_of("proxy.unresolved:controller.TryMove")        # -> ['HandleKey']
engine.callers_of("proxy.unresolved:controller.ExitInputMode")  # -> ['ExitToolWindowInputMode']
```
