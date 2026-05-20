using System.Text.Json;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Infrastructure.AzureOpenAI;

/// <summary>
/// Converts Azure OpenAI service failures into user-friendly CLI exceptions.
/// </summary>
/// <remarks>
/// The Azure SDK exception message can contain a raw JSON response or a service-specific text message.
/// This mapper extracts the most useful message and assigns an exit code that automation can interpret.
/// </remarks>
public static class AzureOpenAIErrorTranslator
{
    private const string AzureImageGenerationAction = "Microsoft.CognitiveServices/accounts/OpenAI/images/generations/action";

    /// <summary>
    /// Translates one failed HTTP response into a <see cref="CliException" />.
    /// </summary>
    /// <param name="statusCode">The service HTTP status code.</param>
    /// <param name="responseBody">The raw service error body or SDK message.</param>
    /// <returns>A CLI exception with an appropriate message and exit code.</returns>
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