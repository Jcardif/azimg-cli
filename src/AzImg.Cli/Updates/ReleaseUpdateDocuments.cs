namespace AzImg.Cli.Updates;

/// <summary>
/// Machine-readable release metadata uploaded beside platform archives for installers and self-update.
/// </summary>
/// <param name="Product">The product name the manifest describes.</param>
/// <param name="Repository">The GitHub repository in <c>owner/name</c> form.</param>
/// <param name="Version">The release version without a leading <c>v</c>.</param>
/// <param name="TagName">The Git tag that owns the assets.</param>
/// <param name="Assets">The RID-specific archives available in the release.</param>
/// <param name="PublishMode">The workflow publish mode, such as <c>single-file</c> or <c>aot</c>.</param>
public sealed record ReleaseManifestDocument(
    string Product,
    string Repository,
    string Version,
    string TagName,
    ReleaseAssetDocument[] Assets,
    string? PublishMode = null);

/// <summary>
/// Metadata for one release archive in <see cref="ReleaseManifestDocument" />.
/// </summary>
/// <param name="Rid">The exact .NET runtime identifier for the asset.</param>
/// <param name="FileName">The archive file name uploaded to the GitHub release.</param>
/// <param name="Sha256">The lowercase hexadecimal SHA-256 digest of the archive.</param>
/// <param name="SizeBytes">The archive size in bytes.</param>
/// <param name="ArchiveType">The archive format, either <c>zip</c> or <c>tar.gz</c>.</param>
/// <param name="Os">The target operating system family.</param>
/// <param name="Architecture">The target CPU architecture.</param>
/// <param name="DownloadUrl">An optional absolute URL for this archive.</param>
public sealed record ReleaseAssetDocument(
    string Rid,
    string FileName,
    string Sha256,
    long SizeBytes,
    string ArchiveType,
    string Os,
    string Architecture,
    string? DownloadUrl = null);

/// <summary>
/// Local metadata stored under the CLI data directory for installation and update bookkeeping.
/// </summary>
/// <remarks>
/// The single document keeps install information and first-launch update state together while still
/// separating their JSON shapes into <see cref="InstallMetadataDocument" /> and <see cref="UpdateStateDocument" /> sections.
/// </remarks>
public sealed class LocalMetadataDocument
{
    /// <summary>The metadata schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Installer and self-update metadata.</summary>
    public InstallMetadataDocument Install { get; set; } = new();

    /// <summary>Best-effort first-launch update-check state.</summary>
    public UpdateStateDocument Update { get; set; } = new();
}

/// <summary>
/// Local metadata written by installers and refreshed by self-update.
/// </summary>
public sealed class InstallMetadataDocument
{
    /// <summary>The metadata schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The absolute executable path that should be updated.</summary>
    public string? InstallPath { get; set; }

    /// <summary>The RID installed on this machine.</summary>
    public string? Rid { get; set; }

    /// <summary>The installed product version.</summary>
    public string? InstalledVersion { get; set; }

    /// <summary>The GitHub repository used for installation.</summary>
    public string? SourceRepository { get; set; }

    /// <summary>The installation method, such as <c>install.sh</c>, <c>install.ps1</c>, or <c>self-update</c>.</summary>
    public string? InstallMethod { get; set; }

    /// <summary>When the install metadata was last written.</summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>
/// Local state used to make automatic first-launch checks best-effort and once-per-version.
/// </summary>
public sealed class UpdateStateDocument
{
    /// <summary>The metadata schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The application version for which the first-launch check has already been attempted.</summary>
    public string? LastCheckedAppVersion { get; set; }

    /// <summary>When the latest first-launch check was attempted.</summary>
    public DateTimeOffset? LastCheckAtUtc { get; set; }

    /// <summary>The latest release version observed during the check.</summary>
    public string? LatestVersion { get; set; }

    /// <summary>Whether the latest check found an available update.</summary>
    public bool? UpdateAvailable { get; set; }

    /// <summary>The last non-fatal check error, if any.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Parsed options for <c>azimg update</c> commands.
/// </summary>
/// <param name="Version">An optional release version or tag to target instead of latest.</param>
/// <param name="ManifestUrl">An optional explicit release manifest URL, primarily for testing and advanced automation.</param>
/// <param name="InstallDirectory">An optional directory that contains the executable to update.</param>
/// <param name="DryRun">Whether to report the intended work without changing files.</param>
/// <param name="Force">Whether to reinstall even when the selected release is not newer.</param>
public sealed record UpdateCommandOptions(
    string? Version,
    string? ManifestUrl,
    string? InstallDirectory,
    bool DryRun,
    bool Force);

/// <summary>
/// JSON shape emitted by <c>azimg update check</c> unless <c>--format text</c> is passed.
/// </summary>
public sealed record UpdateCheckDocument(
    string Product,
    string CommandName,
    string CurrentVersion,
    string LatestVersion,
    bool UpdateAvailable,
    string Rid,
    string ManifestUrl,
    DateTimeOffset CheckedAtUtc,
    ReleaseAssetDocument? Asset);

/// <summary>
/// JSON shape emitted by <c>azimg update</c> and <c>azimg update apply</c> unless <c>--format text</c> is passed.
/// </summary>
public sealed record UpdateApplyDocument(
    string Product,
    string CommandName,
    string CurrentVersion,
    string LatestVersion,
    bool UpdateAvailable,
    bool DryRun,
    bool Updated,
    bool UpdateScheduled,
    string Rid,
    string? TargetPath,
    string ManifestUrl,
    ReleaseAssetDocument? Asset,
    string Message);
