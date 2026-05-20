using AzImg.Cli.ImageOperations;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.ImageOperations;

public class ImageOperationRequestValidatorTests
{
    [Theory]
    [InlineData("1024x1024")]
    [InlineData("1536x1024")]
    public void NormalizeSize_AcceptsSupportedDimensions(string value)
    {
        ImageOperationRequestValidator validator = new();

        string? normalized = validator.NormalizeSize(value);

        Assert.Equal(value, normalized);
    }

    [Fact]
    public void NormalizeSize_RejectsNonDivisibleSizes()
    {
        ImageOperationRequestValidator validator = new();

        CliException exception = Assert.Throws<CliException>(() => validator.NormalizeSize("1000x1000"));

        Assert.Equal(ExitCodes.Validation, exception.ExitCode);
    }

    [Fact]
    public void ValidateGenerate_RejectsZeroCount()
    {
        ImageOperationRequestValidator validator = new();
        GenerateImageRequest request = new(
            "prompt",
            0,
            "1024x1024",
            "high",
            "auto",
            "png",
            100,
            null,
            "{id}-{index}",
            false);

        CliException exception = Assert.Throws<CliException>(() => validator.ValidateGenerate(request));
        Assert.Equal(ExitCodes.Validation, exception.ExitCode);
    }
}