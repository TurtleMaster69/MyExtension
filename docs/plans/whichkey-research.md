# Research — which-key popup for MyExtension (VSIX)

> **Status:** RESEARCH ONLY (2026-10-03). No production code, no Build Plan / BP-n steps.
> This document is the input to the **FEATURE-TRIAGE gate** (build vs extend/reuse vs skip);
> the user decides before any implementation loop starts.
>
> **Reference:** `which-key.nvim` (folke) as used by LazyVim. Sources: LazyVim keymaps page
> (`lazyvim.org/keymaps`), `folke/which-key.nvim` README + `doc/which-key.nvim.txt`, and the
> JetBrains `which-key-lazy` port (a close analog for a non-Neovim IDE).
>
> **Ground truth:** repo GREEN (Telescope `fzf` finder, 2026-10-03). Unit suites
> `tests/Telescope.Tests` 172 / `tests/NeoVisual.Tests` 171; live E2E 37 scenarios, no
> known-RED. All file:line evidence below was read from the current tree.

---

## 1. What which-key does (the LazyVim reference)

`which-key.nvim` "helps you remember your Neovim keymaps, by showing available keybindings
in a popup as you type." The exact UX to target:

- **Trigger:** after a *prefix* is pressed (e.g. `<leader>` = Space), a popup appears listing
  every continuation available under that prefix. Pressing `<leader>f` replaces the popup
  with the `f`-prefixed continuations, and so on down the key tree.
- **Content per row:** the next key (e.g. `f`, `t`, `d`) and a human-readable **description**
  (e.g. "Find File", "Telescope", "Diagnostics"). Groups are shown with a `+` prefix
  (`+buffer`, `+git`, `+search`) and can be expanded.
- **Delay:** the popup is shown after a short delay (default `delay = 200ms` for plugin
  mappings, `0` for built-in triggers) — it does not flash on every keystroke.
- **Dismissal:** the popup disappears when the sequence completes, is aborted, or times out
  (`timeoutlen`); **Hydra Mode** keeps it open until `<esc>`.
- **Modes:** works in normal/insert/visual/operator/terminal/command mode, each
  independently enable/disable-able. In practice it is a **normal-mode discovery aid** — it
  must never fire while the user is typing text.
- **Layouts:** `classic` (columns), `modern`, `helix`; sorted by local/order/group/alphanum.
- **Non-Neovim analog:** `which-key-lazy` for JetBrains/IdeaVim shows the same popup on
  leader press, with breadcrumb navigation and LazyVim group names — proof the pattern
  ports to a host IDE that owns its own key handling.

**Target UX for MyExtension (minimal, faithful):** pressing the leader (Space) shows a
popup of the top-level leader continuations (`B`, `W`, `Q`, `E`, `F`, `C`, `/`, `S`, `G`,
`T`); pressing `F` replaces it with the `F`-prefixed continuations (`F`, `T`, `D`, `G`, `Z`,
`B`, `R`, `I`); each row shows the key + a description. It hides on completion/abort/Escape.

---

## 2. How the extension's leader system works today

### 2.1 The state machine — `LeaderSequenceMatcher`

`MyExtension/Input/Utils/LeaderSequenceMatcher.cs` is the pure, dependency-free leader state
machine (extracted from `InputHandler` by F22, 2026-09-28):

- **Start:** `HandleKey` (`:44-58`) — if the key equals the leader key with no modifiers and
  the caller is not typing, set `_active = true`, clear the sequence builder, return
  `LeaderResult.Consume`. If typing, return `PassThrough` (Space types a literal space).
- **Build/match:** `:62-96` — append the key name (`KeyNames.ToString`) to a `StringBuilder`
  (comma-joined), then:
  - exact binding hit → `_active = false`, run the action, return `Execute(action, sequence)`
    (or `Failed(sequence, msg)` if the action throws);
  - else if the sequence is **not** in `_prefixSet` → `_active = false`, return `Abort`;
  - else → return `Consume` (still waiting for more keys).
- **Prefix set:** `BuildPrefixSet` (`:114-126`) precomputes every proper prefix of every
  binding at key boundaries — e.g. `"F"` for `"F,F"`. A sequence is a live prefix iff it is
  in this set. **This is exactly the "prefix with continuations" predicate which-key needs.**
- **Reset:** `Reset()` (`:103-107`) clears `_active` + the builder (called on Escape and on
  the Ctrl+N/P branch).

`LeaderResult`/`LeaderResultKind` (`:130-163`) carry `PassThrough | Consume | Execute |
Failed | Abort` plus the matched `Sequence` and `Action`.

### 2.2 The routing layer — `InputHandler`

`MyExtension/Input/InputHandler.cs`:

- `IsLeaderActive => _leaderMatcher.IsActive` (`:72`) — the volatile flag the hook pre-filter
  reads.
- `HandleKey` (`:264-403`) delegates the leader branch to `_leaderMatcher.HandleKey(key, ctrl,
  shift, alt, IsTyping())` (`:373`) and maps the result: `Consume` → swallow (`:379`),
  `Execute` → log `leader-binding executed: {seq}` (`:381`), `Failed` → log
  `leader-binding failed: {seq}: {msg}` (`:384`), `Abort` → pass through (`:387`).
- `IsTyping()` (`:494-501`) = `FocusGuard.IsTyping(IsToolWindow, controller.IsInputMode,
  EditorFocusedVeto, _vsVim.IsInTypingMode)` — the insert-mode gate.
- `IsKeyOfInterest` (`:411-459`) returns true while `IsLeaderActive` (so every key during a
  sequence marshals to the UI thread), for the leader key, Escape, modifier chords, and
  tool-window action keys.
- Bindings are split in `BuildBindings` (`:166-191`): keys containing `+` (Ctrl/Shift/Alt)
  go to `_simpleBindings`; everything else is a leader sequence in `_leaderBindings`.

### 2.3 The binding table — `KeybindingConfig` + `default-keybindings.json`

- `MyExtension/Input/Utils/KeybindingConfig.cs` loads the embedded
  `default-keybindings.json` and merges the optional `%APPDATA%\MyExtension\keybindings.json`
  (`Load` `:75-107`; `Merge` `:133-149`). `Bindings` is a
  `Dictionary<string,string>` (sequence → action name), case-insensitive. `IsSimpleShortcut`
  (`:157-166`) classifies a key as a modifier shortcut by its `Ctrl+`/`Shift+`/`Alt+` prefix.
  Test seams: `LoadFromJson` (`:114`) and `LoadDefaults` (`:122`).
- `MyExtension/Resources/default-keybindings.json` is the table. Leader sequences today:
  `B,D` close, `W` save, `Q` exit, `E` toggle-explorer, `F,F` GoToFile, `F,T` telescope,
  `F,D` issues, `F,G` grep, `F,Z` fzf, `F,B` NavigateTo, `F,R` references, `F,I`
  implementation, `C,W` Command Window, `C,A` QuickActions, `C,R` Rename, `/` Find, `C,F`
  Format, `S,S` NavigateTo, `G,G` GitChanges, `G,B` Git branches, `G,C` Git commit, `T,T`
  Terminal, `B,B` Build, `B,R` Debug. Simple shortcuts: `Ctrl+H/J/K/L` navigate.
- **Action labels:** the binding table stores only the *action name* (e.g.
  `command:File.SaveSelectedItems`, `telescope-grep`). There is **no description field** —
  which-key needs a human-readable label, so a label source must be added (see §4).

### 2.4 The hook pre-filter — `GlobalKeyboardHook`

`MyExtension/Hooks/GlobalKeyboardHook.cs`: `HookCallback` (`:86-151`) runs on the UI thread,
checks `IsVisualStudioFocused`, reads physical modifiers via `GetAsyncKeyState`, consults
`IsInteresting` → `InputHandler.IsKeyOfInterest` (`:159-160`), then calls `HandleKey` and
swallows on `true` (`:143-146`). The `InjectedKeyGuard` (`:110-113`) prevents injected keys
from re-entering. **Any which-key popup must not add per-key work here** — the popup is
driven from `InputHandler`'s leader branch, not the hook.

### 2.5 The modal-overlay precedent — `TelescopeOverlay`

`Telescope/Overlay/TelescopeOverlay.cs` is the existing WPF modal Window pattern a which-key
popup would reuse:

- A `Window` with `WindowStyle.None`, `Topmost`, `ShowActivated`, dark chrome, a title bar,
  a prompt `TextBox`, a results `TextBox`, and a preview `RichTextBox` (`:84-241`).
- **Modal + owned:** `ShowOverlayAsync` (`:252-321`) sets `Owner = Application.Current.MainWindow`
  (or the VS HWND), centers over the VS main-window rect, sets `IsOpen = true`, and defers
  `ShowDialog()` to `DispatcherPriority.ApplicationIdle` via `OverlayShowState` (so the
  global hook callback stays fast).
- **Closes on focus loss:** `Deactivated` → `CloseOverlay()` (`:233-240`) — a stale overlay
  can never swallow the next leader sequence.
- **Key routing:** `OnPreviewKeyDown` (`:544-597`) maps WPF keys to a normalized `OverlayKey`
  and delegates to the pure `OverlayKeyHandler` (`Telescope/Overlay/Utils/OverlayKeyHandler.cs`)
  — the "pure state machine the UI delegates to" pattern.
- **Controller:** `TelescopeController` (`Telescope/Controller/TelescopeController.cs`) owns a
  single overlay instance, exposes `IsOpen`, and builds a **fresh** overlay per open (a WPF
  Window cannot be re-shown after `Close()` — `:69-72`).

### 2.6 Insert-mode gating — `VimModeTracker`

`MyExtension/Vim/VimModeTracker.cs` is a shared MEF part (`IWpfTextViewCreationListener`,
`[ContentType("text")]`) tracking the focused editor's VsVim mode event-driven. `IsInTypingMode`
(`:69`) is a volatile read of the pure `VimModeState`; `IsEditorFocused` (`:76`) is the
event-driven focus flag. `InputHandler.IsTyping()` consumes both. **which-key must be gated on
the same `IsTyping()` predicate** so it never fires while the user types.

---

## 3. Where a which-key popup would hook

The natural hook is **inside `InputHandler.HandleKey`'s leader branch**, immediately after
`_leaderMatcher.HandleKey(...)` returns, because that is the only place that knows the
current sequence and its outcome:

| matcher outcome | meaning | which-key action |
|---|---|---|
| `Consume` **and** the sequence is a live prefix with continuations | a prefix was typed; more keys expected | **show/update** the popup for that prefix |
| `Consume` **and** the sequence is the empty prefix (just the leader) | leader pressed | **show** the top-level popup |
| `Execute` | a complete binding fired | **hide** the popup |
| `Failed` | binding fired but threw | **hide** the popup |
| `Abort` | no match, no prefix | **hide** the popup |
| `PassThrough` | not a leader key / typing | **hide** (defensive) |

The matcher already computes the prefix predicate (`_prefixSet.Contains(sequence)`), but it
does **not currently expose** the sequence or the "is a live prefix" fact on the `Consume`
result. Two clean options:

1. **Extend `LeaderResult`** with the current `Sequence` (and a `bool IsPrefix`) on the
   `Consume` result — a small, pure, unit-testable change to `LeaderSequenceMatcher`.
2. **Add a pure query method** `LeaderSequenceMatcher.GetContinuations(string prefix)` that
   returns the immediate next keys + full sequences under a prefix — the enumeration seam
   (§4). `InputHandler` calls it after a `Consume`.

Option 2 is preferred: it keeps the matcher's routing contract unchanged and puts the
enumeration logic in one pure, testable place. The popup is then driven entirely from
`InputHandler` (UI thread, already the hook's thread) — **no hook changes**.

**"Prefix with continuations" vs "complete binding" vs "no match":** the matcher's existing
three-way branch (`:72-95`) is exactly this distinction. A prefix is a sequence in
`_prefixSet`; a complete binding is a key in `_bindings`; anything else is `Abort`.

---

## 4. Enumerating continuations + action labels

### 4.1 Continuations from the binding table

Given a prefix `P` (e.g. `"F"`), the immediate continuations are the distinct next key
segments of every leader binding whose sequence starts with `P + ","` (or, for the empty
prefix, every leader binding's first segment). This is a pure function over
`_leaderBindings.Keys`:

```
GetContinuations(prefix):
  for each leader binding key K (split on ','):
    if prefix == "" : next = K[0]
    elif K starts with prefix + "," : next = K[prefixSegments]
    else skip
  group by `next`, keep the full sequence + action name
```

Example over the current table: prefix `""` → `B, W, Q, E, F, C, /, S, G, T`; prefix `"F"` →
`F, T, D, G, Z, B, R, I`; prefix `"F,F"` → complete binding (no continuations). This is
unit-testable with `KeybindingConfig.LoadDefaults()` — no VS needed.

### 4.2 Action labels (the missing piece)

The binding table has **no description**. Options, in order of preference:

1. **Add an optional `descriptions` map to `default-keybindings.json`** (and the user file),
   keyed by action name or by sequence, e.g. `"telescope-grep": "Grep"`,
   `"command:File.SaveSelectedItems": "Save"`. `KeybindingConfig` parses it into a
   `Dictionary<string,string>`; the popup falls back to the raw action name when absent.
   This is the LazyVim model (`desc = "..."` per mapping) and keeps labels user-overridable.
2. **Derive a label from the action name** — strip `command:`, take the last dotted segment
   (`File.SaveSelectedItems` → `SaveSelectedItems`), map the telescope action names via
   `TelescopeLauncher.FinderNames` (`telescope-grep` → `Grep`). Zero config, but ugly for
   `command:` entries.
3. **Hybrid:** ship a small built-in label table for the known actions + fall back to (2).
   This is the pragmatic default; (1) is the user-override layer.

`Actions.Registry` (`MyExtension/Package/Utils/Actions.cs`) and
`TelescopeLauncher.FinderNames` (`MyExtension/Package/Utils/TelescopeLauncher.cs:26-35`) are
the existing single sources of truth for action names; a label table should key off them.

---

## 5. How to render it — options and trade-offs

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. WPF popup (reuse Telescope overlay pattern)** | A small non-modal `Window` (or a `Popup`) owned by the VS main window, `Topmost`, positioned near the caret/status bar; content = a `TextBlock`/`ItemsControl` of `key → label` rows. Reuse `OverlayShowState`-style deferred show + `Deactivated`-close. | Full control of layout/colors; matches the existing Telescope chrome; easy to unit-test the *content* via a pure model; proven modal pattern in this repo. | A second WPF window adds focus-management risk (must not steal focus from the editor, or it will close itself via `Deactivated`); must be non-activating (`ShowActivated=false`, `Focusable=false`) so the leader sequence keeps flowing to the hook. |
| **B. VS tooltip / adornment** | An `IToolTipService`/adornment on the editor view, or a `IVsStatusbar` hint. | Native look; no new window. | Adornments are editor-view-bound (won't show over tool windows); tooltips are transient and hard to keep open across keystrokes; status bar is one line (no key tree). High VS-API coupling, low testability. |
| **C. Status-bar hint** | `IVsStatusbar.SetText("Space: B W Q E F C ...")`. | Trivial; zero focus risk; no new window. | One line only; no descriptions; not really "which-key". Good as a **fallback/degraded mode** if the popup is unavailable. |
| **D. Skip** | Do nothing; rely on the existing `keybindings.json` + docs. | Zero effort/risk. | No discovery aid — the whole point of which-key. |

**Recommendation:** **Option A**, with **Option C as a graceful fallback** (mirroring the
`PaneFailureTracker` one-time-fallback pattern). The popup must be **non-activating** so it
never takes focus from the editor/tool window — otherwise the `Deactivated`-close behavior
that makes the Telescope overlay safe would immediately close the which-key popup. This is
the single biggest design constraint and the main risk.

---

## 6. Dismissal / lifecycle

- **Show:** on a `Consume` result that is a live prefix (including the bare leader). Apply a
  short **delay** (LazyVim default ~200ms) so a fast complete sequence does not flash the
  popup — a pure `WhichKeyDelay`/timer seam, unit-testable.
- **Update:** on each subsequent prefix key, replace the content with the new prefix's
  continuations (breadcrumb: `Space › F`).
- **Hide:** on `Execute`/`Failed`/`Abort`/`PassThrough`, on Escape (already resets the
  sequence), and on a **timeout** (LazyVim `timeoutlen`; a configurable idle timeout).
- **Focus-loss:** unlike the Telescope overlay, which-key must **not** close on `Deactivated`
  if it is non-activating (it never activates, so `Deactivated` may not fire). If it *is*
  activating, it must close on focus loss to avoid swallowing the next sequence — but then it
  steals focus and breaks the sequence. **Conclusion: make it non-activating and close it
  explicitly from `InputHandler` on sequence end/timeout.** This is the key lifecycle
  decision.
- **Interaction with the Telescope overlay:** `InputHandler.HandleKey` already returns early
  when `_telescope.IsOpen` (`:271-274`), so which-key can never show while Telescope is open.
  No change needed.

---

## 7. Interaction with VsVim insert mode

which-key must **not** fire while typing. The gate already exists: `InputHandler.IsTyping()`
(`:494-501`) combines the tool-window input-mode flag, the `EditorFocusedVeto`, and
`_vsVim.IsInTypingMode`. The leader matcher itself already refuses to start a sequence when
`isTyping` is true (`LeaderSequenceMatcher.HandleKey` `:48-53`). Because which-key is driven
from the leader branch, it inherits this gate for free — **no new insert-mode logic is
needed**, but the popup must be explicitly hidden if a sequence is aborted by entering typing
mode (defensive).

---

## 8. Diagnostics contract (e2e-testable)

Following the repo's "diagnostics = test contract" rule (`docs/spec.md` §4), the popup must
emit deterministic `[NeoVisual]` lines. Proposed contract:

- `[NeoVisual] which-key show prefix={prefix} count={n}` — the popup was shown/updated for a
  prefix with `n` continuations. `prefix` is the comma-joined sequence (`""` for the bare
  leader, `F`, `F,F`); `count` is the number of immediate continuations.
- `[NeoVisual] which-key hide reason={reason}` — the popup was hidden; `reason` ∈
  `execute | abort | escape | timeout | typing`.
- `[NeoVisual] which-key unavailable: {reason}` — one-time fallback when the popup cannot be
  created (mirrors `[NeoVisual] output pane unavailable: {reason}`).

These make the feature e2e-testable without asserting on WPF pixels: a scenario presses
Space, asserts `which-key show prefix= count=10`, presses `F`, asserts
`which-key show prefix=F count=8`, presses Escape, asserts `which-key hide reason=escape`.
The `count` values are deterministic from `default-keybindings.json` (but user overrides can
change them — the harness should assert the prefix + a `count=\d+` pattern, or pin the
defaults).

---

## 9. Native VS alternatives

**Conclusion: there is no native VS which-key equivalent to extend/reuse.**

- VS's own **keyboard options UI** (`Tools > Options > Environment > Keyboard`) is a
  searchable command list, not a prefix-driven popup; it cannot be surfaced on a leader press.
- VS **IntelliSense / completion popups** are editor-bound and command-driven; they cannot be
  repurposed to show arbitrary leader continuations.
- VS **status bar** (`IVsStatusbar`) is the only native surface that could show a hint, but it
  is one line and has no key-tree/description model (Option C above).
- The **JetBrains `which-key-lazy`** port confirms the pattern must be built by the host
  plugin — there is no IDE-native which-key.

So the realistic choice is **BUILD a custom popup** (Option A) or **SKIP**; "extend/reuse
native" is not available for the core UX (only the status-bar fallback is native).

---

## 10. Feasibility, risks, and plan sketch

### Option 1 — BUILD a custom non-activating WPF popup (recommended)

- **Effort:** medium. New pure `WhichKeyModel` (prefix → continuations + labels) + a
  `WhichKeyPopup` WPF window + wiring in `InputHandler`'s leader branch + a label table in
  `default-keybindings.json` + diagnostics + unit tests + one e2e scenario.
- **Risk:** **focus management** is the dominant risk — a popup that activates steals focus
  and breaks the leader sequence; a popup that does not activate must be positioned and
  closed explicitly. Mitigate with `ShowActivated=false`, `Focusable=false`, `Topmost=true`,
  and explicit hide from `InputHandler`. Secondary risk: positioning near the caret across
  DPI/multi-monitor (reuse the Telescope centering math).
- **Testability:** high — the content model is pure and unit-testable; the diagnostics make
  the lifecycle e2e-assertable.

### Option 2 — EXTEND/REUSE the Telescope overlay infrastructure

- **Effort:** low-medium. Reuse `TelescopeController`/`TelescopeOverlay`'s show/close/state
  machinery for a lightweight "which-key finder" that lists continuations as results.
- **Risk:** the Telescope overlay is **modal and activating** (it owns focus and closes on
  `Deactivated`) — using it as-is would break the leader sequence (the overlay would take
  focus, and the next leader key would go to the overlay, not the hook). Making it
  non-activating is a non-trivial change to a proven, heavily-tested component and risks
  regressing the 36 e2e scenarios. **Not recommended** unless the popup is allowed to be
  modal (i.e. press leader → popup → pick a key inside the popup), which is a different UX
  from LazyVim's inline continuation.

### Option 3 — SKIP (or status-bar-only)

- **Effort:** zero (skip) / trivial (status bar).
- **Risk:** none.
- **Trade-off:** no discovery aid; the status-bar variant is a degraded hint only.

**Plan sketch (if BUILD):** (1) pure `WhichKeyModel.GetContinuations(prefix)` over the leader
bindings + a label resolver; (2) `KeybindingConfig` parses an optional `descriptions` map;
(3) `LeaderSequenceMatcher` exposes the current sequence on `Consume` (or a
`GetContinuations` query); (4) `InputHandler` shows/updates/hides the popup on the leader
branch, gated on `IsTyping()`; (5) a non-activating `WhichKeyPopup` WPF window; (6)
diagnostics; (7) unit tests + one e2e scenario (`neovisual-whichkey`). This is a **feature
lane** item (new `[NeoVisual]` diagnostics → M-M7 hard trigger → e2e enabled).

---

## 11. FEATURE-TRIAGE gate (the user decides)

Per the repo's FEATURE TRIAGE RULE (`docs/progress.md`), this feature must pass the gate
**before** any implementation loop starts. The user chooses:

1. **BUILD** — a custom non-activating WPF which-key popup (Option 1). Highest fidelity to
   LazyVim; medium effort; focus-management risk.
2. **EXTEND/REUSE** — reuse the Telescope overlay machinery (Option 2). Lower effort but the
   modal/activating nature conflicts with the inline-continuation UX; likely requires making
   the overlay non-activating (regression risk).
3. **SKIP** — no popup (Option 3), optionally a status-bar hint.

**Open questions for the user:**

- **Q1 — Build vs extend/reuse vs skip?** (the gate itself)
- **Q2 — UX fidelity:** inline non-activating popup (LazyVim-faithful) vs a modal picker
  (press leader → popup → choose a key inside it)? The modal picker is easier to build on the
  Telescope overlay but is a different interaction.
- **Q3 — Labels:** add a `descriptions` map to `keybindings.json` (user-overridable), or
  auto-derive labels from action names (zero config, uglier)?
- **Q4 — Trigger:** show on the bare leader only, or on every prefix (LazyVim shows on every
  prefix)? And the delay (0 vs ~200ms)?
- **Q5 — Scope:** leader sequences only, or also simple shortcuts (`Ctrl+H/J/K/L`)? LazyVim
  which-key is prefix-driven, so leader-only is the faithful scope.
- **Q6 — Dismissal:** timeout value, and whether Escape/any-key hides (LazyVim hides on
  completion/abort/timeout; Hydra Mode keeps it open until `<esc>`).

---

## Appendix — file:line evidence index

| Claim | Evidence |
|---|---|
| Leader state machine (start/match/prefix/abort) | `MyExtension/Input/Utils/LeaderSequenceMatcher.cs:44-126` |
| Prefix set = "prefix with continuations" predicate | `LeaderSequenceMatcher.cs:114-126` |
| Leader branch routing + diagnostics | `MyExtension/Input/InputHandler.cs:370-388` |
| `IsLeaderActive` volatile flag | `InputHandler.cs:72` |
| Insert-mode gate | `InputHandler.cs:494-501`; `MyExtension/Vim/VimModeTracker.cs:69,76` |
| Binding table load/merge | `MyExtension/Input/Utils/KeybindingConfig.cs:75-149` |
| Simple-shortcut classification | `KeybindingConfig.cs:157-166` |
| Binding table contents | `MyExtension/Resources/default-keybindings.json:1-34` |
| Action registry | `MyExtension/Package/Utils/Actions.cs:16-45` |
| Telescope action names | `MyExtension/Package/Utils/TelescopeLauncher.cs:26-35` |
| Hook pre-filter + swallow | `MyExtension/Hooks/GlobalKeyboardHook.cs:86-151` |
| Modal overlay pattern (show/close/Deactivated) | `Telescope/Overlay/TelescopeOverlay.cs:233-321` |
| Pure overlay key state machine | `Telescope/Overlay/Utils/OverlayKeyHandler.cs:66-226` |
| Overlay controller (fresh instance per open) | `Telescope/Controller/TelescopeController.cs:54-77` |
| Diagnostics prefix constants | `Telescope/Logging/Utils/DiagnosticLog.cs:11-15` |
| Diagnostics = test contract | `docs/spec.md` §4 |
| FEATURE TRIAGE RULE | `docs/progress.md` (User-requested features section) |
