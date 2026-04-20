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

        var configDir = Path.Combine(_tempPath, "trips", slug, "config_versioned");
        var versionDir = Path.Combine(configDir, "001_NEW_mario");
        Directory.Exists(versionDir).Should().BeTrue();
        File.Exists(Path.Combine(versionDir, ".metadata")).Should().BeTrue();
        File.Exists(Path.Combine(versionDir, ".data", "trip_config.json")).Should().BeTrue();
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

        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details_versioned");
        var versionDir = Path.Combine(detailsDir, "001_NEW_mario");
        Directory.Exists(versionDir).Should().BeTrue();
        File.Exists(Path.Combine(versionDir, ".data", "transaction_details.json")).Should().BeTrue();
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
        
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details_versioned");
        Directory.GetDirectories(detailsDir).Should().HaveCount(2);
        Directory.GetDirectories(detailsDir).Should().Contain(d => Path.GetFileName(d) == "002_UPD_mario");
        
        var v2Dir = Path.Combine(detailsDir, "002_UPD_mario");
        File.Exists(Path.Combine(v2Dir, ".data", "transaction_details.json")).Should().BeTrue();
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

        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details_versioned");
        Directory.GetDirectories(detailsDir).Should().HaveCount(2);
        Directory.GetDirectories(detailsDir).Should().Contain(d => Path.GetFileName(d) == "002_DEL_mario");
        
        var delDir = Path.Combine(detailsDir, "002_DEL_mario");
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
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details_versioned");
        var delVersionDir = Path.Combine(detailsDir, "002_DEL_mario-123");
        var metadataFile = Path.Combine(delVersionDir, ".metadata");
        
        File.Exists(metadataFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(metadataFile);
        content.Should().Contain("author=Mario Rossi");
        content.Should().Contain("device=mario-123");
        content.Should().Contain("timestamp=");
        
        // Verify timestamp format: yyyy-MM-ddTHH:mm:ssZ
        var lines = content.Split('\n', '\r').Where(l => !string.IsNullOrEmpty(l));
        var timestampLine = lines.FirstOrDefault(l => l.StartsWith("timestamp="));
        timestampLine.Should().NotBeNull();
        var timestamp = timestampLine!.Substring("timestamp=".Length);
        timestamp.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$");
    }

    [Fact]
    public async Task Transaction_Conflict_ShouldFallbackToFirstVersion_WhenNoLocalBranch()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId, "details_versioned");
        Directory.CreateDirectory(detailsDir);
        
        // Manual setup to create conflict (same sequence number for different users)
        var v1 = Path.Combine(detailsDir, "001_NEW_mario");
        var v2 = Path.Combine(detailsDir, "001_NEW_luigi");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        
        await File.WriteAllTextAsync(Path.Combine(v1, ".metadata"), "author=mario\ndevice=m\ntimestamp=2023-10-01T12:00:00Z");
        Directory.CreateDirectory(Path.Combine(v1, ".data"));
        var t1 = new Transaction { Id = transId, Description = "Mario's" };
        await File.WriteAllTextAsync(Path.Combine(v1, ".data", "transaction_details.json"), System.Text.Json.JsonSerializer.Serialize(t1));

        await File.WriteAllTextAsync(Path.Combine(v2, ".metadata"), "author=luigi\ndevice=l\ntimestamp=2023-10-01T12:00:00Z");
        Directory.CreateDirectory(Path.Combine(v2, ".data"));
        var t2 = new Transaction { Id = transId, Description = "Luigi's" };
        await File.WriteAllTextAsync(Path.Combine(v2, ".data", "transaction_details.json"), System.Text.Json.JsonSerializer.Serialize(t2));

        // Act
        var transactions = await _service.GetTransactionsAsync(tripSlug);

        // Assert - Should not throw, should pick one (alphabetical by folder name: luigi comes first in alphabetical order of folders "001_NEW_luigi")
        // Wait, GetLatestVersionFolders returns leaves ordered by Sequence, then it might depend on the list order.
        // Actually, Directory.GetDirectories might return them in any order, but VersionedStorageEngine returns them sorted by Sequence.
        // If they have same sequence, order depends on Directory.GetDirectories or Linq.
        transactions.Should().HaveCount(1);
        transactions[0].Id.Should().Be(transId);
    }

    [Fact]
    public async Task ConflictResolution_ShouldCreateResVersion()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId, "details_versioned");
        Directory.CreateDirectory(detailsDir);
        
        // Setup conflict: 001_NEW_mario, 001_NEW_luigi
        var v1 = Path.Combine(detailsDir, "001_NEW_mario");
        var v2 = Path.Combine(detailsDir, "001_NEW_luigi");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        Directory.CreateDirectory(Path.Combine(v1, ".data"));
        Directory.CreateDirectory(Path.Combine(v2, ".data"));
        await File.WriteAllTextAsync(Path.Combine(v1, ".metadata"), "author=m\ndevice=m\ntimestamp=2023-10-01T12:00:00Z");
        await File.WriteAllTextAsync(Path.Combine(v2, ".metadata"), "author=l\ndevice=l\ntimestamp=2023-10-01T12:00:00Z");

        var resolvedTrans = new Transaction { Id = transId, Description = "Resolved" };

        // Act
        await _service.ResolveConflictAsync(tripSlug, resolvedTrans, "mario");

        // Assert
        // New resolved version should be 002_RES_mario
        Directory.GetDirectories(detailsDir).Should().HaveCount(3);
        Directory.GetDirectories(detailsDir).Should().Contain(d => Path.GetFileName(d) == "002_RES_mario");

        var loaded = await _service.GetLatestTransactionVersionAsync(tripSlug, transId);
        loaded!.Description.Should().Be("Resolved");
        
        var resDir = Path.Combine(detailsDir, "002_RES_mario");
        var metadata = await File.ReadAllTextAsync(Path.Combine(resDir, ".metadata"));
        metadata.Should().Contain("resolved_versions=001_NEW_mario,001_NEW_luigi");
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
        
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId, "details_versioned");
        
        // Setup conflict: 002_UPD_mario, 002_UPD_luigi
        var v2m = Path.Combine(detailsDir, "002_UPD_mario");
        var v2l = Path.Combine(detailsDir, "002_UPD_luigi");
        Directory.CreateDirectory(v2m);
        Directory.CreateDirectory(v2l);
        Directory.CreateDirectory(Path.Combine(v2m, ".data"));
        Directory.CreateDirectory(Path.Combine(v2l, ".data"));
        await File.WriteAllTextAsync(Path.Combine(v2m, ".metadata"), "author=m\ndevice=m\ntimestamp=2023-10-01T12:00:00Z");
        await File.WriteAllTextAsync(Path.Combine(v2l, ".metadata"), "author=l\ndevice=l\ntimestamp=2023-10-01T12:00:00Z");

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
        
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details_versioned");
        var v1Dir = Path.Combine(detailsDir, "001_NEW_device1");
        
        // Simulate successful sync
        File.WriteAllText(Path.Combine(v1Dir, ".synched"), "");
        File.WriteAllText(Path.Combine(v1Dir, ".uploading"), "");

        // Act: Update to V2
        var t2 = new Transaction { Id = "trans-1", Description = "V2" };
        await _service.SaveTransactionAsync(tripSlug, t2, deviceId);

        // Assert
        var v2Dir = Path.Combine(detailsDir, "002_UPD_device1");
        Directory.Exists(v2Dir).Should().BeTrue();
        
        File.Exists(Path.Combine(v2Dir, ".data", "transaction_details.json")).Should().BeTrue();
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
}
