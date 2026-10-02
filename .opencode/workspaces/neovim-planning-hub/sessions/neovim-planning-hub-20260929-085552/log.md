# Session log — neovim-planning-hub-20260929-085552 (code-review findings plan)

## 2026-09-29 — Start
- REQUEST: user asked to read docs/code-review.md and make a plan from it again;
  defer e2e until on a VS-capable machine; do 5 passes of ≥10 subagents each.
- Re-orientation: read AGENTS.md (auto), vs-extension-dev SKILL.md, docs/progress.md
  (queue + resume), docs/spec.md, docs/implementation_plan.md (finished F15 item —
  safe to overwrite at handoff), e2e-queue.md, session state (previous session
  20260928-112201 was the architecture-consolidation plan — different objective).
- docs/code-review.md read: 75 findings (3 critical, 30 major, 30 minor, 12 nit).
- New session dir created: sessions/neovim-planning-hub-20260929-085552/.
- Plan: recon pre-pass → 5 research passes (10 arch-auditors each) → synthesize →
  implementation-planner BP-n → docs-reviewer gate → handoff on approval.

## 2026-09-29 — Recon pre-pass (R0)
- trailmark-recon DONE: graph 1298 nodes / 552 proxies (42.5%) / 0 entrypoints /
  2234 call edges (matches code-review.md's own figures). Complexity hotspots:
  SyntaxHighlighter.Segment 23, IsTextInputType 19, HandleKey 17, ApplyMotionToBox 14,
  HandleNormal 14, HandlePreviewKey/MapKey/ApplyAction 13, MapMotion 11, SelectTarget 10.
  High blast radius: InitializeAsync 85, OnPreviewKeyDown 51, OnViewGotFocus 35,
  TryMoveFocusedSurface 32, GatherImplementations 30, GatherReferences 28.
  False-dead-code traps verified (KeyInjection.Press, NeoVisualLog.Close/Log/Debug,
  InstallDebugListener, TextMotionHelper.MapMotion, controller.TryMove/ExitInputMode).
  STALE CITATIONS: M34 OnPreviewKeyDown is at TelescopeOverlay.cs:500-560 (not :75);
  M14 BuildForest is at SolutionExplorerController.cs:335-362 (not :295). All other
  method-level citations fall inside cited spans. .ps1/tools claims outside C# graph.
- Launching Pass 1 (correctness: CR1-CR3, M9-M17) — 10 arch-auditors.

## 2026-09-29 — Pass 1 (correctness) results
- CR1 CONFIRMED (dict overwrite; fix = static pure factory DefaultControllerFor; existing tests give false confidence).
- CR2 CONFIRMED ($failures only in catch; fix = append on $result -eq $false; no-VS snippet proof).
- CR3 PARTIAL (defect real; Deactivated trigger unreachable — fresh instance per open; realistic = Dispose/Close during idle; fix = IsOpen guard NOT IsVisible; pure OverlayShowState seam).
- M9 PARTIAL (described bug refuted — i+=2 is backslash-escape branch; real bug = unterminated string ending in backslash → AOORE → whole preview fails; fix = clamp escape advance).
- M10 CONFIRMED (trigger corrected: leading blank line + Up() → LastIndexOf('\n',-1) AOORE; fix = clamp startIndex).
- M11 CONFIRMED (blank line → CaretToPointer null → silent no-op, e2e still passes; fix = paragraph-start fallback; needs pure index→(line,offset) extraction first).
- M12 CONFIRMED (worse: AutoHides outside try propagates into hook path; fix = as-cast + Empty rect + wrap AutoHides).
- M13 CONFIRMED (Classify(string) on description; fix = ClassifySeverity(vsBuildErrorLevel) High→Error/Medium→Warning/Low→Info).
- M14 CONFIRMED (line corrected :335-362; fix = pure HierarchyForestBuilder over tuples).
- M15 CONFIRMED (action() unguarded; fix = LeaderResultKind.Failed + [NeoVisual] leader-binding failed: log; sibling hole simpleAction() :351).
- M17 PARTIAL (2/3 confirmed; (int)value cast refuted; effect direction corrected — leader leaks INTO insert mode; fix = single owner + guard out-of-order + don't latch _resolved).
- P1-9 (M16 FocusGuard) returned NO structured verdict → re-dispatching fresh.
- P1-9 re-dispatch #1 cancelled (subagent looped) → escalated via question → user: "redispatch same agent with same instruction" → re-dispatch #2 DONE.
- M16 PARTIAL: isTextInputSurface exemption is a deliberate D4 deviation (neovisual-textinput-motions); real leak vector = text-input surface in NORMAL mode while editor holds focus → moves editor caret + a/A/I corrupt unfocused controller state. Fix needs a reliable textInputSurfaceFocused signal + re-derive FocusGuard truth tables AND EditorFocusedVeto in lockstep. Unit seam: HasToolWindowActionKeys(..., textInputSurfaceFocused:false) must be FALSE.
- PASS 1 COMPLETE (10/10 verdicts). Launching Pass 2 (hook hot-path + perf: M1-M8, m1-m3, m7, m15-m18).

## 2026-09-29 — Pass 2 (hook hot-path + perf) results
- M1 CONFIRMED (no harness dep on per-key [Hook] key=; delete line 124; prefix pin + install assertion are the guard).
- M2 CONFIRMED (low-end major — in-proc COM; single-pass snapshot + pure NavigationSnapshot helper; cleanup write-only _rect).
- M3 CONFIRMED (undercount: ~4-16 File.Exists/key). CRITICAL CORRECTION: stated fix "refresh in OnWindowFocusChanged" is WRONG — sentinel toggles with NO focus change; cache must refresh once per key-down. Add positive diagnostic `[NeoVisual] stale-toolwindow sentinel active` (current e2e is a false-positive risk). Pure StaleToolWindowSentinel seam.
- M4 CONFIRMED (5 sites not 3: 76,93,283,396,423; cache _isTextInputType on WindowManager, respect sentinel; weak unit seam — VS-coupled).
- M5 CONFIRMED (shared ProjectFileCache injected into GrepFinder + CodeIssuesFinder; content cache keyed by LastWriteTimeUtc; RED via counting enumerate delegate).
- M6 CONFIRMED (timeout via Task.WhenAny+TryKill+fallback; [Telescope] fzf filter failed: log; surface IsAvailable once; fix QuoteArg/ArgumentList; RED via injected fzfPath seam).
- M7 CONFIRMED (CORRECTION: preview caret= log reads pure TEXT model, so CaretToPointer desync is e2e-INVISIBLE — worse than stated; line-start index + binary search; share pure LineIndex with M11).
- M8 PARTIAL (asymmetry confirmed; "fzf missing → unobserved" refuted — FilterAsync swallows; real escapes = OCE rethrow + RenderResults/PreviewRenderer faults in BeginInvoke action; wrap body in try/catch + [Telescope] filter failed:; pure FilterFailureLog.Format seam or IFzfFilter fake).
- m1 CONFIRMED (StringBuilder; pure KeyNameBuilder seam). m2 PARTIAL (references only, not implementations; per-gather cache). m3 CONFIRMED (reuse navigator + re-SetText on change). m7 CONFIRMED (hoist GetServiceAsync before loop).
- m15 CONFIRMED (add _promptNavigator field). m16 CONFIRMED (5 static readonly frozen brushes). m17 CONFIRMED (cache _lineNumber/_columnNumber, invalidate on _caret change). m18 CONFIRMED (cache last rendered file or Task.Run read+tokenize).
- Launching Pass 3 (duplication: M21-M30).

## 2026-09-29 — Pass 3 (duplication) results
- M21 CONFIRMED (KeyToString byte-identical ×2, no drift yet; live "/" default contract; fix = shared KeyNames.ToString(Keys) in MyExtension; RED round-trip tests).
- M22 CONFIRMED (FileFinder.CollectProjectFiles + FindFirstSourceFileInItems bypass ProjectFiles.Enumerate; fix = reuse Enumerate + pure walker helper; RED via fake tree).
- M23 CONFIRMED (OpenReference/OpenImplementation byte-identical; fix = OpenHitAtLine(IFileLocation) + pure HitOpener.OpenAtLine; RED tests).
- M24 CONFIRMED ($ drift LIVE: Shift-gated in prompt, bare in preview; fix = shared TryDispatch(Key, shift, navigator); keep per-surface log lines; RED tests).
- M25 CONFIRMED (3 block-caret sites; 2 byte-identical factories; fix = shared BlockCaretStyle in Telescope; RED reference-equality test).
- M26 CONFIRMED (2 focus-keeper timer sites, already diverged — one injects Escapes; fix = FocusKeeper.Run + pure FocusKeeperSchedule; RED compile-error).
- M27 PARTIAL (Debug alias byte-identical confirmed; Write/WriteDebug structurally identical not byte-identical; TraceListener Write/WriteLine byte-identical; 35 Debug call sites not 30; fix = WriteTo core + delete Debug alias).
- M28 CONFIRMED (5 Open-Telescope* helpers/22 sites, 9× SE-toggle loop, 3 wait helpers, 2× seed content; fix = consolidated helpers + -SelfCheck seam; do NOT fold neovisual-explorer-toggle).
- M29 CONFIRMED (23× temp-dir, 8× LogPath save/restore, duplicate MapMotion groups; fix = TempDir IDisposable + WithLogPath in shared tests/TestRunner.cs; NeoVisual 74→70).
- M30 CONFIRMED (no drift today; failure = KeyNotFoundException in hook callback; fix = derive Registry telescope entries from FinderNames; RED set-equality test).
- PASS 3 COMPLETE (10/10 verdicts). Launching Pass 4 (testability + logging: M31-M34, M18, M35, M43, m12-m14, m20, m23-m25).

## 2026-09-29 — Pass 4 (testability + logging) results
- M31 CONFIRMED (extract pure SimpleShortcutMatcher mirroring LeaderSequenceMatcher; share one KeyNames.ToString(Keys) — ties into M21; keep shortcut-binding executed: log in InputHandler; RED Run_SimpleShortcutMatcher_*).
- M32 CONFIRMED (VimModeClassifier.Classify(int?) → (IsTyping, Name) pure + IVimModeSource fake; VimModeTracker thin adapter; coordinate with M17 — same file).
- M33 CONFIRMED (per-step try/catch + distinct [MyExtension] init <step> ok/failed diagnostics; extract named-step orchestration with injected log sink; pre-try steps :58-61 unprotected).
- M34 CONFIRMED (pure FocusTargetModel mirroring OverlayKeyHandler; add CtrlH/CtrlL to OverlayKey; preserve [Telescope] focus target=List|Preview byte-identical; RED Run_FocusTarget_*).
- M18 CONFIRMED (corrected: harness reads log FILE not pane — impact is human-visible pane + diagnosability; fix = one-time fallback via LogFileWriter.Write (never recurse into Log) + lock _pane/_paneInitTried; RED PaneFailureTracker seam).
- M35 CONFIRMED (internal static WriteFailureCount incremented in catches + one-time marker-file for harness; RED Run_LogFileWriter_WriteFailureCountIncrements).
- M43 CONFIRMED (fix = Option A one-shot Change(200, Timeout.Infinite) re-armed on write; Option B dispose-on-Clear does NOT fix idle wakeups; RED FlushCount counter seam).
- m13 PARTIAL (prefix centralized in DiagnosticLog.Telescope — refuted half; 6 sites still bypass TelescopeLog.Log via NeoVisualLog.Debug — confirmed half). m20 CONFIRMED (3 error-reporting mechanisms; bonus: FinderBase.OnSelected double-logs). Fix = route through TelescopeLog.Log, drop concatenation, consolidate double-log, fix GatherErrorLiteral prefix contract.
- m23 CONFIRMED (Clear-on-open is a no-op after package init; fix = REMOVE misleading Clear from ShowOverlay + fix doc — do NOT truncate per-open, breaks harness baseline). m24 CONFIRMED (suffix inversion; rename-or-document, coordinated 4-file change). m25 PARTIAL (documented-accepted per spec.md §2.2 — leave as-is).
- m12 CONFIRMED (carried Action is dead; fix = matcher returns Execute without invoking, InputHandler invokes — synergizes with M15). m14 CONFIRMED (null-payload silent no-op; fix = emit diagnostic in FinderBase.OnSelected; rewrite Run_ResultMapper_UnknownStringNullPayload).
- PASS 4 COMPLETE (10/10 verdicts). Launching Pass 5 (harness + tests + naming: M19, M20, M36-M42, m26-m30, n1-n12).

## 2026-09-29 — Pass 5 (harness + tests + naming) results
- M19 CONFIRMED (fix-direction correction: Assert-Budget is a checkpoint stopwatch that CANNOT interrupt a blocking COM call — need a background runspace with bounded wait + -TimeoutSec; verification = -SelfTest with blocking stub).
- M20 (a) CONFIRMED (corrected refs: Program.cs:132-134,149-151 — :774 wrong); (b) CONFIRMED; (c) CONFIRMED. Fix: Clear idempotent per-path (HashSet); fail loudly when fzf absent (injected fzfPath seam); internal LoadDefaults() reading embedded resource only. Count caveat: static count 84 (Telescope)/81 (NeoVisual) — AGENTS.md 77/74 stale.
- M36 CONFIRMED (also :129,:262,:271 kill paths + :106 env; fix = port test-e2e.ps1 M-M5 pattern: PID-scoped kills + Save-AllDocuments + Process-scope env; verification = PID-scoping self-check).
- M37 CONFIRMED (Assert-Budget wrapping the scenario loop, called at top of each iteration BEFORE the try — inside try would swallow into $failures; verification = pure function test).
- M38 CONFIRMED (only file without namespace block; 7 code sites all use simple name in namespace MyExtension — no reference changes; fix = wrap + drop self-using).
- M39 CONFIRMED (perpendicular confirmed; 2 sites: Direction.cs:8 + WindowNavigationEngine.cs:58; no test touches the method; fix = rename Axis → PerpendicularAxis).
- M40 CONFIRMED (single caller InputHandler.Navigate + 4 Actions.cs sites; delete ToDirection + 4 char constants). CRITICAL: log-line-as-contract — InputHandler.cs:483 logs navigate direction={direction}, e2e asserts L/R/D/U; must keep single-char token.
- M41 CONFIRMED (stub genuinely unreachable; GatherHits abstract in FinderBase so can't delete — fix = throw NotSupportedException; behavior-preserving).
- M42 CONFIRMED (rename 109/117 to *Classified; delete Run_TextInput_MapMotions + Run_TextInput_MapInsertMotions — coordinate with M29). m30 CONFIRMED (named constant PositiveActionKeyCount). Baseline 81 passed (AGENTS.md 74 stale).
- m26 CONFIRMED (cache-index vs fresh Get-Content mixing). m27 CONFIRMED. m28 CONFIRMED (same root cause as m26 — fix together). m29 CONFIRMED. n1 CONFIRMED (rename _keyboardLogger→_keyboardHook). n2 PARTIAL (only :693 [Telescope]→[NeoVisual] safe; [MyExtension] harness-pinned). n3-n12 CONFIRMED/PARTIAL (n10: rename LineStartHome→LineStart; InsertStart/End doc already says text/line).
- PASS 5 COMPLETE (10/10 verdicts). ALL 5 RESEARCH PASSES DONE (50 arch-auditors + 1 recon = 51 subagents).
- Synthesizing the initial plan (S1) at plans/plan.md.

## S2 Build Plan (13 implementation-planner agents, one per phase) - DONE
- User directive: one phase per agent (13 agents, phases 0-12).
- All 13 returned structured verdicts (Phase 12 re-dispatched once fresh after a no-verdict return).
- BP step counts: P0=7, P1=5, P2=8, P3=4, P4=5, P5=6, P6=7, P7=9, P8=9, P9=13, P10=16, P11=5, P12=17.
- Key planner-adjudicated deviations (folded into artifacts):
  - P5: item.Severity -> item.ErrorLevel (EnvDTE80.ErrorItem, vsBuildErrorLevel); ClassifySeverity internal static; doc-ref propagation for BuildForest removal.
  - P7: m24 = DOCUMENT not rename (rename touches 6 harness sites + test literals + docs, unverifiable in unit-only lane); m13/m20 unprefixing must also update ImplementationFinder.cs:61 + ReferencesFinder.cs:63 overrides.
  - P8: M24 tool-window surface NOT folded into TryDispatch (NeoVisual.Tests cannot compile against Telescope/WPF); M29 count corrected -4 -> -2 (81->79); M42 owns the MapMotion deletion (consolidate ONCE).
  - P10: check-doc-refs.ps1 ALREADY RED with 7 pre-existing unresolved refs - m27 regression gate = "no NEW issues", never "0 issues"; M37 Assert-Budget must be called BEFORE the try in the scenario loop.
  - P12: M40 adds pure DirectionExtensions.ToChar() + Run_Direction_ToChar to pin navigate direction=L/R/D/U hermetically; n8 conditional on M2; M41 throw-assert via reflection (GrepFinder sealed).
- Combined into plans/plan.md (4246 lines): 13 per-phase Build Plan sections + 13 Verification Trace tables under '## Build Plan (per-phase; one implementation-planner agent per phase)'.

## S3 Plan review gate - DONE (APPROVE)
- docs-reviewer returned APPROVE (initial-plan + build-plan) at plans/plan.md.
- 2 minor non-blocking findings fixed by the hub (initial-plan sections): M29 count -4 -> -2 (81->79); WindowNavigationEngine test count 11 -> 10 (verified 10 in NeoVisual.Tests/Program.cs).
- Reviewer spot-verified citations, test counts (84/81), the 35 Debug call sites, the 7 pre-existing check-doc-refs issues, and the known-RED allowlist (none).

## S4 Handoff - DONE (user-approved via question)
- docs/implementation_plan.md: overwritten with the assembled plan (4246 lines; prior F15 content was finished/clean).
- docs/progress.md: new FIRST ITEM (2026-09-29) = Code review findings plan, unit-only lane, e2e deferred; no duplicates removed, no prerequisites moved up.
- e2e-queue.md: 12 new QUEUED rows (E2E-CR-1..3, E2E-M1/M3/M4, E2E-M5/M6/M7/M8, E2E-M16, E2E-M17/M32, E2E-M24, E2E-M28/M36/M37/m26/m28, E2E-M20/M29/M42, E2E-M34, E2E-M40).
- Next step for the user: tell neovim_hub to execute the FIRST item in the unit-only lane.
