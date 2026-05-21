using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
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
    private const UnixFileMode TokenDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode TokenFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public AzureCliAccessToken GetAccessToken(string resource, CancellationToken cancellationToken)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"azimg-az-{Guid.NewGuid():N}");
        string stdoutPath = Path.Combine(tempDirectory, "stdout.json");
        string stderrPath = Path.Combine(tempDirectory, "stderr.txt");
        PrepareTokenRedirectionFiles(tempDirectory, stdoutPath, stderrPath);

        using Process process = new()
        {
            StartInfo = CreateStartInfo(resource, stdoutPath, stderrPath),
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
            string output = ReadFileIfExists(stdoutPath);
            string error = ReadFileIfExists(stderrPath);
            if (process.ExitCode != 0)
            {
                throw new AuthenticationFailedException(CreateFailureMessage(resource, error, output));
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
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string resource, string stdoutPath, string stderrPath)
    {
        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? CreateWindowsStartInfo(resource, stdoutPath, stderrPath)
            : CreateUnixStartInfo(resource, stdoutPath, stderrPath);

        startInfo.RedirectStandardOutput = false;
        startInfo.RedirectStandardError = false;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        return startInfo;
    }

    internal static void PrepareTokenRedirectionFiles(string tempDirectory, string stdoutPath, string stderrPath)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(tempDirectory);
            File.Create(stdoutPath).Dispose();
            File.Create(stderrPath).Dispose();
            return;
        }

        Directory.CreateDirectory(tempDirectory, TokenDirectoryMode);
        CreatePrivateTokenFile(stdoutPath);
        CreatePrivateTokenFile(stderrPath);
    }

    [UnsupportedOSPlatform("windows")]
    private static void CreatePrivateTokenFile(string path)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            UnixCreateMode = TokenFileMode,
        };
        using FileStream stream = new(path, options);
    }

    private static ProcessStartInfo CreateUnixStartInfo(string resource, string stdoutPath, string stderrPath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "/bin/sh",
        };

        startInfo.Environment["AZIMG_STDOUT"] = stdoutPath;
        startInfo.Environment["AZIMG_STDERR"] = stderrPath;
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("exec \"$@\" > \"$AZIMG_STDOUT\" 2> \"$AZIMG_STDERR\"");
        startInfo.ArgumentList.Add("azimg-az");
        startInfo.ArgumentList.Add("az");
        startInfo.ArgumentList.Add("account");
        startInfo.ArgumentList.Add("get-access-token");
        startInfo.ArgumentList.Add("--resource");
        startInfo.ArgumentList.Add(resource);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("--only-show-errors");
        return startInfo;
    }

    private static ProcessStartInfo CreateWindowsStartInfo(string resource, string stdoutPath, string stderrPath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(CreateWindowsCommand());
        startInfo.Environment["AZIMG_RESOURCE"] = resource;
        startInfo.Environment["AZIMG_STDOUT"] = stdoutPath;
        startInfo.Environment["AZIMG_STDERR"] = stderrPath;
        return startInfo;
    }

    internal static string CreateWindowsCommand()
        => "& az account get-access-token --resource $env:AZIMG_RESOURCE --output json --only-show-errors "
            + "> $env:AZIMG_STDOUT 2> $env:AZIMG_STDERR; exit $LASTEXITCODE";

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

    private static string CreateFailureMessage(string resource, string error, string output)
    {
        string detail = SummarizeFailure(error);
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = SummarizeFailure(output);
        }

        return string.IsNullOrWhiteSpace(detail)
            ? $"Azure CLI failed to acquire an Azure token for resource '{resource}'."
            : $"Azure CLI failed to acquire an Azure token for resource '{resource}'. {detail}";
    }

    private static string SummarizeFailure(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text.Trim();
        if (normalized.Contains("Failed to resolve 'login.microsoftonline.com'", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("NameResolutionError", StringComparison.OrdinalIgnoreCase))
        {
            return "Azure CLI could not resolve login.microsoftonline.com. Check DNS or network connectivity and try again.";
        }

        string[] lines = normalized.Split(
            ["\r\n", "\n"],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string line = lines[index];
            if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                return Truncate(line["ERROR:".Length..].Trim());
            }
        }

        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string line = lines[index];
            if (!line.StartsWith("Traceback ", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("File ", StringComparison.Ordinal)
                && !line.StartsWith("~", StringComparison.Ordinal)
                && !line.StartsWith("^", StringComparison.Ordinal))
            {
                return Truncate(line);
            }
        }

        return string.Empty;
    }

    private static string Truncate(string value)
        => value.Length <= 500 ? value : value[..500] + "...";

    private static string ReadFileIfExists(string path)
        => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
