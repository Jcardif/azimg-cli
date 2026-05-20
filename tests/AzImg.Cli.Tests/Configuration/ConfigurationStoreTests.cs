using AzImg.Cli.Configuration;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Configuration;

public class ConfigurationStoreTests
{
    [Fact]
    public void CreateSampleConfig_HasExpectedProfile()
    {
        ConfigurationStore store = new();

        AppConfig config = store.CreateSampleConfig();

        Assert.Equal("azure-default", config.DefaultProfile);
        Assert.Contains("azure-default", config.Profiles.Keys);
        Assert.Equal("gpt-image-2", config.Profiles["azure-default"].Deployment);
    }

    [Fact]
    public void GetDefaultPath_UsesHiddenFolderInHomeDirectory()
    {
        ConfigurationStore store = new();

        string path = store.GetDefaultPath();

        Assert.Contains($"{Path.DirectorySeparatorChar}.azimg{Path.DirectorySeparatorChar}config.json", path, StringComparison.Ordinal);
        Assert.EndsWith(CliDefaults.ConfigFileName, path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ReportsMalformedJsonAsConfigurationError()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "config.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{ not valid json");

        try
        {
            CliException exception = await Assert.ThrowsAsync<CliException>(() => new ConfigurationStore().LoadAsync(path, CancellationToken.None));

            Assert.Equal(ExitCodes.Configuration, exception.ExitCode);
            Assert.Contains("not valid JSON", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReportsMissingProfilesAsConfigurationError()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "config.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"profiles\":null}");

        try
        {
            CliException exception = await Assert.ThrowsAsync<CliException>(() => new ConfigurationStore().LoadAsync(path, CancellationToken.None));

            Assert.Equal(ExitCodes.Configuration, exception.ExitCode);
            Assert.Contains("profiles", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}