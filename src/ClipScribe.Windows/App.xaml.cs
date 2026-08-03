using System.Diagnostics;
using System.IO;
using Wpf = System.Windows;
using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;
using ClipScribe.Core.Services;
using ClipScribe.Infrastructure.Sqlite;

namespace ClipScribe.Windows;

public partial class App : Wpf.Application
{
    private const string AppName = "clip-scribe";

    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    private IClipRepository? _repository;
    private ClipboardCaptureEngine? _captureEngine;
    private CaptureRuntimeController? _captureController;
    private TrayIconHost? _trayIconHost;
    private HistoryWindow? _historyWindow;

    protected override async void OnStartup(Wpf.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown;
        Directory.CreateDirectory(_dataDirectory);

        _repository = new SqliteClipRepository(Path.Combine(_dataDirectory, "history.db"));

        var captureService = new ClipboardCaptureService(
            _repository,
            new Win32ClipboardTextReader(),
            new Win32ForegroundWindowInfoProvider(),
            new CaptureOptions());

        _captureEngine = new ClipboardCaptureEngine(
            new Win32ClipboardUpdateSource(),
            captureService,
            onError: ex => _ = Dispatcher.InvokeAsync(() =>
                Wpf.MessageBox.Show($"Clipboard capture error: {ex.Message}", AppName, Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning)));

        _captureController = new CaptureRuntimeController(
            _captureEngine,
            new FileCaptureStateStore(Path.Combine(_dataDirectory, "capture-state.json")));

        try
        {
            await _captureController.InitializeAsync();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show($"Failed to start clipboard capture: {ex.Message}", AppName, Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }

        var launchService = TryCreateLaunchAtLoginService();
        _trayIconHost = new TrayIconHost(
            _captureController,
            _repository,
            launchService,
            openHistoryAsync: OpenHistoryAsync,
            openSettings: OpenSettings,
            quit: QuitApplication);
    }

    private LaunchAtLoginService? TryCreateLaunchAtLoginService()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var quotedPath = $"\"{processPath}\"";
        return new LaunchAtLoginService(AppName, quotedPath, new RegistryLaunchAtLoginStore());
    }

    private Task OpenHistoryAsync()
    {
        if (_repository is null)
        {
            return Task.CompletedTask;
        }

        if (_historyWindow is null)
        {
            _historyWindow = new HistoryWindow(_repository);
            _historyWindow.Closed += (_, _) => _historyWindow = null;
        }

        _historyWindow.Show();
        _historyWindow.Activate();
        _ = _historyWindow.RefreshAsync();
        return Task.CompletedTask;
    }

    private void OpenSettings()
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = _dataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show($"Unable to open settings folder: {ex.Message}", AppName, Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
        }
    }

    private void QuitApplication()
    {
        Shutdown();
    }

    protected override void OnExit(Wpf.ExitEventArgs e)
    {
        _historyWindow?.Close();
        _trayIconHost?.Dispose();
        _captureController?.Dispose();
        _captureEngine?.Dispose();
        base.OnExit(e);
    }
}
