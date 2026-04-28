using System.Text;

namespace AzureOpenAI.ImageGen.Cli.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public string? DefaultProfile { get; set; }

    public Dictionary<string, ProfileConfig> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProfileConfig
{
    public string? Deployment { get; set; }

    public string? Endpoint { get; set; }

    public string? OutputDirectory { get; set; }
}

public sealed record ProfileOverrides(
    string? ProfileName,
    string? Deployment,
    string? Endpoint,
    string? OutputDirectory);

public sealed record ResolvedProfile(
    string Name,
    string DeploymentName,
    Uri Endpoint,
    string OutputDirectory);

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

public sealed record GeneratedImageArtifact(int Index, byte[] Content, string Extension);

public sealed record ImageUsageSnapshot(
    long InputTokens,
    long OutputTokens,
    long TotalTokens);

public sealed record ImageOperationResult(
    IReadOnlyList<GeneratedImageArtifact> Images,
    ImageUsageSnapshot? Usage,
    DateTimeOffset CreatedAt,
    string DeploymentName);

public sealed record SavedImageArtifact(
    int Index,
    string Path,
    string Sha256,
    long SizeBytes);

public sealed record SaveImagesResult(
    IReadOnlyList<SavedImageArtifact> Files,
    string? ManifestPath);

public sealed record DoctorCheck(string Name, bool Passed, string Message);

public sealed record DoctorReport(
    string ConfigPath,
    string? ProfileName,
    IReadOnlyList<DoctorCheck> Checks)
{
    public bool IsHealthy => Checks.All(static check => check.Passed);
}

public sealed record ConfigViewDocument(
    string Path,
    string? DefaultProfile,
    Dictionary<string, ProfileConfig> Profiles);

public sealed record DefaultProfileDocument(
    string Path,
    string DefaultProfile);

public sealed record OperationResultDocument(
    string ConfigPath,
    string Profile,
    string Deployment,
    SavedImageArtifact[] Files,
    string? Manifest,
    ImageUsageSnapshot? Usage);

public sealed record ManifestDocument(
    string Prompt,
    string Service,
    string Deployment,
    DateTimeOffset CreatedAt,
    ImageUsageSnapshot? Usage,
    SavedImageArtifact[] Files);

public sealed record DoctorReportDocument(
    string ConfigPath,
    string? ProfileName,
    DoctorCheck[] Checks,
    bool IsHealthy);

public static class Hashing
{
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
