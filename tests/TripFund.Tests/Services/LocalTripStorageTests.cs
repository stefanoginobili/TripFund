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
        Directory.GetDirectories(configDir).Should().ContainSingle(d => Path.GetFileName(d) == "001_NEW_mario");
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
        Directory.GetDirectories(detailsDir).Should().ContainSingle(d => Path.GetFileName(d) == "001_NEW_mario");
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
        Directory.GetDirectories(detailsDir).Should().HaveCount(2);
        Directory.GetDirectories(detailsDir).Should().Contain(d => Path.GetFileName(d) == "002_UPD_mario");
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
        Directory.GetDirectories(detailsDir).Should().HaveCount(2);
        Directory.GetDirectories(detailsDir).Should().Contain(d => Path.GetFileName(d) == "002_DEL_mario");
    }

    [Fact]
    public async Task Transaction_Delete_ShouldCreateDeletedFileWithInfo()
    {
        // Arrange
        var tripSlug = "test-trip";
        var t1 = new Transaction { Id = "trans-1", Description = "Lunch" };
        var settings = new AppSettings { AuthorName = "Mario Rossi", DeviceId = "mario-123" };
        await _service.SaveAppSettingsAsync(settings);

        // Act
        await _service.SaveTransactionAsync(tripSlug, t1, "mario");
        await _service.SaveTransactionAsync(tripSlug, t1, "mario", isDelete: true);

        // Assert
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1", "details");
        var delVersionDir = Path.Combine(detailsDir, "002_DEL_mario");
        var deletedFile = Path.Combine(delVersionDir, ".deleted.tf");
        
        File.Exists(deletedFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(deletedFile);
        content.Should().Contain("author=Mario Rossi");
        content.Should().Contain("deletedAt=");
        
        // Verify timestamp format roughly
        var lines = content.Split('\n');
        var timestampLine = lines.FirstOrDefault(l => l.StartsWith("deletedAt="));
        timestampLine.Should().NotBeNull();
        var timestamp = timestampLine!.Substring("deletedAt=".Length);
        timestamp.Should().MatchRegex(@"^\d{8}T\d{6}Z$");
    }

    [Fact]
    public async Task Transaction_Conflict_ShouldThrowException()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId, "details");
        Directory.CreateDirectory(detailsDir);
        
        // Manual setup to create conflict (same sequence number for different users)
        Directory.CreateDirectory(Path.Combine(detailsDir, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(detailsDir, "001_NEW_luigi"));

        // Assert
        Func<Task> act = () => _service.GetTransactionsAsync(tripSlug);
        await act.Should().ThrowAsync<TransactionConflictException>()
            .Where(e => e.TransactionId == transId && e.DivergingVersions.Contains("001_NEW_mario") && e.DivergingVersions.Contains("001_NEW_luigi"));

    }

    [Fact]
    public async Task ConflictResolution_ShouldCreateResVersion()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        var detailsDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId, "details");
        Directory.CreateDirectory(detailsDir);
        
        // Setup conflict: 001_NEW_mario, 001_NEW_luigi
        Directory.CreateDirectory(Path.Combine(detailsDir, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(detailsDir, "001_NEW_luigi"));

        var resolvedTrans = new Transaction { Id = transId, Description = "Resolved" };

        // Act
        await _service.ResolveConflictAsync(tripSlug, resolvedTrans, "mario");

        // Assert
        // New resolved version should be 002_RES_mario
        Directory.GetDirectories(detailsDir).Should().HaveCount(3);
        Directory.GetDirectories(detailsDir).Should().Contain(d => Path.GetFileName(d) == "002_RES_mario");

        var loaded = await _service.GetLatestTransactionVersionAsync(tripSlug, transId);
        loaded!.Description.Should().Be("Resolved");
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
        
        // Setup conflict: 002_UPD_mario, 002_UPD_luigi
        // We use sequence 2 because 1 already exists
        Directory.CreateDirectory(Path.Combine(detailsDir, "002_UPD_mario"));
        Directory.CreateDirectory(Path.Combine(detailsDir, "002_UPD_luigi"));

        var resolvedTrans = new Transaction { Id = transId, Description = "Resolved" };
        // resolvedTrans.CreatedAt is default(DateTime)

        // Act
        await Task.Delay(100);
        await _service.ResolveConflictAsync(tripSlug, resolvedTrans, "mario");

        // Assert
        var loaded = await _service.GetLatestTransactionVersionAsync(tripSlug, transId);
        loaded!.CreatedAt.Should().Be(originalCreatedAt);
        loaded.UpdatedAt.Should().BeAfter(originalCreatedAt);
    }

    [Fact]
    public async Task Transaction_Update_ShouldMaintainOriginalCreatedAt()
    {
        // Arrange
        var tripSlug = "test-trip";
        var t1 = new Transaction { Id = "trans-1", Description = "Lunch V1" };
        
        // Act
        await _service.SaveTransactionAsync(tripSlug, t1, "mario");
        var firstVersion = await _service.GetLatestTransactionVersionAsync(tripSlug, "trans-1");
        var originalCreatedAt = firstVersion!.CreatedAt;
        
        // Wait a bit to ensure UtcNow would be different
        await Task.Delay(100);
        
        var t2 = new Transaction { Id = "trans-1", Description = "Lunch V2" };
        // t2.CreatedAt is default(DateTime)
        
        await _service.SaveTransactionAsync(tripSlug, t2, "mario");
        var secondVersion = await _service.GetLatestTransactionVersionAsync(tripSlug, "trans-1");

        // Assert
        secondVersion!.CreatedAt.Should().Be(originalCreatedAt);
        secondVersion.UpdatedAt.Should().BeAfter(originalCreatedAt);
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
        var v1Dir = Path.Combine(detailsDir, "001_NEW_device1");
        
        // Simulate successful sync
        File.WriteAllText(Path.Combine(v1Dir, ".synched.tf"), "");
        File.WriteAllText(Path.Combine(v1Dir, "transaction_details.json.remote-etag.tf"), "etag-v1");
        File.WriteAllText(Path.Combine(v1Dir, ".synching.tf"), "");

        // Act: Update to V2
        var t2 = new Transaction { Id = "trans-1", Description = "V2" };
        await _service.SaveTransactionAsync(tripSlug, t2, deviceId);

        // Assert
        var v2Dir = Path.Combine(detailsDir, "002_UPD_device1");
        Directory.Exists(v2Dir).Should().BeTrue();
        
        File.Exists(Path.Combine(v2Dir, "transaction_details.json")).Should().BeTrue();
        File.Exists(Path.Combine(v2Dir, ".synched.tf")).Should().BeFalse(".synched.tf should NOT be copied");
        File.Exists(Path.Combine(v2Dir, "transaction_details.json.remote-etag.tf")).Should().BeFalse(".remote-etag.tf should NOT be copied");
        File.Exists(Path.Combine(v2Dir, ".synching.tf")).Should().BeFalse(".synching.tf should NOT be copied");
    }
}
