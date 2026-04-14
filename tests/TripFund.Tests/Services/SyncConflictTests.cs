using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using Xunit;

namespace TripFund.Tests.Services;

public class SyncConflictTests
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

    [Fact]
    public async Task SynchronizeAsync_ShouldCollectAllConflicts()
    {
        // Arrange
        var tripSlug = "conflict-trip";
        var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
        Directory.CreateDirectory(localTripPath);

        // 1. Create a metadata conflict
        var metadataDir = Path.Combine(localTripPath, "metadata");
        Directory.CreateDirectory(metadataDir);
        Directory.CreateDirectory(Path.Combine(metadataDir, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(metadataDir, "001_NEW_luigi"));

        // 2. Create a transaction conflict
        var transId = "tx-123";
        var transMetadataDir = Path.Combine(localTripPath, "transactions", transId, "metadata");
        Directory.CreateDirectory(transMetadataDir);
        // Base version 001
        Directory.CreateDirectory(Path.Combine(transMetadataDir, "001_NEW_mario"));
        // Conflict at 002
        Directory.CreateDirectory(Path.Combine(transMetadataDir, "002_UPD_mario"));
        Directory.CreateDirectory(Path.Combine(transMetadataDir, "002_UPD_luigi"));

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
        
        var metaConflict = conflicts.OfType<TripMetadataConflictException>().Single();
        metaConflict.DivergingVersions.Should().Contain(new[] { "001_NEW_mario", "001_NEW_luigi" });
        metaConflict.BaseVersion.Should().BeNull();

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
        await File.WriteAllLinesAsync(Path.Combine(resDir, ".resolves"), new[] { "002_UPD_mario", "002_UPD_carlo" });

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