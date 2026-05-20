using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Commands;
using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Infrastructure.FileSystem;
using AzImg.Cli.Runtime;
using AzImg.Cli.Updates;

namespace AzImg.Cli.Tests.Commands;

public class UpdateCommandTests
{
    [Fact]
    public async Task UpdateCheck_CanEmitJson()
    {
        FakeUpdateService updateService = new();
        CommandDispatcher application = CreateApplication(updateService);
        using StringWriter writer = new();
        TextWriter originalOut = Console.Out;

        Console.SetOut(writer);
        try
        {
            int exitCode = await application.RunAsync(["update", "check"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(1, updateService.CheckCalls);
        Assert.Equal(0, updateService.ApplyCalls);
        string output = writer.ToString();
        Assert.Contains("\"updateAvailable\": true", output, StringComparison.Ordinal);
        Assert.Contains("\"latestVersion\": \"9.9.9\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateDefaultAction_AppliesUpdate()
    {
        FakeUpdateService updateService = new();
        CommandDispatcher application = CreateApplication(updateService);
        using StringWriter writer = new();
        TextWriter originalOut = Console.Out;

        Console.SetOut(writer);
        try
        {
            int exitCode = await application.RunAsync(["update", "--dry-run", "--format", "text"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, updateService.CheckCalls);
        Assert.Equal(1, updateService.ApplyCalls);
        Assert.True(updateService.LastApplyOptions?.DryRun);
        Assert.Contains("Would update", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_RejectsPositionalActionWhenActionOptionIsSupplied()
    {
        FakeUpdateService updateService = new();
        CommandDispatcher application = CreateApplication(updateService);

        CliException exception = await Assert.ThrowsAsync<CliException>(() => application.RunAsync(["update", "check", "--action", "apply"], CancellationToken.None));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("action positional", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, updateService.CheckCalls);
        Assert.Equal(0, updateService.ApplyCalls);
    }

    [Fact]
    public async Task FirstLaunchCheck_RunsBeforeNormalCommandsWithoutStdoutPollution()
    {
        FakeUpdateService updateService = new()
        {
            FirstLaunchNotice = "Update available on stderr.",
        };
        CommandDispatcher application = CreateApplication(updateService);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int exitCode = await application.RunAsync(["version", "--format", "text"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(1, updateService.FirstLaunchCalls);
        Assert.StartsWith($"{CliDefaults.ProductName} ", stdout.ToString().Trim(), StringComparison.Ordinal);
        Assert.Contains("Update available", stderr.ToString(), StringComparison.Ordinal);
    }

    private static CommandDispatcher CreateApplication(IUpdateService updateService)
    {
        AzureCliCredentialProvider credentialProvider = new();
        return new CommandDispatcher(
            new ConfigurationStore(),
            new ProfileResolver(),
            new GeneratedImageRequestValidator(),
            new AzureOpenAIImageClient(credentialProvider),
            new ImageFileStore(),
            new DiagnosticService(credentialProvider),
            new HelpTextProvider(),
            updateService);
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public int FirstLaunchCalls { get; private set; }

        public int CheckCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public string? FirstLaunchNotice { get; init; }

        public UpdateCommandOptions? LastApplyOptions { get; private set; }

        public Task NotifyIfFirstLaunchAsync(IReadOnlyList<string> rawArgs, TextWriter diagnostics, CancellationToken cancellationToken)
        {
            if (rawArgs.Count > 0 && rawArgs[0].Equals("update", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            FirstLaunchCalls++;
            if (!string.IsNullOrWhiteSpace(FirstLaunchNotice))
            {
                diagnostics.WriteLine(FirstLaunchNotice);
            }

            return Task.CompletedTask;
        }

        public Task<UpdateCheckDocument> CheckAsync(UpdateCommandOptions options, CancellationToken cancellationToken)
        {
            CheckCalls++;
            return Task.FromResult(new UpdateCheckDocument(
                CliDefaults.ProductName,
                CliDefaults.CommandName,
                "0.1.0",
                "9.9.9",
                UpdateAvailable: true,
                "linux-x64",
                "https://example.invalid/azimg-release.json",
                DateTimeOffset.UnixEpoch,
                new ReleaseAssetDocument("linux-x64", "azimg-linux-x64.tar.gz", new string('a', 64), 123, "tar.gz", "linux", "x64")));
        }

        public Task<UpdateApplyDocument> ApplyAsync(UpdateCommandOptions options, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            LastApplyOptions = options;
            return Task.FromResult(new UpdateApplyDocument(
                CliDefaults.ProductName,
                CliDefaults.CommandName,
                "0.1.0",
                "9.9.9",
                UpdateAvailable: true,
                options.DryRun,
                Updated: false,
                UpdateScheduled: false,
                "linux-x64",
                "/tmp/azimg",
                "https://example.invalid/azimg-release.json",
                new ReleaseAssetDocument("linux-x64", "azimg-linux-x64.tar.gz", new string('a', 64), 123, "tar.gz", "linux", "x64"),
                options.DryRun ? "Would update azimg." : "Updated azimg."));
        }
    }
}
