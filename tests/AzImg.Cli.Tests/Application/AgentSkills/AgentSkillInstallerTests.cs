using System.Net;
using AzImg.Cli.Application.AgentSkills;
using AzImg.Cli.Runtime;
using AzImg.Cli.Updates;

namespace AzImg.Cli.Tests.Application.AgentSkills;

public class AgentSkillInstallerTests
{
    private const string SkillContent = "---\nname: azimg\n---\n# AzImg CLI\n";

    [Fact]
    public async Task InstallAsync_DryRunDoesNotDownloadOrWrite()
    {
        string installDirectory = CreateTempDirectory();
        FakeHttpMessageHandler handler = new(_ => CreateResponse(SkillContent));
        using HttpClient httpClient = CreateHttpClient(handler);
        AgentSkillInstaller installer = new(httpClient);
        using StringWriter diagnostics = new();

        try
        {
            AgentSkillInstallResult result = await installer.InstallAsync(
                new AgentSkillInstallOptions(installDirectory, "main", null, DryRun: true, Force: false),
                diagnostics,
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(Path.Combine(installDirectory, CliDefaults.AgentSkillName, CliDefaults.AgentSkillFileName)));
            Assert.False(result.Installed);
            Assert.True(result.DryRun);
            Assert.Equal("main", result.SourceRef);
            Assert.Contains("/main/skills/azimg/SKILL.md", result.SourceUrl, StringComparison.Ordinal);
            Assert.Contains("Would download", diagnostics.ToString(), StringComparison.Ordinal);
            Assert.Contains("Would save", diagnostics.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(installDirectory);
        }
    }

    [Fact]
    public async Task InstallAsync_DownloadsAndWritesSkillFile()
    {
        string installDirectory = CreateTempDirectory();
        FakeHttpMessageHandler handler = new(_ => CreateResponse(SkillContent));
        using HttpClient httpClient = CreateHttpClient(handler);
        AgentSkillInstaller installer = new(httpClient);

        try
        {
            AgentSkillInstallResult result = await installer.InstallAsync(
                new AgentSkillInstallOptions(installDirectory, null, "https://example.invalid/SKILL.md", DryRun: false, Force: false),
                TextWriter.Null,
                CancellationToken.None);

            string targetPath = Path.Combine(installDirectory, CliDefaults.AgentSkillName, CliDefaults.AgentSkillFileName);
            Assert.Single(handler.Requests);
            Assert.Equal("https://example.invalid/SKILL.md", handler.Requests[0].ToString());
            Assert.Equal(SkillContent, await File.ReadAllTextAsync(targetPath));
            Assert.True(result.Installed);
            Assert.False(result.AlreadyInstalled);
            Assert.False(result.Overwritten);
            Assert.Equal(targetPath, result.TargetPath);
        }
        finally
        {
            DeleteDirectory(installDirectory);
        }
    }

    [Fact]
    public async Task InstallAsync_ExistingIdenticalContentReturnsAlreadyInstalled()
    {
        string installDirectory = CreateTempDirectory();
        string targetPath = Path.Combine(installDirectory, CliDefaults.AgentSkillName, CliDefaults.AgentSkillFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? installDirectory);
        await File.WriteAllTextAsync(targetPath, SkillContent);
        FakeHttpMessageHandler handler = new(_ => CreateResponse(SkillContent));
        using HttpClient httpClient = CreateHttpClient(handler);
        AgentSkillInstaller installer = new(httpClient);

        try
        {
            AgentSkillInstallResult result = await installer.InstallAsync(
                new AgentSkillInstallOptions(installDirectory, null, "https://example.invalid/SKILL.md", DryRun: false, Force: false),
                TextWriter.Null,
                CancellationToken.None);

            Assert.False(result.Installed);
            Assert.True(result.AlreadyInstalled);
            Assert.False(result.Overwritten);
            Assert.Equal(SkillContent, await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            DeleteDirectory(installDirectory);
        }
    }

    [Fact]
    public async Task InstallAsync_ExistingDifferentContentRequiresForce()
    {
        string installDirectory = CreateTempDirectory();
        string targetPath = Path.Combine(installDirectory, CliDefaults.AgentSkillName, CliDefaults.AgentSkillFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? installDirectory);
        await File.WriteAllTextAsync(targetPath, "old skill");
        FakeHttpMessageHandler handler = new(_ => CreateResponse(SkillContent));
        using HttpClient httpClient = CreateHttpClient(handler);
        AgentSkillInstaller installer = new(httpClient);

        try
        {
            CliException exception = await Assert.ThrowsAsync<CliException>(() => installer.InstallAsync(
                new AgentSkillInstallOptions(installDirectory, null, "https://example.invalid/SKILL.md", DryRun: false, Force: false),
                TextWriter.Null,
                CancellationToken.None));

            Assert.Equal(ExitCodes.Validation, exception.ExitCode);
            Assert.Equal("skill_install_conflict", exception.ErrorCode);
            Assert.Equal("old skill", await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            DeleteDirectory(installDirectory);
        }
    }

    [Fact]
    public async Task InstallAsync_ForceOverwritesDifferentContent()
    {
        string installDirectory = CreateTempDirectory();
        string targetPath = Path.Combine(installDirectory, CliDefaults.AgentSkillName, CliDefaults.AgentSkillFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? installDirectory);
        await File.WriteAllTextAsync(targetPath, "old skill");
        FakeHttpMessageHandler handler = new(_ => CreateResponse(SkillContent));
        using HttpClient httpClient = CreateHttpClient(handler);
        AgentSkillInstaller installer = new(httpClient);

        try
        {
            AgentSkillInstallResult result = await installer.InstallAsync(
                new AgentSkillInstallOptions(installDirectory, null, "https://example.invalid/SKILL.md", DryRun: false, Force: true),
                TextWriter.Null,
                CancellationToken.None);

            Assert.True(result.Installed);
            Assert.True(result.Overwritten);
            Assert.Equal(SkillContent, await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            DeleteDirectory(installDirectory);
        }
    }

    [Fact]
    public async Task InstallAsync_DefaultSourceUrlUsesCurrentVersionTag()
    {
        string installDirectory = CreateTempDirectory();
        FakeHttpMessageHandler handler = new(_ => CreateResponse(SkillContent));
        using HttpClient httpClient = CreateHttpClient(handler);
        AgentSkillInstaller installer = new(httpClient);
        string expectedRef = $"v{ApplicationVersion.Current.Trim().TrimStart('v', 'V')}";

        try
        {
            AgentSkillInstallResult result = await installer.InstallAsync(
                new AgentSkillInstallOptions(installDirectory, null, null, DryRun: true, Force: false),
                TextWriter.Null,
                CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.Equal(expectedRef, result.SourceRef);
            Assert.Contains($"/{expectedRef}/skills/azimg/SKILL.md", result.SourceUrl, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(installDirectory);
        }
    }

    [Fact]
    public async Task InstallAsync_InvalidSourceUrlFailsValidation()
    {
        string installDirectory = CreateTempDirectory();
        FakeHttpMessageHandler handler = new(_ => CreateResponse(SkillContent));
        using HttpClient httpClient = CreateHttpClient(handler);
        AgentSkillInstaller installer = new(httpClient);

        try
        {
            CliException exception = await Assert.ThrowsAsync<CliException>(() => installer.InstallAsync(
                new AgentSkillInstallOptions(installDirectory, null, "file:///tmp/SKILL.md", DryRun: true, Force: false),
                TextWriter.Null,
                CancellationToken.None));

            Assert.Equal(ExitCodes.Validation, exception.ExitCode);
            Assert.Equal("skill_source_url_invalid", exception.ErrorCode);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            DeleteDirectory(installDirectory);
        }
    }

    [Fact]
    public async Task InstallAsync_HttpFailureFailsAsIo()
    {
        string installDirectory = CreateTempDirectory();
        FakeHttpMessageHandler handler = new(_ => CreateResponse("not found", HttpStatusCode.NotFound));
        using HttpClient httpClient = CreateHttpClient(handler);
        AgentSkillInstaller installer = new(httpClient);

        try
        {
            CliException exception = await Assert.ThrowsAsync<CliException>(() => installer.InstallAsync(
                new AgentSkillInstallOptions(installDirectory, null, "https://example.invalid/SKILL.md", DryRun: false, Force: false),
                TextWriter.Null,
                CancellationToken.None));

            Assert.Equal(ExitCodes.Io, exception.ExitCode);
            Assert.Equal("skill_download_failed", exception.ErrorCode);
            Assert.Single(handler.Requests);
        }
        finally
        {
            DeleteDirectory(installDirectory);
        }
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
        => new(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

    private static HttpResponseMessage CreateResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(content),
        };

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"azimg-skill-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                Requests.Add(request.RequestUri);
            }

            return Task.FromResult(_responseFactory(request));
        }
    }
}