using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using CryBar.Updates;
using CryBarEditor.Classes;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CryBarEditor.Windows;

public partial class CheckForUpdatesWindow : SimpleWindow
{
    static readonly IBrush AccentBlueBrush = new SolidColorBrush(Color.Parse("#5da2e8"));
    static readonly IBrush UpToDateGreenBrush = new SolidColorBrush(Color.Parse("#78f542"));
    static readonly IBrush ErrorRedBrush = new SolidColorBrush(Color.Parse("#f55142"));

    readonly HttpClient _http;
    readonly Version _current;
    UpdateInfo? _info;
    CancellationTokenSource? _cts;

    enum UiState { Checking, UpToDate, UpdateAvailable, CheckFailed, Downloading, Extracting, LaunchingUpdater, Error }

    public CheckForUpdatesWindow() : this(http: null!, prefetched: null, current: new Version(0, 0, 0)) { }

    public CheckForUpdatesWindow(HttpClient http, UpdateInfo? prefetched, Version current)
    {
        _http = http;
        _info = prefetched;
        _current = current;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_info == null)
        {
            SetState(UiState.Checking);
            try
            {
                _info = await UpdateService.TryGetLatestVersionAsync(_http, default);
            }
            catch { _info = null; }

            if (_info == null) { SetState(UiState.CheckFailed); return; }
        }

        SetState(UpdateService.IsVersionNewer(_info.LatestVersion, _current)
            ? UiState.UpdateAvailable
            : UiState.UpToDate);
    }

    void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    void SetState(UiState s, string? statusOverride = null)
    {
        ShowChecking = false;
        ShowUpToDate = false;
        ShowReleaseLink = false;
        ShowUpdateButton = false;
        UpdateButtonEnabled = false;
        CloseButtonEnabled = true;
        ShowProgressRegion = false;
        ProgressIndeterminate = true;
        ProgressValue = 0;
        StatusText = statusOverride ?? "";
        StatusBrush = Brushes.LightGray;
        LatestVersionBrush = Brushes.Gray;

        switch (s)
        {
            case UiState.Checking:
                ShowChecking = true;
                LatestVersionText = "Checking...";
                break;
            case UiState.UpToDate:
                LatestVersionText = $"{_current.Major}.{_current.Minor}.{_current.Build}";
                LatestVersionBrush = UpToDateGreenBrush;
                ShowUpToDate = true;
                break;
            case UiState.UpdateAvailable:
                LatestVersionText = _info?.LatestVersion ?? "?";
                LatestVersionBrush = AccentBlueBrush;
                ShowReleaseLink = _info != null;
                ShowUpdateButton = true;
                UpdateButtonEnabled = true;
                break;
            case UiState.CheckFailed:
                LatestVersionText = "Could not check for updates.";
                LatestVersionBrush = ErrorRedBrush;
                break;
            case UiState.Downloading:
                LatestVersionText = _info?.LatestVersion ?? "?";
                LatestVersionBrush = AccentBlueBrush;
                ShowReleaseLink = _info != null;
                ShowProgressRegion = true;
                ProgressIndeterminate = false;
                CloseButtonEnabled = false;
                break;
            case UiState.Extracting:
            case UiState.LaunchingUpdater:
                LatestVersionText = _info?.LatestVersion ?? "?";
                LatestVersionBrush = AccentBlueBrush;
                ShowReleaseLink = _info != null;
                ShowProgressRegion = true;
                ProgressIndeterminate = true;
                CloseButtonEnabled = false;
                break;
            case UiState.Error:
                LatestVersionText = _info?.LatestVersion ?? "?";
                LatestVersionBrush = AccentBlueBrush;
                ShowReleaseLink = _info != null;
                ShowUpdateButton = true;
                UpdateButtonEnabled = true;     // re-enable so user can retry
                ShowProgressRegion = true;
                ProgressIndeterminate = false;
                ProgressValue = 0;
                StatusBrush = ErrorRedBrush;
                break;
        }
        RaiseAll();
    }

    void RaiseAll()
    {
        OnPropertyChanged(nameof(LatestVersionText));
        OnPropertyChanged(nameof(LatestVersionBrush));
        OnPropertyChanged(nameof(ShowChecking));
        OnPropertyChanged(nameof(ShowUpToDate));
        OnPropertyChanged(nameof(ShowReleaseLink));
        OnPropertyChanged(nameof(ShowUpdateButton));
        OnPropertyChanged(nameof(UpdateButtonEnabled));
        OnPropertyChanged(nameof(CloseButtonEnabled));
        OnPropertyChanged(nameof(ShowProgressRegion));
        OnPropertyChanged(nameof(ProgressIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    // ----- Bound properties -----
    public string CurrentVersionText => $"{_current.Major}.{_current.Minor}.{_current.Build}";
    public string LatestVersionText { get; private set; } = "";
    public IBrush LatestVersionBrush { get; private set; } = Brushes.Gray;
    public bool ShowChecking { get; private set; }
    public bool ShowUpToDate { get; private set; }
    public bool ShowReleaseLink { get; private set; }
    public bool ShowUpdateButton { get; private set; }
    public bool UpdateButtonEnabled { get; private set; }
    public bool CloseButtonEnabled { get; private set; } = true;
    public bool ShowProgressRegion { get; private set; }
    public double ProgressValue { get; private set; }
    public bool ProgressIndeterminate { get; private set; } = true;
    public string StatusText { get; private set; } = "";
    public IBrush StatusBrush { get; private set; } = Brushes.LightGray;

    // ----- Handlers -----
    void OpenReleasePage_Click(object? sender, RoutedEventArgs e)
    {
        if (_info == null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _info.ReleasePageUrl, UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }

    async void UpdateNow_Click(object? sender, RoutedEventArgs e)
    {
        if (_info == null) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        SetState(UiState.Downloading, "Preparing...");

        try
        {
            var installDir = AppContext.BaseDirectory;
            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                          ?? System.IO.Path.Combine(installDir, "CryBarEditor.exe");

            var progress = new Progress<UpdateProgress>(p =>
                Dispatcher.UIThread.Post(() => ApplyProgress(p)));

            await UpdateService.DownloadAndExtractAsync(_http, _info, installDir, progress, _cts.Token);

            SetState(UiState.LaunchingUpdater, "Starting updater...");
            UpdateService.LaunchUpdater(installDir, exePath, Environment.ProcessId);

            SetState(UiState.LaunchingUpdater, "Closing editor and finalizing update...");
            await Task.Delay(250);

            (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (UpdateException ex)
        {
            ShowError(ex.Stage, ex.Message);
        }
        catch (OperationCanceledException)
        {
            ShowError(UpdateStage.Downloading, "Update cancelled.");
        }
        catch (Exception ex)
        {
            ShowError(UpdateStage.Downloading, ex.Message);
        }
    }

    void ApplyProgress(UpdateProgress p)
    {
        switch (p.Stage)
        {
            case UpdateStage.LocatingAsset:
            case UpdateStage.Downloading:
                if (p.Percent.HasValue)
                {
                    ProgressIndeterminate = false;
                    ProgressValue = p.Percent.Value;
                }
                else
                {
                    ProgressIndeterminate = true;
                }
                StatusText = p.Status;
                OnPropertyChanged(nameof(ProgressIndeterminate));
                OnPropertyChanged(nameof(ProgressValue));
                OnPropertyChanged(nameof(StatusText));
                break;
            case UpdateStage.Extracting:
                SetState(UiState.Extracting, p.Status);
                break;
            case UpdateStage.LaunchingUpdater:
                SetState(UiState.LaunchingUpdater, p.Status);
                break;
        }
    }

    void ShowError(UpdateStage stage, string message)
    {
        SetState(UiState.Error, $"{StageLabel(stage)}: {message}");
    }

    static string StageLabel(UpdateStage s) => s switch
    {
        UpdateStage.LocatingAsset => "Could not locate update",
        UpdateStage.Downloading => "Download failed",
        UpdateStage.Extracting => "Extraction failed",
        UpdateStage.LaunchingUpdater => "Could not start updater",
        _ => "Update failed",
    };

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
