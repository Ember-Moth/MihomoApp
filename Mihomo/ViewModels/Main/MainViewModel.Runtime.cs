using CommunityToolkit.Mvvm.Input;
using Mihomo.Models;

namespace Mihomo.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ToggleRunningAsync()
    {
        if (IsBusy || IsStarting)
        {
            return;
        }

        if (IsRunning)
        {
            await StopAsync();
            return;
        }

        await StartAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        await RunAsync(async () =>
        {
            SaveConfigContent();
            await StartRuntimeCoreAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await RunAsync(async () =>
        {
            await _runtime.StopAsync();
            ProxyGroups.Clear();
            VisibleProxyNodes.Clear();
            SelectedGroup = null;
            UploadSpeed = "0 B/s";
            DownloadSpeed = "0 B/s";
            ConnectionCount = "0";
            TotalTraffic = "0 B";
            TotalUpload = "0 B";
            TotalDownload = "0 B";
            ResetSpeedSamples();
            OnPropertyChanged(nameof(ProxySummary));
            OnPropertyChanged(nameof(VisibleProxySummary));
            OnPropertyChanged(nameof(HasProxyGroups));
            OnPropertyChanged(nameof(HasVisibleProxyNodes));
            OnPropertyChanged(nameof(MissingSelectedGroup));
            OnPropertyChanged(nameof(MissingVisibleProxyNodes));
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
            RouteAddressCsv.Trim(),
            AccessControlEnabled,
            NormalizeAccessControlMode(AccessControlMode),
            AccessPackageNames,
            ExternalController);
    }

    private async Task StartRuntimeCoreAsync()
    {
        StateText = "Starting";
        LastMessage = "正在启动核心";

        try
        {
            var profile = BuildProfile();
            await _runtime.InitializeAsync(profile);

            var validationMessage = await _runtime.ValidateConfigAsync(profile.ConfigPath);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                StateText = "Error";
                IsRunning = false;
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
            else if (StateText == "Stopped")
            {
                LastMessage = string.IsNullOrWhiteSpace(LastMessage) ? "核心未启动" : LastMessage;
            }
        }
        catch (Exception ex)
        {
            StateText = "Error";
            IsRunning = false;
            LastMessage = ex.Message;
        }
    }

    private async Task RefreshRuntimeUiAfterStartAsync()
    {
        await Task.Delay(700);
        if (!IsRunning)
        {
            return;
        }

        try
        {
            await RefreshProxiesCoreAsync();
            await RefreshTelemetryAsync();
        }
        catch
        {
            // Startup UI refresh is best effort; the timer keeps telemetry current.
        }
    }
}
