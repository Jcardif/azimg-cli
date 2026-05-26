using AzImg.Cli.Application.GeneratedImages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AzImg.Cli.Tests.Application.GeneratedImages;

public class EditInputImageNormalizerTests
{
    [Fact]
    public async Task NormalizeAsync_LeavesPngInputsUnchanged()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string pngPath = Path.Combine(directory, "input.png");

        Directory.CreateDirectory(directory);
        await CreateImageAsync(pngPath, saveAsJpeg: false);

        try
        {
            using NormalizedEditInputFiles normalized = await EditInputImageNormalizer.NormalizeAsync([pngPath], CancellationToken.None);

            Assert.Equal(new[] { pngPath }, normalized.Files);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NormalizeAsync_ConvertsJpegInputsToTemporaryPngsAndCleansUp()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string jpegPath = Path.Combine(directory, "input.jpg");

        Directory.CreateDirectory(directory);
        await CreateImageAsync(jpegPath, saveAsJpeg: true);

        try
        {
            NormalizedEditInputFiles normalized = await EditInputImageNormalizer.NormalizeAsync([jpegPath], CancellationToken.None);
            string normalizedPath = normalized.Files.Single();
            string normalizedDirectory = Path.GetDirectoryName(normalizedPath)!;

            Assert.NotEqual(jpegPath, normalizedPath);
            Assert.Equal(".png", Path.GetExtension(normalizedPath));
            Assert.True(File.Exists(normalizedPath));

            normalized.Dispose();

            Assert.False(Directory.Exists(normalizedDirectory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task CreateImageAsync(string path, bool saveAsJpeg)
    {
        using Image<Rgba32> image = new(4, 4, Color.Red);
        if (saveAsJpeg)
        {
            await image.SaveAsJpegAsync(path);
            return;
        }

        await image.SaveAsPngAsync(path);
    }
}
