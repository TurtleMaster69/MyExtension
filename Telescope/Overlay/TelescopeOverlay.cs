using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    /// A Telescope-style overlay: a modal dialog with a TextBox prompt and a results list, with a
    /// small built-in vim mode. Normal mode intercepts <c>j/k/gg/G/Enter/Esc/q</c> (and
    /// <c>i/a/A/I</c> to enter insert); insert mode types into the prompt and live-filters the
    /// candidates with fzf. Selecting an entry calls the finder's <c>OnSelected</c>.
    ///
    /// <para/>
    /// <b>Preview pane:</b> a REAL read-only VS editor view (IWpfTextView) hosted via the
    /// <see cref="IPreviewEditor"/> seam (implemented host-side in MyExtension — the VS-SDK-coupled
    /// view creation stays out of this library). Roles Document+Interactive+Zoomable EXCLUDING
    /// Editable: the view is non-editable and VsVim never attaches (no insert mode). The old
    /// RichTextBox + SyntaxHighlighter FlowDocument render was retired with this migration.
    /// </summary>
    internal sealed class TelescopeOverlay : Window
    {
        private readonly FzfFilter _fzf;

        // ---- UI chrome ----
        private readonly TextBlock _modeLabel;
        private readonly DockPanel _layout;
        private readonly TextBox _promptBox;
        private readonly ListView _resultsList;
        private readonly GridView _gridView;
        private GridViewColumn? _flexibleColumn;   // the LAST visible flexible column (D-B3)
        private double _fixedWidthSum;             // sum of the fixed columns' assigned widths
        private readonly ContentControl _previewHost;

        /// <summary>The results/row foreground (the old box's #d3d7de).</summary>
        private static readonly Brush ResultForeground = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde));

        // ---- Finder / results state ----
        private IReadOnlyList<FinderEntry> _candidates = Array.Empty<FinderEntry>();
        private IReadOnlyList<FinderEntry> _results = Array.Empty<FinderEntry>();
        private IFinder? _activeFinder;
        private int _selectedIndex;

        // n12: last-rendered results reference + selection, so a no-change RenderResults skips
        // the results-box rebuild + preview reload (the caret/layout hot path).
        private IReadOnlyList<FinderEntry>? _lastRenderedResults;
        private int _lastRenderedSelectedIndex = -1;

        // ---- Columned results state (Section B) ----
        private string[][] _lastRowCells = Array.Empty<string[]>();
        private bool _columnsDirty = true;            // fresh instance => first render rebuilds
        private string? _lastColumnsFinder;
        private IReadOnlyList<ResultColumn> _activeCatalog = Array.Empty<ResultColumn>();
        private ColumnVisibilityModel? _activeVisibilityModel;
        private bool _useFallbackDisplayColumn;
        private ContextMenu? _chooserMenu;
        private bool _chooserMenuOpen;

        // Column-visibility state, per finder, for the PROCESS lifetime: TelescopeController builds
        // a fresh overlay per open (a WPF Window cannot re-show), so per-instance state would
        // silently reset the user's column choices on every open. UI-thread-only access (the
        // overlay's whole lifecycle is on the UI thread).
        private static readonly Dictionary<string, ColumnVisibilityModel> VisibilityByFinder = new();

        private static readonly string[] FallbackDisplayIds = { "display" };

        // ---- Prompt mode / rendering state ----
        private readonly OverlayKeyHandler _keyHandler = new();
        private readonly TextMotionNavigator _previewNavigator = new();
        private readonly TextMotionNavigator _promptNavigator = new();
        private readonly IPreviewEditor? _previewEditor;
        private readonly ResultMapper _resultMapper = new();
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

        // Line caret brush for insert mode (white block in normal mode comes from
        // BlockCaretStyle.ApplyCaretStyle, which creates its own brush).
        private static readonly Brush PromptLineCaretBrush = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde));

        // Where the overlay's keyboard focus currently lives: the results list (default, where
        // j/k select) or the file preview (where h/l/j/k/w/b/e/gg/G navigate the code read-only).
        // Pure state machine (unit-tested); the overlay only applies the resulting focus.
        private readonly FocusTargetModel _focusTargetModel = new();

        public TelescopeOverlay(FzfFilter fzf, Func<IPreviewEditor>? previewEditorFactory = null)
        {
            _fzf = fzf ?? throw new ArgumentNullException(nameof(fzf));
            _previewEditor = previewEditorFactory?.Invoke();   // one host per overlay; disposed on close

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

            // Results list (the columned list) and the editor-view file preview beside it. The
            // bottom area is a Grid: results on the left (narrower), preview on the right.
            var bottom = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1f, 0x24)),
            };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            _resultsList = new ListView
            {
                Focusable = false,   // the overlay's key routing is unchanged: the prompt owns focus
                IsTabStop = false,
                Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1f, 0x24)),
                BorderThickness = new Thickness(0),
                Foreground = ResultForeground,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 13,
                SelectionMode = SelectionMode.Single,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            // Attached ScrollViewer properties (no C# object-initializer syntax for attached
            // properties — set via the static accessors).
            ScrollViewer.SetHorizontalScrollBarVisibility(_resultsList, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_resultsList, ScrollBarVisibility.Auto);
            // Rows are selected programmatically (j/k); a click must never move WPF keyboard focus
            // off the prompt nor desync the ListView's selection from _keyHandler.SelectedIndex —
            // the old TextBox ignored clicks, and this preserves that (D-B6).
            var rowStyle = new Style(typeof(ListViewItem));
            rowStyle.Setters.Add(new Setter(FocusableProperty, false));
            rowStyle.Setters.Add(new Setter(IsTabStopProperty, false));
            rowStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            rowStyle.Setters.Add(new Setter(IsHitTestVisibleProperty, false));
            _resultsList.ItemContainerStyle = rowStyle;

            // Visible column headers (the D1 requirement), themed like the title bar. Headers are
            // non-focusable so a left-click cannot steal keyboard focus either.
            _gridView = new GridView();
            var headerStyle = new Style(typeof(GridViewColumnHeader));
            headerStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x28, 0x2c, 0x34))));
            headerStyle.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x8b, 0x9d, 0xc3))));
            headerStyle.Setters.Add(new Setter(FocusableProperty, false));
            _gridView.ColumnHeaderContainerStyle = headerStyle;
            _resultsList.View = _gridView;

            // GridView does not star-size: the LAST visible (flexible) column is filled by a
            // SizeChanged handler (ApplyFlexibleColumnWidth, BP-B3); the others are fixed-width.
            _resultsList.SizeChanged += (_, _) => ApplyFlexibleColumnWidth();

            // Right-click a column header -> the column chooser (BP-B5). Wired ONCE on the ListView
            // so it survives column rebuilds; handledEventsToo so an upstream handle can't hide it.
            _resultsList.AddHandler(UIElement.MouseRightButtonUpEvent,
                new MouseButtonEventHandler(OnHeaderRightClick), handledEventsToo: true);

            Grid.SetColumn(_resultsList, 0);
            bottom.Children.Add(_resultsList);

            // Read-only file preview beside the results list: a REAL VS editor view (IWpfTextView,
            // read-only — roles Document+Interactive+Zoomable, no Editable, so VsVim never
            // attaches), created/reused by the IPreviewEditor host and swapped into this slot per
            // selection. The old RichTextBox + SyntaxHighlighter FlowDocument render is retired
            // (the editor's own classifiers do the highlighting).
            _previewHost = new ContentControl
            {
                Focusable = false,   // focus goes to the editor's VisualElement (FocusTargetUi), never the slot
                IsTabStop = false,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x18)),
                BorderThickness = new Thickness(1, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)),
            };
            Grid.SetColumn(_previewHost, 1);
            bottom.Children.Add(_previewHost);

            _layout.Children.Add(bottom);

            root.Child = _layout;
            Content = root;

            // Trace incoming characters so the harness can confirm whether text reaches the
            // prompt (the core "does typing filter" assertion).
            PreviewTextInput += (_, e) =>
            {
                if (IsOpen)
                {
                    TelescopeLog.Log($"textinput='{DiagnosticLog.SanitizeText(e.Text)}' focused={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}");
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
                if (!IsOpen)
                {
                    return;
                }
                if (_chooserMenuOpen)
                {
                    // The chooser ContextMenu may transiently take window activation; do not treat
                    // that as focus loss. menu.Closed restores the guard (closing the overlay if
                    // truly inactive).
                    return;
                }
                TelescopeLog.Log($"deactivated -> closing overlay");
                CloseOverlay();
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
        public async Task ShowOverlayAsync(IFinder finder, System.Drawing.Rectangle? centerRect, IntPtr ownerHwnd = default)
        {
            // VSTHRD109: an async method must switch to the UI thread rather than throw.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

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
            // N38/BP-52: the availability probe runs off the UI thread; await it here.
            if (!await _fzf.IsAvailableAsync())
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
            _previewEditor?.Dispose();   // close the editor view + dispose the document (UI thread)
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
            TelescopeLog.Log($"promptChanged query='{DiagnosticLog.SanitizeText(query)}'");
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
            try { results = await finder.GetCandidatesAsync(query) ?? Array.Empty<FinderEntry>(); }
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

                    var items = _resultMapper.MapBack(matched, snapshot);

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
                // N41/BP-55: Format returns the PREFIXED line; NeoVisualLog.Log adds no prefix.
                NeoVisualLog.Log(FilterFailureLog.Format(ex));
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

        // ---- Columned results machinery (Section B) ----

        /// <summary>
        /// One rendered results row: the visible cells, in catalog order. Bound by index
        /// (<c>Binding("[i]")</c>) from the GridView cell templates.
        /// </summary>
        private sealed class ResultRow
        {
            private readonly string[] _cells;
            public ResultRow(string[] cells) { _cells = cells; }
            public string this[int index] => index >= 0 && index < _cells.Length ? _cells[index] : string.Empty;
        }

        /// <summary>
        /// Resolves the active finder's catalog + visibility model. An unknown finder name falls
        /// back to a single flexible <c>display</c> column (the entry's Display text) so the list
        /// never renders blank — all six built-in finders have catalog entries; this is defense.
        /// </summary>
        private void SyncFinderColumns()
        {
            string name = _activeFinder?.Name ?? string.Empty;
            _activeCatalog = FinderColumns.ForFinder(name);
            _useFallbackDisplayColumn = _activeCatalog.Count == 0;
            if (_useFallbackDisplayColumn)
            {
                _activeVisibilityModel = null;   // fallback: a single display column, no model
                return;
            }
            if (!VisibilityByFinder.TryGetValue(name, out var model))
            {
                model = new ColumnVisibilityModel(_activeCatalog);
                VisibilityByFinder[name] = model;
            }
            _activeVisibilityModel = model;
        }

        /// <summary>The visible ResultColumns in catalog order (empty in fallback mode).</summary>
        private IReadOnlyList<ResultColumn> VisibleColumns()
        {
            if (_activeVisibilityModel == null || _useFallbackDisplayColumn) return Array.Empty<ResultColumn>();
            IReadOnlyList<string> ids = _activeVisibilityModel.VisibleIds;
            return _activeCatalog.Where(c => ids.Contains(c.Id)).ToArray();
        }

        /// <summary>The visible column ids for the <c>results columns=</c> diagnostic.</summary>
        private IReadOnlyList<string> VisibleIds()
        {
            if (_activeVisibilityModel == null || _useFallbackDisplayColumn) return FallbackDisplayIds;
            return _activeVisibilityModel.VisibleIds;
        }

        private void RebuildColumns()
        {
            _gridView.Columns.Clear();
            _flexibleColumn = null;
            _fixedWidthSum = 0;

            if (_useFallbackDisplayColumn)
            {
                var fallback = MakeColumn("Result", 0);
                _flexibleColumn = fallback;
                _gridView.Columns.Add(fallback);
                ApplyFlexibleColumnWidth();
                return;
            }

            IReadOnlyList<ResultColumn> visible = VisibleColumns();
            for (int i = 0; i < visible.Count; i++)
            {
                ResultColumn col = visible[i];
                bool flexible = col.Width == ResultColumnWidth.Flexible;
                GridViewColumn gvc = MakeColumn(col.Header, i);
                if (!flexible)
                {
                    gvc.Width = Math.Max(24, col.WidthChars * 8);
                    _fixedWidthSum += gvc.Width;
                }
                else if (_flexibleColumn == null)
                {
                    _flexibleColumn = gvc;   // the FIRST flexible column fills the remaining width
                }
                else
                {
                    gvc.Width = 80;          // defensive: only one flexible column per catalog today
                    _fixedWidthSum += 80;
                }
                _gridView.Columns.Add(gvc);
            }
            ApplyFlexibleColumnWidth();
        }

        /// <summary>Builds one GridViewColumn: visible header text + an index-bound cell template
        /// (Cascadia 13, the overlay's foreground, ellipsis-trimmed — the Telescope `…` pattern).</summary>
        private GridViewColumn MakeColumn(string header, int cellIndex)
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new Binding($"[{cellIndex}]"));
            factory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Code, Consolas"));
            factory.SetValue(TextBlock.FontSizeProperty, 13.0);
            factory.SetValue(TextBlock.ForegroundProperty, ResultForeground);
            factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            template.VisualTree = factory;
            return new GridViewColumn { Header = header, CellTemplate = template };
        }

        /// <summary>GridView does not star-size: keeps the flexible (last visible) column filled to
        /// the ListView's remaining width. No flexible column → no fill (all fixed widths stand).</summary>
        private void ApplyFlexibleColumnWidth()
        {
            if (_flexibleColumn == null || _resultsList.ActualWidth <= 0) return;
            double width = Math.Max(120, _resultsList.ActualWidth - _fixedWidthSum - 18); // 18px v-scrollbar
            if (double.IsNaN(_flexibleColumn.Width) || Math.Abs(_flexibleColumn.Width - width) > 0.5)
            {
                _flexibleColumn.Width = width;
            }
        }

        private void RebuildRows()
        {
            IReadOnlyList<ResultColumn>? visible = _useFallbackDisplayColumn ? null : VisibleColumns();
            var rows = new List<ResultRow>(_results.Count);
            var cells = new string[_results.Count][];
            for (int i = 0; i < _results.Count; i++)
            {
                FinderEntry entry = _results[i];
                string[] row;
                if (visible == null)
                {
                    row = new[] { entry.Display };   // fallback: the legacy single text column
                }
                else
                {
                    row = new string[visible.Count];
                    for (int c = 0; c < visible.Count; c++) row[c] = visible[c].Getter(entry.Payload);
                }
                cells[i] = row;
                rows.Add(new ResultRow(row));
            }
            _lastRowCells = cells;
            _resultsList.ItemsSource = rows;
        }

        private void ApplySelection()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _resultsList.Items.Count)
            {
                _resultsList.SelectedIndex = -1;
                return;
            }
            _resultsList.SelectedIndex = _selectedIndex;
            if (_resultsList.SelectedItem != null)
            {
                _resultsList.ScrollIntoView(_resultsList.SelectedItem);
            }
        }

        private void RenderResults()
        {
            _selectedIndex = _keyHandler.SelectedIndex;
            bool resultsChanged = !ReferenceEquals(_lastRenderedResults, _results);
            bool selectionChanged = _lastRenderedSelectedIndex != _selectedIndex;
            bool columnsChanged = _columnsDirty
                || !string.Equals(_lastColumnsFinder, _activeFinder?.Name, StringComparison.Ordinal);
            if (columnsChanged)
            {
                SyncFinderColumns();
                RebuildColumns();
                _columnsDirty = false;
                _lastColumnsFinder = _activeFinder?.Name;
                resultsChanged = true;   // rows must rebuild against the new visible column set
            }
            if (resultsChanged || selectionChanged)
            {
                _lastRenderedResults = _results;
                _lastRenderedSelectedIndex = _selectedIndex;
                if (resultsChanged)
                {
                    RebuildRows();
                }
                ApplySelection();
                LoadPreviewForSelection();
            }
            // D5: the column-set diagnostic — logged on EVERY render and after every chooser toggle
            // (the toggle path re-enters RenderResults). Byte-exact format:
            //   [Telescope] results columns=<comma-separated visible ids, catalog order, no spaces>
            TelescopeLog.Log($"results columns={ResultsFormatter.ColumnsIdList(VisibleIds())}");
            // Byte-stable harness contract (format unchanged from the TextBox era; boxText is now
            // the rendered row-text length of the visible cells — ResultsFormatter.RenderedTextLength):
            //   [Telescope] results count=<n> selected=<m> boxText=<len>
            TelescopeLog.Log($"results count={_results.Count} selected={_selectedIndex} boxText={ResultsFormatter.RenderedTextLength(_lastRowCells)}");
        }

        /// <summary>
        /// Loads the currently selected result's file into the preview pane (when the finder entry
        /// carries a file path in its payload) as a read-only editor view. For code-issue entries
        /// the preview caret jumps to the issue's line. Resets otherwise to the top.
        /// </summary>
        private void LoadPreviewForSelection()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _results.Count)
            {
                ClearPreview();
                return;
            }

            object payload = _results[_selectedIndex].Payload;
            if (payload is IFileLocation location)
            {
                ShowPreview(location);
                return;
            }

            ClearPreview();
        }

        private void ShowPreview(IFileLocation location)
        {
            // Detach the old view BEFORE Show may close it (a rebuild disposes the previous
            // view+document; a closed element must not stay in the visual tree).
            _previewHost.Content = null;

            PreviewEditorResult result = _previewEditor?.Show(location) ?? PreviewEditorResult.Empty;
            _previewHost.Content = result.Element;
            _previewNavigator.SetText(result.Text);   // ONE source of truth: the buffer's snapshot text

            // Position the caret: the hit line, else the top (the old SetText reset-to-top semantics).
            if (location.LineNumber > 0)
            {
                _previewNavigator.MoveToLine(location.LineNumber);
            }
            else
            {
                _previewNavigator.MoveTo(0);
            }
            // The navigator's target, CLAMPED to the editor text before it crosses the seam (the
            // mtime-drift guard — an unclamped offset makes SnapshotPoint throw); the 1-based line
            // of the CLAMPED offset is the scroll/diagnostic value (LineIndex.LineOf — the
            // navigator's own convention; equal to LineNumber in every non-drift case).
            int caret = PreviewCaretMap.Offset(result.Text, _previewNavigator.Caret);
            _previewEditor?.ApplyCaret(caret);
            if (location.LineNumber > 0)
            {
                TelescopeLog.Log(PreviewDiagnostics.Caret(caret, PreviewCaretMap.Line(result.Text, caret)));
            }
        }

        private void ClearPreview()
        {
            _previewHost.Content = null;
            _previewNavigator.SetText(string.Empty);
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
                case CaretPlacement.AfterCaret: // a (append): caret one position after the current caret
                    _promptNavigator.InsertAfter();
                    break;
                default: // i (current): no motion, caret clamped to current
                    break;
            }
            _promptBox.CaretIndex = _promptNavigator.Caret;
        }

        /// <summary>Enters insert mode and places the caret per <paramref name="placement"/>.</summary>
        private void EnterInsert(CaretPlacement placement)
        {
            // Flip the key handler's mode BEFORE FocusPrompt() reads it. The i/a/A/I tap routes
            // through TryPromptMotion -> EnterInsert directly (not _keyHandler.Handle), so without
            // this the handler is still in normal mode and FocusPrompt() re-sets IsReadOnly=true
            // and logs mode=normal. Idempotent when already called from ApplyAction.
            _keyHandler.EnterInsertMode(placement);
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
                    TelescopeLog.Log(PreviewDiagnostics.Caret(_previewNavigator.Caret, _previewNavigator.LineNumber));
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
        /// R1: the insert placements (a/A/I) are routed here via the <c>insertPlacement</c> out
        /// param — a→Current, A→End, I→Start, i→Current — bypassing OverlayKeyHandler's
        /// OverlayKey.A→End / OverlayKey.I→Current mapping (the R1 placement bug). R44: the
        /// <see cref="TextMotionNavigator.SetText"/> rebuild is skipped when the prompt text is
        /// unchanged (normal-mode motions do not change it), so no LineIndex is rebuilt per keystroke.
        /// </summary>
        private bool TryPromptMotion(Key key)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!PromptMotionRouter.ShouldConsume(key, shift, out CaretPlacement? insertPlacement))
            {
                if (insertPlacement != null)
                {
                    EnterInsert(insertPlacement.Value);
                    return true;
                }
                return false;
            }

            string text = _promptBox.Text;
            if (_promptNavigator.Text != text)
            {
                _promptNavigator.SetText(text);
            }
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
            // R1: the preview surface keeps the FULL motion set (j/k/g/G navigate the code).
            if (!PromptMotionRouter.ShouldConsume(key, shift, out _, previewSurface: true))
            {
                return false;
            }
            return TextMotionDispatcher.Handle(key, shift, _previewNavigator, out _);
        }

        private void ApplyPreviewCaret()
        {
            _previewEditor?.ApplyCaret(_previewNavigator.Caret);
        }

        private void FocusTargetUi()
        {
            if (_focusTargetModel.Current == FocusTarget.Preview)
            {
                if (_previewHost.Content != null)
                {
                    _previewEditor?.Focus();   // the editor's VisualElement takes keyboard focus
                    ApplyPreviewCaret();
                }
                else
                {
                    // Nothing to preview: keep the prompt focused. The model's Preview target makes
                    // the preview motions no-ops over the empty navigator; Escape returns to List.
                    FocusPrompt();
                }
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
                case OverlayAction.EnterInsertAfter:
                    handled = true;
                    EnterInsert(CaretPlacement.AfterCaret);
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

        // ================================================================
        // Header column chooser (right-click a GridViewColumnHeader)
        // ================================================================

        /// <summary>Right-click on a column header opens the column chooser. Wired once on the
        /// ListView (BP-B2) so it survives column rebuilds; the OriginalSource may be the header's
        /// inner TextBlock/Thumb, so walk up the visual tree to the header.</summary>
        private void OnHeaderRightClick(object sender, MouseButtonEventArgs e)
        {
            if (!IsOpen || FindHeader(e.OriginalSource as DependencyObject) == null)
            {
                return;
            }
            e.Handled = true;
            OpenColumnChooser();
        }

        private static GridViewColumnHeader? FindHeader(DependencyObject? source)
        {
            while (source != null && source is not GridViewColumnHeader)
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as GridViewColumnHeader;
        }

        /// <summary>Builds the chooser from the ACTIVE finder's FULL catalog (all columns, catalog
        /// order, checkmark per visibility) and opens it at the mouse. Toggling re-enters
        /// RenderResults, which rebuilds the visible GridViewColumns in catalog order and logs the
        /// NEW <c>results columns=</c> line.</summary>
        private void OpenColumnChooser()
        {
            if (_activeVisibilityModel == null) return;   // fallback column — nothing to choose
            var menu = new ContextMenu();
            foreach (ResultColumn col in _activeCatalog)
            {
                var item = new MenuItem
                {
                    Header = col.Header,
                    IsCheckable = true,
                    IsChecked = _activeVisibilityModel.VisibleIds.Contains(col.Id),
                };
                string id = col.Id;   // capture per item
                item.Click += (_, _) => ToggleColumn(id);
                menu.Items.Add(item);
            }
            menu.Closed += (_, _) =>
            {
                _chooserMenuOpen = false;
                _chooserMenu = null;
                if (!IsActive)
                {
                    CloseOverlay();   // restore the stale-overlay guard if activation was lost meanwhile
                }
                else
                {
                    FocusPrompt();    // keys must land back in the prompt
                }
            };
            _chooserMenu = menu;
            _chooserMenuOpen = true;
            menu.PlacementTarget = _resultsList;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        /// <summary>Applies one chooser toggle: flips the model, marks the columns dirty, re-renders
        /// (which rebuilds the GridViewColumns in catalog order and logs the NEW columns= line).</summary>
        private void ToggleColumn(string id)
        {
            if (_activeVisibilityModel == null) return;
            _activeVisibilityModel.Toggle(id);
            _columnsDirty = true;
            RenderResults();
        }
    }
}
