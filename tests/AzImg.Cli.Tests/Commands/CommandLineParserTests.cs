using AzImg.Cli.Commands;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Commands;

public class CommandLineParserTests
{
    [Fact]
    public void Parse_StopsOptionParsingAfterDoubleDash()
    {
        ParsedArguments parsed = CommandLineParser.Parse(["--", "--not-an-option"], new Dictionary<string, string>());

        Assert.Equal("--not-an-option", parsed.GetRequiredPositional(0, "missing"));
    }

    [Fact]
    public void Parse_StrictFlag_DoesNotConsumeFollowingPrompt()
    {
        ParsedArguments parsed = CommandLineParser.Parse(
            ["--write-manifest", "prompt"],
            new Dictionary<string, string>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(["write-manifest"], StringComparer.OrdinalIgnoreCase));

        Assert.True(parsed.GetFlag("write-manifest"));
        Assert.Equal("prompt", parsed.GetRequiredPositional(0, "missing"));
    }

    [Fact]
    public void Parse_StrictMode_RejectsUnknownLongOption()
    {
        CliException exception = Assert.Throws<CliException>(() => CommandLineParser.Parse(
            ["--cout", "2", "prompt"],
            new Dictionary<string, string>(),
            new HashSet<string>(["count"], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("Unknown option '--cout'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_StrictMode_RejectsMissingValue()
    {
        CliException exception = Assert.Throws<CliException>(() => CommandLineParser.Parse(
            ["--count"],
            new Dictionary<string, string>(),
            new HashSet<string>(["count"], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("expects a value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_StrictMode_RejectsInvalidFlagAssignment()
    {
        CliException exception = Assert.Throws<CliException>(() => CommandLineParser.Parse(
            ["--write-manifest=maybe"],
            new Dictionary<string, string>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(["write-manifest"], StringComparer.OrdinalIgnoreCase)));

        Assert.Equal(ExitCodes.Usage, exception.ExitCode);
        Assert.Contains("is a flag", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RepeatedValueOption_PreservesAllValuesAndReturnsLastValue()
    {
        ParsedArguments parsed = CommandLineParser.Parse(
            ["--image", "one.png", "--image=two.png", "input.png", "prompt"],
            new Dictionary<string, string>(),
            new HashSet<string>(["image"], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal("two.png", parsed.Get("image"));
        Assert.Equal(new[] { "one.png", "two.png" }, parsed.GetValues("image"));
        Assert.Equal("input.png", parsed.GetRequiredPositional(0, "missing"));
        Assert.Equal("prompt", parsed.GetRequiredPositional(1, "missing"));
    }

    [Fact]
    public void RequestsJsonOutput_DefaultsToJson()
    {
        ParsedArguments parsed = CommandLineParser.Parse([], new Dictionary<string, string>());

        Assert.True(parsed.RequestsJsonOutput());
    }

    [Fact]
    public void RequestsJsonOutput_AcceptsExplicitFormatJsonAssignment()
    {
        ParsedArguments parsed = CommandLineParser.Parse(["--format=json"], new Dictionary<string, string>());

        Assert.True(parsed.RequestsJsonOutput());
    }

    [Fact]
    public void RequestsJsonOutput_AcceptsExplicitFormatJsonValue()
    {
        ParsedArguments parsed = CommandLineParser.Parse(["--format", "json"], new Dictionary<string, string>());

        Assert.True(parsed.RequestsJsonOutput());
    }

    [Fact]
    public void RequestsJsonOutput_AcceptsExplicitFormatText()
    {
        ParsedArguments parsed = CommandLineParser.Parse(["--format", "text"], new Dictionary<string, string>());

        Assert.False(parsed.RequestsJsonOutput());
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--json=true")]
    public void RequestsJsonOutput_RejectsRemovedJsonFlag(string token)
    {
        ParsedArguments parsed = CommandLineParser.Parse([token], new Dictionary<string, string>());

        CliException exception = Assert.Throws<CliException>(() => parsed.RequestsJsonOutput());

        Assert.Equal(ExitCodes.Validation, exception.ExitCode);
        Assert.Contains("JSON is the default", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandOutputPreferences_DefaultsToJson()
    {
        Assert.True(CommandOutputPreferences.RequestsJson(["version"]));
    }

    [Fact]
    public void CommandOutputPreferences_DetectsExplicitTextValue()
    {
        Assert.False(CommandOutputPreferences.RequestsJson(["--format", "text"]));
    }

    [Fact]
    public void CommandOutputPreferences_DetectsExplicitTextAssignment()
    {
        Assert.False(CommandOutputPreferences.RequestsJson(["--format=text"]));
    }
}