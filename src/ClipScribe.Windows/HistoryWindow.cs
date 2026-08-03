using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;
using Controls = System.Windows.Controls;
using Input = System.Windows.Input;
using Wpf = System.Windows;

namespace ClipScribe.Windows;

public sealed class HistoryWindow : Wpf.Window
{
    private readonly IClipRepository _repository;
    private readonly Win32PasteBackService? _pasteBackService;

    private readonly Controls.TextBox _searchBox;
    private readonly Controls.ListBox _listBox;

    private IntPtr _pasteTargetWindow = IntPtr.Zero;

    public HistoryWindow(IClipRepository repository, Win32PasteBackService? pasteBackService)
    {
        _repository = repository;
        _pasteBackService = pasteBackService;

        Title = "clip-scribe history";
        Width = 860;
        Height = 580;
        MinWidth = 700;
        MinHeight = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;

        var root = new Controls.DockPanel
        {
            LastChildFill = true,
            Margin = new Wpf.Thickness(12)
        };

        var toolbar = new Controls.StackPanel
        {
            Orientation = Controls.Orientation.Horizontal,
            Margin = new Wpf.Thickness(0, 0, 0, 8)
        };
        Controls.DockPanel.SetDock(toolbar, Controls.Dock.Top);

        var refreshButton = new Controls.Button
        {
            Content = "Refresh",
            Padding = new Wpf.Thickness(10, 4, 10, 4),
            Margin = new Wpf.Thickness(0, 0, 8, 0)
        };
        refreshButton.Click += async (_, _) => await RefreshAsync();

        var clearButton = new Controls.Button
        {
            Content = "Clear history",
            Padding = new Wpf.Thickness(10, 4, 10, 4)
        };
        clearButton.Click += async (_, _) =>
        {
            await _repository.ClearAsync();
            await RefreshAsync();
        };

        toolbar.Children.Add(refreshButton);
        toolbar.Children.Add(clearButton);

        var searchLabel = new Controls.TextBlock
        {
            Text = "Search",
            Margin = new Wpf.Thickness(0, 0, 0, 4),
            FontWeight = Wpf.FontWeights.SemiBold
        };
        Controls.DockPanel.SetDock(searchLabel, Controls.Dock.Top);

        _searchBox = new Controls.TextBox
        {
            Height = 30,
            Margin = new Wpf.Thickness(0, 0, 0, 8),
            ToolTip = "Type to filter history. Use ↑/↓ to navigate, Enter to paste, Esc to dismiss."
        };
        Controls.DockPanel.SetDock(_searchBox, Controls.Dock.Top);

        _searchBox.TextChanged += async (_, _) => await RefreshAsync(_searchBox.Text);
        _searchBox.KeyDown += async (_, args) => await HandleSearchBoxKeyDownAsync(args);

        _listBox = new Controls.ListBox();
        _listBox.MouseDoubleClick += async (_, _) => await PasteSelectedClipAsync();
        _listBox.KeyDown += async (_, args) => await HandleListKeyDownAsync(args);

        root.Children.Add(toolbar);
        root.Children.Add(searchLabel);
        root.Children.Add(_searchBox);
        root.Children.Add(_listBox);

        Content = root;

        Loaded += async (_, _) => await RefreshAsync();
    }

    public void SetPasteTargetWindow(IntPtr targetWindow)
    {
        _pasteTargetWindow = targetWindow;
    }

    public void ClearPasteTargetWindow()
    {
        _pasteTargetWindow = IntPtr.Zero;
    }

    public void FocusSearch()
    {
        _searchBox.Focus();
        _searchBox.SelectAll();
    }

    public async Task RefreshAsync()
    {
        await RefreshAsync(_searchBox.Text);
    }

    public async Task RefreshAsync(string? query)
    {
        var clips = await _repository.SearchAsync(query, take: 200, prioritizePinned: true);
        var items = clips.Select(ClipRow.FromRecord).ToList();

        _listBox.ItemsSource = items;
        _listBox.DisplayMemberPath = nameof(ClipRow.DisplayText);

        if (items.Count > 0)
        {
            _listBox.SelectedIndex = 0;
            _listBox.ScrollIntoView(_listBox.SelectedItem);
        }
    }

    private async Task HandleSearchBoxKeyDownAsync(Input.KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Input.Key.Down:
                MoveSelection(+1);
                args.Handled = true;
                break;
            case Input.Key.Up:
                MoveSelection(-1);
                args.Handled = true;
                break;
            case Input.Key.Enter:
                args.Handled = true;
                await PasteSelectedClipAsync();
                break;
            case Input.Key.Escape:
                args.Handled = true;
                Close();
                break;
        }
    }

    private async Task HandleListKeyDownAsync(Input.KeyEventArgs args)
    {
        if (args.Key == Input.Key.Enter)
        {
            args.Handled = true;
            await PasteSelectedClipAsync();
            return;
        }

        if (args.Key == Input.Key.Escape)
        {
            args.Handled = true;
            Close();
        }
    }

    private void MoveSelection(int direction)
    {
        if (_listBox.Items.Count == 0)
        {
            return;
        }

        var current = _listBox.SelectedIndex;
        if (current < 0)
        {
            current = 0;
        }

        var next = Math.Clamp(current + direction, 0, _listBox.Items.Count - 1);
        _listBox.SelectedIndex = next;
        _listBox.ScrollIntoView(_listBox.SelectedItem);
    }

    private async Task PasteSelectedClipAsync()
    {
        if (_listBox.SelectedItem is not ClipRow row)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Content))
        {
            return;
        }

        Wpf.Clipboard.SetText(row.Content);

        var targetWindow = _pasteTargetWindow;
        Close();

        if (_pasteBackService is not null && targetWindow != IntPtr.Zero)
        {
            await Task.Delay(70);
            _ = _pasteBackService.TryPasteFromClipboard(targetWindow);
        }
    }

    private sealed record ClipRow(long Id, string Content, string DisplayText)
    {
        public static ClipRow FromRecord(ClipRecord record)
        {
            var source = string.IsNullOrWhiteSpace(record.SourceApp) ? "unknown" : record.SourceApp;
            var contentPreview = Abbreviate(record.Content, 180);
            var display = $"[{record.CreatedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}] {source} · {contentPreview}";

            return new ClipRow(record.Id, record.Content, display);
        }

        private static string Abbreviate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return string.Concat(value.AsSpan(0, maxLength - 1), "…");
        }
    }
}
