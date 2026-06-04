namespace Mihomo.Models;

public sealed record ClashProfile(
    string HomeDirectory,
    string ConfigPath,
    int MixedPort,
    bool EnableTun,
    bool EnableIpv6,
    bool DnsHijacking,
    bool SystemProxy,
    string Stack,
    string RouteAddressCsv,
    bool AccessControlEnabled,
    string AccessControlMode,
    IReadOnlyList<string> AccessPackageNames,
    bool ExternalController);
