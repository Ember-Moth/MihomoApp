using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Mihomo.Android.Interop;
using Mihomo.Android.Vpn;
using Mihomo.Models;
using Mihomo.Services.Clash;

namespace Mihomo.Android.Services;

internal sealed class AndroidClashRuntime : IClashRuntime
{
    private const string ExternalControllerListenAt = "127.0.0.1:9090";

    private readonly Activity activity;

    public event EventHandler<ClashStatus>? StatusChanged;

    public ClashStatus Status { get; private set; } = ClashStatus.Stopped;

    public string DefaultHomeDirectory { get; }

    public string DefaultConfigPath { get; }

    public string ApiBaseAddress => "http://" + ExternalControllerListenAt;

    private AndroidClashRuntime(Activity activity)
    {
        this.activity = activity;
        DefaultHomeDirectory = activity.FilesDir?.AbsolutePath ?? string.Empty;
        DefaultConfigPath = Path.Combine(DefaultHomeDirectory, "config.yaml");
    }

    public static void Install(Activity activity)
    {
        ClashRuntimeHost.Current = new AndroidClashRuntime(activity);
    }

    public Task InitializeAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(profile.HomeDirectory);
        EnsureConfigFile(profile);
        LibClashNative.Init(profile.HomeDirectory, (int)Build.VERSION.SdkInt);
        return Task.CompletedTask;
    }

    public Task<string> ValidateConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            return Task.FromResult($"Config file not found: {configPath}");
        }

        return Task.FromResult(LibClashNative.ValidateConfig(configPath));
    }

    public Task StartAsync(ClashProfile profile, CancellationToken cancellationToken = default)
    {
        Publish(new ClashStatus(ClashRunState.Starting, "Starting core"));
        EnsureConfigFile(profile);

        var setupJson = JsonSerializer.Serialize(
            new LibClashSetupRequest(
                profile.HomeDirectory,
                profile.ConfigPath,
                ExternalControllerListenAt,
                profile.MixedPort,
                "https://www.gstatic.com/generate_204"),
            LibClashSetupJsonContext.Default.LibClashSetupRequest);
        var setupMessage = LibClashNative.SetupConfig(setupJson);
        if (!string.IsNullOrWhiteSpace(setupMessage))
        {
            Publish(new ClashStatus(ClashRunState.Error, setupMessage));
            return Task.CompletedTask;
        }

        if (profile.EnableTun)
        {
            var permissionIntent = VpnService.Prepare(activity);
            if (permissionIntent != null)
            {
                activity.StartActivityForResult(permissionIntent, 1000);
                Publish(new ClashStatus(ClashRunState.Stopped, "VPN permission requested. Start again after approval."));
                return Task.CompletedTask;
            }

            ClashVpnService.Start(activity, ClashVpnOptions.FromProfile(profile));
        }
        Publish(new ClashStatus(ClashRunState.Running, "Running", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Publish(new ClashStatus(ClashRunState.Stopping, "Stopping"));

        TryStopVpn();
        TryStopListener();
        TryResetCore();
        Publish(ClashStatus.Stopped);

        return Task.CompletedTask;
    }

    private void TryStopVpn()
    {
        try
        {
            ClashVpnService.Stop(activity);
        }
        catch
        {
            // The service may not be running, or native code may still be absent.
        }
    }

    private static void TryStopListener()
    {
        try
        {
            LibClashNative.StopListener();
        }
        catch
        {
            // Native library may not be present during early app development.
        }
    }

    private static void TryResetCore()
    {
        try
        {
            LibClashNative.Reset();
        }
        catch
        {
            // Native library may not be present during early app development.
        }
    }

    private void EnsureConfigFile(ClashProfile profile)
    {
        var targetConfigPath = Path.Combine(profile.HomeDirectory, "config.yaml");
        if (!string.Equals(profile.ConfigPath, targetConfigPath, StringComparison.Ordinal) && File.Exists(profile.ConfigPath))
        {
            File.Copy(profile.ConfigPath, targetConfigPath, true);
            return;
        }

        if (File.Exists(targetConfigPath))
        {
            return;
        }

        File.WriteAllText(targetConfigPath, DefaultConfig(profile.MixedPort));
    }

    private static string DefaultConfig(int mixedPort)
    {
        return $$"""
               mixed-port: {{mixedPort}}
               allow-lan: false
               mode: rule
               log-level: info
               ipv6: false
               proxies:
                 - name: DIRECT-TEST
                   type: direct
               proxy-groups:
                 - name: Proxy
                   type: select
                   proxies:
                     - DIRECT
                     - DIRECT-TEST
               rules:
                 - MATCH,Proxy
               """;
    }

    private void Publish(ClashStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
