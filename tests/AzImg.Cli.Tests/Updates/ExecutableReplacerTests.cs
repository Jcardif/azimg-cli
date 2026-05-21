using AzImg.Cli.Updates;

namespace AzImg.Cli.Tests.Updates;

public class ExecutableReplacerTests
{
    [Fact]
    public void Replace_SchedulesCurrentProcessExecutableWithoutOverwritingRunningBundle()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string source = Path.Combine(directory, "new-azimg");
        string target = Path.Combine(directory, "azimg");
        FakeReplacementScheduler scheduler = new();
        ExecutableReplacer replacer = new(target, 12345, scheduler);

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");

            ExecutableReplacementResult result = replacer.Replace(source, target);

            Assert.True(result.Scheduled);
            Assert.Equal("old", File.ReadAllText(target));
            Assert.Equal(target, scheduler.TargetExecutable);
            Assert.Equal(12345, scheduler.CurrentProcessId);
            Assert.NotNull(scheduler.StagedPath);
            Assert.Equal("new", File.ReadAllText(scheduler.StagedPath));
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
    public void IsCurrentProcessExecutable_MatchesFullPathUsingPlatformComparison()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string target = Path.Combine(directory, "azimg");

        Assert.True(ExecutableReplacer.IsCurrentProcessExecutable(target, target));
        Assert.False(ExecutableReplacer.IsCurrentProcessExecutable(target, Path.Combine(directory, "other")));
        Assert.False(ExecutableReplacer.IsCurrentProcessExecutable(target, null));
    }

    [Fact]
    public void ResolveExecutablePath_FollowsSymlinksWhenAvailable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string realPath = Path.Combine(directory, "real", "azimg");
        string linkPath = Path.Combine(directory, "bin", "azimg");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(realPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            File.WriteAllText(realPath, "binary");
            File.CreateSymbolicLink(linkPath, realPath);

            Assert.Equal(realPath, ExecutableReplacer.ResolveExecutablePath(linkPath));
            Assert.True(ExecutableReplacer.IsCurrentProcessExecutable(linkPath, realPath));
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
    public void CreateWindowsReplacementArguments_UsesCmdStringWithoutBackslashEscapedQuotes()
    {
        string arguments = ExecutableReplacementScheduler.CreateWindowsReplacementArguments(@"C:\Temp\azimg-update.cmd");

        Assert.Equal("/d /s /c \"\"C:\\Temp\\azimg-update.cmd\"\"", arguments);
        Assert.DoesNotContain("\\\"", arguments, StringComparison.Ordinal);
    }

    private sealed class FakeReplacementScheduler : IExecutableReplacementScheduler
    {
        public string? StagedPath { get; private set; }

        public string? TargetExecutable { get; private set; }

        public int CurrentProcessId { get; private set; }

        public void ScheduleReplacement(string stagedPath, string targetExecutable, int currentProcessId)
        {
            StagedPath = stagedPath;
            TargetExecutable = targetExecutable;
            CurrentProcessId = currentProcessId;
        }
    }
}
