using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Infrastructure.FileSystem;

namespace AzImg.Cli.Commands;

/// <summary>
/// JSON shape emitted by image generation and edit commands.
/// </summary>
/// <param name="ConfigPath">The configuration path used to resolve the operation.</param>
/// <param name="Profile">The resolved profile name.</param>
/// <param name="Deployment">The Azure OpenAI deployment used for the operation.</param>
/// <param name="Files">The saved image files.</param>
/// <param name="Manifest">The manifest path, if one was written.</param>
/// <param name="Usage">Optional Azure OpenAI token usage.</param>
public sealed record ImageCommandResultDocument(
    string ConfigPath,
    string Profile,
    string Deployment,
    SavedImageFile[] Files,
    string? Manifest,
    ImageUsageSnapshot? Usage);

/// <summary>
/// JSON shape emitted by <c>azimg version</c> unless <c>--format text</c> is passed.
/// </summary>
/// <param name="Product">The product name.</param>
/// <param name="CommandName">The executable command name.</param>
/// <param name="Version">The semantic version from the assembly.</param>
/// <param name="SkillName">The bundled agent skill name.</param>
/// <param name="SkillVersion">The bundled agent skill version.</param>
public sealed record VersionDocument(
    string Product,
    string CommandName,
    string Version,
    string SkillName,
    string SkillVersion);

/// <summary>
/// JSON shape emitted by <c>azimg install-skill</c> unless <c>--format text</c> is passed.
/// </summary>
public sealed record AgentSkillInstallDocument(
    string Product,
    string CommandName,
    string SkillName,
    string SourceUrl,
    string TargetPath,
    string InstallDirectory,
    string? SourceRef,
    bool DryRun,
    bool Installed,
    bool AlreadyInstalled,
    bool Overwritten,
    string Message);