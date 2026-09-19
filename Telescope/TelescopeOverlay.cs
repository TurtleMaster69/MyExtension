using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Telescope
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

        // ---- Prompt mode / rendering state ----
        private readonly OverlayKeyHandler _keyHandler = new();
        private readonly TextMotionNavigator _previewNavigator = new();
        private bool _activationHandled;
        private CancellationTokenSource? _filterCts;

        // Query-driven finder debounce: a settle delay before the synchronous full-solution scan
        // runs, so typing does not stall the UI per keystroke. The generation counter invalidates
        // stale gathers when the query keeps changing during the delay.
        private const int QueryDebounceMs = 200;
        private int _queryGeneration;

        // Line caret brush for insert mode; white block brush for normal mode (white block, black
        // text via the TextBox's native glyph render under a white fill, matching the tool windows).
        private static readonly Brush PromptLineCaretBrush = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde));
        private static readonly Brush PromptBlockCaretBrush = CreatePromptBlockBrush();

        private static DrawingBrush CreatePromptBlockBrush()
        {
            var rect = new System.Windows.Rect(0, 0, 8, 16);
            var drawing = new DrawingBrush(new GeometryDrawing(
                Brushes.White, null, new RectangleGeometry(rect)));
            drawing.Freeze();
            return drawing;
        }

        // Where the overlay's keyboard focus currently lives: the results list (default, where
        // j/k select) or the file preview (where h/l/j/k/w/b/e/gg/G navigate the code read-only).
        private enum FocusTarget { List, Preview }
        private FocusTarget _focusTarget = FocusTarget.List;

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
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}textinput='{e.Text}' focused={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}");
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
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}deactivated -> closing overlay");
                    CloseOverlay();
                }
            };
        }

        /// <summary>True while the overlay is open and owns keyboard focus.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Raised when the overlay is closed (by Esc, q, or programmatically).</summary>
        public event EventHandler? OverlayClosed;

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
            _focusTarget = FocusTarget.List;
            _previewNavigator.SetText(string.Empty);
            _promptBox.Text = string.Empty;
            UpdateModeLabel();
            RenderResults();

            NeoVisualLog.Clear();
            NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}open finder={finder.Name} candidates={_candidates.Count}");

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

            // Show as a modal dialog. Deferred out of the global keyboard hook callback
            // (leader-key path) to ApplicationIdle; the modal loop runs there while hook
            // callbacks stay fast. Do NOT pre-focus: ShowDialog() activates this window, firing
            // the one-shot Activated handler, which focuses the prompt in insert mode.
            Dispatcher.BeginInvoke(new Action(() => ShowDialog()), DispatcherPriority.ApplicationIdle);
        }

        public void CloseOverlay()
        {
            CancelFilter();
            IsOpen = false;
            try
            {
                Close();
            }
            catch
            {
                // window may already be closed
            }
            NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}overlay closed");
            OverlayClosed?.Invoke(this, EventArgs.Empty);
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
            NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}promptChanged query='{query}'");
            RefreshResults(query);
        }

        private void RefreshResults(string query)
        {
            CancelFilter();
            if (_activeFinder is IQueryFinder queryFinder)
            {
                _ = RefreshQueryDrivenAsync(queryFinder, query);
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
        private async Task RefreshQueryDrivenAsync(IQueryFinder finder, string query)
        {
            int gen = ++_queryGeneration;
            await Task.Delay(QueryDebounceMs); // resumes on the UI thread (SynchronizationContext)
            if (gen != _queryGeneration || !IsOpen) return;
            IReadOnlyList<FinderEntry> results;
            try { results = finder.GetCandidates(query) ?? Array.Empty<FinderEntry>(); }
            catch (Exception ex) { NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}query gather failed: {ex.Message}"); results = Array.Empty<FinderEntry>(); }
            if (gen != _queryGeneration || !IsOpen) return;
            _results = results;
            _keyHandler.SetResults(results.Count);
            RenderResults();
        }

        private async Task FilterAndUpdateAsync(IReadOnlyList<FinderEntry> snapshot, string query, CancellationToken token)
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

                var byDisplay = snapshot
                    .GroupBy(x => x.Display, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var items = matched
                    .Select(m => byDisplay.TryGetValue(m, out var entry) ? entry : new FinderEntry(m))
                    .ToList();

                _results = items;
                _keyHandler.SetResults(items.Count);
                RenderResults();
            }));
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
            _resultsBox.Text = ResultsFormatter.ToText(_results, _selectedIndex);
            NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}results count={_results.Count} selected={_selectedIndex} boxText={_resultsBox.Text.Length}");
            LoadPreviewForSelection();
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
            if (payload is CodeIssue issue && System.IO.File.Exists(issue.FilePath))
            {
                try
                {
                    string content = System.IO.File.ReadAllText(issue.FilePath);
                    SetPreviewContent(content);
                    if (issue.LineNumber > 0)
                    {
                        _previewNavigator.MoveToLine(issue.LineNumber);
                        ApplyPreviewCaret();
                        NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview caret={_previewNavigator.Caret} line={_previewNavigator.LineNumber}");
                    }
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview file={issue.FilePath} chars={content.Length}");
                }
                catch (Exception ex)
                {
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview load failed: {ex.Message}");
                }
                return;
            }

            if (payload is ReferenceHit hit && System.IO.File.Exists(hit.FilePath))
            {
                try
                {
                    string content = System.IO.File.ReadAllText(hit.FilePath);
                    SetPreviewContent(content);
                    if (hit.LineNumber > 0)
                    {
                        _previewNavigator.MoveToLine(hit.LineNumber);
                        ApplyPreviewCaret();
                        NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview caret={_previewNavigator.Caret} line={_previewNavigator.LineNumber}");
                    }
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview file={hit.FilePath} chars={content.Length}");
                }
                catch (Exception ex)
                {
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview load failed: {ex.Message}");
                }
                return;
            }

            if (payload is GrepHit gh && System.IO.File.Exists(gh.FilePath))
            {
                try
                {
                    string content = System.IO.File.ReadAllText(gh.FilePath);
                    SetPreviewContent(content);
                    if (gh.LineNumber > 0)
                    {
                        _previewNavigator.MoveToLine(gh.LineNumber);
                        ApplyPreviewCaret();
                        NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview caret={_previewNavigator.Caret} line={_previewNavigator.LineNumber}");
                    }
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview file={gh.FilePath} chars={content.Length}");
                }
                catch (Exception ex)
                {
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview load failed: {ex.Message}");
                }
                return;
            }

            if (payload is string path && System.IO.File.Exists(path))
            {
                try
                {
                    string content = System.IO.File.ReadAllText(path);
                    SetPreviewContent(content);
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview file={path} chars={content.Length}");
                }
                catch (Exception ex)
                {
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview load failed: {ex.Message}");
                }
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
                bool focused = _promptBox.Focus();
                _promptBox.CaretIndex = _promptBox.Text.Length;
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}Focus prompt => {focused}, mode={( _keyHandler.IsNormalMode ? "normal" : "insert")}, focusedElement={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}");
            }
            catch (Exception ex)
            {
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}Focus failed: {ex.Message}");
            }
        }

        /// <summary>Applies an insert-mode caret placement to the prompt box.</summary>
        private void ApplyInsertCaret(int caretPlacement)
        {
            switch (caretPlacement)
            {
                case 1: // a (append): caret at end
                    _promptBox.CaretIndex = _promptBox.Text.Length;
                    break;
                case 2: // I (insert at start)
                    _promptBox.CaretIndex = 0;
                    break;
                default: // i (current/end)
                    _promptBox.CaretIndex = Math.Min(_promptBox.CaretIndex, _promptBox.Text.Length);
                    break;
            }
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

            // Ctrl+H / Ctrl+L move focus between the results list and the file preview.
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.H || e.Key == Key.L))
            {
                e.Handled = true;
                _focusTarget = e.Key == Key.H ? FocusTarget.List : FocusTarget.Preview;
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}focus target={_focusTarget}");
                FocusTargetUi();
                return;
            }

            if (_focusTarget == FocusTarget.Preview)
            {
                // Preview: vim motions navigate the code read-only; Escape returns to the list.
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    _focusTarget = FocusTarget.List;
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}focus target={_focusTarget}");
                    FocusTargetUi();
                    return;
                }
                bool handled = HandlePreviewKey(e.Key);
                if (handled)
                {
                    e.Handled = true;
                    ApplyPreviewCaret();
                    NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview caret={_previewNavigator.Caret} line={_previewNavigator.LineNumber}");
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
            var navigator = new TextMotionNavigator();
            navigator.SetText(_promptBox.Text);
            navigator.MoveTo(_promptBox.CaretIndex);

            switch (key)
            {
                case Key.H: navigator.Left(); break;
                case Key.L: navigator.Right(); break;
                case Key.W: navigator.NextWord(); break;
                case Key.B: navigator.PrevWord(); break;
                case Key.E: navigator.EndWord(); break;
                case Key.D0: navigator.LineStartHome(); break; // 0
                case Key.D4 when (Keyboard.Modifiers & ModifierKeys.Shift) != 0: navigator.LineEnd(); break; // $
                default: return false;
            }

            _promptBox.CaretIndex = navigator.Caret;
            NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}prompt-motion key={key} caret={navigator.Caret}");
            return true;
        }

        /// <summary>
        /// Draws a <b>block</b> caret on the prompt box in normal mode (white block, black text) and
        /// a thin line caret in insert mode, mirroring the text-input tool windows.
        /// </summary>
        private void ApplyPromptCaretStyle()
        {
            try
            {
                _promptBox.CaretBrush = _keyHandler.IsNormalMode ? PromptBlockCaretBrush : PromptLineCaretBrush;
            }
            catch
            {
                // caret styling is best-effort
            }
        }

        /// <summary>Applies vim motions to the preview navigator for a list-mode key.</summary>
        private bool HandlePreviewKey(Key key)
        {
            switch (key)
            {
                case Key.H: _previewNavigator.Left(); return true;
                case Key.L: _previewNavigator.Right(); return true;
                case Key.J: _previewNavigator.Down(); return true;
                case Key.K: _previewNavigator.Up(); return true;
                case Key.W: _previewNavigator.NextWord(); return true;
                case Key.B: _previewNavigator.PrevWord(); return true;
                case Key.E: _previewNavigator.EndWord(); return true;
                case Key.D0: _previewNavigator.LineStartHome(); return true;
                case Key.D4: _previewNavigator.LineEnd(); return true; // $ (Shift+4)
                case Key.G:
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    {
                        _previewNavigator.Bottom();
                    }
                    else
                    {
                        _previewNavigator.Top();
                    }
                    return true;
                default:
                    return false;
            }
        }

        private void ApplyPreviewCaret()
        {
            _previewBox.CaretPosition = CaretToPointer(_previewNavigator.Caret);
            // Scroll so the caret's line is visible (RichTextBox has no ScrollToCaret).
            try
            {
                Rect caretRect = _previewBox.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                _previewBox.ScrollToVerticalOffset(caretRect.Top);
            }
            catch
            {
                // scroll is best-effort
            }
        }

        /// <summary>
        /// Replaces the preview content: tokenizes <paramref name="content"/> into colored runs
        /// (one paragraph per line) and feeds the same plain text to the motion navigator, so the
        /// caret index model and the rendered document stay in lockstep.
        /// </summary>
        private void SetPreviewContent(string content)
        {
            content ??= string.Empty;
            _previewNavigator.SetText(content);

            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)),
            };

            var segments = SyntaxHighlighter.Segment(content);
            var para = NewPreviewParagraph();
            foreach (var segment in segments)
            {
                string[] lines = segment.Text.Split('\n');
                for (int k = 0; k < lines.Length; k++)
                {
                    if (k > 0)
                    {
                        doc.Blocks.Add(para);
                        para = NewPreviewParagraph();
                    }
                    if (lines[k].Length > 0)
                    {
                        para.Inlines.Add(new Run(lines[k])
                        {
                            Foreground = ColorFor(segment.Category),
                        });
                    }
                }
            }
            doc.Blocks.Add(para);

            _previewBox.Document = doc;
            _previewBox.CaretPosition = doc.ContentStart;
            _previewBox.ScrollToHome();
            NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview tokens={segments.Count}");
        }

        private static Paragraph NewPreviewParagraph()
        {
            return new Paragraph
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
            };
        }

        private static SolidColorBrush ColorFor(SyntaxCategory category)
        {
            // One-Dark/GitHub-dark palette that matches the overlay's dark chrome.
            switch (category)
            {
                case SyntaxCategory.Keyword: return new SolidColorBrush(Color.FromRgb(0xc7, 0x92, 0xea));
                case SyntaxCategory.String: return new SolidColorBrush(Color.FromRgb(0x98, 0xc3, 0x79));
                case SyntaxCategory.Comment: return new SolidColorBrush(Color.FromRgb(0x7f, 0x84, 0x8e));
                case SyntaxCategory.Number: return new SolidColorBrush(Color.FromRgb(0xd1, 0x9a, 0x66));
                default: return new SolidColorBrush(Color.FromRgb(0xc9, 0xd1, 0xd9));
            }
        }

        /// <summary>
        /// Maps a plain-text caret index (the navigator's model) to a <see cref="TextPointer"/>
        /// inside the currently rendered document. The document is one paragraph per line with one
        /// run per token, so the mapping walks paragraphs/runs accumulating plain-text length —
        /// each paragraph boundary counts as the '\n' between lines.
        /// </summary>
        private TextPointer CaretToPointer(int index)
        {
            FlowDocument doc = _previewBox.Document;
            int plain = 0;
            int blockCount = doc.Blocks.Count;
            int blockIndex = 0;

            foreach (var block in doc.Blocks)
            {
                bool lastBlock = ++blockIndex == blockCount;
                if (block is Paragraph para)
                {
                    foreach (var inline in para.Inlines)
                    {
                        if (inline is Run run)
                        {
                            int len = run.Text.Length;
                            if (index <= plain + len)
                            {
                                return run.ContentStart.GetPositionAtOffset(index - plain, LogicalDirection.Forward);
                            }
                            plain += len;
                        }
                    }
                }
                if (!lastBlock)
                {
                    plain += 1; // the '\n' separating this line from the next
                }
            }

            return doc.ContentEnd;
        }

        private void FocusTargetUi()
        {
            if (_focusTarget == FocusTarget.Preview)
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
                    _promptBox.IsReadOnly = false;
                    UpdateModeLabel();
                    FocusPrompt();
                    ApplyInsertCaret(0);
                    break;
                case OverlayAction.EnterInsertAppend:
                    handled = true;
                    _promptBox.IsReadOnly = false;
                    UpdateModeLabel();
                    FocusPrompt();
                    ApplyInsertCaret(1);
                    break;
                case OverlayAction.EnterInsertStart:
                    handled = true;
                    _promptBox.IsReadOnly = false;
                    UpdateModeLabel();
                    FocusPrompt();
                    ApplyInsertCaret(2);
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
                NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}key={e.Key} mode={mode} handled={handled}");
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
                    System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.Telescope}OnSelected failed: {ex.Message}");
                }
            }
        }
    }
}
