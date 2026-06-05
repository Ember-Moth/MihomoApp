using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Aureline.Models;
using Aureline.Services.Clash;
using Aureline.Services.Storage;

namespace Aureline.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IClashRuntime _runtime = ClashRuntimeHost.Current;
    private readonly HttpClient _httpClient = new();
    private readonly DispatcherTimer _telemetryTimer;
    private readonly DispatcherTimer _stateSaveTimer;
    private readonly DispatcherTimer _runtimeRestartTimer;
    private readonly AppStateStore _stateStore;
    private readonly Task _stateReadyTask;
    private bool _isRefreshingTelemetry;
    private bool _isRefreshingNetworkDetection;
    private bool _isApplyingStoredState;
    private bool _isPersistingState;
    private bool _isStateLoaded;
    private DateTimeOffset? _startedAt;
    private string _pendingRuntimeRestartReason = string.Empty;
    private bool _pendingRuntimeRestartNeedsConfigSave;
    private readonly Queue<double> _uploadSpeedSampleBuffer = new();
    private readonly Queue<double> _downloadSpeedSampleBuffer = new();

    public MainViewModel()
    {
        HomeDirectory = _runtime.DefaultHomeDirectory;
        ConfigPath = _runtime.DefaultConfigPath;
        _stateStore = new AppStateStore(Path.Combine(HomeDirectory, "aureline-state.db"));

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

        _runtimeRestartTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _runtimeRestartTimer.Tick += async (_, _) =>
        {
            _runtimeRestartTimer.Stop();
            await RestartRuntimeForPendingChangeAsync();
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

    public ObservableCollection<InstalledApplicationItem> InstalledApplications { get; } = [];

    public ObservableCollection<InstalledApplicationItem> VisibleInstalledApplications { get; } = [];

    public IReadOnlyList<string> ProxySortOptions { get; } = ["配置顺序", "按延迟", "按名称"];

    public IReadOnlyList<string> OutboundModeOptions { get; } = ["rule", "global", "direct"];

    public IReadOnlyList<string> LogLevelOptions { get; } = ["debug", "info", "warning", "error", "silent"];

    public IReadOnlyList<string> LocaleOptions { get; } = ["", "zh-Hans", "en"];

    public IReadOnlyList<string> AccessControlModeOptions { get; } = ["rejectSelected", "acceptSelected"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportSubscriptionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProxiesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedGroupCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestSelectedGroupDelayCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestProxyDelayCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProxiesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedGroupCommand))]
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
    private bool _allowLan;

    [ObservableProperty]
    private bool _dnsHijacking = true;

    [ObservableProperty]
    private bool _systemProxy;

    [ObservableProperty]
    private string _stack = "system";

    [ObservableProperty]
    private string _routeAddressCsv = "0.0.0.0/0";

    [ObservableProperty]
    private string _outboundMode = "rule";

    [ObservableProperty]
    private string _logLevel = "info";

    [ObservableProperty]
    private string _globalUa = string.Empty;

    [ObservableProperty]
    private string _testUrl = "https://www.gstatic.com/generate_204";

    [ObservableProperty]
    private bool _unifiedDelay = true;

    [ObservableProperty]
    private bool _tcpConcurrent;

    [ObservableProperty]
    private bool _findProcess = true;

    [ObservableProperty]
    private bool _geodataMemory = true;

    [ObservableProperty]
    private bool _externalController = true;

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
    private bool _isProxySearchVisible;

    [ObservableProperty]
    private bool _isProxyGroupListVisible;

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

    [ObservableProperty]
    private string _locale = string.Empty;

    [ObservableProperty]
    private bool _minimizeOnExit;

    [ObservableProperty]
    private bool _autoRun;

    [ObservableProperty]
    private bool _hidden;

    [ObservableProperty]
    private bool _animateTabs = true;

    [ObservableProperty]
    private bool _openLogs = true;

    [ObservableProperty]
    private bool _closeConnections = true;

    [ObservableProperty]
    private bool _onlyStatisticsProxy;

    [ObservableProperty]
    private bool _crashlytics;

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    [ObservableProperty]
    private bool _accessControlEnabled;

    [ObservableProperty]
    private string _accessControlMode = "rejectSelected";

    [ObservableProperty]
    private string _accessPackageCsv = string.Empty;

    [ObservableProperty]
    private bool _accessFilterSystemApps = true;

    [ObservableProperty]
    private bool _accessFilterNoInternetApps = true;

    [ObservableProperty]
    private string _accessSearchText = string.Empty;

    [ObservableProperty]
    private bool _isLoadingApplications;

    [ObservableProperty]
    private string _currentToolPage = string.Empty;

    [ObservableProperty]
    private bool _isAddProfilePanelVisible;

    [ObservableProperty]
    private bool _isAddProfileUrlInputVisible;

    public bool IsOverviewPage => CurrentPage == "overview";

    public bool IsProfilesPage => CurrentPage == "profiles";

    public bool IsProxiesPage => CurrentPage == "proxies";

    public bool IsConfigPage => CurrentPage == "config";

    public bool IsToolsPage => CurrentPage == "tools";

    public bool IsConfigPageWithProfiles => IsConfigPage && HasConfigProfiles;

    public bool IsProxyNavigationVisible => IsRunning;

    public bool IsCompactNavigationVisible => !IsProxyNavigationVisible;

    public bool CanNavigateBack =>
        IsAddProfilePanelVisible ||
        IsProxySearchVisible ||
        IsProxyGroupListVisible ||
        IsToolsPage && !string.IsNullOrWhiteSpace(CurrentToolPage) ||
        !IsOverviewPage;

    public bool IsAppBarBackVisible =>
        IsAddProfilePanelVisible ||
        IsProxySearchVisible ||
        IsProxyGroupListVisible ||
        IsToolsPage && !string.IsNullOrWhiteSpace(CurrentToolPage);

    public bool IsAddProfileMenuVisible => IsAddProfilePanelVisible && !IsAddProfileUrlInputVisible;

    public string PublicIpCountryMark => CountryCodeToEmoji(PublicIpCountryCode);

    public string NetworkDetectionText => IsNetworkDetectionLoading ? "检测中" : PublicIp;

    public string VpnIntranetAddress => EnableIpv6
        ? "172.19.0.1 / fdfe:dcba:9876::1"
        : "172.19.0.1";

    public string ThemeModeText => IsDarkTheme ? "深色" : "浅色";

    public bool IsLightTheme => !IsDarkTheme;

    public bool IsDefaultLocale => string.IsNullOrWhiteSpace(Locale);

    public bool IsChineseLocale => Locale == "zh-Hans";

    public bool IsEnglishLocale => Locale == "en";

    public string GlobalUaText => string.IsNullOrWhiteSpace(GlobalUa) ? "默认" : GlobalUa;

    public string MixedPortText => string.IsNullOrWhiteSpace(MixedPort) ? "7890" : MixedPort;

    public string StackText => string.IsNullOrWhiteSpace(Stack) ? "system" : Stack;

    public string RouteAddressText => string.IsNullOrWhiteSpace(RouteAddressCsv) ? "默认" : RouteAddressCsv;

    public string LocaleText => string.IsNullOrWhiteSpace(Locale)
        ? "默认"
        : Locale switch
        {
            "zh-Hans" => "简体中文",
            "en" => "English",
            _ => Locale
        };

    public bool IsToolsRootPage => IsToolsPage && string.IsNullOrWhiteSpace(CurrentToolPage);

    public bool IsLanguageToolPage => IsToolsPage && CurrentToolPage == "language";

    public bool IsThemeToolPage => IsToolsPage && CurrentToolPage == "theme";

    public bool IsBackupToolPage => IsToolsPage && CurrentToolPage == "backup";

    public bool IsAccessToolPage => IsToolsPage && CurrentToolPage == "access";

    public bool IsBasicConfigToolPage => IsToolsPage && CurrentToolPage == "basic";

    public bool IsAdvancedConfigToolPage => IsToolsPage && CurrentToolPage == "advanced";

    public bool IsNetworkConfigToolPage => IsToolsPage && CurrentToolPage == "network";

    public bool IsDnsConfigToolPage => IsToolsPage && CurrentToolPage == "dns";

    public bool IsRulesConfigToolPage => IsToolsPage && CurrentToolPage == "rules";

    public bool IsScriptsConfigToolPage => IsToolsPage && CurrentToolPage == "scripts";

    public bool IsApplicationSettingsToolPage => IsToolsPage && CurrentToolPage == "application";

    public bool IsDisclaimerToolPage => IsToolsPage && CurrentToolPage == "disclaimer";

    public bool IsAboutToolPage => IsToolsPage && CurrentToolPage == "about";

    public string ToolPageTitle => CurrentToolPage switch
    {
        "language" => "语言",
        "theme" => "主题",
        "backup" => "备份与恢复",
        "access" => "访问控制",
        "basic" => "基本配置",
        "advanced" => "进阶配置",
        "network" => "网络",
        "dns" => "DNS",
        "rules" => "规则",
        "scripts" => "脚本",
        "application" => "应用程序",
        "disclaimer" => "免责声明",
        "about" => "关于",
        _ => "工具"
    };

    public string AccessControlModeText => AccessControlMode == "acceptSelected"
        ? "仅允许选中应用"
        : "排除选中应用";

    public bool IsRejectSelectedAccessMode => AccessControlMode != "acceptSelected";

    public bool IsAcceptSelectedAccessMode => AccessControlMode == "acceptSelected";

    public int AccessSelectedCount => AccessPackageNames.Count;

    public bool HasVisibleInstalledApplications => VisibleInstalledApplications.Count > 0;

    public bool MissingVisibleInstalledApplications => VisibleInstalledApplications.Count == 0;

    public string PageTitle => CurrentPage switch
    {
        "profiles" => "订阅",
        "proxies" => "代理",
        "config" => "配置",
        "tools" => ToolPageTitle,
        _ => "仪表盘"
    };

    public string PageSubtitle => CurrentPage switch
    {
        "profiles" => "远程配置与本地文件",
        "proxies" => ProxySummary,
        "config" => "订阅和本地配置文件",
        "tools" => string.IsNullOrWhiteSpace(CurrentToolPage) ? "工具和设置" : "FlClash 风格设置页",
        _ => RunningStateText
    };

    public string RunningActionText => IsBusy || IsStarting ? "处理中" : IsRunning ? "停止" : "启动";

    public IAsyncRelayCommand RunningActionCommand => IsRunning ? StopCommand : StartCommand;

    public string RunningStateText => StateText switch
    {
        nameof(ClashRunState.Starting) => "正在启动",
        nameof(ClashRunState.Error) => "启动失败",
        _ => IsRunning ? $"已运行 {RunningDuration}" : "未启动"
    };

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
        : TestUrl;

    public string OutboundModeTitle => OutboundMode switch
    {
        "global" => "Global",
        "direct" => "Direct",
        _ => "Rule"
    };

    public bool IsRuleMode => OutboundMode == "rule";

    public bool IsGlobalMode => OutboundMode == "global";

    public bool IsDirectMode => OutboundMode == "direct";

    public bool IsProxySortDefault => ProxySortMode == "配置顺序";

    public bool IsProxySortByDelay => ProxySortMode == "按延迟";

    public bool IsProxySortByName => ProxySortMode == "按名称";

    public bool IsStarting => StateText == ClashRunState.Starting.ToString();

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverviewPage));
        OnPropertyChanged(nameof(IsProfilesPage));
        OnPropertyChanged(nameof(IsProxiesPage));
        OnPropertyChanged(nameof(IsConfigPage));
        OnPropertyChanged(nameof(IsToolsPage));
        OnPropertyChanged(nameof(IsConfigPageWithProfiles));
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(IsAppBarBackVisible));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        NotifyToolPageProperties();
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(RunningActionText));
        OnPropertyChanged(nameof(RunningActionCommand));
        OnPropertyChanged(nameof(RunningStateText));
        OnPropertyChanged(nameof(IsProxyNavigationVisible));
        OnPropertyChanged(nameof(IsCompactNavigationVisible));
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(IsAppBarBackVisible));
        OnPropertyChanged(nameof(PageSubtitle));
        if (value)
        {
            _startedAt ??= DateTimeOffset.Now;
            UpdateRunningDuration();
            _telemetryTimer.Start();
            _ = RefreshNetworkDetectionAsync();
            _ = RefreshRuntimeUiAfterStartAsync();
        }
        else
        {
            if (IsProxiesPage)
            {
                CurrentPage = "overview";
            }

            _telemetryTimer.Stop();
            _startedAt = null;
            RunningDuration = "00:00:00";
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(RunningActionText));
    }

    partial void OnStateTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(RunningActionText));
        OnPropertyChanged(nameof(RunningStateText));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnEnableIpv6Changed(bool value)
    {
        OnPropertyChanged(nameof(VpnIntranetAddress));
        QueueConfigSettingSave();
        QueueRuntimeRestart("IPv6", needsConfigSave: true);
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
        OnPropertyChanged(nameof(IsLightTheme));
        QueueStateSave();
    }

    partial void OnLocaleChanged(string value)
    {
        OnPropertyChanged(nameof(LocaleText));
        OnPropertyChanged(nameof(IsDefaultLocale));
        OnPropertyChanged(nameof(IsChineseLocale));
        OnPropertyChanged(nameof(IsEnglishLocale));
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

    partial void OnIsProxySearchVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(IsAppBarBackVisible));
    }

    partial void OnIsProxyGroupListVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(IsAppBarBackVisible));
    }

    partial void OnProxySortModeChanged(string value)
    {
        UpdateVisibleProxyNodes();
        OnPropertyChanged(nameof(IsProxySortDefault));
        OnPropertyChanged(nameof(IsProxySortByDelay));
        OnPropertyChanged(nameof(IsProxySortByName));
    }

    partial void OnOutboundModeChanged(string value)
    {
        OnPropertyChanged(nameof(OutboundModeTitle));
        OnPropertyChanged(nameof(IsRuleMode));
        OnPropertyChanged(nameof(IsGlobalMode));
        OnPropertyChanged(nameof(IsDirectMode));
        QueueStateSave();
    }

    partial void OnCurrentToolPageChanged(string value)
    {
        NotifyToolPageProperties();
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(IsAppBarBackVisible));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    partial void OnSelectedConfigProfileChanged(ConfigProfileItem? value)
    {
        ApplySelectedConfigProfile(value);
    }

    partial void OnIsAddProfilePanelVisibleChanged(bool value)
    {
        if (!value)
        {
            IsAddProfileUrlInputVisible = false;
        }

        OnPropertyChanged(nameof(IsAddProfileMenuVisible));
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(IsAppBarBackVisible));
    }

    partial void OnIsAddProfileUrlInputVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAddProfileMenuVisible));
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
        if (!IsProxyNavigationVisible)
        {
            LastMessage = "启动 VPN 后可查看代理";
            return;
        }

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

    [RelayCommand]
    private void NavigateBack()
    {
        HandleBack();
    }

    public bool HandleBack()
    {
        if (IsAddProfilePanelVisible)
        {
            IsAddProfilePanelVisible = false;
            return true;
        }

        if (IsProxyGroupListVisible)
        {
            IsProxyGroupListVisible = false;
            return true;
        }

        if (IsProxySearchVisible)
        {
            IsProxySearchVisible = false;
            ProxySearchText = string.Empty;
            return true;
        }

        if (IsToolsPage && !string.IsNullOrWhiteSpace(CurrentToolPage))
        {
            CurrentToolPage = string.Empty;
            return true;
        }

        if (!IsOverviewPage)
        {
            CurrentPage = "overview";
            return true;
        }

        return false;
    }

    private void QueueRuntimeRestart(string reason, bool needsConfigSave = false)
    {
        if (_isApplyingStoredState || !_isStateLoaded)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => QueueRuntimeRestart(reason, needsConfigSave));
            return;
        }

        if (!IsRunning)
        {
            LastMessage = $"{reason} 已保存";
            return;
        }

        _pendingRuntimeRestartReason = reason;
        _pendingRuntimeRestartNeedsConfigSave |= needsConfigSave;
        _runtimeRestartTimer.Stop();
        _runtimeRestartTimer.Start();
    }

    private async Task RestartRuntimeForPendingChangeAsync()
    {
        var reason = string.IsNullOrWhiteSpace(_pendingRuntimeRestartReason)
            ? "运行设置"
            : _pendingRuntimeRestartReason;
        var needsConfigSave = _pendingRuntimeRestartNeedsConfigSave;
        _pendingRuntimeRestartReason = string.Empty;
        _pendingRuntimeRestartNeedsConfigSave = false;

        await RunAsync(async () =>
        {
            await ApplyRunningRuntimeRestartAsync(reason, needsConfigSave);
        });
    }

    private async Task ApplyRunningRuntimeRestartAsync(string reason, bool needsConfigSave)
    {
        if (needsConfigSave)
        {
            SaveConfigContent();
        }

        await PersistAppStateAsync();
        if (!IsRunning)
        {
            LastMessage = $"{reason} 已保存";
            return;
        }

        LastMessage = $"{reason} 已保存，正在重启核心";
        await _runtime.StopAsync();
        await StartRuntimeCoreAsync();
    }

    private bool CanStart()
    {
        return !IsBusy && !IsRunning && !IsStarting;
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
