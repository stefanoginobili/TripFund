using Xunit;
using TripFund.App.Services;
using TripFund.App.Models;
using TripFund.App.Constants;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace TripFund.Tests.Services;

public class ExplicitAncestryTests
{
    private readonly string _testRoot;
    private readonly LocalTripStorageService _storage;
    private readonly string _tripSlug = "test-trip";
    private readonly string _deviceId_Pixel = "pixel-9-pro";
    private readonly string _deviceId_Macbook = "macbook";

    public ExplicitAncestryTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "TripFundTests_Ancestry_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _storage = new LocalTripStorageService(_testRoot);
    }

    [Fact]
    public async Task Conflict_ShouldBePreserved_WhenNewVersionPointsToOnlyOneDivergingBranch()
    {
        // Scenario reported by user:
        // 001_NEW (pixel)
        // 002_UPD (macbook) -> parents: [001]
        // 002_UPD (pixel) -> parents: [001]
        // 003_UPD (pixel) -> parents: [002_pixel]
        // 004_UPD (macbook) -> parents: [002_macbook]
        // EXPECTED: 003_pixel and 004_macbook are both LEAVES (Conflict)

        var txId = "20260421T142433Z-233ac01f";
        var transaction = new Transaction 
        { 
            Id = txId, 
            Type = "expense", 
            Date = DateTimeOffset.Now,
            Description = "Initial"
        };

        // 1. 001_NEW_pixel
        await _storage.SaveTransactionAsync(_tripSlug, transaction, _deviceId_Pixel);
        var versions = GetVersions(txId);
        var v001 = versions.First(v => v.Sequence == 1).FolderName;

        // 2. Divergence at 002
        // Force 002_UPD_macbook to point to 001
        transaction.Description = "Update from Macbook";
        await _storage.SaveTransactionAsync(_tripSlug, transaction, _deviceId_Macbook);
        versions = GetVersions(txId);
        var v002_mac = versions.First(v => v.Sequence == 2 && v.DeviceId == _deviceId_Macbook).FolderName;

        // Force 002_UPD_pixel to point to 001 (Simulating concurrent edit before sync)
        // Note: SaveTransactionAsync automatically picks current leaves. 
        // To simulate conflict we need to manually manipulate the state or ensure multiple leaves exist.
        // Since SaveTransactionAsync now uses _engine.GetLatestVersionFolders, if we have a conflict, 
        // it will pick ALL current leaves as parents. To simulate the user's scenario (divergence),
        // we can manually create the folders or carefully sequence.

        // Actually, the user's case happens when sync brings in a version that diverged earlier.
        // Let's manually create the folders to match the exact scenario.

        var detailsRoot = Path.Combine(_testRoot, "trips", _tripSlug, "transactions", txId, "details_versioned");
        
        // 002_UPD_pixel pointing to 001
        await CreateVersionFolder(detailsRoot, 2, CommitKind.Upd, _deviceId_Pixel, new[] { v001 });
        var v002_pix = "002_UPD_pixel-9-pro";

        // 003_UPD_pixel pointing to 002_pixel
        await CreateVersionFolder(detailsRoot, 3, CommitKind.Upd, _deviceId_Pixel, new[] { v002_pix });
        var v003_pix = "003_UPD_pixel-9-pro";

        // 004_UPD_macbook pointing to 002_macbook
        await CreateVersionFolder(detailsRoot, 4, CommitKind.Upd, _deviceId_Macbook, new[] { v002_mac });
        var v004_mac = "004_UPD_macbook";

        // Act
        var engine = new VersionedStorageEngine();
        var leaves = engine.GetLatestVersionFolders(detailsRoot);

        // Assert
        Assert.Equal(2, leaves.Count);
        Assert.Contains(leaves, l => l.FolderName.Contains("003_UPD_pixel-9-pro"));
        Assert.Contains(leaves, l => l.FolderName.Contains("004_UPD_macbook"));
        
        // In the old system, 004_macbook would have superseded 003_pixel because 003 was unique at its sequence.
        // In the new system, 004 only supersedes its ancestors (002_macbook, 001).
    }

    [Fact]
    public async Task Conflict_ShouldBeResolved_WhenResVersionPointsToMultipleParents()
    {
        var txId = "res-test";
        var detailsRoot = Path.Combine(_testRoot, "trips", _tripSlug, "transactions", txId, "details_versioned");
        Directory.CreateDirectory(detailsRoot);

        await CreateVersionFolder(detailsRoot, 1, CommitKind.New, "dev1", Array.Empty<string>());
        await CreateVersionFolder(detailsRoot, 2, CommitKind.Upd, "dev1", new[] { "001_NEW_dev1" });
        await CreateVersionFolder(detailsRoot, 2, CommitKind.Upd, "dev2", new[] { "001_NEW_dev1" });

        // Currently 2 leaves: 002_UPD_dev1 and 002_UPD_dev2
        var engine = new VersionedStorageEngine();
        Assert.Equal(2, engine.GetLatestVersionFolders(detailsRoot).Count);

        // Resolution: 003_RES_dev1 pointing to both
        await CreateVersionFolder(detailsRoot, 3, CommitKind.Res, "dev1", new[] { "002_UPD_dev1", "002_UPD_dev2" });

        // Act
        var leaves = engine.GetLatestVersionFolders(detailsRoot);

        // Assert
        Assert.Single(leaves);
        Assert.Equal(3, leaves[0].Sequence);
        Assert.Equal(CommitKind.Res, leaves[0].Kind);
    }

    [Fact]
    public async Task Ancestry_ShouldBeRecursive()
    {
        var root = Path.Combine(_testRoot, "ancestry-recursive");
        Directory.CreateDirectory(root);

        await CreateVersionFolder(root, 1, CommitKind.New, "d", Array.Empty<string>());
        await CreateVersionFolder(root, 2, CommitKind.Upd, "d", new[] { "001_NEW_d" });
        await CreateVersionFolder(root, 3, CommitKind.Upd, "d", new[] { "002_UPD_d" });
        await CreateVersionFolder(root, 4, CommitKind.Upd, "d", new[] { "003_UPD_d" });

        var engine = new VersionedStorageEngine();
        var all = engine.GetVersionFolders(root);
        var v4 = all.First(v => v.Sequence == 4);
        var v1 = all.First(v => v.Sequence == 1);

        // Act & Assert
        Assert.True(engine.GetLatestVersionFolders(all).Any(v => v.Sequence == 4));
        Assert.Single(engine.GetLatestVersionFolders(all));
        
        // Manual check of Supersedes logic
        // v4 supersedes v1? Yes, because v1 is ancestor of v4
        // Check internal Supersedes (it's private, but GetLatestVersionFolders uses it)
        var leaves = engine.GetLatestVersionFolders(all);
        Assert.DoesNotContain(all.First(v => v.Sequence == 1), leaves);
        Assert.DoesNotContain(all.First(v => v.Sequence == 2), leaves);
        Assert.DoesNotContain(all.First(v => v.Sequence == 3), leaves);
        Assert.Contains(all.First(v => v.Sequence == 4), leaves);
    }

    private List<VersionFolderInfo> GetVersions(string txId)
    {
        var path = Path.Combine(_testRoot, "trips", _tripSlug, "transactions", txId, "details_versioned");
        var engine = new VersionedStorageEngine();
        return engine.GetVersionFolders(path);
    }

    private async Task CreateVersionFolder(string root, int seq, CommitKind kind, string deviceId, IEnumerable<string> parents)
    {
        string name = $"{seq:D3}_{kind.ToString().ToUpperInvariant()}_{deviceId}";
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, ".content"));
        
        var meta = new Dictionary<string, string>
        {
            { AppConstants.Metadata.Author, "Test" },
            { AppConstants.Metadata.DeviceId, deviceId },
            { AppConstants.Metadata.CreatedAt, DateTime.UtcNow.ToString("O") }
        };
        
        if (parents.Any())
        {
            meta[AppConstants.Metadata.VersioningParents] = string.Join(",", parents);
        }

        var lines = meta.Select(kv => $"{kv.Key}={kv.Value}");
        await File.WriteAllLinesAsync(Path.Combine(path, ".tripfund"), lines);
    }
}
