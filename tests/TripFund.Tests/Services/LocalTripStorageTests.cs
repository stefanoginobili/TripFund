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
        await _service.SaveTripConfigAsync(slug, config);
        var loaded = await _service.GetTripConfigAsync(slug);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Test Trip");
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
        
        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "Transactions", "trans-1");
        Directory.GetDirectories(transDir).Should().HaveCount(2);
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
    }

    [Fact]
    public async Task Transaction_Conflict_ShouldThrowException()
    {
        // Arrange
        var tripSlug = "test-trip";
        var t1 = new Transaction { Id = "trans-1", Description = "Mario's Version" };
        var t2 = new Transaction { Id = "trans-1", Description = "Luigi's Version" };

        // Act
        // Manual setup to create conflict (same version number for different users)
        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "Transactions", "trans-1");
        Directory.CreateDirectory(Path.Combine(transDir, "001_mario"));
        Directory.CreateDirectory(Path.Combine(transDir, "001_luigi"));

        // Assert
        Func<Task> act = () => _service.GetTransactionsAsync(tripSlug);
        await act.Should().ThrowAsync<TransactionConflictException>()
            .Where(e => e.TransactionId == "trans-1" && e.ConflictingUserSlugs.Contains("mario") && e.ConflictingUserSlugs.Contains("luigi"));
    }

    [Fact]
    public async Task ConflictResolution_ShouldArchiveAndCreateNextVersion()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transId = "trans-1";
        var transDir = Path.Combine(_tempPath, "trips", tripSlug, "Transactions", transId);
        
        // Setup: 001_mario, 001_luigi, 002_mario (mario is ahead)
        Directory.CreateDirectory(Path.Combine(transDir, "001_mario"));
        Directory.CreateDirectory(Path.Combine(transDir, "001_luigi"));
        Directory.CreateDirectory(Path.Combine(transDir, "002_mario"));

        var resolvedTrans = new Transaction { Id = transId, Description = "Resolved" };

        // Act
        await _service.ResolveConflictAsync(tripSlug, resolvedTrans, "mario");

        // Assert
        // All active folders should be archived with '_'
        Directory.GetDirectories(transDir).Should().Contain(d => Path.GetFileName(d) == "_001_mario");
        Directory.GetDirectories(transDir).Should().Contain(d => Path.GetFileName(d) == "_001_luigi");
        Directory.GetDirectories(transDir).Should().Contain(d => Path.GetFileName(d) == "_002_mario");
        
        // New resolved version should be 003_mario
        Directory.GetDirectories(transDir).Should().Contain(d => Path.GetFileName(d) == "003_mario");

        var loaded = await _service.GetLatestTransactionVersionAsync(tripSlug, transId);
        loaded!.Description.Should().Be("Resolved");
    }
}
