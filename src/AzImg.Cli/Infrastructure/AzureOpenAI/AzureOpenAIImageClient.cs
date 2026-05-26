using System.ClientModel;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Configuration;
using AzImg.Cli.Runtime;
using OpenAI.Images;

namespace AzImg.Cli.Infrastructure.AzureOpenAI;

#pragma warning disable OPENAI001

/// <summary>
/// Defines the image operations needed by the command dispatcher.
/// </summary>
public interface IGeneratedImageClient
{
    /// <summary>Generates images from a text prompt.</summary>
    Task<GeneratedImageResult> GenerateAsync(ResolvedProfile profile, GenerateImageRequest request, CancellationToken cancellationToken);

    /// <summary>Edits an existing image using a text prompt.</summary>
    Task<GeneratedImageResult> EditAsync(ResolvedProfile profile, EditImageRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Calls Azure OpenAI image generation and image editing APIs and normalizes their responses.
/// </summary>
/// <remarks>
/// The Azure OpenAI image APIs used here are currently exposed through SDK beta surface area, so this
/// file scopes the <c>OPENAI001</c> suppression to the smallest practical area. Command handlers validate
/// requests before this service is called; this class focuses on SDK option mapping, response conversion,
/// authentication failure handling, and image-byte download fallback.
/// </remarks>
public sealed class AzureOpenAIImageClient : IGeneratedImageClient
{
    private const string AzureOpenAIImageApiVersion = "2025-04-01-preview";

    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = CliDefaults.ImageRequestTimeout,
    };

    private static readonly HttpClient ImageEditClient = new()
    {
        Timeout = CliDefaults.ImageRequestTimeout,
    };

    private readonly AzureCliCredentialProvider _credentialProvider;
    private readonly ConcurrentDictionary<string, AzureOpenAIClient> _azureClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageClient> _imageClients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIImageClient" /> class.
    /// </summary>
    /// <param name="credentialProvider">The Azure CLI credential provider.</param>
    public AzureOpenAIImageClient(AzureCliCredentialProvider credentialProvider)
    {
        _credentialProvider = credentialProvider;
    }

    /// <summary>
    /// Generates images from a prompt using the resolved Azure OpenAI profile.
    /// </summary>
    /// <param name="profile">The resolved target Azure OpenAI profile.</param>
    /// <param name="request">The validated generation request.</param>
    /// <param name="cancellationToken">A token used to cancel the service request.</param>
    /// <returns>The normalized operation result containing image bytes and optional usage.</returns>
    /// <exception cref="CliException">Thrown for service, authentication, or malformed response failures.</exception>
    public async Task<GeneratedImageResult> GenerateAsync(ResolvedProfile profile, GenerateImageRequest request, CancellationToken cancellationToken)
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
            throw AzureOpenAIErrorTranslator.TranslateHttpFailure(ex.Status, ex.Message);
        }
        catch (CredentialUnavailableException)
        {
            throw new CliException(
                "Azure CLI authentication is unavailable. Install Azure CLI, run 'az login', and try again.",
                ExitCodes.Authentication);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new CliException(
                $"Azure CLI failed to get an Azure token. Run 'az login' and try again. {ex.Message}",
                ExitCodes.Authentication);
        }
    }

    /// <summary>
    /// Edits an existing image, optionally with a mask image, using the resolved Azure OpenAI profile.
    /// </summary>
    /// <param name="profile">The resolved target Azure OpenAI profile.</param>
    /// <param name="request">The validated edit request.</param>
    /// <param name="cancellationToken">A token used to cancel the service request.</param>
    /// <returns>The normalized operation result containing image bytes and optional usage.</returns>
    /// <exception cref="CliException">Thrown for service, authentication, local file, or malformed response failures.</exception>
    public async Task<GeneratedImageResult> EditAsync(ResolvedProfile profile, EditImageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            ImageClient client = GetImageClient(profile);
            ImageEditOptions options = CreateEditOptions(request);

            if (request.InputFiles.Count == 0)
            {
                throw new CliException("At least one input image is required.", ExitCodes.Validation);
            }

            using NormalizedEditInputFiles normalizedInputFiles = await EditInputImageNormalizer
                .NormalizeAsync(request.InputFiles, cancellationToken)
                .ConfigureAwait(false);

            if (normalizedInputFiles.Files.Count > 1)
            {
                return await EditMultipleInputImagesAsync(profile, request, normalizedInputFiles.Files, cancellationToken).ConfigureAwait(false);
            }

            string inputFile = normalizedInputFiles.Files[0];
            await using FileStream imageStream = File.OpenRead(inputFile);
            ClientResult<GeneratedImageCollection> response;

            if (!string.IsNullOrWhiteSpace(request.MaskFile))
            {
                await using FileStream maskStream = File.OpenRead(request.MaskFile);
                response = await client.GenerateImageEditsAsync(
                    imageStream,
                    Path.GetFileName(inputFile),
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
                    Path.GetFileName(inputFile),
                    request.Prompt,
                    request.Count,
                    options,
                    cancellationToken).ConfigureAwait(false);
            }

            return await ToResultAsync(response.Value, request.OutputFormat, profile.DeploymentName, cancellationToken).ConfigureAwait(false);
        }
        catch (ClientResultException ex)
        {
            throw AzureOpenAIErrorTranslator.TranslateHttpFailure(ex.Status, ex.Message);
        }
        catch (CredentialUnavailableException)
        {
            throw new CliException(
                "Azure CLI authentication is unavailable. Install Azure CLI, run 'az login', and try again.",
                ExitCodes.Authentication);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new CliException(
                $"Azure CLI failed to get an Azure token. Run 'az login' and try again. {ex.Message}",
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

    private async Task<GeneratedImageResult> EditMultipleInputImagesAsync(
        ResolvedProfile profile,
        EditImageRequest request,
        IReadOnlyList<string> inputFiles,
        CancellationToken cancellationToken)
    {
        using MultipartFormDataContent content = new();
        AddStringContent(content, "model", profile.DeploymentName);
        AddStringContent(content, "prompt", request.Prompt);
        AddStringContent(content, "n", request.Count.ToString(CultureInfo.InvariantCulture));
        AddOptionalStringContent(content, "size", request.Size);
        AddOptionalStringContent(content, "quality", request.Quality);
        AddOptionalStringContent(content, "background", request.Background);
        AddOptionalStringContent(content, "output_format", request.OutputFormat);
        if (request.OutputCompression is int outputCompression)
        {
            AddStringContent(content, "output_compression", outputCompression.ToString(CultureInfo.InvariantCulture));
        }

        AddOptionalStringContent(content, "user", request.EndUserId);

        List<FileStream> streams = [];
        try
        {
            foreach (string inputFile in inputFiles)
            {
                FileStream stream = File.OpenRead(inputFile);
                streams.Add(stream);
                StreamContent imageContent = new(stream);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetImageContentType(inputFile));
                content.Add(imageContent, "image[]", Path.GetFileName(inputFile));
            }

            if (!string.IsNullOrWhiteSpace(request.MaskFile))
            {
                FileStream maskStream = File.OpenRead(request.MaskFile);
                streams.Add(maskStream);
                StreamContent maskContent = new(maskStream);
                maskContent.Headers.ContentType = new MediaTypeHeaderValue(GetImageContentType(request.MaskFile));
                content.Add(maskContent, "mask", Path.GetFileName(request.MaskFile));
            }

            using HttpRequestMessage httpRequest = new(HttpMethod.Post, CreateImageEditUri(profile))
            {
                Content = content,
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await _credentialProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));
            httpRequest.Headers.UserAgent.ParseAdd(CliDefaults.UserAgentApplicationId);

            using HttpResponseMessage response = await ImageEditClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw AzureOpenAIErrorTranslator.TranslateHttpFailure((int)response.StatusCode, responseBody);
            }

            return await ToResultAsync(responseBody, request.OutputFormat, profile.DeploymentName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (FileStream stream in streams)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static Uri CreateImageEditUri(ResolvedProfile profile)
    {
        UriBuilder builder = new(profile.Endpoint);
        string basePath = builder.Path.TrimEnd('/');
        builder.Path = $"{basePath}/openai/deployments/{Uri.EscapeDataString(profile.DeploymentName)}/images/edits";
        builder.Query = $"api-version={Uri.EscapeDataString(AzureOpenAIImageApiVersion)}";
        return builder.Uri;
    }

    private static void AddStringContent(MultipartFormDataContent content, string name, string value)
        => content.Add(new StringContent(value), name);

    private static void AddOptionalStringContent(MultipartFormDataContent content, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            AddStringContent(content, name, value);
        }
    }

    private static string GetImageContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png",
        };

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

    private static async Task<GeneratedImageResult> ToResultAsync(
        GeneratedImageCollection payload,
        string? requestedOutputFormat,
        string deploymentName,
        CancellationToken cancellationToken)
    {
        if (payload.Count == 0)
        {
            throw new CliException("The service returned no images.", ExitCodes.Unhandled);
        }

        List<GeneratedImageContent> images = new(payload.Count);
        for (int index = 0; index < payload.Count; index++)
        {
            GeneratedImage image = payload[index];
            byte[] content = await ResolveImageBytesAsync(image, cancellationToken).ConfigureAwait(false);
            images.Add(new GeneratedImageContent(index + 1, content, ResolveExtension(requestedOutputFormat, image)));
        }

        ImageUsageSnapshot? usage = payload.Usage is null
            ? null
            : new ImageUsageSnapshot(
                payload.Usage.InputTokenCount,
                payload.Usage.OutputTokenCount,
                payload.Usage.TotalTokenCount);

        return new GeneratedImageResult(images, usage, payload.CreatedAt, deploymentName);
    }

    private static async Task<GeneratedImageResult> ToResultAsync(
        string responseBody,
        string? requestedOutputFormat,
        string deploymentName,
        CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            {
                throw new CliException("The service returned no images.", ExitCodes.Unhandled);
            }

            List<GeneratedImageContent> images = new(data.GetArrayLength());
            int index = 1;
            foreach (JsonElement image in data.EnumerateArray())
            {
                string? imageBase64 = image.TryGetProperty("b64_json", out JsonElement imageBytesElement) && imageBytesElement.ValueKind == JsonValueKind.String
                    ? imageBytesElement.GetString()
                    : null;
                Uri? imageUri = image.TryGetProperty("url", out JsonElement imageUriElement)
                    && imageUriElement.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(imageUriElement.GetString(), UriKind.Absolute, out Uri? parsedUri)
                    ? parsedUri
                    : null;
                byte[] content = await ResolveImageBytesAsync(imageBase64, imageUri, cancellationToken).ConfigureAwait(false);
                images.Add(new GeneratedImageContent(index++, content, ResolveExtension(requestedOutputFormat, imageUri)));
            }

            ImageUsageSnapshot? usage = root.TryGetProperty("usage", out JsonElement usageElement) && usageElement.ValueKind == JsonValueKind.Object
                ? new ImageUsageSnapshot(
                    ReadUsageCount(usageElement, "input_tokens", "inputTokenCount"),
                    ReadUsageCount(usageElement, "output_tokens", "outputTokenCount"),
                    ReadUsageCount(usageElement, "total_tokens", "totalTokenCount"))
                : null;
            DateTimeOffset createdAt = root.TryGetProperty("created", out JsonElement createdElement)
                && createdElement.ValueKind == JsonValueKind.Number
                && createdElement.TryGetInt64(out long unixCreated)
                ? DateTimeOffset.FromUnixTimeSeconds(unixCreated)
                : DateTimeOffset.UtcNow;

            return new GeneratedImageResult(images, usage, createdAt, deploymentName);
        }
        catch (JsonException ex)
        {
            throw new CliException($"The service returned malformed image data. {ex.Message}", ExitCodes.Unhandled);
        }
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
                using HttpResponseMessage response = await DownloadClient.GetAsync(image.ImageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new CliException(
                        $"The service returned an image URL, but the CLI could not download it. The download returned HTTP {(int)response.StatusCode}.",
                        ExitCodes.Unhandled);
                }

                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                throw new CliException(
                    $"The service returned an image URL, but the CLI timed out while downloading it. {ex.Message}",
                    ExitCodes.Unhandled);
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

    private static async Task<byte[]> ResolveImageBytesAsync(string? imageBase64, Uri? imageUri, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(imageBase64))
        {
            try
            {
                return Convert.FromBase64String(imageBase64);
            }
            catch (FormatException ex)
            {
                throw new CliException($"The service returned malformed base64 image data. {ex.Message}", ExitCodes.Unhandled);
            }
        }

        if (imageUri is not null)
        {
            try
            {
                using HttpResponseMessage response = await DownloadClient.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new CliException(
                        $"The service returned an image URL, but the CLI could not download it. The download returned HTTP {(int)response.StatusCode}.",
                        ExitCodes.Unhandled);
                }

                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                throw new CliException(
                    $"The service returned an image URL, but the CLI timed out while downloading it. {ex.Message}",
                    ExitCodes.Unhandled);
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

    private static string ResolveExtension(string? requestedOutputFormat, Uri? imageUri)
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

        if (imageUri is not null)
        {
            return Path.GetExtension(imageUri.AbsolutePath).TrimStart('.').ToLowerInvariant() switch
            {
                "jpeg" or "jpg" => "jpg",
                "webp" => "webp",
                "png" => "png",
                _ => "png",
            };
        }

        return "png";
    }

    private static long ReadUsageCount(JsonElement usage, string snakeCaseName, string camelCaseName)
    {
        if (usage.TryGetProperty(snakeCaseName, out JsonElement snakeCaseValue) && snakeCaseValue.TryGetInt64(out long value))
        {
            return value;
        }

        if (usage.TryGetProperty(camelCaseName, out JsonElement camelCaseValue) && camelCaseValue.TryGetInt64(out value))
        {
            return value;
        }

        return 0;
    }
}

#pragma warning restore OPENAI001
