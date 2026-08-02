using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using ClipScribe.Core.Abstractions;

namespace ClipScribe.Windows;

public sealed class HistoryWindow : Wpf.Window
{
    private readonly IClipRepository _repository;
    private readonly Controls.ListBox _listBox;

    public HistoryWindow(IClipRepository repository)
    {
        _repository = repository;

        Title = "clip-scribe history";
        Width = 840;
        Height = 560;
        MinWidth = 640;
        MinHeight = 360;

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

        _listBox = new Controls.ListBox();
        _listBox.MouseDoubleClick += (_, _) => PasteSelectedClip();

        root.Children.Add(toolbar);
        root.Children.Add(_listBox);

        Content = root;

        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var clips = await _repository.GetRecentAsync(200);
        var rows = clips.Select(c =>
            $"[{c.CreatedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}] " +
            $"{(string.IsNullOrWhiteSpace(c.SourceApp) ? "unknown" : c.SourceApp)} · {c.Content}").ToList();

        _listBox.ItemsSource = rows;
    }

    private void PasteSelectedClip()
    {
        if (_listBox.SelectedItem is not string row)
        {
            return;
        }

        var separatorIndex = row.IndexOf(" · ", StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex + 3 >= row.Length)
        {
            return;
        }

        var content = row[(separatorIndex + 3)..];
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        Wpf.Clipboard.SetText(content);
        Close();
    }
}
