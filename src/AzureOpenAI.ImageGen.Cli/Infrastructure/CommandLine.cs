using System.Reflection;
using AzureOpenAI.ImageGen.Cli.Models;
using AzureOpenAI.ImageGen.Cli.Services;

namespace AzureOpenAI.ImageGen.Cli.Infrastructure;

public sealed class CliApplication
{
    private readonly ConfigStore _configStore;
    private readonly ProfileResolver _profileResolver;
    private readonly RequestValidator _validator;
    private readonly AzureImageService _imageService;
    private readonly FileOutputService _outputService;
    private readonly DoctorService _doctorService;

    public CliApplication(
        ConfigStore configStore,
        ProfileResolver profileResolver,
        RequestValidator validator,
        AzureImageService imageService,
        FileOutputService outputService,
        DoctorService doctorService)
    {
        _configStore = configStore;
        _profileResolver = profileResolver;
        _validator = validator;
        _imageService = imageService;
        _outputService = outputService;
        _doctorService = doctorService;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            WriteRootHelp();
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
            "version" => RunVersion(),
            _ => throw new CliException($"Unknown command '{args[0]}'. Run '{CliDefaults.CommandName} --help' for usage.", ExitCodes.Usage),
        };
    }

    private async Task<int> RunGenerateAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = "profile",
            ["o"] = "output-directory",
            ["h"] = "help",
            ["?"] = "help",
        });

        if (parsed.GetFlag("help"))
        {
            WriteGenerateHelp();
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

        (string configPath, AppConfig? config) = await _configStore.LoadAsync(parsed.Get("config"), cancellationToken);
        ResolvedProfile profile = _profileResolver.Resolve(
            config,
            new ProfileOverrides(parsed.Get("profile"), parsed.Get("deployment"), parsed.Get("endpoint"), parsed.Get("output-directory")));

        ImageOperationResult result = await _imageService.GenerateAsync(profile, request, cancellationToken);
        SaveImagesResult saveResult = await _outputService.SaveAsync(
            profile,
            prompt,
            request.NameTemplate,
            request.WriteManifest,
            result,
            cancellationToken);

        if (parsed.GetFlag("json"))
        {
            OperationResultDocument document = new(
                configPath,
                profile.Name,
                result.DeploymentName,
                saveResult.Files.ToArray(),
                saveResult.ManifestPath,
                result.Usage);
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.OperationResultDocument));
            return ExitCodes.Success;
        }

        foreach (SavedImageArtifact file in saveResult.Files)
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
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = "profile",
            ["o"] = "output-directory",
            ["h"] = "help",
            ["?"] = "help",
        });

        if (parsed.GetFlag("help"))
        {
            WriteEditHelp();
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

        (string configPath, AppConfig? config) = await _configStore.LoadAsync(parsed.Get("config"), cancellationToken);
        ResolvedProfile profile = _profileResolver.Resolve(
            config,
            new ProfileOverrides(parsed.Get("profile"), parsed.Get("deployment"), parsed.Get("endpoint"), parsed.Get("output-directory")));

        ImageOperationResult result = await _imageService.EditAsync(profile, request, cancellationToken);
        SaveImagesResult saveResult = await _outputService.SaveAsync(
            profile,
            prompt,
            request.NameTemplate,
            request.WriteManifest,
            result,
            cancellationToken);

        if (parsed.GetFlag("json"))
        {
            OperationResultDocument document = new(
                configPath,
                profile.Name,
                result.DeploymentName,
                saveResult.Files.ToArray(),
                saveResult.ManifestPath,
                result.Usage);
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.OperationResultDocument));
            return ExitCodes.Success;
        }

        foreach (SavedImageArtifact file in saveResult.Files)
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
        ParsedArguments parsed = CommandLineParser.Parse(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = "profile",
            ["o"] = "output-directory",
            ["h"] = "help",
            ["?"] = "help",
        });

        if (parsed.GetFlag("help"))
        {
            WriteDoctorHelp();
            return ExitCodes.Success;
        }

        (string configPath, AppConfig? config) = await _configStore.LoadAsync(parsed.Get("config"), cancellationToken);
        ResolvedProfile profile = _profileResolver.Resolve(
            config,
            new ProfileOverrides(parsed.Get("profile"), parsed.Get("deployment"), parsed.Get("endpoint"), parsed.Get("output-directory")));

        DoctorReport report = await _doctorService.RunAsync(configPath, config, profile, parsed.GetFlag("verify-auth"), cancellationToken);
        if (parsed.GetFlag("json"))
        {
            DoctorReportDocument document = new(report.ConfigPath, report.ProfileName, report.Checks.ToArray(), report.IsHealthy);
            Console.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.DoctorReportDocument));
        }
        else
        {
            Console.WriteLine($"config: {report.ConfigPath}");
            Console.WriteLine($"profile: {report.ProfileName}");
            foreach (DoctorCheck check in report.Checks)
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
            WriteConfigHelp();
            return ExitCodes.Success;
        }

        string action = (parsed.Get("action") ?? parsed.GetPositionalOrDefault(0) ?? "show").Trim();
        string? path = parsed.Get("path");
        bool json = parsed.GetFlag("json");

        if (action.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            (string configPath, AppConfig? config) = await _configStore.LoadAsync(path, cancellationToken);
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
            AppConfig sample = _configStore.CreateSampleConfig();
            await _configStore.SaveAsync(sample, path, parsed.GetFlag("force"), cancellationToken);
            Console.WriteLine(path is null ? _configStore.GetDefaultPath() : Path.GetFullPath(path));
            return ExitCodes.Success;
        }

        if (action.Equals("setdefaultprofile", StringComparison.OrdinalIgnoreCase)
            || action.Equals("set-default-profile", StringComparison.OrdinalIgnoreCase))
        {
            string profileName = parsed.Get("profile")
                ?? throw new CliException("Specify --profile when using SetDefaultProfile.", ExitCodes.Validation);

            (string configPath, AppConfig? config) = await _configStore.LoadAsync(path, cancellationToken);
            if (config is null)
            {
                throw new CliException($"No configuration file was found at '{configPath}'.", ExitCodes.Configuration);
            }

            if (!config.Profiles.ContainsKey(profileName))
            {
                throw new CliException($"The profile '{profileName}' was not found in '{configPath}'.", ExitCodes.Configuration);
            }

            config.DefaultProfile = profileName;
            await _configStore.SaveAsync(config, configPath, overwrite: true, cancellationToken);

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

    private static int RunVersion()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        Console.WriteLine($"{CliDefaults.ProductName} {version}");
        return ExitCodes.Success;
    }

    private static bool IsHelpToken(string value)
        => value.Equals("--help", StringComparison.Ordinal)
        || value.Equals("-h", StringComparison.Ordinal)
        || value.Equals("help", StringComparison.OrdinalIgnoreCase);

    private static void WriteRootHelp()
    {
        Console.WriteLine($"Usage: {CliDefaults.CommandName} <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  generate   Generate one or more images from a prompt.");
        Console.WriteLine("  edit       Edit an existing image, optionally using a mask.");
        Console.WriteLine("  doctor     Validate configuration, credentials, and output setup.");
        Console.WriteLine("  config     Initialize, inspect, or update CLI configuration.");
        Console.WriteLine("  version    Display version information.");
        Console.WriteLine();
        Console.WriteLine($"Run '{CliDefaults.CommandName} <command> --help' for more information about a command.");
    }

    private static void WriteGenerateHelp()
    {
        Console.WriteLine($"Usage: {CliDefaults.CommandName} generate <prompt> [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --profile <name>             Profile name from config.");
        Console.WriteLine("      --config <path>              Explicit config file path.");
        Console.WriteLine("      --deployment <name>          Azure deployment override.");
        Console.WriteLine("      --endpoint <url>             Azure endpoint override.");
        Console.WriteLine("  -o, --output-directory <path>    Output directory override.");
        Console.WriteLine("      --count <n>                  Number of images, 1-10.");
        Console.WriteLine("      --size <WxH>                 Image size, for example 1024x1024.");
        Console.WriteLine("      --quality <value>            auto, low, medium, or high.");
        Console.WriteLine("      --background <value>         auto, opaque, or transparent.");
        Console.WriteLine("      --output-format <value>      png, jpeg, or webp.");
        Console.WriteLine("      --output-compression <n>     Compression level 0-100.");
        Console.WriteLine("      --end-user-id <id>           Optional end-user identifier.");
        Console.WriteLine("      --name-template <template>   Uses {timestamp}, {id}, {slug}, {index}, {profile}.");
        Console.WriteLine("      --write-manifest             Write a manifest JSON file.");
        Console.WriteLine("      --json                       Emit machine-readable JSON output.");
    }

    private static void WriteEditHelp()
    {
        Console.WriteLine($"Usage: {CliDefaults.CommandName} edit <input-file> <prompt> [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("      --mask-file <path>           Optional PNG mask image.");
        Console.WriteLine("  -p, --profile <name>             Profile name from config.");
        Console.WriteLine("      --config <path>              Explicit config file path.");
        Console.WriteLine("      --deployment <name>          Azure deployment override.");
        Console.WriteLine("      --endpoint <url>             Azure endpoint override.");
        Console.WriteLine("  -o, --output-directory <path>    Output directory override.");
        Console.WriteLine("      --count <n>                  Number of edited images, 1-10.");
        Console.WriteLine("      --size <WxH>                 Image size, for example 1024x1024.");
        Console.WriteLine("      --quality <value>            auto, low, medium, or high.");
        Console.WriteLine("      --background <value>         auto, opaque, or transparent.");
        Console.WriteLine("      --output-format <value>      png, jpeg, or webp.");
        Console.WriteLine("      --output-compression <n>     Compression level 0-100.");
        Console.WriteLine("      --end-user-id <id>           Optional end-user identifier.");
        Console.WriteLine("      --name-template <template>   Uses {timestamp}, {id}, {slug}, {index}, {profile}.");
        Console.WriteLine("      --write-manifest             Write a manifest JSON file.");
        Console.WriteLine("      --json                       Emit machine-readable JSON output.");
    }

    private static void WriteDoctorHelp()
    {
        Console.WriteLine($"Usage: {CliDefaults.CommandName} doctor [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --profile <name>             Profile name from config.");
        Console.WriteLine("      --config <path>              Explicit config file path.");
        Console.WriteLine("      --deployment <name>          Azure deployment override.");
        Console.WriteLine("      --endpoint <url>             Azure endpoint override.");
        Console.WriteLine("  -o, --output-directory <path>    Output directory override.");
        Console.WriteLine("      --verify-auth                Acquire a real Azure bearer token with DefaultAzureCredential.");
        Console.WriteLine("      --json                       Emit machine-readable JSON output.");
    }

    private static void WriteConfigHelp()
    {
        Console.WriteLine($"Usage: {CliDefaults.CommandName} config [action] [options]");
        Console.WriteLine("Actions:");
        Console.WriteLine("  show");
        Console.WriteLine("  init");
        Console.WriteLine("  set-default-profile");
        Console.WriteLine("Options:");
        Console.WriteLine("      --action <value>             Explicit action name.");
        Console.WriteLine("      --path <path>                Explicit config file path.");
        Console.WriteLine("      --profile <name>             Profile name for set-default-profile.");
        Console.WriteLine("      --force                      Overwrite existing config during init.");
        Console.WriteLine("      --json                       Emit machine-readable JSON output.");
    }
}

internal sealed class ParsedArguments
{
    private readonly Dictionary<string, string> _options;
    private readonly List<string> _positionals;

    public ParsedArguments(Dictionary<string, string> options, List<string> positionals)
    {
        _options = options;
        _positionals = positionals;
    }

    public string? Get(string name)
        => _options.TryGetValue(name, out string? value) ? value : null;

    public bool GetFlag(string name)
        => _options.TryGetValue(name, out string? value)
           && (string.IsNullOrWhiteSpace(value)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    public int GetInt32(string name, int defaultValue)
        => GetOptionalInt32(name) ?? defaultValue;

    public int? GetOptionalInt32(string name)
    {
        string? value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
        {
            throw new CliException($"Option '--{name}' expects an integer value.", ExitCodes.Validation);
        }

        return result;
    }

    public string GetRequiredPositional(int index, string errorMessage)
        => GetPositionalOrDefault(index) ?? throw new CliException(errorMessage, ExitCodes.Usage);

    public string? GetPositionalOrDefault(int index)
        => index >= 0 && index < _positionals.Count ? _positionals[index] : null;
}

internal static class CommandLineParser
{
    public static ParsedArguments Parse(string[] args, IReadOnlyDictionary<string, string> aliases)
    {
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
        List<string> positionals = [];

        for (int index = 0; index < args.Length; index++)
        {
            string token = args[index];
            if (token == "--")
            {
                for (int remaining = index + 1; remaining < args.Length; remaining++)
                {
                    positionals.Add(args[remaining]);
                }

                break;
            }

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                string optionToken = token[2..];
                string name;
                string value;
                int separator = optionToken.IndexOf('=');
                if (separator >= 0)
                {
                    name = optionToken[..separator];
                    value = optionToken[(separator + 1)..];
                }
                else
                {
                    name = optionToken;
                    if (index + 1 < args.Length && !LooksLikeOption(args[index + 1]))
                    {
                        value = args[++index];
                    }
                    else
                    {
                        value = "true";
                    }
                }

                options[name] = value;
                continue;
            }

            if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1)
            {
                string shortName = token[1..];
                if (!aliases.TryGetValue(shortName, out string? mappedName))
                {
                    throw new CliException($"Unknown option '{token}'.", ExitCodes.Usage);
                }

                string value;
                if (index + 1 < args.Length && !LooksLikeOption(args[index + 1]))
                {
                    value = args[++index];
                }
                else
                {
                    value = "true";
                }

                options[mappedName] = value;
                continue;
            }

            positionals.Add(token);
        }

        return new ParsedArguments(options, positionals);
    }

    private static bool LooksLikeOption(string value)
        => value.StartsWith("--", StringComparison.Ordinal)
           || (value.StartsWith("-", StringComparison.Ordinal) && value.Length > 1);
}
