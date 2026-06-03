using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Mihomo.Android.Interop;
using Mihomo.Android.Vpn;
using Mihomo.Models;
using Mihomo.Services.Clash;

namespace Mihomo.Android.Services;

internal sealed class AndroidClashRuntime : IClashRuntime
{
    private const string ExternalControllerListenAt = "127.0.0.1:9090";

    private readonly Activity activity;

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
        ClashRuntimeHost.Current = new AndroidClashRuntime(activity);
    }

    public async Task InitializeAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(profile.HomeDirectory);
            EnsureConfigFile(profile);
            LibClashNative.Init(profile.HomeDirectory, (int)Build.VERSION.SdkInt);
        }, cancellationToken);
    }

    public async Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            return $"Config file not found: {configPath}";
        }

        return await Task.Run(() => LibClashNative.ValidateConfig(configPath), cancellationToken);
    }

    public async Task StartAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Publish(new ClashStatus(ClashRunState.Starting, "Starting core"));

        try
        {
            if (profile.EnableTun)
            {
                var permissionIntent = VpnService.Prepare(activity);
                if (permissionIntent != null)
                {
                    activity.StartActivityForResult(permissionIntent, 1000);
                    Publish(new ClashStatus(ClashRunState.Stopped, "VPN permission requested. Start again after approval."));
                    return;
                }
            }

            var setupMessage = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureConfigFile(profile);
                return SetupCore(profile);
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(setupMessage))
            {
                Publish(new ClashStatus(ClashRunState.Error, setupMessage));
                return;
            }

            if (profile.EnableTun)
            {
                ClashVpnService.Start(activity, ClashVpnOptions.FromProfile(profile));
            }

            Publish(new ClashStatus(ClashRunState.Running, "Running", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
        catch (System.OperationCanceledException)
        {
            Publish(ClashStatus.Stopped);
        }
        catch (Exception ex)
        {
            Publish(new ClashStatus(ClashRunState.Error, ex.Message));
        }
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

    private static string SetupCore(ClashProfile profile)
    {
        var setupJson = JsonSerializer.Serialize(
            new LibClashSetupRequest(
                profile.HomeDirectory,
                profile.ConfigPath,
                ExternalControllerListenAt,
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
