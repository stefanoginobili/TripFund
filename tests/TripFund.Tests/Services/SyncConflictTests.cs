using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
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

    [Fact]
    public async Task SynchronizeAsync_ShouldCollectAllConflicts()
    {
        // Arrange
        var tripSlug = "conflict-trip";
        var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
        Directory.CreateDirectory(localTripPath);

        // 1. Create a config conflict
        var configPath = Path.Combine(localTripPath, "config_versioned");
        Directory.CreateDirectory(configPath);
        var v1m = Path.Combine(configPath, "001_NEW_mario");
        var v1l = Path.Combine(configPath, "001_NEW_luigi");
        Directory.CreateDirectory(v1m);
        Directory.CreateDirectory(v1l);
        Directory.CreateDirectory(Path.Combine(v1m, ".data"));
        Directory.CreateDirectory(Path.Combine(v1l, ".data"));
        await File.WriteAllTextAsync(Path.Combine(v1m, ".metadata"), "author=m\ndevice=m\ntimestamp=2023-10-01T12:00:00Z");
        await File.WriteAllTextAsync(Path.Combine(v1l, ".metadata"), "author=l\ndevice=l\ntimestamp=2023-10-01T12:00:00Z");

        // 2. Create a transaction conflict
        var transId = "tx-123";
        var transDetailsDir = Path.Combine(localTripPath, "transactions", transId, "details_versioned");
        Directory.CreateDirectory(transDetailsDir);
        
        // Base version 001
        var v1 = Path.Combine(transDetailsDir, "001_NEW_mario");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(Path.Combine(v1, ".data"));
        await File.WriteAllTextAsync(Path.Combine(v1, ".metadata"), "author=m\ndevice=m\ntimestamp=2023-10-01T12:00:00Z");

        // Conflict at 002
        var v2m = Path.Combine(transDetailsDir, "002_UPD_mario");
        var v2l = Path.Combine(transDetailsDir, "002_UPD_luigi");
        Directory.CreateDirectory(v2m);
        Directory.CreateDirectory(v2l);
        Directory.CreateDirectory(Path.Combine(v2m, ".data"));
        Directory.CreateDirectory(Path.Combine(v2l, ".data"));
        await File.WriteAllTextAsync(Path.Combine(v2m, ".metadata"), "author=m\ndevice=m\ntimestamp=2023-10-02T12:00:00Z");
        await File.WriteAllTextAsync(Path.Combine(v2l, ".metadata"), "author=l\ndevice=l\ntimestamp=2023-10-02T12:00:00Z");

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
        mockFileSystem.Setup(f => f.ListChildrenAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new List<RemoteItem>());

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
    public async Task VersionedStorageEngine_GetBaseVersionFolder_ShouldReturnPreviousSequence()
    {
        // Arrange
        var engine = new VersionedStorageEngine();
        var root = Path.Combine(_tempPath, "version-test");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(root, "002_UPD_mario"));
        Directory.CreateDirectory(Path.Combine(root, "004_RES_mario")); // Skip 003

        // Act & Assert
        engine.GetBaseVersionFolder(root, 4).Should().Be("002_UPD_mario");
        engine.GetBaseVersionFolder(root, 2).Should().Be("001_NEW_mario");
        engine.GetBaseVersionFolder(root, 1).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestVersionFolders_ShouldHandleDivergingBranchesOfDifferentLengths()
    {
        // Scenario from user: 001_m, 002_m, 003_m, 004_m, 005_m AND 003_c, 004_c
        var engine = new VersionedStorageEngine();
        var root = Path.Combine(_tempPath, "divergence-test");
        Directory.CreateDirectory(root);
        
        Directory.CreateDirectory(Path.Combine(root, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(root, "002_UPD_mario"));
        
        Directory.CreateDirectory(Path.Combine(root, "003_UPD_mario"));
        Directory.CreateDirectory(Path.Combine(root, "004_UPD_mario"));
        Directory.CreateDirectory(Path.Combine(root, "005_UPD_mario"));
        
        Directory.CreateDirectory(Path.Combine(root, "003_UPD_carlo"));
        Directory.CreateDirectory(Path.Combine(root, "004_UPD_carlo"));

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
        // Scenario: 001_m, 002_m, 002_c, 003_RES_m (resolves 002_m, 002_c), 003_c (newer carlo)
        var engine = new VersionedStorageEngine();
        var root = Path.Combine(_tempPath, "res-invalidation-test");
        Directory.CreateDirectory(root);

        Directory.CreateDirectory(Path.Combine(root, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(root, "002_UPD_mario"));
        Directory.CreateDirectory(Path.Combine(root, "002_UPD_carlo"));

        var resDir = Path.Combine(root, "003_RES_luigi");
        Directory.CreateDirectory(resDir);
        
        // Metadata containing resolved_versions
        await File.WriteAllTextAsync(Path.Combine(resDir, ".metadata"), "resolved_versions=002_UPD_mario,002_UPD_carlo");

        // Carlo creates a new commit at sequence 3, sitting alongside the RES
        Directory.CreateDirectory(Path.Combine(root, "003_UPD_carlo"));

        // Act
        var versions = engine.GetVersionFolders(root);
        var latest = engine.GetLatestVersionFolders(versions);
        var baseVer = engine.GetBaseVersionFolder(root, latest);

        // Assert
        latest.Should().HaveCount(2);
        latest.Select(v => v.FolderName).Should().Contain("003_RES_luigi");
        latest.Select(v => v.FolderName).Should().Contain("003_UPD_carlo");
        
        // Base should be 001_mario because sequence 2 was conflicted
        baseVer.Should().Be("001_NEW_mario");
    }
}