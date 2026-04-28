using System.Text.Json;
using System.Text.Json.Serialization;
using AzureOpenAI.ImageGen.Cli.Models;

namespace AzureOpenAI.ImageGen.Cli.Infrastructure;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(Dictionary<string, ProfileConfig>))]
[JsonSerializable(typeof(ConfigViewDocument))]
[JsonSerializable(typeof(DefaultProfileDocument))]
[JsonSerializable(typeof(OperationResultDocument))]
[JsonSerializable(typeof(ManifestDocument))]
[JsonSerializable(typeof(DoctorReportDocument))]
[JsonSerializable(typeof(SavedImageArtifact[]))]
[JsonSerializable(typeof(DoctorCheck[]))]
internal sealed partial class CliJsonContext : JsonSerializerContext
{
}
