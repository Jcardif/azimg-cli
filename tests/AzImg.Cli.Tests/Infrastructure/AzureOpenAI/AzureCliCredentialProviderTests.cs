using System.Diagnostics;
using Azure.Core;
using Azure.Identity;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Infrastructure.AzureOpenAI;

public class AzureCliCredentialProviderTests
{
    [Fact]
    public void CreateStartInfo_RedirectsProcessOutputToFilesWithoutPipes()
    {
        ProcessStartInfo startInfo = AzureCliProcessAccessTokenSource.CreateStartInfo(
            "https://cognitiveservices.azure.com",
            "/tmp/stdout.json",
            "/tmp/stderr.txt");

        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);
        string arguments = string.Join(" ", startInfo.ArgumentList);
        Assert.Contains("get-access-token", arguments, StringComparison.Ordinal);
        Assert.Contains("--only-show-errors", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWindowsCommand_UsesPowerShellEnvironmentRedirection()
    {
        string command = AzureCliProcessAccessTokenSource.CreateWindowsCommand();

        Assert.Contains("az account get-access-token", command, StringComparison.Ordinal);
        Assert.Contains("$env:AZIMG_RESOURCE", command, StringComparison.Ordinal);
        Assert.Contains("> $env:AZIMG_STDOUT 2> $env:AZIMG_STDERR", command, StringComparison.Ordinal);
        Assert.DoesNotContain("az.cmd", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrepareTokenRedirectionFiles_CreatesOwnerOnlyFilesOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string stdoutPath = Path.Combine(directory, "stdout.json");
        string stderrPath = Path.Combine(directory, "stderr.txt");

        try
        {
            AzureCliProcessAccessTokenSource.PrepareTokenRedirectionFiles(directory, stdoutPath, stderrPath);

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(stdoutPath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(stderrPath));
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
    public void ParseAccessTokenOutput_UsesUnixExpiration()
    {
        AzureCliAccessToken token = AzureCliProcessAccessTokenSource.ParseAccessTokenOutput(
            """
            {
              "accessToken": "token-value",
              "expires_on": 1893456000
            }
            """);

        Assert.Equal("token-value", token.Token);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000), token.ExpiresOn);
    }

    [Fact]
    public async Task GetTokenAsync_UsesAzureCliResourceAndCachesUsableToken()
    {
        FakeAccessTokenSource source = new();
        AzCliTokenCredential credential = new(source);
        TokenRequestContext requestContext = new([CliDefaults.AzureTokenScope]);

        AccessToken first = await credential.GetTokenAsync(requestContext, CancellationToken.None);
        AccessToken second = await credential.GetTokenAsync(requestContext, CancellationToken.None);

        Assert.Equal("token-1", first.Token);
        Assert.Equal("token-1", second.Token);
        Assert.Equal(1, source.Calls);
        Assert.Equal("https://cognitiveservices.azure.com", source.LastResource);
    }

    [Fact]
    public void GetAccessToken_FailedAzureCliSummarizesTracebackWithoutPrintingIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string binDirectory = Path.Combine(directory, "bin");
        string? originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Directory.CreateDirectory(binDirectory);
            WriteFailingAz(binDirectory);
            Environment.SetEnvironmentVariable("PATH", binDirectory + Path.PathSeparator + originalPath);

            AuthenticationFailedException exception = Assert.Throws<AuthenticationFailedException>(
                () => new AzureCliProcessAccessTokenSource().GetAccessToken("https://cognitiveservices.azure.com", CancellationToken.None));

            Assert.Contains("could not resolve login.microsoftonline.com", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Traceback", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("site-packages", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void WriteFailingAz(string binDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(
                Path.Combine(binDirectory, "az.cmd"),
                """
                @echo off
                echo ERROR: The command failed with an unexpected error. Here is the traceback: 1>&2
                echo Traceback ^(most recent call last^): 1>&2
                echo requests.exceptions.ConnectionError: HTTPSConnectionPool(host='login.microsoftonline.com', port=443): NameResolutionError 1>&2
                exit /b 1
                """);
            return;
        }

        string azPath = Path.Combine(binDirectory, "az");
        File.WriteAllText(
            azPath,
            """
            #!/bin/sh
            echo "ERROR: The command failed with an unexpected error. Here is the traceback:" >&2
            echo "Traceback (most recent call last):" >&2
            echo "requests.exceptions.ConnectionError: HTTPSConnectionPool(host='login.microsoftonline.com', port=443): NameResolutionError" >&2
            exit 1
            """);
        File.SetUnixFileMode(azPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private sealed class FakeAccessTokenSource : IAzureCliAccessTokenSource
    {
        public int Calls { get; private set; }

        public string? LastResource { get; private set; }

        public AzureCliAccessToken GetAccessToken(string resource, CancellationToken cancellationToken)
        {
            Calls++;
            LastResource = resource;
            return new AzureCliAccessToken($"token-{Calls}", DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
