namespace AzImg.Cli.Commands;

/// <summary>
/// Detects output preferences from raw command-line arguments before full command parsing is available.
/// </summary>
/// <remarks>
/// This helper is used by top-level exception handling so failures stay structured even when an error
/// happens before the command-specific parser returns. JSON is the default; <c>--format text</c> is the
/// explicit opt-in for human-readable errors.
/// </remarks>
public static class CommandOutputPreferences
{
    /// <summary>
    /// Gets whether the raw arguments select JSON output.
    /// </summary>
    public static bool RequestsJson(IReadOnlyList<string> args)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string token = args[index];
            if (token.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
            {
                string value = token["--format=".Length..];
                return !value.Equals("text", StringComparison.OrdinalIgnoreCase);
            }

            if (token.Equals("--format", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Count)
            {
                return !args[index + 1].Equals("text", StringComparison.OrdinalIgnoreCase);
            }
        }

        return true;
    }
}