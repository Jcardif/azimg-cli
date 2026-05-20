using Azure.Core;
using Azure.Identity;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Infrastructure.AzureOpenAI;

/// <summary>
/// Provides Azure CLI credentials for image calls and explicit doctor authentication checks.
/// </summary>
/// <remarks>
/// The CLI intentionally uses <see cref="AzureCliCredential" /> instead of the broader
/// <see cref="DefaultAzureCredential" /> chain. This preserves deterministic behavior: AzImg uses the
/// account prepared by <c>az login</c> and does not silently switch to environment, managed identity,
/// IDE, PowerShell, or other developer-tool credentials that may exist on the same machine.
/// </remarks>
public sealed class AzureCliCredentialProvider
{
    private static readonly TokenRequestContext TokenRequestContext = new([CliDefaults.AzureTokenScope]);

    private readonly AzureCliCredential _credential = new();

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
                    "AzureCliCredential returned an empty Azure access token. Run 'az login' and try again.",
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
                $"AzureCliCredential failed to get an Azure token. Run 'az login' and try again. {ex.Message}",
                ExitCodes.Authentication);
        }
    }
}