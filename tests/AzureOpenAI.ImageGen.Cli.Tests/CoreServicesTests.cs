using AzureOpenAI.ImageGen.Cli.Infrastructure;
using AzureOpenAI.ImageGen.Cli.Models;
using AzureOpenAI.ImageGen.Cli.Services;

namespace AzureOpenAI.ImageGen.Cli.Tests;

public class CoreServicesTests
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

    [Theory]
    [InlineData("1024x1024")]
    [InlineData("1536x1024")]
    public void NormalizeSize_AcceptsSupportedDimensions(string value)
    {
        RequestValidator validator = new();

        string? normalized = validator.NormalizeSize(value);

        Assert.Equal(value, normalized);
    }

    [Fact]
    public void NormalizeSize_RejectsNonDivisibleSizes()
    {
        RequestValidator validator = new();

        CliException exception = Assert.Throws<CliException>(() => validator.NormalizeSize("1000x1000"));

        Assert.Equal(ExitCodes.Validation, exception.ExitCode);
    }

    [Fact]
    public void ValidateGenerate_RejectsZeroCount()
    {
        RequestValidator validator = new();
        GenerateImageRequest request = new(
            "prompt",
            0,
            "1024x1024",
            "high",
            "auto",
            "png",
            100,
            null,
            "{id}-{index}",
            false);

        CliException exception = Assert.Throws<CliException>(() => validator.ValidateGenerate(request));
        Assert.Equal(ExitCodes.Validation, exception.ExitCode);
    }

    [Fact]
    public void CreateSampleConfig_HasExpectedProfile()
    {
        ConfigStore store = new();

        AppConfig config = store.CreateSampleConfig();

        Assert.Equal("azure-default", config.DefaultProfile);
        Assert.Contains("azure-default", config.Profiles.Keys);
        Assert.Equal("gpt-image-2", config.Profiles["azure-default"].Deployment);
    }

    [Fact]
    public void Translate_AzureImageGenerationRoleError_ReturnsFriendlyCliException()
    {
        CliException exception = ServiceErrorTranslator.TranslateHttpFailure(
            401,
            "{\"error\":{\"message\":\"The principal user@example.com lacks the required data action 'Microsoft.CognitiveServices/accounts/OpenAI/images/generations/action' to perform the request.\"}}");

        Assert.Equal(ExitCodes.Authentication, exception.ExitCode);
        Assert.Contains("Cognitive Services OpenAI User", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDefaultPath_UsesHiddenFolderInHomeDirectory()
    {
        ConfigStore store = new();

        string path = store.GetDefaultPath();

        Assert.Contains($"{Path.DirectorySeparatorChar}.azimg{Path.DirectorySeparatorChar}config.json", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_PrintsRenamedProduct()
    {
        CliApplication application = CreateApplication();
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

        Assert.StartsWith($"{CliDefaults.ProductName} ", writer.ToString().Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootHelp_UsesRenamedCommandName()
    {
        CliApplication application = CreateApplication();
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

    [Fact]
    public async Task Doctor_AllowsInlineProfileWithoutConfigFile()
    {
        DoctorService doctorService = new(new AzureCredentialProvider());
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

    [Fact]
    public async Task SaveAsync_UsesShortDefaultNamesForLongPrompts()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        ResolvedProfile profile = new(
            "azure-default",
            "gpt-image-2",
            new Uri("https://example.openai.azure.com/"),
            outputDirectory);
        ImageOperationResult result = new(
            [new GeneratedImageArtifact(1, [1, 2, 3], "png")],
            null,
            new DateTimeOffset(2026, 4, 28, 19, 13, 32, TimeSpan.Zero),
            "gpt-image-2");

        try
        {
            SaveImagesResult saveResult = await new FileOutputService().SaveAsync(
                profile,
                new string('x', 4000),
                string.Empty,
                writeManifest: true,
                result,
                CancellationToken.None);

            string imageFileName = Path.GetFileName(saveResult.Files[0].Path);
            Assert.Matches(@"^[0-9a-f]{8}-01\.png$", imageFileName);
            Assert.NotNull(saveResult.ManifestPath);
            Assert.Matches(@"^[0-9a-f]{8}-00\.manifest\.json$", Path.GetFileName(saveResult.ManifestPath!));
            Assert.True(File.Exists(saveResult.Files[0].Path));
            Assert.True(File.Exists(saveResult.ManifestPath!));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static CliApplication CreateApplication()
    {
        AzureCredentialProvider credentialProvider = new();
        return new CliApplication(
            new ConfigStore(),
            new ProfileResolver(),
            new RequestValidator(),
            new AzureImageService(credentialProvider),
            new FileOutputService(),
            new DoctorService(credentialProvider));
    }
}
