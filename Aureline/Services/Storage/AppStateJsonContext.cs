using System.Text.Json.Serialization;

namespace Aureline.Services.Storage;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AppBackupDocument))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class AppStateJsonContext : JsonSerializerContext;
