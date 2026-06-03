using CommunityToolkit.Mvvm.Input;
using Mihomo.Models;

namespace Mihomo.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        await RunAsync(async () =>
        {
            SaveConfigContent();

            var profile = BuildProfile();
            await _runtime.InitializeAsync(profile);

            var validationMessage = await _runtime.ValidateConfigAsync(profile.ConfigPath);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                LastMessage = validationMessage;
                return;
            }

            await _runtime.StartAsync(profile);

            if (IsRunning)
            {
                await Task.Delay(500);
                await RefreshProxiesCoreAsync();
                await RefreshTelemetryAsync();
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await RunAsync(async () =>
        {
            await _runtime.StopAsync();
            ProxyGroups.Clear();
            SelectedGroup = null;
            UploadSpeed = "0 B/s";
            DownloadSpeed = "0 B/s";
            ConnectionCount = "0";
            TotalTraffic = "0 B";
            OnPropertyChanged(nameof(ProxySummary));
        });
    }

    private ClashProfile BuildProfile()
    {
        var port = CurrentMixedPort();
        return new ClashProfile(
            HomeDirectory.Trim(),
            ConfigPath.Trim(),
            port,
            EnableTun,
            EnableIpv6,
            DnsHijacking,
            SystemProxy,
            Stack.Trim(),
            RouteAddressCsv.Trim());
    }
}
