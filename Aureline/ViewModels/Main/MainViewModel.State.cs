using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Aureline.Services.Storage;

namespace Aureline.ViewModels;

public partial class MainViewModel
{
    private const char ProxySelectionKeySeparator = '\u001f';

    private async Task InitializePersistentStateAsync()
    {
        try
        {
            var defaults = CaptureAppState(Array.Empty<StoredConfigProfile>(), null);
            var state = await _stateStore.LoadAsync(defaults);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isApplyingStoredState = true;
                try
                {
                    HomeDirectory = state.HomeDirectory;
                    ConfigPath = state.ConfigPath;
                    SubscriptionUrl = state.SubscriptionUrl;
                    MixedPort = state.MixedPort;
                    EnableTun = state.EnableTun;
                    EnableIpv6 = state.EnableIpv6;
                    AllowLan = state.AllowLan;
                    DnsHijacking = state.DnsHijacking;
                    SystemProxy = state.SystemProxy;
                    Stack = NormalizeStack(state.Stack);
                    RouteAddressCsv = state.RouteAddressCsv;
                    OutboundMode = NormalizeOutboundMode(state.OutboundMode);
                    LogLevel = NormalizeLogLevel(state.LogLevel);
                    GlobalUa = state.GlobalUa;
                    TestUrl = NormalizeTestUrl(state.TestUrl);
                    UnifiedDelay = state.UnifiedDelay;
                    TcpConcurrent = state.TcpConcurrent;
                    FindProcess = state.FindProcess;
                    GeodataMemory = state.GeodataMemory;
                    ExternalController = state.ExternalController;
                    Locale = state.Locale;
                    IsDarkTheme = state.IsDarkTheme;
                    MinimizeOnExit = state.MinimizeOnExit;
                    AutoRun = state.AutoRun;
                    Hidden = state.Hidden;
                    AnimateTabs = state.AnimateTabs;
                    OpenLogs = state.OpenLogs;
                    CloseConnections = state.CloseConnections;
                    OnlyStatisticsProxy = state.OnlyStatisticsProxy;
                    Crashlytics = state.Crashlytics;
                    AutoCheckUpdates = state.AutoCheckUpdates;
                    AccessControlEnabled = state.AccessControlEnabled;
                    AccessControlMode = NormalizeAccessControlMode(state.AccessControlMode);
                    AccessPackageCsv = state.AccessPackageCsv;
                    AccessFilterSystemApps = state.AccessFilterSystemApps;
                    AccessFilterNoInternetApps = state.AccessFilterNoInternetApps;
                    ApplyProxySelectionsJson(state.ProxySelectionsJson);
                    ApplyConfigProfiles(state.Profiles, state.CurrentProfileId);
                    LoadConfigContent();
                    ApplyThemeMode();
                }
                finally
                {
                    _isApplyingStoredState = false;
                    _isStateLoaded = true;
                }
            });

            await BootstrapProfileIfNeededAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LastMessage = $"状态数据库初始化失败: {ex.Message}";
                _isStateLoaded = true;
            });
        }
    }

    private void QueueStateSave()
    {
        if (_isApplyingStoredState || !_isStateLoaded)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(QueueStateSave);
            return;
        }

        _stateSaveTimer.Stop();
        _stateSaveTimer.Start();
    }

    private async Task PersistAppStateAsync()
    {
        if (_isApplyingStoredState || !_isStateLoaded || _isPersistingState)
        {
            return;
        }

        try
        {
            _isPersistingState = true;
            await _stateStore.SaveAppStateAsync(CaptureAppState(CaptureStoredProfiles(), SelectedConfigProfile?.Id));
        }
        catch (Exception ex)
        {
            LastMessage = $"状态保存失败: {ex.Message}";
        }
        finally
        {
            _isPersistingState = false;
        }
    }

    private AppStateSnapshot CaptureAppState(
        IReadOnlyList<StoredConfigProfile> profiles,
        int? currentProfileId)
    {
        return new AppStateSnapshot(
            HomeDirectory,
            ConfigPath,
            SubscriptionUrl,
            MixedPort,
            EnableTun,
            EnableIpv6,
            AllowLan,
            DnsHijacking,
            SystemProxy,
            Stack,
            RouteAddressCsv,
            OutboundMode,
            LogLevel,
            GlobalUa,
            TestUrl,
            UnifiedDelay,
            TcpConcurrent,
            FindProcess,
            GeodataMemory,
            ExternalController,
            Locale,
            IsDarkTheme,
            MinimizeOnExit,
            AutoRun,
            Hidden,
            AnimateTabs,
            OpenLogs,
            CloseConnections,
            OnlyStatisticsProxy,
            Crashlytics,
            AutoCheckUpdates,
            AccessControlEnabled,
            AccessControlMode,
            AccessPackageCsv,
            AccessFilterSystemApps,
            AccessFilterNoInternetApps,
            currentProfileId,
            SerializeProxySelections(),
            profiles);
    }

    private void ApplyProxySelectionsJson(string json)
    {
        _proxySelections.Clear();
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var selections = JsonSerializer.Deserialize(
                json,
                AppStateJsonContext.Default.DictionaryStringString);
            if (selections == null)
            {
                return;
            }

            foreach (var (key, value) in selections)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                _proxySelections[key] = value;
            }
        }
        catch
        {
            _proxySelections.Clear();
        }
    }

    private string SerializeProxySelections()
    {
        return _proxySelections.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(
                _proxySelections,
                AppStateJsonContext.Default.DictionaryStringString);
    }

    private void RememberProxySelection(string groupName, string proxyName)
    {
        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(proxyName))
        {
            return;
        }

        _proxySelections[ProxySelectionKey(groupName)] = proxyName;
    }

    private bool TryGetRememberedProxySelection(string groupName, out string proxyName)
    {
        return _proxySelections.TryGetValue(ProxySelectionKey(groupName), out proxyName!) &&
            !string.IsNullOrWhiteSpace(proxyName);
    }

    private string ProxySelectionKey(string groupName)
    {
        return $"{CurrentProxySelectionScope()}{ProxySelectionKeySeparator}{groupName}";
    }

    private string CurrentProxySelectionScope()
    {
        if (SelectedConfigProfile?.Id is > 0)
        {
            return $"profile:{SelectedConfigProfile.Id}";
        }

        var configPath = ConfigPath.Trim();
        return string.IsNullOrWhiteSpace(configPath)
            ? "profile:none"
            : $"path:{configPath}";
    }

    private IReadOnlyList<StoredConfigProfile> CaptureStoredProfiles()
    {
        return ConfigProfiles
            .Select((profile, index) => ToStoredProfile(profile, index))
            .ToArray();
    }

    private static StoredConfigProfile ToStoredProfile(ConfigProfileItem profile, int sortOrder)
    {
        return new StoredConfigProfile(
            profile.Id,
            profile.Type == ConfigProfileType.Url ? "url" : "file",
            profile.Label,
            profile.FilePath,
            profile.Url,
            profile.LastUpdateDate,
            sortOrder,
            profile.IsSelected);
    }

    private void ApplyConfigProfiles(IReadOnlyList<StoredConfigProfile> profiles, int? currentProfileId)
    {
        ConfigProfiles.Clear();
        ConfigProfileItem? selected = null;

        foreach (var profile in profiles)
        {
            var item = new ConfigProfileItem(
                profile.Id,
                profile.Type.Equals("url", StringComparison.OrdinalIgnoreCase)
                    ? ConfigProfileType.Url
                    : ConfigProfileType.File,
                profile.Label,
                profile.FilePath,
                profile.Url,
                profile.LastUpdateDate)
            {
                IsSelected = profile.IsSelected || profile.Id == currentProfileId
            };

            if (item.IsSelected)
            {
                selected = item;
            }

            ConfigProfiles.Add(item);
        }

        SelectedConfigProfile = selected ?? ConfigProfiles.FirstOrDefault();
        FocusedConfigProfile = SelectedConfigProfile ?? ConfigProfiles.FirstOrDefault();
        UpdateConfigProfileState();
    }

    private void ApplySelectedConfigProfile(ConfigProfileItem? value)
    {
        foreach (var profile in ConfigProfiles)
        {
            profile.IsSelected = ReferenceEquals(profile, value);
        }

        if (value != null)
        {
            ConfigPath = value.FilePath;
            if (value.IsUrl)
            {
                SubscriptionUrl = value.Url;
            }

            LoadConfigContent();
            QueueRuntimeRestart("配置切换");

            if (_isStateLoaded && !_isApplyingStoredState)
            {
                _ = _stateStore.SelectProfileAsync(value.Id);
            }
        }

        UpdateConfigProfileState();
        QueueStateSave();
    }

    private void UpdateConfigProfileState()
    {
        OnPropertyChanged(nameof(HasConfigProfiles));
        OnPropertyChanged(nameof(MissingConfigProfiles));
        OnPropertyChanged(nameof(IsConfigPageWithProfiles));
        OnPropertyChanged(nameof(ConfigProfileCountText));
        NotifyConfigProfileDetailProperties();
        SyncConfigProfilesCommand.NotifyCanExecuteChanged();
        SortConfigProfilesCommand.NotifyCanExecuteChanged();
        DeleteSelectedProfileCommand.NotifyCanExecuteChanged();
        ActivateFocusedConfigProfileCommand.NotifyCanExecuteChanged();
        ValidateFocusedConfigProfileCommand.NotifyCanExecuteChanged();
        SyncFocusedConfigProfileCommand.NotifyCanExecuteChanged();
        DeleteFocusedConfigProfileCommand.NotifyCanExecuteChanged();
    }

    private async Task BootstrapProfileIfNeededAsync()
    {
        var configPath = await Dispatcher.UIThread.InvokeAsync(() =>
            ConfigProfiles.Count > 0 ? string.Empty : ConfigPath);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return;
        }

        var savedProfile = await _stateStore.UpsertProfileAsync(
            new StoredConfigProfile(
                0,
                "file",
                ProfileLabelFromPath(configPath),
                configPath,
                string.Empty,
                new DateTimeOffset(File.GetLastWriteTime(configPath)),
                0,
                true));

        await ReloadConfigProfilesAsync(savedProfile.Id);
    }

    private async Task UpsertCurrentProfileAsync(ConfigProfileType type, string label, string url)
    {
        var existing = type == ConfigProfileType.Url && !string.IsNullOrWhiteSpace(url)
            ? ConfigProfiles.FirstOrDefault(profile =>
                profile.IsUrl && string.Equals(profile.Url, url, StringComparison.OrdinalIgnoreCase))
            : ConfigProfiles.FirstOrDefault(profile =>
                !profile.IsUrl && string.Equals(profile.FilePath, ConfigPath, StringComparison.OrdinalIgnoreCase));

        var storedProfile = new StoredConfigProfile(
            existing?.Id ?? 0,
            type == ConfigProfileType.Url ? "url" : "file",
            label,
            ConfigPath,
            url,
            DateTimeOffset.Now,
            existing == null ? ConfigProfiles.Count : ConfigProfiles.IndexOf(existing),
            true);

        var savedProfile = await _stateStore.UpsertProfileAsync(storedProfile);
        await ReloadConfigProfilesAsync(savedProfile.Id);
        await PersistAppStateAsync();
    }

    private async Task ReloadConfigProfilesAsync(int? selectedProfileId)
    {
        var profiles = await _stateStore.LoadProfilesAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isApplyingStoredState = true;
            try
            {
                ApplyConfigProfiles(profiles, selectedProfileId);
            }
            finally
            {
                _isApplyingStoredState = false;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(HasConfigProfiles))]
    private async Task SyncConfigProfilesAsync()
    {
        await RunAsync(async () =>
        {
            var urlProfiles = ConfigProfiles.Where(profile => profile.IsUrl).ToArray();
            var restartRequired = false;
            foreach (var profile in urlProfiles)
            {
                restartRequired |= SelectedConfigProfile?.Id == profile.Id;
                await RefreshSubscriptionProfileAsync(profile);
            }

            LastMessage = urlProfiles.Length == 0 ? "没有可同步的远程订阅" : $"已同步 {urlProfiles.Length} 个订阅";
            if (restartRequired)
            {
                await ApplyRunningRuntimeRestartAsync("订阅配置", needsConfigSave: false);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(HasConfigProfiles))]
    private async Task DeleteSelectedProfileAsync()
    {
        if (SelectedConfigProfile == null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _stateStore.DeleteProfileAsync(SelectedConfigProfile.Id);
            await ReloadConfigProfilesAsync(null);
            LastMessage = "配置已删除";
        });
    }

    [RelayCommand(CanExecute = nameof(HasConfigProfiles))]
    private async Task SortConfigProfilesAsync()
    {
        await RunAsync(async () =>
        {
            var sortedProfiles = ConfigProfiles
                .OrderBy(profile => profile.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.Id)
                .ToArray();

            for (var i = 0; i < sortedProfiles.Length; i++)
            {
                await _stateStore.UpsertProfileAsync(ToStoredProfile(sortedProfiles[i], i));
            }

            await ReloadConfigProfilesAsync(SelectedConfigProfile?.Id);
            LastMessage = "配置排序已保存";
        });
    }

    [RelayCommand]
    private void ShowAddProfilePanel()
    {
        IsAddProfilePanelVisible = true;
        IsAddProfileUrlInputVisible = false;
    }

    [RelayCommand]
    private void HideAddProfilePanel()
    {
        IsAddProfilePanelVisible = false;
    }

    [RelayCommand]
    private void ShowAddProfileUrlInput()
    {
        IsAddProfilePanelVisible = true;
        IsAddProfileUrlInputVisible = true;
    }

    [RelayCommand]
    private void SelectConfigProfile(ConfigProfileItem? profile)
    {
        if (profile != null)
        {
            SelectedConfigProfile = profile;
        }
    }

    [RelayCommand]
    private void OpenConfigProfileDetail(ConfigProfileItem? profile)
    {
        if (profile != null)
        {
            FocusedConfigProfile = profile;
        }
    }

    [RelayCommand(CanExecute = nameof(CanActivateFocusedConfigProfile))]
    private void ActivateFocusedConfigProfile()
    {
        if (FocusedConfigProfile == null || FocusedConfigProfileIsActive)
        {
            return;
        }

        SelectedConfigProfile = FocusedConfigProfile;
        LastMessage = $"已启用配置: {FocusedConfigProfile.Label}";
    }

    [RelayCommand(CanExecute = nameof(CanValidateFocusedConfigProfile))]
    private async Task ValidateFocusedConfigProfileAsync()
    {
        if (FocusedConfigProfile == null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var message = await _runtime.ValidateConfigAsync(FocusedConfigProfile.FilePath);
            LastMessage = string.IsNullOrWhiteSpace(message)
                ? $"配置有效: {FocusedConfigProfile.Label}"
                : message;
        });
    }

    [RelayCommand(CanExecute = nameof(CanSyncFocusedConfigProfile))]
    private async Task SyncFocusedConfigProfileAsync()
    {
        if (FocusedConfigProfile?.IsUrl != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var restartRequired = SelectedConfigProfile?.Id == FocusedConfigProfile.Id;
            await RefreshSubscriptionProfileAsync(FocusedConfigProfile);
            LastMessage = $"订阅已更新: {FocusedConfigProfile?.Label ?? "配置"}";
            if (restartRequired)
            {
                await ApplyRunningRuntimeRestartAsync("订阅配置", needsConfigSave: false);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseFocusedConfigProfile))]
    private async Task DeleteFocusedConfigProfileAsync()
    {
        if (FocusedConfigProfile == null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var profile = FocusedConfigProfile;
            await _stateStore.DeleteProfileAsync(profile.Id);
            await ReloadConfigProfilesAsync(null);
            LastMessage = $"配置已删除: {profile.Label}";
        });
    }

    [RelayCommand]
    private async Task SyncConfigProfileAsync(ConfigProfileItem? profile)
    {
        if (profile?.IsUrl != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var restartRequired = SelectedConfigProfile?.Id == profile.Id;
            await RefreshSubscriptionProfileAsync(profile);
            LastMessage = $"订阅已更新: {profile.Label}";
            if (restartRequired)
            {
                await ApplyRunningRuntimeRestartAsync("订阅配置", needsConfigSave: false);
            }
        });
    }

    [RelayCommand]
    private async Task DeleteConfigProfileAsync(ConfigProfileItem? profile)
    {
        if (profile == null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _stateStore.DeleteProfileAsync(profile.Id);
            await ReloadConfigProfilesAsync(null);
            LastMessage = "配置已删除";
        });
    }

    [RelayCommand]
    private async Task ImportProfileFileAsync(PickedProfileFile? file)
    {
        if (file == null || string.IsNullOrWhiteSpace(file.Content))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var label = ProfileLabelFromPath(file.Name);
            ConfigPath = BuildManagedProfilePath(label);
            ConfigContent = file.Content;
            ApplySettingsFromConfigContent(ConfigContent);
            SaveConfigContent();
            await UpsertCurrentProfileAsync(ConfigProfileType.File, label, string.Empty);
            IsAddProfilePanelVisible = false;
            LastMessage = "配置文件已导入";
            await ApplyRunningRuntimeRestartAsync("配置文件", needsConfigSave: false);
        });
    }

    partial void OnHomeDirectoryChanged(string value)
    {
        QueueStateSave();
    }

    partial void OnConfigPathChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentConfigDisplayName));
        QueueStateSave();
    }

    partial void OnSubscriptionUrlChanged(string value)
    {
        QueueStateSave();
    }

    partial void OnMixedPortChanged(string value)
    {
        OnPropertyChanged(nameof(MixedPortText));
        QueueConfigSettingSave();
    }

    partial void OnEnableTunChanged(bool value)
    {
        QueueStateSave();
        QueueRuntimeRestart("VPN/TUN");
    }

    partial void OnDnsHijackingChanged(bool value)
    {
        QueueStateSave();
        QueueRuntimeRestart("DNS 劫持");
    }

    partial void OnSystemProxyChanged(bool value)
    {
        QueueStateSave();
        QueueRuntimeRestart("系统代理");
    }

    partial void OnStackChanged(string value)
    {
        OnPropertyChanged(nameof(StackText));
        QueueStateSave();
        QueueRuntimeRestart("栈模式");
    }

    private string NormalizeStack(string? stack)
    {
        return StackOptions.Contains(stack, StringComparer.OrdinalIgnoreCase)
            ? stack!
            : "system";
    }

    partial void OnRouteAddressCsvChanged(string value)
    {
        OnPropertyChanged(nameof(RouteAddressText));
        QueueStateSave();
    }

    private static string ProfileLabelFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(fileName) ? "config" : fileName;
    }

    private static string ProfileLabelFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return "URL";
    }

    private string BuildManagedProfilePath(string label)
    {
        var directory = Path.Combine(HomeDirectory, "profiles");
        var fileName = $"{SanitizeProfileFileName(label)}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.yaml";
        return Path.Combine(directory, fileName);
    }

    private static string SanitizeProfileFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value
            .Trim()
            .Select(c => invalidChars.Contains(c) || char.IsWhiteSpace(c) ? '-' : c)
            .ToArray();
        var result = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "profile" : result;
    }
}
