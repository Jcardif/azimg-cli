using AzImg.Cli.Configuration;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Configuration;

public class ProfileResolverTests
{
    [Fact]
    public void Resolve_UsesConfigProfileAndOverrides()
    {
        AppConfig config = new()
        {
            DefaultProfile = "azure-default",
            Profiles = new Dictionary<string, ProfileConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["azure-default"] = new()
                {
                    Deployment = "gpt-image-2",
                    Endpoint = "https://example.openai.azure.com/",
                    OutputDirectory = "/tmp/default-output",
                },
            },
        };

        ProfileResolver resolver = new();
        ResolvedProfile resolved = resolver.Resolve(
            config,
            new ProfileOverrides("azure-default", "override-deployment", null, "/tmp/override-output"));

        Assert.Equal("override-deployment", resolved.DeploymentName);
        Assert.Equal(new Uri("https://example.openai.azure.com/"), resolved.Endpoint);
        Assert.Equal(CliPath.GetFullPath("/tmp/override-output"), resolved.OutputDirectory);
    }

    [Fact]
    public void Resolve_IgnoresLegacyProfileOutputDirectory()
    {
        string profileOutputDirectory = Path.Combine(CliPath.GetHomeDirectory(), ".azimg", "output");
        AppConfig config = new()
        {
            DefaultProfile = "azure-default",
            Profiles = new Dictionary<string, ProfileConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["azure-default"] = new()
                {
                    Deployment = "gpt-image-2",
                    Endpoint = "https://example.openai.azure.com/",
                    OutputDirectory = profileOutputDirectory,
                },
            },
        };

        ResolvedProfile resolved = new ProfileResolver().Resolve(config, new ProfileOverrides(null, null, null, null));

        Assert.NotEqual(profileOutputDirectory, resolved.OutputDirectory);
        Assert.Equal(Path.Combine(Environment.CurrentDirectory, CliDefaults.DefaultOutputDirectoryName), resolved.OutputDirectory);
    }

    [Fact]
    public void Resolve_UsesCurrentDirectoryOutputFolderByDefault()
    {
        ProfileResolver resolver = new();

        ResolvedProfile resolved = resolver.Resolve(
            null,
            new ProfileOverrides(null, "gpt-image-2", "https://example.openai.azure.com/", null));

        Assert.Equal(Path.Combine(Environment.CurrentDirectory, CliDefaults.DefaultOutputDirectoryName), resolved.OutputDirectory);
    }

    [Fact]
    public void Resolve_RejectsNonHttpsEndpoint()
    {
        ProfileResolver resolver = new();

        CliException exception = Assert.Throws<CliException>(() => resolver.Resolve(
            null,
            new ProfileOverrides(null, "gpt-image-2", "http://example.openai.azure.com/", null)));

        Assert.Equal(ExitCodes.Configuration, exception.ExitCode);
        Assert.Contains("https", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
