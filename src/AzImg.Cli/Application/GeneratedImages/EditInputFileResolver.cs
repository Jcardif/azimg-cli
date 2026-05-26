using AzImg.Cli.Runtime;

namespace AzImg.Cli.Application.GeneratedImages;

/// <summary>
/// Resolves image file and directory inputs for image edit requests.
/// </summary>
internal static class EditInputFileResolver
{
    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static IReadOnlyList<string> Resolve(string primaryPath, IEnumerable<string> additionalPaths)
    {
        List<string> inputFiles = [];
        AddPath(primaryPath, inputFiles);

        foreach (string path in additionalPaths)
        {
            AddPath(path, inputFiles);
        }

        return inputFiles;
    }

    private static void AddPath(string path, List<string> inputFiles)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliException("Image input paths must not be empty.", ExitCodes.Validation);
        }

        string fullPath = CliPath.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            ValidateSupportedImageFile(fullPath);
            inputFiles.Add(fullPath);
            return;
        }

        if (Directory.Exists(fullPath))
        {
            string[] files = Directory
                .EnumerateFiles(fullPath)
                .Where(IsSupportedImageFile)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static value => value, StringComparer.Ordinal)
                .ToArray();

            if (files.Length == 0)
            {
                throw new CliException(
                    $"Image directory '{fullPath}' did not contain any supported image files ({FormatSupportedExtensions()}).",
                    ExitCodes.Validation);
            }

            inputFiles.AddRange(files);
            return;
        }

        throw new CliException($"Input image or directory '{fullPath}' was not found.", ExitCodes.Validation);
    }

    private static void ValidateSupportedImageFile(string path)
    {
        if (!IsSupportedImageFile(path))
        {
            throw new CliException(
                $"Input image '{path}' must use one of these file extensions: {FormatSupportedExtensions()}.",
                ExitCodes.Validation);
        }
    }

    private static bool IsSupportedImageFile(string path)
        => SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string FormatSupportedExtensions()
        => string.Join(", ", SupportedImageExtensions);
}
