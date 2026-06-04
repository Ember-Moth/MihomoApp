using System.Text.Json.Serialization;

namespace Mihomo.iOS.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TunnelIpcRequest))]
[JsonSerializable(typeof(TunnelIpcResponse))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IosNativeProxyGroup))]
internal sealed partial class IosClashJsonContext : JsonSerializerContext;

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

internal sealed class IosNativeProxyGroup
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("now")]
    public string? Now { get; set; }

    [JsonPropertyName("proxies")]
    public List<IosNativeProxy>? Proxies { get; set; }
}

internal sealed class IosNativeProxy
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
