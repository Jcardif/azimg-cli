using System.Diagnostics;
using Azure.Core;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Infrastructure.AzureOpenAI;

public class AzureCliCredentialProviderTests
{
    [Fact]
    public void CreateStartInfo_RedirectsOnlyStandardOutput()
    {
        ProcessStartInfo startInfo = AzureCliProcessAccessTokenSource.CreateStartInfo("https://cognitiveservices.azure.com");

        Assert.Equal(OperatingSystem.IsWindows() ? "az.cmd" : "az", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);
        Assert.Contains("get-access-token", startInfo.ArgumentList);
        Assert.Contains("--only-show-errors", startInfo.ArgumentList);
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
