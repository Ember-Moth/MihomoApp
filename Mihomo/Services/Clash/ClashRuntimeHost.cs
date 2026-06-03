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

    public string ApiBaseAddress => string.Empty;

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

    private void Publish(ClashStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
