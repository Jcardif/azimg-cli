using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.ImageOperations;

namespace AzImg.Cli.Tests.Diagnostics;

public class DoctorServiceTests
{
    [Fact]
    public async Task Doctor_AllowsInlineProfileWithoutConfigFile()
    {
        DoctorService doctorService = new(new AzureCliCredentialProvider());
        ResolvedProfile profile = new(
            "inline",
            "gpt-image-2",
            new Uri("https://example.openai.azure.com/"),
            Path.GetTempPath());

        DoctorReport report = await doctorService.RunAsync(
            "/tmp/missing-config.json",
            null,
            profile,
            verifyAuth: false,
            CancellationToken.None);

        Assert.True(report.IsHealthy);
        Assert.Contains(report.Checks, check => check.Name == "config-file" && check.Passed);
    }
}