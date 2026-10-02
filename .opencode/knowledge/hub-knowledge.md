# Hub Knowledge — Knowledge Base

> **Purpose:** Single source of truth for building hub orchestrator agents. Written and updated by the primary agent across research iterations. When a working hub exists, it reads and updates this file itself (self-management).
>
> **Edit rule:** Only this file is edited during the knowledge-gathering phase. Nothing else in the repo.

**Status:** COPIED + TRIMMED FOR MyExtension (2026-09-28) — copied from the guardengine repo's
`.opencode/knowledge/hub-knowledge.md` and trimmed to remove GuardEngine-specific content (the
GuardEngine codebase map §I, the implementation-hub design §J, and the GuardEngine iteration/build
logs). The generic hub-building playbook (§A–H, §K) is retained verbatim.

**Last updated:** 2026-09-28

---

## How to use this document

1. Read the **Distilled knowledge** sections first — they are the actionable playbook.
2. The **Sources** section links every claim to an authoritative URL.
3. To build a hub: use section K (hub-creator design) verbatim, plus the embedded reusable hub
   pattern in the hub-creator agent file.

---

## Distilled knowledge

### A. Orchestrator / hub architecture

Core mental model (from "Building effective agents", Anthropic):
- **Workflow** = LLMs/tools orchestrated through predefined code paths. **Agent** = LLM dynamically directs its own tool usage in a loop.
- For an orchestrator that decomposes unpredictable work, the relevant pattern is **orchestrator-workers**: a central LLM dynamically breaks down tasks, delegates to worker LLMs, and synthesizes results. Best for tasks where you cannot predict subtasks in advance (e.g. coding — number/type of file changes depends on the task).
- **Parallelization** (sectioning = independent subtasks in parallel; voting = same task run N times for diversity) and **evaluator-optimizer** (generator + critic loop) are adjacent patterns a hub can use for verification.
- Agents work best when they gain **ground truth from the environment** each step (tool results, test exit codes) and have **stopping conditions** (max iterations) + **human checkpoints**.
- Three principles: (1) simplicity, (2) transparency — show the planning steps, (3) carefully crafted agent-computer interface (ACI) — tool descriptions/docs get as much effort as prompts.
- Multi-agent systems are expensive: ~4× tokens vs chat for single agents, ~15× for multi-agent. Use them only when the task value justifies it. Most coding tasks are NOT highly parallelizable — a hub should parallelize genuinely independent subtasks only.

From "How we built our multi-agent research system" (Anthropic):
- Subagents act as **compression filters**: they explore with their own context windows and return condensed findings, keeping the lead's context small.
- Lead agent benefits: spawn 3–5 subagents in parallel, each with 3+ parallel tool calls → up to 90% time reduction on complex queries.
- **Scale effort to query complexity** — encode explicit budgets in prompts (simple → 1 agent / 3–10 tool calls; comparison → 2–4 subagents / 10–15 calls each; complex → >10 subagents with clearly divided responsibilities).
- Multi-agent excels at: heavy parallelization, info exceeding a single context window, interfacing with many complex tools. NOT good for: domains requiring all agents to share the same context, or many inter-agent dependencies.

#### Iteration 1 — loop control & hub architecture (deep dive)

Loop control vocabulary (LangGraph / OpenAI Agents SDK / AutoGen / Anthropic Managed Agents):
- **Hard iteration cap**: LangGraph `recursion_limit` (default ~25 super-steps); OpenAI `max_turns` (raises `MaxTurnsExceeded`; `None` disables — dangerous). Treat "many iterations without a state change" as a cycle → break it.
- **Termination conditions as composable rules** (AutoGen): `MaxMessageTermination`, `TextMentionTermination("TERMINATE"/"APPROVE")`, `TokenUsageTermination`, `TimeoutTermination` (wall-clock deadlock escape), `HandoffTermination`, `SourceMatchTermination`, `ExternalTermination`, `FunctionCallTermination`, `FunctionalTermination`. Compose with AND/OR. Canonical: `text_mention("TERMINATE") | max_messages(25)`.
- **Tool-loop prevention**: reset `tool_choice` to auto after a tool call; `tool_use_behavior="stop_on_first_tool"`; `StopAtTools`; unknown tool → return error to model (recoverable), not crash.
- **Every wait must time out**; hub needs `create_subagents`, `get_status` (active/idling/done/crashed), `kill_subagents` primitives; "done" = explicit terminal artifact, not silence.
- Human steer as an in-band event (interrupt/redirect mid-run), not a process kill.
- Anthropic lesson: prompt-level loop controls can become dead weight as models improve — keep them external/configurable, not baked into the coordinator personality.

Failure-mode → prompt-fix table (Research system):
- Subagent-count explosion → tiered count guidelines (1 / 2–3 / 3–5 / 5–10), hard max 20; "prefer fewer, more capable subagents."
- Duplicate/overlapping work → 1 core objective per subagent + crisp boundaries + explicit division of labor.
- Endless research → diminishing-returns stop clause + per-subagent tool-call budget (≤5 / 5 / ~10 / up to 15; absolute cap 20 calls ~100 sources; "if you exceed this limit the subagent will be terminated").
- Context pollution → subagents in separate context windows; coordinate via compact messages, condensed reports.
- Orchestrator doing workers' job → "coordinate, guide, synthesize — NOT conduct primary research"; but also avoid over-delegating trivial tasks.
- Orchestrator not synthesizing → "NEVER create a subagent to generate the final report — YOU write it."
- Feedback loop / deadlock → caps + timeouts + kill/get_status + sentinel token.
- Emergent surprises → prompts as "frameworks for collaboration", not rigid scripts; small-sample eval early; LLM-as-judge with rubric; human testing.
- State corruption on deploy → rainbow deploys; resumable checkpoints; end-state (not turn-by-turn) evaluation.

### B. Delegation (task breakdown + task contracts)

From "How we built our multi-agent research system":
- **Teach the orchestrator how to delegate.** Each subagent needs: an objective, an output format, guidance on tools/sources, and clear task boundaries. Vague short instructions ("research the semiconductor shortage") cause duplicate work, gaps, or misinterpretation.
- Avoid overlapping assignments — explicitly divide labor so two subagents don't research the same thing.
- Good subagent handoff ≈ writing a great docstring for a junior engineer: example usage, edge cases, input format requirements, clear boundaries from other tools.
- Let subagents return a **condensed digest** (≈1,000–2,000 tokens) rather than raw exploration, to protect the hub's context.

From Claude Code best practices:
- Give each task a **verification check** (tests, build exit code, diff-vs-fixture). "Looks done" is not enough; close the loop so the subagent can iterate until the check passes.
- Point subagents to **existing patterns** in the codebase (e.g. "follow how X is implemented in file Y") — prevents them from inventing divergent conventions.
- Describe the symptom + likely location + what "fixed" looks like, rather than "fix the bug".
- Use a **Writer/Reviewer split**: one subagent implements, a fresh-context subagent reviews the diff against the plan (fresh context avoids self-bias). Tell the reviewer to flag only correctness/requirement gaps, not style.

#### Iteration 1 — delegation contracts (verbatim-worthy)

Subagent count by complexity (Research lead prompt): Simple=1 (always ≥1); Standard=2–3; Medium=3–5; High=5–10 (max 20). "Prefer fewer, more capable subagents… Only add subagents when they provide distinct value." Run 3 subagents in parallel at start for non-straightforward queries.

Every delegated brief includes (as appropriate): specific objective (ideally **1 core objective**), expected output format, relevant background context, key questions to answer, suggested starting points/sources + what counts as reliable, specific tools to use, and precise scope boundaries to prevent drift.

Model delegation brief (Anthropic's semiconductor example — gold standard): name the objective; list exact starting sources (named portals, SEC EDGAR, Gartner/IDC); prioritize ("prioritize original sources over news aggregators"); name the specific questions to answer; give the output contract ("compile your findings into a dense report… with specific timelines and quantitative data where available").

Anti-pattern: vague briefs ("research the semiconductor shortage") cause duplicate work and gaps. Also: "Avoid deploying subagents for trivial tasks that you can complete yourself."

### C. Progress tracking & single source of truth

From "How we built our multi-agent research system" + "Building effective agents":
- **Subagent output to a filesystem** to minimize the "game of telephone": subagents persist their work (code, reports, artifacts) to files, then pass lightweight references (paths) back to the coordinator. Avoids info loss across stages and cuts token overhead from copying big outputs through conversation history.
- **Persist the plan to memory/disk** — if the context window gets truncated (compaction), the plan must survive. The lead agent saves its plan to external storage and re-reads it.
- **One source of truth** = exactly one writer of shared state (the hub). Subagents write ONLY to their own artifact dir; the hub reconciles artifacts into the shared state. This is how to avoid conflicting/duplicated state between agents.
- **Resumability**: agents are stateful and errors compound — do not restart from scratch; support resume from the last checkpoint. Use durable state + retry logic + regular checkpoints.
- **Evaluation**: for agents that mutate state over many turns, evaluate the **end state** (did it achieve the goal) not the turn-by-turn process; break long work into checkpoints where specific state changes should have occurred.

#### Iteration 1 — tracking & single source of truth (deep dive)

- **Hub pattern (task registry)**: shared state = inbox queues + wake events + `status` dict (active/idling/done/crashed) + auto-incrementing task IDs. Lead reads `get_status`; workers never poll — coordination is appended to the worker's last tool result ("[Messages received while you were working:] …"). Reconciliation = append-to-tool-result.
- **Magentic-One two-ledger pattern**: **Task Ledger** (the plan + assigned subtasks + known facts) and **Progress Ledger** (per-step self-reflection on whether the task is complete). The orchestrator writes BOTH; after each worker returns it updates the Progress Ledger; if no progress for enough steps → update Task Ledger + re-plan (stagnation detector = loop-break rule).
- **CrewAI persistence**: every flow/state gets a UUID; `@persist` (SQLite) gives **resume** (same id, history extends) and **fork** (hydrate snapshot into fresh id). Precedent for resumability.
- **OpenAI**: pick ONE memory writer per conversation (don't mix client-managed + managed state → duplicated context). `RunConfig(trace_id, group_id)` links all traces of one multi-agent run (observability).
- **Anthropic session = append-only event log** separate from context management; interrogate by reading slices (re-enter at last-read point), not replay.
- **Exactly-writer rule**: hub is the ONLY writer of the two ledgers and the ONLY author of the final deliverable. Subagents write only to their own artifact dirs and return lightweight references (paths). A single downstream post-processor (e.g. citations agent) is fine — but not per-worker writers of shared state.
- **Parallel-edit safety**: worktree isolation per subagent (`isolation: worktree`); 4 enforcement checks — block file edits to the main checkout, block commands whose cwd resolves to the main checkout, block git redirects (`git -C`, `--git-dir`, `GIT_DIR`/`GIT_WORK_TREE`), block runtime-computed git targets. Lock worktrees while agents run; never auto-delete a worktree with uncommitted work; explicit user-controlled merge.
- Task-file convention: a subagent receives only its own system prompt + delegation prompt; a uniform footer can be injected into every subagent (`--append-subagent-system-prompt`).

#### Iteration 8 — session safety & concurrency (multi-session hubs)

**Problem (user-reported, 2026-09-22):** every hub owned a FIXED workspace
(`.opencode/workspaces/<hub>/state.md` etc.) and was the ONLY writer. Two opencode sessions
invoking the same hub on different objectives wrote the SAME files → they fought: overwrote each
other's state, collided on task IDs (both start at T001), and re-orientation read the other
session's state. All hubs also self-managed the shared `.opencode/knowledge/hub-knowledge.md` →
concurrent hubs fought over it.

**Rule — ONE SESSION = ONE WORKSPACE. Never share a writable file across sessions.**

1. **Session-scoped workspaces.** Every hub session works in its own sub-workspace:
   `.opencode/workspaces/<hub>/sessions/<session-id>/` holding `session.md` (manifest: id, started,
   objective, status), `state.md`, `tasks.md`, `log.md`, `artifacts/`, and any per-objective outputs
   (`plans/`, `report.md`). The hub's runtime writes go ONLY into its own session dir. New objective
   = new session dir; old sessions are archived (never deleted) and resumable by name.
2. **Session ID.** `<hubname>-<YYYYMMDD>-<HHMMSS>` (e.g. `implementation-hub-20260922-143000`).
   Recorded in the session manifest + the state.md status line.
3. **Shared knowledge is read-merge-write, never overwrite.** The workspace root keeps only
   build-time/shared files: `skills.md` (hub-creator writes once), `knowledge/` (debugging-hub
   bootstrap), `locations.md` (debugging-hub location index), and the global
   `.opencode/knowledge/hub-knowledge.md`. These ACCUMULATE so the hub improves over time. Before
   writing any shared file, re-read it; if it changed since you last read it (another session
   updated it), MERGE your additions into the current content instead of overwriting. Keep
   shared-file updates small and at natural checkpoints (bootstrap, verified-location discovery,
   session end).
4. **Knowledge-base concurrency.** `.opencode/knowledge/hub-knowledge.md` is shared by ALL
   hubs/sessions. Same read-merge-write rule. If a merge conflict is detected, ask the user rather
   than clobber.
5. **Cross-session source-edit safety.** Two sessions of the same hub must not run SOURCE-WRITER
   work on the same checkout concurrently. If they must, use worktrees. Read-only/analysis sessions
   may run fully in parallel.
6. **Permission scoping still holds.** The hub's `edit` allow `.opencode/workspaces/<hub>/**` covers
   `sessions/<session-id>/**` — no permission change needed. The knowledge-file allow stays for
   self-management.

### D. Loop & runaway prevention

- Agents need explicit **stopping conditions**: maximum number of iterations, task-complete criteria, and permission to return control to the human when blocked.
- Anti-runaway guardrails from the Research system: cap subagent count for the task complexity; stop when sufficient results are gathered; prevent endlessly re-searching; "do not keep researching after you have enough to answer."
- Encode **effort budgets** (see B) to prevent overinvestment in simple tasks.
- Track tool-call counts; if a subagent exceeds its budget, it must summarize and return.

#### Iteration 1 — layered stop rules ("a stack, not a style")

Layered so any single rule can fire:
1. **Turn/iteration cap** — per subagent (`max_turns` / `recursion_limit`).
2. **Tool-call budget** — tiered by difficulty (≤5 / 5 / ~10 / up to 15; absolute 20 + ~100 sources; "if exceeded, subagent is terminated").
3. **Wall-clock timeout** — `TimeoutTermination`; every wait gets a timeout.
4. **Sentinel token** — "TERMINATE"/"APPROVE"; coordinators end with TERMINATE.
5. **Diminishing-returns clause** — "when further research has diminishing returns… STOP FURTHER RESEARCH and do not create any new subagents. Just write your final report."
6. **No repeated queries** — "NEVER repeatedly use the exact same queries for the same tools."
7. **Stagnation detector** — if no progress for N steps → re-plan (Task Ledger update).
8. **Human checkpoint** — pause-and-ask at defined gates; in-flight interrupt/redirect.
- (Iteration 1 will add: loop/deadlock detection, iteration caps, "when to stop" phrasing, and human-checkpoint rules.)

### E. Failure handling / evaluation

- Resume-from-checkpoint over restart; retry logic; durable execution.
- Human evaluation catches what evals miss (hallucinated answers, source bias). LLM-as-judge scales for free-form outputs; single judge call scoring 0.0–1.0 + pass/fail was most consistent.
- Start eval immediately with small samples (~20 cases); prompt tweaks early have large effects.
- Deployment: rainbow deploys for long-running agent systems; full tracing/observability of agent decisions.

#### Iteration 1 — failure/resume specifics

- Resume-from-checkpoint over restart (agents are stateful; errors compound). CrewAI resume/fork; Anthropic wake(sessionId)→resume from last event.
- Crash recovery: harness loop is "cattle"; durable append-only event log; re-enter by reading a slice.
- Graceful terminal states instead of hangs: on max_turns return "[name hit max_turns]"; on timeout return "woke: 60s timeout"; unknown recipient → "unknown: [...]".
- Let the agent know when a tool is failing and let it adapt + deterministic retry + regular checkpoints.
- Rainbow deployments when updating prompts/tools (don't force-update in-flight agents).
- Evaluation: end-state eval over turn-by-turn; break into checkpoints with expected state changes; LLM-as-judge (single judge, 0.0–1.0 + pass/fail, rubric: accuracy, citation, completeness, source quality, tool efficiency); human eval catches what evals miss; start with ~20 small samples.
- Cost gate: multi-agent ~15× chat tokens — route to the hub only when task value justifies it; simple tasks → single agent.

### F. Context engineering for subagents (avoiding wrong paths)

From "Effective context engineering for AI agents":
- **Context rot / attention budget**: as tokens grow, recall degrades across all models. Find the **smallest set of high-signal tokens** that produces the desired behavior.
- System-prompt **altitude**: avoid brittle over-specified if-else logic AND vague guidance that falsely assumes shared context. Strike the Goldilocks zone: specific enough to guide, flexible enough to leave heuristics.
- Use distinct prompt sections (`<background_information>`, `<instructions>`, `## Tool guidance`, `## Output description`) via XML tags / Markdown headers.
- Few-shot: curate a few **canonical examples**, not a laundry list of edge cases.
- **Just-in-time context**: give subagents lightweight identifiers (file paths, queries, links) and let them load detail on demand with tools (grep/glob/read) instead of pre-loading everything. Mirrors how Claude Code works (CLAUDE.md/AGENTS.md upfront + grep/glob just-in-time).
- **Progressive disclosure**: agents discover context incrementally; file names, sizes, timestamps are signals; keep only what's needed in working memory + note-taking for persistence.
- Hybrid strategy: small fixed upfront context + on-demand retrieval is usually the right default.
- Tool sets should be **minimal and non-overlapping** — if a human can't say which tool to use in a situation, an agent can't either. Bloated tool sets cause ambiguous decisions and wrong paths.

#### Iteration 2 — subagent prompt hygiene (avoiding wrong paths)

Core principle: **a fresh subagent is a clean window.** Every design decision keeps that window small at spawn AND as it works.

Delegation contract — four mandatory fields (objective, output format, tools/sources guidance, boundaries). Missing any one = documented drift (duplication, gaps, misinterpretation). A drop-in template:

```text
TASK — <one sentence deliverable>
OBJECTIVE — success as a checkable outcome (not an activity)
SCOPE — the ONLY things you may touch (exact paths/files/dirs). If you need
        something outside this list, STOP and report; do not go look on your own.
OUT OF SCOPE / DO NOT
  - Do not fix/refactor/comment on anything outside the listed paths.
  - Do not "improve" adjacent code you read — report observations, don't act.
  - Do not chase rabbit holes; if N tool calls pass without finding X, report what you have.
VERIFY — run <specific check/test/command>; paste the output. "Looks right" is not a pass.
REPORT BACK (≤300 words / ≤~1500 tokens): findings with file:line; what you did NOT check
  and why; anything that blocks the objective.
```

Boundary phrasing bank: "This task is complete when X is true. Do not widen the task." / "Note out-of-scope observations separately — do not fix them." / "You are read-only; you will be graded on the report." / "Stop when <objective> is verified — do not keep reading files 'for completeness'." / single "IMPORTANT:" emphasis only (many = none stand out).

Enforce boundaries **structurally, not by prose**: per-agent `permission` (`edit: deny`, `bash` globs) beats prose every time. The hub should enumerate exactly which subagents it may spawn via `permission.task` (allow/deny/ask, last-match-wins); `deny` removes the subagent from the Task tool entirely. `hidden: true` hides internal subagents from `@` autocomplete (only Task-invocable).

Bounded exploration (anti-"infinite exploration"): entry points (2–3 files/dirs to start from), a hard tool-call budget (max 12), explicit "do not read" dirs (e.g. `bin/`, `library/`), start-broad-then-narrow, stop-when defined, deliverable ≤400 words with file:line. "Start with 1–2 broad grep/glob passes, then narrow to the 2–3 highest-signal hits."

Token-efficient reading: prefer `grep`/`glob` to locate, then `read` with `offset`/`limit` on the relevant slice; never read a whole large file for one function; use file metadata (size → complexity, timestamps → recency, naming → purpose) before committing to a read; read signatures before bodies, call sites before definitions.

Clean-window mechanics (Claude Code / opencode subagents): a non-fork subagent sees only its own system prompt + the delegation prompt — NOT the main conversation, skills invoked, or files already read. No hidden shared state leaks. CLAUDE.md DOES reach subagents unless `omitClaudeMd: true` (use it for specialists that take everything from the delegation prompt). Forks inherit everything — use only when background is genuinely needed, never by default. Resume = full history restored (no longer a clean window). Keep agent `description` fields short (large combined descriptions waste context); move detail into the subagent's own system prompt (loads only when it runs). Uniform house-rule footer for every spawned subagent via `--append-subagent-system-prompt` / `-file` (e.g. "report ≤300 words, cite file:line, never touch outside listed paths") — applies to nested subagents too.

`steps` (opencode config) caps agentic iterations per agent; when hit, the agent is forced to respond with a text-only summary of its work + recommended remaining tasks. Use it on every subagent. `temperature` 0.0–0.2 for focused/deterministic analysis.

Digest + artifact handoff (just-in-time for the hub's own window): subagent writes full findings to a file, returns ≤1500-token digest + the reference path; hub reads the artifact on demand.

Specialist system prompts: one job; heuristic "altitude" (specific enough to guide, flexible enough to leave heuristics); canonical examples over edge-case dumps; a runnable verification check; adversarial reviewer sees only diff + criteria (never the author's reasoning), flags correctness gaps only, not style.

When to spawn vs in-context: spawn a subagent when (a) exploration is separable and would pollute the hub window, (b) work parallelizes across ≥2 independent subtasks, (c) a specialist system prompt is needed. Otherwise do it in-context. Multi-agent ~15× tokens — simple tasks should not spawn subagents.

### G. Compaction & memory retention

From "Effective context engineering" + Claude Code best practices:
- **Compaction** = summarizing a near-full context and reinitiating with the summary. Tune the compaction prompt: start **recall-first** (capture every relevant fact), then improve **precision** (drop superfluous content).
- Preserve across compaction: architectural decisions, unresolved bugs, implementation details, the plan, the list of modified files, and test commands.
- **Safest light-touch compaction**: clear raw tool calls/results deep in history — the agent rarely needs the raw output again.
- **Structured note-taking / agentic memory**: regularly write notes to files OUTSIDE the context (NOTES.md, decisions.md, task registry, to-do list). Re-read them after a context reset. This is how coherence survives compaction — this is also why the hub MUST persist state to disk rather than relying on conversation.
- Custom compaction instructions can be embedded in config: e.g. "When compacting, always preserve the full list of modified files and any test commands."
- `CLAUDE.md`/`AGENTS.md` guidance: keep short; only include what would cause mistakes if removed; prune like code; use "IMPORTANT" emphasis on a single line only; check in to git.
- **Sub-agent architectures** as a context technique: subagents with clean windows return 1–2k token summaries; choose compaction (conversational flow) vs note-taking (iterative milestones) vs subagents (parallel exploration) by task type.
- Reset context (`/clear`) between unrelated tasks; after two failed corrections, restart fresh with a better prompt.

#### Iteration 2 — opencode compaction internals (mechanics that guarantee survival)

opencode config: `compaction: { auto, prune, tail_turns, preserve_recent_tokens, reserved }`. The documented three: `{ auto: true, prune: false, reserved: 10000 }`. Real power is in the **compaction agent** (hidden built-in primary agent named `compaction` — overridable `agent.compaction.model/prompt`) and the **`experimental.session.compacting` plugin hook**.

Mechanics (from opencode source):
- Head (summarized) + tail (kept verbatim). Tail budget default ≈ 25% of usable window, clamped 2k–15k tokens; `tail_turns` caps how many recent user turns are kept verbatim (`tail_turns: 0` ⇒ summarize everything). `preserve_recent_tokens` sets the verbatim budget directly.
- Serialized history shape fed to the summarizer: `[User]`, `[Assistant]`, `[Assistant reasoning]`, `[Assistant tool call]`, `[Tool result]` (truncated at 2000 chars → `"\n[truncated]"`, or `"[Old tool result content cleared]"` when pruned), `[Tool error]`.
- **`prune: true`** = the cheap light-touch first line of defense (tool-result clearing): protects 40k most-recent tokens of tool output + 2 most-recent turns, never crosses a prior summary, skips `skill` outputs, only acts if it frees >20k tokens. Prefer prune over summarize where possible.
- Prior-summary chaining: the most recent prior summary is passed as `<prior-summary>` so the summarizer **merges** (carry forward objectives/constraints/decisions/parallel workstreams; conversation wins on conflict; drop only finished work) rather than rewriting. Anything not carried into the new summary is **lost**.
- The summarizer's output template (`SUMMARY_OUTPUT_TOKENS = 4096`): `## Objective` → `## Important Details` → `## Work State` (Completed / Active / Blocked) → `## Next Move` (numbered) → `## Relevant Files` (path: why it matters). Rules: keep every section, terse bullets, preserve exact paths/symbols/commands/error strings/URLs/identifiers, don't mention compaction.
- Auto-continue: after auto-compaction opencode synthesizes a follow-up user message ("Continue if you have next steps, or stop and ask for clarification…"). Gate via `experimental.compaction.autocontinue`.

**The guaranteed-preservation lever for a hub**: the `experimental.session.compacting` hook fires before the summarizer runs. Returning `output.context` strings appends them verbatim to the compaction prompt (e.g. "always preserve: current task status, decisions, files being modified, blockers"); replacing `output.prompt` replaces the whole prompt. `experimental.chat.messages.transform` can mutate the serialized head before summarization. This is how the hub forces its state into every compaction regardless of what the summarizer would infer. (Requires a plugin file in `.opencode/plugin/` — a build-phase deliverable, not part of the knowledge .md.)

Claude Code compaction (for comparison): two-stage — clear older tool outputs, then summarize. What survives: system prompt, project CLAUDE.md (re-injected from disk), auto memory, plan-mode plan, top-5 most-recently-modified files re-read (files >5000 tokens come back as path references), invoked skill bodies (≤5000 tokens/skill, ≤25000 total, keep start — put critical instructions near the top), running background processes. Path-scoped rules and nested CLAUDE.md do NOT survive. Steer via a "Compact Instructions" section in CLAUDE.md ("when compacting, always preserve: current task + status, architectural decisions, unresolved bugs + file paths, modified-file list + test commands, next step") and `/compact <focus>`.

#### Iteration 2 — resilient on-disk knowledge file (anatomy)

The single source of truth must live in FILES, not conversation — conversation is ephemeral/lossy under compaction; files are not. Model the file on opencode's own `SUMMARY_TEMPLATE` so opencode-generated summaries and the hub file stay congruent, plus a status line + statused decision log:

```markdown
# HUB STATE — <task/project name>
Status: ACTIVE · Updated: <ISO timestamp> · Session/compaction count: N
Objective: <one-two sentences: what the user is trying to accomplish>

## Decisions (append-only; newest on top) — each: what/why/status
- [date] DECIDED: <decision> — reason: <why> — status: ACTIVE | RESOLVED | SUPERSEDED | REJECTED | OPEN

## Unresolved bugs / open questions
- [BUG] <evidence; owner; blocking?>

## Implementation details   (exact symbols, paths, error strings — verbatim survives)
## Plan ([x] done with verification note / [ ] todo)
## Modified files (path: why it matters)
## Test commands (exact command + expected outcome)
## Next move (1. immediate action  2. follow-up)
```

Design rules: status line (open + updated timestamp + compaction counter) so a fresh agent knows currency and how many resets it survived; decision log with statuses (encodes "preserve architectural decisions" + lets the agent detect flips); append raw events to a tail log (cheap, lossless) + periodically rebase the curated top into dense bullets; preserve exact identifiers as code spans; re-orientation ritual at session start — read the state file, verify status line, reconcile conversation vs file (conversation wins on conflict), rewrite the file with new decisions/blockers.

Memory patterns to borrow:
- Anthropic Memory-tool protocol (system prompt): "ALWAYS VIEW YOUR MEMORY DIRECTORY BEFORE DOING ANYTHING ELSE"; "ASSUME INTERRUPTION — your context window might be reset at any moment, so you risk losing any progress not recorded in your memory directory." Path-traversal protection: resolve + verify under a `/memories` root; cap file sizes; expire stale files; page long files.
- Multi-session SW pattern: initializer session sets up memory files (progress log + feature checklist) BEFORE work; subsequent sessions open by reading them; update progress log at end of session; "mark a feature complete only after end-to-end verification confirms it works."
- Claude Code memory: CLAUDE.md hierarchy (<200 lines/file; >200 reduces adherence; ≤4 MiB loads); auto-memory `MEMORY.md` index (first 200 lines / 25KB loaded, one line per entry, detail in topic files loaded on demand).
- Cline Memory Bank: 6 files — `projectbrief.md` (scope source of truth), `productContext.md`, `activeContext.md` (updates most frequently), `systemPatterns.md`, `techContext.md`, `progress.md`; "I MUST read ALL memory bank files at the start of EVERY task"; conditional activation (only when touching memory-bank/).
- opencode: `instructions:` config accepts file paths/globs (like CLAUDE.md imports) — the hub's state file can be injected every session. No built-in auto memory in opencode — the hub must implement the memory protocol itself (system prompt + files + `experimental.session.compacting` hook).

Token-budget discipline: CLAUDE.md <200 lines; memory index ≤200 lines/25KB; summarizer output ≤4096 tokens; verbatim tail 2k–15k; tool-result text truncated at 2000 chars; skills re-injected ≤5000 tokens/skill. Truncate when you can afford to lose the tail (put important content first — both opencode and Claude Code rely on the start surviving); summarize when high-value but must shrink. In a knowledge file do both: curated top always under budget + append-only tail that can be dropped. Split into multiple files with an index when >200 lines/~25KB, when sections have different access frequencies (always-load vs on-demand), or for path-scoped conditional loading. Prune like code: drop completed items, resolve decisions, terse bullets.

### H. Skills (verified sources)

Initial scan of `anthropics/skills` (177k stars, 21k forks, Apache 2.0; doc skills source-available):
- Skills = folder with `SKILL.md` (frontmatter: `name`, `description` + body instructions/scripts). Loaded on demand → do not bloat every conversation.
- `document-skills` plugin (docx/pdf/pptx/xlsx) — relevant for any PDF export (repo already uses an `md-to-pdf` skill).
- Spec lives at `agentskills.io`; template at `template/`; plugin marketplace install via Claude Code.

#### Iteration 3 — verified skill sources (with reputation + license)

| Source | What it offers | Reputation / license | Install for opencode |
|---|---|---|---|
| **Microsoft `dotnet/skills`** (github.com/dotnet/skills) | Official .NET team skills: 15 plugins — `dotnet`, `dotnet-advanced`, `dotnet-diag` (perf/debugging), `dotnet-msbuild`, `dotnet-nuget`, `dotnet-test`, `dotnet-data` (EF), `dotnet-aspnetcore`, `dotnet11`, … | 5.4k★, pushed 2026-09-20, **MIT** — most relevant to this repo | `skill-installer install https://github.com/dotnet/skills/tree/main/plugins/<plugin>/skills/<skill>` or copy `plugins/*/skills/*` into `~/.claude/skills/` / `~/.agents/skills/`; or `dotnet skills install` (managedcode CLI) |
| **Trail of Bits skills** (github.com/trailofbits/skills) | `c-review`, `modern-cpp`, `static-analysis` (CodeQL/Semgrep/SARIF), `testing-handbook-skills` (ASAN/sanitizers/fuzzers), `differential-review`, `second-opinion`, `sharp-edges`, `audit-context-building`, `post-patch-validation`, `gh-cli`, `github-triage` | 7.2k★, elite security firm, active, **CC-BY-SA-4.0** — C/C++ side | clone repo → `"skills": { "paths": ["<parent of skill dirs>"] }` or copy skill folders into `~/.config/opencode/skills/` |
| **obra/superpowers** (github.com/obra/superpowers) | Dev-methodology skills: `systematic-debugging`, `verification-before-completion`, `test-driven-development`, `requesting-code-review`/`receiving-code-review`, `brainstorming`, `writing-plans`, `executing-plans`, `subagent-driven-development`, `using-git-worktrees`, `finishing-a-development-branch` | 289k★, Jesse Vincent, **MIT**, opencode-native install | `"plugins": ["superpowers@git+https://github.com/obra/superpowers.git"]` (V2; Windows fallback: `npm install superpowers@git+… --prefix "$HOME\.config\opencode"` + point `plugins` at the local path) |
| **Aaronontheweb/dotnet-skills** | 30 .NET skills + agents: C# standards, concurrency, EF Core, `dotnet-performance-analyst`, `dotnet-benchmark-designer` | 1.2k★, **MIT**, README has explicit opencode install | `git clone … && cp -r skills/*/*/SKILL.md ~/.config/opencode/skills/<name>/SKILL.md` |
| **managedcode/dotnet-skills** | Catalog + `dotnet skills` CLI (auto-recommend from csproj; installs to `~/.agents/skills/` etc.) | 483★, **MIT**, pushed 2026-09-17 | `dotnet tool install --global dotnet-skills`; `dotnet skills install --agent agents <skill>` |
| **anthropics/skills** doc skills (docx/pdf/pptx/xlsx) | Word/PDF/PowerPoint/Excel create+edit | 177k★ BUT **source-available / proprietary — LICENSE forbids copying into the repo**; use via Claude Code plugin marketplace only, or read as patterns | `/plugin marketplace add anthropics/skills` + `/plugin install document-skills@anthropic-agent-skills` (Claude Code; NOT auto-installed into opencode) |
| anthropics/skills `mcp-builder`, `skill-creator` | Build MCP servers; author new skills | Apache-2.0 | copy folders into `.opencode/skills/` or `~/.config/opencode/skills/` |
| Claude Code LSP plugins `csharp-lsp`, `clangd-lsp` | Real-time code intelligence | Anthropic-official | NOT Agent Skills — opencode has the native equivalent: enable `"lsp": true` in opencode.json + configure clangd / C# server binaries |

Install notes / caveats:
- opencode discovers skills in `.opencode/skills/<name>/SKILL.md`, `.claude/skills/`, `.agents/skills/` (project) and `~/.config/opencode/skills/`, `~/.claude/skills/`, `~/.agents/skills/` (global). `SKILL.md` all-caps; `name` must match folder, lowercase+hyphens. Per-skill control via `permission.skill`. (`skills.paths`/`skills.urls` appear in the opencode config schema and this repo's built-in customize-opencode skill, but current docs may not list them — treat discovery-directory install as the reliable path.)
- opencode does NOT consume Claude Code plugin marketplaces directly — copy `skills/*` folders or use `skills.paths` pointing at a clone.
- Validate skills with `skills-ref validate <folder>` (agentskills.io tooling).
- **anthropics document skills cannot be legally vendored** into the repo (LICENSE.txt forbids reproduction/derivative works); the Anthropic `pdf` skill has NO markdown→PDF path. **Keep the existing custom `md-to-pdf` skill** for the repo's Markdown+mermaid→PDF doc pipeline; anthropic pdf/docx only as an opt-in, non-vendored complement (e.g. Word/Excel deliverables).

Do NOT adopt: anthropics creative/web skills (canvas-design, webapp-testing, brand-guidelines, …), .NET web-stack skill packs (EF/MediatR/Dapper/JWT — not a CRUD app), unlicensed dotnet skill repos (ronnythedev, nesbo — no LICENSE), .NET Framework orchestrator packs.

### K. hub-creator agent — design & full prompt

(VERBATIM blueprint, iteration 4 → BUILT in Build 1 as `.opencode/agents/hub-builder.md`, RENAMED to `.opencode/agents/hub-creator.md` in Build 4. The shipped version adds: mode PRIMARY, skill research/curate/verify phases, the `task` whitelist for its 3 helper subagents, `steps: 200`, and a `customize-opencode` skill reference; web research access was REMOVED (delegated to skill-researcher).)

`````markdown
---
name: hub-creator
description: Designs and writes hub orchestrator agents for any project from the accumulated hub knowledge — reads the target repo's existing agents/skills/docs index and permission config, interviews the user, then produces a tailored hub agent definition plus its workspace state files (state.md / tasks.md / log.md / artifacts/) in the user-named workspace. Research + definition only; never runs the resulting hub.
temperature: 0.1
mode: subagent
permission:
  bash: deny
  webfetch: deny
  websearch: deny
  task: ask
  edit: ask
---

# Hub-Creator — build a hub orchestrator agent for a target project

## Role

Your ONLY job: extract the reusable hub pattern and instantiate it for a target project. You design and write ONE hub orchestrator agent definition plus the workspace state files that hub will manage, all derived from the accumulated hub knowledge. You are the *creator*, never the hub — you never execute the hub's orchestration loop or run its workers. When you are done, the primary agent (or the user) brings the new hub to life.

## Non-negotiables (guardrails)

- **Never wildcard-search or recurse `library/`.** It is enormous with uninitialized submodules; a pattern search hangs the session. Reading a single known path inside it is fine. Apply this to yourself AND to every subagent you spawn, and bake it into every hub you write.
- **Write ONLY inside the target workspace and hub-agent file destination the user names.** Your frontmatter uses `edit: ask` — every write prompts the user, and that approval IS the scope gate. Only accept writes that land inside the user-approved workspace; confirm the exact target path before each write; never write outside it.
- Never modify source code, scenes, configs, or anything outside the named workspace.
- You do not run the hub you build. If asked to execute hub work, point the caller back to the primary/hub flow instead.

## Phase 0 — Read the knowledge source

1. Read `.opencode/knowledge/hub-knowledge.md` (the single source of truth) **in full** if present. It contains the complete research — sections A–K.
2. The **Reusable hub pattern (embedded)** section of this prompt is a distilled fallback that works standalone when the file is absent. When both exist, use the file's full version; reconcile any drift — the file wins on conflict.
3. Section map: A orchestration/architecture · B delegation contracts · C tracking & single source of truth · D loop/stop rules · E failure/evaluation · F context engineering · G compaction & memory · H skills · I GuardEngine codebase map · J implementation_hub design · K hub-creator design.

## Phase 1 — Read the target repo's context

Inventory what the new hub must route to and respect:

1. **Existing agents** — list `.opencode/agents/*.md` and `~/.config/opencode/agents/*.md`. For each, read the frontmatter (`name`, `mode`, `tools`, `permission`) and the first paragraph of the body to learn its role, whether it is read-only or a writer, and what it can do. NOTE: the repo's top-level `external_directory` is `"*": deny`, so reading `~/.config/opencode/agents/*.md` may be blocked — if so, ask the user to paste the global agent list (name + one line each) instead of reading it.
2. **Existing skills** — list `.opencode/skills/` and `~/.config/opencode/skills/` (name + description; read `SKILL.md` only when you need trigger conditions for routing).
3. **Docs index** — read the project's docs index (`docs/agents.md`, `README.md`, or equivalent) for conventions and terminology.
4. **Permission config** — read `opencode.jsonc` (or `~/.config/opencode/opencode.json`) and note the edit/bash/task rules the new hub must respect (deny-listed paths, ask-gated commands).
5. **Build the routing inventory**: work-type → agent/skill, plus an explicit **delegation gaps** list (recurring work with no dedicated agent) — the hub either does those in-context or flags them to the user.

Guardrail at all times: never wildcard-search `library/`.

## Phase 2 — Interview the user (built-in `question` tool)

Before designing anything, ask the user (use the `question` tool; if unavailable, ask in text and wait). Minimum questions:

1. **Purpose/scope** — What recurring work is the hub for? What should it coordinate, and what does success look like for this hub?
2. **Subagents to route to** — Which existing agents/skills (by exact name) should it delegate to? Confirm your Phase 1 inventory against theirs.
3. **Workspace name** — Exact folder for the hub's workspace (suggest slugified lowercase-hyphen, e.g. `.opencode/workspaces/implementation-hub/`, default `.opencode/workspaces/<hubname>/`).
4. **Mode** — subagent (default; invoked by the project primary when hub work is needed) vs primary (standalone). Recommend subagent.
5. **Placement** — single project-local hub (`.opencode/agents/<name>.md`) vs a global hub (`~/.config/opencode/agents/<name>.md`).
6. **Anything else** — existing patterns to respect, extra constraints, preferred verbosity.

Record the answers in `log.md` (Phase 4). If any answer is missing, do NOT guess — ask again.

## Phase 3 — Design the tailored hub agent definition

Reuse the reusable patterns below; customize ONLY the routing and repo-specific details. Every produced hub agent must include, at minimum:

1. **Frontmatter** — `name` (lowercase-hyphen, matches filename), short `description`, `temperature` 0.1–0.2, `mode` per interview, `tools` (read/write/glob/grep/task minimum; `bash` only if the hub runs builds/tests), and a `permission` block scoped so the hub writes ONLY to its own workspace and its workers write only to `artifacts/`.
2. **Role statement** — the hub is an orchestrator: it *coordinates, guides, synthesizes*; it does NOT do workers' work, and it NEVER delegates the final deliverable.
3. **Delegation contract** — embed the four mandatory fields (TASK/OBJECTIVE/SCOPE/VERIFY/REPORT BACK) with the exact template and the boundary-phrasing bank (embedded pattern §1).
4. **Two-ledger tracking** — Task Ledger (tasks.md) + Progress Ledger (log.md); the hub is the ONLY writer of both.
5. **Single-source-of-truth layout** — hub-only-writer rule; workers write only to `hub/artifacts/<task-id>/`; the hub reconciles into `state.md`. Define two worker classes explicitly: SOURCE-WRITERS (also write scoped source in the checkout; serialize them, never two in one wave) vs READ-ONLY (digest is the deliverable).
6. **Layered stop rules** — all 8 layers (embedded pattern §4).
7. **Anti-patterns** — the explicit do-not list (embedded pattern §5).
8. **Context hygiene** — clean-window spawning, ≤1500-token digests, just-in-time reads, bounded exploration (embedded pattern §6).
9. **Compaction resilience** — state persisted to disk, status line + decision log, re-orientation ritual at session start, compaction-hook note (embedded pattern §7).
10. **Adversarial verification** — fresh-context reviewer subagent, diff + criteria only, correctness-only feedback (embedded pattern §8).
11. **Routing table** — work-type → target agent/skill, built from your Phase 1 inventory, with a "gaps / do-in-context" section.

## Phase 4 — Design the workspace file schemas

Produce the files the hub will manage, under the confirmed workspace folder:

- `state.md` — HUB STATE: status line (`Status · Updated · Session/compaction count`), Objective, Decisions (append-only, newest on top, statused), Unresolved bugs/open questions, Implementation details (verbatim symbols/paths/error strings), Plan, Modified files, Test commands, Next move. Model it on the opencode summary template so compaction summaries stay congruent.
- `tasks.md` — Task Ledger: one row per task (id, objective, assignee, status `active/idling/done/crashed`, artifact reference path, verification check + result).
- `log.md` — append-only raw event log (cheap, lossless) + periodic rebase into dense bullets; the hub's Progress Ledger.
- `artifacts/` — one subdirectory per task (`hub/artifacts/<task-id>/`); SOURCE-WRITERS write here + their scoped source; READ-ONLY workers write nothing; the hub reconciles into `state.md` and passes lightweight references (paths) around.
- `skills.md` — the curated skill set for the hub and its subagents: chosen skills (name, source, license, install path, owning agent) + drop reasons (unverifiable / proprietary / noise / contradiction).
- `README.md` (optional) — workspace index; include a mermaid mindmap only if the target uses the waypoint/mindmap convention.

## Phase 5 — Verify against the knowledge base (self-check)

Before writing anything to disk, run the **Hub creation checklist** (below) against your design; every item must pass. Then, if the design is non-trivial, spawn exactly ONE fresh-context reviewer subagent to audit the written files against the checklist — give it ONLY the diff + criteria, never your reasoning (fresh context avoids self-bias); it flags correctness/completeness gaps, not style. Fix what it finds. NOTE: with opencode's default `subagent_depth` of 1, a subagent cannot spawn a subagent — so ask the PRIMARY agent (or the user) to spawn the reviewer for you, or treat the checklist self-check as sufficient for the first cut.

## Phase 6 — Write files and hand back

Write the hub agent `.md` plus the workspace files into the confirmed target workspace only. Then report:

- **What you wrote** — exact paths.
- **Changed vs the template and why** — a short table: template element → tailoring → reason (routing names, permission scoping, mode, added/removed sections).
- **Verification status** — each checklist item pass/fail, plus the reviewer result if one ran.
- **How to invoke the new hub** — exact next step for the primary/user (e.g. `Task > <hubname> with: <objective>` or a suggested entry delegation brief).
- **Restart** — opencode loads config once at startup; tell the user to quit and restart opencode before the new agent is usable.

## Reusable hub pattern (embedded)

Works standalone if `.opencode/knowledge/hub-knowledge.md` is absent; read the file when present for the full version.

### 1. Delegation contract — four mandatory fields
Every worker brief carries all four. Missing any one causes documented drift (duplication, gaps, misinterpretation).

```text
TASK — <one sentence deliverable>
OBJECTIVE — success as a checkable outcome (not an activity)
SCOPE — the ONLY things you may touch (exact paths/files/dirs). If you need
        something outside this list, STOP and report; do not go look on your own.
OUT OF SCOPE / DO NOT
  - Do not fix/refactor/comment on anything outside the listed paths.
  - Do not "improve" adjacent code you read — report observations, don't act.
  - Do not chase rabbit holes; if N tool calls pass without finding X, report what you have.
VERIFY — run <specific check/test/command>; paste the output. "Looks right" is not a pass.
REPORT BACK (≤300 words / ≤~1500 tokens): findings with file:line; what you did NOT check
  and why; anything that blocks the objective.
```

Boundary phrasing bank: "This task is complete when X is true. Do not widen the task." · "Note out-of-scope observations separately — do not fix them." · "You are read-only; you will be graded on the report." · "Stop when <objective> is verified — do not keep reading files 'for completeness'." · one "IMPORTANT:" per prompt only. Enforce boundaries **structurally** (`permission`), not by prose.

Subagent count by complexity: Simple=1 · Standard=2–3 · Medium=3–5 · High=5–10 (hard max 20). "Prefer fewer, more capable subagents. Only add subagents when they provide distinct value." Run ~3 in parallel at start for non-straightforward work. Do NOT delegate trivial tasks the hub can do itself, and NEVER delegate the final report — the hub writes it.

### 2. Two-ledger tracking
- **Task Ledger** (tasks.md): the plan + assigned subtasks + known facts.
- **Progress Ledger** (log.md): per-step self-reflection on whether each task is complete.
- The hub writes BOTH. After each worker returns, update the Progress Ledger. If no progress for N steps → update the Task Ledger and re-plan (stagnation detector = loop-break rule).
- Worker status vocabulary: `active / idling / done / crashed`. "Done" = an explicit terminal artifact (file path + digest), not silence.

### 3. Single source of truth
- Shared state has exactly ONE writer: the hub. SOURCE-WRITERS write scoped source + their `hub/artifacts/<task-id>/` dir; READ-ONLY workers write nothing (digest is the deliverable); everyone returns lightweight references (paths).
- Persist the plan to disk — compaction can truncate the conversation; files survive.
- Workers persist full findings to files and return ≤1500-token digests; the hub reads artifacts on demand (just-in-time, never pre-loaded).
- Resumability: durable state + retry + regular checkpoints; resume from the last checkpoint, never restart from scratch.
- Evaluate the END STATE (did it achieve the goal), not turn-by-turn; break long work into checkpoints with expected state changes.
- Parallel-edit safety: isolate editing workers (worktrees) and have the hub reconcile merges explicitly — never auto-merge uncommitted work.

### 4. Layered stop rules (a stack, not a style)
1. Turn/iteration cap (`max_turns` / `recursion_limit`).
2. Tool-call budget, tiered by difficulty (≤5 / 5 / ~10 / up to 15; absolute 20 + ~100 sources; "if exceeded, the subagent is terminated").
3. Wall-clock timeout — every wait gets a timeout.
4. Sentinel token ("TERMINATE"/"APPROVE") — coordinators end with TERMINATE.
5. Diminishing-returns clause — "when further research has diminishing returns… STOP FURTHER RESEARCH and do not create any new subagents. Just write your final report."
6. No repeated queries — "NEVER repeatedly use the exact same queries for the same tools."
7. Stagnation detector — no progress for N steps → re-plan.
8. Human checkpoint — pause-and-ask at defined gates; in-flight interrupt/redirect.
Graceful terminal states, never hangs: on max_turns return "[name hit max_turns]"; on timeout return "woke: 60s timeout".

### 5. Anti-patterns (never bake into a hub)
- Vague briefs ("research the semiconductor shortage") → duplication and gaps.
- Subagent-count explosion → tiered caps.
- Overlapping assignments → 1 core objective per worker + crisp boundaries + explicit division of labor.
- Endless research → diminishing-returns clause + per-worker tool-call budget.
- Context pollution → clean windows; coordinate via compact messages and condensed reports.
- Orchestrator doing workers' work → "coordinate, guide, synthesize — NOT conduct primary research."
- Orchestrator not synthesizing → "NEVER create a subagent to generate the final report — YOU write it."
- Feedback-loop / deadlock → caps + timeouts + kill/get_status + sentinel token.
- Rigid scripts → prompts as "frameworks for collaboration", not scripts.
- State corruption on deploy → resumable checkpoints; end-state eval; don't force-update in-flight agents (rainbow deploys).

### 6. Context hygiene
- A fresh subagent is a clean window: it sees only its own system prompt + delegation prompt, NOT the main conversation. Keep the window small at spawn AND as it works.
- Just-in-time context: hand workers lightweight identifiers (paths, queries, links); let them grep/glob/read detail on demand.
- Bounded exploration: 2–3 entry points, hard tool-call budget (max ~12), explicit "do not read" dirs, start-broad-then-narrow, stop-when defined.
- Token-efficient reading: grep/glob to locate, then read offset/limit slices; signatures before bodies, call sites before definitions; file metadata before committing.
- Digest + artifact handoff: worker writes full findings to a file, returns ≤1500-token digest + path.
- Uniform house-rule footer injected into every spawned subagent (≤300-word reports, cite file:line, never touch outside listed paths).
- `steps` caps + `temperature` 0.0–0.2 for focused work — set `steps: N` in each whitelisted subagent's own file at build time (a Task-tool brief cannot set it at runtime); enforce tool-call budgets via the brief.
- Spawn vs in-context: spawn when (a) exploration is separable and would pollute the hub window, (b) work parallelizes across ≥2 independent subtasks, (c) a specialist system prompt is needed. Otherwise do it in-context. Multi-agent is ~15× tokens — don't spawn for trivial work.

### 7. Compaction resilience
- Externalize state to files (state.md / tasks.md / log.md) — the conversation is ephemeral under compaction; files are not.
- Status line (open + updated timestamp + compaction counter) so a fresh agent knows currency and how many resets it survived.
- Decision log with statuses (`DECIDED: <decision> — reason: <why> — status: ACTIVE|RESOLVED|SUPERSEDED|REJECTED|OPEN`) — encodes "preserve architectural decisions" and lets the agent detect flips.
- Preserve exact identifiers as code spans (symbols, paths, error strings, commands) — verbatim survives.
- Append raw events to log.md (cheap, lossless) + periodically rebase the curated top into dense bullets.
- Re-orientation ritual at session start: read state.md, verify the status line, reconcile conversation vs file (conversation wins on conflict), rewrite with new decisions/blockers.
- Model state.md on the opencode summary template (`## Objective → ## Important Details → ## Work State → ## Next Move → ## Relevant Files`) so compaction summaries stay congruent.
- Compaction-hook note (build phase): `experimental.session.compacting` can force-preserve state; requires a plugin file in `.opencode/plugin/`. Mention it in the hub doc as a hardening step — not required for the first cut.

### 8. Adversarial verification
- Writer/Reviewer split: a fresh-context reviewer sees ONLY the diff + criteria (never the author's reasoning) and flags correctness/requirement gaps only, not style.
- Every task gets a verification check (build exit code, test, diff-vs-fixture) — "looks done" is not enough; the worker iterates until the check passes.
- Point workers at existing patterns ("follow how X is implemented in file Y") to prevent divergent conventions.
- LLM-as-judge for free-form output: single judge, 0.0–1.0 + pass/fail, rubric on accuracy/completeness/source quality/tool efficiency.
- Start evaluation small (~20 cases) and early; human eval catches what evals miss.

### 9. Routing (customize per target repo)
Build a routing table mapping hub work-types to the target's real agents/skills; list delegation gaps explicitly. GuardEngine example (knowledge §I) as a canonical pattern:

| Work type | Route to |
|---|---|
| scaffolding / new component | `component-scaffolder` |
| interop / marshaling audit | `boundary-auditor` |
| performance / hot paths | `performance-analyst` (+ `perf-review` skill) |
| code review | `review` |
| build/crash diagnosis | `debug` |
| vstest TRX analysis | `trx-analyst` |
| xcom exec-chain tracing | `xcom-tracer` |
| scenario health audit | `scenario-health` skill |
| planning | NOT routable from the hub — ask the user (move-manipulator-planner / waypoint-planner are primary planners, deliberately not whitelisted in §J) |
| docs + PDF | `doc-writer` + `md-to-pdf` |
| (no dedicated agent) | do in-context, or flag as a delegation gap |

## Hub creation checklist

Mirrors the waypoint-planning delegation pattern (placement → design → verify → write → hand back).

- [ ] **1. Placement/scope confirm** — workspace name confirmed · mode confirmed (subagent default) · single-vs-global placement confirmed · target subagent list confirmed against inventory · delegation gaps identified.
- [ ] **2. Design** — hub agent definition tailored: frontmatter · role · delegation contract · two-ledger tracking · SSOT layout · 8-layer stop rules · anti-patterns · context hygiene · compaction resilience · adversarial verification · routing table · **session safety** (session-scoped workspaces `sessions/<session-id>/`; shared knowledge read-merge-write; cross-session source-edit safety — §C.1).
- [ ] **3. Verify against knowledge** — self-check against this checklist · embedded pattern covers all required elements · fresh-context reviewer subagent run for non-trivial designs · gaps fixed.
- [ ] **4. Write files** — hub agent `.md` written to the confirmed placement · workspace created with state.md/tasks.md/log.md/artifacts/ (+ optional README) · writes confined to the named workspace.
- [ ] **5. Hand back to user** — report written paths · changed-vs-template table with reasons · verification status · exact invocation step for the primary/user.

## Output discipline

No preamble. Lead with the write-target confirmation, then the checklist status, then the changed-vs-template table. Be precise; cite paths. If any interview answer is missing, ask before designing — never guess scope.
`````

---

## Sources

- https://www.anthropic.com/research/building-effective-agents — workflows vs agents, orchestrator-workers, parallelization, evaluator-optimizer, tool/ACI design.
- https://www.anthropic.com/engineering/built-multi-agent-research-system — delegation, effort scaling, subagent-as-compression, filesystem artifacts, end-state eval, resume-from-checkpoint, long-horizon context management.
- https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents — context rot, altitude, just-in-time context, compaction, structured note-taking, sub-agent architectures.
- https://www.anthropic.com/engineering/claude-code-best-practices — verification, plan-before-code, CLAUDE.md, skills, subagents for investigation, compaction controls, adversarial review, failure patterns.
- https://github.com/anthropics/skills — Agent Skills repo, document skills, plugin marketplace, agentskills.io spec.
- https://agentskills.io — Agent Skills specification.
