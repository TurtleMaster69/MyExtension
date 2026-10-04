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
        private GridViewColumn? _flexibleColumn;   // FALLBACK-PATH-ONLY: the single display column (D-B3)
        private double _fixedWidthSum;             // FALLBACK-PATH-ONLY: sum of the fixed columns' widths
        private readonly ContentControl _previewHost;

        // D3: the bottom Grid's results column is PIXEL-sized (ApplyWindowWidth owns it); the
        // preview column is Star and takes the rest (never below PreviewMinWidth — the window
        // grows instead, the user's R4).
        private readonly ColumnDefinition _resultsColumnDef;

        // D4: the char widths aligned with the last RebuildColumns' visible columns — RebuildRows
        // truncates against them. UI-thread-only, like all overlay state.
        private int[] _activeCharWidths = Array.Empty<int>();

        /// <summary>The results/row foreground (the old box's #d3d7de).</summary>
        private static readonly Brush ResultForeground = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde));

        /// <summary>The selected-row highlight (a dark blue from the accent #8b9dc3's hue family —
        /// the light #d3d7de text contrasts on it in BOTH the active and inactive states; the
        /// default system highlight is what made the selected text unreadable).</summary>
        private static readonly Brush SelectionHighlightBrush = new SolidColorBrush(Color.FromRgb(0x2d, 0x4a, 0x75));

        /// <summary>The selected-row text (white — contrasts with the dark highlight).</summary>
        private static readonly Brush SelectionForegroundBrush = new SolidColorBrush(Colors.White);

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

        // Feature 7: the three panes (the contract + host under Overlay/Utils/Panes/). The machine
        // (_focusTargetModel) stays the SINGLE focus decision source; the panes only apply it.
        private readonly PromptPane _promptPane;
        private readonly ListPane _listPane;
        private readonly PreviewPane _previewPane;
        private readonly PaneHost _paneHost;
        private bool _applyingSelection;   // the SelectionChanged re-entrancy guard (the native-arrow sync)

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

            // Prompt TextBox — the query input, docked BOTTOM (full width; the plan-D1 geometry:
            // the Input strip at the bottom, the List|Preview region above it). It is editable in
            // insert mode and read-only in normal mode (normal-mode keys are intercepted at the
            // window level).
            var promptHost = new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)),
                BorderThickness = new Thickness(0, 1, 0, 0),
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
            _promptPane = new PromptPane(promptHost, FocusPrompt);   // Feature 7: the Input pane (FocusPrompt = its focus-entry)
            DockPanel.SetDock(_promptPane.Content, Dock.Bottom);
            _layout.Children.Add(_promptPane.Content);

            // Results list (the columned list) and the editor-view file preview beside it. The
            // MIDDLE region is a Grid: the List (col 0, the existing 260 fixed) + the Preview
            // (col 1, star) sit ABOVE the full-width Input (the bottom-docked prompt).
            var bottom = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1f, 0x24)),
            };
            _resultsColumnDef = new ColumnDefinition { Width = new GridLength(260) };   // ApplyWindowWidth owns it (D3)
            bottom.ColumnDefinitions.Add(_resultsColumnDef);
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _resultsList = new ListView
            {
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
            // properties — set via the static accessors). Horizontal is DISABLED (plan D5):
            // the widths fit + the window grows (D2/D3), so nothing is clipped or hidden —
            // the list must never scroll horizontally. Vertical stays Auto.
            ScrollViewer.SetHorizontalScrollBarVisibility(_resultsList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_resultsList, ScrollBarVisibility.Auto);
            // Rows are selected programmatically (j/k); a click must never move WPF keyboard focus
            // off the prompt nor desync the ListView's selection from _keyHandler.SelectedIndex —
            // the old TextBox ignored clicks, and this preserves that (D-B6).
            var rowStyle = new Style(typeof(ListViewItem));
            rowStyle.Setters.Add(new Setter(FocusableProperty, false));
            rowStyle.Setters.Add(new Setter(IsTabStopProperty, false));
            rowStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            rowStyle.Setters.Add(new Setter(IsHitTestVisibleProperty, false));
            // D6 — the selection contrast (the user's R2: "the text under highlighted item is not
            // visible since its same color"). The brush-key overrides recolor the DEFAULT template's
            // selection visuals (the canonical recipe — no re-template); the IsSelected trigger pins
            // the colors explicitly. Active and inactive get the SAME pinned pair (the overlay is
            // modal — both states must contrast).
            _resultsList.Resources[SystemColors.HighlightBrushKey] = SelectionHighlightBrush;
            _resultsList.Resources[SystemColors.HighlightTextBrushKey] = SelectionForegroundBrush;
            _resultsList.Resources[SystemColors.ControlBrushKey] = SelectionHighlightBrush;      // the inactive selection
            _resultsList.Resources[SystemColors.ControlTextBrushKey] = SelectionForegroundBrush; // the inactive text
            rowStyle.Setters.Add(new Setter(ForegroundProperty, ResultForeground));
            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(BackgroundProperty, SelectionHighlightBrush));
            selectedTrigger.Setters.Add(new Setter(ForegroundProperty, SelectionForegroundBrush));
            rowStyle.Triggers.Add(selectedTrigger);
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

            // GridView does not star-size: the columned path's widths come from Compute
            // (ApplyComputedColumnWidths, D2); the SizeChanged re-application is a layout-timing
            // safety net (the Pixel-sized results column makes it a no-op in practice). The
            // fallback display column keeps the landed fill (ApplyFlexibleColumnWidth, BP-B3).
            _resultsList.SizeChanged += (_, _) => ApplyComputedColumnWidths();

            // Right-click a column header -> the column chooser (BP-B5). Wired ONCE on the ListView
            // so it survives column rebuilds; handledEventsToo so an upstream handle can't hide it.
            _resultsList.AddHandler(UIElement.MouseRightButtonUpEvent,
                new MouseButtonEventHandler(OnHeaderRightClick), handledEventsToo: true);

            // Feature 7: the native-arrow sync. The ListView's own Up/Down/PageUp/... handling moves its
            // SelectedIndex (the overlay does NOT claim the arrows — ListKeyMap); this handler adopts the
            // native index into the untouched OverlayKeyHandler by replaying the delta through the
            // machine's own Up/Down gestures (PaneSelectionSync). Guarded: programmatic selections
            // (ApplySelection) must not re-enter.
            _resultsList.SelectionChanged += (_, _) => OnListNativeSelectionChanged();

            _listPane = new ListPane(_resultsList);   // Feature 7: Focusable=true + the accent-line chrome (inside the pane)
            Grid.SetColumn(_listPane.Content, 0);
            bottom.Children.Add(_listPane.Content);

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
            _previewPane = new PreviewPane(_previewHost, ActivatePreviewEditor);   // Feature 7: the pane wraps the host (Focusable=false stays on the slot)
            Grid.SetColumn(_previewPane.Content, 1);
            bottom.Children.Add(_previewPane.Content);

            _paneHost = new PaneHost(_promptPane, _listPane, _previewPane);   // the registry order is PINNED: Input, List, Preview
            _paneHost.PaneClicked += OnPaneClicked;   // left-click normalization (the machine decides)
            SizeChanged += (_, _) => RefreshPaneLayout();   // rev 1: the geometric focus needs current rects (§1.2)

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
                FocusInitialPane();
            };

            ContentRendered += (_, _) =>
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        FocusInitialPane();   // the initial pane is Input (plan §1.5 — no focus target= line at open)
                        RefreshPaneLayout();  // the rects must exist BEFORE the first key can land (§1.2)
                    }),
                    DispatcherPriority.ApplicationIdle);

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
            SyncFinderColumns();   // resolve the catalog + visibility model first (D3 needs the visible set)
            ApplyWindowWidth();    // D3: the width before the centering math below
            RenderResults();       // rebuilds the columns + rows at the final width (columnsDirty is true on a fresh instance)

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

        /// <summary>
        /// D3: the overlay width scales with the visible columns —
        /// max(DefaultOverlayWidth, NeededWidth + scrollbar + preview + chrome), capped by the
        /// work area — and the Pixel-sized results column gets the complement (the preview never
        /// shrinks below PreviewMinWidth). Recomputed at open and on every chooser toggle.
        /// </summary>
        private void ApplyWindowWidth()
        {
            IReadOnlyList<ResultColumn> visible = VisibleColumns();
            double workArea = SystemParameters.WorkArea.Width;
            Width = ColumnWidths.WindowWidth(ColumnWidths.DefaultOverlayWidth, visible, workArea);
            _resultsColumnDef.Width = new GridLength(ColumnWidths.ResultsListWidth(Width));
        }

        /// <summary>
        /// D2: applies the computed column widths to the GridView — Compute at the results list's
        /// content width (the Pixel column minus the vertical scrollbar), EVERY column (including
        /// the absorber) set from the result; the char widths are kept for the row truncation
        /// (RebuildRows). The fallback display column keeps the landed fill. With the Pixel-sized
        /// results column the SizeChanged re-application is a no-op (the estimate is exact; the
        /// window is NoResize) — it is a layout-timing safety net only.
        /// </summary>
        private void ApplyComputedColumnWidths()
        {
            if (_useFallbackDisplayColumn)
            {
                ApplyFlexibleColumnWidth();   // the fallback: the landed single-column fill
                return;
            }

            IReadOnlyList<ResultColumn> visible = VisibleColumns();
            if (visible.Count == 0 || _gridView.Columns.Count == 0) return;
            double available = ColumnWidths.ResultsListWidth(Width) - ColumnWidths.VerticalScrollbarWidth;
            IReadOnlyList<double> px = ColumnWidths.Compute(available, visible);
            var chars = new int[px.Count];
            for (int i = 0; i < px.Count; i++)
            {
                if (i < _gridView.Columns.Count) _gridView.Columns[i].Width = px[i];
                chars[i] = (int)(px[i] / ColumnWidths.PixelsPerChar);
            }

            _activeCharWidths = chars;
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
                _gridView.Columns.Add(MakeColumn(visible[i].Header, i));
            }

            ApplyComputedColumnWidths();   // D2: Compute owns ALL column widths (fixed + absorber)
        }

        /// <summary>Builds one GridViewColumn: visible header text + an index-bound cell template
        /// (Cascadia 13, ellipsis-trimmed — the Telescope `…` pattern). The cell text INHERITS the
        /// row foreground (no per-cell brush — it would defeat the selection trigger, the R2 bug).</summary>
        private GridViewColumn MakeColumn(string header, int cellIndex)
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new Binding($"[{cellIndex}]"));
            factory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Code, Consolas"));
            factory.SetValue(TextBlock.FontSizeProperty, 13.0);
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
                    // D4: each cell shortened to its column's computed char width by the
                    // column's OWN truncation kind (Tail for path-like, End for text).
                    row = ResultRowCells.Compute(entry, visible, _activeCharWidths).ToArray();
                }
                cells[i] = row;
                rows.Add(new ResultRow(row));
            }
            _lastRowCells = cells;
            _resultsList.ItemsSource = rows;
        }

        private void ApplySelection()
        {
            _applyingSelection = true;
            try
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
            finally
            {
                _applyingSelection = false;
            }
        }

        /// <summary>Adopts a NATIVE ListView selection move (the live arrows) into the overlay's
        /// model: replays the index delta through the untouched OverlayKeyHandler (normal mode — the
        /// pane invariant guarantees the List pane is never focused in insert mode) and re-renders.
        /// The _applyingSelection guard keeps programmatic selections (ApplySelection, ItemsSource
        /// resets) out.</summary>
        private void OnListNativeSelectionChanged()
        {
            if (_applyingSelection || !IsOpen)
            {
                return;
            }
            if (_focusTargetModel.Current != FocusTarget.List)
            {
                return;   // only the native-list path
            }
            int to = _resultsList.SelectedIndex;
            if (to < 0)
            {
                return;   // an ItemsSource reset / cleared selection — nothing to adopt
            }
            int steps = PaneSelectionSync.Steps(_selectedIndex, to);
            if (steps == 0)
            {
                return;
            }
            for (int i = 0; i < Math.Abs(steps); i++)
            {
                _keyHandler.Handle(steps > 0 ? OverlayKey.Down : OverlayKey.Up);
            }
            RenderResults();   // re-reads the machine's synced index; the guarded ApplySelection no-ops
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
            FocusPane(FocusTarget.Input);   // Feature 7: the machine moves to Input + the pane focuses the prompt
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

            // No tab semantics: Tab would move WPF focus without the pane machine knowing (the
            // machine and the real focus would desync). Swallowed on every pane (plan §1.3 R5).
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                return;
            }

            string mode = _keyHandler.IsNormalMode ? "normal" : "insert";
            bool hasCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            // 1. The pane-focus machine FIRST (the tunneling interceptor dispatches before the
            //    focused control's own handling): Ctrl+H/J/K/L move focus GEOMETRICALLY —
            //    left/down/up/right (the PaneNavigationEngine over the pane rects, plan §1.2);
            //    Escape in the preview returns to the list. Delegated to the pure focus-target
            //    state machine.
            PaneFocusKey gesture = FocusTargetModel.MapKey(e.Key, hasCtrl);
            var focusAction = _focusTargetModel.Handle(gesture);
            if (focusAction != FocusTargetAction.None)
            {
                e.Handled = true;
                if (focusAction == FocusTargetAction.NoOp)
                {
                    // A direction with no pane: consumed, nothing moves, NO wrap — the m47-style
                    // outcome diagnostic makes the no-op traceable (and e2e-assertable, plan §1.2).
                    TelescopeLog.Log($"focus no-op: no pane {FocusTargetModel.DirectionName(gesture)} from {_focusTargetModel.Current}");
                }
                else
                {
                    FocusPane(_focusTargetModel.Current);
                }
                return;
            }

            // 2. Dispatch by the FOCUSED pane (the pinned consume-vs-fallthrough contract, plan §1.3).
            switch (_focusTargetModel.Current)
            {
                case FocusTarget.Preview:
                    // Preview: vim motions navigate the code read-only; unconsumed keys fall through
                    // to the editor's own handling (arrows scroll, Ctrl+scroll zooms).
                    bool handled = HandlePreviewKey(e.Key);
                    if (handled)
                    {
                        e.Handled = true;
                        ApplyPreviewCaret();
                        TelescopeLog.Log(PreviewDiagnostics.Caret(_previewNavigator.Caret, _previewNavigator.LineNumber));
                    }
                    base.OnPreviewKeyDown(e);
                    return;

                case FocusTarget.List:
                    RouteListKey(e, mode);
                    return;

                default: // FocusTarget.Input — the prompt (the pre-pane behavior, unchanged)
                    RouteInputKey(e, mode);
                    return;
            }
        }

        /// <summary>The Input pane's key path (the pre-pane prompt path, verbatim): normal-mode
        /// prompt motions, then the overlay state machine; unhandled keys reach the TextBox beneath
        /// (R1/R2 — typing in insert mode is NEVER consumed).</summary>
        private void RouteInputKey(KeyEventArgs e, string mode)
        {
            if (_keyHandler.IsNormalMode && TryPromptMotion(e.Key))
            {
                e.Handled = true;
                base.OnPreviewKeyDown(e);
                return;
            }

            var action = _keyHandler.Handle(MapKey(e.Key));
            ApplyAction(action, mode, e);

            // Always continue routing: handled keys were swallowed above (e.Handled = true), but
            // unhandled keys (e.g. typing in insert mode) must reach the TextBox beneath.
            base.OnPreviewKeyDown(e);
        }

        /// <summary>The List pane's key path (Feature 7, R3): the SAME untouched OverlayKeyHandler
        /// via ListKeyMap — the selection gestures claimed; the native arrows NOT claimed (they fall
        /// through to the ListView, whose SelectionChanged the sync adopts).</summary>
        private void RouteListKey(KeyEventArgs e, string mode)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var action = _keyHandler.Handle(ListKeyMap.Map(e.Key, shift));
            ApplyAction(action, mode, e);
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

        /// <summary>One focus change: the machine's decision + the M-M7 diagnostic + the UI apply.
        /// The SINGLE path for key-driven AND click-driven AND restore-driven focus changes. Logs
        /// exactly one <c>focus target=&lt;token&gt;</c> line per change (never at open — plan §1.5).</summary>
        private void FocusPane(FocusTarget target)
        {
            _focusTargetModel.Focus(target);
            TelescopeLog.Log($"focus target={target}");
            ApplyFocusTarget(target);
        }

        /// <summary>Applies a focus decision to the UI: the pinned mode-exit rule (plan §1.4) + the
        /// pane activation. The machine decided; this only applies.</summary>
        private void ApplyFocusTarget(FocusTarget target)
        {
            if (FocusTargetModel.ExitsInsert(target) && !_keyHandler.IsNormalMode)
            {
                // Insert mode owns the keyboard only on the Input pane; leaving the prompt mid-insert
                // would strand typing — exit insert via the mode machine's own path (the synthetic
                // Escape is NOT logged as a key= line).
                _keyHandler.Handle(OverlayKey.Escape);
                UpdateModeLabel();
                _promptBox.IsReadOnly = true;
                ApplyPromptCaretStyle();
            }
            _paneHost.Activate(target);
        }

        /// <summary>The Preview pane's focus-entry (the old FocusTargetUi preview branch, unchanged):
        /// the editor's VisualElement takes keyboard focus; with nothing to preview the prompt stays
        /// focused (the machine stays Preview — the preview motions no-op; Escape returns to List).</summary>
        private void ActivatePreviewEditor()
        {
            if (_previewHost.Content != null)
            {
                _previewEditor?.Focus();
                ApplyPreviewCaret();
            }
            else
            {
                FocusPrompt();
            }
        }

        /// <summary>The open-time pane activation: the initial pane is Input (the machine's default —
        /// pinned by the unit tests). NO focus target= line at open (plan §1.5): the machine is AT
        /// Input, no transition happened; the first key/click logs. The prompt focus itself logs the
        /// unchanged <c>Focus prompt => ...</c> line.</summary>
        private void FocusInitialPane() => _paneHost.Activate(FocusTarget.Input);

        /// <summary>Measures the panes' layout rects (overlay DIP coordinates) and pushes them into
        /// the focus machine — the GEOMETRIC directional move's input (rev 1, plan §1.2). The pane's
        /// visual-tree position is the single source of truth; the machine never reads WPF. Runs on
        /// SizeChanged + ContentRendered, BEFORE the first key can land; until then an empty layout
        /// makes every directional move a safe no-op. The registry order is preserved (the
        /// tie-break's iteration order).</summary>
        private void RefreshPaneLayout()
        {
            var rects = new List<KeyValuePair<FocusTarget, PaneRect>>();
            foreach (IPane pane in _paneHost.Panes)
            {
                Rect bounds = pane.Content.TransformToVisual(this)
                    .TransformBounds(new Rect(pane.Content.RenderSize));
                rects.Add(new KeyValuePair<FocusTarget, PaneRect>(
                    pane.Id,
                    new PaneRect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height)));
            }
            _focusTargetModel.SetLayout(rects);
        }

        /// <summary>The left-click normalization (plan §1.3 R0 + AC4): the pane host reports the
        /// click; the machine decides; the same diagnostic + apply as the key path.</summary>
        private void OnPaneClicked(FocusTarget id)
        {
            if (!IsOpen)
            {
                return;
            }
            FocusPane(id);
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
                    FocusPane(FocusTarget.Input);    // keys must land back in the prompt (the machine's Current must match the focused pane)
                }
            };
            _chooserMenu = menu;
            _chooserMenuOpen = true;
            menu.PlacementTarget = _resultsList;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        /// <summary>Applies one chooser toggle: flips the model, recomputes the D3 window width
        /// for the NEW visible set, marks the columns dirty, re-renders (which rebuilds the
        /// GridViewColumns in catalog order and logs the NEW <c>results columns=</c> line).</summary>
        private void ToggleColumn(string id)
        {
            if (_activeVisibilityModel == null) return;
            _activeVisibilityModel.Toggle(id);
            ApplyWindowWidth();      // D3: the width recomputed for the NEW visible set (before the rebuild)
            _columnsDirty = true;
            RenderResults();
        }
    }
}
