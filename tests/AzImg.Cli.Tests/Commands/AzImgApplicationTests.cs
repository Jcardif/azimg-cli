using AzImg.Cli.Commands;
using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.ImageArtifacts;
using AzImg.Cli.ImageOperations;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Commands;

public class AzImgApplicationTests
{
    [Fact]
    public async Task Version_PrintsRenamedProduct()
    {
        AzImgApplication application = CreateApplication();
        using StringWriter writer = new();
        TextWriter originalOut = Console.Out;

        Console.SetOut(writer);
        try
        {
            int exitCode = await application.RunAsync(["version", "--format", "text"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.StartsWith($"{CliDefaults.ProductName} ", writer.ToString().Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_CanEmitJson()
    {
        AzImgApplication application = CreateApplication();
        using StringWriter writer = new();
        TextWriter originalOut = Console.Out;

        Console.SetOut(writer);
        try
        {
            int exitCode = await application.RunAsync(["version"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        string output = writer.ToString();
        Assert.Contains("\"commandName\": \"azimg\"", output, StringComparison.Ordinal);
        Assert.Contains("\"product\":", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootHelp_UsesRenamedCommandName()
    {
        AzImgApplication application = CreateApplication();
        using StringWriter writer = new();
        TextWriter originalOut = Console.Out;

        Console.SetOut(writer);
        try
        {
            int exitCode = await application.RunAsync(["--help"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        string output = writer.ToString();
        Assert.Contains($"Usage: {CliDefaults.CommandName} <command> [arguments]", output, StringComparison.Ordinal);
        Assert.Contains($"Run '{CliDefaults.CommandName} <command> --help' for more information about a command.", output, StringComparison.Ordinal);
    }

    private static AzImgApplication CreateApplication()
    {
        AzureCliCredentialProvider credentialProvider = new();
        return new AzImgApplication(
            new ConfigurationStore(),
            new ProfileResolver(),
            new ImageOperationRequestValidator(),
            new AzureOpenAIImageClient(credentialProvider),
            new ImageArtifactWriter(),
            new DoctorService(credentialProvider),
            new HelpTextProvider());
    }
}