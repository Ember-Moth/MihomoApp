using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mihomo.Models;
using Mihomo.Services.Clash;
using Mihomo.Services.Storage;

namespace Mihomo.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IClashRuntime _runtime = ClashRuntimeHost.Current;
    private readonly HttpClient _httpClient = new();
    private readonly DispatcherTimer _telemetryTimer;
    private readonly DispatcherTimer _stateSaveTimer;
    private readonly AppStateStore _stateStore;
    private readonly Task _stateReadyTask;
    private bool _isRefreshingTelemetry;
    private bool _isRefreshingNetworkDetection;
    private bool _isApplyingStoredState;
    private bool _isPersistingState;
    private bool _isStateLoaded;
    private DateTimeOffset? _startedAt;
    private readonly Queue<double> _uploadSpeedSampleBuffer = new();
    private readonly Queue<double> _downloadSpeedSampleBuffer = new();

    public MainViewModel()
    {
        HomeDirectory = _runtime.DefaultHomeDirectory;
        ConfigPath = _runtime.DefaultConfigPath;
        _stateStore = new AppStateStore(Path.Combine(HomeDirectory, "mihomo-state.db"));

        _runtime.StatusChanged += (_, status) => ApplyStatus(status);

        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _telemetryTimer.Tick += async (_, _) => await RefreshTelemetryAsync();

        _stateSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _stateSaveTimer.Tick += async (_, _) =>
        {
            _stateSaveTimer.Stop();
            await PersistAppStateAsync();
        };

        ApplyStatus(_runtime.Status);
        LoadConfigContent();
        ApplyThemeMode();
        ResetSpeedSamples();
        _stateReadyTask = InitializePersistentStateAsync();
        _ = RefreshNetworkDetectionAsync();
    }

    public IReadOnlyList<string> StackOptions { get; } = ["system", "gvisor", "mixed"];

    public ObservableCollection<ProxyGroupItem> ProxyGroups { get; } = [];

    public ObservableCollection<ProxyNodeItem> VisibleProxyNodes { get; } = [];

    public ObservableCollection<ConfigProfileItem> ConfigProfiles { get; } = [];

    public IReadOnlyList<string> ProxySortOptions { get; } = ["配置顺序", "按延迟", "按名称"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportSubscriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProxiesCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestSelectedGroupDelayCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestProxyDelayCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProxiesCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestSelectedGroupDelayCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestProxyDelayCommand))]
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
    private ConfigProfileItem? _selectedConfigProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestSelectedGroupDelayCommand))]
    private ProxyGroupItem? _selectedGroup;

    [ObservableProperty]
    private string _proxySearchText = string.Empty;

    [ObservableProperty]
    private string _proxySortMode = "配置顺序";

    [ObservableProperty]
    private bool _isTestingGroupDelay;

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _totalTraffic = "0 B";

    [ObservableProperty]
    private string _totalUpload = "0 B";

    [ObservableProperty]
    private string _totalDownload = "0 B";

    [ObservableProperty]
    private string _connectionCount = "0";

    [ObservableProperty]
    private string _runningDuration = "00:00:00";

    [ObservableProperty]
    private IReadOnlyList<double> _uploadSpeedSamples = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _downloadSpeedSamples = Array.Empty<double>();

    [ObservableProperty]
    private bool _isNetworkDetectionLoading = true;

    [ObservableProperty]
    private string _publicIp = "检测中";

    [ObservableProperty]
    private string _publicIpCountryCode = string.Empty;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    public bool IsOverviewPage => CurrentPage == "overview";

    public bool IsProfilesPage => CurrentPage == "profiles";

    public bool IsProxiesPage => CurrentPage == "proxies";

    public bool IsConfigPage => CurrentPage == "config";

    public bool IsToolsPage => CurrentPage == "tools";

    public string PublicIpCountryMark => CountryCodeToEmoji(PublicIpCountryCode);

    public string NetworkDetectionText => IsNetworkDetectionLoading ? "检测中" : PublicIp;

    public string VpnIntranetAddress => EnableIpv6
        ? "172.19.0.1 / fdfe:dcba:9876::1"
        : "172.19.0.1";

    public string ThemeModeText => IsDarkTheme ? "深色" : "浅色";

    public string PageTitle => CurrentPage switch
    {
        "profiles" => "订阅",
        "proxies" => "代理",
        "config" => "基本配置",
        "tools" => "工具",
        _ => "仪表盘"
    };

    public string PageSubtitle => CurrentPage switch
    {
        "profiles" => "远程配置与本地文件",
        "proxies" => ProxySummary,
        "config" => "运行参数和 config.yaml",
        "tools" => "工具和订阅",
        _ => RunningStateText
    };

    public string RunningActionText => IsRunning ? "停止" : "启动";

    public IAsyncRelayCommand RunningActionCommand => IsRunning ? StopCommand : StartCommand;

    public string RunningStateText => IsRunning ? $"已运行 {RunningDuration}" : "未启动";

    public string ProxySummary => ProxyGroups.Count == 0 ? "无策略组" : $"{ProxyGroups.Count} 个策略组";

    public string SelectedGroupTitle => SelectedGroup?.Name ?? "未选择策略组";

    public string SelectedGroupSummary => SelectedGroup?.Summary ?? "启动后刷新策略组";

    public string SelectedGroupNow => string.IsNullOrWhiteSpace(SelectedGroup?.Now) ? "-" : SelectedGroup.Now;

    public string VisibleProxySummary => SelectedGroup == null
        ? "无节点"
        : $"{VisibleProxyNodes.Count}/{SelectedGroup.Nodes.Count} 个节点";

    public bool HasProxyGroups => ProxyGroups.Count > 0;

    public bool HasSelectedGroup => SelectedGroup != null;

    public bool HasVisibleProxyNodes => VisibleProxyNodes.Count > 0;

    public bool HasConfigProfiles => ConfigProfiles.Count > 0;

    public bool MissingConfigProfiles => ConfigProfiles.Count == 0;

    public bool MissingSelectedGroup => SelectedGroup == null;

    public bool MissingVisibleProxyNodes => SelectedGroup != null && VisibleProxyNodes.Count == 0;

    public string ProxyDelayTestUrl => SelectedGroup?.TestUrl is { Length: > 0 } value
        ? value
        : "https://www.gstatic.com/generate_204";

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverviewPage));
        OnPropertyChanged(nameof(IsProfilesPage));
        OnPropertyChanged(nameof(IsProxiesPage));
        OnPropertyChanged(nameof(IsConfigPage));
        OnPropertyChanged(nameof(IsToolsPage));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(RunningActionText));
        OnPropertyChanged(nameof(RunningActionCommand));
        OnPropertyChanged(nameof(RunningStateText));
        OnPropertyChanged(nameof(PageSubtitle));
        if (value)
        {
            _startedAt ??= DateTimeOffset.Now;
            UpdateRunningDuration();
            _telemetryTimer.Start();
            _ = RefreshNetworkDetectionAsync();
        }
        else
        {
            _telemetryTimer.Stop();
            _startedAt = null;
            RunningDuration = "00:00:00";
        }
    }

    partial void OnEnableIpv6Changed(bool value)
    {
        OnPropertyChanged(nameof(VpnIntranetAddress));
        QueueStateSave();
    }

    partial void OnIsNetworkDetectionLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(NetworkDetectionText));
    }

    partial void OnPublicIpChanged(string value)
    {
        OnPropertyChanged(nameof(NetworkDetectionText));
    }

    partial void OnPublicIpCountryCodeChanged(string value)
    {
        OnPropertyChanged(nameof(PublicIpCountryMark));
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ApplyThemeMode();
        OnPropertyChanged(nameof(ThemeModeText));
        QueueStateSave();
    }

    partial void OnRunningDurationChanged(string value)
    {
        OnPropertyChanged(nameof(RunningStateText));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    partial void OnSelectedGroupChanged(ProxyGroupItem? value)
    {
        UpdateVisibleProxyNodes();
        OnPropertyChanged(nameof(SelectedGroupTitle));
        OnPropertyChanged(nameof(SelectedGroupSummary));
        OnPropertyChanged(nameof(SelectedGroupNow));
        OnPropertyChanged(nameof(ProxyDelayTestUrl));
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(MissingSelectedGroup));
    }

    partial void OnProxySearchTextChanged(string value)
    {
        UpdateVisibleProxyNodes();
    }

    partial void OnProxySortModeChanged(string value)
    {
        UpdateVisibleProxyNodes();
    }

    partial void OnSelectedConfigProfileChanged(ConfigProfileItem? value)
    {
        ApplySelectedConfigProfile(value);
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

    [RelayCommand]
    private void ShowTools()
    {
        CurrentPage = "tools";
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
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
            await _stateReadyTask;
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
        if (status.State == ClashRunState.Running)
        {
            _startedAt = status.StartedAtUnixMilliseconds > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(status.StartedAtUnixMilliseconds).ToLocalTime()
                : DateTimeOffset.Now;
        }

        IsRunning = status.State == ClashRunState.Running;
        LastMessage = status.Message;
    }

    private void ApplyThemeMode()
    {
        Application.Current!.RequestedThemeVariant = IsDarkTheme
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }

    private static string CountryCodeToEmoji(string countryCode)
    {
        var code = countryCode.Trim().ToUpperInvariant();
        if (code.Length != 2 || code.Any(c => c is < 'A' or > 'Z'))
        {
            return string.IsNullOrWhiteSpace(countryCode) ? "⌁" : countryCode;
        }

        Span<char> result = stackalloc char[4];
        var first = char.ConvertFromUtf32(0x1F1E6 + code[0] - 'A');
        var second = char.ConvertFromUtf32(0x1F1E6 + code[1] - 'A');
        result[0] = first[0];
        result[1] = first[1];
        result[2] = second[0];
        result[3] = second[1];
        return new string(result);
    }
}
