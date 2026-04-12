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

        var metadataDir = Path.Combine(_tempPath, "trips", slug, "metadata");
        Directory.GetDirectories(metadataDir).Should().ContainSingle(d => Path.GetFileName(d) == "001_NEW_mario");
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

        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1");
        Directory.GetDirectories(transDir).Should().ContainSingle(d => Path.GetFileName(d) == "001_NEW_mario");
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
        
        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1");
        Directory.GetDirectories(transDir).Should().HaveCount(2);
        Directory.GetDirectories(transDir).Should().Contain(d => Path.GetFileName(d) == "002_UPD_mario");
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

        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1");
        Directory.GetDirectories(transDir).Should().HaveCount(2);
        Directory.GetDirectories(transDir).Should().Contain(d => Path.GetFileName(d) == "002_DEL_mario");
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
        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", "trans-1");
        var delVersionDir = Path.Combine(transDir, "002_DEL_mario");
        var deletedFile = Path.Combine(delVersionDir, ".deleted");
        
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
        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId);
        Directory.CreateDirectory(transDir);
        
        // Manual setup to create conflict (same sequence number for different users)
        Directory.CreateDirectory(Path.Combine(transDir, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(transDir, "001_NEW_luigi"));

        // Assert
        Func<Task> act = () => _service.GetTransactionsAsync(tripSlug);
        await act.Should().ThrowAsync<TransactionConflictException>()
            .Where(e => e.TransactionId == transId && e.ConflictingDeviceIds.Contains("mario") && e.ConflictingDeviceIds.Contains("luigi"));

    }

    [Fact]
    public async Task ConflictResolution_ShouldCreateResVersion()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transId);
        Directory.CreateDirectory(transDir);
        
        // Setup conflict: 001_NEW_mario, 001_NEW_luigi
        Directory.CreateDirectory(Path.Combine(transDir, "001_NEW_mario"));
        Directory.CreateDirectory(Path.Combine(transDir, "001_NEW_luigi"));

        var resolvedTrans = new Transaction { Id = transId, Description = "Resolved" };

        // Act
        await _service.ResolveConflictAsync(tripSlug, resolvedTrans, "mario");

        // Assert
        // New resolved version should be 002_RES_mario
        Directory.GetDirectories(transDir).Should().HaveCount(3);
        Directory.GetDirectories(transDir).Should().Contain(d => Path.GetFileName(d) == "002_RES_mario");

        var loaded = await _service.GetLatestTransactionVersionAsync(tripSlug, transId);
        loaded!.Description.Should().Be("Resolved");
    }
}
