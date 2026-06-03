using System.Text.Json.Serialization;

namespace Mihomo.Android.Services;

[JsonSerializable(typeof(LibClashSetupRequest))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(NativeProxyGroup))]
internal sealed partial class LibClashSetupJsonContext : JsonSerializerContext;

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

internal sealed class NativeProxyGroup
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("now")]
    public string? Now { get; set; }

    [JsonPropertyName("proxies")]
    public List<NativeProxy>? Proxies { get; set; }
}

internal sealed class NativeProxy
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
