using AzImg.Cli.Runtime;

namespace AzImg.Cli.Configuration;

/// <summary>
/// Loads, saves, and creates <c>azimg</c> configuration files.
/// </summary>
/// <remarks>
/// Writes are atomic within the target directory: the store writes a temporary file first, then moves it
/// over the final path. This keeps partially written config files from being observed by later commands.
/// </remarks>
public sealed class ConfigurationStore
{
    /// <summary>
    /// Gets the conventional per-user config path, normally <c>~/.azimg/config.json</c>.
    /// </summary>
    public string GetDefaultPath()
        => Path.Combine(GetHomeDirectory(), CliDefaults.ConfigDirectoryName, CliDefaults.ConfigFileName);

    /// <summary>
    /// Loads a config file from an explicit path or the default path.
    /// </summary>
    /// <param name="explicitPath">An optional path supplied by <c>--config</c> or <c>--path</c>.</param>
    /// <param name="cancellationToken">A token used to cancel file I/O.</param>
    /// <returns>The resolved path and parsed config, or <see langword="null" /> when the file does not exist.</returns>
    /// <exception cref="CliException">Thrown when a present config file is empty or invalid.</exception>
    public async Task<(string Path, AppConfig? Config)> LoadAsync(string? explicitPath, CancellationToken cancellationToken)
    {
        string path = ResolvePath(explicitPath);
        if (!File.Exists(path))
        {
            return (path, null);
        }

        await using FileStream stream = File.OpenRead(path);
        AppConfig? config = await JsonDefaults.DeserializeAsync(stream, CliJsonContext.Default.AppConfig, cancellationToken);
        if (config is null)
        {
            throw new CliException($"The configuration file '{path}' is empty or invalid.", ExitCodes.Configuration);
        }

        config.Profiles = new Dictionary<string, ProfileConfig>(config.Profiles, StringComparer.OrdinalIgnoreCase);
        return (path, config);
    }

    /// <summary>
    /// Writes a config file atomically, optionally refusing to overwrite an existing file.
    /// </summary>
    /// <param name="config">The config document to save.</param>
    /// <param name="explicitPath">An optional target path; the default path is used when omitted.</param>
    /// <param name="overwrite">Whether an existing config file may be replaced.</param>
    /// <param name="cancellationToken">A token used to cancel file I/O.</param>
    /// <exception cref="CliException">Thrown when the path is invalid or overwrite is disallowed.</exception>
    public async Task SaveAsync(AppConfig config, string? explicitPath, bool overwrite, CancellationToken cancellationToken)
    {
        string path = ResolvePath(explicitPath);
        if (!overwrite && File.Exists(path))
        {
            throw new CliException($"The configuration file '{path}' already exists. Use --force to overwrite it.", ExitCodes.Configuration);
        }

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new CliException($"The configuration path '{path}' is invalid.", ExitCodes.Configuration);
        }

        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonDefaults.SerializeAsync(stream, config, CliJsonContext.Default.AppConfig, cancellationToken);
        }

        File.Move(tempPath, path, true);
    }

    /// <summary>
    /// Creates the starter config written by <c>azimg config init</c>.
    /// </summary>
    public AppConfig CreateSampleConfig()
    {
        string home = GetHomeDirectory();

        return new AppConfig
        {
            DefaultProfile = "azure-default",
            Profiles = new Dictionary<string, ProfileConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["azure-default"] = new()
                {
                    Deployment = "gpt-image-2",
                    Endpoint = "https://your-resource.openai.azure.com/",
                    OutputDirectory = Path.Combine(home, CliDefaults.ConfigDirectoryName, "output"),
                },
            },
        };
    }

    private string ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        return GetDefaultPath();
    }

    private static string GetHomeDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? AppContext.BaseDirectory : home;
    }
}