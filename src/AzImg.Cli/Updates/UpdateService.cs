using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AzImg.Cli.Commands;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Updates;

/// <summary>
/// Defines update operations used by the command dispatcher and first-launch check path.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Performs the best-effort first-launch update check and writes non-fatal diagnostics to stderr.
    /// </summary>
    Task NotifyIfFirstLaunchAsync(IReadOnlyList<string> rawArgs, TextWriter diagnostics, CancellationToken cancellationToken);

    /// <summary>Checks whether an update is available without changing files.</summary>
    Task<UpdateCheckDocument> CheckAsync(UpdateCommandOptions options, CancellationToken cancellationToken);

    /// <summary>Applies the selected update, or reports planned work when <see cref="UpdateCommandOptions.DryRun" /> is true.</summary>
    Task<UpdateApplyDocument> ApplyAsync(UpdateCommandOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// No-op update service used by tests that do not need network or filesystem update behavior.
/// </summary>
public sealed class NoOpUpdateService : IUpdateService
{
    /// <summary>A reusable no-op instance.</summary>
    public static NoOpUpdateService Instance { get; } = new();

    private NoOpUpdateService()
    {
    }

    /// <inheritdoc />
    public Task NotifyIfFirstLaunchAsync(IReadOnlyList<string> rawArgs, TextWriter diagnostics, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<UpdateCheckDocument> CheckAsync(UpdateCommandOptions options, CancellationToken cancellationToken)
        => Task.FromResult(CreateUnavailableCheck());

    /// <inheritdoc />
    public Task<UpdateApplyDocument> ApplyAsync(UpdateCommandOptions options, CancellationToken cancellationToken)
        => Task.FromResult(new UpdateApplyDocument(
            CliDefaults.ProductName,
            CliDefaults.CommandName,
            ApplicationVersion.Current,
            ApplicationVersion.Current,
            UpdateAvailable: false,
            options.DryRun,
            Updated: false,
            UpdateScheduled: false,
            RuntimeRidDetector.GetCurrentRid(),
            Environment.ProcessPath,
            CliDefaults.LatestReleaseManifestUrl,
            Asset: null,
            "No update service is configured."));

    private static UpdateCheckDocument CreateUnavailableCheck()
        => new(
            CliDefaults.ProductName,
            CliDefaults.CommandName,
            ApplicationVersion.Current,
            ApplicationVersion.Current,
            UpdateAvailable: false,
            RuntimeRidDetector.GetCurrentRid(),
            CliDefaults.LatestReleaseManifestUrl,
            DateTimeOffset.UtcNow,
            Asset: null);
}

/// <summary>
/// Default implementation for checking GitHub release metadata and replacing the installed executable.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private readonly ReleaseManifestClient _manifestClient;
    private readonly ReleaseAssetDownloader _assetDownloader;
    private readonly LocalMetadataStore _metadataStore;
    private readonly UpdateArchiveExtractor _archiveExtractor;
    private readonly ExecutableReplacer _executableReplacer;

    /// <summary>Initializes an update service with production dependencies.</summary>
    public UpdateService()
        : this(
            new ReleaseManifestClient(),
            new ReleaseAssetDownloader(),
            new LocalMetadataStore(),
            new UpdateArchiveExtractor(),
            new ExecutableReplacer())
    {
    }

    /// <summary>Initializes an update service with explicit dependencies for tests.</summary>
    public UpdateService(
        ReleaseManifestClient manifestClient,
        ReleaseAssetDownloader assetDownloader,
        LocalMetadataStore metadataStore,
        UpdateArchiveExtractor archiveExtractor,
        ExecutableReplacer executableReplacer)
    {
        _manifestClient = manifestClient;
        _assetDownloader = assetDownloader;
        _metadataStore = metadataStore;
        _archiveExtractor = archiveExtractor;
        _executableReplacer = executableReplacer;
    }

    /// <inheritdoc />
    public async Task NotifyIfFirstLaunchAsync(IReadOnlyList<string> rawArgs, TextWriter diagnostics, CancellationToken cancellationToken)
    {
        if (IsUpdateCommand(rawArgs))
        {
            return;
        }

        string currentVersion = ApplicationVersion.Current;
        UpdateStateDocument state;
        try
        {
            state = await _metadataStore.LoadUpdateStateAsync(cancellationToken);
            if (string.Equals(state.LastCheckedAppVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        catch
        {
            state = new UpdateStateDocument();
        }

        try
        {
            UpdateCheckDocument check = await CheckAsync(new UpdateCommandOptions(null, null, null, DryRun: true, Force: false), cancellationToken);
            state.LastCheckedAppVersion = currentVersion;
            state.LastCheckAtUtc = check.CheckedAtUtc;
            state.LatestVersion = check.LatestVersion;
            state.UpdateAvailable = check.UpdateAvailable;
            state.LastError = null;
            await _metadataStore.SaveUpdateStateAsync(state, cancellationToken);

            if (check.UpdateAvailable && !CommandOutputPreferences.RequestsJson(rawArgs))
            {
                diagnostics.WriteLine($"Update available: {CliDefaults.CommandName} {check.LatestVersion} is available. Run '{CliDefaults.CommandName} update' to install it.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.LastCheckedAppVersion = currentVersion;
            state.LastCheckAtUtc = DateTimeOffset.UtcNow;
            state.LastError = ex.Message;
            state.UpdateAvailable = null;
            await TrySaveStateAsync(state, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<UpdateCheckDocument> CheckAsync(UpdateCommandOptions options, CancellationToken cancellationToken)
    {
        UpdateContext context = await BuildContextAsync(options, cancellationToken);
        return new UpdateCheckDocument(
            CliDefaults.ProductName,
            CliDefaults.CommandName,
            context.CurrentVersion,
            context.LatestVersion,
            context.UpdateAvailable,
            context.Rid,
            context.ManifestUrl.ToString(),
            context.CheckedAtUtc,
            context.Asset);
    }

    /// <inheritdoc />
    public async Task<UpdateApplyDocument> ApplyAsync(UpdateCommandOptions options, CancellationToken cancellationToken)
    {
        UpdateContext context = await BuildContextAsync(options, cancellationToken);
        string targetPath = await ResolveTargetPathAsync(options, context.Rid, cancellationToken);

        if (!context.UpdateAvailable && !options.Force)
        {
            return CreateApplyDocument(context, options, targetPath, updated: false, scheduled: false, $"{CliDefaults.CommandName} is already up to date ({context.CurrentVersion}).");
        }

        if (context.Asset is null)
        {
            throw new CliException($"Release '{context.LatestVersion}' does not include an asset for '{context.Rid}'.", ExitCodes.Validation, "update_asset_missing");
        }

        if (options.DryRun)
        {
            return CreateApplyDocument(context, options, targetPath, updated: false, scheduled: false, $"Would update {CliDefaults.CommandName} from {context.CurrentVersion} to {context.LatestVersion} using {context.Asset.FileName}.");
        }

        string stagingRoot = Path.Combine(Path.GetTempPath(), $"azimg-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            Uri assetUri = ResolveAssetUri(context.Manifest, context.Asset);
            string archivePath = Path.Combine(stagingRoot, context.Asset.FileName);
            await _assetDownloader.DownloadAsync(assetUri, archivePath, cancellationToken);

            string actualHash = await HashUtility.ComputeSha256Async(archivePath, cancellationToken);
            if (!actualHash.Equals(context.Asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CliException($"Downloaded archive checksum mismatch. Expected {context.Asset.Sha256}, got {actualHash}.", ExitCodes.Io, "update_checksum_mismatch");
            }

            string extractDirectory = Path.Combine(stagingRoot, "extract");
            Directory.CreateDirectory(extractDirectory);
            await _archiveExtractor.ExtractAsync(archivePath, context.Asset.ArchiveType, extractDirectory, cancellationToken);
            string sourceExecutable = FindExtractedExecutable(extractDirectory, context.Rid);

            ExecutableReplacementResult replacement = _executableReplacer.Replace(sourceExecutable, targetPath);
            await _metadataStore.SaveInstallMetadataAsync(new InstallMetadataDocument
            {
                InstallPath = targetPath,
                Rid = context.Rid,
                InstalledVersion = context.LatestVersion,
                SourceRepository = CliDefaults.ReleaseRepository,
                InstallMethod = replacement.Scheduled ? "self-update-scheduled" : "self-update",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            string message = replacement.Scheduled
                ? $"Update to {context.LatestVersion} is scheduled. Restart {CliDefaults.CommandName} after this process exits."
                : $"Updated {CliDefaults.CommandName} to {context.LatestVersion}.";

            return CreateApplyDocument(context, options, targetPath, updated: !replacement.Scheduled, replacement.Scheduled, message);
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliException($"Update failed while writing files. {ex.Message}", ExitCodes.Io, "update_io");
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private async Task<UpdateContext> BuildContextAsync(UpdateCommandOptions options, CancellationToken cancellationToken)
    {
        string rid = RuntimeRidDetector.GetCurrentRid();
        Uri manifestUrl = ResolveManifestUri(options);
        ReleaseManifestDocument manifest = await _manifestClient.GetManifestAsync(manifestUrl, cancellationToken);
        string latestVersion = VersionUtility.NormalizeVersion(!string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version : manifest.TagName);
        string currentVersion = ApplicationVersion.Current;
        bool updateAvailable = options.Force || VersionUtility.IsNewer(latestVersion, currentVersion);
        ReleaseAssetDocument? asset = manifest.Assets.FirstOrDefault(candidate => candidate.Rid.Equals(rid, StringComparison.OrdinalIgnoreCase));

        if ((updateAvailable || options.Force) && asset is null)
        {
            throw new CliException($"Release '{latestVersion}' does not include an asset for '{rid}'.", ExitCodes.Validation, "update_asset_missing");
        }

        return new UpdateContext(manifest, manifestUrl, currentVersion, latestVersion, updateAvailable, rid, DateTimeOffset.UtcNow, asset);
    }

    private async Task<string> ResolveTargetPathAsync(UpdateCommandOptions options, string rid, CancellationToken cancellationToken)
    {
        string executableName = RuntimeRidDetector.GetExecutableFileName(rid);
        if (!string.IsNullOrWhiteSpace(options.InstallDirectory))
        {
            return Path.GetFullPath(Path.Combine(options.InstallDirectory, executableName));
        }

        InstallMetadataDocument metadata = await _metadataStore.LoadInstallMetadataAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(metadata.InstallPath))
        {
            return Path.GetFullPath(metadata.InstallPath);
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Path.GetFullPath(Environment.ProcessPath);
        }

        throw new CliException("Unable to determine the installed executable path. Re-run update with --install-dir <directory>.", ExitCodes.Configuration, "update_install_path_unknown");
    }

    private static UpdateApplyDocument CreateApplyDocument(
        UpdateContext context,
        UpdateCommandOptions options,
        string? targetPath,
        bool updated,
        bool scheduled,
        string message)
        => new(
            CliDefaults.ProductName,
            CliDefaults.CommandName,
            context.CurrentVersion,
            context.LatestVersion,
            context.UpdateAvailable,
            options.DryRun,
            updated,
            scheduled,
            context.Rid,
            targetPath,
            context.ManifestUrl.ToString(),
            context.Asset,
            message);

    private static Uri ResolveManifestUri(UpdateCommandOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ManifestUrl))
        {
            return Uri.TryCreate(options.ManifestUrl, UriKind.Absolute, out Uri? explicitUri)
                ? explicitUri
                : throw new CliException("--manifest-url must be an absolute URL.", ExitCodes.Validation, "update_manifest_url_invalid");
        }

        if (string.IsNullOrWhiteSpace(options.Version) || options.Version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(CliDefaults.LatestReleaseManifestUrl);
        }

        string tag = VersionUtility.EnsureTag(options.Version);
        return new Uri($"{CliDefaults.GitHubReleasesBaseUrl}/download/{Uri.EscapeDataString(tag)}/{CliDefaults.ReleaseManifestFileName}");
    }

    private static Uri ResolveAssetUri(ReleaseManifestDocument manifest, ReleaseAssetDocument asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.DownloadUrl)
            && Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out Uri? explicitUri))
        {
            return explicitUri;
        }

        string tag = VersionUtility.EnsureTag(!string.IsNullOrWhiteSpace(manifest.TagName) ? manifest.TagName : manifest.Version);
        return new Uri($"{CliDefaults.GitHubReleasesBaseUrl}/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(asset.FileName)}");
    }

    private static string FindExtractedExecutable(string extractDirectory, string rid)
    {
        string executableName = RuntimeRidDetector.GetExecutableFileName(rid);
        string[] matches = Directory.GetFiles(extractDirectory, executableName, SearchOption.AllDirectories);
        if (matches.Length == 0)
        {
            throw new CliException($"The release archive did not contain '{executableName}'.", ExitCodes.Io, "update_archive_invalid");
        }

        return matches[0];
    }

    private static bool IsUpdateCommand(IReadOnlyList<string> rawArgs)
        => rawArgs.Count > 0 && rawArgs[0].Equals("update", StringComparison.OrdinalIgnoreCase);

    private async Task TrySaveStateAsync(UpdateStateDocument state, CancellationToken cancellationToken)
    {
        try
        {
            await _metadataStore.SaveUpdateStateAsync(state, cancellationToken);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record UpdateContext(
        ReleaseManifestDocument Manifest,
        Uri ManifestUrl,
        string CurrentVersion,
        string LatestVersion,
        bool UpdateAvailable,
        string Rid,
        DateTimeOffset CheckedAtUtc,
        ReleaseAssetDocument? Asset);
}

/// <summary>
/// Fetches release manifests from GitHub or an explicitly supplied manifest URL.
/// </summary>
public sealed class ReleaseManifestClient
{
    private readonly HttpClient _httpClient;

    /// <summary>Initializes a release manifest client with a short version-check timeout.</summary>
    public ReleaseManifestClient()
        : this(CreateHttpClient(CliDefaults.UpdateCheckTimeout))
    {
    }

    /// <summary>Initializes a release manifest client with a caller-supplied HTTP client.</summary>
    public ReleaseManifestClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Downloads and parses a release manifest.</summary>
    public async Task<ReleaseManifestDocument> GetManifestAsync(Uri manifestUri, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CliException($"Unable to fetch release manifest from '{manifestUri}' ({(int)response.StatusCode}).", ExitCodes.Io, "update_manifest_fetch_failed");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            ReleaseManifestDocument? manifest = await JsonDefaults.DeserializeAsync(stream, CliJsonContext.Default.ReleaseManifestDocument, cancellationToken);
            if (manifest is null)
            {
                throw new CliException($"The release manifest at '{manifestUri}' is empty or invalid.", ExitCodes.Io, "update_manifest_invalid");
            }

            return manifest;
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new CliException($"Unable to fetch release manifest from '{manifestUri}'. {ex.Message}", ExitCodes.Io, "update_manifest_fetch_failed");
        }
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        HttpClient httpClient = new()
        {
            Timeout = timeout,
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(CliDefaults.UserAgentApplicationId, ApplicationVersion.Current));
        return httpClient;
    }
}

/// <summary>
/// Downloads release archive assets.
/// </summary>
public sealed class ReleaseAssetDownloader
{
    private readonly HttpClient _httpClient;

    /// <summary>Initializes an asset downloader with the longer archive download timeout.</summary>
    public ReleaseAssetDownloader()
        : this(new HttpClient { Timeout = CliDefaults.UpdateDownloadTimeout })
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(CliDefaults.UserAgentApplicationId, ApplicationVersion.Current));
    }

    /// <summary>Initializes an asset downloader with a caller-supplied HTTP client.</summary>
    public ReleaseAssetDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Downloads an archive asset to the provided path.</summary>
    public async Task DownloadAsync(Uri assetUri, string destinationPath, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(assetUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CliException($"Unable to download update archive from '{assetUri}' ({(int)response.StatusCode}).", ExitCodes.Io, "update_download_failed");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
            await using Stream remote = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream local = File.Create(destinationPath);
            await remote.CopyToAsync(local, cancellationToken);
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            throw new CliException($"Unable to download update archive from '{assetUri}'. {ex.Message}", ExitCodes.Io, "update_download_failed");
        }
    }
}

/// <summary>
/// Stores local install metadata and automatic first-launch update-check state in one file.
/// </summary>
public sealed class LocalMetadataStore
{
    private readonly string _path;

    /// <summary>Initializes a metadata store at the default path.</summary>
    public LocalMetadataStore()
        : this(System.IO.Path.Combine(UpdatePaths.GetDefaultDirectory(), CliDefaults.LocalMetadataFileName))
    {
    }

    /// <summary>Initializes a metadata store at an explicit path.</summary>
    public LocalMetadataStore(string path)
    {
        _path = path;
    }

    /// <summary>Gets the metadata file path.</summary>
    public string Path => _path;

    /// <summary>Loads local metadata, returning an empty document when the file does not exist.</summary>
    public async Task<LocalMetadataDocument> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new LocalMetadataDocument();
        }

        await using FileStream stream = File.OpenRead(_path);
        LocalMetadataDocument metadata = await JsonDefaults.DeserializeAsync(stream, CliJsonContext.Default.LocalMetadataDocument, cancellationToken)
            ?? new LocalMetadataDocument();
        metadata.Install ??= new InstallMetadataDocument();
        metadata.Update ??= new UpdateStateDocument();
        return metadata;
    }

    /// <summary>Loads the install metadata section.</summary>
    public async Task<InstallMetadataDocument> LoadInstallMetadataAsync(CancellationToken cancellationToken)
        => (await LoadAsync(cancellationToken)).Install;

    /// <summary>Loads the update state section.</summary>
    public async Task<UpdateStateDocument> LoadUpdateStateAsync(CancellationToken cancellationToken)
        => (await LoadAsync(cancellationToken)).Update;

    /// <summary>Saves the install metadata section while preserving update state.</summary>
    public async Task SaveInstallMetadataAsync(InstallMetadataDocument install, CancellationToken cancellationToken)
    {
        LocalMetadataDocument metadata = await LoadAsync(cancellationToken);
        metadata.Install = install;
        await SaveAsync(metadata, cancellationToken);
    }

    /// <summary>Saves the update state section while preserving install metadata.</summary>
    public async Task SaveUpdateStateAsync(UpdateStateDocument update, CancellationToken cancellationToken)
    {
        LocalMetadataDocument metadata = await LoadAsync(cancellationToken);
        metadata.Update = update;
        await SaveAsync(metadata, cancellationToken);
    }

    private Task SaveAsync(LocalMetadataDocument metadata, CancellationToken cancellationToken)
        => AtomicJsonFile.WriteAsync(_path, metadata, CliJsonContext.Default.LocalMetadataDocument, cancellationToken);
}

/// <summary>
/// Extracts trusted release archives after checksum verification.
/// </summary>
public sealed class UpdateArchiveExtractor
{
    /// <summary>Extracts a zip or tar.gz archive into a destination directory.</summary>
    public async Task ExtractAsync(string archivePath, string archiveType, string destinationDirectory, CancellationToken cancellationToken)
    {
        if (archiveType.Equals("zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZip(archivePath, destinationDirectory);
            return;
        }

        if (archiveType.Equals("tar.gz", StringComparison.OrdinalIgnoreCase) || archiveType.Equals("tgz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(archivePath, destinationDirectory, cancellationToken);
            return;
        }

        throw new CliException($"Unsupported update archive type '{archiveType}'.", ExitCodes.Validation, "update_archive_type_unsupported");
    }

    private static void ExtractZip(string archivePath, string destinationDirectory)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                string directoryPath = SafeCombine(destinationDirectory, entry.FullName);
                Directory.CreateDirectory(directoryPath);
                continue;
            }

            string destinationPath = SafeCombine(destinationDirectory, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationDirectory);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static async Task ExtractTarGzAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
    {
        await using FileStream archiveStream = File.OpenRead(archivePath);
        await using GZipStream gzipStream = new(archiveStream, CompressionMode.Decompress);
        using TarReader reader = new(gzipStream);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destinationPath = SafeCombine(destinationDirectory, entry.Name);
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            if (entry.DataStream is null)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationDirectory);
            await using FileStream output = File.Create(destinationPath);
            await entry.DataStream.CopyToAsync(output, cancellationToken);
        }
    }

    private static string SafeCombine(string rootDirectory, string relativePath)
    {
        string fullRoot = Path.GetFullPath(rootDirectory);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.Equals(fullRoot, StringComparison.Ordinal) && !fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new CliException("The update archive contains an unsafe path.", ExitCodes.Io, "update_archive_path_traversal");
        }

        return fullPath;
    }
}

/// <summary>
/// Replaces the installed executable, using a deferred helper on Windows when the file is running.
/// </summary>
public sealed class ExecutableReplacer
{
    /// <summary>Replaces or schedules replacement of the installed executable.</summary>
    public ExecutableReplacementResult Replace(string sourceExecutable, string targetExecutable)
    {
        string? targetDirectory = Path.GetDirectoryName(targetExecutable);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new CliException($"The update target path '{targetExecutable}' is invalid.", ExitCodes.Configuration, "update_target_invalid");
        }

        Directory.CreateDirectory(targetDirectory);
        if (OperatingSystem.IsWindows())
        {
            string stagedPath = System.IO.Path.Combine(targetDirectory, $"{System.IO.Path.GetFileName(targetExecutable)}.new");
            File.Copy(sourceExecutable, stagedPath, overwrite: true);
            ScheduleWindowsReplacement(stagedPath, targetExecutable);
            return new ExecutableReplacementResult(Scheduled: true);
        }

        string temporaryTarget = System.IO.Path.Combine(targetDirectory, $".{System.IO.Path.GetFileName(targetExecutable)}.{Guid.NewGuid():N}.tmp");
        File.Copy(sourceExecutable, temporaryTarget, overwrite: true);
        File.SetUnixFileMode(temporaryTarget, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Move(temporaryTarget, targetExecutable, overwrite: true);
        return new ExecutableReplacementResult(Scheduled: false);
    }

    private static void ScheduleWindowsReplacement(string stagedPath, string targetExecutable)
    {
        string scriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"azimg-update-{Guid.NewGuid():N}.cmd");
        string script = $"@echo off{Environment.NewLine}ping 127.0.0.1 -n 3 > nul{Environment.NewLine}move /Y \"{stagedPath}\" \"{targetExecutable}\" > nul{Environment.NewLine}del \"%~f0\"{Environment.NewLine}";
        File.WriteAllText(scriptPath, script);
        ProcessStartInfo startInfo = new("cmd.exe", $"/c \"{scriptPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(startInfo)?.Dispose();
    }
}

/// <summary>
/// Result of replacing the executable.
/// </summary>
/// <param name="Scheduled">Whether replacement was scheduled for after the current process exits.</param>
public sealed record ExecutableReplacementResult(bool Scheduled);

/// <summary>
/// Computes exact portable RIDs supported by the first release matrix.
/// </summary>
public static class RuntimeRidDetector
{
    /// <summary>Gets the current supported release RID.</summary>
    public static string GetCurrentRid()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                _ => throw UnsupportedArchitecture(),
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => throw UnsupportedArchitecture(),
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? "linux-x64"
                : throw UnsupportedPlatform("Only Linux x64 is supported by the first release.");
        }

        throw UnsupportedPlatform("This operating system is not supported by the release updater.");
    }

    /// <summary>Gets the executable file name for a RID.</summary>
    public static string GetExecutableFileName(string rid)
        => rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? "azimg.exe" : "azimg";

    private static CliException UnsupportedArchitecture()
        => UnsupportedPlatform($"Architecture '{RuntimeInformation.ProcessArchitecture}' is not supported by the release updater.");

    private static CliException UnsupportedPlatform(string message)
        => new(message, ExitCodes.Validation, "update_platform_unsupported");
}

/// <summary>
/// Provides the current application version from assembly metadata.
/// </summary>
public static class ApplicationVersion
{
    /// <summary>The current semantic version from the entry assembly.</summary>
    public static string Current { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
}

internal static class VersionUtility
{
    public static string NormalizeVersion(string value)
        => value.Trim().TrimStart('v', 'V');

    public static string EnsureTag(string versionOrTag)
    {
        string trimmed = versionOrTag.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? trimmed : $"v{trimmed}";
    }

    public static bool IsNewer(string candidateVersion, string currentVersion)
    {
        string normalizedCandidate = NormalizeVersion(candidateVersion);
        string normalizedCurrent = NormalizeVersion(currentVersion);
        if (Version.TryParse(normalizedCandidate, out Version? candidate)
            && Version.TryParse(normalizedCurrent, out Version? current))
        {
            return candidate > current;
        }

        return !normalizedCandidate.Equals(normalizedCurrent, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class UpdatePaths
{
    public static string GetDefaultDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = string.IsNullOrWhiteSpace(home) ? AppContext.BaseDirectory : home;
        return System.IO.Path.Combine(root, CliDefaults.ConfigDirectoryName);
    }
}

internal static class AtomicJsonFile
{
    public static async Task WriteAsync<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new CliException($"The path '{path}' is invalid.", ExitCodes.Configuration);
        }

        Directory.CreateDirectory(directory);
        string tempPath = System.IO.Path.Combine(directory, $"{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonDefaults.SerializeAsync(stream, value, typeInfo, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}

internal static class HashUtility
{
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
