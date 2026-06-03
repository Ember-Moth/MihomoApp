using Foundation;
using NetworkExtension;
using Mihomo.Models;
using Mihomo.Services.Clash;

namespace Mihomo.iOS.Services;

internal sealed class IosClashRuntime : IClashRuntime
{
    private const string ExternalControllerListenAt = "127.0.0.1:9090";
    private const string PacketTunnelBundleIdentifier = "com.embermoth.mihomo.PacketTunnel";
    private const string AppGroupIdentifier = "group.com.embermoth.mihomo";

    public event EventHandler<ClashStatus>? StatusChanged;

    public ClashStatus Status { get; private set; } = ClashStatus.Stopped;

    public string DefaultHomeDirectory { get; }

    public string DefaultConfigPath { get; }

    private IosClashRuntime()
    {
        DefaultHomeDirectory = DefaultHomeDirectoryPath();
        DefaultConfigPath = Path.Combine(DefaultHomeDirectory, "config.yaml");
    }

    public static void Install()
    {
        ClashRuntimeHost.Current = new IosClashRuntime();
    }

    public Task InitializeAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(profile.HomeDirectory);
        EnsureConfigFile(profile);
        return Task.CompletedTask;
    }

    public Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            return Task.FromResult($"Config file not found: {configPath}");
        }

        return Task.FromResult(string.Empty);
    }

    public async Task StartAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Publish(new ClashStatus(ClashRunState.Starting, "Starting core"));
        EnsureConfigFile(profile);

        try
        {
            var manager = await LoadOrCreateManagerAsync();
            ConfigureManager(manager, profile);
            await SaveManagerAsync(manager);
            await LoadManagerAsync(manager);

            if (manager.Connection is not NETunnelProviderSession session)
            {
                Publish(new ClashStatus(
                    ClashRunState.Error,
                    "Configured VPN connection is not a NETunnelProviderSession"));
                return;
            }

            if (!session.StartTunnel(null, out var error))
            {
                Publish(new ClashStatus(
                    ClashRunState.Error,
                    error?.LocalizedDescription ?? "Failed to start iOS packet tunnel"));
                return;
            }

            Publish(new ClashStatus(ClashRunState.Running, "Running", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
        catch (Exception ex)
        {
            Publish(new ClashStatus(ClashRunState.Error, ex.Message));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Publish(new ClashStatus(ClashRunState.Stopping, "Stopping"));

        try
        {
            var manager = await LoadOrCreateManagerAsync();
            if (manager.Connection is NETunnelProviderSession session)
            {
                session.StopTunnel();
            }
        }
        catch
        {
            // The tunnel may not have been configured yet.
        }

        Publish(ClashStatus.Stopped);
    }

    public Task<IReadOnlyList<ClashProxyGroup>> GetProxyGroupsAsync(
        string sortMode,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ClashProxyGroup>>([]);
    }

    public Task<ClashTraffic?> GetTrafficAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ClashTraffic?>(null);
    }

    public Task<int?> GetConnectionCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<int?>(null);
    }

    public Task<bool> SelectProxyAsync(
        string groupName,
        string proxyName,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<int?> TestProxyDelayAsync(
        string proxyName,
        string testUrl,
        int timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<int?>(null);
    }

    public Task HealthCheckAsync(string groupName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private static async Task<NETunnelProviderManager> LoadOrCreateManagerAsync()
    {
        var managers = await LoadAllManagersAsync();
        return managers.FirstOrDefault(manager =>
        {
            return manager.ProtocolConfiguration is NETunnelProviderProtocol protocol &&
                string.Equals(protocol.ProviderBundleIdentifier, PacketTunnelBundleIdentifier, StringComparison.Ordinal);
        }) ?? new NETunnelProviderManager();
    }

    private static Task<IReadOnlyList<NETunnelProviderManager>> LoadAllManagersAsync()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<NETunnelProviderManager>>();
        NETunnelProviderManager.LoadAllFromPreferences((managers, error) =>
        {
            if (error != null)
            {
                completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
                return;
            }

            completion.TrySetResult(managers ?? []);
        });

        return completion.Task;
    }

    private static Task SaveManagerAsync(NETunnelProviderManager manager)
    {
        var completion = new TaskCompletionSource();
        manager.SaveToPreferences(error =>
        {
            if (error != null)
            {
                completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
                return;
            }

            completion.TrySetResult();
        });

        return completion.Task;
    }

    private static Task LoadManagerAsync(NETunnelProviderManager manager)
    {
        var completion = new TaskCompletionSource();
        manager.LoadFromPreferences(error =>
        {
            if (error != null)
            {
                completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
                return;
            }

            completion.TrySetResult();
        });

        return completion.Task;
    }

    private static void ConfigureManager(NETunnelProviderManager manager, ClashProfile profile)
    {
        var providerConfiguration = new NSDictionary<NSString, NSObject>(
            [
                new NSString("home-dir"),
                new NSString("config-path"),
                new NSString("mixed-port"),
                new NSString("stack"),
                new NSString("route-address-csv"),
                new NSString("enable-ipv6"),
                new NSString("dns-hijacking")
            ],
            [
                new NSString(profile.HomeDirectory),
                new NSString(profile.ConfigPath),
                NSNumber.FromInt32(profile.MixedPort),
                new NSString(profile.Stack),
                new NSString(profile.RouteAddressCsv),
                NSNumber.FromBoolean(profile.EnableIpv6),
                NSNumber.FromBoolean(profile.DnsHijacking)
            ]);

        manager.LocalizedDescription = "Mihomo";
        manager.ProtocolConfiguration = new NETunnelProviderProtocol
        {
            ProviderBundleIdentifier = PacketTunnelBundleIdentifier,
            ServerAddress = "Mihomo",
            ProviderConfiguration = providerConfiguration
        };
        manager.Enabled = true;
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

        Directory.CreateDirectory(profile.HomeDirectory);
        File.WriteAllText(targetConfigPath, DefaultConfig(profile.MixedPort));
    }

    private static string DefaultHomeDirectoryPath()
    {
        using var containerUrl = NSFileManager.DefaultManager.GetContainerUrl(AppGroupIdentifier);
        var path = containerUrl?.Path;
        return string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Path.GetTempPath(), "Mihomo")
            : path;
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

    private void Publish(ClashStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
