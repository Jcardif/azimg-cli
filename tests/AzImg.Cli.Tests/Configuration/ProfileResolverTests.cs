using AzImg.Cli.Configuration;

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
        Assert.Equal("/tmp/override-output", resolved.OutputDirectory);
    }
}