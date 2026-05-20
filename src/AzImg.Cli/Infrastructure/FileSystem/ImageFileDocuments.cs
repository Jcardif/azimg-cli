using AzImg.Cli.Application.GeneratedImages;

namespace AzImg.Cli.Infrastructure.FileSystem;

/// <summary>
/// Metadata for one image file saved by the CLI.
/// </summary>
/// <param name="Index">The one-based index of the image within the operation result.</param>
/// <param name="Path">The absolute path to the saved file.</param>
/// <param name="Sha256">The lowercase hexadecimal SHA-256 digest of the saved bytes.</param>
/// <param name="SizeBytes">The number of bytes written to disk.</param>
public sealed record SavedImageFile(
    int Index,
    string Path,
    string Sha256,
    long SizeBytes);

/// <summary>
/// Result of writing image files and an optional manifest to disk.
/// </summary>
/// <param name="Files">The saved image files.</param>
/// <param name="ManifestPath">The saved manifest path, if manifest output was requested.</param>
public sealed record SaveImagesResult(
    IReadOnlyList<SavedImageFile> Files,
    string? ManifestPath);

/// <summary>
/// JSON sidecar document written next to image outputs when manifest output is enabled.
/// </summary>
/// <param name="Prompt">The prompt used for the image operation.</param>
/// <param name="Service">The service family that produced the images.</param>
/// <param name="Deployment">The Azure OpenAI deployment used for the operation.</param>
/// <param name="CreatedAt">The service-created timestamp.</param>
/// <param name="Usage">Optional Azure OpenAI token usage.</param>
/// <param name="Files">The image files included in the operation.</param>
public sealed record ImageManifestDocument(
    string Prompt,
    string Service,
    string Deployment,
    DateTimeOffset CreatedAt,
    ImageUsageSnapshot? Usage,
    SavedImageFile[] Files);