using System.Globalization;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Util;
using Aureline.Models;
using Aureline.Services.Clash;

namespace Aureline.Android.Services;

internal sealed class AndroidClashRuntime : IClashRuntime, IDisposable
{
    private const string Tag = "AurelineRuntime";
    public const int RequestVpnPermission = 1000;

    private readonly Activity activity;
    private readonly AndroidCoreIpcClient ipcClient;
    private ClashProfile? pendingStartProfile;
    private bool disposed;

    public static AndroidClashRuntime? Current { get; private set; }

    public event EventHandler<ClashStatus>? StatusChanged;

    public ClashStatus Status { get; private set; } = ClashStatus.Stopped;

    public CoreCapabilities Capabilities { get; } = CoreCapabilities.AndroidLibClash;

    public string DefaultHomeDirectory { get; }

    public string DefaultConfigPath { get; }

    private AndroidClashRuntime(Activity activity)
    {
        this.activity = activity;
        ipcClient = new AndroidCoreIpcClient(activity);
        DefaultHomeDirectory = activity.FilesDir?.AbsolutePath ?? string.Empty;
        DefaultConfigPath = Path.Combine(DefaultHomeDirectory, "config.yaml");
        if (IsCoreProcessRunning())
        {
            _ = RefreshStatusAsync();
        }
    }

    public static void Install(Activity activity)
    {
        Current = new AndroidClashRuntime(activity);
        ClashRuntimeHost.Current = Current;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ipcClient.Dispose();
    }

    public async Task InitializeAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Log.Info(Tag, $"Initialize via IPC home={profile.HomeDirectory}, config={profile.ConfigPath}");
        var payload = SerializeProfile(profile);
        await ipcClient.SendAsync(AndroidIpcCommands.Initialize, payload, cancellationToken);
    }

    public async Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        Log.Info(Tag, $"Validate config via IPC path={configPath}");
        var payload = JsonSerializer.Serialize(
            new ValidateConfigIpcRequest(configPath),
            AndroidIpcJsonContext.Default.ValidateConfigIpcRequest);
        return await ipcClient.SendAsync(AndroidIpcCommands.ValidateConfig, payload, cancellationToken);
    }

    public async Task StartAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        await StartCoreAsync(profile, hasVpnPermission: false, cancellationToken);
    }

    public void OnVpnPermissionResult(bool granted)
    {
        var profile = pendingStartProfile;
        pendingStartProfile = null;

        if (!granted || profile == null)
        {
            Log.Warn(Tag, $"VPN permission denied or missing profile. granted={granted}, profile={(profile == null ? "null" : "present")}");
            Publish(new ClashStatus(ClashRunState.Stopped, "VPN 授权已取消"));
            return;
        }

        Log.Info(Tag, "VPN permission granted; continuing pending start");
        _ = StartCoreAsync(profile, hasVpnPermission: true);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Publish(new ClashStatus(ClashRunState.Stopping, "Stopping"));
        try
        {
            await ipcClient.SendAsync(AndroidIpcCommands.Stop, cancellationToken: cancellationToken);
            await RefreshStatusAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Stop failed: {ex}");
            Publish(new ClashStatus(ClashRunState.Error, ex.Message));
        }
    }

    public async Task<IReadOnlyList<ClashProxyGroup>> GetProxyGroupsAsync(
        string sortMode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(
                new ProxyGroupsIpcRequest(sortMode),
                AndroidIpcJsonContext.Default.ProxyGroupsIpcRequest);
            var response = await ipcClient.SendAsync(AndroidIpcCommands.GetProxyGroups, payload, cancellationToken);
            return JsonSerializer.Deserialize(
                    response,
                    AndroidIpcJsonContext.Default.ClashProxyGroupArray) ??
                [];
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to query proxy groups: {ex.Message}");
            return [];
        }
    }

    public async Task<ClashTraffic?> GetTrafficAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ipcClient.SendAsync(AndroidIpcCommands.GetTraffic, cancellationToken: cancellationToken);
            return JsonSerializer.Deserialize(response, AndroidIpcJsonContext.Default.ClashTraffic);
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to query traffic: {ex.Message}");
            return null;
        }
    }

    public async Task<int?> GetConnectionCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ipcClient.SendAsync(AndroidIpcCommands.GetConnectionCount, cancellationToken: cancellationToken);
            return int.TryParse(response, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                ? count
                : null;
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to query connection count: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SelectProxyAsync(
        string groupName,
        string proxyName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(
                new SelectProxyIpcRequest(groupName, proxyName),
                AndroidIpcJsonContext.Default.SelectProxyIpcRequest);
            var response = await ipcClient.SendAsync(AndroidIpcCommands.SelectProxy, payload, cancellationToken);
            return response == "1";
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to select proxy: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SetModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(
                new SetModeIpcRequest(mode),
                AndroidIpcJsonContext.Default.SetModeIpcRequest);
            var response = await ipcClient.SendAsync(AndroidIpcCommands.SetMode, payload, cancellationToken);
            return response == "1";
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to set mode: {ex.Message}");
            return false;
        }
    }

    public async Task<int?> TestProxyDelayAsync(
        string proxyName,
        string testUrl,
        int timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(
                new ProxyDelayIpcRequest(proxyName, testUrl, timeoutMilliseconds),
                AndroidIpcJsonContext.Default.ProxyDelayIpcRequest);
            var response = await ipcClient.SendAsync(AndroidIpcCommands.TestProxyDelay, payload, cancellationToken);
            return int.TryParse(response, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay)
                ? delay
                : null;
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to test proxy delay: {ex.Message}");
            return null;
        }
    }

    public async Task HealthCheckAsync(string groupName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                await ipcClient.SendAsync(AndroidIpcCommands.HealthCheckAll, cancellationToken: cancellationToken);
                return;
            }

            var payload = JsonSerializer.Serialize(
                new HealthCheckIpcRequest(groupName),
                AndroidIpcJsonContext.Default.HealthCheckIpcRequest);
            await ipcClient.SendAsync(AndroidIpcCommands.HealthCheck, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to run health check: {ex.Message}");
        }
    }

    public async Task CloseAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ipcClient.SendAsync(AndroidIpcCommands.CloseAllConnections, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to close connections: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<ClashInstalledApplication>> GetInstalledApplicationsAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageManager = activity.PackageManager;
            if (packageManager == null)
            {
                return (IReadOnlyList<ClashInstalledApplication>)Array.Empty<ClashInstalledApplication>();
            }

#pragma warning disable CA1422
#pragma warning disable CS0618
            var packages = packageManager.GetInstalledPackages(PackageInfoFlags.MetaData | PackageInfoFlags.Permissions);
#pragma warning restore CS0618
#pragma warning restore CA1422
            var selfPackage = activity.PackageName ?? string.Empty;

            return packages
                .Where(package => !string.Equals(package.PackageName, selfPackage, StringComparison.Ordinal) &&
                    !string.Equals(package.PackageName, "android", StringComparison.Ordinal))
                .Select(package =>
                {
                    var applicationInfo = package.ApplicationInfo;
                    var packageName = package.PackageName ?? string.Empty;
                    var label = applicationInfo?.LoadLabel(packageManager)?.ToString() ?? packageName;
                    var isSystem = applicationInfo != null &&
                        (applicationInfo.Flags & ApplicationInfoFlags.System) == ApplicationInfoFlags.System;
                    var usesInternet = package.RequestedPermissions?.Contains(global::Android.Manifest.Permission.Internet) == true;
                    return new ClashInstalledApplication(
                        packageName,
                        label,
                        isSystem,
                        usesInternet,
                        package.LastUpdateTime);
                })
                .Where(application => !string.IsNullOrWhiteSpace(application.PackageName))
                .ToArray();
        }, cancellationToken);
    }

    private async Task StartCoreAsync(
        ClashProfile profile,
        bool hasVpnPermission,
        CancellationToken cancellationToken = default)
    {
        Publish(new ClashStatus(ClashRunState.Starting, "Starting core"));
        Log.Info(Tag, $"Start requested via IPC. tun={profile.EnableTun}, hasVpnPermission={hasVpnPermission}");

        try
        {
            if (profile.EnableTun && !hasVpnPermission)
            {
                var permissionIntent = VpnService.Prepare(activity);
                Log.Info(Tag, permissionIntent == null
                    ? "VPN permission already granted"
                    : "VPN permission required");

                if (permissionIntent != null)
                {
                    pendingStartProfile = profile;
                    activity.StartActivityForResult(permissionIntent, RequestVpnPermission);
                    Publish(new ClashStatus(ClashRunState.Starting, "等待 VPN 授权"));
                    return;
                }
            }

            var payload = SerializeProfile(profile);
            await ipcClient.SendAsync(AndroidIpcCommands.Start, payload, cancellationToken);
            await RefreshStatusAsync(cancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            Log.Warn(Tag, "Start canceled");
            Publish(ClashStatus.Stopped);
        }
        catch (Exception ex)
        {
            Log.Error(Tag, ex.ToString());
            Publish(new ClashStatus(ClashRunState.Error, ex.Message));
        }
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ipcClient.SendAsync(AndroidIpcCommands.GetStatus, cancellationToken: cancellationToken);
            var nextStatus = JsonSerializer.Deserialize(response, AndroidIpcJsonContext.Default.ClashStatus);
            if (nextStatus != null)
            {
                Publish(nextStatus);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to refresh core status: {ex.Message}");
        }
    }

    private void Publish(ClashStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    private static string SerializeProfile(ClashProfile profile)
    {
        return JsonSerializer.Serialize(
            AndroidCoreProfile.FromProfile(profile),
            AndroidIpcJsonContext.Default.AndroidCoreProfile);
    }

    private bool IsCoreProcessRunning()
    {
        try
        {
            var manager = activity.GetSystemService(Context.ActivityService) as ActivityManager;
            var processName = $"{activity.PackageName}:vpn";
            return manager?.RunningAppProcesses?.Any(process =>
                    string.Equals(process.ProcessName, processName, StringComparison.Ordinal)) == true;
        }
        catch (Exception ex)
        {
            Log.Debug(Tag, $"Failed to inspect running app processes: {ex.Message}");
            return false;
        }
    }
}
