# F12 — Display-keyed payload lookup loses same-named duplicates (Architecture review backlog)

> **Lane: bugfix (unit-level RED, no VS boot).** F12 from `docs/progress.md`
> (Architecture review backlog, major): `TelescopeOverlay.cs` maps the fzf-matched
> display lines back to the original `FinderEntry` payloads by display text
> (`GroupBy(x => x.Display).ToDictionary(g => g.Key, g => g.First())`), so two entries
> with the SAME display text (e.g. two `Program.cs` files in different folders) collapse
> to the FIRST entry — the second becomes a null-payload `new FinderEntry(m)` that
> silently does nothing when selected. Fix: map filtered lines back by stable ordinal
> (each matched display string consumes the next unconsumed entry with that display),
> extracted into a pure, dependency-free `ResultMapper` (the `OverlayKeyHandler`
> pattern). NO `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal change
> (M-M7 NOT triggered).

## Goal

Preserve same-named duplicate entries when mapping fzf-matched display lines back to
their payloads, by extracting the mapping into a pure, unit-testable `ResultMapper`.

## Approach

1. **CREATE `Telescope/ResultMapper.cs`** — a pure, dependency-free static class
   (namespace `Telescope`):
   - `public static IReadOnlyList<FinderEntry> MapBack(IReadOnlyList<string> matched, IReadOnlyList<FinderEntry> snapshot)`:
     - Build `var byDisplay = snapshot.Select((entry, index) => (entry, index)).GroupBy(x => x.entry.Display, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);`
     - `var consumed = new HashSet<int>(); var items = new List<FinderEntry>();`
     - For each `m` in `matched`: if `byDisplay.TryGetValue(m, out var candidates)`, take the first candidate whose index is NOT in `consumed` (add it to `consumed`), and add its `entry`; if none left, add `new FinderEntry(m)` (the null-payload fallback, matching the current behavior for unknown strings). Else add `new FinderEntry(m)`.
     - Return `items`.
   - This preserves duplicates: two matched "Program.cs" strings consume the two distinct entries with that display.
2. **MODIFY `Telescope/TelescopeOverlay.cs`** (`FilterAndUpdateAsync`, lines 387-392): replace the inline `byDisplay` GroupBy/ToDictionary + Select with `var items = ResultMapper.MapBack(matched, snapshot);`. The rest of the method (token checks, `_results = items`, `_keyHandler.SetResults`, `RenderResults`) unchanged.
3. **Tests** (in `tests/Telescope.Tests/Program.cs`, RED: `ResultMapper` doesn't exist → compile error):
   - `Run_ResultMapper_DuplicateDisplayPreserved` — snapshot with two entries sharing a display (e.g. two `Program.cs` with different payloads), matched = ["Program.cs", "Program.cs"] → both entries returned with their distinct payloads (the second is NOT a null-payload `FinderEntry`).
   - `Run_ResultMapper_UniqueDisplayMapped` — snapshot with distinct displays, matched = the displays → each maps to its entry.
   - `Run_ResultMapper_UnknownStringNullPayload` — matched contains a string not in the snapshot → a null-payload `FinderEntry` with that display.
   - `Run_ResultMapper_OrderPreserved` — matched order is preserved in the output.
   - Use `FinderEntry` with `FileHit` payloads (the Lane 3 hit model) to assert the payload identity.

## Acceptance criteria

- `Run_ResultMapper_*` (4) pass — RED before (compile error: `ResultMapper` doesn't exist), GREEN after.
- `dotnet run --project tests/Telescope.Tests` → **81 pass** (77 + 4); `dotnet run --project tests/NeoVisual.Tests` → **81 pass** (unchanged).
- No `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal change.
- Doc-ref diff gate: no NEW unresolved backticked refs.

## Known-RED allowlist

None.

## Verification Trace

| failing test | implicated steps | expected pass signal |
|---|---|---|
| `Run_ResultMapper_*` (RED: `ResultMapper` doesn't exist) | BP-1 | GREEN after the merge; duplicate displays preserve both payloads |
| `tests/Telescope.Tests` (81) | BP-1 | all existing `Run_*` pass |
| `tests/NeoVisual.Tests` (81) | BP-1 | all existing `Run_*` pass (no NeoVisual code touched) |
| A3 log-literal check (`git diff`) | BP-1 | no `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changed |

## Execution Log

(empty — populated by `neovim_hub` per attempt.)
