using FluentAssertions;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.Tests.Services;

public class LocalTripStorageTests : IDisposable
{
    private readonly string _tempPath;
    private readonly LocalTripStorageService _service;

    public LocalTripStorageTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "TripFundTests_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempPath);
        _service = new LocalTripStorageService(_tempPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [Fact]
    public async Task TripConfig_SaveAndLoad_ShouldWork()
    {
        // Arrange
        var slug = "test-trip";
        var config = new TripConfig { Id = "guid-1", Name = "Test Trip" };

        // Act
        await _service.SaveTripConfigAsync(slug, config, "mario");
        var loaded = await _service.GetTripConfigAsync(slug);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Test Trip");
        loaded.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var configDir = Path.Combine(_tempPath, "trips", slug, "config");
        var versionDir = Path.Combine(configDir, ".versions", "001_NEW_mario");
        Directory.Exists(versionDir).Should().BeTrue();
        File.Exists(Path.Combine(configDir, ".tripfund")).Should().BeTrue("Pointer file should exist at root");
        File.Exists(Path.Combine(versionDir, ".tripfund")).Should().BeTrue("Metadata file should exist in version dir");
        File.Exists(Path.Combine(versionDir, ".content", "trip_config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task TripConfig_HeadOptimization_ShouldWork()
    {
        // Arrange
        var slug = "test-trip";
        var config = new TripConfig { Name = "Optimized Trip" };
        await _service.SaveTripConfigAsync(slug, config, "mario");
        
        var configPath = Path.Combine(_tempPath, "trips", slug, "config");
        var pointerFile = Path.Combine(configPath, ".tripfund");
        File.Exists(pointerFile).Should().BeTrue();

        // Act & Assert 1: Read should use the head
        var loaded = await _service.GetTripConfigAsync(slug);
        loaded!.Name.Should().Be("Optimized Trip");

        // Act 2: Manually corrupt the head to point to a non-existing folder
        await File.WriteAllTextAsync(pointerFile, "contentType=tripfund/versioned-storage\nversioning.head=999_NON_EXISTING");
        
        // Assert: Read should fallback to DAG evaluation and REPAIR the head
        var loaded2 = await _service.GetTripConfigAsync(slug);
        loaded2!.Name.Should().Be("Optimized Trip");
        
        var repairedHead = await File.ReadAllTextAsync(pointerFile);
        repairedHead.Should().Contain("versioning.head=001_NEW_mario");
    }

    [Fact]
    public async Task Transaction_SaveAndLoad_ShouldWork()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transaction = new Transaction { Id = "trans-1", Description = "Lunch" };

        // Act
        await _service.SaveTransactionAsync(tripSlug, transaction, "mario");
        var transactions = await _service.GetTransactionsAsync(tripSlug);

        // Assert
        transactions.Should().ContainSingle();
        transactions[0].Description.Should().Be("Lunch");

        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details");
        var versionDir = Path.Combine(detailsDir, ".versions", "001_NEW_mario");
        Directory.Exists(versionDir).Should().BeTrue();
        File.Exists(Path.Combine(versionDir, ".content", "transaction_details.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Transaction_Update_ShouldCreateNewVersionAndLoadLatest()
    {
        // Arrange
        var tripSlug = "test-trip";
        var t1 = new Transaction { Id = "trans-1", Description = "Lunch V1" };
        var t2 = new Transaction { Id = "trans-1", Description = "Lunch V2" };

        // Act
        await _service.SaveTransactionAsync(tripSlug, t1, "mario");
        await _service.SaveTransactionAsync(tripSlug, t2, "mario");
        var transactions = await _service.GetTransactionsAsync(tripSlug);

        // Assert
        transactions.Should().ContainSingle();
        transactions[0].Description.Should().Be("Lunch V2");
        
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details");
        var versionsDir = Path.Combine(detailsDir, ".versions");
        Directory.GetDirectories(versionsDir).Should().HaveCount(2);
        Directory.GetDirectories(versionsDir).Should().Contain(d => Path.GetFileName(d) == "002_UPD_mario");
        
        var v2Dir = Path.Combine(versionsDir, "002_UPD_mario");
        File.Exists(Path.Combine(v2Dir, ".content", "transaction_details.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Transaction_Delete_ShouldBeIgnored()
    {
        // Arrange
        var tripSlug = "test-trip";
        var t1 = new Transaction { Id = "trans-1", Description = "Lunch" };

        // Act
        await _service.SaveTransactionAsync(tripSlug, t1, "mario");
        await _service.SaveTransactionAsync(tripSlug, t1, "mario", isDelete: true);
        var transactions = await _service.GetTransactionsAsync(tripSlug);

        // Assert
        transactions.Should().BeEmpty();

        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details");
        var versionsDir = Path.Combine(detailsDir, ".versions");
        Directory.GetDirectories(versionsDir).Should().HaveCount(2);
        Directory.GetDirectories(versionsDir).Should().Contain(d => Path.GetFileName(d) == "002_DEL_mario");
        
        var delDir = Path.Combine(versionsDir, "002_DEL_mario");
        var leaf = new LocalLeafFolder(delDir);
        (await leaf.IsDataEmptyAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Transaction_Delete_ShouldCreateMetadataWithInfo()
    {
        // Arrange
        var tripSlug = "test-trip";
        var t1 = new Transaction { Id = "trans-1", Description = "Lunch" };
        var settings = new AppSettings { AuthorName = "Mario Rossi", DeviceId = "mario-123" };
        await _service.SaveAppSettingsAsync(settings);

        // Act
        await _service.SaveTransactionAsync(tripSlug, t1, "mario-123");
        await _service.SaveTransactionAsync(tripSlug, t1, "mario-123", isDelete: true);

        // Assert
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details");
        var delVersionDir = Path.Combine(detailsDir, ".versions", "002_DEL_mario-123");
        var metadataFile = Path.Combine(delVersionDir, ".tripfund");
        
        File.Exists(metadataFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(metadataFile);
        content.Should().Contain("author=Mario Rossi");
        content.Should().Contain("device=mario-123");
        content.Should().Contain("createdAt=");
        
        // Verify format: yyyy-MM-ddTHH:mm:ss.fffZ
        var lines = content.Split('\n', '\r').Where(l => !string.IsNullOrEmpty(l));
        var createdAtLine = lines.FirstOrDefault(l => l.StartsWith("createdAt="));
        createdAtLine.Should().NotBeNull();
        var createdAt = createdAtLine!.Substring("createdAt=".Length);
        createdAt.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$");
    }

    [Fact]
    public async Task Transaction_Conflict_ShouldFallbackToFirstVersion_WhenNoLocalBranch()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId, "details");
        var versionsDir = Path.Combine(detailsDir, ".versions");
        Directory.CreateDirectory(versionsDir);
        
        // Manual setup to create conflict (same sequence number for different users)
        var v1 = Path.Combine(versionsDir, "001_NEW_mario");
        var v2 = Path.Combine(versionsDir, "001_NEW_luigi");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        
        await File.WriteAllTextAsync(Path.Combine(v1, ".tripfund"), "author=mario\ndevice=m\ncreatedAt=2023-10-01T12:00:00.000Z");
        Directory.CreateDirectory(Path.Combine(v1, ".content"));
        var t1 = new Transaction { Id = transId, Description = "Mario's" };
        await File.WriteAllTextAsync(Path.Combine(v1, ".content", "transaction_details.json"), System.Text.Json.JsonSerializer.Serialize(t1));

        await File.WriteAllTextAsync(Path.Combine(v2, ".tripfund"), "author=luigi\ndevice=l\ncreatedAt=2023-10-01T12:00:00.000Z");
        Directory.CreateDirectory(Path.Combine(v2, ".content"));
        var t2 = new Transaction { Id = transId, Description = "Luigi's" };
        await File.WriteAllTextAsync(Path.Combine(v2, ".content", "transaction_details.json"), System.Text.Json.JsonSerializer.Serialize(t2));

        // Act
        var transactions = await _service.GetTransactionsAsync(tripSlug);

        // Assert - Should not throw, should pick one
        transactions.Should().HaveCount(1);
        transactions[0].Id.Should().Be(transId);
    }

    [Fact]
    public async Task ConflictResolution_ShouldCreateResVersion()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId, "details");
        var versionsDir = Path.Combine(detailsDir, ".versions");
        Directory.CreateDirectory(versionsDir);
        
        // Setup conflict: 001_NEW_mario, 001_NEW_luigi
        var v1 = Path.Combine(versionsDir, "001_NEW_mario");
        var v2 = Path.Combine(versionsDir, "001_NEW_luigi");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        Directory.CreateDirectory(Path.Combine(v1, ".content"));
        Directory.CreateDirectory(Path.Combine(v2, ".content"));
        await File.WriteAllTextAsync(Path.Combine(v1, ".tripfund"), "author=m\ndevice=m\ncreatedAt=2023-10-01T12:00:00.000Z");
        await File.WriteAllTextAsync(Path.Combine(v2, ".tripfund"), "author=l\ndevice=l\ncreatedAt=2023-10-01T12:00:00.000Z");

        var resolvedTrans = new Transaction { Id = transId, Description = "Resolved" };

        // Act
        await _service.ResolveConflictAsync(tripSlug, resolvedTrans, "mario");

        // Assert
        // New resolved version should be 002_RES_mario
        Directory.GetDirectories(versionsDir).Should().HaveCount(3);
        Directory.GetDirectories(versionsDir).Should().Contain(d => Path.GetFileName(d) == "002_RES_mario");

        var loaded = await _service.GetLatestTransactionVersionAsync(tripSlug, transId);
        loaded!.Description.Should().Be("Resolved");
        
        var resDir = Path.Combine(versionsDir, "002_RES_mario");
        var metadata = await File.ReadAllTextAsync(Path.Combine(resDir, ".tripfund"));
        metadata.Should().Contain("versioning.parents=001_NEW_mario,001_NEW_luigi");
    }

    [Fact]
    public async Task ConflictResolution_ShouldMaintainOriginalCreatedAt()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        
        // Create initial version
        var t1 = new Transaction { Id = transId, Description = "V1" };
        await _service.SaveTransactionAsync(tripSlug, t1, "mario");
        var firstVersion = await _service.GetLatestTransactionVersionAsync(tripSlug, transId);
        var originalCreatedAt = firstVersion!.CreatedAt;
        
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId, "details");
        var versionsDir = Path.Combine(detailsDir, ".versions");

        // Setup conflict: 002_UPD_mario, 002_UPD_luigi
        var v2m = Path.Combine(versionsDir, "002_UPD_mario");
        var v2l = Path.Combine(versionsDir, "002_UPD_luigi");
        Directory.CreateDirectory(v2m);
        Directory.CreateDirectory(v2l);
        Directory.CreateDirectory(Path.Combine(v2m, ".content"));
        Directory.CreateDirectory(Path.Combine(v2l, ".content"));
        await File.WriteAllTextAsync(Path.Combine(v2m, ".tripfund"), "author=m\ndevice=m\ncreatedAt=2023-10-01T12:00:00.000Z");
        await File.WriteAllTextAsync(Path.Combine(v2l, ".tripfund"), "author=l\ndevice=l\ncreatedAt=2023-10-01T12:00:00.000Z");

        var resolvedTrans = new Transaction { Id = transId, Description = "Resolved" };

        // Act
        await Task.Delay(100);
        await _service.ResolveConflictAsync(tripSlug, resolvedTrans, "mario");

        // Assert
        var loaded = await _service.GetLatestTransactionVersionAsync(tripSlug, transId);
        loaded!.CreatedAt.Should().Be(originalCreatedAt);
        loaded.UpdatedAt.Should().BeAfter(originalCreatedAt);
    }

    [Fact]
    public async Task SaveTransaction_Update_ShouldNotCopySyncMarkers()
    {
        // Arrange
        var tripSlug = "test-trip";
        var t1 = new Transaction { Id = "trans-1", Description = "V1" };
        var deviceId = "device1";
        
        await _service.SaveTransactionAsync(tripSlug, t1, deviceId);
        
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details");
        var v1Dir = Path.Combine(detailsDir, ".versions", "001_NEW_device1");
        
        // Simulate successful sync
        File.WriteAllText(Path.Combine(v1Dir, ".synched"), "");
        File.WriteAllText(Path.Combine(v1Dir, ".uploading"), "");

        // Act: Update to V2
        var t2 = new Transaction { Id = "trans-1", Description = "V2" };
        await _service.SaveTransactionAsync(tripSlug, t2, deviceId);

        // Assert
        var v2Dir = Path.Combine(detailsDir, ".versions", "002_UPD_device1");
        Directory.Exists(v2Dir).Should().BeTrue();
        
        File.Exists(Path.Combine(v2Dir, ".content", "transaction_details.json")).Should().BeTrue();
        File.Exists(Path.Combine(v2Dir, ".synched")).Should().BeFalse(".synched should NOT be copied");
        File.Exists(Path.Combine(v2Dir, ".uploading")).Should().BeFalse(".uploading should NOT be copied");
    }

    [Fact]
    public async Task CleanupIncompleteImports_ShouldRemoveMarkedFoldersAndRegistryEntries()
    {
        // Arrange
        var slug = "interrupted-trip";
        var registry = await _service.GetTripRegistryAsync();
        registry.Trips[slug] = new TripRegistryEntry { CreatedAt = DateTime.UtcNow };
        await _service.SaveTripRegistryAsync(registry);

        await _service.InitializeInitialImportAsync(slug);
        
        var tripDir = Path.Combine(_tempPath, "trips", slug);
        Directory.Exists(tripDir).Should().BeTrue();
        File.Exists(Path.Combine(tripDir, ".initial_import")).Should().BeTrue();

        // Act
        await _service.CleanupIncompleteImportsAsync();

        // Assert
        Directory.Exists(tripDir).Should().BeFalse("Incomplete trip folder should be deleted");
        var updatedRegistry = await _service.GetTripRegistryAsync();
        updatedRegistry.Trips.ContainsKey(slug).Should().BeFalse("Incomplete trip should be removed from registry");
    }

    [Fact]
    public async Task CleanupBrokenTrips_ShouldRemoveTripsWithoutConfig()
    {
        // Arrange
        var slug1 = "good-trip";
        var slug2 = "broken-trip";
        
        var registry = await _service.GetTripRegistryAsync();
        registry.Trips[slug1] = new TripRegistryEntry { CreatedAt = DateTime.UtcNow };
        registry.Trips[slug2] = new TripRegistryEntry { CreatedAt = DateTime.UtcNow };
        await _service.SaveTripRegistryAsync(registry);

        // Setup good trip
        var config = new TripConfig { Name = "Good Trip", StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
        await _service.SaveTripConfigAsync(slug1, config, "device-1");

        // Setup broken trip: directory exists but no config
        var brokenDir = Path.Combine(_tempPath, "trips", slug2);
        Directory.CreateDirectory(brokenDir);

        // Act
        await _service.CleanupBrokenTripsAsync();

        // Assert
        var updatedRegistry = await _service.GetTripRegistryAsync();
        updatedRegistry.Trips.Should().ContainKey(slug1);
        updatedRegistry.Trips.Should().NotContainKey(slug2);
        Directory.Exists(brokenDir).Should().BeFalse("Broken trip folder should be deleted");
    }

    [Fact]
    public async Task CleanupBrokenTrips_ShouldRemoveTripsWithoutDirectory()
    {
        // Arrange
        var slug = "missing-folder-trip";
        var registry = await _service.GetTripRegistryAsync();
        registry.Trips[slug] = new TripRegistryEntry { CreatedAt = DateTime.UtcNow };
        await _service.SaveTripRegistryAsync(registry);

        // Act
        await _service.CleanupBrokenTripsAsync();

        // Assert
        var updatedRegistry = await _service.GetTripRegistryAsync();
        updatedRegistry.Trips.Should().NotContainKey(slug);
    }

    [Fact]
    public async Task CleanupTempFolders_ShouldRemoveTempDirectories()
    {
        // Arrange
        var slug1 = "trip-1";
        var slug2 = "trip-2";
        var temp1 = Path.Combine(_tempPath, "trips", slug1, "temp");
        var temp2 = Path.Combine(_tempPath, "trips", slug2, "temp");
        
        Directory.CreateDirectory(temp1);
        Directory.CreateDirectory(temp2);
        
        await File.WriteAllTextAsync(Path.Combine(temp1, "garbage.txt"), "junk");
        await File.WriteAllTextAsync(Path.Combine(temp2, "junk.zip"), "binary");

        Directory.Exists(temp1).Should().BeTrue();
        Directory.Exists(temp2).Should().BeTrue();

        // Act
        await _service.CleanupTempFoldersAsync();

        // Assert
        Directory.Exists(temp1).Should().BeFalse();
        Directory.Exists(temp2).Should().BeFalse();
        
        // Ensure parent directories still exist
        Directory.Exists(Path.GetDirectoryName(temp1)).Should().BeTrue();
        Directory.Exists(Path.GetDirectoryName(temp2)).Should().BeTrue();
    }

    [Fact]
    public async Task AtomicWrite_ShouldBeResilient()
    {
        // Arrange
        var registry = new LocalTripRegistry();
        registry.Trips["trip-1"] = new TripRegistryEntry { CreatedAt = DateTime.UtcNow };
        
        // Act 1: Normal save
        await _service.SaveTripRegistryAsync(registry);
        var path = Path.Combine(_tempPath, "known_trips.json");
        File.Exists(path).Should().BeTrue();

        // Act 2: Simulate corruption by writing invalid JSON
        await File.WriteAllTextAsync(path, "{ \"invalid\": json... ");
        
        // Assert: Get should recover
        var recovered = await _service.GetTripRegistryAsync();
        recovered.Should().NotBeNull();
        recovered.Trips.Should().BeEmpty();

        // Act 3: Save again should fix it
        await _service.SaveTripRegistryAsync(registry);
        var fixedRegistry = await _service.GetTripRegistryAsync();
        fixedRegistry.Trips.Should().ContainKey("trip-1");
    }

    [Fact]
    public async Task SyncState_SaveAndLoad_ShouldWorkInNewPath()
    {
        // Arrange
        var tripSlug = "sync-test-trip";
        var state = new SyncState();
        state.Sync.Remote.AppliedPackages.Add("pack1.zip");
        state.Sync.Local.Pending.Add(new PendingUpload { Path = "config/001_NEW_mario", CreatedAt = DateTime.UtcNow.ToString("O") });

        // Act
        await _service.SaveSyncStateAsync(tripSlug, state);
        var loaded = await _service.GetSyncStateAsync(tripSlug);

        // Assert
        loaded.Should().NotBeNull();
        loaded.Sync.Remote.AppliedPackages.Should().Contain("pack1.zip");
        loaded.Sync.Local.Pending.Should().HaveCount(1);
        loaded.Sync.Local.Pending[0].Path.Should().Be("config/001_NEW_mario");

        var syncPath = Path.Combine(_tempPath, "trips", tripSlug, "sync", "sync_state.json");
        File.Exists(syncPath).Should().BeTrue("sync_state.json should be in the 'sync' subfolder");
    }
}
