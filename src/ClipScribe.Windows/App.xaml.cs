using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;
using ClipScribe.Core.Services;
using ClipScribe.Infrastructure.Sqlite;
using Wpf = System.Windows;

namespace ClipScribe.Windows;

public partial class App : Wpf.Application
{
    private const string AppName = "clip-scribe";

    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    private readonly string _settingsPath;
    private readonly string _onboardingStatePath;

    private IClipRepository? _repository;
    private ClipboardCaptureEngine? _captureEngine;
    private CaptureRuntimeController? _captureController;
    private TrayIconHost? _trayIconHost;
    private HistoryWindow? _historyWindow;

    private JsonAppSettingsStore? _settingsStore;
    private FileSystemWatcher? _settingsWatcher;
    private CancellationTokenSource? _settingsReloadCts;

    private Win32GlobalHotkeyHost? _hotkeyHost;
    private Win32ForegroundWindowHandleProvider? _foregroundWindowHandleProvider;
    private Win32PasteBackService? _pasteBackService;
    private LocalAiTextTransformClient? _localAiTransformClient;

    private GlobalHotkeySettings _activeHotkey = GlobalHotkeySettings.Default;
    private LocalAiSettings _activeLocalAiSettings = LocalAiSettings.Default;
    private PrivacySettings _activePrivacySettings = PrivacySettings.Default;
    private bool _hotkeyRegistered;

    public App()
    {
        _settingsPath = Path.Combine(_dataDirectory, "config.json");
        _onboardingStatePath = Path.Combine(_dataDirectory, "onboarding.dismissed");
    }

    protected override async void OnStartup(Wpf.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown;
        Directory.CreateDirectory(_dataDirectory);

        _repository = new SqliteClipRepository(Path.Combine(_dataDirectory, "history.db"));

        if (OperatingSystem.IsWindows())
        {
            _settingsStore = new JsonAppSettingsStore(_settingsPath);
            _settingsStore.EnsureExists();
            _activeLocalAiSettings = _settingsStore.LoadLocalAiSettings();
            _activePrivacySettings = _settingsStore.LoadPrivacySettings();
        }

        var captureService = new ClipboardCaptureService(
            _repository,
            new Win32ClipboardTextReader(),
            new Win32ForegroundWindowInfoProvider(),
            new CaptureOptions(),
            () => _activePrivacySettings);

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

        if (OperatingSystem.IsWindows())
        {
            _foregroundWindowHandleProvider = new Win32ForegroundWindowHandleProvider();
            _pasteBackService = new Win32PasteBackService();
            _localAiTransformClient = new LocalAiTextTransformClient();

            _hotkeyHost = new Win32GlobalHotkeyHost(HandleHistoryHotkeyPressed);
            TryApplyHotkey(_settingsStore!.LoadHotkey(), showErrorDialog: true);
            StartSettingsWatcher();
        }

        var launchService = TryCreateLaunchAtLoginService();
        _trayIconHost = new TrayIconHost(
            _captureController,
            _repository,
            launchService,
            openHistoryAsync: OpenHistoryFromTrayAsync,
            openSettings: OpenSettings,
            quit: QuitApplication);

        MaybeShowFirstRunOnboarding();
    }

    private void StartSettingsWatcher()
    {
        if (_settingsWatcher is not null)
        {
            return;
        }

        _settingsWatcher = new FileSystemWatcher(_dataDirectory, Path.GetFileName(_settingsPath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _settingsWatcher.Changed += OnSettingsFileChanged;
        _settingsWatcher.Created += OnSettingsFileChanged;
        _settingsWatcher.Renamed += OnSettingsFileRenamed;
    }

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs e)
        => QueueSettingsReload();

    private void OnSettingsFileRenamed(object sender, RenamedEventArgs e)
        => QueueSettingsReload();

    private void QueueSettingsReload()
    {
        _settingsReloadCts?.Cancel();
        _settingsReloadCts?.Dispose();

        var cts = new CancellationTokenSource();
        _settingsReloadCts = cts;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(220, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            ReloadSettingsFromFile();
        });
    }

    private void ReloadSettingsFromFile()
    {
        if (_settingsStore is null)
        {
            return;
        }

        TryApplyHotkey(_settingsStore.LoadHotkey(), showErrorDialog: false);

        _activeLocalAiSettings = _settingsStore.LoadLocalAiSettings();
        _activePrivacySettings = _settingsStore.LoadPrivacySettings();
        _historyWindow?.SetLocalAiSettings(_activeLocalAiSettings);
    }

    private void MaybeShowFirstRunOnboarding()
    {
        if (!OperatingSystem.IsWindows() || File.Exists(_onboardingStatePath))
        {
            return;
        }

        try
        {
            Wpf.MessageBox.Show(
                "Welcome to clip-scribe!\n\n" +
                "• Open history with Ctrl+Shift+V\n" +
                "• Right-click clips for pinning, snippets, plain-text paste, and local-AI transforms\n" +
                "• Privacy defaults are enabled: clipboard ignore flags are respected and sensitive-looking content is skipped\n\n" +
                "You can edit %LOCALAPPDATA%\\clip-scribe\\config.json to change hotkey/local-AI/privacy settings.",
                AppName,
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);

            File.WriteAllText(_onboardingStatePath, "dismissed");
        }
        catch
        {
            // Best effort only; onboarding should never block startup.
        }
    }

    private void TryApplyHotkey(GlobalHotkeySettings settings, bool showErrorDialog)
    {
        if (_hotkeyHost is null)
        {
            return;
        }

        var normalized = GlobalHotkeySettings.Normalize(settings);
        if (_hotkeyRegistered && normalized == _activeHotkey)
        {
            return;
        }

        try
        {
            _hotkeyHost.Register(normalized);
            _activeHotkey = normalized;
            _hotkeyRegistered = true;
        }
        catch (Exception ex) when (ex is Win32Exception or PlatformNotSupportedException)
        {
            _hotkeyRegistered = false;

            if (showErrorDialog)
            {
                Wpf.MessageBox.Show(
                    $"Unable to register history hotkey from settings ({DescribeHotkey(normalized)}): {ex.Message}",
                    AppName,
                    Wpf.MessageBoxButton.OK,
                    Wpf.MessageBoxImage.Warning);
            }
        }
    }

    private static string DescribeHotkey(GlobalHotkeySettings hotkey)
    {
        var parts = new List<string>();

        if (hotkey.Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (hotkey.Shift)
        {
            parts.Add("Shift");
        }

        if (hotkey.Alt)
        {
            parts.Add("Alt");
        }

        if (hotkey.Win)
        {
            parts.Add("Win");
        }

        parts.Add(hotkey.Key);
        return string.Join("+", parts);
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

    private void HandleHistoryHotkeyPressed()
    {
        _ = Dispatcher.InvokeAsync(async () => await OpenHistoryAsync(capturePasteTarget: true));
    }

    private Task OpenHistoryFromTrayAsync()
        => OpenHistoryAsync(capturePasteTarget: false);

    private Task OpenHistoryAsync(bool capturePasteTarget)
    {
        if (_repository is null)
        {
            return Task.CompletedTask;
        }

        if (_historyWindow is null)
        {
            _historyWindow = new HistoryWindow(_repository, _pasteBackService, _localAiTransformClient, _activeLocalAiSettings);
            _historyWindow.Closed += (_, _) => _historyWindow = null;
        }

        _historyWindow.SetLocalAiSettings(_activeLocalAiSettings);

        if (capturePasteTarget)
        {
            var targetWindow = _foregroundWindowHandleProvider?.TryGetForegroundWindowHandle() ?? IntPtr.Zero;
            _historyWindow.SetPasteTargetWindow(targetWindow);
        }
        else
        {
            _historyWindow.ClearPasteTargetWindow();
        }

        if (!_historyWindow.IsVisible)
        {
            _historyWindow.Show();
        }

        _historyWindow.Activate();
        _historyWindow.FocusSearch();

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

        _settingsWatcher?.Dispose();
        _settingsReloadCts?.Cancel();
        _settingsReloadCts?.Dispose();

        _hotkeyHost?.Dispose();
        _trayIconHost?.Dispose();
        _captureController?.Dispose();
        _captureEngine?.Dispose();
        _localAiTransformClient?.Dispose();

        base.OnExit(e);
    }
}
