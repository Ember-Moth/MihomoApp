using Aureline.Models;

namespace Aureline.Services.Clash;

public enum CorePlatform
{
    Unknown,
    Unsupported,
    Android,
    Ios,
    Desktop
}

public enum CoreControlPlane
{
    None,
    NativeApi,
    Ipc,
    ExternalController
}

public sealed record CoreCapabilities(
    string RuntimeName,
    CorePlatform Platform,
    CoreControlPlane ControlPlane,
    bool CanStart,
    bool CanValidateConfig,
    bool SupportsTun,
    bool SupportsIpv6,
    bool SupportsDnsHijacking,
    bool SupportsSystemProxy,
    bool SupportsAccessControl,
    bool SupportsInstalledApplications,
    bool SupportsProxyGroups,
    bool SupportsProxySelection,
    bool SupportsProxyDelayTest,
    bool SupportsGroupHealthCheck,
    bool SupportsTraffic,
    bool SupportsConnectionCount,
    bool SupportsModeSwitch,
    bool SupportsCloseConnections,
    bool SupportsExternalController,
    bool SupportsGeodataMemoryMode,
    bool SupportsRuntimeRestart,
    IReadOnlyList<string> SupportedStacks)
{
    public static CoreCapabilities Unsupported(string reason = "No runtime is registered")
    {
        return new CoreCapabilities(
            reason,
            CorePlatform.Unsupported,
            CoreControlPlane.None,
            CanStart: false,
            CanValidateConfig: false,
            SupportsTun: false,
            SupportsIpv6: false,
            SupportsDnsHijacking: false,
            SupportsSystemProxy: false,
            SupportsAccessControl: false,
            SupportsInstalledApplications: false,
            SupportsProxyGroups: false,
            SupportsProxySelection: false,
            SupportsProxyDelayTest: false,
            SupportsGroupHealthCheck: false,
            SupportsTraffic: false,
            SupportsConnectionCount: false,
            SupportsModeSwitch: false,
            SupportsCloseConnections: false,
            SupportsExternalController: false,
            SupportsGeodataMemoryMode: false,
            SupportsRuntimeRestart: false,
            SupportedStacks: []);
    }

    public static CoreCapabilities AndroidLibClash { get; } = new(
        "libclash Android",
        CorePlatform.Android,
        CoreControlPlane.NativeApi,
        CanStart: true,
        CanValidateConfig: true,
        SupportsTun: true,
        SupportsIpv6: true,
        SupportsDnsHijacking: true,
        SupportsSystemProxy: true,
        SupportsAccessControl: true,
        SupportsInstalledApplications: true,
        SupportsProxyGroups: true,
        SupportsProxySelection: true,
        SupportsProxyDelayTest: true,
        SupportsGroupHealthCheck: true,
        SupportsTraffic: true,
        SupportsConnectionCount: true,
        SupportsModeSwitch: true,
        SupportsCloseConnections: true,
        SupportsExternalController: true,
        SupportsGeodataMemoryMode: true,
        SupportsRuntimeRestart: true,
        SupportedStacks: ["system", "gvisor", "mixed"]);

    public static CoreCapabilities IosPacketTunnel { get; } = new(
        "libclash iOS PacketTunnel",
        CorePlatform.Ios,
        CoreControlPlane.Ipc,
        CanStart: true,
        CanValidateConfig: true,
        SupportsTun: true,
        SupportsIpv6: true,
        SupportsDnsHijacking: true,
        SupportsSystemProxy: false,
        SupportsAccessControl: false,
        SupportsInstalledApplications: false,
        SupportsProxyGroups: true,
        SupportsProxySelection: true,
        SupportsProxyDelayTest: true,
        SupportsGroupHealthCheck: true,
        SupportsTraffic: true,
        SupportsConnectionCount: true,
        SupportsModeSwitch: true,
        SupportsCloseConnections: true,
        SupportsExternalController: false,
        SupportsGeodataMemoryMode: true,
        SupportsRuntimeRestart: true,
        SupportedStacks: ["system", "gvisor", "mixed"]);

    public static CoreCapabilities NativeClash { get; } = AndroidLibClash with
    {
        RuntimeName = "libclash",
        Platform = CorePlatform.Unknown
    };
}

public sealed record RuntimeState(
    ClashStatus Status,
    CoreCapabilities Capabilities,
    string HomeDirectory = "",
    string ConfigPath = "",
    int MixedPort = 0,
    bool TunEnabled = false,
    bool Ipv6Enabled = false,
    bool ExternalControllerEnabled = false,
    string ActiveStack = "")
{
    public static RuntimeState Unsupported { get; } = new(
        new ClashStatus(ClashRunState.Error, "Runtime is not registered"),
        CoreCapabilities.Unsupported());

    public bool IsRunning => Status.State == ClashRunState.Running;

    public bool IsStarting => Status.State == ClashRunState.Starting;

    public bool IsAvailable => Capabilities.CanStart;

    public bool CanShowProxyPage => IsRunning && Capabilities.SupportsProxyGroups;

    public bool CanReadTelemetry => IsRunning &&
        (Capabilities.SupportsTraffic || Capabilities.SupportsConnectionCount);

    public RuntimeState WithStatus(ClashStatus status)
    {
        return this with { Status = status };
    }

    public RuntimeState WithProfile(ClashProfile profile)
    {
        return this with
        {
            HomeDirectory = profile.HomeDirectory,
            ConfigPath = profile.ConfigPath,
            MixedPort = profile.MixedPort,
            TunEnabled = profile.EnableTun,
            Ipv6Enabled = profile.EnableIpv6,
            ExternalControllerEnabled = profile.ExternalController,
            ActiveStack = profile.Stack
        };
    }
}

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

public sealed record ClashInstalledApplication(
    string PackageName,
    string Label,
    bool IsSystem,
    bool UsesInternet,
    long LastUpdateUnixMilliseconds);
