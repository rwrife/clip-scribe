using System.Net.Http;
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
    private readonly ILocalAiTextTransformClient? _localAiTransformClient;

    private readonly Controls.TextBox _searchBox;
    private readonly Controls.ListBox _listBox;
    private readonly Controls.Button _pasteQueuedButton;

    private readonly Controls.MenuItem _pasteMenuItem;
    private readonly Controls.MenuItem _pastePlainMenuItem;
    private readonly Controls.MenuItem _transformMenuItem;
    private readonly Controls.MenuItem _togglePinMenuItem;
    private readonly Controls.MenuItem _toggleQueueMenuItem;
    private readonly Controls.MenuItem _pasteQueuedMenuItem;
    private readonly Controls.MenuItem _addSnippetMenuItem;
    private readonly Controls.MenuItem _editSnippetMenuItem;
    private readonly Controls.MenuItem _deleteSnippetMenuItem;
    private readonly Controls.MenuItem _deleteClipMenuItem;

    private readonly List<long> _queuedClipIds = new();
    private readonly Dictionary<long, string> _transformedContentByClipId = new();

    private LocalAiSettings _localAiSettings;
    private bool _localAiTemporarilyUnavailable;

    private IntPtr _pasteTargetWindow = IntPtr.Zero;
    private List<ClipRow> _currentRows = new();

    public HistoryWindow(
        IClipRepository repository,
        Win32PasteBackService? pasteBackService,
        ILocalAiTextTransformClient? localAiTransformClient,
        LocalAiSettings localAiSettings)
    {
        _repository = repository;
        _pasteBackService = pasteBackService;
        _localAiTransformClient = localAiTransformClient;
        _localAiSettings = LocalAiSettings.Normalize(localAiSettings);

        Title = "clip-scribe history";
        Width = 980;
        Height = 620;
        MinWidth = 760;
        MinHeight = 460;
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
            Padding = new Wpf.Thickness(10, 4, 10, 4),
            Margin = new Wpf.Thickness(0, 0, 8, 0)
        };
        clearButton.Click += async (_, _) =>
        {
            await _repository.ClearAsync();
            _queuedClipIds.Clear();
            _transformedContentByClipId.Clear();
            await RefreshAsync();
        };

        var addSnippetButton = new Controls.Button
        {
            Content = "New snippet",
            Padding = new Wpf.Thickness(10, 4, 10, 4),
            Margin = new Wpf.Thickness(0, 0, 8, 0)
        };
        addSnippetButton.Click += async (_, _) => await CreateSnippetAsync();

        _pasteQueuedButton = new Controls.Button
        {
            Content = "Paste collected (0)",
            Padding = new Wpf.Thickness(10, 4, 10, 4),
            IsEnabled = false
        };
        _pasteQueuedButton.Click += async (_, _) => await PasteQueuedClipsAsync();

        toolbar.Children.Add(refreshButton);
        toolbar.Children.Add(clearButton);
        toolbar.Children.Add(addSnippetButton);
        toolbar.Children.Add(_pasteQueuedButton);

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
            ToolTip = "Type to filter. Enter=paste, Ctrl+Enter=paste plain text, Ctrl+Space=collect for multi-paste, Esc=close."
        };
        Controls.DockPanel.SetDock(_searchBox, Controls.Dock.Top);

        _searchBox.TextChanged += async (_, _) => await RefreshAsync(_searchBox.Text);
        _searchBox.KeyDown += async (_, args) => await HandleSearchBoxKeyDownAsync(args);

        _listBox = new Controls.ListBox();
        _listBox.MouseDoubleClick += async (_, _) => await PasteSelectedClipAsync();
        _listBox.KeyDown += async (_, args) => await HandleListKeyDownAsync(args);

        var menu = new Controls.ContextMenu();

        _pasteMenuItem = new Controls.MenuItem { Header = "Paste" };
        _pasteMenuItem.Click += async (_, _) => await PasteSelectedClipAsync();

        _pastePlainMenuItem = new Controls.MenuItem { Header = "Paste as plain text" };
        _pastePlainMenuItem.Click += async (_, _) => await PasteSelectedClipAsync(forcePlainText: true);

        _transformMenuItem = new Controls.MenuItem { Header = "Transform" };
        foreach (var preset in TransformPreset.Defaults)
        {
            var presetItem = new Controls.MenuItem { Header = preset.Name };
            presetItem.Click += async (_, _) => await TransformSelectedClipAsync(preset);
            _transformMenuItem.Items.Add(presetItem);
        }

        _togglePinMenuItem = new Controls.MenuItem { Header = "Pin" };
        _togglePinMenuItem.Click += async (_, _) => await TogglePinForSelectedAsync();

        _toggleQueueMenuItem = new Controls.MenuItem { Header = "Collect for multi-paste" };
        _toggleQueueMenuItem.Click += async (_, _) => await ToggleQueueForSelectedAsync();

        _pasteQueuedMenuItem = new Controls.MenuItem { Header = "Paste collected" };
        _pasteQueuedMenuItem.Click += async (_, _) => await PasteQueuedClipsAsync();

        _addSnippetMenuItem = new Controls.MenuItem { Header = "New snippet" };
        _addSnippetMenuItem.Click += async (_, _) => await CreateSnippetAsync();

        _editSnippetMenuItem = new Controls.MenuItem { Header = "Edit snippet" };
        _editSnippetMenuItem.Click += async (_, _) => await EditSelectedSnippetAsync();

        _deleteSnippetMenuItem = new Controls.MenuItem { Header = "Delete snippet" };
        _deleteSnippetMenuItem.Click += async (_, _) => await DeleteSelectedSnippetAsync();

        _deleteClipMenuItem = new Controls.MenuItem { Header = "Delete clip" };
        _deleteClipMenuItem.Click += async (_, _) => await DeleteSelectedClipAsync();

        menu.Items.Add(_pasteMenuItem);
        menu.Items.Add(_pastePlainMenuItem);
        menu.Items.Add(_transformMenuItem);
        menu.Items.Add(new Controls.Separator());
        menu.Items.Add(_togglePinMenuItem);
        menu.Items.Add(_toggleQueueMenuItem);
        menu.Items.Add(_pasteQueuedMenuItem);
        menu.Items.Add(new Controls.Separator());
        menu.Items.Add(_addSnippetMenuItem);
        menu.Items.Add(_editSnippetMenuItem);
        menu.Items.Add(_deleteSnippetMenuItem);
        menu.Items.Add(new Controls.Separator());
        menu.Items.Add(_deleteClipMenuItem);
        menu.Opened += (_, _) => RefreshContextMenuState();

        _listBox.ContextMenu = menu;

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

    public void SetLocalAiSettings(LocalAiSettings settings)
    {
        _localAiSettings = LocalAiSettings.Normalize(settings);
        _localAiTemporarilyUnavailable = false;
        RefreshContextMenuState();
    }

    public async Task RefreshAsync()
    {
        await RefreshAsync(_searchBox.Text);
    }

    public async Task RefreshAsync(string? query)
    {
        var selectedId = (_listBox.SelectedItem as ClipRow)?.Id;

        var clips = await _repository.SearchAsync(query, take: 250, prioritizePinned: true);
        var byId = clips.ToDictionary(c => c.Id, c => c);

        _queuedClipIds.RemoveAll(id => !byId.ContainsKey(id));

        foreach (var clipId in _transformedContentByClipId.Keys.ToList())
        {
            if (!byId.ContainsKey(clipId))
            {
                _transformedContentByClipId.Remove(clipId);
            }
        }

        var queueOrderById = _queuedClipIds
            .Select((id, idx) => new { id, order = idx + 1 })
            .ToDictionary(x => x.id, x => x.order);

        _currentRows = clips
            .Select(record =>
            {
                var effectiveContent = _transformedContentByClipId.TryGetValue(record.Id, out var transformed)
                    ? transformed
                    : record.Content;

                return ClipRow.FromRecord(
                    record,
                    effectiveContent,
                    queueOrderById.TryGetValue(record.Id, out var order) ? order : null);
            })
            .ToList();

        _listBox.ItemsSource = _currentRows;
        _listBox.DisplayMemberPath = nameof(ClipRow.DisplayText);

        if (_currentRows.Count > 0)
        {
            var index = selectedId is null
                ? 0
                : _currentRows.FindIndex(x => x.Id == selectedId.Value);

            if (index < 0)
            {
                index = 0;
            }

            _listBox.SelectedIndex = index;
            _listBox.ScrollIntoView(_listBox.SelectedItem);
        }

        RefreshContextMenuState();
        RefreshQueuedActionsVisualState();
    }

    private async Task HandleSearchBoxKeyDownAsync(Input.KeyEventArgs args)
    {
        if (args.Key == Input.Key.Enter)
        {
            args.Handled = true;
            var forcePlain = Input.Keyboard.Modifiers.HasFlag(Input.ModifierKeys.Control);
            await PasteSelectedClipAsync(forcePlain);
            return;
        }

        if (args.Key == Input.Key.Space && Input.Keyboard.Modifiers.HasFlag(Input.ModifierKeys.Control))
        {
            args.Handled = true;
            await ToggleQueueForSelectedAsync();
            return;
        }

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
            var forcePlain = Input.Keyboard.Modifiers.HasFlag(Input.ModifierKeys.Control);
            await PasteSelectedClipAsync(forcePlain);
            return;
        }

        if (args.Key == Input.Key.Space && Input.Keyboard.Modifiers.HasFlag(Input.ModifierKeys.Control))
        {
            args.Handled = true;
            await ToggleQueueForSelectedAsync();
            return;
        }

        if (args.Key == Input.Key.Delete)
        {
            args.Handled = true;
            if ((_listBox.SelectedItem as ClipRow)?.IsSnippet == true)
            {
                await DeleteSelectedSnippetAsync();
            }
            else
            {
                await DeleteSelectedClipAsync();
            }

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

    private async Task PasteSelectedClipAsync(bool forcePlainText = false)
    {
        if (_listBox.SelectedItem is not ClipRow row)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Content))
        {
            return;
        }

        await PasteContentsAsync(new[] { row.Content }, forcePlainText);
    }

    private async Task TransformSelectedClipAsync(TransformPreset preset)
    {
        if (_listBox.SelectedItem is not ClipRow row)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Content)
            || _localAiTransformClient is null
            || !_localAiSettings.IsEnabledAndConfigured)
        {
            return;
        }

        try
        {
            var transformed = await _localAiTransformClient.TransformAsync(
                _localAiSettings,
                preset.Instruction,
                row.Content);

            var choice = ShowTransformChoiceDialog(preset.Name, transformed);
            if (choice == TransformApplyChoice.Cancel)
            {
                return;
            }

            var updated = choice == TransformApplyChoice.Replace
                ? transformed
                : string.Concat(row.Content, Environment.NewLine, Environment.NewLine, transformed);

            _transformedContentByClipId[row.Id] = NormalizePlainText(updated);
            await RefreshAsync(_searchBox.Text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _localAiTemporarilyUnavailable = true;
            RefreshContextMenuState();

            Wpf.MessageBox.Show(
                this,
                $"AI transform failed: {ex.Message}",
                "clip-scribe",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Warning);
        }
    }

    private TransformApplyChoice ShowTransformChoiceDialog(string presetName, string transformedText)
    {
        var preview = transformedText.Length <= 1600
            ? transformedText
            : string.Concat(transformedText.AsSpan(0, 1599), "…");

        var result = Wpf.MessageBox.Show(
            this,
            $"{presetName} output:\n\n{preview}\n\nYes = replace selected preview\nNo = append to selected preview\nCancel = discard",
            "clip-scribe",
            Wpf.MessageBoxButton.YesNoCancel,
            Wpf.MessageBoxImage.Question);

        return result switch
        {
            Wpf.MessageBoxResult.Yes => TransformApplyChoice.Replace,
            Wpf.MessageBoxResult.No => TransformApplyChoice.Append,
            _ => TransformApplyChoice.Cancel
        };
    }

    private async Task TogglePinForSelectedAsync()
    {
        if (_listBox.SelectedItem is not ClipRow row)
        {
            return;
        }

        if (row.IsSnippet)
        {
            return;
        }

        await _repository.SetPinnedAsync(row.Id, !row.IsPinned);
        await RefreshAsync(_searchBox.Text);
    }

    private async Task ToggleQueueForSelectedAsync()
    {
        if (_listBox.SelectedItem is not ClipRow row)
        {
            return;
        }

        var existingIndex = _queuedClipIds.IndexOf(row.Id);
        if (existingIndex >= 0)
        {
            _queuedClipIds.RemoveAt(existingIndex);
        }
        else
        {
            _queuedClipIds.Add(row.Id);
        }

        await RefreshAsync(_searchBox.Text);
    }

    private async Task PasteQueuedClipsAsync()
    {
        if (_queuedClipIds.Count == 0)
        {
            return;
        }

        var rowsById = _currentRows.ToDictionary(x => x.Id, x => x);
        var queuedRows = _queuedClipIds
            .Where(rowsById.ContainsKey)
            .Select(id => rowsById[id])
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .ToList();

        if (queuedRows.Count == 0)
        {
            return;
        }

        await PasteContentsAsync(queuedRows.Select(x => x.Content), forcePlainText: false);
    }

    private async Task CreateSnippetAsync()
    {
        var input = ShowSnippetEditor(
            title: "Create snippet",
            name: string.Empty,
            content: string.Empty,
            owner: this);

        if (input is null)
        {
            return;
        }

        await _repository.CreateSnippetAsync(input.Value.Name, input.Value.Content);
        await RefreshAsync(_searchBox.Text);
    }

    private async Task EditSelectedSnippetAsync()
    {
        if (_listBox.SelectedItem is not ClipRow row || !row.IsSnippet)
        {
            return;
        }

        var input = ShowSnippetEditor(
            title: "Edit snippet",
            name: row.SnippetName ?? string.Empty,
            content: row.Content,
            owner: this);

        if (input is null)
        {
            return;
        }

        await _repository.UpdateSnippetAsync(row.Id, input.Value.Name, input.Value.Content);
        _transformedContentByClipId.Remove(row.Id);
        await RefreshAsync(_searchBox.Text);
    }

    private async Task DeleteSelectedSnippetAsync()
    {
        if (_listBox.SelectedItem is not ClipRow row || !row.IsSnippet)
        {
            return;
        }

        await _repository.DeleteClipAsync(row.Id);
        _queuedClipIds.Remove(row.Id);
        _transformedContentByClipId.Remove(row.Id);
        await RefreshAsync(_searchBox.Text);
    }

    private async Task DeleteSelectedClipAsync()
    {
        if (_listBox.SelectedItem is not ClipRow row || row.IsSnippet)
        {
            return;
        }

        await _repository.DeleteClipAsync(row.Id);
        _queuedClipIds.Remove(row.Id);
        _transformedContentByClipId.Remove(row.Id);
        await RefreshAsync(_searchBox.Text);
    }

    private async Task PasteContentsAsync(IEnumerable<string> rawContents, bool forcePlainText)
    {
        var normalized = rawContents
            .Select(NormalizePlainText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (normalized.Count == 0)
        {
            return;
        }

        var payload = normalized.Count == 1
            ? normalized[0]
            : string.Join(Environment.NewLine, normalized);

        var targetWindow = _pasteTargetWindow;
        _queuedClipIds.Clear();
        Close();

        if (_pasteBackService is not null && targetWindow != IntPtr.Zero)
        {
            await Task.Delay(70);

            if (forcePlainText)
            {
                _ = _pasteBackService.TryTypeText(targetWindow, payload);
                return;
            }

            Wpf.Clipboard.Clear();
            Wpf.Clipboard.SetText(payload, Wpf.TextDataFormat.UnicodeText);
            _ = _pasteBackService.TryPasteFromClipboard(targetWindow);
            return;
        }

        Wpf.Clipboard.Clear();
        Wpf.Clipboard.SetText(payload, Wpf.TextDataFormat.UnicodeText);
    }

    private static string NormalizePlainText(string value)
        => value
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);

    private void RefreshContextMenuState()
    {
        var row = _listBox.SelectedItem as ClipRow;
        var hasSelection = row is not null;

        _pasteMenuItem.IsEnabled = hasSelection;
        _pastePlainMenuItem.IsEnabled = hasSelection;

        var canUseLocalAi = _localAiTransformClient is not null && _localAiSettings.IsEnabledAndConfigured;
        _transformMenuItem.Visibility = canUseLocalAi ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        _transformMenuItem.IsEnabled = canUseLocalAi && !_localAiTemporarilyUnavailable && hasSelection && !string.IsNullOrWhiteSpace(row?.Content);
        _transformMenuItem.Header = _localAiTemporarilyUnavailable
            ? "Transform (endpoint unavailable)"
            : "Transform";

        _togglePinMenuItem.IsEnabled = hasSelection && row is { IsSnippet: false };
        _togglePinMenuItem.Header = row is null
            ? "Pin"
            : row.IsSnippet
                ? "Snippet is always pinned"
                : row.IsPinned ? "Unpin" : "Pin";

        var isCollected = row is not null && _queuedClipIds.Contains(row.Id);
        _toggleQueueMenuItem.IsEnabled = hasSelection;
        _toggleQueueMenuItem.Header = isCollected
            ? "Remove from multi-paste collection"
            : "Collect for multi-paste";

        _pasteQueuedMenuItem.IsEnabled = _queuedClipIds.Count > 0;
        _pasteQueuedMenuItem.Header = $"Paste collected ({_queuedClipIds.Count})";

        _addSnippetMenuItem.IsEnabled = true;

        _editSnippetMenuItem.IsEnabled = row?.IsSnippet == true;
        _deleteSnippetMenuItem.IsEnabled = row?.IsSnippet == true;

        _deleteClipMenuItem.IsEnabled = hasSelection && row is { IsSnippet: false };
    }

    private void RefreshQueuedActionsVisualState()
    {
        _pasteQueuedButton.Content = $"Paste collected ({_queuedClipIds.Count})";
        _pasteQueuedButton.IsEnabled = _queuedClipIds.Count > 0;
    }

    private static SnippetInput? ShowSnippetEditor(string title, string name, string content, Wpf.Window owner)
    {
        SnippetInput? result = null;

        var dialog = new Wpf.Window
        {
            Title = title,
            Width = 520,
            Height = 380,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = Wpf.WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
            ResizeMode = Wpf.ResizeMode.CanResize
        };

        var root = new Controls.DockPanel
        {
            Margin = new Wpf.Thickness(12)
        };

        var buttonPanel = new Controls.StackPanel
        {
            Orientation = Controls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right,
            Margin = new Wpf.Thickness(0, 10, 0, 0)
        };
        Controls.DockPanel.SetDock(buttonPanel, Controls.Dock.Bottom);

        var saveButton = new Controls.Button
        {
            Content = "Save",
            MinWidth = 88,
            Margin = new Wpf.Thickness(0, 0, 8, 0),
            IsDefault = true
        };

        var cancelButton = new Controls.Button
        {
            Content = "Cancel",
            MinWidth = 88,
            IsCancel = true
        };

        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(cancelButton);

        var form = new Controls.Grid();
        form.RowDefinitions.Add(new Controls.RowDefinition { Height = Wpf.GridLength.Auto });
        form.RowDefinitions.Add(new Controls.RowDefinition { Height = Wpf.GridLength.Auto });
        form.RowDefinitions.Add(new Controls.RowDefinition { Height = Wpf.GridLength.Auto });
        form.RowDefinitions.Add(new Controls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var nameLabel = new Controls.TextBlock
        {
            Text = "Snippet name",
            FontWeight = Wpf.FontWeights.SemiBold
        };
        Controls.Grid.SetRow(nameLabel, 0);

        var nameBox = new Controls.TextBox
        {
            Margin = new Wpf.Thickness(0, 4, 0, 10),
            Text = name
        };
        Controls.Grid.SetRow(nameBox, 1);

        var contentLabel = new Controls.TextBlock
        {
            Text = "Content",
            FontWeight = Wpf.FontWeights.SemiBold
        };
        Controls.Grid.SetRow(contentLabel, 2);

        var contentBox = new Controls.TextBox
        {
            Margin = new Wpf.Thickness(0, 4, 0, 0),
            Text = content,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            TextWrapping = Wpf.TextWrapping.Wrap
        };
        Controls.Grid.SetRow(contentBox, 3);

        form.Children.Add(nameLabel);
        form.Children.Add(nameBox);
        form.Children.Add(contentLabel);
        form.Children.Add(contentBox);

        saveButton.Click += (_, _) =>
        {
            var normalizedName = nameBox.Text.Trim();
            var normalizedContent = contentBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                Wpf.MessageBox.Show(dialog, "Snippet name is required.", "clip-scribe", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
                nameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(normalizedContent))
            {
                Wpf.MessageBox.Show(dialog, "Snippet content is required.", "clip-scribe", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
                contentBox.Focus();
                return;
            }

            result = new SnippetInput(normalizedName, normalizedContent);
            dialog.DialogResult = true;
            dialog.Close();
        };

        cancelButton.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        root.Children.Add(buttonPanel);
        root.Children.Add(form);
        dialog.Content = root;

        _ = dialog.ShowDialog();
        return result;
    }

    private readonly record struct SnippetInput(string Name, string Content);

    private enum TransformApplyChoice
    {
        Replace,
        Append,
        Cancel
    }

    private sealed record TransformPreset(string Name, string Instruction)
    {
        public static IReadOnlyList<TransformPreset> Defaults { get; } =
        [
            new("Fix grammar", "Fix grammar and spelling while preserving meaning."),
            new("Summarize", "Summarize this text in concise bullet points."),
            new("Make polite", "Rewrite this text to sound more polite and professional."),
            new("Make formal", "Rewrite this text in a formal tone."),
            new("Make casual", "Rewrite this text in a casual, friendly tone."),
            new("To Markdown", "Convert this text into clean Markdown."),
            new("Extract action items", "Extract explicit action items as a bulleted list with owners when present."),
            new("Translate", "Translate this text to English while preserving meaning and formatting where possible.")
        ];
    }

    private sealed record ClipRow(
        long Id,
        string Content,
        bool IsPinned,
        bool IsSnippet,
        string? SnippetName,
        bool IsTransformed,
        int? QueueOrder,
        string DisplayText)
    {
        public static ClipRow FromRecord(ClipRecord record, string content, int? queueOrder)
        {
            var source = string.IsNullOrWhiteSpace(record.SourceApp) ? "unknown" : record.SourceApp;
            var contentPreview = Abbreviate(content, 160);
            var isTransformed = !string.Equals(content, record.Content, StringComparison.Ordinal);

            var badge = record.IsSnippet
                ? $"[SNIPPET:{record.SnippetName ?? "unnamed"}]"
                : record.IsPinned ? "[PIN]" : "[CLIP]";

            var queue = queueOrder is int order ? $"[{order}] " : string.Empty;
            var transformBadge = isTransformed ? "[AI] " : string.Empty;

            var display = $"{queue}{transformBadge}{badge} [{record.CreatedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}] {source} · {contentPreview}";

            return new ClipRow(
                Id: record.Id,
                Content: content,
                IsPinned: record.IsPinned,
                IsSnippet: record.IsSnippet,
                SnippetName: record.SnippetName,
                IsTransformed: isTransformed,
                QueueOrder: queueOrder,
                DisplayText: display);
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
