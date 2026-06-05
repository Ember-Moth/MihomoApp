using System.Text.Json.Serialization;

namespace Aureline.iOS.PacketTunnel.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LibClashSetupRequest))]
[JsonSerializable(typeof(TunnelIpcRequest))]
[JsonSerializable(typeof(TunnelIpcResponse))]
internal sealed partial class PacketTunnelJsonContext : JsonSerializerContext;

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
