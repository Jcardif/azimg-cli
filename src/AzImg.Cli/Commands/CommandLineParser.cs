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