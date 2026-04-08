using FluentAssertions;
using TripFund.App.Models;
using TripFund.App.Utilities;

namespace TripFund.Tests.Utilities;

public class ReceiptGeneratorTests
{
    [Fact]
    public void GenerateContributionText_ShouldShowLocalTime()
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

        // Date in UTC
        var utcDate = new DateTime(2023, 10, 27, 10, 0, 0, DateTimeKind.Utc);
        
        var contribution = new Transaction
        {
            Id = "1",
            Type = "contribution",
            Date = utcDate,
            Currency = "EUR",
            Amount = 100,
            Split = new Dictionary<string, SplitInfo> { { "mario", new SplitInfo { Amount = 100 } } }
        };

        var allTransactions = new List<Transaction> { contribution };

        // Act
        var result = ReceiptGenerator.GenerateContributionText(trip, contribution, allTransactions);

        // Assert
        result.Should().Contain("Data: 27/10/2023");
        result.Should().MatchRegex(@"• Data: \d{2}/\d{2}/\d{4} \d{2}:\d{2}");
        result.Should().Contain($"Gli orari sono espressi nel fuso orario {TimeZoneInfo.Local.Id}");
    }
}
