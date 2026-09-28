# F22 — Leader state machine not extracted (Architecture review backlog)

> **Lane: bugfix (lighter — behavior-preserving refactor, unit-level RED, no VS boot).**
> F22 from `docs/progress.md` (Architecture review backlog, minor): the leader-key
> sequence routing in `InputHandler` (`InputHandler.cs:329-370`) needs AsyncPackage +
> MEF + WindowManager to construct, so it is not unit-testable. Fix: extract a pure,
> dependency-free `LeaderSequenceMatcher` (the `OverlayKeyHandler` pattern) that owns
> the leader state machine (leader-key start, sequence building, binding match, prefix
> detection, abort). Behavior-preserving: NO `[Telescope]`/`[NeoVisual]`/`[Hook]`
> structured-log literal change (M-M7 NOT triggered — no diagnostic added/changed).

## Goal

Make the leader-key sequence routing unit-testable by extracting it into a pure
state machine, without changing any observable behavior or diagnostic.

## Approach

1. **CREATE `MyExtension/LeaderSequenceMatcher.cs`** — a pure, dependency-free state
   machine (namespace `MyExtension`):
   - `internal sealed class LeaderSequenceMatcher`
   - Fields: `private readonly Keys _leaderKey; private readonly IReadOnlyDictionary<string, Action> _bindings; private bool _active; private readonly List<Keys> _sequence = new List<Keys>();`
   - `public bool IsActive => _active;`
   - `public LeaderResult HandleKey(Keys key, bool ctrl, bool shift, bool alt, bool isTyping)`:
     1. `if (key == _leaderKey && !ctrl && !shift && !alt)`: `if (isTyping) return LeaderResult.PassThrough;` else `_active = true; _sequence.Clear(); return LeaderResult.Consume;`
     2. `if (_active)`: `_sequence.Add(key); string sequence = string.Join(",", _sequence.Select(KeyToString));` — if `_bindings.TryGetValue(sequence, out var action)` → `_active = false; _sequence.Clear(); return LeaderResult.Execute(action, sequence);` — else `bool isPrefix = _bindings.Keys.Any(k => k.StartsWith(sequence + ",", StringComparison.OrdinalIgnoreCase));` — if `!isPrefix` → `_active = false; _sequence.Clear(); return LeaderResult.Abort;` — else `return LeaderResult.Consume;`
     3. else `return LeaderResult.PassThrough;`
   - `public void Reset()` → `_active = false; _sequence.Clear();`
   - `private static string KeyToString(Keys key)` — the current `InputHandler.KeyToString` logic moved verbatim.
   - `internal enum LeaderResultKind { PassThrough, Consume, Execute, Abort }` + `internal readonly struct LeaderResult { public LeaderResultKind Kind { get; } public Action? Action { get; } public string? Sequence { get; } ... static factories PassThrough/Consume/Execute(Action, string)/Abort }`.
2. **MODIFY `MyExtension/InputHandler.cs`**:
   - Replace `_leaderActive` (bool) + `_currentSequence` (List<Keys>) + the leader routing block (lines 329-370) with a `private readonly LeaderSequenceMatcher _leaderMatcher;` field (constructed with `_leaderKey` + `_leaderBindings`).
   - `public bool IsLeaderActive => _leaderMatcher.IsActive;` (the hook's pre-filter reads it).
   - `ResetSequence()` → `_leaderMatcher.Reset();`.
   - The leader block becomes: `var result = _leaderMatcher.HandleKey(key, ctrl, shift, alt, IsTyping()); switch (result.Kind) { case LeaderResultKind.PassThrough: break; case LeaderResultKind.Consume: return true; case LeaderResultKind.Execute: NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}leader-binding executed: {result.Sequence}"); result.Action!(); return true; case LeaderResultKind.Abort: return false; }` — then fall through to the simple-shortcut block (step 3) unchanged.
   - Delete the now-unused `KeyToString` from InputHandler (moved to the matcher).
   - The `IsTyping()` call is passed into `HandleKey` (the matcher is pure — it does not call `IsTyping` itself).
3. **Tests** (in `tests/NeoVisual.Tests/Program.cs`, RED: `LeaderSequenceMatcher` doesn't exist → compile error):
   - `Run_LeaderMatcher_LeaderKeyStartsSequence` — `HandleKey(leader, false,false,false, false)` → Consume, `IsActive` true.
   - `Run_LeaderMatcher_LeaderKeyWhileTypingPassesThrough` — `HandleKey(leader, false,false,false, true)` → PassThrough, `IsActive` false.
   - `Run_LeaderMatcher_SingleKeyBindingExecutes` — leader + binding key → Execute with the action + the sequence string.
   - `Run_LeaderMatcher_MultiKeySequence` — leader + "f" (a prefix) → Consume; + "f" → Execute.
   - `Run_LeaderMatcher_UnknownSequenceAborts` — leader + unknown key → Abort, `IsActive` false.
   - `Run_LeaderMatcher_ResetClearsState` — after a started sequence, `Reset()` → `IsActive` false.
   - `Run_LeaderMatcher_NonLeaderKeyPassesThrough` — non-leader key when inactive → PassThrough.
   - Use a test-local `Dictionary<string, Action>` with a captured counter to assert the executed action.

## Acceptance criteria

- `Run_LeaderMatcher_*` (7) pass — RED before (compile error: `LeaderSequenceMatcher` doesn't exist), GREEN after.
- `dotnet run --project tests/NeoVisual.Tests` → **81 pass** (74 + 7); `dotnet run --project tests/Telescope.Tests` → **77 pass** (unchanged).
- No `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal change (`leader-binding executed: {sequence}` byte-identical).
- Doc-ref diff gate: no NEW unresolved backticked refs.

## Known-RED allowlist

None.

## Verification Trace

| failing test | implicated steps | expected pass signal |
|---|---|---|
| `Run_LeaderMatcher_*` (RED: `LeaderSequenceMatcher` doesn't exist) | BP-1 | GREEN after the merge; `leader-binding executed: {sequence}` unchanged |
| `tests/NeoVisual.Tests` (81) | BP-1 | all existing `Run_*` pass |
| `tests/Telescope.Tests` (77) | BP-1 | all existing `Run_*` pass (no Telescope code touched) |
| A3 log-literal check (`git diff`) | BP-1 | no `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changed |

## Execution Log

(empty — populated by `neovim_hub` per attempt.)
