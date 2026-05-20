using AzImg.Cli.Updates;

namespace AzImg.Cli.Tests.Updates;

public class LocalMetadataStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsInstallMetadataAndFirstLaunchState()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "metadata.json");
        LocalMetadataStore store = new(path);

        try
        {
            await store.SaveInstallMetadataAsync(new InstallMetadataDocument
            {
                InstallPath = "/tmp/azimg",
                Rid = "linux-x64",
                InstalledVersion = "0.1.0",
                SourceRepository = "Jcardif/azimg-cli",
                InstallMethod = "test",
                UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            }, CancellationToken.None);

            await store.SaveUpdateStateAsync(new UpdateStateDocument
            {
                LastCheckedAppVersion = "0.1.0",
                LatestVersion = "0.2.0",
                UpdateAvailable = true,
                LastCheckAtUtc = DateTimeOffset.UnixEpoch,
            }, CancellationToken.None);

            LocalMetadataDocument loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal("/tmp/azimg", loaded.Install.InstallPath);
            Assert.Equal("linux-x64", loaded.Install.Rid);
            Assert.Equal("0.1.0", loaded.Update.LastCheckedAppVersion);
            Assert.Equal("0.2.0", loaded.Update.LatestVersion);
            Assert.True(loaded.Update.UpdateAvailable);
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