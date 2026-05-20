using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Commands;
using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Infrastructure.FileSystem;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Commands;

public class ConfigCommandTests
{
    [Fact]
    public async Task ConfigInit_CanEmitJson()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string configPath = Path.Combine(directory, "config.json");
        CommandDispatcher application = CreateApplication();
        using StringWriter writer = new();
        TextWriter originalOut = Console.Out;

        Console.SetOut(writer);
        try
        {
            int exitCode = await application.RunAsync(["config", "init", "--path", configPath, "--force"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        string output = writer.ToString();
        Assert.Contains("\"path\":", output, StringComparison.Ordinal);
        Assert.Contains("\"defaultProfile\": \"azure-default\"", output, StringComparison.Ordinal);
        Assert.Contains("\"profiles\":", output, StringComparison.Ordinal);
    }

    private static CommandDispatcher CreateApplication()
    {
        AzureCliCredentialProvider credentialProvider = new();
        return new CommandDispatcher(
            new ConfigurationStore(),
            new ProfileResolver(),
            new GeneratedImageRequestValidator(),
            new AzureOpenAIImageClient(credentialProvider),
            new ImageFileStore(),
            new DiagnosticService(credentialProvider),
            new HelpTextProvider());
    }
}