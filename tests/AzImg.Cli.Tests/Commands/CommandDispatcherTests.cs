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

    [Theory]
    [InlineData("--format", "xml", "Format must be one of")]
    [InlineData("--json", null, "JSON is the default")]
    [InlineData("--cout", "2", "Unknown option")]
    public async Task Generate_InvalidCommandLine_FailsBeforeCallingImageClient(string option, string? value, string expectedMessage)
    {
        FakeGeneratedImageClient imageClient = new();
        CommandDispatcher application = CreateApplication(imageClient);
        string[] args = value is null
            ? ["generate", "prompt", option]
            : ["generate", "prompt", option, value];

        CliException exception = await Assert.ThrowsAsync<CliException>(() => application.RunAsync(args, CancellationToken.None));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, imageClient.GenerateCalls);
    }

    [Fact]
    public async Task Generate_AllowsFlagsBeforePromptAndWritesUniqueFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string missingConfigPath = Path.Combine(directory, "missing-config.json");
        FakeGeneratedImageClient imageClient = new();
        CommandDispatcher application = CreateApplication(imageClient);
        using StringWriter writer = new();
        using StringWriter stderr = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        Console.SetOut(writer);
        Console.SetError(stderr);
        try
        {
            int exitCode = await application.RunAsync(
                [
                    "generate",
                    "--config", missingConfigPath,
                    "--write-manifest",
                    "--deployment", "gpt-image-2",
                    "--endpoint", "https://example.openai.azure.com/",
                    "--output-directory", directory,
                    "--count", "2",
                    "--name-template", "fixed-name",
                    "A quoted prompt"
                ],
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        try
        {
            Assert.Equal(1, imageClient.GenerateCalls);
            Assert.NotNull(imageClient.LastGenerateRequest);
            Assert.True(imageClient.LastGenerateRequest.WriteManifest);
            Assert.Equal("A quoted prompt", imageClient.LastGenerateRequest.Prompt);

            string output = writer.ToString();
            Assert.Contains("\"files\":", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Generating", output, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(directory, "fixed-name.png")));
            Assert.True(File.Exists(Path.Combine(directory, "fixed-name-01.png")));
            Assert.True(File.Exists(Path.Combine(directory, "fixed-name.manifest.json")));

            string diagnosticOutput = stderr.ToString();
            Assert.Contains("Generating 2 images", diagnosticOutput, StringComparison.Ordinal);
            Assert.Contains("Generation response received", diagnosticOutput, StringComparison.Ordinal);
            Assert.Contains("Generation complete", diagnosticOutput, StringComparison.Ordinal);
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
    public async Task Generate_RejectsExtraPositionalsBeforeCallingImageClient()
    {
        FakeGeneratedImageClient imageClient = new();
        CommandDispatcher application = CreateApplication(imageClient);

        CliException exception = await Assert.ThrowsAsync<CliException>(() => application.RunAsync(["generate", "two", "words"], CancellationToken.None));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Equal(0, imageClient.GenerateCalls);
    }

    [Fact]
    public async Task Edit_WritesProgressToStderrWithoutPollutingJsonOutput()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string inputFile = Path.Combine(directory, "input.png");
        string missingConfigPath = Path.Combine(directory, "missing-config.json");
        FakeGeneratedImageClient imageClient = new();
        CommandDispatcher application = CreateApplication(imageClient);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(inputFile, [137, 80, 78, 71]);
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int exitCode = await application.RunAsync(
                [
                    "edit",
                    "--config", missingConfigPath,
                    "--deployment", "gpt-image-2",
                    "--endpoint", "https://example.openai.azure.com/",
                    "--output-directory", directory,
                    "--count", "1",
                    "--name-template", "edited",
                    inputFile,
                    "Make it blue"
                ],
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        try
        {
            Assert.Equal(1, imageClient.EditCalls);
            Assert.NotNull(imageClient.LastEditRequest);
            Assert.Equal("Make it blue", imageClient.LastEditRequest.Prompt);

            string output = stdout.ToString();
            Assert.Contains("\"files\":", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Editing", output, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(directory, "edited.png")));

            string diagnosticOutput = stderr.ToString();
            Assert.Contains("Editing 1 image", diagnosticOutput, StringComparison.Ordinal);
            Assert.Contains("Edit response received", diagnosticOutput, StringComparison.Ordinal);
            Assert.Contains("Edit complete", diagnosticOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static CommandDispatcher CreateApplication(IGeneratedImageClient? imageClient = null)
    {
        AzureCliCredentialProvider credentialProvider = new();
        return new CommandDispatcher(
            new ConfigurationStore(),
            new ProfileResolver(),
            new GeneratedImageRequestValidator(),
            imageClient ?? new AzureOpenAIImageClient(credentialProvider),
            new ImageFileStore(),
            new DiagnosticService(credentialProvider),
            new HelpTextProvider());
    }

    private sealed class FakeGeneratedImageClient : IGeneratedImageClient
    {
        public int GenerateCalls { get; private set; }

        public int EditCalls { get; private set; }

        public GenerateImageRequest? LastGenerateRequest { get; private set; }

        public EditImageRequest? LastEditRequest { get; private set; }

        public Task<GeneratedImageResult> GenerateAsync(ResolvedProfile profile, GenerateImageRequest request, CancellationToken cancellationToken)
        {
            GenerateCalls++;
            LastGenerateRequest = request;
            return Task.FromResult(CreateResult(profile, request.Count));
        }

        public Task<GeneratedImageResult> EditAsync(ResolvedProfile profile, EditImageRequest request, CancellationToken cancellationToken)
        {
            EditCalls++;
            LastEditRequest = request;
            return Task.FromResult(CreateResult(profile, request.Count));
        }

        private static GeneratedImageResult CreateResult(ResolvedProfile profile, int count)
        {
            List<GeneratedImageContent> images = [];
            for (int index = 1; index <= count; index++)
            {
                images.Add(new GeneratedImageContent(index, [(byte)index, (byte)(index + 1), (byte)(index + 2)], "png"));
            }

            return new GeneratedImageResult(images, null, DateTimeOffset.UnixEpoch, profile.DeploymentName);
        }
    }
}