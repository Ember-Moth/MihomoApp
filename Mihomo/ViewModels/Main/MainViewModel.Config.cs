using CommunityToolkit.Mvvm.Input;

namespace Mihomo.ViewModels;

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
        });
    }

    private async Task ImportSubscriptionCoreAsync(string url, string label)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("订阅地址为空");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("clash");
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        ConfigContent = await response.Content.ReadAsStringAsync();
        SaveConfigContent();
        await UpsertCurrentProfileAsync(ConfigProfileType.Url, label, url);
        LastMessage = "订阅已导入并保存";
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
                return;
            }

            ConfigContent = DefaultConfigText(CurrentMixedPort());
        }
        catch (Exception ex)
        {
            ConfigContent = DefaultConfigText(CurrentMixedPort());
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

        File.WriteAllText(path, ConfigContent);
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
