using System.IO.Compression;
using Moq;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;

namespace TripFund.Tests.Services;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempTestDir;
    private readonly PdfReportService _pdfReportService;
    private readonly Mock<LocalStorageService> _storageMock;
    private readonly Mock<LocalTripStorage> _tripStorageMock;
    private readonly ExportService _exportService;

    public ExportServiceTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), $"ExportServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempTestDir);

        _pdfReportService = new PdfReportService(_tempTestDir);
        
        _storageMock = new Mock<LocalStorageService>("dummy_path");
        _tripStorageMock = new Mock<LocalTripStorage>(_storageMock.Object, "test-trip");
        
        _storageMock.Setup(s => s.GetLocalTripStorage(It.IsAny<string>())).Returns(_tripStorageMock.Object);

        _exportService = new ExportService(_pdfReportService, _storageMock.Object);
    }

    [Fact]
    public async Task GenerateExportZipAsync_ShouldCreateZipWithPdfAndAttachments()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "1",
            Name = "Test Trip",
            StartDate = DateTime.UtcNow.AddDays(-5),
            EndDate = DateTime.UtcNow.AddDays(5),
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 100 } } },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };

        var now = DateTime.UtcNow;
        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "expense",
                Currency = "EUR",
                Amount = 10,
                Date = now.AddHours(-2),
                Description = "First Expense",
                Attachments = new List<TransactionAttachment>() // No attachments
            },
            new Transaction
            {
                Id = "t2",
                Type = "expense",
                Currency = "EUR",
                Amount = 20,
                Date = now.AddHours(-1),
                Description = "Second Expense",
                Attachments = new List<TransactionAttachment>
                {
                    new TransactionAttachment { Name = "att1.jpg", OriginalName = "receipt.jpg" }
                }
            }
        };

        // Create a dummy file for the attachment
        var dummyAttPath = Path.Combine(_tempTestDir, "dummy_att.jpg");
        await File.WriteAllTextAsync(dummyAttPath, "dummy content");

        _tripStorageMock.Setup(ts => ts.GetAttachmentPath("t2", "att1.jpg")).ReturnsAsync(dummyAttPath);

        // Act
        var zipPath = await _exportService.GenerateExportZipAsync(tripSlug, config, transactions);

        // Assert
        File.Exists(zipPath).Should().BeTrue();

        using var zip = ZipFile.OpenRead(zipPath);
        
        // Should contain PDF
        zip.Entries.Any(e => e.Name.EndsWith(".pdf")).Should().BeTrue();
        
        // Should contain Allegati folder and subfolder 002 with receipt.jpg
        // Note: Zip entries for files inside folders contain the path
        zip.Entries.Any(e => e.FullName.Contains("Allegati/002/receipt.jpg")).Should().BeTrue();
        
        // Should NOT contain folder 001 because t1 has no attachments
        zip.Entries.Any(e => e.FullName.Contains("Allegati/001/")).Should().BeFalse();
        
        // Clean up zip
        File.Delete(zipPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempTestDir))
        {
            try
            {
                Directory.Delete(_tempTestDir, true);
            }
            catch { }
        }
    }
}
