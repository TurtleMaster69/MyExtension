# Tasks — code-review-hub-20261001-065242

## Slice file lists (enumerated 2026-10-01)

### A — MyExtension core
- MyExtension/Hooks/GlobalKeyboardHook.cs
- MyExtension/Hooks/KeyInjection.cs
- MyExtension/Hooks/InjectedKeyGuard.cs
- MyExtension/Hooks/NativeMethods.cs
- MyExtension/Input/InputHandler.cs
- MyExtension/Input/KeybindingConfig.cs
- MyExtension/Input/KeyNames.cs
- MyExtension/Input/KeyNameBuilder.cs
- MyExtension/Input/LeaderSequenceMatcher.cs
- MyExtension/Input/SimpleShortcutMatcher.cs
- MyExtension/Input/PopupNavigation.cs
- MyExtension/Input/StaleToolWindowSentinel.cs
- MyExtension/Vim/VimModeTracker.cs
- MyExtension/Vim/VimModeState.cs
- MyExtension/Vim/VimModeSource.cs
- MyExtension/Vim/VimModeClassifier.cs
- MyExtension/Vim/VimBufferSubscriptions.cs
- MyExtension/Package/MyExtensionPackage.cs
- MyExtension/Package/InitSteps.cs
- MyExtension/Package/Actions.cs
- MyExtension/Package/VsServices.cs
- MyExtension/Package/TelescopeLauncher.cs
- MyExtension/Package/TelescopeCommand.cs
- MyExtension/Adornments/BlockCaretAdornment.cs

### B — MyExtension/Navigation
- MyExtension/Navigation/WindowMatrix.cs
- MyExtension/Navigation/WindowAdapter.cs
- MyExtension/Navigation/WindowNavigationEngine.cs
- MyExtension/Navigation/NavigationSettings.cs
- MyExtension/Navigation/NavigationSnapshot.cs
- MyExtension/Navigation/UtilityMethods.cs
- MyExtension/Navigation/CardinalNavigationConstants.cs
- MyExtension/Navigation/RectCoordinate.cs
- MyExtension/Navigation/Direction.cs

### C — MyExtension/ToolWindows
- MyExtension/ToolWindows/IToolWindowController.cs
- MyExtension/ToolWindows/GeneralToolWindowController.cs
- MyExtension/ToolWindows/TextInputToolWindowController.cs
- MyExtension/ToolWindows/SolutionExplorerController.cs
- MyExtension/ToolWindows/WindowManager.cs
- MyExtension/ToolWindows/ToolWindowTypeResolver.cs
- MyExtension/ToolWindows/ToolWindowControllerBase.cs
- MyExtension/ToolWindows/TextMotionHelper.cs
- MyExtension/ToolWindows/HierarchyResolver.cs
- MyExtension/ToolWindows/HierarchyForestBuilder.cs
- MyExtension/ToolWindows/FocusGuard.cs
- MyExtension/ToolWindows/FocusKeeper.cs

### D — Telescope (whole folder)
- Telescope/Controller/TelescopeController.cs
- Telescope/Overlay/TelescopeOverlay.cs
- Telescope/Overlay/OverlayKeyHandler.cs
- Telescope/Overlay/OverlayShowState.cs
- Telescope/Overlay/TextMotionNavigator.cs
- Telescope/Overlay/TextMotionDispatcher.cs
- Telescope/Overlay/TryDispatch.cs
- Telescope/Overlay/SyntaxHighlighter.cs
- Telescope/Overlay/ResultsFormatter.cs
- Telescope/Overlay/ResultMapper.cs
- Telescope/Overlay/PreviewRenderer.cs
- Telescope/Overlay/LineIndex.cs
- Telescope/Overlay/FocusTargetModel.cs
- Telescope/Overlay/BlockCaretStyle.cs
- Telescope/Filter/FzfFilter.cs
- Telescope/Finders/TelescopeFinder.cs
- Telescope/Finders/FileFinder.cs
- Telescope/Finders/CodeIssuesFinder.cs
- Telescope/Finders/CodeIssue.cs
- Telescope/Finders/GrepFinder.cs
- Telescope/Finders/GrepHit.cs
- Telescope/Finders/ReferencesFinder.cs
- Telescope/Finders/ReferenceHit.cs
- Telescope/Finders/ImplementationFinder.cs
- Telescope/Finders/ImplementationHit.cs
- Telescope/Finders/ProjectFiles.cs
- Telescope/Finders/ProjectFileCache.cs
- Telescope/Finders/FileContentCache.cs
- Telescope/Finders/HitOpener.cs
- Telescope/Finders/HierarchyWalker.cs
- Telescope/Finders/DteFileOpener.cs
- Telescope/Finders/FinderBase.cs
- Telescope/Finders/FileLocation.cs
- Telescope/Finders/IFileLocation.cs
- Telescope/Finders/FileHit.cs
- Telescope/Logging/NeoVisualLog.cs
- Telescope/Logging/LogFileWriter.cs
- Telescope/Logging/NeoVisualTraceListener.cs
- Telescope/Logging/DiagnosticLog.cs
- Telescope/Logging/TelescopeLog.cs
- Telescope/Logging/FilterFailureLog.cs
- Telescope/Logging/PaneFailureTracker.cs

### E — tests + tools
- tests/TestRunner.cs
- tests/Telescope.Tests/Program.cs
- tests/NeoVisual.Tests/Program.cs
- tools/harness/test-e2e.ps1
- tools/harness/iterate-telescope.ps1
- tools/harness/harness-common.ps1
- tools/harness/dte-command.ps1
- tools/lint/check-doc-refs.ps1

### F — cross-cutting (hub-conducted)
- duplication BETWEEN slices, whole-repo perf hazards, net472/BCL consistency,
  namespace/folder hygiene, log-format drift across the two projects, hook-path cost.
