using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mihomo.Models;
using Mihomo.Services.Clash;

namespace Mihomo.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IClashRuntime _runtime = ClashRuntimeHost.Current;
    private readonly HttpClient _httpClient = new();
    private readonly ClashApiClient _api = new("http://127.0.0.1:9090");
    private readonly DispatcherTimer _telemetryTimer;
    private bool _isRefreshingTelemetry;

    public MainViewModel()
    {
        HomeDirectory = _runtime.DefaultHomeDirectory;
        ConfigPath = _runtime.DefaultConfigPath;
        ApiBaseAddress = string.IsNullOrWhiteSpace(_runtime.ApiBaseAddress)
            ? "http://127.0.0.1:9090"
            : _runtime.ApiBaseAddress;

        _runtime.StatusChanged += (_, status) => ApplyStatus(status);

        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _telemetryTimer.Tick += async (_, _) => await RefreshTelemetryAsync();

        ApplyStatus(_runtime.Status);
        LoadConfigContent();
    }

    public IReadOnlyList<string> StackOptions { get; } = ["system", "gvisor", "mixed"];

    public ObservableCollection<ProxyGroupItem> ProxyGroups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportSubscriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProxiesCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProxiesCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _currentPage = "overview";

    [ObservableProperty]
    private string _stateText = "Stopped";

    [ObservableProperty]
    private string _lastMessage = "Ready";

    [ObservableProperty]
    private string _homeDirectory = string.Empty;

    [ObservableProperty]
    private string _configPath = string.Empty;

    [ObservableProperty]
    private string _apiBaseAddress = string.Empty;

    [ObservableProperty]
    private string _subscriptionUrl = string.Empty;

    [ObservableProperty]
    private string _mixedPort = "7890";

    [ObservableProperty]
    private bool _enableTun = true;

    [ObservableProperty]
    private bool _enableIpv6;

    [ObservableProperty]
    private bool _dnsHijacking = true;

    [ObservableProperty]
    private bool _systemProxy;

    [ObservableProperty]
    private string _stack = "system";

    [ObservableProperty]
    private string _routeAddressCsv = "0.0.0.0/0";

    [ObservableProperty]
    private string _configContent = string.Empty;

    [ObservableProperty]
    private ProxyGroupItem? _selectedGroup;

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _totalTraffic = "0 B";

    [ObservableProperty]
    private string _connectionCount = "0";

    public bool IsOverviewPage => CurrentPage == "overview";

    public bool IsProfilesPage => CurrentPage == "profiles";

    public bool IsProxiesPage => CurrentPage == "proxies";

    public bool IsConfigPage => CurrentPage == "config";

    public string RunningActionText => IsRunning ? "停止" : "启动";

    public IAsyncRelayCommand RunningActionCommand => IsRunning ? StopCommand : StartCommand;

    public string ProxySummary => ProxyGroups.Count == 0 ? "No groups" : $"{ProxyGroups.Count} groups";

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverviewPage));
        OnPropertyChanged(nameof(IsProfilesPage));
        OnPropertyChanged(nameof(IsProxiesPage));
        OnPropertyChanged(nameof(IsConfigPage));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(RunningActionText));
        OnPropertyChanged(nameof(RunningActionCommand));
        if (value)
        {
            _telemetryTimer.Start();
        }
        else
        {
            _telemetryTimer.Stop();
        }
    }

    partial void OnApiBaseAddressChanged(string value)
    {
        _api.SetBaseAddress(value);
    }

    [RelayCommand]
    private void ShowOverview()
    {
        CurrentPage = "overview";
    }

    [RelayCommand]
    private void ShowProfiles()
    {
        CurrentPage = "profiles";
    }

    [RelayCommand]
    private void ShowProxies()
    {
        CurrentPage = "proxies";
    }

    [RelayCommand]
    private void ShowConfig()
    {
        CurrentPage = "config";
    }

    private bool CanStart()
    {
        return !IsBusy && !IsRunning;
    }

    private bool CanStop()
    {
        return !IsBusy && IsRunning;
    }

    private bool CanValidate()
    {
        return !IsBusy;
    }

    private bool CanImportSubscription()
    {
        return !IsBusy;
    }

    private bool CanRefreshProxies()
    {
        return !IsBusy && IsRunning;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            LastMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyStatus(ClashStatus status)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyStatus(status));
            return;
        }

        StateText = status.State.ToString();
        IsRunning = status.State == ClashRunState.Running;
        LastMessage = status.Message;
    }
}
