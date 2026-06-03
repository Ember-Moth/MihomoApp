using System.Text.Json;
using Foundation;
using NetworkExtension;
using Mihomo.iOS.PacketTunnel.Interop;

namespace Mihomo.iOS.PacketTunnel.Services;

internal sealed class PacketTunnelRuntime
{
    private const string ExternalControllerListenAt = "127.0.0.1:9090";

    private readonly NEPacketTunnelProvider provider;

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

        MemoryPressure.Trim();
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
                IncludedRoutes = [NEIPv6Route.DefaultRoute]
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
            IncludedRoutes = [NEIPv4Route.DefaultRoute]
        };

        return settings;
    }

    private void StartCore(TunnelOptions options, Action<NSError?> completionHandler)
    {
        var fd = UtunFileDescriptor.Find(provider.PacketFlow);
        if (fd < 0)
        {
            completionHandler(CreateError("failed to locate utun file descriptor"));
            return;
        }

        var setupJson = JsonSerializer.Serialize(
            new LibClashSetupRequest(
                options.HomeDirectory,
                options.ConfigPath,
                ExternalControllerListenAt,
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
        MemoryPressure.Trim();
        completionHandler(null);
    }

    private static NSError CreateError(string message)
    {
        using var descriptionKey = new NSString("NSLocalizedDescription");
        using var description = new NSString(message);
        using var domain = new NSString("Mihomo.PacketTunnel");
        using var userInfo = NSDictionary.FromObjectAndKey(description, descriptionKey);
        return NSError.FromDomain(domain, 1, userInfo);
    }
}
