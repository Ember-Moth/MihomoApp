using Mihomo.Models;

namespace Mihomo.Services.Clash;

public interface IClashRuntime
{
    event EventHandler<ClashStatus>? StatusChanged;

    ClashStatus Status { get; }

    string DefaultHomeDirectory { get; }

    string DefaultConfigPath { get; }

    string ApiBaseAddress { get; }

    Task InitializeAsync(ClashProfile profile, CancellationToken cancellationToken = default);

    Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default);

    Task StartAsync(ClashProfile profile, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
