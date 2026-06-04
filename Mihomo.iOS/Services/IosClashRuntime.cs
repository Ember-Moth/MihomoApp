using System.Text;
using System.Text.Json;
using Foundation;
using NetworkExtension;
using Mihomo.Models;
using Mihomo.Services.Clash;

namespace Mihomo.iOS.Services;

internal sealed class IosClashRuntime : IClashRuntime
{
    private const string ExternalControllerListenAt = "127.0.0.1:9090";
    private const string ExternalControllerBaseUrl = "http://127.0.0.1:9090";
    private const string PacketTunnelBundleIdentifier = "com.embermoth.mihomo.PacketTunnel";
    private const string AppGroupIdentifier = "group.com.embermoth.mihomo";

    private readonly HttpClient controllerClient = new() { BaseAddress = new Uri(ExternalControllerBaseUrl) };
    private readonly object trafficLock = new();
    private DateTimeOffset? lastTrafficAt;
    private long lastUploadTotal;
    private long lastDownloadTotal;

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

    public async Task<IReadOnlyList<ClashProxyGroup>> GetProxyGroupsAsync(
        string sortMode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await controllerClient.GetStringAsync("/proxies", cancellationToken);
            var response = JsonSerializer.Deserialize(
                json,
                IosClashJsonContext.Default.IosControllerProxiesResponse);
            var proxyMap = response?.Proxies;
            if (proxyMap is not { Count: > 0 })
            {
                return [];
            }

            var groups = new List<ClashProxyGroup>();
            foreach (var (name, group) in proxyMap)
            {
                if (group.All is not { Count: > 0 } all)
                {
                    continue;
                }

                var proxies = all
                    .Where(proxyName => !string.IsNullOrWhiteSpace(proxyName))
                    .Select(proxyName =>
                    {
                        proxyMap.TryGetValue(proxyName, out var proxy);
                        return new ClashProxy(
                            proxyName,
                            proxy?.Type ?? string.Empty,
                            proxy?.Now ?? string.Empty,
                            LatestDelay(proxy));
                    })
                    .ToArray();

                groups.Add(new ClashProxyGroup(
                    name,
                    group.Type ?? string.Empty,
                    group.Now ?? string.Empty,
                    string.Empty,
                    proxies));
            }

            return groups;
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
            var snapshot = await GetConnectionsSnapshotAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;

            lock (trafficLock)
            {
                var elapsed = lastTrafficAt == null ? 0 : Math.Max(0.001, (now - lastTrafficAt.Value).TotalSeconds);
                var uploadRate = lastTrafficAt == null
                    ? 0
                    : (long)Math.Max(0, (snapshot.UploadTotal - lastUploadTotal) / elapsed);
                var downloadRate = lastTrafficAt == null
                    ? 0
                    : (long)Math.Max(0, (snapshot.DownloadTotal - lastDownloadTotal) / elapsed);

                lastTrafficAt = now;
                lastUploadTotal = snapshot.UploadTotal;
                lastDownloadTotal = snapshot.DownloadTotal;

                return new ClashTraffic(
                    uploadRate,
                    downloadRate,
                    snapshot.UploadTotal,
                    snapshot.DownloadTotal);
            }
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
            var snapshot = await GetConnectionsSnapshotAsync(cancellationToken);
            return snapshot.Connections?.Count;
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
            using var request = CreateJsonRequest(
                HttpMethod.Put,
                $"/proxies/{Uri.EscapeDataString(groupName)}",
                JsonSerializer.Serialize(
                    new IosControllerNameRequest(proxyName),
                    IosClashJsonContext.Default.IosControllerNameRequest));
            using var response = await controllerClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
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
            using var request = CreateJsonRequest(
                HttpMethod.Patch,
                "/configs",
                JsonSerializer.Serialize(
                    new IosControllerModeRequest(mode),
                    IosClashJsonContext.Default.IosControllerModeRequest));
            using var response = await controllerClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
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
            var path = $"/proxies/{Uri.EscapeDataString(proxyName)}/delay" +
                $"?timeout={timeoutMilliseconds}&url={Uri.EscapeDataString(testUrl)}";
            var json = await controllerClient.GetStringAsync(path, cancellationToken);
            var response = JsonSerializer.Deserialize(
                json,
                IosClashJsonContext.Default.IosControllerDelayResponse);
            return response?.Delay > 0 ? response.Delay : null;
        }
        catch
        {
            return null;
        }
    }

    public Task HealthCheckAsync(string groupName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task CloseAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/connections");
            using var response = await controllerClient.SendAsync(request, cancellationToken);
        }
        catch
        {
            // Closing stale connections is best-effort after switching nodes.
        }
    }

    private async Task<IosControllerConnectionsResponse> GetConnectionsSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var json = await controllerClient.GetStringAsync("/connections", cancellationToken);
        return JsonSerializer.Deserialize(
            json,
            IosClashJsonContext.Default.IosControllerConnectionsResponse) ?? new IosControllerConnectionsResponse();
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, string json)
    {
        return new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static int? LatestDelay(IosControllerProxy? proxy)
    {
        var delay = proxy?.History?
            .LastOrDefault(history => history.Delay > 0)?
            .Delay;
        return delay > 0 ? delay : null;
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
