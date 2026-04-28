using AzureOpenAI.ImageGen.Cli.Infrastructure;
using AzureOpenAI.ImageGen.Cli.Services;

CancellationTokenSource cancellationTokenSource = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

ConfigStore configStore = new();
ProfileResolver profileResolver = new();
RequestValidator validator = new();
AzureCredentialProvider credentialProvider = new();
AzureImageService imageService = new(credentialProvider);
FileOutputService outputService = new();
DoctorService doctorService = new(credentialProvider);
CliApplication application = new(configStore, profileResolver, validator, imageService, outputService, doctorService);

try
{
    return await application.RunAsync(args, cancellationTokenSource.Token);
}
catch (CliException ex)
{
    Console.Error.WriteLine(ex.Message);
    return ex.ExitCode;
}
catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
{
    Console.Error.WriteLine("Operation cancelled.");
    return ExitCodes.Cancelled;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unhandled error. {ex.Message}");
    return ExitCodes.Unhandled;
}
