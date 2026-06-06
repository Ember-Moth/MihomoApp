using System.Text.Json.Serialization;

namespace Aureline.iOS.PacketTunnel.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LibClashSetupRequest))]
[JsonSerializable(typeof(TunnelIpcRequest))]
[JsonSerializable(typeof(TunnelIpcResponse))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(TunnelProxyGroup))]
[JsonSerializable(typeof(TunnelProxyGroup[]))]
internal sealed partial class PacketTunnelJsonContext : JsonSerializerContext;

internal static class TunnelIpcCommands
{
    public const string ValidateConfig = "validate-config";
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

internal sealed class TunnelIpcRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("groupName")]
    public string? GroupName { get; set; }

    [JsonPropertyName("proxyName")]
    public string? ProxyName { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("testUrl")]
    public string? TestUrl { get; set; }

    [JsonPropertyName("timeoutMilliseconds")]
    public int TimeoutMilliseconds { get; set; } = 5000;

    [JsonPropertyName("configPath")]
    public string? ConfigPath { get; set; }

    [JsonPropertyName("sortMode")]
    public string? SortMode { get; set; }
}

internal sealed class TunnelIpcResponse
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

internal sealed class LibClashSetupRequest
{
    public LibClashSetupRequest(
        string homeDirectory,
        string configPath,
        string externalController,
        int mixedPort,
        string testUrl)
    {
        HomeDirectory = homeDirectory;
        ConfigPath = configPath;
        ExternalController = externalController;
        MixedPort = mixedPort;
        TestUrl = testUrl;
    }

    [JsonPropertyName("home-dir")]
    public string HomeDirectory { get; }

    [JsonPropertyName("config-path")]
    public string ConfigPath { get; }

    [JsonPropertyName("external-controller")]
    public string ExternalController { get; }

    [JsonPropertyName("mixed-port")]
    public int MixedPort { get; }

    [JsonPropertyName("test-url")]
    public string TestUrl { get; }
}

internal sealed class TunnelProxyGroup
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("now")]
    public string? Now { get; set; }

    [JsonPropertyName("proxies")]
    public List<TunnelProxy>? Proxies { get; set; }
}

internal sealed class TunnelProxy
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("delay")]
    public int Delay { get; set; }
}
