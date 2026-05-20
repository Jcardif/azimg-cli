namespace AzImg.Cli.Application.GeneratedImages;

/// <summary>
/// User request for creating new images from a text prompt.
/// </summary>
/// <param name="Prompt">The prompt to send to Azure OpenAI.</param>
/// <param name="Count">The number of images to generate.</param>
/// <param name="Size">An optional image size in <c>WIDTHxHEIGHT</c> format.</param>
/// <param name="Quality">An optional quality value accepted by the image API.</param>
/// <param name="Background">An optional background mode accepted by the image API.</param>
/// <param name="OutputFormat">An optional image file format.</param>
/// <param name="OutputCompression">An optional compression factor from 0 through 100.</param>
/// <param name="EndUserId">An optional end-user identifier passed to Azure OpenAI.</param>
/// <param name="NameTemplate">The file-name template used when saving results.</param>
/// <param name="WriteManifest">Whether a manifest JSON sidecar should be written.</param>
public sealed record GenerateImageRequest(
    string Prompt,
    int Count,
    string? Size,
    string? Quality,
    string? Background,
    string? OutputFormat,
    int? OutputCompression,
    string? EndUserId,
    string NameTemplate,
    bool WriteManifest);

/// <summary>
/// User request for editing an existing image, optionally constrained by a mask image.
/// </summary>
/// <param name="InputFile">The local image file to edit.</param>
/// <param name="MaskFile">An optional local PNG mask file.</param>
/// <param name="Prompt">The edit prompt to send to Azure OpenAI.</param>
/// <param name="Count">The number of edited images to request.</param>
/// <param name="Size">An optional image size in <c>WIDTHxHEIGHT</c> format.</param>
/// <param name="Quality">An optional quality value accepted by the image API.</param>
/// <param name="Background">An optional background mode accepted by the image API.</param>
/// <param name="OutputFormat">An optional image file format.</param>
/// <param name="OutputCompression">An optional compression factor from 0 through 100.</param>
/// <param name="EndUserId">An optional end-user identifier passed to Azure OpenAI.</param>
/// <param name="NameTemplate">The file-name template used when saving results.</param>
/// <param name="WriteManifest">Whether a manifest JSON sidecar should be written.</param>
public sealed record EditImageRequest(
    string InputFile,
    string? MaskFile,
    string Prompt,
    int Count,
    string? Size,
    string? Quality,
    string? Background,
    string? OutputFormat,
    int? OutputCompression,
    string? EndUserId,
    string NameTemplate,
    bool WriteManifest);

/// <summary>
/// Image bytes returned by Azure OpenAI before they are written to disk.
/// </summary>
/// <param name="Index">The one-based image index within the operation result.</param>
/// <param name="Content">The encoded image bytes.</param>
/// <param name="Extension">The file extension without a leading dot.</param>
public sealed record GeneratedImageContent(int Index, byte[] Content, string Extension);

/// <summary>
/// Token usage returned by Azure OpenAI for an image operation, when available.
/// </summary>
/// <param name="InputTokens">Input tokens billed by the service.</param>
/// <param name="OutputTokens">Output tokens billed by the service.</param>
/// <param name="TotalTokens">Total tokens billed by the service.</param>
public sealed record ImageUsageSnapshot(
    long InputTokens,
    long OutputTokens,
    long TotalTokens);

/// <summary>
/// Normalized result of an Azure image generation or edit request.
/// </summary>
/// <param name="Images">The generated or edited image payloads.</param>
/// <param name="Usage">Optional service token usage.</param>
/// <param name="CreatedAt">The timestamp reported by Azure OpenAI.</param>
/// <param name="DeploymentName">The deployment used for the operation.</param>
public sealed record GeneratedImageResult(
    IReadOnlyList<GeneratedImageContent> Images,
    ImageUsageSnapshot? Usage,
    DateTimeOffset CreatedAt,
    string DeploymentName);