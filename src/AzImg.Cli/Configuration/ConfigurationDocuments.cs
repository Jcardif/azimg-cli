namespace AzImg.Cli.Configuration;

/// <summary>
/// Root object stored in the user's <c>azimg</c> configuration file.
/// </summary>
/// <remarks>
/// The file currently uses schema version <c>1</c>. Profiles are keyed case-insensitively after
/// loading so command-line profile names behave consistently across platforms.
/// </remarks>
public sealed class AppConfig
{
    /// <summary>The configuration schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The profile selected when the user does not pass <c>--profile</c>.</summary>
    public string? DefaultProfile { get; set; }

    /// <summary>Named Azure OpenAI profiles available to CLI commands.</summary>
    public Dictionary<string, ProfileConfig> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Named Azure OpenAI profile containing deployment, endpoint, and default output settings.
/// </summary>
public sealed class ProfileConfig
{
    /// <summary>The Azure OpenAI image deployment name.</summary>
    public string? Deployment { get; set; }

    /// <summary>The absolute Azure OpenAI resource endpoint.</summary>
    public string? Endpoint { get; set; }

    /// <summary>The directory where generated or edited images are written by default.</summary>
    public string? OutputDirectory { get; set; }
}

/// <summary>
/// JSON shape emitted by <c>azimg config show</c> unless <c>--format text</c> is passed.
/// </summary>
/// <param name="Path">The resolved configuration file path.</param>
/// <param name="DefaultProfile">The configured default profile name.</param>
/// <param name="Profiles">The configured profile map.</param>
public sealed record ConfigViewDocument(
    string Path,
    string? DefaultProfile,
    Dictionary<string, ProfileConfig> Profiles);

/// <summary>
/// JSON shape emitted by <c>azimg config init</c> unless <c>--format text</c> is passed.
/// </summary>
/// <param name="Path">The resolved configuration file path that was written.</param>
/// <param name="DefaultProfile">The default profile included in the created config.</param>
/// <param name="Profiles">The profile names included in the created config.</param>
public sealed record ConfigInitDocument(
    string Path,
    string? DefaultProfile,
    string[] Profiles);

/// <summary>
/// JSON shape emitted after changing the default profile.
/// </summary>
/// <param name="Path">The resolved configuration file path.</param>
/// <param name="DefaultProfile">The newly selected default profile.</param>
public sealed record DefaultProfileDocument(
    string Path,
    string DefaultProfile);