using System.Text;
using System.Text.Json;
using System.Net;
using Foundation;
using NetworkExtension;
using Aureline.iOS.PacketTunnel.Interop;

namespace Aureline.iOS.PacketTunnel.Services;

internal sealed class PacketTunnelRuntime
{
    private readonly object sync = new();
    private readonly NEPacketTunnelProvider provider;
    private MemoryPressureMaintenance? memoryMaintenance;
    private bool coreStarted;

    public PacketTunnelRuntime(NEPacketTunnelProvider provider)
    {
        this.provider = provider;
    }

    public void Start(NSDictionary<NSString, NSObject> options, Action<NSError?> completionHandler)
    {
        try
        {
            var tunnelOptions = TunnelOptions.FromProviderConfiguration(
                (provider.ProtocolConfiguration as NETunnelProviderProtocol)?.ProviderConfiguration);

            if (string.IsNullOrWhiteSpace(tunnelOptions.HomeDirectory))
            {
                completionHandler(CreateError("home-dir is empty"));
                return;
            }

            MemoryPressure.ConfigureNativeRuntime();
            Directory.CreateDirectory(tunnelOptions.HomeDirectory);
            LibClashNative.Init(tunnelOptions.HomeDirectory);

            var settings = CreateNetworkSettings(tunnelOptions);
            provider.SetTunnelNetworkSettings(settings, error =>
            {
                if (error != null)
                {
                    completionHandler(error);
                    return;
                }

                StartCore(tunnelOptions, completionHandler);
            });
        }
        catch (Exception ex)
        {
            completionHandler(CreateError(ex.Message));
        }
    }

    public void Stop()
    {
        memoryMaintenance?.Dispose();
        memoryMaintenance = null;

        lock (sync)
        {
            coreStarted = false;

            try
            {
                LibClashNative.StopTun();
            }
            catch
            {
                // The tunnel may not have started fully.
            }

            try
            {
                LibClashNative.StopListener();
                LibClashNative.Reset();
            }
            catch
            {
                // The core may already be stopped.
            }
        }

        MemoryPressure.Trim();
    }

    public NSData HandleAppMessage(NSData messageData)
    {
        try
        {
            var request = DecodeRequest(messageData);
            var response = HandleRequest(request);
            return EncodeResponse(response);
        }
        catch (Exception ex)
        {
            return EncodeResponse(Fail(ex.Message));
        }
    }

    private static NEPacketTunnelNetworkSettings CreateNetworkSettings(TunnelOptions options)
    {
        var settings = new NEPacketTunnelNetworkSettings("198.18.0.1")
        {
            Mtu = NSNumber.FromInt32(9000),
            IPv4Settings = CreateIpv4Settings(options),
            DnsSettings = new NEDnsSettings(new[] { options.Ipv4Dns })
            {
                MatchDomains = [string.Empty]
            }
        };

        if (options.EnableIpv6)
        {
            settings.IPv6Settings = new NEIPv6Settings(
                [options.Ipv6Address],
                [NSNumber.FromInt32(126)])
            {
                IncludedRoutes = CreateIpv6Routes(options.RouteAddressCsv)
            };
        }

        return settings;
    }

    private static NEIPv4Settings CreateIpv4Settings(TunnelOptions options)
    {
        var settings = new NEIPv4Settings(
            [options.Ipv4Address],
            ["255.255.255.252"])
        {
            IncludedRoutes = CreateIpv4Routes(options.RouteAddressCsv)
        };

        return settings;
    }

    private static NEIPv4Route[] CreateIpv4Routes(string routeAddressCsv)
    {
        var routes = routeAddressCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseIpv4Route)
            .Where(route => route != null)
            .Cast<NEIPv4Route>()
            .ToArray();

        return routes.Length == 0 ? [NEIPv4Route.DefaultRoute] : routes;
    }

    private static NEIPv6Route[] CreateIpv6Routes(string routeAddressCsv)
    {
        var routes = routeAddressCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseIpv6Route)
            .Where(route => route != null)
            .Cast<NEIPv6Route>()
            .ToArray();

        return routes.Length == 0 ? [NEIPv6Route.DefaultRoute] : routes;
    }

    private static NEIPv4Route? ParseIpv4Route(string route)
    {
        if (route.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        var parsed = ParseCidr(route, maxPrefix: 32);
        if (parsed == null ||
            !IPAddress.TryParse(parsed.Value.Address, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return null;
        }

        return parsed.Value.Prefix == 0
            ? NEIPv4Route.DefaultRoute
            : new NEIPv4Route(address.ToString(), Ipv4PrefixToSubnetMask(parsed.Value.Prefix));
    }

    private static NEIPv6Route? ParseIpv6Route(string route)
    {
        if (!route.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        var parsed = ParseCidr(route, maxPrefix: 128);
        if (parsed == null ||
            !IPAddress.TryParse(parsed.Value.Address, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return null;
        }

        return parsed.Value.Prefix == 0
            ? NEIPv6Route.DefaultRoute
            : new NEIPv6Route(address.ToString(), NSNumber.FromInt32(parsed.Value.Prefix));
    }

    private static (string Address, int Prefix)? ParseCidr(string route, int maxPrefix)
    {
        var parts = route.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            !int.TryParse(parts[1], out var prefix) ||
            prefix < 0 ||
            prefix > maxPrefix)
        {
            return null;
        }

        return (parts[0], prefix);
    }

    private static string Ipv4PrefixToSubnetMask(int prefix)
    {
        if (prefix <= 0)
        {
            return "0.0.0.0";
        }

        var mask = prefix == 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);
        return $"{(mask >> 24) & 0xff}.{(mask >> 16) & 0xff}.{(mask >> 8) & 0xff}.{mask & 0xff}";
    }

    private void StartCore(TunnelOptions options, Action<NSError?> completionHandler)
    {
        var fd = UtunFileDescriptor.Find(provider.PacketFlow);
        if (fd < 0)
        {
            completionHandler(CreateError("failed to locate utun file descriptor"));
            return;
        }

        lock (sync)
        {
            var setupJson = JsonSerializer.Serialize(
                new LibClashSetupRequest(
                    options.HomeDirectory,
                    options.ConfigPath,
                    string.Empty,
                    options.MixedPort,
                    "https://www.gstatic.com/generate_204"),
                PacketTunnelJsonContext.Default.LibClashSetupRequest);

            var setupMessage = LibClashNative.SetupConfig(setupJson);
            if (!string.IsNullOrWhiteSpace(setupMessage))
            {
                completionHandler(CreateError(setupMessage));
                return;
            }

            LibClashNative.StartTun(fd, options.Stack, options.TunAddressCsv, options.TunDnsCsv);
            coreStarted = true;
        }

        MemoryPressure.Trim();
        memoryMaintenance = new MemoryPressureMaintenance();
        completionHandler(null);
    }

    private TunnelIpcResponse HandleRequest(TunnelIpcRequest request)
    {
        lock (sync)
        {
            return request.Action switch
            {
                TunnelIpcCommands.GetStatus => Ok(boolValue: coreStarted),
                TunnelIpcCommands.ValidateConfig => ValidateConfig(request.ConfigPath),
                TunnelIpcCommands.GetProxyGroups => WithCore(() => Ok(
                    payload: QueryProxyGroups(request.SortMode ?? "Default"))),
                TunnelIpcCommands.GetTraffic => WithCore(() => Ok(
                    longValue: LibClashNative.QueryTrafficNow(),
                    secondLongValue: LibClashNative.QueryTrafficTotal())),
                TunnelIpcCommands.GetConnectionCount => WithCore(() => Ok(intValue: LibClashNative.QueryConnectionCount())),
                TunnelIpcCommands.SelectProxy => WithCore(() => Ok(
                    boolValue: LibClashNative.PatchSelector(
                        Require(request.GroupName, "groupName"),
                        Require(request.ProxyName, "proxyName")))),
                TunnelIpcCommands.SetMode => WithCore(() => Ok(boolValue: LibClashNative.SetMode(Require(request.Mode, "mode")))),
                TunnelIpcCommands.TestProxyDelay => WithCore(() => Ok(
                    intValue: LibClashNative.TestProxyDelay(
                        Require(request.ProxyName, "proxyName"),
                        string.IsNullOrWhiteSpace(request.TestUrl)
                            ? "https://www.gstatic.com/generate_204"
                            : request.TestUrl,
                        request.TimeoutMilliseconds <= 0 ? 5000 : request.TimeoutMilliseconds) ?? 0)),
                TunnelIpcCommands.HealthCheck => WithCore(() =>
                {
                    if (string.IsNullOrWhiteSpace(request.GroupName))
                    {
                        LibClashNative.HealthCheckAll();
                    }
                    else
                    {
                        LibClashNative.HealthCheck(request.GroupName);
                    }

                    return Ok();
                }),
                TunnelIpcCommands.HealthCheckAll => WithCore(() =>
                {
                    LibClashNative.HealthCheckAll();
                    return Ok();
                }),
                TunnelIpcCommands.CloseAllConnections => WithCore(() =>
                {
                    LibClashNative.CloseAllConnections();
                    return Ok();
                }),
                TunnelIpcCommands.ForceGc => OkAfterGc(),
                _ => Fail($"unsupported action: {request.Action}")
            };
        }
    }

    private static string QueryProxyGroups(string sortMode)
    {
        var namesJson = LibClashNative.QueryGroupNames(excludeNotSelectable: false);
        var names = JsonSerializer.Deserialize(
            namesJson,
            PacketTunnelJsonContext.Default.ListString) ?? [];
        var nativeSortMode = ToNativeSortMode(sortMode);
        var groups = new List<TunnelProxyGroup>(names.Count);

        foreach (var name in names)
        {
            var groupJson = LibClashNative.QueryGroup(name, nativeSortMode);
            if (string.IsNullOrWhiteSpace(groupJson))
            {
                continue;
            }

            var group = JsonSerializer.Deserialize(
                groupJson,
                PacketTunnelJsonContext.Default.TunnelProxyGroup);
            if (group?.Proxies is not { Count: > 0 })
            {
                continue;
            }

            group.Name = name;
            groups.Add(group);
        }

        return JsonSerializer.Serialize(
            groups.ToArray(),
            PacketTunnelJsonContext.Default.TunnelProxyGroupArray);
    }

    private TunnelIpcResponse WithCore(Func<TunnelIpcResponse> action)
    {
        return coreStarted ? action() : Fail("packet tunnel core is not running");
    }

    private static TunnelIpcResponse ValidateConfig(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return Fail("configPath is empty");
        }

        return Ok(payload: LibClashNative.ValidateConfig(configPath));
    }

    private static TunnelIpcResponse OkAfterGc()
    {
        MemoryPressure.Trim();
        return Ok();
    }

    private static TunnelIpcRequest DecodeRequest(NSData messageData)
    {
        var json = Encoding.UTF8.GetString(messageData.ToArray());
        return JsonSerializer.Deserialize(
            json,
            PacketTunnelJsonContext.Default.TunnelIpcRequest) ?? new TunnelIpcRequest();
    }

    private static NSData EncodeResponse(TunnelIpcResponse response)
    {
        var json = JsonSerializer.Serialize(response, PacketTunnelJsonContext.Default.TunnelIpcResponse);
        return NSData.FromArray(Encoding.UTF8.GetBytes(json));
    }

    private static TunnelIpcResponse Ok(
        string payload = "",
        long longValue = 0,
        long secondLongValue = 0,
        int intValue = 0,
        bool boolValue = false)
    {
        return new TunnelIpcResponse
        {
            Ok = true,
            Payload = payload,
            LongValue = longValue,
            SecondLongValue = secondLongValue,
            IntValue = intValue,
            BoolValue = boolValue
        };
    }

    private static TunnelIpcResponse Fail(string error)
    {
        return new TunnelIpcResponse
        {
            Ok = false,
            Error = error
        };
    }

    private static string Require(string? value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is empty")
            : value;
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

    private static NSError CreateError(string message)
    {
        using var descriptionKey = new NSString("NSLocalizedDescription");
        using var description = new NSString(message);
        using var domain = new NSString("Aureline.PacketTunnel");
        using var userInfo = NSDictionary.FromObjectAndKey(description, descriptionKey);
        return NSError.FromDomain(domain, 1, userInfo);
    }
}
