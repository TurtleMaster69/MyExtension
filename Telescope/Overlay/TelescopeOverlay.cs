using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Telescope.Filter;
using Telescope.Finders;
using Telescope.Logging;

namespace Telescope.Overlay
{
    /// <summary>
    /// A Telescope-style overlay: a modal dialog with a TextBox prompt and a read-only results
    /// pane, with a small built-in vim mode. Normal mode intercepts <c>j/k/gg/G/Enter/Esc/q</c>
    /// (and <c>i/a/A/I</c> to enter insert); insert mode types into the prompt and live-filters
    /// the candidates with fzf. Selecting an entry calls the finder's <c>OnSelected</c>.
    ///
    /// <para/>
    /// <b>Why not a hosted VS editor view / VsVim:</b> hosting a fully-wired editable editor view
    /// programmatically requires the VS editor document infrastructure (<c>IVsTextManager</c>/
    /// <c>IVsTextDocData</c>) that a standalone dialog doesn't have, so the view never activates
    /// and VsVim never drives input. A plain TextBox with manual vim motions is simple and
    /// reliable for a fuzzy finder prompt.
    /// </summary>
    internal sealed class TelescopeOverlay : Window
    {
        private readonly FzfFilter _fzf;

        // ---- UI chrome ----
        private readonly TextBlock _modeLabel;
        private readonly DockPanel _layout;
        private readonly TextBox _promptBox;
        private readonly TextBox _resultsBox;
        private readonly RichTextBox _previewBox;

        // ---- Finder / results state ----
        private IReadOnlyList<FinderEntry> _candidates = Array.Empty<FinderEntry>();
        private IReadOnlyList<FinderEntry> _results = Array.Empty<FinderEntry>();
        private IFinder? _activeFinder;
        private int _selectedIndex;

        // n12: last-rendered results reference + selection, so a no-change RenderResults skips
        // the results-box rebuild + preview reload (the caret/layout hot path).
        private IReadOnlyList<FinderEntry>? _lastRenderedResults;
        private int _lastRenderedSelectedIndex = -1;

        // ---- Prompt mode / rendering state ----
        private readonly OverlayKeyHandler _keyHandler = new();
        private readonly TextMotionNavigator _previewNavigator = new();
        private readonly TextMotionNavigator _promptNavigator = new();
        private readonly PreviewRenderer _previewRenderer = new();
        private bool _activationHandled;
        private CancellationTokenSource? _filterCts;

        // State-based guard for the deferred ShowDialog(): RequestShow() in ShowOverlay, Close() in
        // CloseOverlay, and the ApplicationIdle BeginInvoke only shows when ShouldShowDialog().
        private readonly OverlayShowState _showState = new();

        // Query-driven finder debounce: a settle delay before the synchronous full-solution scan
        // runs, so typing does not stall the UI per keystroke. The generation counter invalidates
        // stale gathers when the query keeps changing during the delay.
        private const int QueryDebounceMs = 200;
        private int _queryGeneration;

        // Line caret brush for insert mode; white block brush for normal mode (white block, black
        // text via the TextBox's native glyph render under a white fill, matching the tool windows).
        private static readonly Brush PromptLineCaretBrush = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde));
        private static readonly Brush PromptBlockCaretBrush = BlockCaretStyle.CreateBlockBrush();

        // Where the overlay's keyboard focus currently lives: the results list (default, where
        // j/k select) or the file preview (where h/l/j/k/w/b/e/gg/G navigate the code read-only).
        // Pure state machine (unit-tested); the overlay only applies the resulting focus.
        private readonly FocusTargetModel _focusTargetModel = new();

        public TelescopeOverlay(FzfFilter fzf)
        {
            _fzf = fzf ?? throw new ArgumentNullException(nameof(fzf));

            Title = "Telescope";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2b));
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            Focusable = true;
            SizeToContent = SizeToContent.Manual;
            Width = 760;
            Height = 420;

            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2b)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
            };

            _layout = new DockPanel();

            // Title bar with the finder name + current mode.
            var titleBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x28, 0x2c, 0x34)),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            _modeLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x9d, 0xc3)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 14,
            };
            titleBar.Child = _modeLabel;
            DockPanel.SetDock(titleBar, Dock.Top);
            _layout.Children.Add(titleBar);

            // Prompt TextBox — the query input. It is editable in insert mode and read-only in
            // normal mode (normal-mode keys are intercepted at the window level).
            var promptHost = new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            _promptBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1f, 0x24)),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde)),
                CaretBrush = PromptLineCaretBrush,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 14,
                AcceptsReturn = false,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _promptBox.TextChanged += OnPromptTextChanged;
            promptHost.Child = _promptBox;
            DockPanel.SetDock(promptHost, Dock.Top);
            _layout.Children.Add(promptHost);

            // Results list rendered as read-only text, and a read-only file preview beside it.
            // The bottom area is a Grid: results on the left (narrower), preview on the right.
            var bottom = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1f, 0x24)),
            };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            _resultsBox = new TextBox
            {
                IsReadOnly = true,
                Focusable = false,
                IsTabStop = false,
                Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1f, 0x24)),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 13,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetColumn(_resultsBox, 0);
            bottom.Children.Add(_resultsBox);

            // Read-only file preview beside the results list, rendered with basic syntax
            // highlighting (keywords/strings/comments/numbers via SyntaxHighlighter) in a
            // RichTextBox so each token gets its own color.
            _previewBox = new RichTextBox
            {
                IsReadOnly = true,
                Focusable = true,
                IsTabStop = true,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)),
                BorderThickness = new Thickness(1, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)),
                CaretBrush = PromptBlockCaretBrush,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 13,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetColumn(_previewBox, 1);
            bottom.Children.Add(_previewBox);

            _layout.Children.Add(bottom);

            root.Child = _layout;
            Content = root;

            // Trace incoming characters so the harness can confirm whether text reaches the
            // prompt (the core "does typing filter" assertion).
            PreviewTextInput += (_, e) =>
            {
                if (IsOpen)
                {
                    TelescopeLog.Log($"textinput='{e.Text}' focused={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}");
                }
            };

            Activated += (_, _) =>
            {
                if (_activationHandled)
                {
                    return;
                }
                _activationHandled = true;
                FocusPrompt();
            };

            ContentRendered += (_, _) =>
                Dispatcher.BeginInvoke(new Action(FocusPrompt), DispatcherPriority.ApplicationIdle);

            // If the overlay ever loses focus while open, close it. A modal overlay that lost
            // focus to the window underneath is broken (keystrokes would go to the wrong surface),
            // and leaving it open would also swallow the NEXT leader sequence. Closing on focus
            // loss guarantees we never leave a stale open overlay behind — and makes the E2E
            // harness's "is the overlay still open?" check deterministic.
            Deactivated += (_, _) =>
            {
                if (IsOpen)
                {
                    TelescopeLog.Log($"deactivated -> closing overlay");
                    CloseOverlay();
                }
            };
        }

        /// <summary>True while the overlay is open and owns keyboard focus.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Opens the overlay for the given finder, centered over <paramref name="centerRect"/>
        /// (screen pixels; the VS main-window rect) or the work area if none.
        /// <paramref name="ownerHwnd"/>, when non-zero, is the fallback owner HWND. The overlay is
        /// shown as a modal dialog so VS handles focus/key routing. Runs on the UI thread.
        /// </summary>
        public void ShowOverlay(IFinder finder, System.Drawing.Rectangle? centerRect, IntPtr ownerHwnd = default)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _activeFinder = finder;
            _candidates = finder.GetCandidates();
            _results = _candidates;
            _selectedIndex = 0;
            _keyHandler.Reset();
            _keyHandler.SetResults(_candidates.Count);
            _focusTargetModel.Reset();
            _previewNavigator.SetText(string.Empty);
            _promptBox.Text = string.Empty;
            UpdateModeLabel();
            RenderResults();

            TelescopeLog.Log($"open finder={finder.Name} candidates={_candidates.Count}");
            if (!_fzf.IsAvailable())
            {
                TelescopeLog.Log("fzf unavailable — showing unfiltered list");
            }

            // Own the dialog to the VS main window (the Code Search / InstaSearch pattern). A
            // modal dialog owned by VS is OS-guaranteed to be the focused window and disables the
            // owner while open — so keys go here, never to the editor underneath.
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null)
            {
                Owner = mainWindow;
            }
            else if (ownerHwnd != IntPtr.Zero)
            {
                new System.Windows.Interop.WindowInteropHelper(this).Owner = ownerHwnd;
            }

            // Center over the given rect (the VS main window), converting pixels to DIPs.
            if (centerRect.HasValue)
            {
                var r = centerRect.Value;
                var dpi = VisualTreeHelper.GetDpi(this);
                double scale = dpi.PixelsPerDip;
                Left = (r.Left + (r.Width - Width) / 2.0) / scale;
                Top = (r.Top + (r.Height - Height) / 3.0) / scale;
            }
            else
            {
                var area = SystemParameters.WorkArea;
                Left = (area.Width - Width) / 2;
                Top = (area.Height - Height) / 3;
            }

            IsOpen = true;
            _showState.RequestShow();

            // Show as a modal dialog. Deferred out of the global keyboard hook callback
            // (leader-key path) to ApplicationIdle; the modal loop runs there while hook
            // callbacks stay fast. Do NOT pre-focus: ShowDialog() activates this window, firing
            // the one-shot Activated handler, which focuses the prompt in insert mode. Guard with
            // the state (equivalent to IsOpen), NOT IsVisible — IsVisible is false at
            // ApplicationIdle time, so an IsVisible guard would silently never open the overlay.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_showState.ShouldShowDialog())
                {
                    ShowDialog();
                }
            }), DispatcherPriority.ApplicationIdle);
        }

        public void CloseOverlay()
        {
            CancelFilter();
            IsOpen = false;
            _showState.Close();
            try
            {
                Close();
            }
            catch
            {
                // window may already be closed
            }
            TelescopeLog.Log($"overlay closed");
        }

        // ================================================================
        // Prompt filtering (fzf)
        // ================================================================

        private void OnPromptTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsOpen)
            {
                return;
            }
            string query = _promptBox.Text;
            TelescopeLog.Log($"promptChanged query='{query}'");
            RefreshResults(query);
        }

        private void RefreshResults(string query)
        {
            CancelFilter();
            if (_activeFinder.IsQueryDriven)
            {
                _ = RefreshQueryDrivenAsync(_activeFinder, query);
                return;
            }
            _filterCts = new CancellationTokenSource();
            var token = _filterCts.Token;
            var snapshot = _candidates;

            _ = FilterAndUpdateAsync(snapshot, query, token);
        }

        /// <summary>
        /// Query-driven gather (grep semantics — literal substring, no fzf): debounce the scan
        /// until typing settles, then re-gather candidates from the finder and render them
        /// directly. The await captures the WPF SynchronizationContext, so the synchronous scan
        /// resumes on the UI thread.
        /// </summary>
        private async Task RefreshQueryDrivenAsync(IFinder finder, string query)
        {
            int gen = ++_queryGeneration;
            await Task.Delay(QueryDebounceMs); // resumes on the UI thread (SynchronizationContext)
            if (gen != _queryGeneration || !IsOpen) return;
            IReadOnlyList<FinderEntry> results;
            try { results = finder.GetCandidates(query) ?? Array.Empty<FinderEntry>(); }
            catch (Exception ex) { TelescopeLog.Log($"query gather failed: {ex.Message}"); results = Array.Empty<FinderEntry>(); }
            if (gen != _queryGeneration || !IsOpen) return;
            _results = results;
            _keyHandler.SetResults(results.Count);
            RenderResults();
        }

        private async Task FilterAndUpdateAsync(IReadOnlyList<FinderEntry> snapshot, string query, CancellationToken token)
        {
            try
            {
                var matched = await _fzf.FilterAsync(snapshot.Select(x => x.Display), query, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var items = ResultMapper.MapBack(matched, snapshot);

                    _results = items;
                    _keyHandler.SetResults(items.Count);
                    RenderResults();
                }));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // m14: Format returns the UNPREFIXED message; TelescopeLog adds the [Telescope] prefix.
                TelescopeLog.Log(FilterFailureLog.Format(ex));
            }
        }

        private void CancelFilter()
        {
            var old = _filterCts;
            _filterCts = null;
            try
            {
                old?.Cancel();
                old?.Dispose();
            }
            catch
            {
                // already cancelled/disposed
            }
        }

        // ================================================================
        // Rendering
        // ================================================================

        private void RenderResults()
        {
            _selectedIndex = _keyHandler.SelectedIndex;
            bool resultsChanged = !ReferenceEquals(_lastRenderedResults, _results);
            bool selectionChanged = _lastRenderedSelectedIndex != _selectedIndex;
            if (resultsChanged || selectionChanged)
            {
                _lastRenderedResults = _results;
                _lastRenderedSelectedIndex = _selectedIndex;
                _resultsBox.Text = ResultsFormatter.ToText(_results, _selectedIndex);
                LoadPreviewForSelection();
            }
            TelescopeLog.Log($"results count={_results.Count} selected={_selectedIndex} boxText={_resultsBox.Text.Length}");
        }

        /// <summary>
        /// Loads the currently selected result's file content into the preview pane (when the
        /// finder entry carries a file path in its payload), syntax-highlighted. For code-issue
        /// entries the preview caret jumps to the issue's line. Resets otherwise to the top.
        /// </summary>
        private void LoadPreviewForSelection()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _results.Count)
            {
                SetPreviewContent(string.Empty);
                return;
            }

            object payload = _results[_selectedIndex].Payload;
            if (payload is IFileLocation location)
            {
                _previewRenderer.Show(_previewBox, _previewNavigator, location);
                return;
            }

            SetPreviewContent(string.Empty);
        }

        private void UpdateModeLabel()
        {
            string name = _activeFinder?.Name ?? "Telescope";
            _modeLabel.Text = _keyHandler.IsNormalMode ? name + " [NORMAL]" : name + " [INSERT]";
        }

        // ================================================================
        // Focus + key interception (manual vim motions)
        // ================================================================

        private void FocusPrompt()
        {
            try
            {
                _promptBox.IsReadOnly = _keyHandler.IsNormalMode;
                ApplyPromptCaretStyle();
                int caret = _promptBox.CaretIndex;
                bool focused = _promptBox.Focus();
                _promptBox.CaretIndex = caret;
                TelescopeLog.Log($"Focus prompt => {focused}, mode={( _keyHandler.IsNormalMode ? "normal" : "insert")}, focusedElement={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}");
            }
            catch (Exception ex)
            {
                TelescopeLog.Log($"Focus failed: {ex.Message}");
            }
        }

        /// <summary>Applies an insert-mode caret placement to the prompt box.</summary>
        private void ApplyInsertCaret(CaretPlacement placement)
        {
            _promptNavigator.SetText(_promptBox.Text);
            _promptNavigator.MoveTo(_promptBox.CaretIndex);
            switch (placement)
            {
                case CaretPlacement.End: // a (append): caret at end
                    _promptNavigator.InsertEnd();
                    break;
                case CaretPlacement.Start: // I (insert at start)
                    _promptNavigator.InsertStart();
                    break;
                default: // i (current): no motion, caret clamped to current
                    break;
            }
            _promptBox.CaretIndex = _promptNavigator.Caret;
        }

        /// <summary>Enters insert mode and places the caret per <paramref name="placement"/>.</summary>
        private void EnterInsert(CaretPlacement placement)
        {
            _promptBox.IsReadOnly = false;
            UpdateModeLabel();
            FocusPrompt();
            ApplyInsertCaret(placement);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Only act on keys while the overlay is open (modal).
            if (!IsOpen)
            {
                base.OnPreviewKeyDown(e);
                return;
            }

            string mode = _keyHandler.IsNormalMode ? "normal" : "insert";

            // Ctrl+H / Ctrl+L move focus between the results list and the file preview; Escape in
            // the preview returns to the list. Delegated to the pure focus-target state machine.
            var focusAction = _focusTargetModel.Handle(MapKey(e.Key));
            if (focusAction != FocusTargetAction.None)
            {
                e.Handled = true;
                TelescopeLog.Log($"focus target={_focusTargetModel.Current}");
                FocusTargetUi();
                return;
            }

            if (_focusTargetModel.Current == FocusTarget.Preview)
            {
                // Preview: vim motions navigate the code read-only; Escape returns to the list.
                bool handled = HandlePreviewKey(e.Key);
                if (handled)
                {
                    e.Handled = true;
                    ApplyPreviewCaret();
                    TelescopeLog.Log($"preview caret={_previewNavigator.Caret} line={_previewNavigator.LineNumber}");
                }
                base.OnPreviewKeyDown(e);
                return;
            }

            // Prompt box has focus (List target). In normal mode, h/l/w/b/e/0/$ move the prompt
            // caret; everything else routes through the list/mode state machine.
            if (_keyHandler.IsNormalMode && TryPromptMotion(e.Key))
            {
                e.Handled = true;
                base.OnPreviewKeyDown(e);
                return;
            }

            // Translate the WPF key into the logic's normalized gesture, run the state machine,
            // then apply whatever UI action the logic requests.
            var action = _keyHandler.Handle(MapKey(e.Key));
            ApplyAction(action, mode, e);

            // Always continue routing: handled keys were swallowed above (e.Handled = true), but
            // unhandled keys (e.g. typing in insert mode) must reach the TextBox beneath.
            base.OnPreviewKeyDown(e);
        }

        /// <summary>
        /// Applies a normal-mode prompt text motion (h/l/w/b/e/0/$) to the prompt box's caret,
        /// mirroring the text-input tool-window motions. Returns true when the key was consumed.
        /// </summary>
        private bool TryPromptMotion(Key key)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!PromptMotionRouter.ShouldConsume(key, shift, out _))
            {
                return false;
            }

            _promptNavigator.SetText(_promptBox.Text);
            _promptNavigator.MoveTo(_promptBox.CaretIndex);

            if (!TextMotionDispatcher.Handle(key, shift, _promptNavigator, out _))
            {
                return false;
            }

            _promptBox.CaretIndex = _promptNavigator.Caret;
            TelescopeLog.Log($"prompt-motion key={key} caret={_promptNavigator.Caret}");
            return true;
        }

        /// <summary>
        /// Draws a <b>block</b> caret on the prompt box in normal mode (white block, black text) and
        /// a thin line caret in insert mode, mirroring the text-input tool windows. m35: delegates
        /// to the shared <see cref="BlockCaretStyle.ApplyCaretStyle"/> (block in normal, line in
        /// insert) instead of duplicating the caret-brush logic.
        /// </summary>
        private void ApplyPromptCaretStyle()
        {
            BlockCaretStyle.ApplyCaretStyle(_promptBox, !_keyHandler.IsNormalMode, PromptLineCaretBrush);
        }

        /// <summary>Applies vim motions to the preview navigator for a list-mode key.</summary>
        private bool HandlePreviewKey(Key key)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!PromptMotionRouter.ShouldConsume(key, shift, out _))
            {
                return false;
            }
            return TextMotionDispatcher.Handle(key, shift, _previewNavigator, out _);
        }

        private void ApplyPreviewCaret()
        {
            _previewRenderer.ApplyCaret(_previewBox, _previewNavigator);
        }

        /// <summary>
        /// Replaces the preview content: tokenizes <paramref name="content"/> into colored runs
        /// (one paragraph per line) and feeds the same plain text to the motion navigator, so the
        /// caret index model and the rendered document stay in lockstep.
        /// </summary>
        private void SetPreviewContent(string content)
        {
            _previewRenderer.SetContent(_previewBox, _previewNavigator, null, content);
        }

        private void FocusTargetUi()
        {
            if (_focusTargetModel.Current == FocusTarget.Preview)
            {
                _previewBox.Focus();
                ApplyPreviewCaret();
            }
            else
            {
                FocusPrompt();
            }
        }

        private static OverlayKey MapKey(Key key)
        {
            switch (key)
            {
                case Key.Escape: return OverlayKey.Escape;
                case Key.Q: return OverlayKey.Q;
                case Key.Enter: return OverlayKey.Enter;
                case Key.Up: return OverlayKey.Up;
                case Key.Down: return OverlayKey.Down;
                case Key.J: return OverlayKey.J;
                case Key.K: return OverlayKey.K;
                case Key.G:
                    return (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? OverlayKey.ShiftG : OverlayKey.G;
                case Key.I: return OverlayKey.I;
                case Key.A: return OverlayKey.A;
                case Key.H when (Keyboard.Modifiers & ModifierKeys.Control) != 0: return OverlayKey.CtrlH;
                case Key.L when (Keyboard.Modifiers & ModifierKeys.Control) != 0: return OverlayKey.CtrlL;
                default: return OverlayKey.Other;
            }
        }

        private void ApplyAction(OverlayAction action, string mode, KeyEventArgs e)
        {
            bool handled = false;

            switch (action)
            {
                case OverlayAction.Close:
                    handled = true;
                    CloseOverlay();
                    break;
                case OverlayAction.SelectCurrent:
                    handled = true;
                    SelectCurrent();
                    break;
                case OverlayAction.EnterInsert:
                    handled = true;
                    EnterInsert(CaretPlacement.Current);
                    break;
                case OverlayAction.EnterInsertAppend:
                    handled = true;
                    EnterInsert(CaretPlacement.End);
                    break;
                case OverlayAction.EnterInsertStart:
                    handled = true;
                    EnterInsert(CaretPlacement.Start);
                    break;
                case OverlayAction.EnterNormal:
                    handled = true;
                    UpdateModeLabel();
                    _promptBox.IsReadOnly = true;
                    ApplyPromptCaretStyle();
                    break;
                case OverlayAction.MoveDown:
                case OverlayAction.MoveUp:
                case OverlayAction.MoveToFirst:
                case OverlayAction.MoveToLast:
                    handled = true;
                    RenderResults();
                    break;
                default:
                    break;
            }

            // Preserve the original logging contract: log handled keys (and the Escape/Enter/J/K
            // chords even when unhandled in insert mode) so the harness assertions stay stable.
            if (handled || e.Key == Key.J || e.Key == Key.K || e.Key == Key.Escape || e.Key == Key.Enter)
            {
                e.Handled = handled;
                TelescopeLog.Log($"key={e.Key} mode={mode} handled={handled}");
            }
        }

        private void SelectCurrent()
        {
            var finder = _activeFinder;
            var entry = _selectedIndex >= 0 && _selectedIndex < _results.Count ? _results[_selectedIndex] : null;
            CloseOverlay();
            if (finder != null && entry != null)
            {
                try
                {
                    finder.OnSelected(entry);
                }
                catch (Exception ex)
                {
                    TelescopeLog.Log($"OnSelected failed: {ex.Message}");
                }
            }
        }
    }
}
