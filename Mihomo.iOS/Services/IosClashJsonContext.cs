using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mihomo.iOS.Services;

internal sealed record IosControllerProxiesResponse(
    [property: JsonPropertyName("proxies")] Dictionary<string, IosControllerProxy>? Proxies);

internal sealed record IosControllerProxy(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("now")] string? Now,
    [property: JsonPropertyName("all")] List<string>? All,
    [property: JsonPropertyName("history")] List<IosControllerDelayHistory>? History);

internal sealed record IosControllerDelayHistory(
    [property: JsonPropertyName("delay")] int Delay);

internal sealed record IosControllerDelayResponse(
    [property: JsonPropertyName("delay")] int Delay);

internal sealed record IosControllerConnectionsResponse(
    [property: JsonPropertyName("uploadTotal")] long UploadTotal = 0,
    [property: JsonPropertyName("downloadTotal")] long DownloadTotal = 0,
    [property: JsonPropertyName("connections")] List<JsonElement>? Connections = null);

internal sealed record IosControllerNameRequest(
    [property: JsonPropertyName("name")] string Name);

internal sealed record IosControllerModeRequest(
    [property: JsonPropertyName("mode")] string Mode);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IosControllerProxiesResponse))]
[JsonSerializable(typeof(IosControllerDelayResponse))]
[JsonSerializable(typeof(IosControllerConnectionsResponse))]
[JsonSerializable(typeof(IosControllerNameRequest))]
[JsonSerializable(typeof(IosControllerModeRequest))]
internal sealed partial class IosClashJsonContext : JsonSerializerContext;
