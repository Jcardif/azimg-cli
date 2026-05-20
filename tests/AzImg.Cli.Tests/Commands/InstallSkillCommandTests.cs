using AzImg.Cli.Application.AgentSkills;
using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Commands;
using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Infrastructure.FileSystem;
using AzImg.Cli.Runtime;
using AzImg.Cli.Updates;

namespace AzImg.Cli.Tests.Commands;

public class InstallSkillCommandTests
{
    [Fact]
    public async Task InstallSkill_DefaultsToJsonAndCallsInstaller()
    {
        FakeAgentSkillInstaller installer = new();
        CommandDispatcher application = CreateApplication(installer);
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int exitCode = await application.RunAsync(["install-skill"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(1, installer.Calls);
        Assert.NotNull(installer.LastOptions);
        Assert.Null(installer.LastOptions.InstallDirectory);
        Assert.Null(installer.LastOptions.SourceRef);
        Assert.Null(installer.LastOptions.SourceUrl);
        Assert.False(installer.LastOptions.DryRun);
        Assert.False(installer.LastOptions.Force);
        Assert.Contains("fake install-skill diagnostics", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"skillName\": \"azimg\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"sourceUrl\": \"https://example.invalid/SKILL.md\"", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallSkill_TextOutputIncludesSourceAndTarget()
    {
        FakeAgentSkillInstaller installer = new();
        CommandDispatcher application = CreateApplication(installer);
        string installDirectory = Path.Combine(Path.GetTempPath(), $"azimg-command-test-{Guid.NewGuid():N}");
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int exitCode = await application.RunAsync(
                [
                    "install-skill",
                    "--install-dir", installDirectory,
                    "--ref", "main",
                    "--source-url", "https://example.invalid/custom/SKILL.md",
                    "--dry-run",
                    "--force",
                    "--format", "text",
                ],
                CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(1, installer.Calls);
        Assert.NotNull(installer.LastOptions);
        Assert.Equal(installDirectory, installer.LastOptions.InstallDirectory);
        Assert.Equal("main", installer.LastOptions.SourceRef);
        Assert.Equal("https://example.invalid/custom/SKILL.md", installer.LastOptions.SourceUrl);
        Assert.True(installer.LastOptions.DryRun);
        Assert.True(installer.LastOptions.Force);
        string output = stdout.ToString();
        Assert.Contains("Installed azimg skill.", output, StringComparison.Ordinal);
        Assert.Contains("source: https://example.invalid/custom/SKILL.md", output, StringComparison.Ordinal);
        Assert.Contains("target: ", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallSkill_RejectsPositionalArguments()
    {
        FakeAgentSkillInstaller installer = new();
        CommandDispatcher application = CreateApplication(installer);

        CliException exception = await Assert.ThrowsAsync<CliException>(() => application.RunAsync(["install-skill", "azimg"], CancellationToken.None));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("does not accept positional", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, installer.Calls);
    }

    [Fact]
    public async Task InstallSkill_HelpDoesNotCallInstaller()
    {
        FakeAgentSkillInstaller installer = new();
        CommandDispatcher application = CreateApplication(installer);
        using StringWriter stdout = new();
        TextWriter originalOut = Console.Out;

        Console.SetOut(stdout);
        try
        {
            int exitCode = await application.RunAsync(["install-skill", "--help"], CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, installer.Calls);
        string output = stdout.ToString();
        Assert.Contains("Usage: azimg install-skill [options]", output, StringComparison.Ordinal);
        Assert.Contains("--source-url <url>", output, StringComparison.Ordinal);
    }

    private static CommandDispatcher CreateApplication(IAgentSkillInstaller agentSkillInstaller)
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
            NoOpUpdateService.Instance,
            agentSkillInstaller);
    }

    private sealed class FakeAgentSkillInstaller : IAgentSkillInstaller
    {
        public int Calls { get; private set; }

        public AgentSkillInstallOptions? LastOptions { get; private set; }

        public Task<AgentSkillInstallResult> InstallAsync(AgentSkillInstallOptions options, TextWriter diagnostics, CancellationToken cancellationToken)
        {
            Calls++;
            LastOptions = options;
            diagnostics.WriteLine("fake install-skill diagnostics");
            string installDirectory = options.InstallDirectory ?? Path.Combine(Path.GetTempPath(), "azimg-command-test-skills");
            string sourceUrl = options.SourceUrl ?? "https://example.invalid/SKILL.md";
            string targetPath = Path.Combine(installDirectory, CliDefaults.AgentSkillName, CliDefaults.AgentSkillFileName);
            return Task.FromResult(new AgentSkillInstallResult(
                CliDefaults.AgentSkillName,
                sourceUrl,
                targetPath,
                installDirectory,
                options.SourceUrl is null ? options.SourceRef : null,
                options.DryRun,
                Installed: true,
                AlreadyInstalled: false,
                Overwritten: options.Force,
                "Installed azimg skill."));
        }
    }
}