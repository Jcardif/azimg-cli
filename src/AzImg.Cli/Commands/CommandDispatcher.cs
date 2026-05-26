using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Application.AgentSkills;
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
    private readonly IGeneratedImageClient _imageClient;
    private readonly ImageFileStore _imageFileStore;
    private readonly DiagnosticService _diagnosticService;
    private readonly HelpTextProvider _helpText;
    private readonly IUpdateService _updateService;
    private readonly IAgentSkillInstaller _agentSkillInstaller;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDispatcher" /> class.
    /// </summary>
    public CommandDispatcher(
        ConfigurationStore configurationStore,
        ProfileResolver profileResolver,
        GeneratedImageRequestValidator validator,
        IGeneratedImageClient imageClient,
        ImageFileStore imageFileStore,
        DiagnosticService diagnosticService,
        HelpTextProvider helpText,
        IUpdateService? updateService = null,
        IAgentSkillInstaller? agentSkillInstaller = null)
    {
        _configurationStore = configurationStore;
        _profileResolver = profileResolver;
        _validator = validator;
        _imageClient = imageClient;
        _imageFileStore = imageFileStore;
        _diagnosticService = diagnosticService;
        _helpText = helpText;
        _updateService = updateService ?? NoOpUpdateService.Instance;
        _agentSkillInstaller = agentSkillInstaller ?? NoOpAgentSkillInstaller.Instance;
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
            "install-skill" => await RunInstallSkillAsync(commandArgs, cancellationToken),
            "update" => await RunUpdateAsync(commandArgs, cancellationToken),
            "uninstall" => await RunUninstallAsync(commandArgs, cancellationToken),
            "version" => RunVersion(commandArgs),
            _ => throw new CliException($"Unknown command '{args[0]}'. Run '{CliDefaults.CommandName} --help' for usage.", ExitCodes.Usage),
        };
    }

    private async Task<int> RunGenerateAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, CreateProfileAliases(), CreateImageValueOptions(), CreateGenerateFlagOptions());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteGenerateHelp(Console.Out);
            return ExitCodes.Success;
        }

        bool json = parsed.RequestsJsonOutput();
        string prompt = await ResolveGeneratePromptAsync(parsed, cancellationToken);
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

        await using OperationProgress progress = OperationProgress.Start(
            Console.Error,
            $"Generating {FormatImageCount(request.Count)} · {profile.DeploymentName} · waiting for Azure OpenAI");

        GeneratedImageResult result = await _imageClient.GenerateAsync(profile, request, cancellationToken);
        progress.Report($"Saving {FormatImageCount(result.Images.Count)} · writing image files");
        SaveImagesResult saveResult = await _imageFileStore.SaveAsync(
            profile,
            prompt,
            request.NameTemplate,
            request.WriteManifest,
            result,
            cancellationToken);
        progress.Complete($"Generated {FormatFileCount(saveResult.Files.Count)}");

        if (json)
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

        private static async Task<string> ResolveGeneratePromptAsync(ParsedArguments parsed, CancellationToken cancellationToken)
        {
            string? promptFile = parsed.Get("prompt-file");
            if (promptFile is null)
            {
                string prompt = parsed.GetRequiredPositional(0, "generate requires a prompt or --prompt-file <path>.");
                parsed.ThrowIfExtraPositionals(1, "generate accepts exactly one prompt. Quote multi-word prompts as a single argument.");
                return prompt;
            }

            if (string.IsNullOrWhiteSpace(promptFile))
            {
                throw new CliException("Option '--prompt-file' expects a path.", ExitCodes.Usage);
            }

            parsed.ThrowIfExtraPositionals(0, "generate accepts either one prompt or --prompt-file <path>, not both.");
            string promptPath = CliPath.GetFullPath(promptFile);
            if (Directory.Exists(promptPath))
            {
                throw new CliException($"Prompt file '{promptPath}' is a directory.", ExitCodes.Validation);
            }

            if (!File.Exists(promptPath))
            {
                throw new CliException($"Prompt file '{promptPath}' was not found.", ExitCodes.Validation);
            }

            try
            {
                return await File.ReadAllTextAsync(promptPath, cancellationToken);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new CliException($"Prompt file '{promptPath}' could not be read: {exception.Message}", ExitCodes.Io);
            }
            catch (IOException exception)
            {
                throw new CliException($"Prompt file '{promptPath}' could not be read: {exception.Message}", ExitCodes.Io);
            }
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
        ParsedArguments parsed = CommandLineParser.Parse(args, CreateProfileAliases(), CreateEditValueOptions(), CreateGenerateFlagOptions());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteEditHelp(Console.Out);
            return ExitCodes.Success;
        }

        bool json = parsed.RequestsJsonOutput();
        string inputFile = parsed.GetRequiredPositional(0, "edit requires an input image path or image folder.");
        string prompt = parsed.GetRequiredPositional(1, "edit requires a prompt.");
        parsed.ThrowIfExtraPositionals(2, "edit accepts exactly an input image path or folder and one prompt. Quote multi-word prompts as a single argument.");
        int count = parsed.GetInt32("count", 1);
        int? outputCompression = parsed.GetOptionalInt32("output-compression");
        IReadOnlyList<string> inputFiles = EditInputFileResolver.Resolve(inputFile, parsed.GetValues("image"));
        EditImageRequest request = new(
            inputFiles,
            parsed.Get("mask-file") is { Length: > 0 } maskFile ? CliPath.GetFullPath(maskFile) : null,
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

        await using OperationProgress progress = OperationProgress.Start(
            Console.Error,
            $"Editing {FormatImageCount(request.Count)} · {profile.DeploymentName} · waiting for Azure OpenAI");

        GeneratedImageResult result = await _imageClient.EditAsync(profile, request, cancellationToken);
        progress.Report($"Saving {FormatImageCount(result.Images.Count)} · writing image files");
        SaveImagesResult saveResult = await _imageFileStore.SaveAsync(
            profile,
            prompt,
            request.NameTemplate,
            request.WriteManifest,
            result,
            cancellationToken);
        progress.Complete($"Edited {FormatFileCount(saveResult.Files.Count)}");

        if (json)
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
        ParsedArguments parsed = CommandLineParser.Parse(args, CreateProfileAliases(), CreateDoctorValueOptions(), CreateDoctorFlagOptions());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteDoctorHelp(Console.Out);
            return ExitCodes.Success;
        }

        bool json = parsed.RequestsJsonOutput();
        parsed.ThrowIfExtraPositionals(0, "doctor does not accept positional arguments.");
        (string configPath, AppConfig? config) = await _configurationStore.LoadAsync(parsed.Get("config"), cancellationToken);
        ResolvedProfile profile = _profileResolver.Resolve(
            config,
            new ProfileOverrides(parsed.Get("profile"), parsed.Get("deployment"), parsed.Get("endpoint"), parsed.Get("output-directory")));

        DiagnosticReport report = await _diagnosticService.RunAsync(configPath, config, profile, parsed.GetFlag("verify-auth"), cancellationToken);
        if (json)
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
            ["p"] = "profile",
            ["h"] = "help",
            ["?"] = "help",
        }, CreateConfigValueOptions(), CreateConfigFlagOptions());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteConfigHelp(Console.Out);
            return ExitCodes.Success;
        }

        string? actionOption = parsed.Get("action");
        parsed.ThrowIfExtraPositionals(actionOption is null ? 1 : 0, "config accepts at most one action positional argument.");
        string action = (actionOption ?? parsed.GetPositionalOrDefault(0) ?? "show").Trim();
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
            ApplyConfigInitOverrides(sample, parsed);
            await _configurationStore.SaveAsync(sample, path, parsed.GetFlag("force"), cancellationToken);
            string configPath = path is null ? _configurationStore.GetDefaultPath() : CliPath.GetFullPath(path);
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

    private static void ApplyConfigInitOverrides(AppConfig config, ParsedArguments parsed)
    {
        string profileName = NormalizeConfigInitProfileName(parsed.Get("profile") ?? config.DefaultProfile ?? "azure-default");
        ProfileConfig starterProfile = config.Profiles.TryGetValue(config.DefaultProfile ?? profileName, out ProfileConfig? existingProfile)
            ? existingProfile
            : new ProfileConfig();

        ProfileConfig profile = new()
        {
            Deployment = NormalizeConfigInitValue(parsed.Get("deployment")) ?? starterProfile.Deployment,
            Endpoint = NormalizeConfigInitEndpoint(parsed.Get("endpoint")) ?? starterProfile.Endpoint,
        };

        config.DefaultProfile = profileName;
        config.Profiles = new Dictionary<string, ProfileConfig>(StringComparer.OrdinalIgnoreCase)
        {
            [profileName] = profile,
        };
    }

    private static string NormalizeConfigInitProfileName(string value)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new CliException("--profile cannot be empty when initializing config.", ExitCodes.Validation);
        }

        return normalized;
    }

    private static string? NormalizeConfigInitValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeConfigInitEndpoint(string? value)
    {
        string? endpoint = NormalizeConfigInitValue(value);
        if (endpoint is null)
        {
            return null;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
        {
            throw new CliException($"The Azure endpoint '{endpoint}' is not a valid absolute URI.", ExitCodes.Validation);
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException("The Azure endpoint must use https.", ExitCodes.Validation);
        }

        return endpoint;
    }

    private int RunVersion(string[] args)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["h"] = "help",
            ["?"] = "help",
        }, CreateStructuredValueOptions(), CreateHelpFlagOptions());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteVersionHelp(Console.Out);
            return ExitCodes.Success;
        }

        parsed.ThrowIfExtraPositionals(0, "version does not accept positional arguments.");
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

    private async Task<int> RunInstallSkillAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["h"] = "help",
            ["?"] = "help",
        }, CreateInstallSkillValueOptions(), CreateInstallSkillFlagOptions());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteInstallSkillHelp(Console.Out);
            return ExitCodes.Success;
        }

        parsed.ThrowIfExtraPositionals(0, "install-skill does not accept positional arguments.");
        bool json = parsed.RequestsJsonOutput();
        AgentSkillInstallOptions options = new(
            parsed.Get("install-dir"),
            parsed.Get("ref"),
            parsed.Get("source-url"),
            parsed.GetFlag("dry-run"),
            parsed.GetFlag("force"));

        AgentSkillInstallResult result = await _agentSkillInstaller.InstallAsync(options, Console.Error, cancellationToken);
        WriteAgentSkillInstall(result, json);
        return ExitCodes.Success;
    }

    private async Task<int> RunUpdateAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["h"] = "help",
            ["?"] = "help",
        }, CreateUpdateValueOptions(), CreateUpdateFlagOptions());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteUpdateHelp(Console.Out);
            return ExitCodes.Success;
        }

        string? actionOption = parsed.Get("action");
        parsed.ThrowIfExtraPositionals(actionOption is null ? 1 : 0, "update accepts at most one action positional argument.");
        string action = (actionOption ?? parsed.GetPositionalOrDefault(0) ?? "apply").Trim();
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

    private async Task<int> RunUninstallAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["h"] = "help",
            ["?"] = "help",
        }, CreateUninstallValueOptions(), CreateUninstallFlagOptions());

        if (parsed.GetFlag("help"))
        {
            _helpText.WriteUninstallHelp(Console.Out);
            return ExitCodes.Success;
        }

        string? actionOption = parsed.Get("action");
        parsed.ThrowIfExtraPositionals(actionOption is null ? 1 : 0, "uninstall accepts at most one action positional argument.");
        string action = (actionOption ?? parsed.GetPositionalOrDefault(0) ?? "remove").Trim();
        bool fullCleanup = action.Equals("full-cleanup", StringComparison.OrdinalIgnoreCase);
        if (!fullCleanup
            && !action.Equals("remove", StringComparison.OrdinalIgnoreCase)
            && !action.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException($"Unsupported uninstall action '{action}'.", ExitCodes.Usage);
        }

        bool json = parsed.RequestsJsonOutput();
        UninstallCommandOptions options = new(
            parsed.Get("install-dir"),
            parsed.GetFlag("dry-run"),
            fullCleanup);

        UninstallDocument document = await _updateService.UninstallAsync(options, cancellationToken);
        WriteUninstall(document, json);
        return ExitCodes.Success;
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

    private static void WriteUninstall(UninstallDocument document, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.UninstallDocument));
            return;
        }

        Console.WriteLine(document.Message);
    }

    private static void WriteAgentSkillInstall(AgentSkillInstallResult result, bool json)
    {
        AgentSkillInstallDocument document = new(
            CliDefaults.ProductName,
            CliDefaults.CommandName,
            result.SkillName,
            result.SourceUrl,
            result.TargetPath,
            result.InstallDirectory,
            result.SourceRef,
            result.DryRun,
            result.Installed,
            result.AlreadyInstalled,
            result.Overwritten,
            result.Message);

        if (json)
        {
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.AgentSkillInstallDocument));
            return;
        }

        Console.WriteLine(document.Message);
        Console.WriteLine($"source: {document.SourceUrl}");
        Console.WriteLine($"target: {document.TargetPath}");
    }

    private static string FormatImageCount(int count)
        => count == 1 ? "1 image" : $"{count} images";

    private static string FormatFileCount(int count)
        => count == 1 ? "1 file" : $"{count} files";

    private static Dictionary<string, string> CreateProfileAliases()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = "profile",
            ["o"] = "output-directory",
            ["h"] = "help",
            ["?"] = "help",
        };

    private static HashSet<string> CreateStructuredValueOptions()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            "format",
        };

    private static HashSet<string> CreateProfileValueOptions()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            "profile",
            "config",
            "deployment",
            "endpoint",
            "output-directory",
        };

    private static HashSet<string> CreateImageValueOptions()
    {
        HashSet<string> options = CreateProfileValueOptions();
        options.Add("count");
        options.Add("prompt-file");
        options.Add("size");
        options.Add("quality");
        options.Add("background");
        options.Add("output-format");
        options.Add("output-compression");
        options.Add("end-user-id");
        options.Add("name-template");
        options.Add("format");
        return options;
    }

    private static HashSet<string> CreateEditValueOptions()
    {
        HashSet<string> options = CreateImageValueOptions();
        options.Add("image");
        options.Add("mask-file");
        return options;
    }

    private static HashSet<string> CreateDoctorValueOptions()
    {
        HashSet<string> options = CreateProfileValueOptions();
        options.Add("format");
        return options;
    }

    private static HashSet<string> CreateConfigValueOptions()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            "action",
            "path",
            "profile",
            "deployment",
            "endpoint",
            "format",
        };

    private static HashSet<string> CreateUpdateValueOptions()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            "action",
            "version",
            "manifest-url",
            "install-dir",
            "format",
        };

    private static HashSet<string> CreateUninstallValueOptions()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            "action",
            "install-dir",
            "format",
        };

    private static HashSet<string> CreateInstallSkillValueOptions()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            "install-dir",
            "ref",
            "source-url",
            "format",
        };

    private static HashSet<string> CreateHelpFlagOptions()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            "help",
            "json",
        };

    private static HashSet<string> CreateGenerateFlagOptions()
    {
        HashSet<string> options = CreateHelpFlagOptions();
        options.Add("write-manifest");
        return options;
    }

    private static HashSet<string> CreateDoctorFlagOptions()
    {
        HashSet<string> options = CreateHelpFlagOptions();
        options.Add("verify-auth");
        return options;
    }

    private static HashSet<string> CreateConfigFlagOptions()
    {
        HashSet<string> options = CreateHelpFlagOptions();
        options.Add("force");
        return options;
    }

    private static HashSet<string> CreateUpdateFlagOptions()
    {
        HashSet<string> options = CreateHelpFlagOptions();
        options.Add("dry-run");
        options.Add("force");
        return options;
    }

    private static HashSet<string> CreateUninstallFlagOptions()
    {
        HashSet<string> options = CreateHelpFlagOptions();
        options.Add("dry-run");
        return options;
    }

    private static HashSet<string> CreateInstallSkillFlagOptions()
    {
        HashSet<string> options = CreateHelpFlagOptions();
        options.Add("dry-run");
        options.Add("force");
        return options;
    }

    private static bool IsHelpToken(string value)
        => value.Equals("--help", StringComparison.Ordinal)
        || value.Equals("-h", StringComparison.Ordinal)
        || value.Equals("help", StringComparison.OrdinalIgnoreCase);
}
