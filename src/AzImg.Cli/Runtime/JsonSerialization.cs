using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AzImg.Cli.Commands;
using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.Infrastructure.FileSystem;
using AzImg.Cli.Updates;

namespace AzImg.Cli.Runtime;

/// <summary>
/// Central JSON helper methods for configuration files, manifests, command output, and error output.
/// </summary>
/// <remarks>
/// All serialization goes through source-generated metadata so the CLI has predictable JSON behavior
/// and avoids reflection-based serialization surprises in trimmed or native deployment scenarios.
/// </remarks>
public static class JsonDefaults
{
    /// <summary>Serializes a value using the provided source-generated type metadata.</summary>
    public static string Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo);

    /// <summary>Serializes a value as UTF-8 JSON into the provided stream.</summary>
    public static Task SerializeAsync<T>(
        Stream utf8Json,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        => JsonSerializer.SerializeAsync(utf8Json, value, typeInfo, cancellationToken);

    /// <summary>Deserializes a value from a UTF-8 JSON stream using source-generated metadata.</summary>
    public static ValueTask<T?> DeserializeAsync<T>(
        Stream utf8Json,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        => JsonSerializer.DeserializeAsync(utf8Json, typeInfo, cancellationToken);

    /// <summary>Serializes a value to UTF-8 bytes using source-generated metadata.</summary>
    public static byte[] SerializeToUtf8Bytes<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
}

/// <summary>
/// Provides source-generated JSON metadata for every configuration and command-output type.
/// </summary>
/// <remarks>
/// Add a <see cref="JsonSerializableAttribute" /> entry here whenever a new model is passed to
/// <see cref="JsonDefaults" />. Missing entries are compile-time visible because callers require
/// strongly typed metadata from this context.
/// </remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(Dictionary<string, ProfileConfig>))]
[JsonSerializable(typeof(ConfigViewDocument))]
[JsonSerializable(typeof(ConfigInitDocument))]
[JsonSerializable(typeof(DefaultProfileDocument))]
[JsonSerializable(typeof(ImageCommandResultDocument))]
[JsonSerializable(typeof(ImageManifestDocument))]
[JsonSerializable(typeof(DiagnosticReportDocument))]
[JsonSerializable(typeof(VersionDocument))]
[JsonSerializable(typeof(AgentSkillInstallDocument))]
[JsonSerializable(typeof(ReleaseManifestDocument))]
[JsonSerializable(typeof(ReleaseAssetDocument[]))]
[JsonSerializable(typeof(LocalMetadataDocument))]
[JsonSerializable(typeof(InstallMetadataDocument))]
[JsonSerializable(typeof(UpdateStateDocument))]
[JsonSerializable(typeof(UpdateCheckDocument))]
[JsonSerializable(typeof(UpdateApplyDocument))]
[JsonSerializable(typeof(UninstallDocument))]
[JsonSerializable(typeof(CliErrorDocument))]
[JsonSerializable(typeof(SavedImageFile[]))]
[JsonSerializable(typeof(DiagnosticCheck[]))]
internal sealed partial class CliJsonContext : JsonSerializerContext
{
}
