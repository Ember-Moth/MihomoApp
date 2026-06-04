using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Util;
using Mihomo.Android.Interop;
using Mihomo.Android.Vpn;
using Mihomo.Models;
using Mihomo.Services.Clash;

namespace Mihomo.Android.Services;

internal sealed class AndroidClashRuntime : IClashRuntime
{
    private const string ExternalControllerListenAt = "127.0.0.1:9090";
    private const string Tag = "MihomoRuntime";
    public const int RequestVpnPermission = 1000;

    private readonly Activity activity;
    private ClashProfile? pendingStartProfile;

    public static AndroidClashRuntime? Current { get; private set; }

    public event EventHandler<ClashStatus>? StatusChanged;

    public ClashStatus Status { get; private set; } = ClashStatus.Stopped;

    public string DefaultHomeDirectory { get; }

    public string DefaultConfigPath { get; }

    private AndroidClashRuntime(Activity activity)
    {
        this.activity = activity;
        DefaultHomeDirectory = activity.FilesDir?.AbsolutePath ?? string.Empty;
        DefaultConfigPath = Path.Combine(DefaultHomeDirectory, "config.yaml");
    }

    public static void Install(Activity activity)
    {
        Current = new AndroidClashRuntime(activity);
        ClashRuntimeHost.Current = Current;
    }

    public async Task InitializeAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Log.Info(Tag, $"Initialize home={profile.HomeDirectory}, config={profile.ConfigPath}, sdk={(int)Build.VERSION.SdkInt}");
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(profile.HomeDirectory);
            EnsureConfigFile(profile);
            LibClashNative.Init(profile.HomeDirectory, (int)Build.VERSION.SdkInt);
        }, cancellationToken);
        Log.Info(Tag, "Initialize completed");
    }

    public async Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        Log.Info(Tag, $"Validate config={configPath}");
        if (!File.Exists(configPath))
        {
            Log.Error(Tag, $"Config file not found: {configPath}");
            return $"Config file not found: {configPath}";
        }

        var message = await Task.Run(() => LibClashNative.ValidateConfig(configPath), cancellationToken);
        if (string.IsNullOrWhiteSpace(message))
        {
            Log.Info(Tag, "Validate completed: ok");
        }
        else
        {
            Log.Error(Tag, $"Validate completed: {message}");
        }

        return message;
    }

    public async Task StartAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        await StartCoreAsync(profile, hasVpnPermission: false, cancellationToken);
    }

    private async Task StartCoreAsync(
        ClashProfile profile,
        bool hasVpnPermission,
        CancellationToken cancellationToken = default)
    {
        Publish(new ClashStatus(ClashRunState.Starting, "Starting core"));
        Log.Info(Tag, $"Start requested. tun={profile.EnableTun}, hasVpnPermission={hasVpnPermission}");

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

            Log.Info(Tag, "Setting up libclash core");
            var setupMessage = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureConfigFile(profile);
                return SetupCore(profile);
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(setupMessage))
            {
                Log.Error(Tag, $"Core setup failed: {setupMessage}");
                Publish(new ClashStatus(ClashRunState.Error, setupMessage));
                return;
            }

            Log.Info(Tag, "Starting libclash listener");
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                LibClashNative.StartListener();
            }, cancellationToken);

            if (profile.EnableTun)
            {
                Log.Info(Tag, "Starting Android VPN service");
                ClashVpnService.Start(activity, ClashVpnOptions.FromProfile(profile));
            }

            Log.Info(Tag, "Core marked running");
            Publish(new ClashStatus(ClashRunState.Running, "Running", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
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

        TryStopVpn();
        await Task.Run(() =>
        {
            TryStopListener();
            TryResetCore();
        }, cancellationToken);
        Publish(ClashStatus.Stopped);
    }

    public async Task<IReadOnlyList<ClashProxyGroup>> GetProxyGroupsAsync(
        string sortMode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var namesJson = LibClashNative.QueryGroupNames(false);
                var names = JsonSerializer.Deserialize(
                    namesJson,
                    LibClashSetupJsonContext.Default.ListString) ?? [];
                var groups = new List<ClashProxyGroup>(names.Count);
                var nativeSortMode = ToNativeSortMode(sortMode);

                foreach (var name in names)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var groupJson = LibClashNative.QueryGroup(name, nativeSortMode);
                    if (string.IsNullOrWhiteSpace(groupJson))
                    {
                        continue;
                    }

                    var group = JsonSerializer.Deserialize(
                        groupJson,
                        LibClashSetupJsonContext.Default.NativeProxyGroup);
                    if (group?.Proxies is not { Count: > 0 } proxies)
                    {
                        continue;
                    }

                    var nodes = proxies
                        .Where(proxy => !string.IsNullOrWhiteSpace(proxy.Name))
                        .Select(proxy => new ClashProxy(
                            proxy.Name ?? string.Empty,
                            proxy.Type ?? proxy.Subtitle ?? string.Empty,
                            string.Empty,
                            proxy.Delay > 0 && proxy.Delay < ushort.MaxValue ? proxy.Delay : null))
                        .ToArray();

                    groups.Add(new ClashProxyGroup(
                        name,
                        group.Type ?? string.Empty,
                        group.Now ?? string.Empty,
                        string.Empty,
                        nodes));
                }

                return (IReadOnlyList<ClashProxyGroup>)groups;
            }, cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    public async Task<ClashTraffic?> GetTrafficAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = UnpackTraffic(LibClashNative.QueryTrafficNow());
                var total = UnpackTraffic(LibClashNative.QueryTrafficTotal());
                return new ClashTraffic(now.Upload, now.Download, total.Upload, total.Download);
            }, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<int?> GetConnectionCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return (int?)LibClashNative.QueryConnectionCount();
            }, cancellationToken);
        }
        catch
        {
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
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return LibClashNative.PatchSelector(groupName, proxyName);
            }, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return LibClashNative.SetMode(mode);
            }, cancellationToken);
        }
        catch
        {
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
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return LibClashNative.TestProxyDelay(proxyName, testUrl, timeoutMilliseconds);
            }, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task HealthCheckAsync(string groupName, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(groupName))
                {
                    LibClashNative.HealthCheckAll();
                    return;
                }

                LibClashNative.HealthCheck(groupName);
            }, cancellationToken);
        }
        catch
        {
            // Health checks are best-effort UI refresh hints.
        }
    }

    public async Task CloseAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                LibClashNative.CloseAllConnections();
            }, cancellationToken);
        }
        catch
        {
            // Closing stale connections is best-effort after switching nodes.
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

    private static string SetupCore(ClashProfile profile)
    {
        var setupJson = JsonSerializer.Serialize(
            new LibClashSetupRequest(
                profile.HomeDirectory,
                profile.ConfigPath,
                profile.ExternalController ? ExternalControllerListenAt : string.Empty,
                profile.MixedPort,
                "https://www.gstatic.com/generate_204"),
            LibClashSetupJsonContext.Default.LibClashSetupRequest);
        var setupMessage = LibClashNative.SetupConfig(setupJson);
        if (!string.IsNullOrWhiteSpace(setupMessage))
        {
            return setupMessage;
        }

        return string.Empty;
    }

    private void TryStopVpn()
    {
        try
        {
            ClashVpnService.Stop(activity);
        }
        catch
        {
            // The service may not be running, or native code may still be absent.
        }
    }

    private static void TryStopListener()
    {
        try
        {
            LibClashNative.StopListener();
        }
        catch
        {
            // Native library may not be present during early app development.
        }
    }

    private static void TryResetCore()
    {
        try
        {
            LibClashNative.Reset();
        }
        catch
        {
            // Native library may not be present during early app development.
        }
    }

    private void EnsureConfigFile(ClashProfile profile)
    {
        var targetConfigPath = Path.Combine(profile.HomeDirectory, "config.yaml");
        if (!string.Equals(profile.ConfigPath, targetConfigPath, StringComparison.Ordinal) && File.Exists(profile.ConfigPath))
        {
            File.Copy(profile.ConfigPath, targetConfigPath, true);
            return;
        }

        if (File.Exists(targetConfigPath))
        {
            return;
        }

        File.WriteAllText(targetConfigPath, DefaultConfig(profile.MixedPort));
    }

    private static string DefaultConfig(int mixedPort)
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

    private static string ToNativeSortMode(string sortMode)
    {
        return sortMode switch
        {
            "按延迟" => "Delay",
            "按名称" => "Title",
            _ => "Default"
        };
    }

    private static (long Upload, long Download) UnpackTraffic(long packed)
    {
        var upload = DecodeTrafficUnit((uint)((ulong)packed >> 32));
        var download = DecodeTrafficUnit((uint)((ulong)packed & uint.MaxValue));
        return (upload, download);
    }

    private static long DecodeTrafficUnit(uint value)
    {
        var unit = value >> 30;
        var amount = value & 0x3fffffff;
        return unit switch
        {
            1 => amount * 1024L / 100L,
            2 => amount * 1024L * 1024L / 100L,
            3 => amount * 1024L * 1024L * 1024L / 100L,
            _ => amount
        };
    }

    private void Publish(ClashStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
