using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mihomo.Services.Clash;

[JsonSerializable(typeof(ClashProxiesResponse))]
[JsonSerializable(typeof(ClashProxySelectionRequest))]
[JsonSerializable(typeof(ClashTrafficResponse))]
internal sealed partial class ClashApiJsonContext : JsonSerializerContext;

internal sealed class ClashProxiesResponse
{
    [JsonPropertyName("proxies")]
    public Dictionary<string, ClashProxyDocument>? Proxies { get; set; }
}

internal sealed class ClashProxyDocument
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("now")]
    public string? Now { get; set; }

    [JsonPropertyName("all")]
    public List<string>? All { get; set; }

    [JsonPropertyName("delay")]
    public int? Delay { get; set; }

    [JsonPropertyName("history")]
    public List<ClashProxyHistory>? History { get; set; }
}

internal sealed class ClashProxyHistory
{
    [JsonPropertyName("delay")]
    public int? Delay { get; set; }
}

internal sealed class ClashProxySelectionRequest
{
    public ClashProxySelectionRequest(string name)
    {
        Name = name;
    }

    [JsonPropertyName("name")]
    public string Name { get; }
}

internal sealed class ClashTrafficResponse
{
    [JsonPropertyName("up")]
    public long Up { get; set; }

    [JsonPropertyName("down")]
    public long Down { get; set; }

    [JsonPropertyName("upTotal")]
    public long UpTotal { get; set; }

    [JsonPropertyName("downTotal")]
    public long DownTotal { get; set; }
}
