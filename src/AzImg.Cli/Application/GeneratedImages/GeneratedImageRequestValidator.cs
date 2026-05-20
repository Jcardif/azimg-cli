using System.Globalization;
using System.Text.RegularExpressions;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Application.GeneratedImages;

/// <summary>
/// Validates and normalizes image generation and editing request options before Azure calls are made.
/// </summary>
/// <remarks>
/// Validation is intentionally performed before any network call so bad command lines fail quickly and
/// non-interactively. Normalization methods are public to keep edge cases easy to test directly.
/// </remarks>
public sealed class GeneratedImageRequestValidator
{
    private static readonly Regex CustomSizePattern = new(
        @"^(?<width>\d+)x(?<height>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Validates an image generation request before Azure calls are made.
    /// </summary>
    /// <param name="request">The request assembled from command-line input.</param>
    /// <exception cref="CliException">Thrown when the prompt, count, or shared image options are invalid.</exception>
    public void ValidateGenerate(GenerateImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new CliException("Prompt is required.", ExitCodes.Validation);
        }

        if (request.Count is < 1 or > 10)
        {
            throw new CliException("Count must be between 1 and 10.", ExitCodes.Validation);
        }

        ValidateSharedOptions(request.Size, request.Quality, request.Background, request.OutputFormat, request.OutputCompression);
    }

    /// <summary>
    /// Validates an image edit request, including local input and mask file existence.
    /// </summary>
    /// <param name="request">The edit request assembled from command-line input.</param>
    /// <exception cref="CliException">Thrown when required files or shared image options are invalid.</exception>
    public void ValidateEdit(EditImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new CliException("Prompt is required.", ExitCodes.Validation);
        }

        if (!File.Exists(request.InputFile))
        {
            throw new CliException($"Input image '{request.InputFile}' was not found.", ExitCodes.Validation);
        }

        if (!string.IsNullOrWhiteSpace(request.MaskFile) && !File.Exists(request.MaskFile))
        {
            throw new CliException($"Mask image '{request.MaskFile}' was not found.", ExitCodes.Validation);
        }

        if (request.Count is < 1 or > 10)
        {
            throw new CliException("Count must be between 1 and 10.", ExitCodes.Validation);
        }

        ValidateSharedOptions(request.Size, request.Quality, request.Background, request.OutputFormat, request.OutputCompression);
    }

    /// <summary>
    /// Normalizes a custom image size in <c>WIDTHxHEIGHT</c> format.
    /// </summary>
    /// <param name="value">The user-supplied size value.</param>
    /// <returns>A canonical size string, or <see langword="null" /> when no value was supplied.</returns>
    /// <exception cref="CliException">Thrown when the size syntax or dimensions are invalid.</exception>
    public string? NormalizeSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match match = CustomSizePattern.Match(value.Trim());
        if (!match.Success)
        {
            throw new CliException("Size must use WIDTHxHEIGHT format, for example 1024x1024.", ExitCodes.Validation);
        }

        int width = int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture);
        int height = int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);
        if (width <= 0 || height <= 0)
        {
            throw new CliException("Image width and height must be positive.", ExitCodes.Validation);
        }

        if (width > 4096 || height > 4096)
        {
            throw new CliException("Image width and height must not exceed 4096.", ExitCodes.Validation);
        }

        if (width % 16 != 0 || height % 16 != 0)
        {
            throw new CliException("Custom image sizes must use dimensions divisible by 16.", ExitCodes.Validation);
        }

        return $"{width}x{height}";
    }

    /// <summary>
    /// Normalizes image quality input to values accepted by the Azure image API.
    /// </summary>
    public string? NormalizeQuality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "auto" => null,
            "low" or "medium" or "high" => normalized,
            _ => throw new CliException("Quality must be one of: auto, low, medium, high.", ExitCodes.Validation),
        };
    }

    /// <summary>
    /// Normalizes image background input to values accepted by the Azure image API.
    /// </summary>
    public string? NormalizeBackground(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "auto" => null,
            "opaque" or "transparent" => normalized,
            _ => throw new CliException("Background must be one of: auto, opaque, transparent.", ExitCodes.Validation),
        };
    }

    /// <summary>
    /// Normalizes output image format input to the canonical file format value.
    /// </summary>
    public string? NormalizeOutputFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "png" => "png",
            "jpeg" or "jpg" => "jpeg",
            "webp" => "webp",
            _ => throw new CliException("Output format must be one of: png, jpeg, webp.", ExitCodes.Validation),
        };
    }

    private void ValidateSharedOptions(string? size, string? quality, string? background, string? outputFormat, int? outputCompression)
    {
        _ = NormalizeSize(size);
        _ = NormalizeQuality(quality);
        _ = NormalizeBackground(background);
        _ = NormalizeOutputFormat(outputFormat);

        if (outputCompression is < 0 or > 100)
        {
            throw new CliException("Output compression must be between 0 and 100.", ExitCodes.Validation);
        }
    }
}