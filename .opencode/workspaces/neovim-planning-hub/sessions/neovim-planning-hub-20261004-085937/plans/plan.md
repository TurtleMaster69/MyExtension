# Plan — Gap 1: Window-management leader bindings (`w` prefix) + case-sensitive leader combos

> **Lane: feature (unit-only, e2e deferred).** New capability (window-management bindings +
> case-sensitive leader matching) that REUSES the existing `[NeoVisual] leader-binding executed:`
> diagnostic — no new diagnostic format, no M-M7 trigger. E2E is DEFERRED to `e2e-queue.md`
> (user mandate 2026-10-04: never boot the VS Experimental Instance until the user says so).
>
> **Source:** `docs/progress.md` item 6 (Gap 1, triage EXTEND/REUSE native 2026-09-28) + user
> instructions 2026-10-04 (this session): skip zoom, skip resize, remove `Space+W` save (user
> saves with Ctrl+S), use `W` as the window prefix, make leader combos case-sensitive
> (`leader+s+g` ≠ `leader+s+G`), delete-window = focus-aware new action.
>
> **Ground truth:** repo GREEN. Unit suites: `tests/Telescope.Tests` **172 passed**,
> `tests/NeoVisual.Tests` **171 passed**. Live E2E: 37 scenarios, no known-RED (not runnable
> on this machine — deferred). This plan changes the leader-matching contract, so the e2e
> scenario count goes **37 → 38 registered** (one NEW scenario `neovisual-window-management`
> is registered by the harness update but never executed — queued as E2E-GAP1-1; three
> existing scenarios are UPDATED in place).
>
> **Research:** `feature-researcher` verified the VS command names from the VS SDK
> `ShellCmdDef.vsct` + MS Learn default-keyboard-shortcuts (HIGH confidence, compat-frozen):
> split-below = `Window.NewHorizontalTabGroup`, split-right = `Window.NewVerticalTabGroup`,
> close-document = `Window.CloseDocumentWindow`, close-tool-window = `Window.CloseToolWindow`.
> `trailmark-recon` verified the binding path (JSON → `KeybindingConfig.Load` →
> `InputHandler.BuildBindings` → `LeaderSequenceMatcher.HandleKey` → `ParseCommand` →
> `ExecuteVsCommand`), that `OemMinus`→`"-"` is already representable, that `OemPipe` has NO
> `KeyNames` mapping (needs a code change for `|`), and that `IsKeyOfInterest` returns true for
> ANY key while a leader sequence is active (so `-`/`|` reach the matcher).

## Goal

Free the `W` leader binding (remove the save binding — the user saves with Ctrl+S) and use
`W` as a **window-management prefix** (LazyVim `<leader>w`): `w,-` split below,
`w,|` split right, `w,d` close window (focus-aware native command). Additionally make
**leader combos case-sensitive** so `leader+s+g` and `leader+s+G` are distinct sequences
(capital letter in the config = Shift+letter), per the user's 2026-10-04 instruction.

## Approach

### Part 1 — Case-sensitive leader combos (the matcher refactor)

Today the leader matcher ignores Shift: `LeaderSequenceMatcher.HandleKey` appends
`KeyNames.ToString(key)` (no shift), and both the bindings dictionary and the prefix set use
`StringComparer.OrdinalIgnoreCase`. So `Space S G` and `Space S Shift+G` are identical.

The user's decision (2026-10-04): after the leader key, Shift is NOT written as a `Shift+`
prefix in the binding notation — a **capital letter in the config means Shift+letter**, a
lowercase letter means the plain key. So the sequence key name must encode the case.

**D1 — `KeyNames` gains a shift-aware overload; the single-arg form is unchanged.**

```csharp
// MyExtension/Input/Utils/KeyNames.cs
/// <summary>
/// Shift-aware key name for LEADER sequences: a letter's case encodes Shift
/// (lowercase = unshifted, uppercase = Shift+letter) so `s,g` and `s,G` are distinct
/// bindings. Non-letter keys delegate to the printable mapping (shift-insensitive —
/// the user's decision: shift is not noted in the config for non-letters).
/// The single-arg ToString stays the SIMPLE-shortcut contract (KeyNameBuilder prepends
/// "Ctrl+"/"Shift+"/"Alt+" itself and keeps the uppercase letter format).
/// </summary>
public static string ToString(Keys key, bool shift)
{
    if (key >= Keys.A && key <= Keys.Z)
    {
        char lower = (char)('a' + (key - Keys.A));
        return shift ? char.ToUpperInvariant(lower).ToString() : lower.ToString();
    }
    return ToString(key);   // non-letters: printable mapping, shift-insensitive
}
```

And the single-arg `ToString` gains one case (needed for `w,|`):

```csharp
case Keys.OemPipe: return "|";   // 220 (0xDC); Shift+OemPipe types '|'
```

`KeyNameBuilder.Build` (the SIMPLE-shortcut path) keeps calling the single-arg
`KeyNames.ToString(key)` — the `Ctrl+H` config format is a live contract and must not change.

**D2 — The leader path becomes case-sensitive; the simple path stays case-insensitive.**

- `LeaderSequenceMatcher.HandleKey`: `_sequenceBuilder.Append(KeyNames.ToString(key, shift));`
- `LeaderSequenceMatcher.BuildPrefixSet`: `new HashSet<string>(StringComparer.Ordinal)`.
- `InputHandler.BuildBindings`: the **leader** dictionary becomes
  `new Dictionary<string, Action>(StringComparer.Ordinal)`; the **simple** dictionary stays
  `OrdinalIgnoreCase` (simple shortcuts are matched via `KeyNameBuilder.Build`, whose
  `Ctrl+H` format is case-canonical).
- `KeybindingConfig.Merge`: the `bindings` map becomes
  `new Dictionary<string, string>(StringComparer.Ordinal)` — REQUIRED so `s,g` and `s,G`
  can coexist in the config (an OrdinalIgnoreCase map would collapse them).

**D3 — All existing leader bindings migrate to lowercase.** Every leader-sequence key in
`default-keybindings.json` is an unshifted letter today, so under the case-based contract it
must be written lowercase: `B,D`→`b,d`, `Q`→`q`, `E`→`e`, `F,F`→`f,f`, `F,T`→`f,t`, `F,D`→`f,d`,
`F,G`→`f,g`, `F,Z`→`f,z`, `F,B`→`f,b`, `F,R`→`f,r`, `F,I`→`f,i`, `C,W`→`c,w`, `C,A`→`c,a`,
`C,R`→`c,r`, `C,F`→`c,f`, `S,S`→`s,s`, `G,G`→`g,g`, `G,B`→`g,b`, `G,C`→`g,c`, `T,T`→`t,t`,
`B,B`→`b,b`, `B,R`→`b,r`, `/` stays `/`. The `Ctrl+H/J/K/L` simple shortcuts are UNCHANGED
(different path). The user's `%APPDATA%` override file (if any) is merged over the defaults by
the same case-sensitive map — a stale uppercase override would simply stop matching (documented
risk R4).

### Part 2 — Window-management bindings (`w` prefix)

**D4 — Remove the save binding, add the three window bindings.** In
`MyExtension/Resources/default-keybindings.json`:

```json
"w,-": "command:Window.NewHorizontalTabGroup",
"w,|": "command:Window.NewVerticalTabGroup",
"w,d": "close-window"
```

and DELETE `"W": "command:File.SaveSelectedItems"` (the user saves with Ctrl+S; `W` must stop
being a binding so it can be a prefix — the leader matcher executes a binding as soon as it
matches, so `w` as a binding would shadow every `w,*` sequence).

Semantics (researcher-verified): `Window.NewHorizontalTabGroup` moves the active tab into a new
tab group BELOW (the VS analog of a horizontal split); `Window.NewVerticalTabGroup` moves it
into a new tab group to the RIGHT. Caveat documented for the user: VS tab groups MOVE the
active tab (not a duplicate viewport) and VS cannot mix horizontal+vertical groups
simultaneously — semantics differ slightly from Neovim splits.

**D5 — `w,d` is a new built-in action `close-window` (focus-aware), per the user's choice.**
`Window.CloseDocumentWindow` closes the active document; `Window.CloseToolWindow` closes the
active tool window; there is no single "close whatever is active" command. The user chose the
focus-aware variant:

- `MyExtension/Package/Utils/Actions.cs` — add to the registry:
  `["close-window"] = (h, _) => () => h.CloseWindow(),`
- `MyExtension/Input/InputHandler.cs` — new method:
  ```csharp
  /// <summary>Focus-aware "delete window": closes the active tool window when a tool window
  /// holds focus, else the active document. Runs the native VS command by name via DTE.</summary>
  internal void CloseWindow()
  {
      ThreadHelper.ThrowIfNotOnUIThread();
      ExecuteVsCommand(CloseWindowCommand.For(_windowManager.IsToolWindow));
  }
  ```
- **Pure seam** (the `OverlayKeyHandler` pattern — dependency-free so it stays unit-testable),
  new file `MyExtension/Input/Utils/CloseWindowCommand.cs`:
  ```csharp
  internal static class CloseWindowCommand
  {
      /// <summary>Pure focus-aware close-window decision: a focused tool window closes via
      /// Window.CloseToolWindow; anything else (editor/document) via Window.CloseDocumentWindow.</summary>
      internal static string For(bool isToolWindow)
          => isToolWindow ? "Window.CloseToolWindow" : "Window.CloseDocumentWindow";
  }
  ```

### Part 3 — Tests, harness, docs

**D6 — Unit tests (all in `tests/NeoVisual.Tests`; `Telescope.Tests` untouched).** The RED
evidence is unit-level: before the change, `LoadDefaults()` has no `w,-`/`w,|`/`w,d`, the
`w` save binding still exists, `KeyNames.ToString(Keys.OemPipe)` returns `"OemPipe"`, the
leader matcher is case-insensitive, and `Actions.Registry` has 11 entries.

**D7 — Harness scenarios (updated in place, NOT run).** Three existing scenarios assert
`leader-binding executed: W` and break when `w` becomes a prefix; every `leader-binding
executed:` assertion must also become lowercase under the case-based contract. The harness is
updated as part of this change (it is committed test code) but NOT executed — the e2e gate is
queued. `neovisual-editor-insert` switches its save step from `Space+W` to `Send-Ctrl 0x53`
(Ctrl+S — VS's native save, which the extension passes through unbound) and drops the
leader-binding assertion (the file-content check is the ground truth).

**D8 — Docs sync.** `docs/spec.md` (§3 keybindings list + §4 diagnostics unchanged + §5 counts),
`AGENTS.md` (keybindings bullet + test counts), `.opencode/skills/vs-extension-dev/SKILL.md`
(binding list + counts), `docs/progress.md` (item 6 → DONE at GREEN). No new diagnostic
literal → the log-literal diff gate is unaffected.

## Acceptance criteria (each mapped to a diagnostic + a test)

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | `Space w -` fires split-below (native `Window.NewHorizontalTabGroup`) | `[NeoVisual] leader-binding executed: w,-` | unit `Run_Keybinding_DefaultFileHasWindowManagement` + `Run_KeyNames_RoundTrip_WindowPrefix`; e2e `neovisual-window-management` (QUEUED) |
| AC2 | `Space w |` fires split-right (native `Window.NewVerticalTabGroup`) | `[NeoVisual] leader-binding executed: w,|` | unit `Run_Keybinding_DefaultFileHasWindowManagement` + `Run_KeyNames_RoundTrip_WindowPrefix`; e2e `neovisual-window-management` (QUEUED) |
| AC3 | `Space w d` closes the focused surface: tool window → `Window.CloseToolWindow`, else `Window.CloseDocumentWindow` | `[NeoVisual] leader-binding executed: w,d` (+ `Command '<name>' failed` only on a VS fault) | unit `Run_CloseWindowCommand_For` + `Run_ActionsRegistry_ContainsAllBuiltins`; e2e `neovisual-window-management` (QUEUED) |
| AC4 | The `W` save binding is gone; `w` is a prefix (Space+W alone consumes and waits, fires nothing) | (absence — no `leader-binding executed: w` line for a lone Space+W) | unit `Run_Keybinding_DefaultFileHasWindowManagement` (no `w` key) + `Run_LeaderSequenceMatcher_PrefixWaits` |
| AC5 | Leader combos are case-sensitive: `s,g` and `s,G` are distinct sequences | `[NeoVisual] leader-binding executed: s,G` vs `s,g` | unit `Run_LeaderSequenceMatcher_CaseSensitive` + `Run_KeyNames_CaseEncodesShift` |
| AC6 | All existing leader bindings still fire after the lowercase migration | existing `leader-binding executed: <lowercase seq>` lines | unit `Run_Keybinding_DefaultFileHasTelescopeAndNav` (updated) + `Run_KeyNames_RoundTrip_LeaderSequence` (unchanged, `/`); e2e `neovisual-leader` (UPDATED, QUEUED) |
| AC7 | Simple shortcuts are unregressed (`Ctrl+H` format unchanged, case-insensitive simple dict) | existing `shortcut-binding executed: Ctrl+H` | unit `Run_KeybindingConfig_IsSimpleShortcut` + `Run_KeyNames_RoundTrip_SimpleShortcut` stay GREEN; e2e `neovisual-window-nav` (QUEUED) |
| AC8 | `neovisual-editor-insert` still proves insert-mode typing reaches the editor, now saving via Ctrl+S | file content contains the typed marker | e2e `neovisual-editor-insert` (UPDATED, QUEUED) |
| AC9 | `neovisual-explorer-move-editor-focus` still proves an editor-focused `m` is not a tree action, with a non-`w` positive bound | `leader-binding executed: <new seq>` | e2e `neovisual-explorer-move-editor-focus` (UPDATED, QUEUED) |

> AC4's "absence" criterion and AC5's `s,G` have no live e2e scenario in this plan — AC4 is
> pinned by unit tests (the matcher's Consume-not-Execute behavior), AC5 by unit tests. The
> queued `neovisual-window-management` scenario covers AC1–AC3 live.

## Unit test plan

**Project: `tests/NeoVisual.Tests`** (the leader/binding logic lives in `MyExtension.Input`;
`Telescope.Tests` is untouched — no Telescope type changes).

New/updated tests (RED before the change, GREEN after):

- `Run_Keybinding_DefaultFileHasWindowManagement` (**new**) — `KeybindingConfig.LoadDefaults()`:
  contains `w,-`→`command:Window.NewHorizontalTabGroup`, `w,|`→`command:Window.NewVerticalTabGroup`,
  `w,d`→`close-window`; does NOT contain key `w` (save removed) (AC1/AC2/AC3/AC4).
- `Run_Keybinding_DefaultFileHasTelescopeAndNav` (**update**) — `F,T` → `f,t` (AC6).
- `Run_Keybinding_CaseSensitive` (**update** of `Run_Keybinding_CaseInsensitive`) — the config
  map is now case-sensitive: `LoadFromJson("{\"bindings\":{\"W\":\"command:X\"}}")` does NOT
  contain `w`; `{"bindings":{"s,g":"a","s,G":"b"}}` yields TWO distinct entries (AC5).
- `Run_KeyNames_PrintableMappings` (**extend**) — add `KeyNames.ToString(Keys.OemPipe) == "|"` (AC2).
- `Run_KeyNames_CaseEncodesShift` (**new**) — `ToString(Keys.F, false)=="f"`,
  `ToString(Keys.F, true)=="F"`, `ToString(Keys.OemMinus, true)=="-"` (non-letters
  shift-insensitive) (AC5).
- `Run_KeyNames_RoundTrip_WindowPrefix` (**new**) — a matcher bound to
  `{"w,-","w,|","w,d"}`: Space→Consume, W→Consume (prefix), OemMinus→Execute `w,-`;
  fresh matcher: Space,W,OemPipe(shift:true)→Execute `w,|`; Space,W,Keys.D→Execute `w,d` (AC1/AC2/AC3).
- `Run_LeaderSequenceMatcher_CaseSensitive` (**new**) — bindings `{"s,g","s,G"}`:
  Space,S,g→`s,g`; Space,S,Shift+G→`s,G`; the two fire different actions (AC5).
- `Run_LeaderSequenceMatcher_PrefixWaits` (**new**) — with only `w,-` bound, Space+W returns
  `Consume` (not Execute) and nothing fires; a following unrelated key Aborts (AC4).
- `Run_CloseWindowCommand_For` (**new**) — `CloseWindowCommand.For(true)=="Window.CloseToolWindow"`,
  `For(false)=="Window.CloseDocumentWindow"` (AC3).
- `Run_ActionsRegistry_ContainsAllBuiltins` (**update**) — count 11→12, add `close-window` (AC3).
- Existing leader-matcher tests that bind uppercase sequences (`Run_LeaderMatcher_*`,
  `Run_KeyNames_RoundTrip_LeaderSequence` uses `/` so it is unaffected) — **update** any
  uppercase leader binding to lowercase so they keep testing the same behavior under the
  case-sensitive contract.

## Diagnostics

**No new diagnostic literal.** The existing `[NeoVisual] leader-binding executed: {sequence}`
line is the contract for every new binding (the sequence string is now case-sensitive, e.g.
`w,-`, `w,|`, `w,d`, `s,G`). Command failures keep logging the existing
`[NeoVisual] Command '{command}' failed: {msg}` line (never crashes the hook). The
log-literal diff gate sees NO new literals — only the harness's assertion strings change.

## Known-RED allowlist

**None.** All 37 e2e scenarios are GREEN and both unit suites pass (172 / 171). The only
expected RED is the new/updated unit tests listed above before the change exists (the RED
evidence for the Build Plan). The three UPDATED harness scenarios are not run on this machine
(e2e deferred) — their correctness is queued, not proven here.

## E2E queue reference (deferred — queued at handoff, NOT created/executed now)

| ID | Scenarios | What each asserts | Diagnostics depended on |
|----|-----------|-------------------|--------------------------|
| E2E-GAP1-1 | `neovisual-window-management` (**new**) | `Space w -` → `leader-binding executed: w,-` + a new horizontal tab group appears; `Space w |` → `leader-binding executed: w,|` + a new vertical tab group; `Space w d` with an editor focused → `leader-binding executed: w,d` + the active document closes | `[NeoVisual] leader-binding executed: w,-` / `w,|` / `w,d` |
| E2E-GAP1-2 | `neovisual-leader` (**updated**) | the leader system still fires after the lowercase migration (e.g. `Space e` → `leader-binding executed: e`) and `Space w -` fires the new prefix binding | `leader-binding executed: e`, `w,-` |
| E2E-GAP1-3 | `neovisual-editor-insert` (**updated**) | insert-mode typing still reaches the editor; the save step uses Ctrl+S (`Send-Ctrl 0x53`) and the file content contains the marker | file-content check (no leader assertion) |
| E2E-GAP1-4 | `neovisual-explorer-move-editor-focus` (**updated**) | editor-focused `m` still leaks no tree action; the positive focus bound uses a non-`w` leader binding | `leader-binding executed: <new seq>`; absence of `solution-explorer move` |
| E2E-GAP1-5 | full suite re-run | no regression from the lowercase migration across all leader-driven scenarios | all existing `[NeoVisual]`/`[Telescope]` lines (lowercased sequences) |

## Files to be touched (initial estimate)

- **Modified:** `MyExtension/Input/Utils/KeyNames.cs` (D1: OemPipe case + shift-aware overload),
  `MyExtension/Input/Utils/LeaderSequenceMatcher.cs` (D1/D2: shift-aware append + Ordinal
  prefix set), `MyExtension/Input/InputHandler.cs` (D2: Ordinal leader dict; D5: `CloseWindow`),
  `MyExtension/Input/Utils/KeybindingConfig.cs` (D2: Ordinal bindings map),
  `MyExtension/Resources/default-keybindings.json` (D3/D4: lowercase migration + window
  bindings + remove save), `MyExtension/Package/Utils/Actions.cs` (D5: registry entry),
  `tools/harness/test-e2e.ps1` (D7: lowercase assertions + 3 scenario updates),
  `tests/NeoVisual.Tests/Program.cs` (D6), `docs/spec.md`, `AGENTS.md`,
  `.opencode/skills/vs-extension-dev/SKILL.md`, `docs/progress.md` (D8).
- **Created:** `MyExtension/Input/Utils/CloseWindowCommand.cs` (D5 pure seam).
- **Not touched:** `KeyNameBuilder.cs` (simple-shortcut format unchanged), `SimpleShortcutMatcher.cs`,
  `Telescope/**` (no Telescope type changes), `GlobalKeyboardHook.cs` (`IsKeyOfInterest` already
  passes any key while the leader is active — recon-verified).

## Open risks / uncertainty

1. **`|` key ambiguity (low).** `KeyNames.ToString` maps `OemPipe`→`"|"` shift-insensitively, so
   an unshifted `\` (OemPipe) also builds the sequence name `|`. A future `w,\` binding would
   collide with `w,|`. Acceptable now (no `\` binding exists); document in KeyNames.
2. **`IsToolWindow` staleness for `close-window` (medium).** `WindowManager.IsToolWindow` is the
   raw `SEID_WindowFrame` flag and can be stale right after a focus move (the FocusGuard's
   known issue). Worst case: `w,d` closes the wrong surface class once after a rapid
   tool-window→editor move. Mitigation: the pure seam makes the decision trivially re-tunable;
   if live testing shows staleness, feed `VimModeTracker.IsEditorFocused` into `For(...)`.
3. **User `%APPDATA%` override drift (low).** A user override file with uppercase leader keys
   stops matching after the case migration. Documented in the plan; the merge is
   case-sensitive so an override cannot silently shadow a default.
4. **VS command availability (low, researcher-verified HIGH confidence).** The three command
   names come from the compat-frozen `ShellCmdDef.vsct`; if a name were wrong,
   `ExecuteVsCommand` logs `Command '<name>' failed` and the key is swallowed — never a crash.
   The queued e2e gate proves the live behavior.
5. **Case-sensitive config map vs user muscle memory (low).** Existing users typing uppercase
   overrides in `%APPDATA%` lose those bindings. The defaults are migrated; the risk is
   user-config only.

## Build Plan

> **Lane: feature (unit-only, e2e deferred).** Aggregated (Stage 3) from the five Stage-2
> section artifacts — the authoritative full detail (ground-truth tables, per-test snippets,
> complete enumerations) lives in
> `.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-085937/artifacts/section-{a,b,c,d,e}.md`;
> every step below carries its complete executable contract (Files / Change / Verify-with /
> Fails-if). **No RED evidence exists yet** — Verify-with cites unit test names + diagnostic
> formats only; e2e scenarios are QUEUED, never executed. No new diagnostic literal anywhere
> (the existing `[NeoVisual] leader-binding executed: {sequence}` line is the contract; its
> sequence values become lowercase/case-sensitive).

### Execution order + transient-RED warning (binding for the build agent)

1. Execute **A → B → C → D → E** top-to-bottom in ONE pass. Section A's BP-A2 + Section B's
   BP-B4 must land in the same pass: between them alone, every live leader binding aborts
   (lowercase built sequence vs uppercase config keys) — the extension is leader-dead in that
   window. Unit-wise this is invisible until Section C's updates land.
2. **Transient RED is expected, not a regression.** The moment BP-A2 lands, these EXISTING
   `tests/NeoVisual.Tests` tests go RED (uppercase fixtures vs the now-case-sensitive matcher) —
   Section C owns their update; do NOT "fix" the matcher to make them pass:
   `Run_LeaderMatcher_SingleKeyBindingExecutes` (:2062), `_MultiKeySequence` (:2080),
   `_ThrowingActionIsCaught` (:2155), `_ActiveSequenceConsumesI` (:2177), `_PrefixSetBuiltOnce`
   (:2197); and when BP-A4 lands, `Run_Keybinding_CaseInsensitive` (:161). Exact failing
   assertions are tabulated in `artifacts/section-a.md` (Transient RED table).
3. **BP-B4 is ATOMIC** — remove `"W"` and add the three `w` bindings in ONE edit. An
   intermediate state with `W` still bound makes `w` both a complete binding and a prefix; the
   matcher executes the complete match first (LeaderSequenceMatcher.cs:72), so Space+W would
   fire SAVE and every `w,*` sequence would be unreachable.
4. Do NOT run the full unit suite mid-section — it is only meaningful after Section C lands.
   Per-filter mid-point checks are specified in the phases below.
5. **Never run the e2e harness against VS** (user mandate 2026-10-04). The only allowed
   harness invocations are the no-VS `-SelfCheck` and `-List`.

### Phase 1 — Case-sensitive leader matcher (BP-A1 … BP-A5)

**BP-A1 — `KeyNames`: `OemPipe`→`"|"` case + shift-aware two-arg overload.**
- **Files:** `MyExtension/Input/Utils/KeyNames.cs`
- **Change:** (1) in the single-arg `ToString(Keys)` switch insert after the `OemMinus` case:
  `case Keys.OemPipe: return "|";  // 220 (0xDC); Shift+OemPipe types '|'`.
  (2) add the two-arg overload after the single-arg method:
  ```csharp
  public static string ToString(Keys key, bool shift)
  {
      if (key >= Keys.A && key <= Keys.Z)
      {
          char lower = (char)('a' + (key - Keys.A));
          return shift ? char.ToUpperInvariant(lower).ToString() : lower.ToString();
      }
      return ToString(key);   // non-letters: printable mapping, shift-insensitive
  }
  ```
  Do NOT touch `KeyNameBuilder.cs` (it calls the single-arg form — the simple `Ctrl+H`
  contract is structurally unaffected).
- **Verify-with:** `Run_KeyNames_CaseEncodesShift` (new, C): `ToString(Keys.F,false)=="f"`,
  `ToString(Keys.F,true)=="F"`, `ToString(Keys.OemMinus,true)=="-"`; `Run_KeyNames_PrintableMappings`
  (extend, C): `ToString(Keys.OemPipe)=="|"`; `Run_KeyNames_RoundTrip_WindowPrefix` (new, C).
  Diagnostic: `[NeoVisual] leader-binding executed: w,|` (not `w,OemPipe`).
- **Fails-if:** `ToString(Keys.OemPipe)` returns `"OemPipe"` → the `w,|` binding can never
  match; `ToString(Keys.F,true)` returns `"f"` → letters do not encode Shift; the overload is
  missing → BP-A2 does not compile (CS1501).

**BP-A2 — `LeaderSequenceMatcher`: shift-aware append + Ordinal prefix set.**
- **Files:** `MyExtension/Input/Utils/LeaderSequenceMatcher.cs`
- **Change:** (1) L68 `_sequenceBuilder.Append(KeyNames.ToString(key));` →
  `_sequenceBuilder.Append(KeyNames.ToString(key, shift));` (the leader-key guard at L48
  already requires `!shift` to START a sequence — unchanged). (2) L116 `BuildPrefixSet`:
  `StringComparer.OrdinalIgnoreCase` → `StringComparer.Ordinal` (REQUIRED for correct
  case-sensitive abort: with only `s,g` bound, Shift+S must Abort, not Consume-and-hang).
  (3) doc example L110–112: `"F"`/`"F,F"` → `"f"`/`"f,f"`.
- **Verify-with:** `Run_LeaderSequenceMatcher_CaseSensitive` (new, C): `s,g` vs `s,G` fire
  different actions; `Run_KeyNames_RoundTrip_WindowPrefix`; `Run_LeaderSequenceMatcher_PrefixWaits`.
  Diagnostic: `[NeoVisual] leader-binding executed: s,G` vs `s,g` (AC5).
- **Fails-if:** `Space S Shift+G` and `Space S g` produce the SAME sequence →
  `Run_LeaderSequenceMatcher_CaseSensitive` sees one action; Shift+S with only `s,g` bound
  returns `Consume` instead of `Abort` (prefix set still IgnoreCase) → the sequence hangs.

**BP-A3 — `InputHandler.BuildBindings`: leader dict → Ordinal (simple dict unchanged).**
- **Files:** `MyExtension/Input/InputHandler.cs` — ONLY inside `BuildBindings` (L166–191)
- **Change:** L168 leader dict `StringComparer.OrdinalIgnoreCase` → `StringComparer.Ordinal`;
  L169 simple dict stays `OrdinalIgnoreCase` (UNCHANGED — `KeyNameBuilder`'s `Ctrl+H` format
  is case-canonical, AC7); extend the method's XML doc with the case-sensitivity sentence.
- **Verify-with:** `Run_Keybinding_DefaultFileHasWindowManagement` (needs BP-B4);
  `Run_KeybindingConfig_IsSimpleShortcut` stays GREEN. Diagnostics:
  `[NeoVisual] leader-binding executed: f,t` (lowercase JSON + Ordinal dict);
  `[NeoVisual] shortcut-binding executed: Ctrl+H` unregressed.
- **Fails-if:** `Space f t` logs nothing and falls through (dict case-folded wrong or the
  binding classified simple); `Ctrl+H` navigation stops firing (simple dict accidentally
  Ordinal).

**BP-A4 — `KeybindingConfig.Merge`: bindings map → Ordinal.**
- **Files:** `MyExtension/Input/Utils/KeybindingConfig.cs` — ONLY the `Merge` map comparer
  (L135) + Merge's XML doc
- **Change:** L135 `StringComparer.OrdinalIgnoreCase` → `StringComparer.Ordinal`; update the
  XML doc: case-sensitive map, `s,g`/`s,G` coexist, a user `%APPDATA%` override must match the
  default's case to override/unbind it (case-mismatched `null` no longer removes a default).
- **Verify-with:** `Run_Keybinding_CaseSensitive` (replaces `Run_Keybinding_CaseInsensitive`):
  `{"s,g":"a","s,G":"b"}` → 2 distinct entries; `"W"` ≠ `"w"`. `Run_Keybinding_BindingsParsed`
  + `Run_Keybinding_NullUnbinds` stay GREEN.
- **Fails-if:** the two distinct-case keys collapse to 1 entry; `{"W":...}` suddenly yields
  key `w` (comparer inverted).

**BP-A5 — Comment-only stale-example refresh (hub-added; zero behavior).**
- **Files:** `MyExtension/Input/InputHandler.cs` (field comment L113; `ResolveAction` doc
  example ~L197 `<c>w</c> -> save`), `MyExtension/Input/Utils/KeybindingConfig.cs` (class doc
  format example L42 + example block L47–50)
- **Change:** update the stale uppercase examples to the post-migration notation —
  InputHandler L113 `"W", "F,F", "B,D"` → `"w", "f,f", "b,d"`; InputHandler ~L197
  `(<c>w</c> -> save, etc.)` → `(<c>w,-</c> -> split below, etc.)`; KeybindingConfig L42
  `"F,F":   "command:Edit.GoToFile",` → `"f,f":   "command:Edit.GoToFile",`; KeybindingConfig
  L47–50 `"F,F"` → `"f,f"` (and any `"W"` example → `"w"`). Comment/doc text ONLY.
- **Verify-with:** none (comment-only; no behavior, no test). `dotnet build` stays 0-error.
- **Fails-if:** n/a (a build break here means a code line was touched by mistake — revert).

### Phase 2 — Binding table + close-window action (BP-B1 … BP-B4)

**BP-B1 — Create the pure `CloseWindowCommand` seam.**
- **Files:** `MyExtension/Input/Utils/CloseWindowCommand.cs` (NEW)
- **Change:** exactly (block-scoped `namespace MyExtension.Input` per folder convention —
  verified: KeybindingConfig.cs:9, KeyNames.cs:3, LeaderSequenceMatcher.cs:7):
  ```csharp
  namespace MyExtension.Input
  {
      /// <summary>
      /// Pure focus-aware close-window decision (the OverlayKeyHandler pattern: a dependency-free
      /// static seam the VS-coupled caller delegates to, so the choice stays unit-testable). There
      /// is no single VS "close whatever is active" command: a focused tool window closes via
      /// <c>Window.CloseToolWindow</c>, anything else (editor/document) via
      /// <c>Window.CloseDocumentWindow</c>.
      /// </summary>
      internal static class CloseWindowCommand
      {
          /// <summary>Returns the native VS command name that closes the focused surface class.</summary>
          internal static string For(bool isToolWindow)
              => isToolWindow ? "Window.CloseToolWindow" : "Window.CloseDocumentWindow";
      }
  }
  ```
- **Verify-with:** `Run_CloseWindowCommand_For` (new, C): `For(true)=="Window.CloseToolWindow"`,
  `For(false)=="Window.CloseDocumentWindow"`. No diagnostic by design (pure seam).
- **Fails-if:** CS0246 from InputHandler/Actions → wrong namespace (`MyExtension.Input.Utils`);
  the test fails → ternary arms swapped or a command name misspelled.

**BP-B2 — `InputHandler.CloseWindow()` (focus-aware action method).**
- **Files:** `MyExtension/Input/InputHandler.cs`
- **Change:** insert after `ToggleSolutionExplorer()`'s closing brace (L556), before the class
  closing brace:
  ```csharp
  /// <summary>
  /// Focus-aware "delete window": closes the active tool window when a tool window holds
  /// focus, else the active document. Runs the native VS command by name via DTE; the
  /// surface choice is the pure <see cref="CloseWindowCommand"/> seam.
  /// </summary>
  internal void CloseWindow()
  {
      ThreadHelper.ThrowIfNotOnUIThread();
      ExecuteVsCommand(CloseWindowCommand.For(_windowManager.IsToolWindow));
  }
  ```
  Also (cosmetic, same file): class doc L22–23 stale example `e.g. <c>w</c> → save, <c>f f</c> →
  Go To File` → `e.g. <c>f,f</c> → Go To File`.
- **Verify-with:** the EXISTING diagnostic pair downstream: `[NeoVisual] leader-binding
  executed: w,d` (L382) then the native command runs; on a VS fault the EXISTING
  `[NeoVisual] Command 'Window.CloseToolWindow' failed: {msg}` (or `...CloseDocumentWindow...`,
  or `... failed: DTE unavailable`). Unit coverage is indirect by design (the seam + registry
  are tested; the method stays untested like `ToggleSolutionExplorer`).
- **Fails-if:** CS1061 `CloseWindow` not found from Actions.cs (BP-B3 depends on this);
  `w,d` fires but nothing closes and NO `Command ... failed` line → check the
  `DTE unavailable` path; the WRONG surface closes → the `For(...)` argument is inverted.

**BP-B3 — Register the `close-window` built-in action.**
- **Files:** `MyExtension/Package/Utils/Actions.cs`
- **Change:** in `BuildRegistry()`'s initializer, insert one line immediately after the
  `["toggle-solution-explorer"]` entry (L27): `["close-window"] = (h, _) => () => h.CloseWindow(),`.
  Registry 11 → 12. Do NOT touch the `FinderNames` loop.
- **Verify-with:** `Run_ActionsRegistry_ContainsAllBuiltins` (C updates): count 12 + contains
  `close-window`. Transitive: `Run_Keybinding_DefaultFileHasWindowManagement` only passes when
  this exists — else the EXISTING load line `[NeoVisual] Unknown action 'close-window' for
  binding 'w,d' - ignored.` appears (InputHandler.cs:176).
- **Fails-if:** CS1061 (BP-B2 skipped); the count test still sees 11; the Unknown-action load
  line appears (registry key ≠ JSON value).

**BP-B4 — Rewrite `default-keybindings.json` (ATOMIC: lowercase migration + remove `W` + add the `w` group).**
- **Files:** `MyExtension/Resources/default-keybindings.json` (full-file rewrite, ONE edit)
- **Change:** replace the file content with EXACTLY:
  ```json
  {
    "leader": "Space",
    "bindings": {
      "Ctrl+H": "navigate-left",
      "Ctrl+L": "navigate-right",
      "Ctrl+K": "navigate-up",
      "Ctrl+J": "navigate-down",

      "w,-": "command:Window.NewHorizontalTabGroup",
      "w,|": "command:Window.NewVerticalTabGroup",
      "w,d": "close-window",
      "b,d": "command:File.Close",
      "q": "command:File.Exit",
      "e": "toggle-solution-explorer",
      "f,f": "command:Edit.GoToFile",
      "f,t": "telescope",
      "f,d": "telescope-issues",
      "f,g": "telescope-grep",
      "f,z": "telescope-fzf",
      "f,b": "command:Window.NavigateTo",
      "f,r": "telescope-references",
      "f,i": "telescope-implementation",
      "c,w": "command:View.CommandWindow",
      "c,a": "command:View.QuickActions",
      "c,r": "command:Refactor.Rename",
      "/": "command:Edit.Find",
      "c,f": "command:Edit.FormatDocument",
      "s,s": "command:Edit.NavigateTo",
      "g,g": "command:View.GitChanges",
      "g,b": "command:Team.Git.Branches",
      "g,c": "command:Team.Git.Commit",
      "t,t": "command:View.Terminal",
      "b,b": "command:Build.BuildSolution",
      "b,r": "command:Debug.Start"
    }
  }
  ```
  Complete migration ledger (all 24 current leader keys — verified, nothing missed):
  `B,D`→`b,d`; **`W` REMOVED**; `Q`→`q`; `E`→`e`; `F,F`→`f,f`; `F,T`→`f,t`; `F,D`→`f,d`;
  `F,G`→`f,g`; `F,Z`→`f,z`; `F,B`→`f,b`; `F,R`→`f,r`; `F,I`→`f,i`; `C,W`→`c,w`; `C,A`→`c,a`;
  `C,R`→`c,r`; `/` UNCHANGED; `C,F`→`c,f`; `S,S`→`s,s`; `G,G`→`g,g`; `G,B`→`g,b`; `G,C`→`g,c`;
  `T,T`→`t,t`; `B,B`→`b,b`; `B,R`→`b,r`. Plus the three NEW `w` bindings. The 4 simple
  shortcuts (`Ctrl+H/L/K/J`) UNTOUCHED. Binding count 28 → **30**. No trailing commas.
  `c,w` note: `w` is now also a prefix character, but the matcher checks a COMPLETE match
  before the prefix set (LeaderSequenceMatcher.cs:72→87) and `c,w` prefixes nothing — it
  still executes.
- **Verify-with:** `Run_Keybinding_DefaultFileHasWindowManagement` (3 ContainsKey + 3 values +
  no `w` key); `Run_Keybinding_DefaultFileHasTelescopeAndNav` (updated, `f,t` + the exhaustive
  lowercase loop); `Run_Keybinding_CaseSensitive`. Diagnostics: EXISTING
  `[NeoVisual] Keybindings loaded: 30 binding(s), leader = Space (user file: none)` (the count
  30 is a deterministic post-edit check); `[NeoVisual] leader-binding executed: w,-` / `w,|` /
  `w,d`; AC4 absence: NO `leader-binding executed: w` for a lone Space+W.
- **Fails-if:** the load line still reads `28 binding(s)` (W not removed / new lines missing);
  a count OTHER than 28/30 or `Failed to load built-in keybindings` (JSON syntax error);
  the Unknown-action line (BP-B2/BP-B3 missing); an UPPERCASE sequence diagnostic after
  Section A (a leader key left uppercase — re-check the 24-row ledger); Space+W fires SAVE
  (`W` still present — breaks the whole `w` group).

### Phase 3 — Unit tests (BP-C1 … BP-C25)

> **Files: ONLY `tests/NeoVisual.Tests/Program.cs`.** Full per-test snippets with exact
> current text: `artifacts/section-c.md`. Suite arithmetic: 171 − 1 replaced + 6 new =
> **177 expected, 0 failed** (BP-C25 pins it). Two tests are COMPILE-ERROR RED until
> Sections A/B land (`Run_KeyNames_CaseEncodesShift` CS1501, `Run_CloseWindowCommand_For`
> CS0246) — add them in place; they compile once A/B are in.

- **BP-C1** — REPLACE `Run_Keybinding_CaseInsensitive` (:161) with `Run_Keybinding_CaseSensitive`:
  `{"W":"command:X"}` does NOT contain `w` (case-sensitive) + `{"s,g":"a","s,G":"b"}` yields
  TWO distinct entries. RED today: `leader binding lookup is case-sensitive (W != w)`.
- **BP-C2** — NEW `Run_Keybinding_DefaultFileHasWindowManagement` (after :193): `LoadDefaults()`
  contains `w,-`→`command:Window.NewHorizontalTabGroup`, `w,|`→`command:Window.NewVerticalTabGroup`,
  `w,d`→`close-window`; `Assert.False(ContainsKey("w"))`. RED: `w,- -> split-below binding present`.
  Depends on BP-B4.
- **BP-C3** — UPDATE `Run_Keybinding_DefaultFileHasTelescopeAndNav` (:181): `"F,T"`→`"f,t"` (:187)
  + insert an exhaustive loop asserting EVERY non-simple leader key is lowercase (Ordinal
  compare). RED: `leader binding 'B,D' must be lowercase after the migration (D3)`.
- **BP-C4/C5/C6** — GREEN-preserving fixture migrations: `Run_Keybinding_BindingsParsed`
  (`"W"`→`"w"`, :155/:157), `Run_Keybinding_NullUnbinds` (`"Q"`/`"W"`→`"q"`/`"w"`, :170–172),
  `Run_KeybindingConfig_IsSimpleShortcut` (`"F,+"`/`"W"`→`"f,+"`/`"w"`, :206–207). No RED.
- **BP-C7** — EXTEND `Run_KeyNames_PrintableMappings` (:215): add
  `Assert.Equal("|", KeyNames.ToString(Keys.OemPipe));` after :222. KEEP :223
  `Assert.Equal("F", KeyNames.ToString(Keys.F));` UNCHANGED (simple-shortcut contract).
  RED: `Expected [|] but got [OemPipe]`.
- **BP-C8** — NEW `Run_KeyNames_CaseEncodesShift` (after :224): `ToString(Keys.F,false)=="f"`,
  `(Keys.F,true)=="F"`, `(Keys.G,false)=="g"`, `(Keys.G,true)=="G"`,
  `(Keys.OemMinus,false)=="-"`, `(Keys.OemMinus,true)=="-"`. RED: compile error CS1501.
- **BP-C9** — NEW `Run_KeyNames_RoundTrip_WindowPrefix` (after :244): an Ordinal fixture bound
  to `w,-`/`w,|`/`w,d`; Space→Consume, W→Consume, OemMinus→Execute `"w,-"`;
  fresh matcher Space,W,OemPipe(shift:true)→Execute `"w,|"`; Space,W,Keys.D→Execute `"w,d"`;
  `Assert.Equal(3, executed)`. RED: `Expected [Execute] but got [Abort]` (the `w,-` path —
  today the matcher builds `"W,-"`, which misses the Ordinal fixture binding AND is not in
  today's IgnoreCase prefix set, which holds only the proper prefix `"w"` → Abort).
- **BP-C10** — NEW `Run_LeaderSequenceMatcher_CaseSensitive` (after :2219): Ordinal fixture
  `{"s,g","s,G"}`; Space,S,g→Execute `"s,g"` (executedLower=1, executedUpper=0); fresh matcher
  Space,S,Shift+G→Execute `"s,G"`. RED: `Expected [Execute] but got [Abort]` (today the
  matcher builds `"S,g"`, which misses the Ordinal fixture bindings and is not a proper
  prefix → Abort).
- **BP-C11** — NEW `Run_LeaderSequenceMatcher_PrefixWaits` (after BP-C10): with only `w,-`
  bound, Space+W → `Consume`, `IsActive` true, nothing fires; a following `X` → `Abort`.
  **GREEN today — a regression PIN for AC4, NOT RED evidence.**
- **BP-C12** — Common transformation for the TEN `Run_LeaderMatcher_*` fixtures (:2032–2219):
  fixture dict `OrdinalIgnoreCase`→`Ordinal`; binding keys lowercase (`"F"`→`"f"`,
  `"F,F"`→`"f,f"`, `"I,F"`→`"i,f"`); expected sequence strings lowercase.
- **BP-C13…BP-C22** — the ten per-test applications (exact lines in section-c.md):
  `_LeaderKeyStartsSequence`(:2032), `_LeaderKeyWhileTypingPassesThrough`(:2047),
  `_SingleKeyBindingExecutes`(:2062, RED `Expected [Execute] but got [Abort]`),
  `_MultiKeySequence`(:2080, RED `Expected [Execute] but got [Abort]`), `_UnknownSequenceAborts`(:2101),
  `_ResetClearsState`(:2118), `_NonLeaderKeyPassesThrough`(:2139),
  `_ThrowingActionIsCaught`(:2155, RED `Expected [Failed] but got [Abort]`),
  `_ActiveSequenceConsumesI`(:2177), `_PrefixSetBuiltOnce`(:2197, RED `Expected [Execute] but got [Abort]`).
- **BP-C23** — UPDATE `Run_ActionsRegistry_ContainsAllBuiltins` (:1661): count 11→12, add
  `"close-window"` to the names array. RED: `Expected [12] but got [11]`.
- **BP-C24** — NEW `Run_CloseWindowCommand_For` (after :1714, new section banner):
  `For(true)=="Window.CloseToolWindow"`, `For(false)=="Window.CloseDocumentWindow"`.
  RED: compile error CS0246.
- **BP-C25** — GATE: `dotnet run --project tests/NeoVisual.Tests` → `177 passed, 0 failed,
  177 total.`; `--list` contains the 7 new/renamed names and NO `Run_Keybinding_CaseInsensitive`;
  sanity `dotnet run --project tests/Telescope.Tests` → `172 passed, 0 failed` (staggered —
  never simultaneous with NeoVisual, W11 policy).

**Deliberately NOT updated (the verifier must not misflag):** `Run_KeyNames_RoundTrip_LeaderSequence`
(binds `/`, non-letter), `Run_KeyNames_RoundTrip_SimpleShortcut`, `Run_SimpleShortcutMatcher_CaseInsensitiveMatch`
(simple path PRESERVED — do NOT flip), `Run_ActionsRegistry_CaseInsensitive` (action-NAME lookup,
untouched), `Run_SimpleShortcutMatcher_PrintableKeys`, `Run_KeyNameBuilder_*`, the leader-key
tests with no sequence fixtures.

### Phase 4 — Harness scenario updates (BP-D1 … BP-D7)

> **Files: ONLY `tools/harness/test-e2e.ps1`.** `harness-common.ps1` NOT touched (its
> `Send-Text` already maps `|`→`@(0xDC,$true)` :116 and `-`→`@(0xBD,$false)` :119). Full
> before/after snippets: `artifacts/section-d.md`. E2E is DEFERRED — the edits are specified,
> never executed; the only allowed gates are no-VS `-SelfCheck` + `-List` (BP-D7).

- **BP-D1** — Header comment (:27): `neovisual-leader` description → `Space w - + Space+E fire
  leader bindings (leader-binding executed: w,- / e)`; insert a `neovisual-window-management`
  line after it. Doc-only (the m63 self-check reads `-TotalCount 60` and only requires the
  ordering/self-seed text — lines 46–59 keep it).
- **BP-D2** — `neovisual-toolwindow` (:710): `leader-binding executed: E` → `... executed: e`.
- **BP-D3** — REWORK `neovisual-leader` (:685–697): drop the Space+W save block; add
  `Send-Tap $script:VkW` (prefix, consumes) + `Send-Text '-'` → assert
  `[NeoVisual] leader-binding executed: w,-`; keep Space+E → assert `... executed: e`.
  Grounding: `Send-Text '-'`→VK 0xBD unshifted→`"-"`→sequence `w,-`.
- **BP-D4** — REGISTER the NEW `neovisual-window-management` scenario (insert between :697 and
  :699; SPECIFIED, never executed): (1) Space w `-` → assert `leader-binding executed: w,-`;
  (2) Space w `|` (`Send-Text '|'`→0xDC+shift) → assert `leader-binding executed: w,\|`
  (escaped pipe in the regex); (3) Space w `d` with an editor focused → assert
  `leader-binding executed: w,d` + bounded-poll (3s) that `Get-ActiveDocumentPath $vs.Id`
  (EXISTING helper, test-e2e.ps1:248) changed; (4) `Ensure-SolutionExplorerOpen` then Space w
  `d` → assert `leader-binding executed: w,d` (tool-window close). Tab-group GEOMETRY is
  deliberately never asserted (VS moves the active tab rather than duplicating it). Writes NO
  seed file; does not open Gamma.cs/Beta.cs (both reserved); registered after
  `neovisual-leader` so `seed-leak` stays last.
- **BP-D5** — REWORK `neovisual-explorer-move-editor-focus` (:876 comment, :908–910 comment,
  :913–915 keys): the positive focus bound Space+W → **Space+B,D** (`b,d` → `command:File.Close`):
  `Send-Tap 0x42` (b — no `$script:VkB` exists in harness-common; literal VK per the file's
  established pattern) + `Send-Tap $script:VkD` → assert `[NeoVisual] leader-binding executed:
  b,d`. Rationale: `b,d` is quiet (no dialog, no tool-window focus change) and doubles as
  cleanup (closes the scenario's Gamma.cs). Rejected: `e` (toggles SE mid-scenario), `q`
  (kills VS), `c,f` (rewrites the Gamma.cs seed → seed-leak failure), `b,r` (Debug.Start),
  `f,*` (modal overlay left open). The absence scan (:920–929) UNCHANGED.
- **BP-D6** — REWORK `neovisual-editor-insert` (:1126–1132 save block, :1136 comment): the save
  step Space+W → `Send-Ctrl 0x53` (Ctrl+S → VS native `File.SaveSelectedItems`, passed through
  unbound); the `leader-binding executed: W` assertion is DROPPED — the file-content check
  (:1140–1146) is the only oracle. Everything else in the scenario UNCHANGED (the marker still
  contains a literal Space typed in INSERT mode).
- **BP-D7** — No-VS gate for the section: `pwsh tools/harness/test-e2e.ps1 -SelfCheck` PASS
  (parse + helper invariants + `Assert-SeedConsistent`), then
  `pwsh tools/harness/test-e2e.ps1 -List` → **38 scenarios** incl.
  `neovisual-window-management`. The LIVE gate is QUEUED (E2E-GAP1-1..5) — the build agent
  must NOT run it.

### Phase 5 — Docs sync + e2e queue (BP-E1 … BP-E7)

> **Files: ONLY `docs/spec.md`, `AGENTS.md`, `.opencode/skills/vs-extension-dev/SKILL.md`,
> `docs/progress.md`, `.opencode/workspaces/neovim-planning-hub/e2e-queue.md`.** Full
> before/after text for every edit: `artifacts/section-e.md`. `<ACTUAL>` = the NeoVisual
> count printed by the real suite run (expected **177**; never write the estimate).
> Phase-1 precondition: Sections A–D built, `dotnet build` 0-error, both unit suites pass
> staggered, `-List` shows 38.

- **BP-E1** — `docs/spec.md` §3+§7: (a) the simple/leader bullet (:180–181) → case-sensitivity
  sentence (leader sequences case-sensitive, capital = Shift+letter, `s,g` ≠ `s,G`; simple
  case-insensitive); (b) the ResolveAction list (:182–185) gains `close-window`; (c) the
  built-in-defaults sentence (:190–196) → the lowercase `w`-prefix list + the no-save note
  ("There is no save binding (save with Ctrl+S); `w` is a window-management prefix — a lone
  `Space+w` consumes and waits, firing nothing."); (d) §7 leader-prose normalization — 7
  single-token replacements (`Space+E`→`Space+e` :380; `Space+F D/R/G/I/Z/T`→`Space+f,d/r/g/i/z/t`
  :396/398/401/404/408/414).
- **BP-E2** — `docs/spec.md` §5/§8: (a) NeoVisual count :265 → `<ACTUAL> tests`; (b) coverage
  tail :276–277 gains "and the close-window seam (`CloseWindowCommand`)"; (c) :291 comment
  `# all 37 scenarios` → `# all 38 scenarios`; (d) :296–297 prose → `The **38 registered
  scenarios** (37 GREEN with no known-RED — `neovisual-window-management` is registered but
  never executed, queued as E2E-GAP1-1; …)`; (e) :304 list gains `neovisual-window-management`
  after `neovisual-leader`; (f) :422 → `38 registered — 37 GREEN + neovisual-window-management
  queued unexecuted`. Telescope 172 UNTOUCHED (:252, :420).
- **BP-E3** — `AGENTS.md`: (a) keybindings bullet :367–369 — the `w`→save example →
  `w,-`→`Window.NewHorizontalTabGroup` + the case-sensitivity sentence; (b) :163
  `neovisual-leader` line → `Space+E and Space w - fire leader bindings (lowercase sequences;
  a lone Space+W consumes and waits — no binding fires)`; (c) NEW scenario bullet after (b):
  `neovisual-window-management` (registered, never executed — queued as E2E-GAP1-1); (d) :137–139
  scenario header → `38 registered — 37 GREEN …`; (e) :131 comment → `# all 38 scenarios`;
  (f) :115 unit count → `<ACTUAL> tests, all passing`; (g) :110–111 coverage tail gains
  `CloseWindowCommand`; (h) leader-prose normalization — 8 single-token replacements
  (:258 `Space+E`→`Space+e`; :281 `Space+C W`→`Space+c,w`; :290/297/310/319/329–330
  `Space+F D/R/G/I/Z`→lowercase; :372 `Space+E`→`Space+e`). Scenario-list prose (:140–176)
  UNTOUCHED except :163 + the new bullet (they describe physical keystrokes).
- **BP-E4** — `SKILL.md`: (a) "Adding a key binding" :168–174 — examples `W`,`F,F`→`w`,`f,f` +
  the case-sensitivity sentence + `close-window` in the ResolveAction list; (b) :14 → `38
  registered live E2E scenarios (37 GREEN + neovisual-window-management queued unexecuted…)`;
  (c) :246–248 NeoVisual count → `<ACTUAL>`; (d) :249 → `38 registered — 37 GREEN + …`.
- **BP-E5** — `.opencode/workspaces/neovim-planning-hub/e2e-queue.md`: READ-MERGE-WRITE append
  of rows **E2E-GAP1-1..5** (status QUEUED) between the last row (`E2E-CR51-10`) and
  `## Rules`; idempotence guard first (no `E2E-GAP1-*` row may already exist); existing rows
  byte-identical; pipes inside cells escaped (`\|`). Exact row text: `artifacts/section-e.md`
  BP-5. The rows assert: GAP1-1 the new scenario (`w,-`/`w,|`/`w,d` ×2 + active-doc change);
  GAP1-2 `neovisual-leader` (`e` + `w,-`); GAP1-3 `neovisual-editor-insert` (Ctrl+S,
  file-content oracle); GAP1-4 `neovisual-explorer-move-editor-focus` (`b,d` + absence);
  GAP1-5 full-suite re-run (38).
- **BP-E6** — `docs/progress.md` (GREEN-gated — runs ONLY after the verification-agent passes):
  (a) pending-queue item 6 (:450–453) → strikethrough + **DONE** with the shipped summary +
  `e2e gates QUEUED: E2E-GAP1-1..5`; (b) run-order line (:317–318) → `~~gap 1~~ (DONE GREEN
  <date>) → **gap 3 (next)**`. NOTHING else in progress.md (Done section, Baseline, Decisions,
  SESSION HANDOFF, Current state, In-progress, backlogs, items 0–5/7–15 are hub-owned at
  handoff). DOC-66-2 note: if the hub rewrites the "Next up" bullet it MUST keep one of the
  lint-required tokens (`51-findings`|`72 findings`|`Code review fixes`|`combined plan`|
  `Functional restructure`|`F13`).
- **BP-E7** — Final no-VS gate: (1) `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved
  (resolves `CloseWindowCommand` against BP-B1's file); (2) `pwsh tools/lint/check-doc-content.ps1`
  → PASS (12 assertions; BP-E5 appends to the WORKSPACE queue, not `docs/e2e-queue.md`, so
  DOC-64-1's zero-`## E2E-` assertion is unaffected); (3) stale-text sweep — all 0 hits:
  `Space\+W` in spec.md+AGENTS.md; `Space\+F [DRGZIT]` in both; `` w`→save `` in AGENTS.md;
  `` `W`, `F,F` `` in spec.md+SKILL.md; `37 scenarios|37 total|37 live` in all three;
  (4) `-List` → 38 scenarios.

### Phase 6 — Build + unit gate (BP-G1)

- **Files:** none (verification only).
- **Change:** `dotnet build MyExtension.slnx` (0 errors); `dotnet run --project
  tests/NeoVisual.Tests` → **177 passed, 0 failed**; `dotnet run --project
  tests/Telescope.Tests` → **172 passed, 0 failed** (STAGGERED — never simultaneous, W11
  policy); then BP-D7's `-SelfCheck` + `-List` and BP-E7's lints + sweep.
- **Verify-with:** all of the above green. NO e2e run (deferred — E2E-GAP1-1..5).
- **Fails-if:** any unit failure (map through the Verification Trace); a build error; `-List`
  ≠ 38; a lint failure.

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_KeyNames_PrintableMappings` (extend) | BP-A1, BP-C7 | RED `Expected [|] but got [OemPipe]` → GREEN `ToString(OemPipe)=="|"` |
| `Run_KeyNames_CaseEncodesShift` (new) | BP-A1, BP-C8 | RED compile CS1501 → GREEN `f`/`F`/`g`/`G`/`-` |
| `Run_KeyNames_RoundTrip_WindowPrefix` (new) | BP-A1, BP-A2, BP-C9 | RED `Expected [Execute] but got [Abort]` → GREEN `w,-`/`w,|`/`w,d` sequences |
| `Run_LeaderSequenceMatcher_CaseSensitive` (new) | BP-A1, BP-A2, BP-C10 | RED `Expected [Execute] but got [Abort]` → GREEN `s,g` ≠ `s,G`, different actions |
| `Run_LeaderSequenceMatcher_PrefixWaits` (new) | BP-C11 | **no RED — GREEN pin** (AC4 matcher half); regression guard only |
| `Run_Keybinding_CaseSensitive` (replaces CaseInsensitive) | BP-A4, BP-C1 | RED `leader binding lookup is case-sensitive (W != w)` → GREEN 2 distinct `s,g`/`s,G` entries |
| `Run_Keybinding_DefaultFileHasWindowManagement` (new) | BP-B4, BP-A3, BP-A4, BP-C2 | RED `w,- -> split-below binding present` → GREEN 3 bindings + no `w` key |
| `Run_Keybinding_DefaultFileHasTelescopeAndNav` (update) | BP-B4, BP-C3 | RED `leader binding 'B,D' must be lowercase` → GREEN `f,t` + exhaustive loop |
| `Run_LeaderMatcher_SingleKeyBindingExecutes` (transient RED at BP-A2) | BP-A2, BP-C12, BP-C15 | RED `Expected [Execute] but got [Abort]` → GREEN after the fixture migration |
| `Run_LeaderMatcher_MultiKeySequence` (transient RED) | BP-A2, BP-C12, BP-C16 | transient (post-BP-A2): `Expected [Consume] but got [Abort]` at the FIRST key (Ordinal prefix set misses the uppercase `"F"`); RED-today: `Expected [Execute] but got [Abort]` at the last key → GREEN after the fixture migration |
| `Run_LeaderMatcher_ThrowingActionIsCaught` (transient RED) | BP-A2, BP-C12, BP-C20 | RED `Expected [Failed] but got [Abort]` → GREEN |
| `Run_LeaderMatcher_PrefixSetBuiltOnce` (transient RED) | BP-A2, BP-C12, BP-C22 | transient (post-BP-A2): `Expected [Consume] but got [Abort]` at the FIRST key; RED-today: `Expected [Execute] but got [Abort]` → GREEN |
| `Run_CloseWindowCommand_For` (new) | BP-B1, BP-C24 | RED compile CS0246 → GREEN both command names |
| `Run_ActionsRegistry_ContainsAllBuiltins` (update) | BP-B3, BP-C23 | RED `Expected [12] but got [11]` → GREEN count 12 + `close-window` |
| Load-time diagnostic | BP-B4, BP-B3 | `[NeoVisual] Keybindings loaded: 30 binding(s), leader = Space ...`; NO `Unknown action 'close-window'` line |
| Runtime diagnostics (queued e2e) | BP-A1/A2, BP-B2/B4, BP-D3/D4/D5 | `[NeoVisual] leader-binding executed: w,-` / `w,\|` / `w,d` / `b,d` / `e` (E2E-GAP1-1..4) |
| AC4 absence (unit-pinned) | BP-B4, BP-C11 | no `leader-binding executed: w` for a lone Space+W; matcher Consume |
| e2e `neovisual-editor-insert` (queued, E2E-GAP1-3) | BP-D6 | file-content check finds the marker; NO leader assertion |
| e2e `neovisual-explorer-move-editor-focus` (queued, E2E-GAP1-4) | BP-D5 | `leader-binding executed: b,d`; absence of `solution-explorer move` |
| Full-suite count gate | BP-C25, BP-G1 | `177 passed, 0 failed` (NeoVisual); `172 passed, 0 failed` (Telescope, staggered) |
| No-VS harness gates | BP-D7, BP-G1 | `-SelfCheck` PASS; `-List` = 38 incl. `neovisual-window-management` |
| Lint + stale-text gates | BP-E7, BP-G1 | check-doc-refs 0 unresolved; check-doc-content PASS (12); all stale-text greps 0 hits |

**Known-RED allowlist: NONE.** Both unit suites GREEN today (172/171), all 37 e2e GREEN. The
only expected RED is the new/updated unit tests above before Sections A/B land, plus the
documented transient-RED set the moment BP-A2 lands (owned by Section C — not regressions).
The verification-agent must NOT flag: the transient-RED set, `Run_LeaderSequenceMatcher_PrefixWaits`
(GREEN pin, never RED), the queued-unexecuted e2e scenarios, or the simple-path tests listed as
deliberately unchanged.

## Hub handoff steps (Step 7, on user approval — hub-owned, not build-agent steps)

1. Write the assembled plan to `docs/implementation_plan.md`.
2. **Canonical-queue reconciliation:** append the E2E-GAP1-1..5 gates to `docs/e2e-queue.md`
   (the repo-canonical queue — currently EMPTY, 2026-10-04 policy: unit-only-lane items append
   gates THERE with Status QUEUED) as list items/table rows — NEVER as `## E2E-*` section
   headers (check-doc-content.ps1 DOC-64-1 asserts zero such headers). The workspace-queue
   rows (BP-E5) are the hub's tracking copy; both must exist.
3. `docs/progress.md` hub-owned edits: the new plan as the FIRST pending item with explicit
   unit-only-lane + defer-e2e instructions; the Done entry at GREEN; Baseline counts
   (NeoVisual → actual, scenarios → 38 registered); "Current state"/"Next up"/SESSION HANDOFF
   refresh (keep a DOC-66-2 token); Decisions entry recording the user's 2026-10-04 scope
   decisions (skip zoom/resize, remove `w` save, `w` window prefix, case-sensitive leader
   combos, focus-aware close-window).
4. Workspace `e2e-queue.md`: the QUEUED rows flip to READY only when this plan is GREEN in
   `docs/progress.md` (Step 8 reconciliation).
5. Residual drift noted for later items (not this plan): progress.md queue items 7–15 cite
   uppercase leader keys (`Leader+C+A`, `C,A`) — normalize when those items are planned;
   spec.md §2.2 / SKILL.md key-files tables gain a `CloseWindowCommand.cs` row (optional
   completeness; the doc-ref lint is already satisfied by BP-E2b/BP-E3g).
