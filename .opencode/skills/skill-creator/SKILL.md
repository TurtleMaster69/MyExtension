---
name: skill-creator
description: Authors new opencode Agent Skills (SKILL.md files) and improves existing ones — captures intent, interviews for edge cases, writes schema-valid frontmatter and body, and verifies the skill triggers and is actually used by its owning agent. Use when creating a skill from scratch, editing/optimizing an existing skill, or encoding a repo/domain convention as a reusable skill for an agent or hub.
---

# Skill Creator — author opencode Agent Skills

A skill for creating new Agent Skills and iteratively improving them, tailored to opencode's skill
system and this repo's hub ecosystem. Adapted from the anthropics `skill-creator` (Apache-2.0) with
all Claude.ai / Claude Code / Cowork-specific mechanics removed.

## The core loop

1. Decide what the skill should do and roughly how.
2. Write a draft `SKILL.md`.
3. Verify it (schema + trigger + loop-closure) and iterate with the user.
4. Repeat until the user is satisfied.

Your job is to figure out where the user is in this loop and jump in there.

## Capture intent

Start by understanding what the skill should enable. If the conversation already contains a workflow
to capture (e.g. "turn this into a skill"), extract it first — the tools used, the sequence of steps,
the corrections, the input/output formats. Confirm with the user before proceeding.

1. What should this skill enable the agent to do?
2. When should it trigger? (what user phrases/contexts)
3. What's the expected output format?
4. Should we set up test prompts to verify it works? Skills with objectively verifiable outputs
   (file transforms, data extraction, code generation, fixed workflow steps) benefit from tests;
   subjective skills (writing style) often don't.

## Interview and research

Ask about edge cases, input/output formats, example files, success criteria, and dependencies before
writing. In this repo, research existing patterns first: read the target agent's rules/instructions
and any existing skills so the new skill does not contradict them. Never wildcard-search or recurse
`library/` or other enormous dirs.

## Write the SKILL.md

**Load the `customize-opencode` skill before writing or validating any SKILL.md** — it is the
authoritative reference for opencode's skill schema (name rules, description requirements, discovery
paths). Do not write skill frontmatter from memory.

### Frontmatter

- `name` — required, lowercase-hyphen, up to 64 chars, MUST match the folder name.
- `description` — effectively required: skills without one are filtered out and never surfaced.
  Cover BOTH what the skill does AND when to use it. Front-load the literal trigger keywords or
  filenames the user is likely to say. Write in third person ("Use when...", not "I help with...").
  Gate with "Use ONLY when..." if the skill should stay quiet on adjacent topics.
- Optional: `license`, `compatibility`, `metadata`.

### Anatomy

```
skill-name/
├── SKILL.md (required)
│   ├── YAML frontmatter (name, description required)
│   └── Markdown instructions
└── Bundled resources (optional)
    ├── scripts/    - executable code for deterministic/repetitive tasks
    ├── references/ - docs loaded into context as needed
    └── assets/     - files used in output (templates, icons, fonts)
```

### Progressive disclosure

Three-level loading:
1. Metadata (name + description) — always in context (~100 words).
2. SKILL.md body — in context whenever the skill triggers (<500 lines ideal).
3. Bundled resources — loaded as needed.

Keep SKILL.md under 500 lines; if approaching the limit, add a hierarchy layer with clear pointers.
Reference files clearly with guidance on when to read them; for large reference files (>300 lines)
include a table of contents. When a skill supports multiple domains, organize by variant
(`references/aws.md`, `references/gcp.md`, ...) and read only the relevant one.

### Writing patterns

- Prefer the imperative form in instructions.
- Define output formats with an exact template.
- Include examples (Input → Output).
- Explain the WHY behind instructions rather than heavy-handed MUSTs. If you find yourself writing
  ALWAYS/NEVER in caps or rigid structures, reframe and explain the reasoning.
- Make the skill general, not super-narrow to specific examples.

### Safety

Skills must not contain malware, exploit code, or anything that compromises security. Do not create
misleading skills or skills that facilitate unauthorized access or data exfiltration.

## Description optimization (triggering)

The `description` is the primary trigger mechanism. opencode surfaces skills by name + description,
and the model decides whether to consult one based on that description. To combat undertriggering,
make descriptions a little "pushy": include the concrete trigger keywords and contexts, even ones the
user might not name explicitly.

To validate a description, draft ~10 should-trigger and ~10 should-not-trigger realistic user queries
(concrete, with detail — not abstract one-liners). Should-not-trigger cases should be near-misses
(share keywords but need something different), not obviously irrelevant. Present them to the user for
sign-off, then refine the description until the should-trigger set fires and the near-misses don't.

## Test and verify

After writing the draft, come up with 2-3 realistic test prompts and share them with the user. Then
verify the skill end-to-end:

1. **Schema** — `name` matches the folder, `description` present and trigger-rich (per
   `customize-opencode`).
2. **Trigger** — the description's keywords would actually surface for the test prompts.
3. **Loop-closure** — the owning agent's instructions reference the skill (SEE/ACCESS/WILL-USE).
   A skill an agent can see but is not prompted to use is equivalent to not having it.
4. **Contradiction** — the skill's instructions do not fight the owning agent's rules. If they do,
   adapt the skill or drop it.

In this repo, the `skill-verifier` subagent runs these checks formally (SEE/ACCESS/WILL-USE +
contradiction). Use it before declaring a skill ready.

## Improving an existing skill

1. **Generalize from feedback** — the skill will be used across many prompts; don't overfit to the
   few test examples.
2. **Keep it lean** — remove anything not pulling its weight.
3. **Explain the why** — transmit understanding, not rote rules.
4. **Look for repeated work** — if test runs all wrote the same helper script, bundle it into
   `scripts/` once.

Apply improvements, re-verify (schema + trigger + loop-closure), and repeat until the user is happy
or you stop making meaningful progress.

## Output discipline

No preamble. Lead with the skill's name + target install path, then the frontmatter, then the body.
Confirm the owning agent references the skill before considering it done.
