using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Telescope
{
    /// <summary>
    /// A Telescope-style overlay popup: a borderless, dark panel centered over the VS host that
    /// shows a prompt row (finder name + a filter <see cref="TextBox"/>) above a ranked results
    /// list. Typing filters the candidates via fzf on a background task; Enter selects; Esc closes.
    ///
    /// <para/>
    /// <b>Why a plain WPF Window (not a ToolWindowPane):</b> the original Telescope is an overlay
    /// centered in the editor, so we mirror that with a borderless <see cref="Window"/> — no tool
    /// window registration, docking, or VSIX asset work required for the UI itself.
    ///
    /// <para/>
    /// <b>Focus / hook interplay:</b> while open this window owns keyboard focus. The global
    /// keyboard hook must be told to pass keys through (the controller flips a flag) so Space,
    /// hjkl and the leader key reach this window instead of being swallowed by the extension.
    ///
    /// <para/>
    /// <b>Threading:</b> the filter runs off the UI thread (fzf subprocess, no VS calls) and
    /// results are marshaled back with <see cref="Dispatcher"/>. Enter selection runs on the UI
    /// thread and may touch VS.
    /// </summary>
    internal sealed class TelescopeOverlay : Window
    {
        private readonly FzfFilter _fzf;
        private readonly TextBox _prompt;
        private readonly ListBox _results;
        private readonly TextBlock _promptLabel;

        private IReadOnlyList<FinderEntry> _candidates = Array.Empty<FinderEntry>();
        private CancellationTokenSource? _filterCts;
        private IFinder? _activeFinder;

        public TelescopeOverlay(FzfFilter fzf)
        {
            _fzf = fzf ?? throw new ArgumentNullException(nameof(fzf));

            Title = "Telescope";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Focusable = true;
            SizeToContent = SizeToContent.Manual;

            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2b)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
            };

            var layout = new DockPanel();

            var promptBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x28, 0x2c, 0x34)),
                Padding = new Thickness(12, 10, 12, 10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x38, 0x41)),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            DockPanel.SetDock(promptBar, Dock.Top);

            var promptStack = new StackPanel { Orientation = Orientation.Horizontal };

            _promptLabel = new TextBlock
            {
                Text = "Telescope >",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x9d, 0xc3)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };

            _prompt = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde)),
                CaretBrush = new SolidColorBrush(Color.FromRgb(0x8b, 0x9d, 0xc3)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 15,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            promptStack.Children.Add(_promptLabel);
            promptStack.Children.Add(_prompt);
            promptBar.Child = promptStack;

            _results = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x2b)),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xd3, 0xd7, 0xde)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 14,
                MaxHeight = 320,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_results, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_results, ScrollBarVisibility.Auto);

            layout.Children.Add(promptBar);
            layout.Children.Add(_results);
            root.Child = layout;
            Content = root;

            Width = 620;
            Height = 400;

            _prompt.TextChanged += OnPromptChanged;
            _prompt.KeyDown += OnPromptKeyDown;
            _results.PreviewKeyDown += OnResultsKeyDown;
        }

        /// <summary>True while the overlay is open and owns keyboard focus.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Raised when the overlay is closed (by Esc or programmatically).</summary>
        public event EventHandler? OverlayClosed;

        /// <summary>
        /// Opens the overlay for the given finder, centered over <paramref name="centerRect"/>
        /// (screen pixels; the VS main-window rect) or the work area if none. Runs on the UI
        /// thread. Candidates are gathered on the UI thread; filtering happens in the background
        /// as the user types.
        /// </summary>
        public void ShowOverlay(IFinder finder, System.Drawing.Rectangle? centerRect)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _activeFinder = finder;
            _promptLabel.Text = finder.Name + " >";
            _candidates = finder.GetCandidates();

            _prompt.Clear();
            RefreshResults(string.Empty);

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
            Show();
            _prompt.Focus();
            _prompt.SelectAll();
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
            OverlayClosed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // Lost focus (clicked elsewhere in VS) => dismiss, like Telescope.
            CloseOverlay();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseOverlay();
                return;
            }
            base.OnPreviewKeyDown(e);
        }

        private void OnPromptChanged(object sender, TextChangedEventArgs e)
        {
            RefreshResults(_prompt.Text);
        }

        private void RefreshResults(string query)
        {
            CancelFilter();
            _filterCts = new CancellationTokenSource();
            var token = _filterCts.Token;
            var snapshot = _candidates;

            _ = FilterAndUpdateAsync(snapshot, query, token);
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

                var byDisplay = snapshot.ToDictionary(x => x.Display, StringComparer.OrdinalIgnoreCase);
                var items = matched.Select(m => byDisplay.TryGetValue(m, out var e) ? e : new FinderEntry(m)).ToList();

                _results.ItemsSource = items;
                if (items.Count > 0)
                {
                    _results.SelectedIndex = 0;
                }
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

        private void OnPromptKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    CloseOverlay();
                    return;
                case Key.Enter:
                    e.Handled = true;
                    SelectCurrent();
                    return;
                case Key.Down:
                    e.Handled = true;
                    MoveSelection(1);
                    return;
                case Key.Up:
                    e.Handled = true;
                    MoveSelection(-1);
                    return;
                case Key.J:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        e.Handled = true;
                        MoveSelection(1);
                    }
                    return;
                case Key.K:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        e.Handled = true;
                        MoveSelection(-1);
                    }
                    return;
            }
        }

        private void OnResultsKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    SelectCurrent();
                    return;
                case Key.Escape:
                    e.Handled = true;
                    CloseOverlay();
                    return;
            }
        }

        private void MoveSelection(int delta)
        {
            if (_results.Items.Count == 0)
            {
                return;
            }
            int idx = Math.Max(0, _results.SelectedIndex);
            idx = (idx + delta + _results.Items.Count) % _results.Items.Count;
            _results.SelectedIndex = idx;
            _results.ScrollIntoView(_results.SelectedItem);
        }

        private void SelectCurrent()
        {
            var finder = _activeFinder;
            var entry = _results.SelectedItem as FinderEntry;
            CloseOverlay();
            if (finder != null && entry != null)
            {
                try
                {
                    finder.OnSelected(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Telescope] OnSelected failed: {ex.Message}");
                }
            }
        }
    }
}
