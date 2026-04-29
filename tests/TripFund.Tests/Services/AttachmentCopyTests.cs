using FluentAssertions;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.Tests.Services;

public class AttachmentCopyTests : IDisposable
{
    private readonly string _tempPath;
    private readonly LocalStorageService _service;
    private const string TripSlug = "test-trip";

    public AttachmentCopyTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "TripFundAttachmentTests_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempPath);
        _service = new LocalStorageService(_tempPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [Fact]
    public async Task SaveTransaction_ShouldStoreAttachmentsInLeafFoldersWithDataAndMetadata()
    {
        // Arrange
        var tripStorage = _service.GetLocalTripStorage(TripSlug);
        var transactionId = "trans-1";
        var t1 = new Transaction 
        { 
            Id = transactionId, 
            Description = "Initial",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "ATT_001", OriginalName = "original_1.jpg", CreatedAt = DateTime.UtcNow },
                new TransactionAttachment { Name = "ATT_002", OriginalName = "original_2.png", CreatedAt = DateTime.UtcNow }
            }
        };

        var attachments = new Dictionary<string, byte[]>
        {
            { "ATT_001", new byte[] { 1, 2, 3 } },
            { "ATT_002", new byte[] { 4, 5, 6 } }
        };

        // Act 1: Create initial version with two attachments
        await tripStorage.SaveTransactionAsync(t1, "device1", attachments: attachments);

        var transRoot = Path.Combine(_tempPath, "trips", TripSlug, "transactions", transactionId);
        
        // Verify Details V1
        var v1Path = Path.Combine(transRoot, "details", ".versions", "001_NEW_device1");
        Directory.Exists(v1Path).Should().BeTrue("Details V1 folder should exist");
        File.Exists(Path.Combine(v1Path, ".content", "transaction_details.json")).Should().BeTrue();

        // Verify Attachments - Now in .content subfolder
        var att1Path = Path.Combine(transRoot, "attachments", "ATT_001", ".content", "original_1.jpg");
        var att2Path = Path.Combine(transRoot, "attachments", "ATT_002", ".content", "original_2.png");
        File.Exists(att1Path).Should().BeTrue("Attachment 1 should exist in .content subfolder of its leaf");
        File.Exists(att2Path).Should().BeTrue("Attachment 2 should exist in .content subfolder of its leaf");

        // Verify Metadata for attachments
        File.Exists(Path.Combine(transRoot, "attachments", "ATT_001", ".tripfund")).Should().BeTrue();
        File.Exists(Path.Combine(transRoot, "attachments", "ATT_002", ".tripfund")).Should().BeTrue();

        // Act 2: Update transaction (add new attachment)
        var t2 = new Transaction 
        { 
            Id = transactionId, 
            Description = "Updated",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "ATT_001", OriginalName = "original_1.jpg", CreatedAt = t1.Attachments[0].CreatedAt },
                new TransactionAttachment { Name = "ATT_002", OriginalName = "original_2.png", CreatedAt = t1.Attachments[1].CreatedAt },
                new TransactionAttachment { Name = "ATT_003", OriginalName = "original_3.pdf", CreatedAt = DateTime.UtcNow }
            }
        };
        var newAttachments = new Dictionary<string, byte[]>
        {
            { "ATT_003", new byte[] { 7, 8, 9 } }
        };

        await tripStorage.SaveTransactionAsync(t2, "device1", attachments: newAttachments);

        // Assert: V2 details created
        var v2Path = Path.Combine(transRoot, "details", ".versions", "002_UPD_device1");
        Directory.Exists(v2Path).Should().BeTrue("Details V2 folder should exist");
        
        // Assert: Attachments are not duplicated but all exist in unversioned folder
        File.Exists(att1Path).Should().BeTrue();
        File.Exists(att2Path).Should().BeTrue();
        var att3Path = Path.Combine(transRoot, "attachments", "ATT_003", ".content", "original_3.pdf");
        File.Exists(att3Path).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateTransaction_ShouldKeepPhysicalFilesWhenRemovedFromMetadata()
    {
        // Arrange
        var tripStorage = _service.GetLocalTripStorage(TripSlug);
        var transactionId = "trans-1";
        var t1 = new Transaction 
        { 
            Id = transactionId, 
            Description = "Initial",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "ATT_KEEP", OriginalName = "keep.jpg", CreatedAt = DateTime.UtcNow },
                new TransactionAttachment { Name = "ATT_DELETE", OriginalName = "delete.png", CreatedAt = DateTime.UtcNow }
            }
        };

        var attachments = new Dictionary<string, byte[]>
        {
            { "ATT_KEEP", new byte[] { 1, 2, 3 } },
            { "ATT_DELETE", new byte[] { 4, 5, 6 } }
        };

        await tripStorage.SaveTransactionAsync(t1, "device1", attachments: attachments);

        // Act: Update, keeping only keep.jpg in details
        var t2 = new Transaction 
        { 
            Id = transactionId, 
            Description = "Updated",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "ATT_KEEP", OriginalName = "keep.jpg", CreatedAt = t1.Attachments[0].CreatedAt }
            }
        };

        await tripStorage.SaveTransactionAsync(t2, "device1");

        // Assert
        var transRoot = Path.Combine(_tempPath, "trips", TripSlug, "transactions", transactionId);
        var attKeepPath = Path.Combine(transRoot, "attachments", "ATT_KEEP", ".content", "keep.jpg");
        var attDeletePath = Path.Combine(transRoot, "attachments", "ATT_DELETE", ".content", "delete.png");
        
        File.Exists(attKeepPath).Should().BeTrue("Physical file for kept attachment should exist");
        File.Exists(attDeletePath).Should().BeTrue("Physical file for removed attachment should ALSO exist (soft delete/history preservation)");

        var latestTx = await tripStorage.GetLatestTransactionVersionAsync(transactionId);
        latestTx!.Attachments.Should().HaveCount(1);
        latestTx.Attachments[0].Name.Should().Be("ATT_KEEP");
    }
}
