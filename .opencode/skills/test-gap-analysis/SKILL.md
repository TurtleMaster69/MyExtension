---
name: test-gap-analysis
description: >-
  Pseudo-mutation analysis ONLY (read-only): answer whether tests would catch a
  bug if production code changed, which meaningful changes would still pass, or
  which caller-visible mutations existing assertions would miss. Static
  source-to-assertion reasoning only — never executes tests, never mutates
  production code, never writes tests. Activate for behavioral blind spots or
  missing edge cases tied to production behavior. Polyglot. DO NOT USE FOR:
  suite organization, taxonomy, metadata, or distribution reports
  (test-tagging); .NET line-vs-branch or Cobertura interpretation, arithmetic,
  plateaus, project-wide coverage gaps, or coverage-backed test/CRAP priorities
  (coverage-analysis; use native coverage tooling outside .NET); named-target
  CRAP (crap-score); new suites (code-testing-agent); assertion/smell audits;
  or mutation tools.
license: MIT
---

# Test Gap Analysis (read-only)

Answer one question: **which caller-visible production behaviors could change
without an existing test failing?** Mutation reasoning is a probe, not the goal.
Inventory public outcomes first, then classify gaps statically. This is a
READ-ONLY variant: you never run the test suite, never apply a mutation to
production code, and never write tests. All findings are static
source-to-assertion conclusions; anything that would require execution is
reported as **Candidate survivor (unverified)** or **No coverage**.

## Decision flow

### 1. Set scope

Discover production and test files from manifests and file types. After a narrow
search misses, inspect the current directory broadly before asking for paths.

| Request | Action |
|---|---|
| One component or named risk | Inventory every high-risk public outcome in scope |
| General small-component review | Inventory distinct outcomes and report caller-visible gaps from source/assertion mapping |
| Explicit exhaustive audit | Classify all meaningful candidates statically using the observable-candidate rules below |
| Add tests to an existing suite | Out of scope for this read-only reviewer — report gaps for a writer agent to close |
| Create a new suite | Out of scope — hand off to `code-testing-agent` |

When the request names a risk, turn it into a one-line public-outcome allowlist
before reading code. An outcome is not in scope merely because the same method writes it.
For `money math`, allow computed or returned amounts, rates, tier/boundary
choice, percentage base/order, floors/caps, and rounding; exclude non-monetary
state predicates (including derived booleans), identity, and formatting. Private
code is in scope only to trace an allowed outcome.

Do not expand a focused request into a repository audit, plan artifact, or
dashboard. Use source and tests directly for familiar frameworks. If the
`test-analysis-extensions` skill is available, invoke it only when discovery or
assertion semantics are unclear; if it is NOT available, proceed with the
built-in rules below (self-contained).

### 2. Static baseline (no execution)

Do NOT run the test suite, do not run a build, do not invoke any mutation tool.
This reviewer is read-only: no test command is executed, ever. State once that
the analysis is static-only, then give the static source/assertion conclusion
directly instead of hedging every row.

Static-only limits only claims of empirical mutation survival. It does not make
source-proven facts tentative: a public outcome with no reaching test is still
**No coverage**, and an exact expected value derived from the unmodified
implementation is still actionable. Source-to-assertion mapping is sufficient
evidence for **No coverage** and **Candidate survivor (unverified)**. Trace the
unmodified code through the call chain when an original value is unclear; never
apply a mutation to disk.

Keep every distinct unasserted public outcome in the inventory.

### 3. Inventory public outcomes

For each public entry point, map:

- input partitions: classifier arms, compound conditions, invalid and
  nearest-valid guard boundaries, and default cases;
- each independent observation: returned field/variant, exception type,
  invalid-input acceptance, public state transition, or external side effect;
- private-helper composition, constants/rates, rounding, retries, cancellation,
  and error propagation as observed through the public caller.

Use `public input/sequence -> expected outcome -> existing assertion -> gap`.
One asserted return field does not cover another. One allowed result does not
cover its denial.

**Money math:** inventory the no-op path, every rate/tier and exact boundary,
operation order, percentage base or composition, floor/cap, and rounding. Trace
private helpers through the public result. A test asserting only a broad range
does not pin any exact amount. For each actionable money row, derive one witness
input and its exact original result through the complete call chain; do not
recommend a generic "assert the exact amount" without supplying that amount.

**Ordered guards and retries:** inventory `invalid below minimum | first valid |
last allowed or retryable | first blocked | later blocked`. For an upper guard
such as `value >= limit`, use `limit - 1`, `limit`, and `limit + 1`; the last
witness exposes narrowing to `value == limit`. Inventory every accepted and
rejected error class. When type matching is polymorphic, include a representative
derived accepted type that would expose exact-runtime-type narrowing. A test at
the first blocked value does not protect the last allowed or later blocked value.

**Authorization:** enumerate each relevant identity/role, resource class, and
action from the caller's view. Untested `false`, forbidden, and unchanged-role
outcomes are first-class security gaps. Do not analyze variants of an allowed
path while a denial outcome remains uninventoried. Check each public surface:

- permission-returning APIs: every distinct role/resource class and every
  returned capability independently;
- action-dispatch APIs: each read/write/delete-style action branch, especially
  paths that must return denial;
- role/state transitions: accepted, rejected, invalid, null, and empty inputs,
  including outcomes that must leave state unchanged.

If more than five high-risk behaviors are unasserted, report the top 3-5 and
keep the rest visible as **No coverage** or **Candidate survivor (unverified)**.

The ledger is never replaced by execution (there is none). Before answering,
classify every required outcome, including each invalid input, guard boundary,
classifier arm, action, and denial.

**Completeness checkpoint:** before selecting findings, explicitly account for
every independent mode/flag, both zero and negative for a `<= 0` guard, every
accepted exception class, and a representative derived accepted exception when
matching is polymorphic. For a removed guard, trace the fallthrough: if it still
produces the same public exception type, it is equivalent unless finer exception
metadata is an established contract.

### 4. Admit only observable candidates

First replay each exact mutation against every existing asserted input or
sequence with all arguments fixed. Any changed return, exception, state, or side
effect is **Likely killed**; a dedicated single-purpose test is unnecessary.
Never compare the mutant on one input with the original on another.

For survivors, choose a witness before reporting and state
`witness -> original observation -> mutant observation`. Admit it only when the
last two differ publicly after tracing the full call chain; otherwise choose a
distinguishing witness or drop it.

Exclude:

- edits that require inserting or reordering statements rather than changing or
  removing an existing expression, condition, constant, return, or side effect;
- edits that do not compile, including removal of a declaration whose value is
  still referenced;
- overflow behavior, exception message/`ParamName` metadata, or other semantics
  not established by the current contract, source intent, or tests;
- a removed guard or short-circuit that falls through to the same result,
  exception, state, and side effects;
- private representation changes that every public input sequence observes
  identically, even if the suite stays green;
- a mutation whose proposed test passes against both original and mutant;
- boundary edits that return the same value on the distinguishing input; for
  example, changing `result < floor ? floor : result` to `<=` is equivalent at
  equality because both branches return `floor`;
- a standalone auto-property or trivial one-line wrapper/predicate with no
  meaningful branch, calculation, or side effect, unless the user names it;
- hypothetical future impact, generated code, logging/formatting-only changes,
  impossible values, and duplicate syntax variants.

Missing direct assertions do not prove **No coverage**: first trace existing
assertions through public callers and shared branches. Missing assertions make
an **observable** candidate a survivor; they do not make an inert mutation
meaningful.

### 5. Rank and classify

Rank: (1) security denials, financial outcomes, errors, and state changes;
(2) wholly unasserted public outcomes; (3) boundaries or exact values reached by
weak assertions; (4) alternate variants of already-asserted behavior.

Finish the inventory before selecting mutations or a verdict. One killed
attempt, exception type, or switch arm does not clear its siblings.

Choose the verdict from the completed inventory:

- **Strong** when core branches and primary boundaries are protected and only a
  few validation or default-case variants remain;
- **Mixed** when meaningful coverage exists but at least one important outcome
  partition is unprotected;
- **Weak** when important outcomes are broadly unprotected.

A handful of validation gaps does not make an otherwise broad suite **Mixed**
unless validation is the named risk or the gaps threaten security, data, or
other contract-critical behavior.

When the inventory meets the **Strong** criteria above, lead with **Strong** and
name the protected boundaries and dual assertions before listing minor gaps. Do
not open with `Mixed`, "only core paths", or a risk-heavy dashboard.

Stop when existing assertions kill the remaining candidates or no credible
public survivor remains. Do not enumerate every operator merely to fill a report
or calculate a score.

| Result | Meaning |
|---|---|
| **Likely killed** | An existing assertion observes the changed outcome |
| **Candidate survivor (unverified)** | Observable change appears unasserted; not executed (read-only: this is the strongest claim available) |
| **No coverage** | No test reaches the public outcome; report the missing branch without inventing a survivor |
| **Equivalent** | No public observation changes; omit from findings |

No mutation is ever executed in this read-only mode. **Survived** (execution-
proven) is not a label this reviewer may use. Do not mutate to confirm obvious
no coverage.

### 6. Static verification (no mutation applied)

This read-only reviewer never applies a mutation to disk and never runs a test.
To verify a candidate without execution:

1. Trace the counterfactual through the complete call chain: for the chosen
   witness, derive the original observation from the unmodified source, then
   derive the mutant observation by reasoning over the edited expression only.
2. Confirm the two observations differ publicly (return, exception, state, or
   side effect) — otherwise the candidate is **Equivalent**; drop it.
3. Confirm an existing asserted input/sequence would observe the change; if so,
   the candidate is **Likely killed** and needs no report row.

Never leave mutations in the workspace (none are ever applied). Before
reporting, reconcile every unasserted high-risk outcome as **Candidate survivor
(unverified)**, **No coverage**, or omitted **Equivalent**. Stop when no credible
public gap remains; do not fill a report with internal details or calculate a
score unless the user requested an exhaustive audit.

### 7. Close gaps — out of scope (hand off)

This reviewer is read-only and never writes tests. Do not add, edit, or delete
any test or production file. Report every demonstrated gap (with its
distinguishing witness and a concrete smallest-test suggestion) so a writer
agent can close it. Do not claim a gap is closed; closing is the writer's job.

## Output contract

Scale the response to the request.

For focused or small analysis, return:

1. A one-line verdict: **Strong**, **Mixed**, or **Weak**, with the reason.
2. For a **Strong** suite, one short strengths sentence naming the concrete
   protected boundaries, guards, or paired observations that justify the verdict.
3. One compact row per actionable **Candidate survivor (unverified)** or
   **No coverage** outcome. Before adding a row, apply the outcome allowlist
   when the request names a risk, then apply the observable-candidate rules;
   omit any candidate that fails either filter. Include every high-risk outcome,
   use one row per distinct public outcome, and consolidate only related
   low-risk variants:

   | Risk | Public outcome | Change | Result/evidence | Smallest test |
   |---|---|---|---|---|

   Every gap needs a distinguishing witness and a concrete smallest test. An
   error-path gap must name an invalid input and the expected error/result.

4. For a **Mixed** or **Weak** suite, one short strengths sentence naming
   important killed behavior.
5. When the request names exclusions, one short scope sentence naming the
   generated, trivial, or unrelated code intentionally skipped.

Do not repeat the table in prose or report discarded mutants, tool chronology,
or in-flight reasoning.

For an exhaustive audit, add counts for Likely killed / Candidate survivor
(unverified) / No coverage / Equivalent and group findings by risk. Count only
definitively classified candidates.

## Reliability rules

- A passing test that does not assert the changed outcome does not kill a
  mutation.
- Coverage is per behavior partition. One switch/ternary arm or compound input
  does not prove siblings: allow does not prove deny; read does not prove write;
  null does not prove empty or whitespace when those inputs have different
  caller-visible outcomes. A kill clears only the edit and path that ran.
- Private helpers reached through a public method remain in scope.
- Error semantics are language-specific: in Rust, `?` propagation versus panic
  is observable behavior; in C#, exception type and whether an input guard
  accepts or rejects a value are observable behavior.
- Cross-check every exact amount or boundary result against the unmodified
  implementation or an existing exact assertion. If it cannot be checked,
  state the behavioral relation without inventing a number.
- Do not label a finding high-risk merely because a mutation survived.
- Never recommend a redundant test for behavior the existing suite already
  protects.
- Read-only: never run tests, never mutate production code, never write tests.

## Validation

- [ ] Scope stayed proportional to the request
- [ ] No test command was executed; no mutation was applied to disk
- [ ] Every high-risk public outcome in scope was inventoried
- [ ] Original and mutant have different caller-visible observations (traced)
- [ ] No outcome is labeled **Survived** (execution-only label is forbidden here)
- [ ] Findings exclude trivial, generated, and equivalent changes
- [ ] Recommendations target only demonstrated gaps
- [ ] Every public entry-point branch and each accepted exception type in scope
      is explicitly accounted for
- [ ] No test or production file was written, edited, or deleted
