using System.Text;

namespace AzureOpenAI.ImageGen.Cli.Models;

/// <summary>
/// Root object stored in the user's config file.
/// </summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public string? DefaultProfile { get; set; }

    public Dictionary<string, ProfileConfig> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Named Azure OpenAI profile containing the deployment, endpoint, and default output location.
/// </summary>
public sealed class ProfileConfig
{
    public string? Deployment { get; set; }

    public string? Endpoint { get; set; }

    public string? OutputDirectory { get; set; }
}

/// <summary>
/// Command-line values that can override the selected profile for one invocation.
/// </summary>
public sealed record ProfileOverrides(
    string? ProfileName,
    string? Deployment,
    string? Endpoint,
    string? OutputDirectory);

/// <summary>
/// Final profile values after config defaults and command-line overrides are merged.
/// </summary>
public sealed record ResolvedProfile(
    string Name,
    string DeploymentName,
    Uri Endpoint,
    string OutputDirectory);

/// <summary>
/// User request for creating new images from a text prompt.
/// </summary>
public sealed record GenerateImageRequest(
    string Prompt,
    int Count,
    string? Size,
    string? Quality,
    string? Background,
    string? OutputFormat,
    int? OutputCompression,
    string? EndUserId,
    string NameTemplate,
    bool WriteManifest);

/// <summary>
/// User request for editing an existing image, optionally constrained by a mask image.
/// </summary>
public sealed record EditImageRequest(
    string InputFile,
    string? MaskFile,
    string Prompt,
    int Count,
    string? Size,
    string? Quality,
    string? Background,
    string? OutputFormat,
    int? OutputCompression,
    string? EndUserId,
    string NameTemplate,
    bool WriteManifest);

/// <summary>
/// Image bytes returned by Azure OpenAI before they are written to disk.
/// </summary>
public sealed record GeneratedImageArtifact(int Index, byte[] Content, string Extension);

/// <summary>
/// Token usage returned by Azure OpenAI for an image operation, when available.
/// </summary>
public sealed record ImageUsageSnapshot(
    long InputTokens,
    long OutputTokens,
    long TotalTokens);

/// <summary>
/// Normalized result of an Azure image generation or edit operation.
/// </summary>
public sealed record ImageOperationResult(
    IReadOnlyList<GeneratedImageArtifact> Images,
    ImageUsageSnapshot? Usage,
    DateTimeOffset CreatedAt,
    string DeploymentName);

/// <summary>
/// Metadata for one image file saved by the CLI.
/// </summary>
public sealed record SavedImageArtifact(
    int Index,
    string Path,
    string Sha256,
    long SizeBytes);

/// <summary>
/// Result of writing image artifacts and an optional manifest to disk.
/// </summary>
public sealed record SaveImagesResult(
    IReadOnlyList<SavedImageArtifact> Files,
    string? ManifestPath);

/// <summary>
/// One diagnostic check produced by the doctor command.
/// </summary>
public sealed record DoctorCheck(string Name, bool Passed, string Message);

/// <summary>
/// Full diagnostic report produced by the doctor command.
/// </summary>
public sealed record DoctorReport(
    string ConfigPath,
    string? ProfileName,
    IReadOnlyList<DoctorCheck> Checks)
{
    public bool IsHealthy => Checks.All(static check => check.Passed);
}

/// <summary>
/// JSON shape emitted by <c>config show --json</c>.
/// </summary>
public sealed record ConfigViewDocument(
    string Path,
    string? DefaultProfile,
    Dictionary<string, ProfileConfig> Profiles);

/// <summary>
/// JSON shape emitted after changing the default profile.
/// </summary>
public sealed record DefaultProfileDocument(
    string Path,
    string DefaultProfile);

/// <summary>
/// JSON shape emitted by image generation and edit commands.
/// </summary>
public sealed record OperationResultDocument(
    string ConfigPath,
    string Profile,
    string Deployment,
    SavedImageArtifact[] Files,
    string? Manifest,
    ImageUsageSnapshot? Usage);

/// <summary>
/// JSON sidecar document written next to image outputs when manifest output is enabled.
/// </summary>
public sealed record ManifestDocument(
    string Prompt,
    string Service,
    string Deployment,
    DateTimeOffset CreatedAt,
    ImageUsageSnapshot? Usage,
    SavedImageArtifact[] Files);

/// <summary>
/// JSON shape emitted by <c>doctor --json</c>.
/// </summary>
public sealed record DoctorReportDocument(
    string ConfigPath,
    string? ProfileName,
    DoctorCheck[] Checks,
    bool IsHealthy);

/// <summary>
/// Hashing helper for recording stable checksums for saved image files.
/// </summary>
public static class Hashing
{
    /// <summary>
    /// Computes a lowercase hexadecimal SHA-256 digest for the provided bytes.
    /// </summary>
    public static string ComputeSha256(byte[] content)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(content);
        StringBuilder builder = new(hash.Length * 2);
        foreach (byte value in hash)
        {
            builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
