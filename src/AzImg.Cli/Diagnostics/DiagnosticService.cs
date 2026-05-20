using AzImg.Cli.Configuration;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Diagnostics;

/// <summary>
/// Runs local diagnostics for config, output directory, selected Azure settings, and optional authentication.
/// </summary>
/// <remarks>
/// The default doctor workflow is local and non-interactive. It only touches Azure credentials when the
/// caller explicitly passes <c>--verify-auth</c>, which keeps routine agent preflight checks safe in CI and
/// unattended shells.
/// </remarks>
public sealed class DiagnosticService
{
    private readonly AzureCliCredentialProvider _credentialProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticService" /> class.
    /// </summary>
    /// <param name="credentialProvider">The credential provider used only for explicit auth verification.</param>
    public DiagnosticService(AzureCliCredentialProvider credentialProvider)
    {
        _credentialProvider = credentialProvider;
    }

    /// <summary>
    /// Builds a diagnostic report for the selected profile and optionally verifies Azure token acquisition.
    /// </summary>
    /// <param name="configPath">The resolved configuration path.</param>
    /// <param name="config">The loaded configuration, or <see langword="null" /> for inline operation.</param>
    /// <param name="profile">The resolved profile to check.</param>
    /// <param name="verifyAuth">Whether to acquire an Azure bearer token.</param>
    /// <param name="cancellationToken">A token used to cancel auth verification.</param>
    /// <returns>The diagnostic report.</returns>
    public async Task<DiagnosticReport> RunAsync(
        string configPath,
        AppConfig? config,
        ResolvedProfile profile,
        bool verifyAuth,
        CancellationToken cancellationToken)
    {
        bool usesInlineProfile = config is null && string.Equals(profile.Name, "inline", StringComparison.OrdinalIgnoreCase);
        List<DiagnosticCheck> checks =
        [
            new(
                "config-file",
                config is not null || usesInlineProfile,
                config is not null
                    ? "Configuration file loaded."
                    : "No configuration file found; using inline or override settings."),
            new("output-directory", CanPrepareDirectory(profile.OutputDirectory), $"Output directory: {profile.OutputDirectory}"),
            new("deployment", !string.IsNullOrWhiteSpace(profile.DeploymentName), $"Deployment: {profile.DeploymentName}"),
            new("azure-endpoint", true, profile.Endpoint.ToString()),
        ];

        if (!verifyAuth)
        {
            checks.Add(new DiagnosticCheck(
                "azure-identity",
                true,
                "This CLI uses AzureCliCredential. Run with --verify-auth to test the current Azure CLI sign-in."));
        }
        else
        {
            try
            {
                string token = await _credentialProvider.GetAccessTokenAsync(cancellationToken);
                checks.Add(new DiagnosticCheck("azure-identity", !string.IsNullOrWhiteSpace(token), "Successfully acquired an Azure bearer token."));
            }
            catch (CliException ex)
            {
                checks.Add(new DiagnosticCheck("azure-identity", false, ex.Message));
            }
        }

        return new DiagnosticReport(configPath, profile.Name, checks);
    }

    private static bool CanPrepareDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}