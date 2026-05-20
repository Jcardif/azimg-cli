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
}