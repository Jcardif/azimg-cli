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

    [Fact]
    public async Task ConfigInit_AcceptsOptionalAzureProfileValues()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string configPath = Path.Combine(directory, "config.json");
        CommandDispatcher application = CreateApplication();

        try
        {
            int exitCode = await application.RunAsync(
                [
                    "config",
                    "init",
                    "--path", configPath,
                    "--force",
                    "--profile", "fabric",
                    "--deployment", "gpt-image-2",
                    "--endpoint", "https://example.openai.azure.com/",
                    "--format", "text"
                ],
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);

            ConfigurationStore store = new();
            (string loadedPath, AppConfig? config) = await store.LoadAsync(configPath, CancellationToken.None);

            Assert.Equal(configPath, loadedPath);
            Assert.NotNull(config);
            Assert.Equal("fabric", config.DefaultProfile);
            ProfileConfig profile = Assert.Single(config.Profiles).Value;
            Assert.Equal("gpt-image-2", profile.Deployment);
            Assert.Equal("https://example.openai.azure.com/", profile.Endpoint);
            Assert.Null(profile.OutputDirectory);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConfigInit_RejectsInvalidEndpointOption()
    {
        CommandDispatcher application = CreateApplication();

        CliException exception = await Assert.ThrowsAsync<CliException>(
            () => application.RunAsync(["config", "init", "--endpoint", "http://example.openai.azure.com/"], CancellationToken.None));

        Assert.Equal(ExitCodes.Validation, exception.ExitCode);
        Assert.Contains("must use https", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigInit_RejectsOutputDirectoryProfileOption()
    {
        CommandDispatcher application = CreateApplication();

        CliException exception = await Assert.ThrowsAsync<CliException>(
            () => application.RunAsync(["config", "init", "--output-directory", "./azimg-output"], CancellationToken.None));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("Unknown option '--output-directory'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Config_RejectsPositionalActionWhenActionOptionIsSupplied()
    {
        CommandDispatcher application = CreateApplication();

        CliException exception = await Assert.ThrowsAsync<CliException>(() => application.RunAsync(["config", "show", "--action", "init"], CancellationToken.None));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("action positional", exception.Message, StringComparison.Ordinal);
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
