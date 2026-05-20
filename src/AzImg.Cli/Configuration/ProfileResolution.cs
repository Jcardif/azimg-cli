using AzImg.Cli.Runtime;

namespace AzImg.Cli.Configuration;

/// <summary>
/// Command-line values that can override the selected profile for one invocation.
/// </summary>
/// <param name="ProfileName">The requested profile name, if any.</param>
/// <param name="Deployment">An inline Azure OpenAI deployment override.</param>
/// <param name="Endpoint">An inline Azure OpenAI endpoint override.</param>
/// <param name="OutputDirectory">An inline output directory override.</param>
public sealed record ProfileOverrides(
    string? ProfileName,
    string? Deployment,
    string? Endpoint,
    string? OutputDirectory);

/// <summary>
/// Final profile values after config defaults and command-line overrides are merged.
/// </summary>
/// <param name="Name">The resolved profile name, or <c>inline</c> when no config profile was used.</param>
/// <param name="DeploymentName">The Azure OpenAI deployment name.</param>
/// <param name="Endpoint">The absolute Azure OpenAI resource endpoint.</param>
/// <param name="OutputDirectory">The absolute directory where outputs should be written.</param>
public sealed record ResolvedProfile(
    string Name,
    string DeploymentName,
    Uri Endpoint,
    string OutputDirectory);

/// <summary>
/// Combines config-file profiles with command-line overrides into the values needed for one operation.
/// </summary>
/// <remarks>
/// Precedence is deliberately simple and script-friendly: explicit command-line values win, then the
/// selected profile, then safe local defaults where possible. Deployment and endpoint are required
/// because the CLI cannot infer the target Azure OpenAI resource.
/// </remarks>
public sealed class ProfileResolver
{
    /// <summary>
    /// Resolves the effective Azure OpenAI deployment, endpoint, and output directory.
    /// </summary>
    /// <param name="config">The loaded configuration, or <see langword="null" /> for inline operation.</param>
    /// <param name="overrides">Command-line values for this invocation.</param>
    /// <returns>The fully resolved profile used by image, output, and doctor services.</returns>
    /// <exception cref="CliException">Thrown when required profile data is missing or invalid.</exception>
    public ResolvedProfile Resolve(AppConfig? config, ProfileOverrides overrides)
    {
        ProfileConfig? profile = null;
        string profileName = overrides.ProfileName ?? config?.DefaultProfile ?? "inline";

        if (!string.IsNullOrWhiteSpace(overrides.ProfileName))
        {
            if (config is null || !config.Profiles.TryGetValue(overrides.ProfileName, out profile))
            {
                throw new CliException($"The profile '{overrides.ProfileName}' was not found in the configuration file.", ExitCodes.Configuration);
            }
        }
        else if (!string.IsNullOrWhiteSpace(config?.DefaultProfile))
        {
            if (!config!.Profiles.TryGetValue(config.DefaultProfile, out profile))
            {
                throw new CliException($"The default profile '{config.DefaultProfile}' was not found in the configuration file.", ExitCodes.Configuration);
            }
        }

        string? deployment = overrides.Deployment ?? profile?.Deployment;
        if (string.IsNullOrWhiteSpace(deployment))
        {
            throw new CliException("Azure OpenAI requires a deployment name. Provide --deployment or configure one in the selected profile.", ExitCodes.Configuration);
        }

        string? endpointValue = overrides.Endpoint ?? profile?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            throw new CliException("Azure OpenAI requires an endpoint. Provide --endpoint or configure one in the selected profile.", ExitCodes.Configuration);
        }

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint))
        {
            throw new CliException($"The Azure endpoint '{endpointValue}' is not a valid absolute URI.", ExitCodes.Configuration);
        }

        if (!endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("The Azure endpoint must use https.", ExitCodes.Configuration);
        }

        if (endpoint.Host.Contains("your-resource", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("The Azure endpoint still contains the starter-config placeholder. Replace it with your Azure OpenAI resource endpoint.", ExitCodes.Configuration);
        }

        string outputDirectory = CliPath.GetFullPath(
            overrides.OutputDirectory
            ?? profile?.OutputDirectory
            ?? Path.Combine(Environment.CurrentDirectory, "output"));

        return new ResolvedProfile(profileName, deployment, endpoint, outputDirectory);
    }
}