using CommunityToolkit.Mvvm.Input;
using Aureline.Services.Storage;

namespace Aureline.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ValidateConfigAsync()
    {
        await RunAsync(async () =>
        {
            SaveConfigContent();
            var message = await _runtime.ValidateConfigAsync(ConfigPath);
            LastMessage = string.IsNullOrWhiteSpace(message) ? "配置有效" : message;
        });
    }

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task SaveConfigAsync()
    {
        await RunAsync(async () =>
        {
            SaveConfigContent();
            await UpsertCurrentProfileAsync(ConfigProfileType.File, ProfileLabelFromPath(ConfigPath), string.Empty);
            LastMessage = $"已保存: {ConfigPath}";
            await ApplyRunningRuntimeRestartAsync("配置", needsConfigSave: false);
        });
    }

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ReloadConfigAsync()
    {
        await RunAsync(() =>
        {
            LoadConfigContent();
            LastMessage = $"已载入: {ConfigPath}";
            return Task.CompletedTask;
        });
    }

    [RelayCommand(CanExecute = nameof(CanValidate))]
    private async Task ResetConfigAsync()
    {
        await RunAsync(async () =>
        {
            ConfigContent = DefaultConfigText(CurrentMixedPort());
            SaveConfigContent();
            await UpsertCurrentProfileAsync(ConfigProfileType.File, ProfileLabelFromPath(ConfigPath), string.Empty);
            LastMessage = $"已重置: {ConfigPath}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanImportSubscription))]
    private async Task ImportSubscriptionAsync()
    {
        await RunAsync(async () =>
        {
            var url = SubscriptionUrl.Trim();
            await ImportSubscriptionCoreAsync(url, ProfileLabelFromUrl(url));
            IsAddProfilePanelVisible = false;
            await ApplyRunningRuntimeRestartAsync("订阅配置", needsConfigSave: false);
        });
    }

    private async Task ImportSubscriptionCoreAsync(string url, string label)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("订阅地址为空");
        }

        var existingProfile = ConfigProfiles.FirstOrDefault(profile =>
            profile.IsUrl && string.Equals(profile.Url, url, StringComparison.OrdinalIgnoreCase));
        ConfigPath = existingProfile?.FilePath ?? BuildManagedProfilePath(label);

        ConfigContent = await DownloadSubscriptionAsync(url);
        ApplySettingsFromConfigContent(ConfigContent);
        SaveConfigContent();
        await UpsertCurrentProfileAsync(ConfigProfileType.Url, label, url);
        LastMessage = "订阅已导入并保存";
    }

    private async Task RefreshSubscriptionProfileAsync(ConfigProfileItem profile)
    {
        if (!profile.IsUrl)
        {
            return;
        }

        var currentProfile = ConfigProfiles.FirstOrDefault(item => item.Id == profile.Id) ?? profile;
        var path = string.IsNullOrWhiteSpace(currentProfile.FilePath)
            ? BuildManagedProfilePath(currentProfile.Label)
            : currentProfile.FilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var wasSelected = SelectedConfigProfile?.Id == currentProfile.Id;
        var content = await DownloadSubscriptionAsync(currentProfile.Url);
        if (wasSelected)
        {
            ConfigPath = path;
            ConfigContent = content;
            ApplySettingsFromConfigContent(ConfigContent);
            SaveConfigContent();
        }
        else
        {
            File.WriteAllText(path, content);
        }

        var sortOrder = Math.Max(0, ConfigProfiles.IndexOf(currentProfile));
        var storedProfile = new StoredConfigProfile(
            currentProfile.Id,
            "url",
            currentProfile.Label,
            path,
            currentProfile.Url,
            DateTimeOffset.Now,
            sortOrder,
            wasSelected);

        await _stateStore.UpsertProfileAsync(storedProfile);
        await ReloadConfigProfilesAsync(SelectedConfigProfile?.Id);
        FocusedConfigProfile = ConfigProfiles.FirstOrDefault(item => item.Id == currentProfile.Id) ?? SelectedConfigProfile;
    }

    private async Task<string> DownloadSubscriptionAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("clash");
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private int CurrentMixedPort()
    {
        return int.TryParse(MixedPort, out var value) && value > 0 ? value : 7890;
    }

    private void LoadConfigContent()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ConfigPath) && File.Exists(ConfigPath))
            {
                ConfigContent = File.ReadAllText(ConfigPath);
                ApplySettingsFromConfigContent(ConfigContent);
                return;
            }

            ConfigContent = DefaultConfigText(CurrentMixedPort());
            ApplySettingsFromConfigContent(ConfigContent);
        }
        catch (Exception ex)
        {
            ConfigContent = DefaultConfigText(CurrentMixedPort());
            ApplySettingsFromConfigContent(ConfigContent);
            LastMessage = ex.Message;
        }
    }

    private void SaveConfigContent()
    {
        var path = ConfigPath.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("配置文件路径为空");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (string.IsNullOrWhiteSpace(ConfigContent))
        {
            ConfigContent = DefaultConfigText(CurrentMixedPort());
        }

        ConfigContent = ApplyConfigSettings(ConfigContent);
        File.WriteAllText(path, ConfigContent);
    }

    [RelayCommand]
    private async Task ChangeOutboundModeAsync(string? mode)
    {
        var nextMode = NormalizeOutboundMode(mode);
        if (OutboundMode == nextMode)
        {
            return;
        }

        await RunAsync(async () =>
        {
            OutboundMode = nextMode;
            ConfigContent = ApplyOutboundMode(ConfigContent, OutboundMode);
            SaveConfigContent();

            if (IsRunning)
            {
                if (CoreCapabilities.SupportsModeSwitch)
                {
                    var applied = await _runtime.SetModeAsync(OutboundMode);
                    LastMessage = applied
                        ? $"出站模式: {OutboundModeTitle}"
                        : $"出站模式切换失败: {OutboundModeTitle}";
                    await RefreshProxiesCoreAsync();
                    return;
                }

                await ApplyRunningRuntimeRestartAsync("出站模式", needsConfigSave: false);
                return;
            }

            LastMessage = $"出站模式: {OutboundModeTitle}";
        });
    }

    private static string ExtractOutboundMode(string content)
    {
        return NormalizeOutboundMode(ExtractRootScalar(content, "mode", "rule"));
    }

    private static string ApplyOutboundMode(string content, string mode)
    {
        return ApplyRootScalar(content, "mode", NormalizeOutboundMode(mode));
    }

    private string ApplyConfigSettings(string content)
    {
        content = ApplyRootScalar(content, "mixed-port", CurrentMixedPort().ToString());
        content = ApplyRootScalar(content, "allow-lan", ToYamlBool(AllowLan));
        content = ApplyRootScalar(content, "mode", NormalizeOutboundMode(OutboundMode));
        content = ApplyRootScalar(content, "log-level", NormalizeLogLevel(LogLevel));
        content = ApplyRootScalar(content, "ipv6", ToYamlBool(EnableIpv6));
        content = ApplyRootScalar(content, "unified-delay", ToYamlBool(UnifiedDelay));
        content = ApplyRootScalar(content, "tcp-concurrent", ToYamlBool(TcpConcurrent));
        content = ApplyRootScalar(content, "find-process-mode", FindProcess ? "always" : "off");
        content = ApplyRootScalar(content, "geodata-loader", GeodataMemory ? "memconservative" : "standard");
        content = ExternalController
            ? ApplyRootScalar(content, "external-controller", "127.0.0.1:9090")
            : RemoveRootScalar(content, "external-controller");

        var ua = GlobalUa.Trim();
        content = string.IsNullOrWhiteSpace(ua)
            ? RemoveRootScalar(content, "global-ua")
            : ApplyRootScalar(content, "global-ua", ua);
        return content;
    }

    private void ApplySettingsFromConfigContent(string content)
    {
        var wasApplying = _isApplyingStoredState;
        _isApplyingStoredState = true;
        try
        {
            OutboundMode = ExtractOutboundMode(content);
            MixedPort = ExtractRootScalar(content, "mixed-port", MixedPort);
            AllowLan = ExtractRootBool(content, "allow-lan", AllowLan);
            LogLevel = NormalizeLogLevel(ExtractRootScalar(content, "log-level", LogLevel));
            EnableIpv6 = ExtractRootBool(content, "ipv6", EnableIpv6);
            UnifiedDelay = ExtractRootBool(content, "unified-delay", UnifiedDelay);
            TcpConcurrent = ExtractRootBool(content, "tcp-concurrent", TcpConcurrent);
            FindProcess = ExtractRootScalar(content, "find-process-mode", FindProcess ? "always" : "off")
                .Equals("always", StringComparison.OrdinalIgnoreCase);
            GeodataMemory = ExtractRootScalar(content, "geodata-loader", GeodataMemory ? "memconservative" : "standard")
                .Equals("memconservative", StringComparison.OrdinalIgnoreCase);
            ExternalController = !string.IsNullOrWhiteSpace(ExtractRootScalar(content, "external-controller", string.Empty));
            GlobalUa = ExtractRootScalar(content, "global-ua", GlobalUa);
        }
        finally
        {
            _isApplyingStoredState = wasApplying;
        }
    }

    private static string ExtractRootScalar(string content, string key, string fallback)
    {
        using var reader = new StringReader(content);
        string? line;
        var prefix = $"{key}:";
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length != line.Length || !trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return trimmed[prefix.Length..].Trim().Trim('"', '\'');
        }

        return fallback;
    }

    private static bool ExtractRootBool(string content, string key, bool fallback)
    {
        return ExtractRootScalar(content, key, fallback ? "true" : "false").Trim() switch
        {
            "true" or "True" or "TRUE" or "1" => true,
            "false" or "False" or "FALSE" or "0" => false,
            _ => fallback
        };
    }

    private static string ApplyRootScalar(string content, string key, string value)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var prefix = $"{key}:";
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.Length != lines[i].Length || !trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var indent = lines[i][..(lines[i].Length - trimmed.Length)];
            lines[i] = $"{indent}{key}: {value}";
            return string.Join('\n', lines);
        }

        return $"{key}: {value}\n{content}";
    }

    private static string RemoveRootScalar(string content, string key)
    {
        var prefix = $"{key}:";
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line =>
            {
                var trimmed = line.TrimStart();
                return trimmed.Length != line.Length || !trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            });

        return string.Join('\n', lines);
    }

    private static string NormalizeOutboundMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            "global" => "global",
            "direct" => "direct",
            _ => "rule"
        };
    }

    private static string NormalizeLogLevel(string? logLevel)
    {
        return logLevel?.Trim().ToLowerInvariant() switch
        {
            "debug" => "debug",
            "warning" => "warning",
            "error" => "error",
            "silent" => "silent",
            _ => "info"
        };
    }

    private static string NormalizeTestUrl(string? value)
    {
        var url = value?.Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out _) ? url! : DefaultDelayTestUrl;
    }

    private static string ToYamlBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string DefaultConfigText(int mixedPort)
    {
        return $$"""
               mixed-port: {{mixedPort}}
               allow-lan: false
               mode: rule
               log-level: info
               ipv6: false
               proxies:
                 - name: DIRECT-TEST
                   type: direct
               proxy-groups:
                 - name: Proxy
                   type: select
                   proxies:
                     - DIRECT
                     - DIRECT-TEST
               rules:
                 - MATCH,Proxy
               """;
    }
}
