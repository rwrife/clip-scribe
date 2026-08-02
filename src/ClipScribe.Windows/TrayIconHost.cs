using System.Drawing;
using System.Windows.Forms;
using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Services;

namespace ClipScribe.Windows;

public sealed class TrayIconHost : IDisposable
{
    private const string AppName = "clip-scribe";

    private readonly CaptureRuntimeController _captureController;
    private readonly IClipRepository _repository;
    private readonly LaunchAtLoginService? _launchAtLoginService;
    private readonly Func<Task> _openHistoryAsync;
    private readonly Action _openSettings;
    private readonly Action _quit;

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private readonly ToolStripMenuItem _launchAtLoginItem;

    private bool _disposed;

    public TrayIconHost(
        CaptureRuntimeController captureController,
        IClipRepository repository,
        LaunchAtLoginService? launchAtLoginService,
        Func<Task> openHistoryAsync,
        Action openSettings,
        Action quit)
    {
        _captureController = captureController;
        _repository = repository;
        _launchAtLoginService = launchAtLoginService;
        _openHistoryAsync = openHistoryAsync;
        _openSettings = openSettings;
        _quit = quit;

        _pauseResumeItem = new ToolStripMenuItem();
        _pauseResumeItem.Click += async (_, _) => await TogglePauseAsync();

        _launchAtLoginItem = new ToolStripMenuItem("Launch at login")
        {
            CheckOnClick = true,
            Enabled = _launchAtLoginService is not null,
            Checked = _launchAtLoginService?.IsEnabled() ?? false
        };
        _launchAtLoginItem.Click += (_, _) =>
        {
            if (_launchAtLoginService is null)
            {
                return;
            }

            _launchAtLoginService.SetEnabled(_launchAtLoginItem.Checked);
        };

        var menu = new ContextMenuStrip();

        var openHistory = new ToolStripMenuItem("Open history");
        openHistory.Click += async (_, _) => await _openHistoryAsync();

        var clearHistory = new ToolStripMenuItem("Clear history");
        clearHistory.Click += async (_, _) => await ClearHistoryAsync();

        var settings = new ToolStripMenuItem("Settings");
        settings.Click += (_, _) => _openSettings();

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => _quit();

        menu.Items.Add(openHistory);
        menu.Items.Add(_pauseResumeItem);
        menu.Items.Add(clearHistory);
        menu.Items.Add(settings);
        menu.Items.Add(_launchAtLoginItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);
        menu.Opening += (_, _) => RefreshVisualState();

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += async (_, _) => await _openHistoryAsync();

        RefreshVisualState();
    }

    private async Task TogglePauseAsync()
    {
        await _captureController.TogglePausedAsync();
        RefreshVisualState();
    }

    private async Task ClearHistoryAsync()
    {
        await _repository.ClearAsync();
        _notifyIcon.ShowBalloonTip(2500, AppName, "Clipboard history cleared.", ToolTipIcon.Info);
    }

    private void RefreshVisualState()
    {
        var paused = _captureController.IsPaused;

        _pauseResumeItem.Text = paused ? "Resume capture" : "Pause capture";
        _pauseResumeItem.ToolTipText = paused
            ? "Resume clipboard capture"
            : "Temporarily pause clipboard capture";

        _notifyIcon.Icon = paused ? SystemIcons.Exclamation : SystemIcons.Application;
        _notifyIcon.Text = paused ? "clip-scribe (paused)" : "clip-scribe (recording)";

        if (_launchAtLoginService is not null)
        {
            _launchAtLoginItem.Checked = _launchAtLoginService.IsEnabled();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposed = true;
    }
}
