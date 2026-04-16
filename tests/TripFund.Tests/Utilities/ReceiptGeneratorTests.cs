using FluentAssertions;
using TripFund.App.Models;
using TripFund.App.Utilities;

namespace TripFund.Tests.Utilities;

public class ReceiptGeneratorTests
{
    [Fact]
    public void GenerateContributionText_ShouldShowAcquisitionTimeWithUtcOffset()
    {
        // Arrange
        var trip = new TripConfig
        {
            Name = "Test Trip",
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Email = "mario@example.com", Avatar = "👨" } }
            },
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", Decimals = 2 } }
            }
        };

        // Date with a specific offset (e.g., +02:00)
        var dateWithOffset = new DateTimeOffset(2023, 10, 27, 12, 30, 0, TimeSpan.FromHours(2));
        
        var contribution = new Transaction
        {
            Id = "1",
            Type = "contribution",
            Date = dateWithOffset,
            Currency = "EUR",
            Amount = 100,
            Timezone = "Europe/Rome",
            Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 100 } } }
        };

        var allTransactions = new List<Transaction> { contribution };

        // Act
        var result = ReceiptGenerator.GenerateContributionText(trip, "mario", contribution, allTransactions);

        // Assert
        result.Should().Contain("Data: 27/10/2023 12:30 (UTC+02:00)");
    }

    [Fact]
    public void GenerateContributionText_ShouldHandleNegativeOffsets()
    {
        // Arrange
        var trip = new TripConfig
        {
            Name = "Test Trip",
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Email = "mario@example.com", Avatar = "👨" } }
            },
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", Decimals = 2 } }
            }
        };

        // Date with a negative offset (e.g., -05:00)
        var dateWithOffset = new DateTimeOffset(2023, 10, 27, 08, 00, 0, TimeSpan.FromHours(-5));
        
        var contribution = new Transaction
        {
            Id = "2",
            Type = "contribution",
            Date = dateWithOffset,
            Currency = "EUR",
            Amount = 50,
            Timezone = "America/New_York",
            Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 50 } } }
        };

        var allTransactions = new List<Transaction> { contribution };

        // Act
        var result = ReceiptGenerator.GenerateContributionText(trip, "mario", contribution, allTransactions);

        // Assert
        result.Should().Contain("Data: 27/10/2023 09:00 (UTC-04:00)");
    }

    [Fact]
    public void GenerateContributionText_ShouldShowNoContributionsMessage_WhenMemberHasNoContributions()
    {
        // Arrange
        var trip = new TripConfig
        {
            Name = "Test Trip",
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Email = "mario@example.com", Avatar = "👨" } }
            },
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", Decimals = 2 } }
            }
        };

        var allTransactions = new List<Transaction>();

        // Act
        var result = ReceiptGenerator.GenerateContributionText(trip, "mario", null, allTransactions);

        // Assert
        result.Should().Contain("Ciao Mario,");
        result.Should().Contain("--- DETTAGLIO VERSAMENTI ---");
        result.Should().Contain("Nessun versamento registrato");
        result.Should().NotContain("--- RIEPILOGO TOTALI PER VALUTA ---");
    }
}
