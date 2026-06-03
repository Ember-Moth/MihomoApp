namespace Mihomo.Services.Clash;

public sealed record ClashProxyGroup(
    string Name,
    string Type,
    string Now,
    string TestUrl,
    IReadOnlyList<ClashProxy> Proxies);

public sealed record ClashProxy(
    string Name,
    string Type,
    string Now,
    int? Delay);

public sealed record ClashTraffic(
    long Up,
    long Down,
    long UpTotal,
    long DownTotal);
