using Foundation;

namespace Aureline.iOS.PacketTunnel.Services;

internal sealed record TunnelOptions(
    string HomeDirectory,
    string ConfigPath,
    int MixedPort,
    string Stack,
    string RouteAddressCsv,
    bool EnableIpv6,
    bool DnsHijacking)
{
    private const string DefaultIpv4Address = "172.19.0.1";
    private const string DefaultIpv4Dns = "172.19.0.2";
    private const string DefaultIpv6Address = "fdfe:dcba:9876::1";
    private const string DefaultIpv6Dns = "fdfe:dcba:9876::2";
    private static readonly NSString HomeDirectoryKey = new("home-dir");
    private static readonly NSString ConfigPathKey = new("config-path");
    private static readonly NSString MixedPortKey = new("mixed-port");
    private static readonly NSString StackKey = new("stack");
    private static readonly NSString RouteAddressCsvKey = new("route-address-csv");
    private static readonly NSString EnableIpv6Key = new("enable-ipv6");
    private static readonly NSString DnsHijackingKey = new("dns-hijacking");

    public string Ipv4Address => DefaultIpv4Address;

    public string Ipv4Dns => DefaultIpv4Dns;

    public string Ipv6Address => DefaultIpv6Address;

    public string Ipv6Dns => DefaultIpv6Dns;

    public string TunAddressCsv => EnableIpv6
        ? $"{Ipv4Address}/30,{Ipv6Address}/126"
        : $"{Ipv4Address}/30";

    public string TunDnsCsv
    {
        get
        {
            if (DnsHijacking)
            {
                return EnableIpv6 ? "0.0.0.0,::" : "0.0.0.0";
            }

            return EnableIpv6 ? $"{Ipv4Dns},{Ipv6Dns}" : Ipv4Dns;
        }
    }

    public static TunnelOptions FromProviderConfiguration(NSDictionary<NSString, NSObject>? configuration)
    {
        return new TunnelOptions(
            ReadString(configuration, HomeDirectoryKey),
            ReadString(configuration, ConfigPathKey),
            ReadInt(configuration, MixedPortKey, 7890),
            ReadString(configuration, StackKey, "system"),
            ReadString(configuration, RouteAddressCsvKey, "0.0.0.0/0"),
            ReadBool(configuration, EnableIpv6Key, false),
            ReadBool(configuration, DnsHijackingKey, true));
    }

    private static string ReadString(
        NSDictionary<NSString, NSObject>? configuration,
        NSString key,
        string fallback = "")
    {
        var value = configuration?[key];
        return value switch
        {
            NSString text => text.ToString(),
            _ => fallback
        };
    }

    private static int ReadInt(
        NSDictionary<NSString, NSObject>? configuration,
        NSString key,
        int fallback)
    {
        var value = configuration?[key];
        return value switch
        {
            NSNumber number => number.Int32Value,
            NSString text when int.TryParse(text.ToString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private static bool ReadBool(
        NSDictionary<NSString, NSObject>? configuration,
        NSString key,
        bool fallback)
    {
        var value = configuration?[key];
        return value switch
        {
            NSNumber number => number.BoolValue,
            NSString text when bool.TryParse(text.ToString(), out var parsed) => parsed,
            _ => fallback
        };
    }
}
