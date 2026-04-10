using FluentAssertions;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.Tests.Services;

public class AttachmentCopyTests : IDisposable
{
    private readonly string _tempPath;
    private readonly LocalTripStorageService _service;

    public AttachmentCopyTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "TripFundAttachmentTests_" + Guid.NewGuid().ToString("n"));
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
    public async Task UpdateTransaction_ShouldCopyUntouchedAttachments()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transactionId = "trans-1";
        var t1 = new Transaction 
        { 
            Id = transactionId, 
            Description = "Initial",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "attachment_1.jpg", OriginalName = "original_1.jpg", CreatedAt = DateTime.UtcNow },
                new TransactionAttachment { Name = "attachment_2.png", OriginalName = "original_2.png", CreatedAt = DateTime.UtcNow }
            }
        };

        var attachments = new Dictionary<string, byte[]>
        {
            { "attachment_1.jpg", new byte[] { 1, 2, 3 } },
            { "attachment_2.png", new byte[] { 4, 5, 6 } }
        };

        // Act 1: Create initial version with two attachments
        await _service.SaveTransactionAsync(tripSlug, t1, "device1", attachments: attachments);

        var transRoot = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transactionId);
        
        // Verify V1
        var v1Path = Path.Combine(transRoot, "001_new_device1");
        Directory.Exists(v1Path).Should().BeTrue("V1 folder should exist");
        File.Exists(Path.Combine(v1Path, "attachment_1.jpg")).Should().BeTrue();

        // Act 2: Update transaction
        var t2 = new Transaction 
        { 
            Id = transactionId, 
            Description = "Updated",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "attachment_1.jpg", OriginalName = "original_1.jpg", CreatedAt = t1.Attachments[0].CreatedAt },
                new TransactionAttachment { Name = "attachment_2.png", OriginalName = "original_2.png", CreatedAt = t1.Attachments[1].CreatedAt },
                new TransactionAttachment { Name = "attachment_3.pdf", OriginalName = "original_3.pdf", CreatedAt = DateTime.UtcNow }
            }
        };
        var newAttachments = new Dictionary<string, byte[]>
        {
            { "attachment_3.pdf", new byte[] { 7, 8, 9 } }
        };

        await _service.SaveTransactionAsync(tripSlug, t2, "device1", attachments: newAttachments);

        // Assert: V2 should contain all three attachments
        var v2Path = Path.Combine(transRoot, "002_upd_device1");
        Directory.Exists(v2Path).Should().BeTrue("V2 folder should exist");
        
        File.Exists(Path.Combine(v2Path, "data.json")).Should().BeTrue("data.json should exist in V2");
        File.Exists(Path.Combine(v2Path, "attachment_1.jpg")).Should().BeTrue("attachment_1.jpg should be copied to V2");
        File.Exists(Path.Combine(v2Path, "attachment_2.png")).Should().BeTrue("attachment_2.png should be copied to V2");
        File.Exists(Path.Combine(v2Path, "attachment_3.pdf")).Should().BeTrue("attachment_3.pdf should be new in V2");

        (await File.ReadAllBytesAsync(Path.Combine(v2Path, "attachment_1.jpg"))).Should().Equal(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task UpdateTransaction_ShouldExcludeDeletedAttachments()
    {
        // Arrange
        var tripSlug = "test-trip";
        var transactionId = "trans-1";
        var t1 = new Transaction 
        { 
            Id = transactionId, 
            Description = "Initial",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "keep.jpg", OriginalName = "keep.jpg", CreatedAt = DateTime.UtcNow },
                new TransactionAttachment { Name = "delete.png", OriginalName = "delete.png", CreatedAt = DateTime.UtcNow }
            }
        };

        var attachments = new Dictionary<string, byte[]>
        {
            { "keep.jpg", new byte[] { 1, 2, 3 } },
            { "delete.png", new byte[] { 4, 5, 6 } }
        };

        await _service.SaveTransactionAsync(tripSlug, t1, "device1", attachments: attachments);

        // Act: Update, keeping only keep.jpg
        var t2 = new Transaction 
        { 
            Id = transactionId, 
            Description = "Updated",
            Attachments = new List<TransactionAttachment> 
            { 
                new TransactionAttachment { Name = "keep.jpg", OriginalName = "keep.jpg", CreatedAt = t1.Attachments[0].CreatedAt }
            }
        };

        await _service.SaveTransactionAsync(tripSlug, t2, "device1");

        // Assert
        var transRoot = Path.Combine(_tempPath, "trips", tripSlug, "transactions", transactionId);
        var v2Path = Path.Combine(transRoot, "002_upd_device1");
        Directory.Exists(v2Path).Should().BeTrue("V2 folder should exist");
        
        File.Exists(Path.Combine(v2Path, "keep.jpg")).Should().BeTrue("keep.jpg should be kept");
        File.Exists(Path.Combine(v2Path, "delete.png")).Should().BeFalse("delete.png should be deleted");
    }
}
