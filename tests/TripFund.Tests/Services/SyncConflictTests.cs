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

        [Fact]
        public async Task GetConflictsAsync_ShouldReturnLabeledConflicts()
        {
        // Arrange
        var tripSlug = "labeled-conflict-trip";
        var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
        Directory.CreateDirectory(localTripPath);

        var myDeviceId = "my-device";
        await _localStorage.SaveAppSettingsAsync(new AppSettings { DeviceId = myDeviceId, AuthorName = "Me" });

        // 1. Config Conflict
        var configPath = Path.Combine(localTripPath, "config_versioned");
        Directory.CreateDirectory(configPath);
        Directory.CreateDirectory(Path.Combine(configPath, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(configPath, "001_NEW_luigi"));

        // Local version of config for member names
        var myConfigDir = Path.Combine(configPath, "001_NEW_my-device");
        Directory.CreateDirectory(myConfigDir);
        var config = new TripConfig { Name = "Test Trip" };
        config.Members["mario-slug"] = new User { Name = "Mario Rossi" };
        var configJson = System.Text.Json.JsonSerializer.Serialize(config);
        Directory.CreateDirectory(Path.Combine(myConfigDir, ".data"));
        await File.WriteAllTextAsync(Path.Combine(myConfigDir, ".data", "trip_config.json"), configJson);

        // 2. Expense Conflict
        var expId = "exp-1";
        var expPath = Path.Combine(localTripPath, "transactions", expId, "details_versioned");
        Directory.CreateDirectory(expPath);
        Directory.CreateDirectory(Path.Combine(expPath, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(expPath, "001_NEW_luigi"));

        var myExpDir = Path.Combine(expPath, "001_NEW_my-device");
        Directory.CreateDirectory(myExpDir);
        var exp = new Transaction { Id = expId, Type = "expense", Description = "Pizza" };
        var expJson = System.Text.Json.JsonSerializer.Serialize(exp);
        Directory.CreateDirectory(Path.Combine(myExpDir, ".data"));
        await File.WriteAllTextAsync(Path.Combine(myExpDir, ".data", "transaction_details.json"), expJson);

        // 3. Contribution Conflict
        var conId = "con-1";
        var conPath = Path.Combine(localTripPath, "transactions", conId, "details_versioned");
        Directory.CreateDirectory(conPath);
        Directory.CreateDirectory(Path.Combine(conPath, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(conPath, "001_NEW_luigi"));

        var myConDir = Path.Combine(conPath, "001_NEW_my-device");
        Directory.CreateDirectory(myConDir);
        var con = new Transaction { Id = conId, Type = "contribution" };
        con.Split["mario-slug"] = new SplitInfo { Amount = 50 };
        var conJson = System.Text.Json.JsonSerializer.Serialize(con);
        Directory.CreateDirectory(Path.Combine(myConDir, ".data"));
        await File.WriteAllTextAsync(Path.Combine(myConDir, ".data", "transaction_details.json"), conJson);

        // Act
        var conflicts = await _localStorage.GetConflictsAsync(tripSlug);

        // Assert
        conflicts.Should().HaveCount(3);

        var cConfig = conflicts.Single(c => c.Type == "config");
        cConfig.Label.Should().Be("Configurazione Viaggio");

        var cExp = conflicts.Single(c => c.Type == "expense");
        cExp.Label.Should().Be("Spesa \"Pizza\"");

        var cCon = conflicts.Single(c => c.Type == "contribution");
        cCon.Label.Should().Be("Versamento di Mario Rossi");
        }

        [Fact]
        public async Task GetConflictsAsync_ShouldUseLCA_WhenLocalIsDelete()
        {
        // Arrange
        var tripSlug = "lca-delete-trip";
        var localTripPath = Path.Combine(_localStorage.TripsPath, tripSlug);
        Directory.CreateDirectory(localTripPath);

        var myDeviceId = "my-device";
        await _localStorage.SaveAppSettingsAsync(new AppSettings { DeviceId = myDeviceId, AuthorName = "Me" });

        var expId = "exp-lca";
        var expPath = Path.Combine(localTripPath, "transactions", expId, "details_versioned");
        Directory.CreateDirectory(expPath);

        // 001: LCA (the original expense)
        var v1 = Path.Combine(expPath, "001_NEW_mario");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(Path.Combine(v1, ".data"));
        var exp1 = new Transaction { Id = expId, Type = "expense", Description = "Original Pizza" };
        await File.WriteAllTextAsync(Path.Combine(v1, ".data", "transaction_details.json"), System.Text.Json.JsonSerializer.Serialize(exp1));

        // 002: Conflict - Me deleted it, Someone else updated it
        var v2my = Path.Combine(expPath, "002_DEL_my-device");
        var v2other = Path.Combine(expPath, "002_UPD_luigi");
        Directory.CreateDirectory(v2my);
        Directory.CreateDirectory(v2other);
        // v2my is empty (DEL)
        Directory.CreateDirectory(Path.Combine(v2other, ".data"));
        var exp2 = new Transaction { Id = expId, Type = "expense", Description = "Updated Pizza" };
        await File.WriteAllTextAsync(Path.Combine(v2other, ".data", "transaction_details.json"), System.Text.Json.JsonSerializer.Serialize(exp2));

        // Act
        var conflicts = await _localStorage.GetConflictsAsync(tripSlug);

        // Assert - Label should come from LCA (001), not the local branch (DEL)
        conflicts.Should().HaveCount(1);
        conflicts[0].Label.Should().Be("Spesa \"Original Pizza\"");
        }
        }