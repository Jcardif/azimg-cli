using AzImg.Cli.Application.GeneratedImages;
using AzImg.Cli.Runtime;

namespace AzImg.Cli.Tests.Application.GeneratedImages;

public class EditInputFileResolverTests
{
    [Fact]
    public async Task Resolve_ExpandsPrimaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string firstFile = Path.Combine(directory, "character-a.png");
        string secondFile = Path.Combine(directory, "character-b.webp");

        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(secondFile, [2]);
        await File.WriteAllBytesAsync(firstFile, [1]);

        try
        {
            IReadOnlyList<string> files = EditInputFileResolver.Resolve(directory, []);

            Assert.Equal(new[] { firstFile, secondFile }, files);
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
    public async Task Resolve_ExpandsDirectoriesInDeterministicOrder()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string primaryFile = Path.Combine(directory, "primary.png");
        string referenceDirectory = Path.Combine(directory, "references");
        string firstReference = Path.Combine(referenceDirectory, "a-character.webp");
        string secondReference = Path.Combine(referenceDirectory, "b-character.jpg");

        Directory.CreateDirectory(referenceDirectory);
        await File.WriteAllBytesAsync(primaryFile, [1]);
        await File.WriteAllBytesAsync(secondReference, [2]);
        await File.WriteAllBytesAsync(firstReference, [3]);
        await File.WriteAllTextAsync(Path.Combine(referenceDirectory, "notes.txt"), "not an image");

        try
        {
            IReadOnlyList<string> files = EditInputFileResolver.Resolve(primaryFile, [referenceDirectory]);

            Assert.Equal(new[] { primaryFile, firstReference, secondReference }, files);
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
    public async Task Resolve_RejectsExplicitUnsupportedFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string textFile = Path.Combine(directory, "reference.txt");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(textFile, "not an image");

        try
        {
            CliException exception = Assert.Throws<CliException>(() => EditInputFileResolver.Resolve(textFile, []));

            Assert.Equal(ExitCodes.Validation, exception.ExitCode);
            Assert.Contains("must use one of these file extensions", exception.Message, StringComparison.Ordinal);
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
    public void Resolve_RejectsEmptyImageDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        try
        {
            CliException exception = Assert.Throws<CliException>(() => EditInputFileResolver.Resolve(directory, []));

            Assert.Equal(ExitCodes.Validation, exception.ExitCode);
            Assert.Contains("did not contain any supported image files", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
