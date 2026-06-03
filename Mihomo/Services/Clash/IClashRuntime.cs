using Mihomo.Models;

namespace Mihomo.Services.Clash;

public interface IClashRuntime
{
    event EventHandler<ClashStatus>? StatusChanged;

    ClashStatus Status { get; }

    string DefaultHomeDirectory { get; }

    string DefaultConfigPath { get; }

    Task InitializeAsync(ClashProfile profile, CancellationToken cancellationToken = default);

    Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default);

    Task StartAsync(ClashProfile profile, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClashProxyGroup>> GetProxyGroupsAsync(
        string sortMode,
        CancellationToken cancellationToken = default);

    Task<ClashTraffic?> GetTrafficAsync(CancellationToken cancellationToken = default);

    Task<int?> GetConnectionCountAsync(CancellationToken cancellationToken = default);

    Task<bool> SelectProxyAsync(
        string groupName,
        string proxyName,
        CancellationToken cancellationToken = default);

    Task<int?> TestProxyDelayAsync(
        string proxyName,
        string testUrl,
        int timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default);

    Task HealthCheckAsync(string groupName, CancellationToken cancellationToken = default);
}
