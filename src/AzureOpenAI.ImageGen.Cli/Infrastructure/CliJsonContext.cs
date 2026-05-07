using System.Text.Json;
using System.Text.Json.Serialization;
using AzureOpenAI.ImageGen.Cli.Models;

namespace AzureOpenAI.ImageGen.Cli.Infrastructure;

/// <summary>
/// Provides source-generated JSON metadata for every configuration and command-output type serialized by the CLI.
/// </summary>
/// <remarks>
/// Add new <see cref="JsonSerializableAttribute" /> entries here when a new model is passed to <see cref="JsonDefaults" />.
/// </remarks>
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
