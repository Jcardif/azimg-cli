namespace AzImg.Cli.Runtime;

/// <summary>
/// Standard process exit codes used by the <c>azimg</c> command.
/// </summary>
/// <remarks>
/// The numeric values are intentionally stable because scripts and agents can use
/// them to distinguish usage errors, validation failures, configuration problems,
/// authentication failures, local I/O failures, cancellation, and unexpected faults.
/// </remarks>
public static class ExitCodes
{
    /// <summary>The command completed successfully.</summary>
    public const int Success = 0;

    /// <summary>The command line was malformed or requested an unsupported command.</summary>
    public const int Usage = 1;

    /// <summary>User input was understood but failed validation.</summary>
    public const int Validation = 2;

    /// <summary>Configuration was missing, incomplete, or inconsistent.</summary>
    public const int Configuration = 3;

    /// <summary>Azure authentication or authorization failed.</summary>
    public const int Authentication = 4;

    /// <summary>Local file or directory operations failed.</summary>
    public const int Io = 5;

    /// <summary>The user cancelled the command, commonly with Ctrl+C.</summary>
    public const int Cancelled = 130;

    /// <summary>The command failed with an unexpected error.</summary>
    public const int Unhandled = 255;
}

/// <summary>
/// Runtime defaults shared by command parsing, Azure SDK calls, file storage, and help text.
/// </summary>
public static class CliDefaults
{
    /// <summary>The product name shown in version and help output.</summary>
    public const string ProductName = "AzImg CLI";

    /// <summary>The executable name users and automation invoke.</summary>
    public const string CommandName = "azimg";

    /// <summary>The application identifier sent in Azure SDK user-agent metadata.</summary>
    public const string UserAgentApplicationId = "azimg";

    /// <summary>The hidden directory under the user profile that stores config and metadata.</summary>
    public const string ConfigDirectoryName = ".azimg";

    /// <summary>The relative output directory used when no command-specific output path is supplied.</summary>
    public const string DefaultOutputDirectoryName = "azimg-output";

    /// <summary>The configuration file name inside <see cref="ConfigDirectoryName" />.</summary>
    public const string ConfigFileName = "config.json";

    /// <summary>The local install and update metadata file name inside <see cref="ConfigDirectoryName" />.</summary>
    public const string LocalMetadataFileName = "metadata.json";

    /// <summary>The GitHub repository used for release discovery.</summary>
    public const string ReleaseRepository = "Jcardif/azimg-cli";

    /// <summary>The base GitHub Releases URL for the CLI repository.</summary>
    public const string GitHubReleasesBaseUrl = "https://github.com/Jcardif/azimg-cli/releases";

    /// <summary>The release manifest file uploaded beside platform archives.</summary>
    public const string ReleaseManifestFileName = "azimg-release.json";

    /// <summary>The latest-release manifest URL consumed by installers and self-update.</summary>
    public const string LatestReleaseManifestUrl = "https://github.com/Jcardif/azimg-cli/releases/latest/download/azimg-release.json";

    /// <summary>The GitHub raw-content base URL for the CLI repository.</summary>
    public const string GitHubRawBaseUrl = "https://raw.githubusercontent.com/Jcardif/azimg-cli";

    /// <summary>The agent skill installed by <c>azimg install-skill</c>.</summary>
    public const string AgentSkillName = "azimg";

    /// <summary>The bundled AzImg agent skill version.</summary>
    public const string AgentSkillVersion = "0.2.1";

    /// <summary>The top-level hidden directory that stores agent customizations.</summary>
    public const string AgentsDirectoryName = ".agents";

    /// <summary>The subdirectory under <see cref="AgentsDirectoryName" /> that stores skills.</summary>
    public const string AgentSkillsDirectoryName = "skills";

    /// <summary>The skill definition file name installed for the AzImg agent skill.</summary>
    public const string AgentSkillFileName = "SKILL.md";

    /// <summary>The repository-relative path to the AzImg agent skill definition.</summary>
    public const string AgentSkillRepositoryPath = "skills/azimg/SKILL.md";

    /// <summary>The Azure Cognitive Services token scope used for Azure OpenAI requests.</summary>
    public const string AzureTokenScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>The network timeout for long-running image generation and image download operations.</summary>
    public static TimeSpan ImageRequestTimeout { get; } = TimeSpan.FromMinutes(20);

    /// <summary>The short timeout used by best-effort update/version checks.</summary>
    public static TimeSpan UpdateCheckTimeout { get; } = TimeSpan.FromSeconds(5);

    /// <summary>The longer timeout used when downloading a release archive during explicit updates.</summary>
    public static TimeSpan UpdateDownloadTimeout { get; } = TimeSpan.FromMinutes(5);

    /// <summary>The timeout used when downloading the small agent skill markdown file.</summary>
    public static TimeSpan AgentSkillDownloadTimeout { get; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Exception type used for expected CLI failures that should map directly to an exit code.
/// </summary>
public sealed class CliException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CliException" /> class.
    /// </summary>
    /// <param name="message">The human-readable failure message.</param>
    /// <param name="exitCode">The process exit code that should be returned.</param>
    /// <param name="errorCode">An optional stable, machine-readable error code.</param>
    public CliException(string message, int exitCode, string? errorCode = null)
        : base(message)
    {
        ExitCode = exitCode;
        ErrorCode = errorCode ?? GetDefaultErrorCode(exitCode);
    }

    /// <summary>The process exit code associated with this failure.</summary>
    public int ExitCode { get; }

    /// <summary>A stable, machine-readable error code used in JSON error output.</summary>
    public string ErrorCode { get; }

    private static string GetDefaultErrorCode(int exitCode)
        => exitCode switch
        {
            ExitCodes.Usage => "usage",
            ExitCodes.Validation => "validation",
            ExitCodes.Configuration => "configuration",
            ExitCodes.Authentication => "authentication",
            ExitCodes.Io => "io",
            ExitCodes.Cancelled => "cancelled",
            _ => "unhandled",
        };
}

/// <summary>
/// JSON envelope written to stderr when a command fails while JSON output is active.
/// </summary>
/// <param name="Error">Structured information about the failed command.</param>
public sealed record CliErrorDocument(CliErrorInfo Error);

/// <summary>
/// Machine-readable details for one CLI error.
/// </summary>
/// <param name="Code">A stable error category such as <c>validation</c> or <c>authentication</c>.</param>
/// <param name="Message">The human-readable error message.</param>
/// <param name="ExitCode">The process exit code that accompanies the error.</param>
public sealed record CliErrorInfo(string Code, string Message, int ExitCode);

/// <summary>
/// Expands user-facing path shorthand before paths are normalized by the file system.
/// </summary>
public static class CliPath
{
    /// <summary>
    /// Expands <c>~</c>, <c>~/...</c>, and <c>~\...</c> to the current user's home directory.
    /// </summary>
    public static string ExpandUserHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        string trimmed = path.Trim();
        if (trimmed == "~")
        {
            return GetHomeDirectory();
        }

        if (trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(GetHomeDirectory(), trimmed[2..]);
        }

        return path;
    }

    /// <summary>Expands supported shorthand and returns a full path.</summary>
    public static string GetFullPath(string path)
        => Path.GetFullPath(ExpandUserHome(path));

    /// <summary>Gets the current user's home directory or the application base directory as a fallback.</summary>
    public static string GetHomeDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? AppContext.BaseDirectory : home;
    }
}
