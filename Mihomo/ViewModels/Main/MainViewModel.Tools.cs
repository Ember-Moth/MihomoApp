using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Mihomo.Services.Storage;

namespace Mihomo.ViewModels;

public partial class MainViewModel
{
    private const string BackupStateEntryName = "app_state.json";

    private sealed record BackupProfileFile(int ProfileId, string EntryName);

    private sealed record BackupDocument(AppStateSnapshot State, IReadOnlyList<BackupProfileFile> ProfileFiles);

    public IReadOnlyList<string> AccessPackageNames => SplitPackageCsv(AccessPackageCsv);

    [RelayCommand]
    private async Task ShowToolPageAsync(string? page)
    {
        CurrentToolPage = page?.Trim() switch
        {
            "language" => "language",
            "theme" => "theme",
            "backup" => "backup",
            "access" => "access",
            "basic" => "basic",
            "advanced" => "advanced",
            "application" => "application",
            "disclaimer" => "disclaimer",
            "about" => "about",
            _ => string.Empty
        };

        if (CurrentToolPage == "access" && InstalledApplications.Count == 0)
        {
            await LoadInstalledApplicationsAsync();
        }
    }

    [RelayCommand]
    private void CloseToolPage()
    {
        CurrentToolPage = string.Empty;
    }

    [RelayCommand]
    private void SetLocale(string? locale)
    {
        Locale = locale?.Trim() switch
        {
            "zh-Hans" => "zh-Hans",
            "en" => "en",
            _ => string.Empty
        };
        LastMessage = $"语言: {LocaleText}";
    }

    [RelayCommand]
    private void SetThemeMode(string? mode)
    {
        IsDarkTheme = !string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase);
        LastMessage = $"主题: {ThemeModeText}";
    }

    [RelayCommand]
    private void SetAccessControlMode(string? mode)
    {
        AccessControlMode = NormalizeAccessControlMode(mode);
        LastMessage = $"访问控制: {AccessControlModeText}";
    }

    [RelayCommand]
    private async Task LoadInstalledApplicationsAsync()
    {
        await RunAsync(async () =>
        {
            IsLoadingApplications = true;
            try
            {
                var selectedPackages = AccessPackageNameSet();
                var applications = await _runtime.GetInstalledApplicationsAsync();
                InstalledApplications.Clear();

                foreach (var application in applications
                    .OrderBy(item => item.IsSystem)
                    .ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.PackageName, StringComparer.OrdinalIgnoreCase))
                {
                    InstalledApplications.Add(new InstalledApplicationItem(application)
                    {
                        IsSelected = selectedPackages.Contains(application.PackageName)
                    });
                }

                UpdateVisibleInstalledApplications();
                LastMessage = InstalledApplications.Count == 0
                    ? "未读取到应用列表"
                    : $"已读取 {InstalledApplications.Count} 个应用";
            }
            finally
            {
                IsLoadingApplications = false;
            }
        });
    }

    [RelayCommand]
    private void ToggleAccessApplication(InstalledApplicationItem? application)
    {
        if (application == null)
        {
            return;
        }

        var packageNames = AccessPackageNameSet();
        if (!packageNames.Add(application.PackageName))
        {
            packageNames.Remove(application.PackageName);
        }

        AccessPackageCsv = JoinPackageCsv(packageNames);
        application.IsSelected = packageNames.Contains(application.PackageName);
        MarkAccessSelections(packageNames);
        LastMessage = $"{application.Label}: {(application.IsSelected ? "已选择" : "已取消")}";
    }

    [RelayCommand]
    private void SelectAllVisibleAccessApplications()
    {
        var packageNames = AccessPackageNameSet();
        foreach (var application in VisibleInstalledApplications)
        {
            packageNames.Add(application.PackageName);
        }

        AccessPackageCsv = JoinPackageCsv(packageNames);
        MarkAccessSelections(packageNames);
        LastMessage = $"已选择 {AccessSelectedCount} 个应用";
    }

    [RelayCommand]
    private void ClearAccessApplications()
    {
        AccessPackageCsv = string.Empty;
        MarkAccessSelections(new HashSet<string>(StringComparer.Ordinal));
        LastMessage = "访问控制列表已清空";
    }

    public async Task WriteBackupAsync(Stream output)
    {
        await _stateReadyTask;
        await PersistAppStateAsync();

        var profileFiles = new List<BackupProfileFile>();
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var profile in CaptureStoredProfiles())
        {
            if (!File.Exists(profile.FilePath))
            {
                continue;
            }

            var entryName = $"profiles/{profile.Id}/{Path.GetFileName(profile.FilePath)}";
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using (var entryStream = entry.Open())
            await using (var fileStream = File.OpenRead(profile.FilePath))
            {
                await fileStream.CopyToAsync(entryStream);
            }

            profileFiles.Add(new BackupProfileFile(profile.Id, entryName));
        }

        var document = new BackupDocument(
            CaptureAppState(CaptureStoredProfiles(), SelectedConfigProfile?.Id),
            profileFiles);
        var stateEntry = archive.CreateEntry(BackupStateEntryName, CompressionLevel.Optimal);
        await using (var stateStream = stateEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stateStream, document);
        }

        LastMessage = "备份已写入";
    }

    public async Task RestoreBackupAsync(Stream input)
    {
        await RunAsync(async () =>
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var stateEntry = archive.GetEntry(BackupStateEntryName)
                ?? throw new InvalidOperationException("备份文件缺少 app_state.json");

            BackupDocument? document;
            await using (var stateStream = stateEntry.Open())
            {
                document = await JsonSerializer.DeserializeAsync<BackupDocument>(stateStream);
            }

            if (document == null)
            {
                throw new InvalidOperationException("备份文件无法解析");
            }

            var restoredProfiles = RestoreProfileFiles(archive, document);
            var selectedProfileId = document.State.CurrentProfileId;
            var selectedProfile = restoredProfiles.FirstOrDefault(profile => profile.Id == selectedProfileId)
                ?? restoredProfiles.FirstOrDefault(profile => profile.IsSelected)
                ?? restoredProfiles.FirstOrDefault();
            var restoredState = document.State with
            {
                HomeDirectory = HomeDirectory,
                ConfigPath = selectedProfile?.FilePath ?? ConfigPath,
                CurrentProfileId = selectedProfile?.Id,
                Profiles = restoredProfiles
            };

            await _stateStore.ReplaceProfilesAsync(restoredProfiles, restoredState.CurrentProfileId);
            await _stateStore.SaveAppStateAsync(restoredState);
            ApplyRestoredState(restoredState);
            LastMessage = "备份已恢复";
        });
    }

    private IReadOnlyList<StoredConfigProfile> RestoreProfileFiles(
        ZipArchive archive,
        BackupDocument document)
    {
        var profileEntries = document.ProfileFiles.ToDictionary(
            item => item.ProfileId,
            item => item.EntryName);
        var directory = Path.Combine(HomeDirectory, "profiles");
        Directory.CreateDirectory(directory);

        return document.State.Profiles
            .Select(profile =>
            {
                var filePath = profile.FilePath;
                if (profileEntries.TryGetValue(profile.Id, out var entryName) &&
                    archive.GetEntry(entryName) is { } entry)
                {
                    var fileName = $"{profile.Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Path.GetFileName(profile.FilePath)}";
                    filePath = Path.Combine(directory, SanitizeProfileFileName(fileName));
                    using var entryStream = entry.Open();
                    using var fileStream = File.Create(filePath);
                    entryStream.CopyTo(fileStream);
                }

                return profile with { FilePath = filePath };
            })
            .ToArray();
    }

    private void ApplyRestoredState(AppStateSnapshot state)
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
            Stack = state.Stack;
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
            ApplyConfigProfiles(state.Profiles, state.CurrentProfileId);
            LoadConfigContent();
            ApplyThemeMode();
        }
        finally
        {
            _isApplyingStoredState = false;
            _isStateLoaded = true;
        }
    }

    partial void OnAccessControlModeChanged(string value)
    {
        AccessControlMode = NormalizeAccessControlMode(value);
        OnPropertyChanged(nameof(AccessControlModeText));
        OnPropertyChanged(nameof(IsRejectSelectedAccessMode));
        OnPropertyChanged(nameof(IsAcceptSelectedAccessMode));
        QueueStateSave();
        QueueRuntimeRestart("访问控制");
    }

    partial void OnAccessPackageCsvChanged(string value)
    {
        OnPropertyChanged(nameof(AccessSelectedCount));
        QueueStateSave();
        if (AccessControlEnabled)
        {
            QueueRuntimeRestart("访问控制");
        }
    }

    partial void OnAccessSearchTextChanged(string value)
    {
        UpdateVisibleInstalledApplications();
    }

    partial void OnAccessFilterSystemAppsChanged(bool value)
    {
        UpdateVisibleInstalledApplications();
        QueueStateSave();
    }

    partial void OnAccessFilterNoInternetAppsChanged(bool value)
    {
        UpdateVisibleInstalledApplications();
        QueueStateSave();
    }

    partial void OnAccessControlEnabledChanged(bool value)
    {
        QueueStateSave();
        QueueRuntimeRestart("访问控制");
    }

    partial void OnIsLoadingApplicationsChanged(bool value)
    {
        LoadInstalledApplicationsCommand.NotifyCanExecuteChanged();
    }

    partial void OnAllowLanChanged(bool value)
    {
        QueueConfigSettingSave();
        QueueRuntimeRestart("Allow LAN", needsConfigSave: true);
    }

    partial void OnLogLevelChanged(string value)
    {
        QueueConfigSettingSave();
        QueueRuntimeRestart("日志等级", needsConfigSave: true);
    }

    partial void OnGlobalUaChanged(string value) => QueueConfigSettingSave();

    partial void OnTestUrlChanged(string value)
    {
        OnPropertyChanged(nameof(ProxyDelayTestUrl));
        QueueConfigSettingSave();
    }

    partial void OnUnifiedDelayChanged(bool value)
    {
        QueueConfigSettingSave();
        QueueRuntimeRestart("统一延迟", needsConfigSave: true);
    }

    partial void OnTcpConcurrentChanged(bool value)
    {
        QueueConfigSettingSave();
        QueueRuntimeRestart("TCP 并发", needsConfigSave: true);
    }

    partial void OnFindProcessChanged(bool value)
    {
        QueueConfigSettingSave();
        QueueRuntimeRestart("进程匹配", needsConfigSave: true);
    }

    partial void OnGeodataMemoryChanged(bool value)
    {
        QueueConfigSettingSave();
        QueueRuntimeRestart("Geo低内存模式", needsConfigSave: true);
    }

    partial void OnExternalControllerChanged(bool value)
    {
        QueueConfigSettingSave();
        QueueRuntimeRestart("外部控制器", needsConfigSave: true);
    }

    partial void OnMinimizeOnExitChanged(bool value) => QueueStateSave();

    partial void OnAutoRunChanged(bool value) => QueueStateSave();

    partial void OnHiddenChanged(bool value) => QueueStateSave();

    partial void OnAnimateTabsChanged(bool value) => QueueStateSave();

    partial void OnOpenLogsChanged(bool value) => QueueStateSave();

    partial void OnCloseConnectionsChanged(bool value) => QueueStateSave();

    partial void OnOnlyStatisticsProxyChanged(bool value) => QueueStateSave();

    partial void OnCrashlyticsChanged(bool value) => QueueStateSave();

    partial void OnAutoCheckUpdatesChanged(bool value) => QueueStateSave();

    private void QueueConfigSettingSave()
    {
        QueueStateSave();
        if (_isApplyingStoredState || !_isStateLoaded || string.IsNullOrWhiteSpace(ConfigContent))
        {
            return;
        }

        ConfigContent = ApplyConfigSettings(ConfigContent);
    }

    private async Task ApplyGeodataLoaderChangeAsync(bool isMemconservative)
    {
        await RunAsync(async () =>
        {
            SaveConfigContent();
            await PersistAppStateAsync();

            var mode = isMemconservative ? "memconservative" : "standard";
            LastMessage = $"Geo低内存模式: {mode}";

            if (!IsRunning)
            {
                return;
            }

            LastMessage = $"Geo低内存模式: {mode}，正在重启核心";
            await _runtime.StopAsync();
            await StartRuntimeCoreAsync();
        });
    }

    private void UpdateVisibleInstalledApplications()
    {
        var selectedPackages = AccessPackageNameSet();
        var searchText = AccessSearchText.Trim();
        var filtered = InstalledApplications
            .Where(application => !AccessFilterSystemApps || !application.IsSystem)
            .Where(application => !AccessFilterNoInternetApps || application.UsesInternet)
            .Where(application =>
                string.IsNullOrWhiteSpace(searchText) ||
                application.Label.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                application.PackageName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(application => selectedPackages.Contains(application.PackageName))
            .ThenBy(application => application.Label, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(application => application.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        VisibleInstalledApplications.Clear();
        foreach (var application in filtered)
        {
            application.IsSelected = selectedPackages.Contains(application.PackageName);
            VisibleInstalledApplications.Add(application);
        }

        OnPropertyChanged(nameof(HasVisibleInstalledApplications));
        OnPropertyChanged(nameof(MissingVisibleInstalledApplications));
    }

    private void MarkAccessSelections(IReadOnlySet<string> packageNames)
    {
        foreach (var application in InstalledApplications)
        {
            application.IsSelected = packageNames.Contains(application.PackageName);
        }

        UpdateVisibleInstalledApplications();
        OnPropertyChanged(nameof(AccessSelectedCount));
        QueueStateSave();
    }

    private HashSet<string> AccessPackageNameSet()
    {
        return new HashSet<string>(AccessPackageNames, StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> SplitPackageCsv(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string JoinPackageCsv(IEnumerable<string> values)
    {
        return string.Join(',', values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
    }

    private static string NormalizeAccessControlMode(string? mode)
    {
        return mode?.Trim() switch
        {
            "acceptSelected" => "acceptSelected",
            _ => "rejectSelected"
        };
    }

    private void NotifyToolPageProperties()
    {
        OnPropertyChanged(nameof(IsToolsRootPage));
        OnPropertyChanged(nameof(IsLanguageToolPage));
        OnPropertyChanged(nameof(IsThemeToolPage));
        OnPropertyChanged(nameof(IsBackupToolPage));
        OnPropertyChanged(nameof(IsAccessToolPage));
        OnPropertyChanged(nameof(IsBasicConfigToolPage));
        OnPropertyChanged(nameof(IsAdvancedConfigToolPage));
        OnPropertyChanged(nameof(IsApplicationSettingsToolPage));
        OnPropertyChanged(nameof(IsDisclaimerToolPage));
        OnPropertyChanged(nameof(IsAboutToolPage));
        OnPropertyChanged(nameof(ToolPageTitle));
    }
}
