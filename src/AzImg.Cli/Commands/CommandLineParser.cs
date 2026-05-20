using System.Globalization;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Commands;

/// <summary>
/// Parsed command-line values split into named options and positional arguments.
/// </summary>
/// <remarks>
/// The parser intentionally remains small and deterministic because the CLI command surface is modest.
/// Long options use <c>--name value</c> or <c>--name=value</c>, short aliases are supplied per command,
/// and <c>--</c> stops option parsing for prompts that start with dashes.
/// </remarks>
public sealed class ParsedArguments
{
    private readonly Dictionary<string, string> _options;
    private readonly List<string> _positionals;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParsedArguments" /> class.
    /// </summary>
    public ParsedArguments(Dictionary<string, string> options, List<string> positionals)
    {
        _options = options;
        _positionals = positionals;
    }

    /// <summary>Gets a named option value, or <see langword="null" /> when omitted.</summary>
    public string? Get(string name)
        => _options.TryGetValue(name, out string? value) ? value : null;

    /// <summary>Gets whether a flag-style option is present and truthy.</summary>
    public bool GetFlag(string name)
        => _options.TryGetValue(name, out string? value)
           && (string.IsNullOrWhiteSpace(value)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets an integer option value or the provided default when the option is absent.</summary>
    public int GetInt32(string name, int defaultValue)
        => GetOptionalInt32(name) ?? defaultValue;

    /// <summary>Gets an optional integer option value.</summary>
    /// <exception cref="CliException">Thrown when the option is present but not an integer.</exception>
    public int? GetOptionalInt32(string name)
    {
        string? value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            throw new CliException($"Option '--{name}' expects an integer value.", ExitCodes.Validation);
        }

        return result;
    }

    /// <summary>Gets a required positional value.</summary>
    /// <exception cref="CliException">Thrown when the positional value is missing.</exception>
    public string GetRequiredPositional(int index, string errorMessage)
        => GetPositionalOrDefault(index) ?? throw new CliException(errorMessage, ExitCodes.Usage);

    /// <summary>Gets a positional value, or <see langword="null" /> when the index is absent.</summary>
    public string? GetPositionalOrDefault(int index)
        => index >= 0 && index < _positionals.Count ? _positionals[index] : null;

    /// <summary>Gets the number of positional arguments supplied after option parsing.</summary>
    public int PositionalCount => _positionals.Count;

    /// <summary>Throws when more positional arguments were supplied than a command supports.</summary>
    /// <exception cref="CliException">Thrown when extra positionals are present.</exception>
    public void ThrowIfExtraPositionals(int allowedCount, string errorMessage)
    {
        if (_positionals.Count > allowedCount)
        {
            throw new CliException(errorMessage, ExitCodes.Usage);
        }
    }

    /// <summary>
    /// Gets whether the parsed command should emit machine-readable JSON output.
    /// </summary>
    /// <remarks>
    /// JSON is the default response format for commands that produce structured results. Users can pass
    /// <c>--format text</c> when they explicitly want human-readable output instead.
    /// </remarks>
    public bool RequestsJsonOutput()
    {
        if (Get("json") is not null)
        {
            throw new CliException("The --json option was removed because JSON is the default. Use --format text for human-readable output.", ExitCodes.Validation);
        }

        string? format = Get("format");
        if (string.IsNullOrWhiteSpace(format))
        {
            return true;
        }

        string normalized = format.Trim().ToLowerInvariant();
        return normalized switch
        {
            "json" => true,
            "text" => false,
            _ => throw new CliException("Format must be one of: json, text.", ExitCodes.Validation),
        };
    }
}

/// <summary>
/// Small command-line parser for the CLI's supported long options, short aliases, flags, and positionals.
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// Parses command arguments into options and positionals.
    /// </summary>
    /// <param name="args">The command-specific arguments to parse.</param>
    /// <param name="aliases">Short-option aliases accepted by the command.</param>
    /// <returns>The parsed command arguments.</returns>
    /// <exception cref="CliException">Thrown when an unknown short option is supplied.</exception>
    public static ParsedArguments Parse(string[] args, IReadOnlyDictionary<string, string> aliases)
        => Parse(args, aliases, valueOptions: null, flagOptions: null);

    /// <summary>
    /// Parses command arguments with a strict long-option schema.
    /// </summary>
    /// <param name="args">The command-specific arguments to parse.</param>
    /// <param name="aliases">Short-option aliases accepted by the command.</param>
    /// <param name="valueOptions">Long option names that require values.</param>
    /// <param name="flagOptions">Long option names that do not consume following positional values.</param>
    /// <returns>The parsed command arguments.</returns>
    /// <exception cref="CliException">Thrown when an option is unknown or a value is missing.</exception>
    public static ParsedArguments Parse(
        string[] args,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlySet<string>? valueOptions,
        IReadOnlySet<string>? flagOptions)
    {
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
        List<string> positionals = [];
        bool strict = valueOptions is not null || flagOptions is not null;
        valueOptions ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        flagOptions ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    ValidateLongOption(name, valueOptions, flagOptions, strict, token);
                    if (strict && valueOptions.Contains(name) && value.Length == 0)
                    {
                        throw new CliException($"Option '--{name}' expects a value.", ExitCodes.Usage);
                    }

                    if (strict && flagOptions.Contains(name))
                    {
                        ValidateFlagValue(name, value);
                    }
                }
                else
                {
                    name = optionToken;
                    ValidateLongOption(name, valueOptions, flagOptions, strict, token);
                    if (strict && flagOptions.Contains(name))
                    {
                        value = "true";
                    }
                    else if (index + 1 < args.Length && !LooksLikeLongOption(args[index + 1]) && args[index + 1] != "--")
                    {
                        value = args[++index];
                    }
                    else
                    {
                        if (strict && valueOptions.Contains(name))
                        {
                            throw new CliException($"Option '--{name}' expects a value.", ExitCodes.Usage);
                        }

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
                if (strict && flagOptions.Contains(mappedName))
                {
                    value = "true";
                }
                else if (index + 1 < args.Length && !LooksLikeLongOption(args[index + 1]) && args[index + 1] != "--")
                {
                    value = args[++index];
                }
                else
                {
                    if (strict && valueOptions.Contains(mappedName))
                    {
                        throw new CliException($"Option '{token}' expects a value.", ExitCodes.Usage);
                    }

                    value = "true";
                }

                options[mappedName] = value;
                continue;
            }

            positionals.Add(token);
        }

        return new ParsedArguments(options, positionals);
    }

    private static void ValidateLongOption(
        string name,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> flagOptions,
        bool strict,
        string token)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CliException($"Invalid option '{token}'.", ExitCodes.Usage);
        }

        if (strict && !valueOptions.Contains(name) && !flagOptions.Contains(name))
        {
            throw new CliException($"Unknown option '--{name}'.", ExitCodes.Usage);
        }
    }

    private static void ValidateFlagValue(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new CliException($"Option '--{name}' is a flag and only accepts true or false when assigned explicitly.", ExitCodes.Usage);
    }

    private static bool LooksLikeLongOption(string value)
        => value.StartsWith("--", StringComparison.Ordinal);
}