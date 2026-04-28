using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Constants;
using FluentAssertions;
using Xunit;

namespace TripFund.Tests.Services;

public class SyncConflictTests : IDisposable
{
    private readonly string _tempPath;
    private readonly LocalTripStorageService _localStorage;
    private readonly RemoteStorageSyncEngine _syncEngine;

    public SyncConflictTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        _localStorage = new LocalTripStorageService(_tempPath);
        _syncEngine = new RemoteStorageSyncEngine(_localStorage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }

    private async Task CreateFolderWithMeta(string path, string author, string device, string? parents = null)
    {
        var versionsPath = Path.Combine(path, AppConstants.Folders.Versions);
        if (!Directory.Exists(versionsPath)) Directory.CreateDirectory(versionsPath);
        
        // This is a helper for creating a version folder, but 'path' in the tests 
        // sometimes refers to the root of the versioned storage, and sometimes it's been
        // used to construct a specific version folder path in old tests.
        // Let's adjust the logic to handle both or just fix the callers.
        // Actually, looking at the calls: Path.Combine(configPath, "001_NEW_mario")
        // The old tests passed the FULL path of the version folder.
        // I should change it to pass the rootPath and the folderName.
    }

    private async Task CreateVersionInRoot(string rootPath, string folderName, string author, string device, string? parents = null)
    {
        var versionsPath = Path.Combine(rootPath, AppConstants.Folders.Versions);
        if (!Directory.Exists(versionsPath)) Directory.CreateDirectory(versionsPath);
        
        var path = Path.Combine(versionsPath, folderName);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, ".content"));
        var meta = $"author={author}\ndevice={device}\ntimestamp=2023-10-01T12:00:00Z";
        if (parents != null) meta += $"\n{AppConstants.Metadata.VersioningParents}={parents}";
        await File.WriteAllTextAsync(Path.Combine(path, ".tripfund"), meta);
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldCollectAllConflicts()
    {
        // Arrange
        var tripSlug = "conflict-trip";
        var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
        Directory.CreateDirectory(localTripPath);

        // 1. Create a config conflict (two NEW roots)
        var configPath = Path.Combine(localTripPath, "config");
        Directory.CreateDirectory(configPath);
        await CreateVersionInRoot(configPath, "001_NEW_mario", "m", "m");
        await CreateVersionInRoot(configPath, "001_NEW_luigi", "l", "l");

        // 2. Create a transaction conflict
        var transId = "tx-123";
        var transDetailsDir = Path.Combine(localTripPath, "transactions", transId, "details");
        Directory.CreateDirectory(transDetailsDir);
        
        // Base version 001
        await CreateVersionInRoot(transDetailsDir, "001_NEW_mario", "m", "m");

        // Conflict at 002 (both point to 001)
        await CreateVersionInRoot(transDetailsDir, "002_UPD_mario", "m", "m", "001_NEW_mario");
        await CreateVersionInRoot(transDetailsDir, "002_UPD_luigi", "l", "l", "001_NEW_mario");

        // Registry setup
        var registry = new LocalTripRegistry();
        registry.Trips[tripSlug] = new TripRegistryEntry
        {
            RemoteStorage = new RemoteStorageConfig
            {
                Provider = "onedrive",
                Parameters = new Dictionary<string, string> { { "folderId", "remote-folder-id" } }
            }
        };
        await _localStorage.SaveTripRegistryAsync(registry);

        var mockFileSystem = new Mock<IRemoteFileSystem>();
        mockFileSystem.Setup(f => f.ListChildrenAsync("remote-folder-id", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new List<RemoteItem> { 
                new RemoteItem { Id = "devices_id", Name = "devices", IsFolder = true },
                new RemoteItem { Id = "packages_id", Name = "packages", IsFolder = true }
            });
        mockFileSystem.Setup(f => f.ListChildrenAsync("devices_id", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new List<RemoteItem> {
                new RemoteItem { Id = "device_root_id", Name = "unknown-device", IsFolder = true }
            });
        mockFileSystem.Setup(f => f.ListChildrenAsync("packages_id", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new List<RemoteItem>());
        
        mockFileSystem.Setup(f => f.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new RemoteItem { Id = "check_id" });

        // Act
        Func<Task> act = () => _syncEngine.SynchronizeAsync(tripSlug, mockFileSystem.Object);

        // Assert
        var exception = await act.Should().ThrowAsync<SyncConflictException>();
        var conflicts = exception.Which.Conflicts;

        conflicts.Should().HaveCount(2);
        
        var configConflict = conflicts.OfType<TripConfigConflictException>().Single();
        configConflict.DivergingVersions.Should().Contain(new[] { "001_NEW_mario", "001_NEW_luigi" });
        configConflict.BaseVersion.Should().BeNull();

        var txConflict = conflicts.OfType<TransactionConflictException>().Single();
        txConflict.TransactionId.Should().Be(transId);
        txConflict.DivergingVersions.Should().Contain(new[] { "002_UPD_mario", "002_UPD_luigi" });
        txConflict.BaseVersion.Should().Be("001_NEW_mario");
    }

    [Fact]
    public async Task GetLatestVersionFolders_ShouldHandleDivergingBranchesOfDifferentLengths()
    {
        // Scenario from user: 001_m, 002_m, 003_m, 004_m, 005_m AND 003_c, 004_c
        var engine = new VersionedStorageEngine();
        var root = Path.Combine(_tempPath, "divergence-test");
        Directory.CreateDirectory(root);
        
        await CreateVersionInRoot(root, "001_NEW_mario", "m", "m");
        await CreateVersionInRoot(root, "002_UPD_mario", "m", "m", "001_NEW_mario");
        
        await CreateVersionInRoot(root, "003_UPD_mario", "m", "m", "002_UPD_mario");
        await CreateVersionInRoot(root, "004_UPD_mario", "m", "m", "003_UPD_mario");
        await CreateVersionInRoot(root, "005_UPD_mario", "m", "m", "004_UPD_mario");
        
        // Carlo diverged from 002
        await CreateVersionInRoot(root, "003_UPD_carlo", "c", "c", "002_UPD_mario");
        await CreateVersionInRoot(root, "004_UPD_carlo", "c", "c", "003_UPD_carlo");

        // Act
        var versions = engine.GetVersionFolders(root);
        var latest = engine.GetLatestVersionFolders(versions);
        var baseVer = engine.GetBaseVersionFolder(root, latest);

        // Assert
        latest.Should().HaveCount(2);
        latest.Select(v => v.FolderName).Should().Contain("005_UPD_mario");
        latest.Select(v => v.FolderName).Should().Contain("004_UPD_carlo");
        baseVer.Should().Be("002_UPD_mario");
    }

    [Fact]
    public async Task GetLatestVersionFolders_ShouldInvalidateResIfNewerCommitExistsOnOtherThread()
    {
        // Scenario: 001_m, 002_m, 002_c, 003_RES_m (resolves 002_m, 002_c), 003_c (newer carlo diverged from 002_c)
        var engine = new VersionedStorageEngine();
        var root = Path.Combine(_tempPath, "res-invalidation-test");
        Directory.CreateDirectory(root);

        await CreateVersionInRoot(root, "001_NEW_mario", "m", "m");
        await CreateVersionInRoot(root, "002_UPD_mario", "m", "m", "001_NEW_mario");
        await CreateVersionInRoot(root, "002_UPD_carlo", "c", "c", "001_NEW_mario");

        // RES resolves both
        await CreateVersionInRoot(root, "003_RES_luigi", "l", "l", "002_UPD_mario,002_UPD_carlo");

        // Carlo creates a new commit at sequence 3, but pointing only to 002_carlo (diverging from the resolution)
        await CreateVersionInRoot(root, "003_UPD_carlo", "c", "c", "002_UPD_carlo");

        // Act
        var versions = engine.GetVersionFolders(root);
        var latest = engine.GetLatestVersionFolders(versions);
        var baseVer = engine.GetBaseVersionFolder(root, latest);

        // Assert
        latest.Should().HaveCount(2);
        latest.Select(v => v.FolderName).Should().Contain("003_RES_luigi");
        latest.Select(v => v.FolderName).Should().Contain("003_UPD_carlo");
        
        // Base should be 001_mario because it's the LCA of the two leaves
        baseVer.Should().Be("001_NEW_mario");
    }
}
