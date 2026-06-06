using System.Text.Json.Serialization;
using Aureline.Models;
using Aureline.Services.Clash;

namespace Aureline.Android.Services;

[JsonSerializable(typeof(AndroidCoreProfile))]
[JsonSerializable(typeof(ValidateConfigIpcRequest))]
[JsonSerializable(typeof(ProxyGroupsIpcRequest))]
[JsonSerializable(typeof(SelectProxyIpcRequest))]
[JsonSerializable(typeof(SetModeIpcRequest))]
[JsonSerializable(typeof(ProxyDelayIpcRequest))]
[JsonSerializable(typeof(HealthCheckIpcRequest))]
[JsonSerializable(typeof(CoreIpcResponse))]
[JsonSerializable(typeof(ClashStatus))]
[JsonSerializable(typeof(ClashTraffic))]
[JsonSerializable(typeof(ClashProxyGroup[]))]
internal sealed partial class AndroidIpcJsonContext : JsonSerializerContext;

internal sealed record AndroidCoreProfile(
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
    string AccessPackageCsv,
    bool ExternalController)
{
    public static AndroidCoreProfile FromProfile(ClashProfile profile)
    {
        return new AndroidCoreProfile(
            profile.HomeDirectory,
            profile.ConfigPath,
            profile.MixedPort,
            profile.EnableTun,
            profile.EnableIpv6,
            profile.DnsHijacking,
            profile.SystemProxy,
            profile.Stack,
            profile.RouteAddressCsv,
            profile.AccessControlEnabled,
            profile.AccessControlMode,
            string.Join(',', profile.AccessPackageNames),
            profile.ExternalController);
    }

    public ClashProfile ToProfile()
    {
        return new ClashProfile(
            HomeDirectory,
            ConfigPath,
            MixedPort,
            EnableTun,
            EnableIpv6,
            DnsHijacking,
            SystemProxy,
            Stack,
            RouteAddressCsv,
            AccessControlEnabled,
            AccessControlMode,
            AccessPackageCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ExternalController);
    }
}

internal sealed record ValidateConfigIpcRequest(string ConfigPath);

internal sealed record ProxyGroupsIpcRequest(string SortMode);

internal sealed record SelectProxyIpcRequest(string GroupName, string ProxyName);

internal sealed record SetModeIpcRequest(string Mode);

internal sealed record ProxyDelayIpcRequest(string ProxyName, string TestUrl, int TimeoutMilliseconds);

internal sealed record HealthCheckIpcRequest(string GroupName);

internal sealed class CoreIpcResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [JsonPropertyName("longValue")]
    public long LongValue { get; set; }

    [JsonPropertyName("secondLongValue")]
    public long SecondLongValue { get; set; }

    [JsonPropertyName("intValue")]
    public int IntValue { get; set; }

    [JsonPropertyName("boolValue")]
    public bool BoolValue { get; set; }
}

internal static class AndroidIpcCommands
{
    public const string Initialize = "initialize";
    public const string ValidateConfig = "validate-config";
    public const string Start = "start";
    public const string Stop = "stop";
    public const string GetStatus = "get-status";
    public const string GetProxyGroups = "get-proxy-groups";
    public const string GetTraffic = "get-traffic";
    public const string GetConnectionCount = "get-connection-count";
    public const string SelectProxy = "select-proxy";
    public const string SetMode = "set-mode";
    public const string TestProxyDelay = "test-proxy-delay";
    public const string HealthCheck = "health-check";
    public const string HealthCheckAll = "health-check-all";
    public const string CloseAllConnections = "close-all-connections";
    public const string ForceGc = "force-gc";
}

internal static class AndroidIpcWire
{
    public const int MessageRequest = 1;
    public const int MessageResponse = 2;
    public const string ActionBindCore = "com.embermoth.aureline.action.BIND_CORE";
    public const string ExtraRequestId = "requestId";
    public const string ExtraCommand = "command";
    public const string ExtraPayload = "payload";
}
