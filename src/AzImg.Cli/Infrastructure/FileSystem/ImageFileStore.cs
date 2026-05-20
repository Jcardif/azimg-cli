using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Configuration;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Infrastructure.FileSystem;

/// <summary>
/// Saves generated image bytes and optional manifest files to the resolved output directory.
/// </summary>
/// <remarks>
/// The writer uses atomic same-directory writes to avoid leaving truncated files behind when a process is
/// interrupted. File names are rendered from a template, slugified for portability, and capped at a safe
/// leaf-name length before writing.
/// </remarks>
public sealed class ImageFileStore
{
    private const int MaxOutputLeafLength = 200;

    /// <summary>
    /// Writes all returned images and, when requested, a manifest file describing the operation.
    /// </summary>
    /// <param name="profile">The resolved profile containing the output directory.</param>
    /// <param name="prompt">The prompt used for file-name slugging and manifests.</param>
    /// <param name="nameTemplate">The user-supplied or default file-name template.</param>
    /// <param name="writeManifest">Whether to write a manifest sidecar.</param>
    /// <param name="result">The image operation result to persist.</param>
    /// <param name="cancellationToken">A token used to cancel file I/O.</param>
    /// <returns>Metadata for saved images and the optional manifest.</returns>
    /// <exception cref="CliException">Thrown when output paths cannot be safely created or written.</exception>
    public async Task<SaveImagesResult> SaveAsync(
        ResolvedProfile profile,
        string prompt,
        string nameTemplate,
        bool writeManifest,
        GeneratedImageResult result,
        CancellationToken cancellationToken)
    {
        string fullOutputDirectory = Path.GetFullPath(profile.OutputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        List<SavedImageFile> files = [];
        string timestamp = result.CreatedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string operationId = Guid.NewGuid().ToString("N")[..8];
        string slug = Slugify(prompt);

        foreach (GeneratedImageContent image in result.Images)
        {
            string fileName = RenderTemplate(nameTemplate, timestamp, operationId, slug, image.Index, profile);
            string path = BuildOutputPath(fullOutputDirectory, fileName, $".{image.Extension}");
            await WriteFileAtomicallyAsync(path, image.Content, cancellationToken);
            files.Add(new SavedImageFile(image.Index, path, ComputeSha256(image.Content), image.Content.LongLength));
        }

        string? manifestPath = null;
        if (writeManifest)
        {
            manifestPath = BuildOutputPath(fullOutputDirectory, RenderTemplate(nameTemplate, timestamp, operationId, slug, 0, profile), ".manifest.json");
            ImageManifestDocument manifest = new(
                prompt,
                "azure-openai",
                result.DeploymentName,
                result.CreatedAt,
                result.Usage,
                files.ToArray());

            byte[] content = JsonDefaults.SerializeToUtf8Bytes(manifest, CliJsonContext.Default.ImageManifestDocument);
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

    private static string ComputeSha256(byte[] content)
    {
        byte[] hash = SHA256.HashData(content);
        StringBuilder builder = new(hash.Length * 2);
        foreach (byte value in hash)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}