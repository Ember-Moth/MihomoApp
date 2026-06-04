using Mihomo.Models;

namespace Mihomo.Services.Clash;

public static class ClashRuntimeHost
{
    public static IClashRuntime Current { get; set; } = new UnsupportedClashRuntime();
}

internal sealed class UnsupportedClashRuntime : IClashRuntime
{
    public event EventHandler<ClashStatus>? StatusChanged;

    public ClashStatus Status { get; private set; } = new(ClashRunState.Error, "Android runtime is not registered");

    public string DefaultHomeDirectory => string.Empty;

    public string DefaultConfigPath => string.Empty;

    public Task InitializeAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Publish(Status);
        return Task.CompletedTask;
    }

    public Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("Android runtime is not registered");
    }

    public Task StartAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Publish(Status);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Publish(Status);
        return Task.CompletedTask;
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

    public Task<bool> SetModeAsync(string mode, CancellationToken cancellationToken = default)
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

    public Task CloseAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private void Publish(ClashStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
