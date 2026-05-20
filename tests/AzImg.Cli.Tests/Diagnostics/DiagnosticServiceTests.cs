using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.Infrastructure.AzureOpenAI;

namespace AzImg.Cli.Tests.Diagnostics;

public class DiagnosticServiceTests
{
    [Fact]
    public async Task Doctor_AllowsInlineProfileWithoutConfigFile()
    {
        DiagnosticService diagnosticService = new(new AzureCliCredentialProvider());
        ResolvedProfile profile = new(
            "inline",
            "gpt-image-2",
            new Uri("https://example.openai.azure.com/"),
            Path.GetTempPath());

        DiagnosticReport report = await diagnosticService.RunAsync(
            "/tmp/missing-config.json",
            null,
            profile,
            verifyAuth: false,
            CancellationToken.None);

        Assert.True(report.IsHealthy);
        Assert.Contains(report.Checks, check => check.Name == "config-file" && check.Passed);
    }
}