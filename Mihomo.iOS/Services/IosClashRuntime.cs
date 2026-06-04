using System.Text;
using System.Text.Json;
using Foundation;
using NetworkExtension;
using Mihomo.Models;
using Mihomo.Services.Clash;

namespace Mihomo.iOS.Services;

internal sealed class IosClashRuntime : IClashRuntime
{
    private const string PacketTunnelBundleIdentifier = "com.embermoth.mihomo.PacketTunnel";
    private const string AppGroupIdentifier = "group.com.embermoth.mihomo";
    private static readonly TimeSpan StartReadyTimeout = TimeSpan.FromSeconds(10);

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

    public async Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            return $"Config file not found: {configPath}";
        }

        if (Status.State != ClashRunState.Running)
        {
            return string.Empty;
        }

        try
        {
            var response = await SendIpcAsync(
                new TunnelIpcRequest
                {
                    Action = "validate-config",
                    ConfigPath = configPath
                },
                cancellationToken);
            return response.Ok ? response.Payload : response.Error;
        }
        catch
        {
            return string.Empty;
        }
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

            var ready = await WaitForReadyAsync(session, cancellationToken);
            if (!ready.Ok || !ready.BoolValue)
            {
                Publish(new ClashStatus(
                    ClashRunState.Error,
                    string.IsNullOrWhiteSpace(ready.Error) ? "Packet tunnel core did not become ready" : ready.Error));
                return;
            }

            Publish(new ClashStatus(ClashRunState.Running, "Running", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
        catch (OperationCanceledException)
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
            var namesResponse = await SendIpcAsync(
                new TunnelIpcRequest { Action = "query-group-names" },
                cancellationToken);
            if (!namesResponse.Ok || string.IsNullOrWhiteSpace(namesResponse.Payload))
            {
                return [];
            }

            var names = JsonSerializer.Deserialize(
                namesResponse.Payload,
                IosClashJsonContext.Default.ListString) ?? [];
            var nativeSortMode = ToNativeSortMode(sortMode);
            var groups = new List<ClashProxyGroup>(names.Count);

            foreach (var name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var groupResponse = await SendIpcAsync(
                    new TunnelIpcRequest
                    {
                        Action = "query-group",
                        GroupName = name,
                        SortMode = nativeSortMode
                    },
                    cancellationToken);
                if (!groupResponse.Ok || string.IsNullOrWhiteSpace(groupResponse.Payload))
                {
                    continue;
                }

                var group = JsonSerializer.Deserialize(
                    groupResponse.Payload,
                    IosClashJsonContext.Default.IosNativeProxyGroup);
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
            var response = await SendIpcAsync(
                new TunnelIpcRequest { Action = "traffic" },
                cancellationToken);
            if (!response.Ok)
            {
                return null;
            }

            var now = UnpackTraffic(response.LongValue);
            var total = UnpackTraffic(response.SecondLongValue);
            return new ClashTraffic(now.Upload, now.Download, total.Upload, total.Download);
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
            var response = await SendIpcAsync(
                new TunnelIpcRequest { Action = "connection-count" },
                cancellationToken);
            return response.Ok ? response.IntValue : null;
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
            var response = await SendIpcAsync(
                new TunnelIpcRequest
                {
                    Action = "select-proxy",
                    GroupName = groupName,
                    ProxyName = proxyName
                },
                cancellationToken);
            return response.Ok && response.BoolValue;
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
            var response = await SendIpcAsync(
                new TunnelIpcRequest
                {
                    Action = "set-mode",
                    Mode = mode
                },
                cancellationToken);
            return response.Ok && response.BoolValue;
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
            var response = await SendIpcAsync(
                new TunnelIpcRequest
                {
                    Action = "test-proxy-delay",
                    ProxyName = proxyName,
                    TestUrl = testUrl,
                    TimeoutMilliseconds = timeoutMilliseconds
                },
                cancellationToken);
            return response.Ok && response.IntValue > 0 ? response.IntValue : null;
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
            _ = await SendIpcAsync(
                new TunnelIpcRequest
                {
                    Action = "health-check",
                    GroupName = groupName
                },
                cancellationToken);
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
            _ = await SendIpcAsync(
                new TunnelIpcRequest { Action = "close-connections" },
                cancellationToken);
        }
        catch
        {
            // Closing stale connections is best-effort after switching nodes.
        }
    }

    private static async Task<TunnelIpcResponse> WaitForReadyAsync(
        NETunnelProviderSession session,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var deadline = DateTimeOffset.UtcNow + StartReadyTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await SendIpcAsync(
                    session,
                    new TunnelIpcRequest { Action = "status" },
                    cancellationToken);
                if (response.Ok && response.BoolValue)
                {
                    return response;
                }

                if (!response.Ok)
                {
                    lastError = new InvalidOperationException(response.Error);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(500, cancellationToken);
        }

        return new TunnelIpcResponse
        {
            Ok = false,
            Error = lastError?.Message ?? "packet tunnel did not reply before timeout"
        };
    }

    private static async Task<TunnelIpcResponse> SendIpcAsync(
        TunnelIpcRequest request,
        CancellationToken cancellationToken)
    {
        var session = await LoadTunnelSessionAsync();
        return await SendIpcAsync(session, request, cancellationToken);
    }

    private static async Task<TunnelIpcResponse> SendIpcAsync(
        NETunnelProviderSession session,
        TunnelIpcRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestJson = JsonSerializer.Serialize(request, IosClashJsonContext.Default.TunnelIpcRequest);
        using var requestData = NSData.FromArray(Encoding.UTF8.GetBytes(requestJson));
        var completion = new TaskCompletionSource<NSData?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!session.SendProviderMessage(requestData, out var error, responseData =>
            {
                completion.TrySetResult(responseData);
            }))
        {
            throw new InvalidOperationException(error?.LocalizedDescription ?? "failed to send packet tunnel message");
        }

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var response = await completion.Task.ConfigureAwait(false);
        if (response == null || response.Length == 0)
        {
            throw new InvalidOperationException("packet tunnel returned an empty response");
        }

        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        return JsonSerializer.Deserialize(
            responseJson,
            IosClashJsonContext.Default.TunnelIpcResponse) ?? new TunnelIpcResponse
            {
                Ok = false,
                Error = "packet tunnel returned an invalid response"
            };
    }

    private static async Task<NETunnelProviderSession> LoadTunnelSessionAsync()
    {
        var manager = await LoadOrCreateManagerAsync();
        return manager.Connection as NETunnelProviderSession ??
            throw new InvalidOperationException("configured VPN connection is not a NETunnelProviderSession");
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

            completion.TrySetResult(ToManagerList(managers));
        });

        return completion.Task;
    }

    private static IReadOnlyList<NETunnelProviderManager> ToManagerList(NSArray? managers)
    {
        if (managers == null || managers.Count == 0)
        {
            return [];
        }

        var nativeManagers = NSArray.ArrayFromHandle<NETunnelProviderManager>(managers.Handle);
        if (nativeManagers == null || nativeManagers.Length == 0)
        {
            return [];
        }

        var result = new List<NETunnelProviderManager>(nativeManagers.Length);
        foreach (var manager in nativeManagers)
        {
            if (manager != null)
            {
                result.Add(manager);
            }
        }

        return result;
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
