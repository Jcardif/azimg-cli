using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Infrastructure.FileSystem;
using AzImg.Cli.Runtime;
using AzImg.Cli.Updates;

namespace AzImg.Cli.Commands;

/// <summary>
/// Coordinates command parsing, request validation, service calls, and console output for the CLI.
/// </summary>
/// <remarks>
/// This type is the command dispatcher. It deliberately delegates config I/O, profile resolution, image
/// service calls, file output, and diagnostics to focused collaborators so command handlers stay readable.
/// </remarks>
public sealed class CommandDispatcher
{
    private readonly ConfigurationStore _configurationStore;
    private readonly ProfileResolver _profileResolver;
    private readonly GeneratedImageRequestValidator _validator;
    private readonly AzureOpenAIImageClient _imageClient;
    private readonly ImageFileStore _imageFileStore;
    private readonly DiagnosticService _diagnosticService;
    private readonly HelpTextProvider _helpText;
    private readonly IUpdateService _updateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDispatcher" /> class.
    /// </summary>
    public CommandDispatcher(
        ConfigurationStore configurationStore,
        ProfileResolver profileResolver,
        GeneratedImageRequestValidator validator,
        AzureOpenAIImageClient imageClient,
        ImageFileStore imageFileStore,
        DiagnosticService diagnosticService,
        HelpTextProvider helpText,
        IUpdateService? updateService = null)
    {
        _configurationStore = configurationStore;
        _profileResolver = profileResolver;
        _validator = validator;
        _imageClient = imageClient;
        _imageFileStore = imageFileStore;
        _diagnosticService = diagnosticService;
        _helpText = helpText;
        _updateService = updateService ?? NoOpUpdateService.Instance;
    }

    /// <summary>
    /// Dispatches the provided command-line arguments to the selected command and returns the process exit code.
    /// </summary>
    /// <param name="args">The raw process arguments.</param>
    /// <param name="cancellationToken">A token used to cancel command work.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        await _updateService.NotifyIfFirstLaunchAsync(args, Console.Error, cancellationToken);

        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            _helpText.WriteRootHelp(Console.Out);
            return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Success;
        }

        string command = args[0].Trim().ToLowerInvariant();
        string[] commandArgs = args.Skip(1).ToArray();

        return command switch
        {
            "generate" => await RunGenerateAsync(commandArgs, cancellationToken),
            "edit" => await RunEditAsync(commandArgs, cancellationToken),
            "doctor" => await RunDoctorAsync(commandArgs, cancellationToken),
            "config" => await RunConfigAsync(commandArgs, cancellationToken),
            "update" => await RunUpdateAsync(commandArgs, cancellationToken),
            "version" => RunVersion(commandArgs),
            _ => throw new CliException($"Unknown command '{args[0]}'. Run '{CliDefaults.CommandName} --help' for usage.", ExitCodes.Usage),
        };
    }

    private async Task<int> RunGenerateAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, CreateProfileAliases());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteGenerateHelp(Console.Out);
            return ExitCodes.Success;
        }

        string prompt = parsed.GetRequiredPositional(0, "generate requires a prompt.");
        int count = parsed.GetInt32("count", 1);
        int? outputCompression = parsed.GetOptionalInt32("output-compression");
        GenerateImageRequest request = new(
            prompt,
            count,
            parsed.Get("size"),
            parsed.Get("quality"),
            parsed.Get("background"),
            parsed.Get("output-format"),
            outputCompression,
            parsed.Get("end-user-id"),
            parsed.Get("name-template") ?? "{id}-{index}",
            parsed.GetFlag("write-manifest"));

        _validator.ValidateGenerate(request);

        (string configPath, AppConfig? config) = await _configurationStore.LoadAsync(parsed.Get("config"), cancellationToken);
        ResolvedProfile profile = _profileResolver.Resolve(
            config,
            new ProfileOverrides(parsed.Get("profile"), parsed.Get("deployment"), parsed.Get("endpoint"), parsed.Get("output-directory")));

        GeneratedImageResult result = await _imageClient.GenerateAsync(profile, request, cancellationToken);
        SaveImagesResult saveResult = await _imageFileStore.SaveAsync(
            profile,
            prompt,
            request.NameTemplate,
            request.WriteManifest,
            result,
            cancellationToken);

        if (parsed.RequestsJsonOutput())
        {
            ImageCommandResultDocument document = new(
                configPath,
                profile.Name,
                result.DeploymentName,
                saveResult.Files.ToArray(),
                saveResult.ManifestPath,
                result.Usage);
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.ImageCommandResultDocument));
            return ExitCodes.Success;
        }

        foreach (SavedImageFile file in saveResult.Files)
        {
            Console.WriteLine(file.Path);
        }

        if (!string.IsNullOrWhiteSpace(saveResult.ManifestPath))
        {
            Console.WriteLine($"manifest: {saveResult.ManifestPath}");
        }

        return ExitCodes.Success;
    }

    private async Task<int> RunEditAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, CreateProfileAliases());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteEditHelp(Console.Out);
            return ExitCodes.Success;
        }

        string inputFile = parsed.GetRequiredPositional(0, "edit requires an input image path.");
        string prompt = parsed.GetRequiredPositional(1, "edit requires a prompt.");
        int count = parsed.GetInt32("count", 1);
        int? outputCompression = parsed.GetOptionalInt32("output-compression");
        EditImageRequest request = new(
            Path.GetFullPath(inputFile),
            parsed.Get("mask-file") is { Length: > 0 } maskFile ? Path.GetFullPath(maskFile) : null,
            prompt,
            count,
            parsed.Get("size"),
            parsed.Get("quality"),
            parsed.Get("background"),
            parsed.Get("output-format"),
            outputCompression,
            parsed.Get("end-user-id"),
            parsed.Get("name-template") ?? "{id}-{index}",
            parsed.GetFlag("write-manifest"));

        _validator.ValidateEdit(request);

        (string configPath, AppConfig? config) = await _configurationStore.LoadAsync(parsed.Get("config"), cancellationToken);
        ResolvedProfile profile = _profileResolver.Resolve(
            config,
            new ProfileOverrides(parsed.Get("profile"), parsed.Get("deployment"), parsed.Get("endpoint"), parsed.Get("output-directory")));

        GeneratedImageResult result = await _imageClient.EditAsync(profile, request, cancellationToken);
        SaveImagesResult saveResult = await _imageFileStore.SaveAsync(
            profile,
            prompt,
            request.NameTemplate,
            request.WriteManifest,
            result,
            cancellationToken);

        if (parsed.RequestsJsonOutput())
        {
            ImageCommandResultDocument document = new(
                configPath,
                profile.Name,
                result.DeploymentName,
                saveResult.Files.ToArray(),
                saveResult.ManifestPath,
                result.Usage);
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.ImageCommandResultDocument));
            return ExitCodes.Success;
        }

        foreach (SavedImageFile file in saveResult.Files)
        {
            Console.WriteLine(file.Path);
        }

        if (!string.IsNullOrWhiteSpace(saveResult.ManifestPath))
        {
            Console.WriteLine($"manifest: {saveResult.ManifestPath}");
        }

        return ExitCodes.Success;
    }

    private async Task<int> RunDoctorAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, CreateProfileAliases());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteDoctorHelp(Console.Out);
            return ExitCodes.Success;
        }

        (string configPath, AppConfig? config) = await _configurationStore.LoadAsync(parsed.Get("config"), cancellationToken);
        ResolvedProfile profile = _profileResolver.Resolve(
            config,
            new ProfileOverrides(parsed.Get("profile"), parsed.Get("deployment"), parsed.Get("endpoint"), parsed.Get("output-directory")));

        DiagnosticReport report = await _diagnosticService.RunAsync(configPath, config, profile, parsed.GetFlag("verify-auth"), cancellationToken);
        if (parsed.RequestsJsonOutput())
        {
            DiagnosticReportDocument document = new(report.ConfigPath, report.ProfileName, report.Checks.ToArray(), report.IsHealthy);
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.DiagnosticReportDocument));
        }
        else
        {
            Console.WriteLine($"config: {report.ConfigPath}");
            Console.WriteLine($"profile: {report.ProfileName}");
            foreach (DiagnosticCheck check in report.Checks)
            {
                Console.WriteLine($"{(check.Passed ? "[ok]" : "[x]")} {check.Name}: {check.Message}");
            }
        }

        return report.IsHealthy ? ExitCodes.Success : ExitCodes.Configuration;
    }

    private async Task<int> RunConfigAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["h"] = "help",
            ["?"] = "help",
        });

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteConfigHelp(Console.Out);
            return ExitCodes.Success;
        }

        string action = (parsed.Get("action") ?? parsed.GetPositionalOrDefault(0) ?? "show").Trim();
        string? path = parsed.Get("path");
        bool json = parsed.RequestsJsonOutput();

        if (action.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            (string configPath, AppConfig? config) = await _configurationStore.LoadAsync(path, cancellationToken);
            if (config is null)
            {
                throw new CliException($"No configuration file was found at '{configPath}'. Run 'config init' to create one.", ExitCodes.Configuration);
            }

            if (json)
            {
                ConfigViewDocument document = new(configPath, config.DefaultProfile, config.Profiles);
                Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.ConfigViewDocument));
            }
            else
            {
                Console.WriteLine(configPath);
                Console.WriteLine(JsonDefaults.Serialize(config, CliJsonContext.Default.AppConfig));
            }

            return ExitCodes.Success;
        }

        if (action.Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            AppConfig sample = _configurationStore.CreateSampleConfig();
            await _configurationStore.SaveAsync(sample, path, parsed.GetFlag("force"), cancellationToken);
            string configPath = path is null ? _configurationStore.GetDefaultPath() : Path.GetFullPath(path);
            if (json)
            {
                ConfigInitDocument document = new(configPath, sample.DefaultProfile, sample.Profiles.Keys.ToArray());
                Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.ConfigInitDocument));
            }
            else
            {
                Console.WriteLine(configPath);
            }

            return ExitCodes.Success;
        }

        if (action.Equals("setdefaultprofile", StringComparison.OrdinalIgnoreCase)
            || action.Equals("set-default-profile", StringComparison.OrdinalIgnoreCase))
        {
            string profileName = parsed.Get("profile")
                ?? throw new CliException("Specify --profile when using set-default-profile.", ExitCodes.Validation);

            (string configPath, AppConfig? config) = await _configurationStore.LoadAsync(path, cancellationToken);
            if (config is null)
            {
                throw new CliException($"No configuration file was found at '{configPath}'.", ExitCodes.Configuration);
            }

            if (!config.Profiles.ContainsKey(profileName))
            {
                throw new CliException($"The profile '{profileName}' was not found in '{configPath}'.", ExitCodes.Configuration);
            }

            config.DefaultProfile = profileName;
            await _configurationStore.SaveAsync(config, configPath, overwrite: true, cancellationToken);

            if (json)
            {
                DefaultProfileDocument document = new(configPath, profileName);
                Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.DefaultProfileDocument));
            }
            else
            {
                Console.WriteLine($"default-profile: {profileName}");
            }

            return ExitCodes.Success;
        }

        throw new CliException($"Unsupported config action '{action}'.", ExitCodes.Usage);
    }

    private int RunVersion(string[] args)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["h"] = "help",
            ["?"] = "help",
        });

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteVersionHelp(Console.Out);
            return ExitCodes.Success;
        }

        string version = ApplicationVersion.Current;
        if (parsed.RequestsJsonOutput())
        {
            VersionDocument document = new(CliDefaults.ProductName, CliDefaults.CommandName, version);
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.VersionDocument));
            return ExitCodes.Success;
        }

        Console.WriteLine($"{CliDefaults.ProductName} {version}");
        return ExitCodes.Success;
    }

    private async Task<int> RunUpdateAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["h"] = "help",
            ["?"] = "help",
        });

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteUpdateHelp(Console.Out);
            return ExitCodes.Success;
        }

        string action = (parsed.Get("action") ?? parsed.GetPositionalOrDefault(0) ?? "apply").Trim();
        bool json = parsed.RequestsJsonOutput();
        UpdateCommandOptions options = new(
            parsed.Get("version"),
            parsed.Get("manifest-url"),
            parsed.Get("install-dir"),
            parsed.GetFlag("dry-run"),
            parsed.GetFlag("force"));

        if (action.Equals("check", StringComparison.OrdinalIgnoreCase))
        {
            UpdateCheckDocument document = await _updateService.CheckAsync(options, cancellationToken);
            WriteUpdateCheck(document, json);
            return ExitCodes.Success;
        }

        if (action.Equals("apply", StringComparison.OrdinalIgnoreCase)
            || action.Equals("install", StringComparison.OrdinalIgnoreCase))
        {
            UpdateApplyDocument document = await _updateService.ApplyAsync(options, cancellationToken);
            WriteUpdateApply(document, json);
            return ExitCodes.Success;
        }

        throw new CliException($"Unsupported update action '{action}'.", ExitCodes.Usage);
    }

    private static void WriteUpdateCheck(UpdateCheckDocument document, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.UpdateCheckDocument));
            return;
        }

        if (document.UpdateAvailable)
        {
            Console.WriteLine($"Update available: {document.LatestVersion} (current {document.CurrentVersion}). Run '{CliDefaults.CommandName} update' to install it.");
            return;
        }

        Console.WriteLine($"{CliDefaults.CommandName} is up to date ({document.CurrentVersion}).");
    }

    private static void WriteUpdateApply(UpdateApplyDocument document, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.UpdateApplyDocument));
            return;
        }

        Console.WriteLine(document.Message);
    }

    private static Dictionary<string, string> CreateProfileAliases()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = "profile",
            ["o"] = "output-directory",
            ["h"] = "help",
            ["?"] = "help",
        };

    private static bool IsHelpToken(string value)
        => value.Equals("--help", StringComparison.Ordinal)
        || value.Equals("-h", StringComparison.Ordinal)
        || value.Equals("help", StringComparison.OrdinalIgnoreCase);
}