using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Infrastructure.AzureOpenAI;

/// <summary>
/// Provides Azure CLI credentials for image calls and explicit doctor authentication checks.
/// </summary>
/// <remarks>
/// The CLI intentionally uses the Azure CLI sign-in instead of the broader <see cref="DefaultAzureCredential" />
/// chain. This preserves deterministic behavior: AzImg uses the account prepared by <c>az login</c> and does not
/// silently switch to environment, managed identity, IDE, PowerShell, or other developer-tool credentials that may
/// exist on the same machine.
/// </remarks>
public sealed class AzureCliCredentialProvider
{
    private static readonly TokenRequestContext TokenRequestContext = new([CliDefaults.AzureTokenScope]);

    private readonly TokenCredential _credential;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureCliCredentialProvider" /> class.
    /// </summary>
    public AzureCliCredentialProvider()
        : this(new AzCliTokenCredential(new AzureCliProcessAccessTokenSource()))
    {
    }

    internal AzureCliCredentialProvider(TokenCredential credential)
    {
        _credential = credential;
    }

    /// <summary>The Azure CLI credential used by Azure SDK clients.</summary>
    public TokenCredential Credential => _credential;

    /// <summary>
    /// Acquires an Azure Cognitive Services bearer token from the current Azure CLI sign-in.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel credential acquisition.</param>
    /// <returns>The non-empty bearer token value.</returns>
    /// <exception cref="CliException">Thrown when Azure CLI auth is unavailable or fails.</exception>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            AccessToken token = await _credential.GetTokenAsync(TokenRequestContext, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token.Token))
            {
                throw new CliException(
                    "Azure CLI returned an empty Azure access token. Run 'az login' and try again.",
                    ExitCodes.Authentication);
            }

            return token.Token;
        }
        catch (CredentialUnavailableException)
        {
            throw new CliException(
                "Azure CLI authentication is unavailable. Install Azure CLI, run 'az login', and try again.",
                ExitCodes.Authentication);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new CliException(
                $"Azure CLI failed to get an Azure token. Run 'az login' and try again. {ex.Message}",
                ExitCodes.Authentication);
        }
    }
}

internal sealed class AzCliTokenCredential : TokenCredential
{
    private static readonly TimeSpan RefreshOffset = TimeSpan.FromMinutes(5);

    private readonly IAzureCliAccessTokenSource _tokenSource;
    private readonly object _cacheLock = new();
    private AccessToken? _cachedToken;
    private string? _cachedResource;

    public AzCliTokenCredential(IAzureCliAccessTokenSource tokenSource)
    {
        _tokenSource = tokenSource;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => GetTokenCore(requestContext, cancellationToken);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(GetTokenCore(requestContext, cancellationToken));

    private AccessToken GetTokenCore(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string resource = ResolveResource(requestContext);
        AccessToken? cachedToken = GetCachedToken(resource);
        if (cachedToken is not null)
        {
            return cachedToken.Value;
        }

        AzureCliAccessToken cliToken = _tokenSource.GetAccessToken(resource, cancellationToken);
        if (string.IsNullOrWhiteSpace(cliToken.Token))
        {
            throw new AuthenticationFailedException("Azure CLI returned an empty Azure access token.");
        }

        AccessToken accessToken = new(cliToken.Token, cliToken.ExpiresOn);
        lock (_cacheLock)
        {
            _cachedResource = resource;
            _cachedToken = accessToken;
        }

        return accessToken;
    }

    private AccessToken? GetCachedToken(string resource)
    {
        lock (_cacheLock)
        {
            if (string.Equals(_cachedResource, resource, StringComparison.Ordinal)
                && _cachedToken is AccessToken cachedToken
                && cachedToken.ExpiresOn > DateTimeOffset.UtcNow.Add(RefreshOffset))
            {
                return cachedToken;
            }
        }

        return null;
    }

    private static string ResolveResource(TokenRequestContext requestContext)
    {
        if (requestContext.Scopes.Length != 1 || string.IsNullOrWhiteSpace(requestContext.Scopes[0]))
        {
            throw new AuthenticationFailedException("Azure CLI token acquisition requires exactly one non-empty Azure resource scope.");
        }

        const string defaultScopeSuffix = "/.default";
        string scope = requestContext.Scopes[0].Trim();
        return scope.EndsWith(defaultScopeSuffix, StringComparison.OrdinalIgnoreCase)
            ? scope[..^defaultScopeSuffix.Length]
            : scope;
    }
}

internal interface IAzureCliAccessTokenSource
{
    AzureCliAccessToken GetAccessToken(string resource, CancellationToken cancellationToken);
}

internal sealed record AzureCliAccessToken(string Token, DateTimeOffset ExpiresOn);

internal sealed class AzureCliProcessAccessTokenSource : IAzureCliAccessTokenSource
{
    private const int ProcessWaitMilliseconds = 100;

    public AzureCliAccessToken GetAccessToken(string resource, CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = CreateStartInfo(resource),
        };

        try
        {
            if (!process.Start())
            {
                throw new CredentialUnavailableException("Azure CLI authentication is unavailable because the 'az' process could not be started.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new CredentialUnavailableException("Azure CLI authentication is unavailable. Install Azure CLI, run 'az login', and try again.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new CredentialUnavailableException("Azure CLI authentication is unavailable because the 'az' process could not be started.", ex);
        }

        try
        {
            WaitForExit(process, cancellationToken);
            string output = process.StandardOutput.ReadToEnd();
            if (process.ExitCode != 0)
            {
                throw new AuthenticationFailedException($"Azure CLI failed to acquire an Azure token for resource '{resource}'.");
            }

            return ParseAccessTokenOutput(output);
        }
        catch (OperationCanceledException)
        {
            KillIfRunning(process);
            throw;
        }
        catch (IOException ex)
        {
            throw new AuthenticationFailedException("Azure CLI token output could not be read.", ex);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string resource)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = OperatingSystem.IsWindows() ? "az.cmd" : "az",
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("account");
        startInfo.ArgumentList.Add("get-access-token");
        startInfo.ArgumentList.Add("--resource");
        startInfo.ArgumentList.Add(resource);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("--only-show-errors");
        return startInfo;
    }

    internal static AzureCliAccessToken ParseAccessTokenOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new AuthenticationFailedException("Azure CLI returned no token output.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("accessToken", out JsonElement tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                throw new AuthenticationFailedException("Azure CLI token output did not include an accessToken value.");
            }

            return new AzureCliAccessToken(tokenElement.GetString()!, ReadExpiresOn(root));
        }
        catch (JsonException ex)
        {
            throw new AuthenticationFailedException("Azure CLI returned malformed token output.", ex);
        }
    }

    private static DateTimeOffset ReadExpiresOn(JsonElement root)
    {
        if (root.TryGetProperty("expires_on", out JsonElement unixExpiryElement))
        {
            if (unixExpiryElement.ValueKind == JsonValueKind.Number && unixExpiryElement.TryGetInt64(out long unixExpiry))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixExpiry);
            }

            if (unixExpiryElement.ValueKind == JsonValueKind.String
                && long.TryParse(unixExpiryElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out unixExpiry))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixExpiry);
            }
        }

        if (root.TryGetProperty("expiresOn", out JsonElement expiryElement)
            && expiryElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                expiryElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                out DateTimeOffset expiresOn))
        {
            return expiresOn.ToUniversalTime();
        }

        throw new AuthenticationFailedException("Azure CLI token output did not include a parseable token expiration.");
    }

    private static void WaitForExit(Process process, CancellationToken cancellationToken)
    {
        while (!process.WaitForExit(ProcessWaitMilliseconds))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
