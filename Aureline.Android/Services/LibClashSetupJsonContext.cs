using System.Text.Json.Serialization;

namespace Aureline.Android.Services;

[JsonSerializable(typeof(LibClashSetupRequest))]
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
