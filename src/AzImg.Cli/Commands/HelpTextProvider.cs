using AzImg.Cli.Runtime;

namespace AzImg.Cli.Commands;

/// <summary>
/// Writes command help text using the CLI's canonical command and option vocabulary.
/// </summary>
/// <remarks>
/// Keeping help text in one place makes it easier to keep tests, documentation, and the Agent Skill aligned
/// with the command surface. Command handlers should not embed long help strings directly.
/// </remarks>
public sealed class HelpTextProvider
{
    /// <summary>Writes top-level command help.</summary>
    public void WriteRootHelp(TextWriter writer)
    {
        writer.WriteLine($"Usage: {CliDefaults.CommandName} <command> [arguments]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  generate   Generate one or more images from a prompt.");
        writer.WriteLine("  edit       Edit an existing image, optionally using a mask.");
        writer.WriteLine("  doctor     Validate configuration, credentials, and output setup.");
        writer.WriteLine("  config     Initialize, inspect, or update CLI configuration.");
        writer.WriteLine("  install-skill Install the AzImg agent skill.");
        writer.WriteLine("  update     Check for or install a newer azimg release.");
        writer.WriteLine("  version    Display version information.");
        writer.WriteLine();
        writer.WriteLine($"Run '{CliDefaults.CommandName} <command> --help' for more information about a command.");
    }

    /// <summary>Writes help for the image generation command.</summary>
    public void WriteGenerateHelp(TextWriter writer)
    {
        writer.WriteLine($"Usage: {CliDefaults.CommandName} generate <prompt> [options]");
        writer.WriteLine("Options:");
        WriteProfileOptions(writer);
        writer.WriteLine("      --count <n>                  Number of images, 1-10.");
        WriteImageOptions(writer);
        WriteOutputOptions(writer);
    }

    /// <summary>Writes help for the image edit command.</summary>
    public void WriteEditHelp(TextWriter writer)
    {
        writer.WriteLine($"Usage: {CliDefaults.CommandName} edit <input-file> <prompt> [options]");
        writer.WriteLine("Options:");
        writer.WriteLine("      --mask-file <path>           Optional PNG mask image.");
        WriteProfileOptions(writer);
        writer.WriteLine("      --count <n>                  Number of edited images, 1-10.");
        WriteImageOptions(writer);
        WriteOutputOptions(writer);
    }

    /// <summary>Writes help for the diagnostics command.</summary>
    public void WriteDoctorHelp(TextWriter writer)
    {
        writer.WriteLine($"Usage: {CliDefaults.CommandName} doctor [options]");
        writer.WriteLine("Options:");
        WriteProfileOptions(writer);
        writer.WriteLine("      --verify-auth                Acquire a real Azure bearer token from the Azure CLI sign-in.");
        WriteStructuredOutputOptions(writer);
    }

    /// <summary>Writes help for configuration management.</summary>
    public void WriteConfigHelp(TextWriter writer)
    {
        writer.WriteLine($"Usage: {CliDefaults.CommandName} config [action] [options]");
        writer.WriteLine("Actions:");
        writer.WriteLine("  show");
        writer.WriteLine("  init");
        writer.WriteLine("  set-default-profile");
        writer.WriteLine("Options:");
        writer.WriteLine("      --action <value>             Explicit action name.");
        writer.WriteLine("      --path <path>                Explicit config file path.");
        writer.WriteLine("  -p, --profile <name>             Profile name for init or set-default-profile.");
        writer.WriteLine("      --deployment <name>          Deployment name to write during init.");
        writer.WriteLine("      --endpoint <url>             Azure OpenAI endpoint to write during init.");
        writer.WriteLine("      --force                      Overwrite existing config during init.");
        WriteStructuredOutputOptions(writer);
    }

    /// <summary>Writes help for version output.</summary>
    public void WriteVersionHelp(TextWriter writer)
    {
        writer.WriteLine($"Usage: {CliDefaults.CommandName} version [options]");
        writer.WriteLine("Options:");
        WriteStructuredOutputOptions(writer);
    }

    /// <summary>Writes help for installing the AzImg agent skill.</summary>
    public void WriteInstallSkillHelp(TextWriter writer)
    {
        writer.WriteLine($"Usage: {CliDefaults.CommandName} install-skill [options]");
        writer.WriteLine($"Installs the AzImg agent skill to ~/{CliDefaults.AgentsDirectoryName}/{CliDefaults.AgentSkillsDirectoryName}/{CliDefaults.AgentSkillName}/{CliDefaults.AgentSkillFileName}.");
        writer.WriteLine("Options:");
        writer.WriteLine($"      --install-dir <path>         Agent skills root directory. Default: ~/{CliDefaults.AgentsDirectoryName}/{CliDefaults.AgentSkillsDirectoryName}.");
        writer.WriteLine("      --ref <ref>                  Git branch, tag, or commit. Default: current CLI version tag.");
        writer.WriteLine("      --source-url <url>           Explicit SKILL.md URL. Overrides --ref.");
        writer.WriteLine("      --dry-run                    Show download and save paths without changing files.");
        writer.WriteLine("      --force                      Overwrite an existing different SKILL.md.");
        WriteStructuredOutputOptions(writer);
    }

    /// <summary>Writes help for release update commands.</summary>
    public void WriteUpdateHelp(TextWriter writer)
    {
        writer.WriteLine($"Usage: {CliDefaults.CommandName} update [check|apply] [options]");
        writer.WriteLine("Actions:");
        writer.WriteLine("  check                       Check for a newer release without installing it.");
        writer.WriteLine("  apply                       Install the selected release; this is the default action.");
        writer.WriteLine("Options:");
        writer.WriteLine("      --action <value>             Explicit action name.");
        writer.WriteLine("      --version <tag>              Release version or tag to install instead of latest.");
        writer.WriteLine("      --install-dir <path>         Directory containing the azimg executable to replace.");
        writer.WriteLine("      --manifest-url <url>         Explicit release manifest URL for advanced automation.");
        writer.WriteLine("      --dry-run                    Report intended update work without changing files.");
        writer.WriteLine("      --force                      Reinstall even when the selected release is current.");
        WriteStructuredOutputOptions(writer);
    }

    private static void WriteProfileOptions(TextWriter writer)
    {
        writer.WriteLine("  -p, --profile <name>             Profile name from config.");
        writer.WriteLine("      --config <path>              Explicit config file path.");
        writer.WriteLine("      --deployment <name>          Azure deployment override.");
        writer.WriteLine("      --endpoint <url>             Azure endpoint override.");
        writer.WriteLine("  -o, --output-directory <path>    Output directory override.");
    }

    private static void WriteImageOptions(TextWriter writer)
    {
        writer.WriteLine("      --size <WxH>                 Image size, for example 1024x1024.");
        writer.WriteLine("      --quality <value>            auto, low, medium, or high.");
        writer.WriteLine("      --background <value>         auto, opaque, or transparent.");
        writer.WriteLine("      --output-format <value>      png, jpeg, or webp.");
        writer.WriteLine("      --output-compression <n>     Compression level 0-100.");
        writer.WriteLine("      --end-user-id <id>           Optional end-user identifier.");
    }

    private static void WriteOutputOptions(TextWriter writer)
    {
        writer.WriteLine("      --name-template <template>   Uses {timestamp}, {id}, {slug}, {index}, {profile}.");
        writer.WriteLine("      --write-manifest             Write a manifest JSON file.");
        WriteStructuredOutputOptions(writer);
    }

    private static void WriteStructuredOutputOptions(TextWriter writer)
    {
        writer.WriteLine("      --format <json|text>         Output format; defaults to json. Use text for human-readable output.");
    }
}
