using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Mihomo.Services.Storage;

namespace Mihomo.ViewModels;

public partial class MainViewModel
{
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
                    DnsHijacking = state.DnsHijacking;
                    SystemProxy = state.SystemProxy;
                    Stack = state.Stack;
                    RouteAddressCsv = state.RouteAddressCsv;
                    IsDarkTheme = state.IsDarkTheme;
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
            DnsHijacking,
            SystemProxy,
            Stack,
            RouteAddressCsv,
            IsDarkTheme,
            currentProfileId,
            profiles);
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
        SyncConfigProfilesCommand.NotifyCanExecuteChanged();
        DeleteSelectedProfileCommand.NotifyCanExecuteChanged();
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
            foreach (var profile in urlProfiles)
            {
                SubscriptionUrl = profile.Url;
                ConfigPath = profile.FilePath;
                await ImportSubscriptionCoreAsync(profile.Url, profile.Label);
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

    partial void OnHomeDirectoryChanged(string value)
    {
        QueueStateSave();
    }

    partial void OnConfigPathChanged(string value)
    {
        QueueStateSave();
    }

    partial void OnSubscriptionUrlChanged(string value)
    {
        QueueStateSave();
    }

    partial void OnMixedPortChanged(string value)
    {
        QueueStateSave();
    }

    partial void OnEnableTunChanged(bool value)
    {
        QueueStateSave();
    }

    partial void OnDnsHijackingChanged(bool value)
    {
        QueueStateSave();
    }

    partial void OnSystemProxyChanged(bool value)
    {
        QueueStateSave();
    }

    partial void OnStackChanged(string value)
    {
        QueueStateSave();
    }

    partial void OnRouteAddressCsvChanged(string value)
    {
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
}
