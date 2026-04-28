using Xunit;
using TripFund.App.Services;
using TripFund.App.Models;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TripFund.Tests.Services;

public class ConflictResolutionLogicTests
{
    private readonly string _testRoot;
    private readonly LocalTripStorageService _storage;

    public ConflictResolutionLogicTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "TripFundTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _storage = new LocalTripStorageService(_testRoot);
    }

    [Fact]
    public async Task GetConflictingConfigVersionsAsync_ShouldReturnMultipleVersions()
    {
        // Arrange
        string tripSlug = "test-trip";
        
        var config1 = new TripConfig { Name = "Version 1" };
        var config2 = new TripConfig { Name = "Version 2" };

        var configPath = Path.Combine(_testRoot, "trips", tripSlug, "config");
        var versionsPath = Path.Combine(configPath, ".versions");
        Directory.CreateDirectory(versionsPath);

        // Version 1 (001_NEW_device1)
        var folder1 = Path.Combine(versionsPath, "001_NEW_device1");
        Directory.CreateDirectory(Path.Combine(folder1, ".content"));
        await File.WriteAllTextAsync(Path.Combine(folder1, ".content", "trip_config.json"), JsonSerializer.Serialize(config1));
        await File.WriteAllLinesAsync(Path.Combine(folder1, ".tripfund"), new[] { "author=User1", "device=device1", "timestamp=2026-04-20T10:00:00Z" });
        
        // Version 2 (001_NEW_device2) - Conflict at same sequence
        var folder2 = Path.Combine(versionsPath, "001_NEW_device2");
        Directory.CreateDirectory(Path.Combine(folder2, ".content"));
        await File.WriteAllTextAsync(Path.Combine(folder2, ".content", "trip_config.json"), JsonSerializer.Serialize(config2));
        await File.WriteAllLinesAsync(Path.Combine(folder2, ".tripfund"), new[] { "author=User2", "device=device2", "timestamp=2026-04-20T10:00:00Z" });

        // Act
        var conflicts = await _storage.GetConflictingConfigVersionsAsync(tripSlug);

        // Assert
        Assert.Equal(2, conflicts.Count);
    }

    [Fact]
    public async Task ResolveConfigConflictAsync_ShouldCreateResFolder()
    {
        // Arrange
        string tripSlug = "test-trip";
        string resolverDevice = "resolver";
        
        var configPath = Path.Combine(_testRoot, "trips", tripSlug, "config");
        var versionsPath = Path.Combine(configPath, ".versions");
        Directory.CreateDirectory(versionsPath);

        // 001_NEW_device1
        var folder1 = Path.Combine(versionsPath, "001_NEW_device1");
        Directory.CreateDirectory(Path.Combine(folder1, ".content"));
        await File.WriteAllTextAsync(Path.Combine(folder1, ".content", "trip_config.json"), JsonSerializer.Serialize(new TripConfig { Name = "V1" }));
        await File.WriteAllLinesAsync(Path.Combine(folder1, ".tripfund"), new[] { "author=User1", "device=device1", "timestamp=2026-04-20T10:00:00Z" });

        // 001_NEW_device2
        var folder2 = Path.Combine(versionsPath, "001_NEW_device2");
        Directory.CreateDirectory(Path.Combine(folder2, ".content"));
        await File.WriteAllTextAsync(Path.Combine(folder2, ".content", "trip_config.json"), JsonSerializer.Serialize(new TripConfig { Name = "V2" }));
        await File.WriteAllLinesAsync(Path.Combine(folder2, ".tripfund"), new[] { "author=User2", "device=device2", "timestamp=2026-04-20T10:00:00Z" });

        var winner = new TripConfig { Name = "V2 (Winner)" };

        // Act
        await _storage.ResolveConfigConflictAsync(tripSlug, winner, resolverDevice);

        // Assert
        var folders = Directory.GetDirectories(versionsPath).Select(Path.GetFileName).Where(f => f != null).Cast<string>().ToList();
        Assert.Contains(folders, f => f.Contains("_RES_"));
        
        var finalConfig = await _storage.GetTripConfigAsync(tripSlug);
        Assert.Equal("V2 (Winner)", finalConfig?.Name);
    }

    [Fact]
    public async Task GetConflictingTransactionVersionsAsync_ShouldReturnMultipleVersions()
    {
        // Arrange
        string tripSlug = "test-trip";
        string txId = "20260420T100000Z-abcd";
        
        var detailsPath = Path.Combine(_testRoot, "trips", tripSlug, "transactions", txId, "details");
        var versionsPath = Path.Combine(detailsPath, ".versions");
        Directory.CreateDirectory(versionsPath);

        // 001_NEW_device1
        var folder1 = Path.Combine(versionsPath, "001_NEW_device1");
        Directory.CreateDirectory(Path.Combine(folder1, ".content"));
        await File.WriteAllTextAsync(Path.Combine(folder1, ".content", "transaction_details.json"), JsonSerializer.Serialize(new Transaction { Id = txId, Amount = 10 }));
        await File.WriteAllLinesAsync(Path.Combine(folder1, ".tripfund"), new[] { "author=User1", "device=device1", "timestamp=2026-04-20T10:00:00Z" });

        // 001_NEW_device2
        var folder2 = Path.Combine(versionsPath, "001_NEW_device2");
        Directory.CreateDirectory(Path.Combine(folder2, ".content"));
        await File.WriteAllTextAsync(Path.Combine(folder2, ".content", "transaction_details.json"), JsonSerializer.Serialize(new Transaction { Id = txId, Amount = 20 }));
        await File.WriteAllLinesAsync(Path.Combine(folder2, ".tripfund"), new[] { "author=User2", "device=device2", "timestamp=2026-04-20T10:00:00Z" });

        // Act
        var conflicts = await _storage.GetConflictingTransactionVersionsAsync(tripSlug, txId);

        // Assert
        Assert.Equal(2, conflicts.Count);
    }

    [Fact]
    public async Task ResolveTransactionConflictWithDeletion_ShouldCreateResFolderWithNoData()
    {
        // Arrange
        string tripSlug = "test-trip";
        string txId = "20260420T100000Z-abcd";
        string resolverDevice = "resolver";
        
        var detailsPath = Path.Combine(_testRoot, "trips", tripSlug, "transactions", txId, "details");
        var versionsPath = Path.Combine(detailsPath, ".versions");
        Directory.CreateDirectory(versionsPath);

        // 001_NEW_device1
        var folder1 = Path.Combine(versionsPath, "001_NEW_device1");
        Directory.CreateDirectory(Path.Combine(folder1, ".content"));
        await File.WriteAllTextAsync(Path.Combine(folder1, ".content", "transaction_details.json"), JsonSerializer.Serialize(new Transaction { Id = txId, Amount = 10 }));
        await File.WriteAllLinesAsync(Path.Combine(folder1, ".tripfund"), new[] { "author=User1", "device=device1", "timestamp=2026-04-20T10:00:00Z" });

        // 001_NEW_device2 (Deletion)
        var folder2 = Path.Combine(versionsPath, "001_NEW_device2");
        Directory.CreateDirectory(folder2); // No .content folder for DEL
        await File.WriteAllLinesAsync(Path.Combine(folder2, ".tripfund"), new[] { "author=User2", "device=device2", "timestamp=2026-04-20T10:00:00Z" });

        // Act - resolve choosing the deletion (null winner)
        await _storage.ResolveConflictAsync(tripSlug, txId, null, resolverDevice);

        // Assert
        var folders = Directory.GetDirectories(versionsPath).Select(Path.GetFileName).Where(f => f != null).Cast<string>().ToList();
        var resFolder = folders.First(f => f.Contains("_RES_"));
        var leaf = new LocalLeafFolder(Path.Combine(versionsPath, resFolder));
        
        Assert.True(await leaf.IsDataEmptyAsync());
        
        var finalTx = await _storage.GetLatestTransactionVersionAsync(tripSlug, txId);
        Assert.Null(finalTx);
    }
}
