namespace Mihomo.Services.Storage;

public sealed record AppStateSnapshot(
    string HomeDirectory,
    string ConfigPath,
    string SubscriptionUrl,
    string MixedPort,
    bool EnableTun,
    bool EnableIpv6,
    bool DnsHijacking,
    bool SystemProxy,
    string Stack,
    string RouteAddressCsv,
    bool IsDarkTheme,
    int? CurrentProfileId,
    IReadOnlyList<StoredConfigProfile> Profiles);

public sealed record StoredConfigProfile(
    int Id,
    string Type,
    string Label,
    string FilePath,
    string Url,
    DateTimeOffset? LastUpdateDate,
    int SortOrder,
    bool IsSelected);
