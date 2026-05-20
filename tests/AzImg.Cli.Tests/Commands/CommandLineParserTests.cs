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