using AzImg.Cli.ImageOperations;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.ImageOperations;

public class AzureOpenAIErrorTranslatorTests
{
    [Fact]
    public void Translate_AzureImageGenerationRoleError_ReturnsFriendlyCliException()
    {
        CliException exception = AzureOpenAIErrorTranslator.TranslateHttpFailure(
            401,
            "{\"error\":{\"message\":\"The principal user@example.com lacks the required data action 'Microsoft.CognitiveServices/accounts/OpenAI/images/generations/action' to perform the request.\"}}");

        Assert.Equal(ExitCodes.Authentication, exception.ExitCode);
        Assert.Contains("Cognitive Services OpenAI User", exception.Message, StringComparison.Ordinal);
    }
}