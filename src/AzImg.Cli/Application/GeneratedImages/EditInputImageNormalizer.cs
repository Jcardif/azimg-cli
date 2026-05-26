using AzImg.Cli.Runtime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AzImg.Cli.Application.GeneratedImages;

/// <summary>
/// Normalizes edit input images into upload-safe files for Azure OpenAI image edits.
/// </summary>
internal sealed class EditInputImageNormalizer
{
    /// <summary>Normalizes input images and returns paths to upload.</summary>
    public static async Task<NormalizedEditInputFiles> NormalizeAsync(
        IReadOnlyList<string> inputFiles,
        CancellationToken cancellationToken)
    {
        List<string> uploadFiles = new(inputFiles.Count);
        string? temporaryDirectory = null;

        try
        {
            for (int index = 0; index < inputFiles.Count; index++)
            {
                string inputFile = inputFiles[index];
                if (IsPng(inputFile))
                {
                    uploadFiles.Add(inputFile);
                    continue;
                }

                temporaryDirectory ??= CreateTemporaryDirectory();
                string normalizedPath = Path.Combine(temporaryDirectory, $"{index + 1:D2}.png");
                await NormalizeToPngAsync(inputFile, normalizedPath, cancellationToken).ConfigureAwait(false);
                uploadFiles.Add(normalizedPath);
            }
        }
        catch
        {
            DeleteTemporaryDirectory(temporaryDirectory);
            throw;
        }

        return new NormalizedEditInputFiles(uploadFiles, temporaryDirectory);
    }

    private static bool IsPng(string path)
        => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"azimg-edit-inputs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task NormalizeToPngAsync(string inputFile, string outputFile, CancellationToken cancellationToken)
    {
        try
        {
            using Image image = await Image.LoadAsync(inputFile, cancellationToken).ConfigureAwait(false);
            image.Mutate(static operations => operations.AutoOrient());
            await image.SaveAsPngAsync(outputFile, cancellationToken).ConfigureAwait(false);
        }
        catch (ImageFormatException ex)
        {
            throw new CliException(
                $"Input image '{inputFile}' could not be decoded for normalization. {ex.Message}",
                ExitCodes.Validation);
        }
    }

    private static void DeleteTemporaryDirectory(string? temporaryDirectory)
    {
        if (string.IsNullOrWhiteSpace(temporaryDirectory) || !Directory.Exists(temporaryDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Resolved edit input files plus any temporary normalized copies that must be cleaned up.
/// </summary>
internal sealed class NormalizedEditInputFiles : IDisposable
{
    private readonly string? _temporaryDirectory;

    public NormalizedEditInputFiles(IReadOnlyList<string> files, string? temporaryDirectory)
    {
        Files = files;
        _temporaryDirectory = temporaryDirectory;
    }

    /// <summary>Gets the local files ready to upload.</summary>
    public IReadOnlyList<string> Files { get; }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_temporaryDirectory) || !Directory.Exists(_temporaryDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
