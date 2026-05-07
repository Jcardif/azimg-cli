using AzureOpenAI.ImageGen.Cli.Infrastructure;
using AzureOpenAI.ImageGen.Cli.Models;

namespace AzureOpenAI.ImageGen.Cli.Services;

/// <summary>
/// Loads, saves, and creates CLI configuration files.
/// </summary>
public sealed class ConfigStore
{
    /// <summary>
    /// Gets the conventional per-user config path, normally <c>~/.azimg/config.json</c>.
    /// </summary>
    public string GetDefaultPath()
        => Path.Combine(GetHomeDirectory(), CliDefaults.ConfigDirectoryName, CliDefaults.ConfigFileName);

    /// <summary>
    /// Loads a config file from an explicit path or the default path.
    /// </summary>
    /// <returns>The resolved path and the parsed config, or <see langword="null" /> when the file does not exist.</returns>
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
