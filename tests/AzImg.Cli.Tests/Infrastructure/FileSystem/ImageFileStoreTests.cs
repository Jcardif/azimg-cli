using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Configuration;
using AzImg.Cli.Infrastructure.FileSystem;

namespace AzImg.Cli.Tests.Infrastructure.FileSystem;

public class ImageFileStoreTests
{
    [Fact]
    public async Task SaveAsync_UsesShortDefaultNamesForLongPrompts()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        ResolvedProfile profile = new(
            "azure-default",
            "gpt-image-2",
            new Uri("https://example.openai.azure.com/"),
            outputDirectory);
        GeneratedImageResult result = new(
            [new GeneratedImageContent(1, [1, 2, 3], "png")],
            null,
            new DateTimeOffset(2026, 4, 28, 19, 13, 32, TimeSpan.Zero),
            "gpt-image-2");

        try
        {
            SaveImagesResult saveResult = await new ImageFileStore().SaveAsync(
                profile,
                new string('x', 4000),
                string.Empty,
                writeManifest: true,
                result,
                CancellationToken.None);

            string imageFileName = Path.GetFileName(saveResult.Files[0].Path);
            Assert.Matches("^[0-9a-f]{8}-01\\.png$", imageFileName);
            Assert.NotNull(saveResult.ManifestPath);
            Assert.Matches("^[0-9a-f]{8}-00\\.manifest\\.json$", Path.GetFileName(saveResult.ManifestPath!));
            Assert.True(File.Exists(saveResult.Files[0].Path));
            Assert.True(File.Exists(saveResult.ManifestPath!));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}