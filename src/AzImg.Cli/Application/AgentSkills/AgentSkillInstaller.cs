using System.Net.Http.Headers;
using System.Text;
using AzImg.Cli.Runtime;
using AzImg.Cli.Updates;

namespace AzImg.Cli.Application.AgentSkills;

/// <summary>
/// Options for installing the AzImg agent skill from GitHub raw content.
/// </summary>
/// <param name="InstallDirectory">The root agent skills directory, or <see langword="null" /> for <c>~/.agents/skills</c>.</param>
/// <param name="SourceRef">The Git branch, tag, or commit SHA used to build the default raw GitHub URL.</param>
/// <param name="SourceUrl">An explicit absolute URL for <c>SKILL.md</c>, overriding <paramref name="SourceRef" />.</param>
/// <param name="DryRun">Whether to report intended work without downloading or writing files.</param>
/// <param name="Force">Whether to overwrite an existing different <c>SKILL.md</c>.</param>
public sealed record AgentSkillInstallOptions(
    string? InstallDirectory,
    string? SourceRef,
    string? SourceUrl,
    bool DryRun,
    bool Force);

/// <summary>
/// Result of an AzImg agent skill installation attempt.
/// </summary>
public sealed record AgentSkillInstallResult(
    string SkillName,
    string SourceUrl,
    string TargetPath,
    string InstallDirectory,
    string? SourceRef,
    bool DryRun,
    bool Installed,
    bool AlreadyInstalled,
    bool Overwritten,
    string Message);

/// <summary>
/// Installs the AzImg agent skill into a local agent skills directory.
/// </summary>
public interface IAgentSkillInstaller
{
    /// <summary>Installs or previews installation of the AzImg agent skill.</summary>
    Task<AgentSkillInstallResult> InstallAsync(AgentSkillInstallOptions options, TextWriter diagnostics, CancellationToken cancellationToken);
}

/// <summary>
/// Placeholder installer used by tests that do not exercise <c>install-skill</c>.
/// </summary>
public sealed class NoOpAgentSkillInstaller : IAgentSkillInstaller
{
    /// <summary>A reusable no-op instance.</summary>
    public static NoOpAgentSkillInstaller Instance { get; } = new();

    private NoOpAgentSkillInstaller()
    {
    }

    /// <inheritdoc />
    public Task<AgentSkillInstallResult> InstallAsync(AgentSkillInstallOptions options, TextWriter diagnostics, CancellationToken cancellationToken)
        => Task.FromException<AgentSkillInstallResult>(new CliException("No agent skill installer is configured.", ExitCodes.Configuration, "skill_installer_not_configured"));
}

/// <summary>
/// Downloads <c>skills/azimg/SKILL.md</c> from GitHub and writes it to <c>~/.agents/skills/azimg/SKILL.md</c>.
/// </summary>
public sealed class AgentSkillInstaller : IAgentSkillInstaller
{
    private readonly HttpClient _httpClient;

    /// <summary>Initializes an installer with the default HTTP client and timeout.</summary>
    public AgentSkillInstaller()
        : this(CreateDefaultHttpClient())
    {
    }

    /// <summary>Initializes an installer with a caller-supplied HTTP client.</summary>
    public AgentSkillInstaller(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<AgentSkillInstallResult> InstallAsync(AgentSkillInstallOptions options, TextWriter diagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        string installDirectory = ResolveInstallDirectory(options.InstallDirectory);
        string? sourceRef = ResolveSourceRef(options.SourceRef, options.SourceUrl);
        Uri sourceUri = ResolveSourceUri(sourceRef, options.SourceUrl);
        string targetDirectory = Path.Combine(installDirectory, CliDefaults.AgentSkillName);
        string targetPath = Path.Combine(targetDirectory, CliDefaults.AgentSkillFileName);

        if (options.DryRun)
        {
            diagnostics.WriteLine($"Would download {CliDefaults.AgentSkillName} skill from {sourceUri}.");
            diagnostics.WriteLine($"Would save {CliDefaults.AgentSkillName} skill to {targetPath}.");
            return CreateResult(
                sourceUri,
                targetPath,
                installDirectory,
                sourceRef,
                options,
                installed: false,
                alreadyInstalled: false,
                overwritten: false,
                $"Would install {CliDefaults.AgentSkillName} skill to {targetPath} from {sourceUri}.");
        }

        diagnostics.WriteLine($"Downloading {CliDefaults.AgentSkillName} skill from {sourceUri}.");
        string skillContent = await DownloadSkillAsync(sourceUri, cancellationToken);
        diagnostics.WriteLine($"Saving {CliDefaults.AgentSkillName} skill to {targetPath}.");

        try
        {
            if (File.Exists(targetPath))
            {
                string existingContent = await File.ReadAllTextAsync(targetPath, cancellationToken);
                if (existingContent.Equals(skillContent, StringComparison.Ordinal))
                {
                    return CreateResult(
                        sourceUri,
                        targetPath,
                        installDirectory,
                        sourceRef,
                        options,
                        installed: false,
                        alreadyInstalled: true,
                        overwritten: false,
                        $"{CliDefaults.AgentSkillName} skill is already installed at {targetPath}.");
                }

                if (!options.Force)
                {
                    throw new CliException(
                        $"{CliDefaults.AgentSkillName} skill already exists at '{targetPath}' with different content. Re-run with --force to overwrite it.",
                        ExitCodes.Validation,
                        "skill_install_conflict");
                }
            }

            Directory.CreateDirectory(targetDirectory);
            bool overwritten = File.Exists(targetPath);
            await WriteSkillFileAsync(targetPath, skillContent, cancellationToken);
            return CreateResult(
                sourceUri,
                targetPath,
                installDirectory,
                sourceRef,
                options,
                installed: true,
                alreadyInstalled: false,
                overwritten,
                overwritten
                    ? $"Overwrote {CliDefaults.AgentSkillName} skill at {targetPath}."
                    : $"Installed {CliDefaults.AgentSkillName} skill to {targetPath}.");
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliException($"Unable to save {CliDefaults.AgentSkillName} skill to '{targetPath}'. {ex.Message}", ExitCodes.Io, "skill_install_write_failed");
        }
    }

    private static AgentSkillInstallResult CreateResult(
        Uri sourceUri,
        string targetPath,
        string installDirectory,
        string? sourceRef,
        AgentSkillInstallOptions options,
        bool installed,
        bool alreadyInstalled,
        bool overwritten,
        string message)
        => new(
            CliDefaults.AgentSkillName,
            sourceUri.ToString(),
            targetPath,
            installDirectory,
            sourceRef,
            options.DryRun,
            installed,
            alreadyInstalled,
            overwritten,
            message);

    private async Task<string> DownloadSkillAsync(Uri sourceUri, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string hint = sourceUri.ToString().Contains($"/{GetDefaultSourceRef()}/", StringComparison.Ordinal)
                    ? " If this build does not have a matching GitHub tag yet, re-run with --ref main."
                    : string.Empty;
                throw new CliException(
                    $"Unable to download {CliDefaults.AgentSkillName} skill from '{sourceUri}' ({(int)response.StatusCode}).{hint}",
                    ExitCodes.Io,
                    "skill_download_failed");
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new CliException($"The downloaded {CliDefaults.AgentSkillName} skill from '{sourceUri}' was empty.", ExitCodes.Io, "skill_download_empty");
            }

            return content;
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new CliException($"Unable to download {CliDefaults.AgentSkillName} skill from '{sourceUri}'. {ex.Message}", ExitCodes.Io, "skill_download_failed");
        }
    }

    private static async Task WriteSkillFileAsync(string targetPath, string content, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? throw new CliException($"The target path '{targetPath}' is invalid.", ExitCodes.Validation, "skill_install_target_invalid");
        string temporaryPath = Path.Combine(directory, $".{CliDefaults.AgentSkillFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static string ResolveInstallDirectory(string? installDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return Path.Combine(CliPath.GetHomeDirectory(), CliDefaults.AgentsDirectoryName, CliDefaults.AgentSkillsDirectoryName);
            }

            return CliPath.GetFullPath(installDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CliException($"The install directory '{installDirectory}' is invalid. {ex.Message}", ExitCodes.Validation, "skill_install_directory_invalid");
        }
    }

    private static string? ResolveSourceRef(string? sourceRef, string? sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(sourceRef))
        {
            return GetDefaultSourceRef();
        }

        string trimmed = sourceRef.Trim();
        if (trimmed.Length == 0)
        {
            throw new CliException("--ref must not be empty.", ExitCodes.Validation, "skill_source_ref_invalid");
        }

        return trimmed;
    }

    private static Uri ResolveSourceUri(string? sourceRef, string? sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            if (Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out Uri? explicitUri)
                && (explicitUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    || explicitUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            {
                return explicitUri;
            }

            throw new CliException("--source-url must be an absolute HTTP or HTTPS URL.", ExitCodes.Validation, "skill_source_url_invalid");
        }

        string resolvedRef = sourceRef ?? GetDefaultSourceRef();
        return new Uri($"{CliDefaults.GitHubRawBaseUrl}/{Uri.EscapeDataString(resolvedRef)}/{CliDefaults.AgentSkillRepositoryPath}");
    }

    private static string GetDefaultSourceRef()
        => $"v{ApplicationVersion.Current.Trim().TrimStart('v', 'V')}";

    private static HttpClient CreateDefaultHttpClient()
    {
        HttpClient httpClient = new()
        {
            Timeout = CliDefaults.AgentSkillDownloadTimeout,
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(CliDefaults.UserAgentApplicationId, ApplicationVersion.Current));
        return httpClient;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}