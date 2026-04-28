using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AzureOpenAI.ImageGen.Cli.Infrastructure;

public static class ExitCodes
{
    public const int Success = 0;
    public const int Usage = 1;
    public const int Validation = 2;
    public const int Configuration = 3;
    public const int Authentication = 4;
    public const int Io = 5;
    public const int Cancelled = 130;
    public const int Unhandled = 255;
}

public static class CliDefaults
{
    public const string ProductName = "Azure OpenAI Image CLI";
    public const string CommandName = "azimg";
    public const string UserAgentApplicationId = "azimg";
    public const string ConfigDirectoryName = ".azimg";
    public const string ConfigFileName = "config.json";
    public const string AzureTokenScope = "https://cognitiveservices.azure.com/.default";
    public static TimeSpan ImageRequestTimeout { get; } = TimeSpan.FromMinutes(20);
}

public sealed class CliException : Exception
{
    public CliException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}

public static class JsonDefaults
{
    public static string Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(value, typeInfo);

    public static Task SerializeAsync<T>(
        Stream utf8Json,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        => JsonSerializer.SerializeAsync(utf8Json, value, typeInfo, cancellationToken);

    public static ValueTask<T?> DeserializeAsync<T>(
        Stream utf8Json,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        => JsonSerializer.DeserializeAsync(utf8Json, typeInfo, cancellationToken);

    public static byte[] SerializeToUtf8Bytes<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
}

public static class ServiceErrorTranslator
{
    private const string AzureImageGenerationAction = "Microsoft.CognitiveServices/accounts/OpenAI/images/generations/action";

    public static CliException TranslateHttpFailure(int statusCode, string? responseBody)
    {
        string message = TryReadMessage(responseBody) ?? responseBody?.Trim() ?? "The service returned an empty error response.";

        if ((statusCode == 401 || statusCode == 403)
            && message.Contains(AzureImageGenerationAction, StringComparison.Ordinal)
            && message.Contains("lacks the required data action", StringComparison.OrdinalIgnoreCase))
        {
            return new CliException(
                "Azure OpenAI authenticated your Entra identity, but Azure RBAC denied image generation. Grant the 'Cognitive Services OpenAI User' role on the Azure OpenAI resource, wait for propagation, then try again.",
                ExitCodes.Authentication);
        }

        if (statusCode == 401 || statusCode == 403)
        {
            return new CliException($"Service authorization failed ({statusCode}). {message}", ExitCodes.Authentication);
        }

        if (statusCode >= 400 && statusCode < 500)
        {
            return new CliException($"Service request failed ({statusCode}). {message}", ExitCodes.Validation);
        }

        return new CliException($"Service request failed ({statusCode}). {message}", ExitCodes.Unhandled);
    }

    private static string? TryReadMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement messageElement))
                {
                    return messageElement.GetString();
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }
            }

            if (document.RootElement.TryGetProperty("message", out JsonElement rootMessage))
            {
                return rootMessage.GetString();
            }
        }
        catch (JsonException)
        {
            return responseBody.Trim();
        }

        return responseBody.Trim();
    }
}
