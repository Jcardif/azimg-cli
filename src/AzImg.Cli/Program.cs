using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Application.AgentSkills;
using AzImg.Cli.Commands;
using AzImg.Cli.Configuration;
using AzImg.Cli.Diagnostics;
using AzImg.Cli.Infrastructure.AzureOpenAI;
using AzImg.Cli.Infrastructure.FileSystem;
using AzImg.Cli.Runtime;
using AzImg.Cli.Updates;

CancellationTokenSource cancellationTokenSource = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

ConfigurationStore configurationStore = new();
ProfileResolver profileResolver = new();
GeneratedImageRequestValidator validator = new();
AzureCliCredentialProvider credentialProvider = new();
AzureOpenAIImageClient imageClient = new(credentialProvider);
ImageFileStore imageFileStore = new();
DiagnosticService diagnosticService = new(credentialProvider);
HelpTextProvider helpText = new();
UpdateService updateService = new();
AgentSkillInstaller agentSkillInstaller = new();
CommandDispatcher application = new(
    configurationStore,
    profileResolver,
    validator,
    imageClient,
    imageFileStore,
    diagnosticService,
    helpText,
    updateService,
    agentSkillInstaller);

try
{
    return await application.RunAsync(args, cancellationTokenSource.Token);
}
catch (CliException ex)
{
    WriteError(args, ex.Message, ex.ExitCode, ex.ErrorCode);
    return ex.ExitCode;
}
catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
{
    WriteError(args, "Operation cancelled.", ExitCodes.Cancelled, "cancelled");
    return ExitCodes.Cancelled;
}
catch (Exception ex)
{
    WriteError(args, $"Unhandled error. {ex.Message}", ExitCodes.Unhandled, "unhandled");
    return ExitCodes.Unhandled;
}

static void WriteError(string[] args, string message, int exitCode, string errorCode)
{
    if (CommandOutputPreferences.RequestsJson(args))
    {
        CliErrorDocument document = new(new CliErrorInfo(errorCode, message, exitCode));
        try
        {
            Console.Error.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.CliErrorDocument));
        }
        catch (Exception ex) when (IsAssemblyLoadFailure(ex))
        {
            Console.Error.WriteLine(CreateFallbackJsonError(errorCode, message, exitCode));
        }

        return;
    }

    Console.Error.WriteLine(message);
}

static string CreateFallbackJsonError(string errorCode, string message, int exitCode)
    => "{\"error\":{\"code\":\""
        + EscapeJsonString(errorCode)
        + "\",\"message\":\""
        + EscapeJsonString(message)
        + "\",\"exitCode\":"
        + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "}}";

static string EscapeJsonString(string value)
{
    return value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);
}

static bool IsAssemblyLoadFailure(Exception exception)
    => exception is FileNotFoundException or FileLoadException or BadImageFormatException
        || (exception.InnerException is not null && IsAssemblyLoadFailure(exception.InnerException));
