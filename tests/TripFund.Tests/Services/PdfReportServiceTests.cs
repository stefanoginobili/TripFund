using FluentAssertions;
using TripFund.App.Models;
using TripFund.App.Services;

namespace TripFund.Tests.Services;

public class PdfReportServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly PdfReportService _service;

    public PdfReportServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "TripFundReportTests_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempPath);
        _service = new PdfReportService(_tempPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [Fact]
    public async Task GenerateExpenseReport_ShouldCreateFile()
    {
        // Arrange
        var config = new TripConfig
        {
            Id = "trip-1",
            Name = "Giappone 2024",
            StartDate = new DateTime(2024, 10, 1),
            EndDate = new DateTime(2024, 10, 15),
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario" } },
                { "luigi", new User { Name = "Luigi" } }
            },
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", Decimals = 2, ExpectedQuotaPerMember = 1000 } },
                { "JPY", new Currency { Symbol = "¥", Decimals = 0, ExpectedQuotaPerMember = 150000 } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "expense",
                Description = "Pranzo a Tokyo",
                Amount = 5000,
                Currency = "JPY",
                Date = new DateTimeOffset(2024, 10, 2, 12, 30, 0, TimeSpan.FromHours(9)),
                Timezone = "Asia/Tokyo",
                Location = new LocationInfo { Name = "Shinjuku" }
            },
            new Transaction
            {
                Id = "t2",
                Type = "expense",
                Description = "Biglietto Treno",
                Amount = 25.50m,
                Currency = "EUR",
                Date = new DateTimeOffset(2024, 10, 1, 10, 0, 0, TimeSpan.FromHours(2)),
                Timezone = "Europe/Rome"
            },
            new Transaction
            {
                Id = "t3",
                Type = "contribution",
                Description = "Versamento iniziale",
                Amount = 1000,
                Currency = "EUR",
                Date = new DateTimeOffset(2024, 9, 30, 18, 0, 0, TimeSpan.FromHours(2)),
                Timezone = "Europe/Rome"
            }
        };

        // Act
        var filePath = await _service.GenerateExpenseReportAsync(config, transactions);

        // Assert
        File.Exists(filePath).Should().BeTrue();
        var fileInfo = new FileInfo(filePath);
        fileInfo.Length.Should().BeGreaterThan(0);
        fileInfo.Extension.Should().Be(".pdf");
    }

    [Fact]
    public async Task GenerateExpenseReport_ManyExpenses_ShouldHandleMultiplePages()
    {
        // Arrange
        var config = new TripConfig
        {
            Id = "trip-large",
            Name = "Viaggio Lungo",
            Members = new Dictionary<string, User> { { "user", new User { Name = "User" } } },
            Currencies = new Dictionary<string, Currency> { { "EUR", new Currency { Decimals = 2 } } }
        };

        var transactions = new List<Transaction>();
        for (int i = 0; i < 100; i++)
        {
            transactions.Add(new Transaction
            {
                Id = $"t{i}",
                Type = "expense",
                Description = $"Spesa molto lunga numero {i}",
                Amount = 10 + i,
                Currency = "EUR",
                Date = DateTimeOffset.UtcNow.AddHours(i)
            });
        }

        // Act
        var filePath = await _service.GenerateExpenseReportAsync(config, transactions);

        // Assert
        File.Exists(filePath).Should().BeTrue();
        new FileInfo(filePath).Length.Should().BeGreaterThan(0);
    }
}
