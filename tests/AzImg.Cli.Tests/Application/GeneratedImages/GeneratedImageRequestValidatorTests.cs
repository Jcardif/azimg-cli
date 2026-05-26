using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Application.GeneratedImages;

public class GeneratedImageRequestValidatorTests
{
    [Theory]
    [InlineData("1024x1024")]
    [InlineData("1536x1024")]
    public void NormalizeSize_AcceptsSupportedDimensions(string value)
    {
        GeneratedImageRequestValidator validator = new();

        string? normalized = validator.NormalizeSize(value);

        Assert.Equal(value, normalized);
    }

    [Fact]
    public void NormalizeSize_RejectsNonDivisibleSizes()
    {
        GeneratedImageRequestValidator validator = new();

        CliException exception = Assert.Throws<CliException>(() => validator.NormalizeSize("1000x1000"));

        Assert.Equal(ExitCodes.Validation, exception.ExitCode);
    }

    [Fact]
    public void ValidateGenerate_RejectsZeroCount()
    {
        GeneratedImageRequestValidator validator = new();
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

    [Fact]
    public void ValidateEdit_RejectsMoreThanSixteenInputImages()
    {
        GeneratedImageRequestValidator validator = new();
        EditImageRequest request = new(
            Enumerable.Range(0, 17).Select(index => $"image-{index}.png").ToArray(),
            null,
            "prompt",
            1,
            "1024x1024",
            "high",
            "auto",
            "png",
            100,
            null,
            "{id}-{index}",
            false);

        CliException exception = Assert.Throws<CliException>(() => validator.ValidateEdit(request));

        Assert.Equal(ExitCodes.Validation, exception.ExitCode);
        Assert.Contains("at most 16 input images", exception.Message, StringComparison.Ordinal);
    }
}