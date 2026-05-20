using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Commands;
using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Infrastructure.FileSystem;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Commands;

public class CommandDispatcherTests
{
    [Fact]
    public async Task Version_PrintsRenamedProduct()
    {
        CommandDispatcher application = CreateApplication();
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
        CommandDispatcher application = CreateApplication();
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
        CommandDispatcher application = CreateApplication();
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