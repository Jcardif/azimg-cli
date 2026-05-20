using AzImg.Cli.Application.GeneratedImages;
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
CommandDispatcher application = new(
    configurationStore,
    profileResolver,
    validator,
    imageClient,
    imageFileStore,
    diagnosticService,
    helpText,
    updateService);

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
        Console.Error.WriteLine(JsonDefaults.Serialize(document, CliJsonContext.Default.CliErrorDocument));
        return;
    }

    Console.Error.WriteLine(message);
}