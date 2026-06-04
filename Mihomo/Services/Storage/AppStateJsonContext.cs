using System.Text.Json.Serialization;

namespace Mihomo.Services.Storage;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AppBackupDocument))]
internal sealed partial class AppStateJsonContext : JsonSerializerContext;
