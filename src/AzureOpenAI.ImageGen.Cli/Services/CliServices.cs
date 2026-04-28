using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using AzureOpenAI.ImageGen.Cli.Infrastructure;
using AzureOpenAI.ImageGen.Cli.Models;
using OpenAI.Images;

namespace AzureOpenAI.ImageGen.Cli.Services;

#pragma warning disable OPENAI001

public sealed class ProfileResolver
{
    public ResolvedProfile Resolve(AppConfig? config, ProfileOverrides overrides)
    {
        ProfileConfig? profile = null;
        string profileName = overrides.ProfileName ?? config?.DefaultProfile ?? "inline";

        if (!string.IsNullOrWhiteSpace(overrides.ProfileName))
        {
            if (config is null || !config.Profiles.TryGetValue(overrides.ProfileName, out profile))
            {
                throw new CliException($"The profile '{overrides.ProfileName}' was not found in the configuration file.", ExitCodes.Configuration);
            }
        }
        else if (!string.IsNullOrWhiteSpace(config?.DefaultProfile))
        {
            if (!config!.Profiles.TryGetValue(config.DefaultProfile, out profile))
            {
                throw new CliException($"The default profile '{config.DefaultProfile}' was not found in the configuration file.", ExitCodes.Configuration);
            }
        }

        string? deployment = overrides.Deployment ?? profile?.Deployment;
        if (string.IsNullOrWhiteSpace(deployment))
        {
            throw new CliException("Azure OpenAI requires a deployment name. Provide --deployment or configure one in the selected profile.", ExitCodes.Configuration);
        }

        string? endpointValue = overrides.Endpoint ?? profile?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            throw new CliException("Azure OpenAI requires an endpoint. Provide --endpoint or configure one in the selected profile.", ExitCodes.Configuration);
        }

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint))
        {
            throw new CliException($"The Azure endpoint '{endpointValue}' is not a valid absolute URI.", ExitCodes.Configuration);
        }

        string outputDirectory = Path.GetFullPath(
            overrides.OutputDirectory
            ?? profile?.OutputDirectory
            ?? Path.Combine(Environment.CurrentDirectory, "output"));

        return new ResolvedProfile(profileName, deployment, endpoint, outputDirectory);
    }
}

public sealed class RequestValidator
{
    private static readonly System.Text.RegularExpressions.Regex CustomSizePattern =
        new(@"^(?<width>\d+)x(?<height>\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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

    public string? NormalizeSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        System.Text.RegularExpressions.Match match = CustomSizePattern.Match(value.Trim());
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
}

public sealed class AzureCredentialProvider
{
    private static readonly TokenRequestContext TokenRequestContext = new([CliDefaults.AzureTokenScope]);

    private readonly DefaultAzureCredential _credential = new(new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true,
    });

    public TokenCredential Credential => _credential;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            AccessToken token = await _credential.GetTokenAsync(TokenRequestContext, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token.Token))
            {
                throw new CliException(
                    "DefaultAzureCredential returned an empty Azure access token. Sign in with 'az login' and try again.",
                    ExitCodes.Authentication);
            }

            return token.Token;
        }
        catch (CredentialUnavailableException)
        {
            throw new CliException(
                "Azure authentication is unavailable. Sign in with 'az login' or configure another DefaultAzureCredential source.",
                ExitCodes.Authentication);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new CliException(
                $"DefaultAzureCredential failed to get an Azure token. Sign in with 'az login' and try again. {ex.Message}",
                ExitCodes.Authentication);
        }
    }
}

public sealed class AzureImageService
{
    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = CliDefaults.ImageRequestTimeout,
    };

    private readonly AzureCredentialProvider _credentialProvider;
    private readonly ConcurrentDictionary<string, AzureOpenAIClient> _azureClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageClient> _imageClients = new(StringComparer.OrdinalIgnoreCase);

    public AzureImageService(AzureCredentialProvider credentialProvider)
    {
        _credentialProvider = credentialProvider;
    }

    public async Task<ImageOperationResult> GenerateAsync(ResolvedProfile profile, GenerateImageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            ImageClient client = GetImageClient(profile);
            ImageGenerationOptions options = CreateGenerationOptions(request);
            ClientResult<GeneratedImageCollection> response = await client.GenerateImagesAsync(
                request.Prompt,
                request.Count,
                options,
                cancellationToken).ConfigureAwait(false);

            return await ToResultAsync(response.Value, request.OutputFormat, profile.DeploymentName, cancellationToken).ConfigureAwait(false);
        }
        catch (ClientResultException ex)
        {
            throw ServiceErrorTranslator.TranslateHttpFailure(ex.Status, ex.Message);
        }
        catch (CredentialUnavailableException)
        {
            throw new CliException(
                "Azure authentication is unavailable. Sign in with 'az login' or configure another DefaultAzureCredential source.",
                ExitCodes.Authentication);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new CliException(
                $"DefaultAzureCredential failed to get an Azure token. Sign in with 'az login' and try again. {ex.Message}",
                ExitCodes.Authentication);
        }
    }

    public async Task<ImageOperationResult> EditAsync(ResolvedProfile profile, EditImageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            ImageClient client = GetImageClient(profile);
            ImageEditOptions options = CreateEditOptions(request);

            await using FileStream imageStream = File.OpenRead(request.InputFile);
            ClientResult<GeneratedImageCollection> response;

            if (!string.IsNullOrWhiteSpace(request.MaskFile))
            {
                await using FileStream maskStream = File.OpenRead(request.MaskFile);
                response = await client.GenerateImageEditsAsync(
                    imageStream,
                    Path.GetFileName(request.InputFile),
                    request.Prompt,
                    maskStream,
                    Path.GetFileName(request.MaskFile),
                    request.Count,
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                response = await client.GenerateImageEditsAsync(
                    imageStream,
                    Path.GetFileName(request.InputFile),
                    request.Prompt,
                    request.Count,
                    options,
                    cancellationToken).ConfigureAwait(false);
            }

            return await ToResultAsync(response.Value, request.OutputFormat, profile.DeploymentName, cancellationToken).ConfigureAwait(false);
        }
        catch (ClientResultException ex)
        {
            throw ServiceErrorTranslator.TranslateHttpFailure(ex.Status, ex.Message);
        }
        catch (CredentialUnavailableException)
        {
            throw new CliException(
                "Azure authentication is unavailable. Sign in with 'az login' or configure another DefaultAzureCredential source.",
                ExitCodes.Authentication);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new CliException(
                $"DefaultAzureCredential failed to get an Azure token. Sign in with 'az login' and try again. {ex.Message}",
                ExitCodes.Authentication);
        }
    }

    private ImageClient GetImageClient(ResolvedProfile profile)
    {
        string endpoint = profile.Endpoint.AbsoluteUri.TrimEnd('/');
        string key = $"{endpoint}|{profile.DeploymentName}";
        return _imageClients.GetOrAdd(
            key,
            _ => GetAzureClient(profile.Endpoint).GetImageClient(profile.DeploymentName));
    }

    private AzureOpenAIClient GetAzureClient(Uri endpoint)
    {
        string key = endpoint.AbsoluteUri.TrimEnd('/');
        return _azureClients.GetOrAdd(key, _ => CreateAzureClient(endpoint));
    }

    private AzureOpenAIClient CreateAzureClient(Uri endpoint)
    {
        AzureOpenAIClientOptions options = new()
        {
            Audience = CliDefaults.AzureTokenScope,
            UserAgentApplicationId = CliDefaults.UserAgentApplicationId,
            NetworkTimeout = CliDefaults.ImageRequestTimeout,
        };

        return new AzureOpenAIClient(endpoint, _credentialProvider.Credential, options);
    }

    private static ImageGenerationOptions CreateGenerationOptions(GenerateImageRequest request)
    {
        ImageGenerationOptions options = new()
        {
            Size = CreateGeneratedImageSize(request.Size),
            Quality = CreateGeneratedImageQuality(request.Quality),
            Background = CreateGeneratedImageBackground(request.Background),
            OutputFileFormat = CreateGeneratedImageFileFormat(request.OutputFormat),
            OutputCompressionFactor = request.OutputCompression,
        };

        if (!string.IsNullOrWhiteSpace(request.EndUserId))
        {
            options.EndUserId = request.EndUserId;
        }

        return options;
    }

    private static ImageEditOptions CreateEditOptions(EditImageRequest request)
    {
        ImageEditOptions options = new()
        {
            Size = CreateGeneratedImageSize(request.Size),
            Quality = CreateGeneratedImageQuality(request.Quality),
            Background = CreateGeneratedImageBackground(request.Background),
            OutputFileFormat = CreateGeneratedImageFileFormat(request.OutputFormat),
            OutputCompressionFactor = request.OutputCompression,
        };

        if (!string.IsNullOrWhiteSpace(request.EndUserId))
        {
            options.EndUserId = request.EndUserId;
        }

        return options;
    }

    private static GeneratedImageSize? CreateGeneratedImageSize(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            string[] parts = value.Split('x', 2, StringSplitOptions.TrimEntries);
            int width = int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            int height = int.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
            return new GeneratedImageSize(width, height);
        }

        return null;
    }

    private static GeneratedImageQuality? CreateGeneratedImageQuality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (GeneratedImageQuality?)null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "auto" => (GeneratedImageQuality?)null,
            "low" or "medium" or "high" or "standard" => new GeneratedImageQuality(normalized),
            _ => throw new CliException("Unsupported quality option.", ExitCodes.Validation),
        };
    }

    private static GeneratedImageBackground? CreateGeneratedImageBackground(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (GeneratedImageBackground?)null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "auto" => (GeneratedImageBackground?)null,
            "opaque" or "transparent" => new GeneratedImageBackground(normalized),
            _ => throw new CliException("Unsupported background option.", ExitCodes.Validation),
        };
    }

    private static GeneratedImageFileFormat? CreateGeneratedImageFileFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (GeneratedImageFileFormat?)null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "png" => GeneratedImageFileFormat.Png,
            "jpeg" or "jpg" => GeneratedImageFileFormat.Jpeg,
            "webp" => GeneratedImageFileFormat.Webp,
            _ => throw new CliException("Unsupported output format option.", ExitCodes.Validation),
        };
    }

    private static async Task<ImageOperationResult> ToResultAsync(
        GeneratedImageCollection payload,
        string? requestedOutputFormat,
        string deploymentName,
        CancellationToken cancellationToken)
    {
        if (payload.Count == 0)
        {
            throw new CliException("The service returned no images.", ExitCodes.Unhandled);
        }

        List<GeneratedImageArtifact> images = new(payload.Count);
        for (int index = 0; index < payload.Count; index++)
        {
            GeneratedImage image = payload[index];
            byte[] content = await ResolveImageBytesAsync(image, cancellationToken).ConfigureAwait(false);
            images.Add(new GeneratedImageArtifact(index + 1, content, ResolveExtension(requestedOutputFormat, image)));
        }

        ImageUsageSnapshot? usage = payload.Usage is null
            ? null
            : new ImageUsageSnapshot(
                payload.Usage.InputTokenCount,
                payload.Usage.OutputTokenCount,
                payload.Usage.TotalTokenCount);

        return new ImageOperationResult(images, usage, payload.CreatedAt, deploymentName);
    }

    private static async Task<byte[]> ResolveImageBytesAsync(GeneratedImage image, CancellationToken cancellationToken)
    {
        if (image.ImageBytes is not null)
        {
            return image.ImageBytes.ToArray();
        }

        if (image.ImageUri is not null)
        {
            try
            {
                return await DownloadClient.GetByteArrayAsync(image.ImageUri, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new CliException(
                    $"The service returned an image URL, but the CLI could not download it. {ex.Message}",
                    ExitCodes.Unhandled);
            }
        }

        throw new CliException("The service returned an image without byte data or a download URL.", ExitCodes.Unhandled);
    }

    private static string ResolveExtension(string? requestedOutputFormat, GeneratedImage image)
    {
        if (!string.IsNullOrWhiteSpace(requestedOutputFormat))
        {
            return requestedOutputFormat.Trim().ToLowerInvariant() switch
            {
                "jpeg" or "jpg" => "jpg",
                "webp" => "webp",
                _ => "png",
            };
        }

        if (image.ImageUri is not null)
        {
            return Path.GetExtension(image.ImageUri.AbsolutePath).TrimStart('.').ToLowerInvariant() switch
            {
                "jpeg" or "jpg" => "jpg",
                "webp" => "webp",
                "png" => "png",
                _ => "png",
            };
        }

        return "png";
    }
}

public sealed class FileOutputService
{
    private const int MaxOutputLeafLength = 200;

    public async Task<SaveImagesResult> SaveAsync(
        ResolvedProfile profile,
        string prompt,
        string nameTemplate,
        bool writeManifest,
        ImageOperationResult result,
        CancellationToken cancellationToken)
    {
        string fullOutputDirectory = Path.GetFullPath(profile.OutputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        List<SavedImageArtifact> files = [];
        string timestamp = result.CreatedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string operationId = Guid.NewGuid().ToString("N")[..8];
        string slug = Slugify(prompt);

        foreach (GeneratedImageArtifact image in result.Images)
        {
            string fileName = RenderTemplate(nameTemplate, timestamp, operationId, slug, image.Index, profile);
            string path = BuildOutputPath(fullOutputDirectory, fileName, $".{image.Extension}");
            await WriteFileAtomicallyAsync(path, image.Content, cancellationToken);
            files.Add(new SavedImageArtifact(image.Index, path, Hashing.ComputeSha256(image.Content), image.Content.LongLength));
        }

        string? manifestPath = null;
        if (writeManifest)
        {
            manifestPath = BuildOutputPath(fullOutputDirectory, RenderTemplate(nameTemplate, timestamp, operationId, slug, 0, profile), ".manifest.json");
            ManifestDocument manifest = new(
                prompt,
                "azure-openai",
                result.DeploymentName,
                result.CreatedAt,
                result.Usage,
                files.ToArray());

            byte[] content = JsonDefaults.SerializeToUtf8Bytes(manifest, CliJsonContext.Default.ManifestDocument);
            await WriteFileAtomicallyAsync(manifestPath, content, cancellationToken);
        }

        return new SaveImagesResult(files, manifestPath);
    }

    private static async Task WriteFileAtomicallyAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new CliException($"The output path '{path}' is invalid.", ExitCodes.Io);
        }

        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $".azimg-writing-{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, path, true);
    }

    private static string BuildOutputPath(string directory, string fileName, string suffix)
        => Path.Combine(directory, CreateLeafName(fileName, suffix));

    private static string CreateLeafName(string fileName, string suffix)
    {
        if (suffix.Length >= MaxOutputLeafLength)
        {
            throw new CliException("The output file suffix is too long to save safely.", ExitCodes.Io);
        }

        string normalizedName = string.IsNullOrWhiteSpace(fileName) ? "image" : fileName;
        int maxBaseLength = MaxOutputLeafLength - suffix.Length;
        if (normalizedName.Length > maxBaseLength)
        {
            throw new CliException(
                "The rendered output file name is too long. Use a shorter --name-template or output directory.",
                ExitCodes.Io);
        }

        return $"{normalizedName}{suffix}";
    }

    private static string RenderTemplate(string template, string timestamp, string operationId, string slug, int index, ResolvedProfile profile)
    {
        string effectiveTemplate = string.IsNullOrWhiteSpace(template) ? "{id}-{index}" : template;
        string fileName = effectiveTemplate
            .Replace("{timestamp}", timestamp, StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", operationId, StringComparison.OrdinalIgnoreCase)
            .Replace("{slug}", slug, StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", index.ToString("D2", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{profile}", Slugify(profile.Name), StringComparison.OrdinalIgnoreCase);

        return Slugify(fileName);
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "image";
        }

        StringBuilder builder = new(value.Length);
        bool previousWasSeparator = false;

        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        string slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "image" : slug;
    }
}

public sealed class DoctorService
{
    private readonly AzureCredentialProvider _credentialProvider;

    public DoctorService(AzureCredentialProvider credentialProvider)
    {
        _credentialProvider = credentialProvider;
    }

    public async Task<DoctorReport> RunAsync(
        string configPath,
        AppConfig? config,
        ResolvedProfile profile,
        bool verifyAuth,
        CancellationToken cancellationToken)
    {
        bool usesInlineProfile = config is null && string.Equals(profile.Name, "inline", StringComparison.OrdinalIgnoreCase);
        List<DoctorCheck> checks =
        [
            new(
                "config-file",
                config is not null || usesInlineProfile,
                config is not null
                    ? "Configuration file loaded."
                    : "No configuration file found; using inline or override settings."),
            new("output-directory", CanPrepareDirectory(profile.OutputDirectory), $"Output directory: {profile.OutputDirectory}"),
            new("deployment", !string.IsNullOrWhiteSpace(profile.DeploymentName), $"Deployment: {profile.DeploymentName}"),
            new("azure-endpoint", true, profile.Endpoint.ToString()),
        ];

        if (!verifyAuth)
        {
            checks.Add(new DoctorCheck(
                "azure-identity",
                true,
                "This CLI uses DefaultAzureCredential. Run with --verify-auth to test the current credential chain, including your az login session."));
        }
        else
        {
            try
            {
                string token = await _credentialProvider.GetAccessTokenAsync(cancellationToken);
                checks.Add(new DoctorCheck("azure-identity", !string.IsNullOrWhiteSpace(token), "Successfully acquired an Azure bearer token."));
            }
            catch (CliException ex)
            {
                checks.Add(new DoctorCheck("azure-identity", false, ex.Message));
            }
        }

        return new DoctorReport(configPath, profile.Name, checks);
    }

    private static bool CanPrepareDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

#pragma warning restore OPENAI001
