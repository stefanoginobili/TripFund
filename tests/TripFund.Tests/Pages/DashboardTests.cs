using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripFund.App.Components.Pages;
using TripFund.App.Models;
using TripFund.App.Services;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;

namespace TripFund.Tests.Pages;

public class DashboardTests : BunitContext
{
    private readonly Mock<LocalTripStorageService> _storageMock;

    public DashboardTests()
    {
        _storageMock = new Mock<LocalTripStorageService>("dummy_path");
        Services.AddSingleton(_storageMock.Object);
    }

    [Fact]
    public void TripDashboard_ShouldCalculateAndDisplayMathCorrectly()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 500 } },
                { "USD", new Currency { Symbol = "$", ExpectedQuotaPerMember = 600 } }
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 300,
                Split = new Dictionary<string, decimal> { { "mario", 300 } }
            },
            new Transaction
            {
                Id = "t2",
                Type = "expense",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, decimal> { { "mario", 50 }, { "luigi", 50 } }
            },
            new Transaction
            {
                Id = "t3",
                Type = "contribution",
                Currency = "USD",
                Amount = 100,
                Split = new Dictionary<string, decimal> { { "luigi", 100 } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert (EUR is default)
        // EUR target = 500 * 2 = 1000
        // Mario EUR: Contributed 300, Expense 50 -> Remaining 250
        // Luigi EUR: Contributed 0, Expense 50 -> Remaining -50
        // Total Contributed EUR = 300
        // Total Remaining EUR = 200

        var summaryTotal = cut.Find(".summary-total").TextContent;
        summaryTotal.Should().Contain("200"); // Totale rimasto

        var summarySub = cut.Find(".summary-sub").TextContent;
        summarySub.Should().Contain("300"); // Totale versato

        var memberRows = cut.FindAll(".member-row");
        memberRows.Should().HaveCount(2);

        var marioRow = memberRows.First(r => r.InnerHtml.Contains("Mario"));
        marioRow.InnerHtml.Should().Contain("300"); // Versato
        marioRow.InnerHtml.Should().Contain("250"); // Rimasto

        var luigiRow = memberRows.First(r => r.InnerHtml.Contains("Luigi"));
        luigiRow.InnerHtml.Should().Contain("0"); // Versato
        luigiRow.InnerHtml.Should().Contain("-50"); // Rimasto

        // Act - Switch to USD
        var usdBtn = cut.FindAll(".currency-pill").First(b => b.TextContent.Contains("USD"));
        usdBtn.Click();

        // Assert (USD)
        // USD target = 600 * 2 = 1200
        // Luigi USD: Contributed 100, Expense 0 -> Remaining 100
        // Mario USD: Contributed 0, Expense 0 -> Remaining 0
        // Total Contributed USD = 100
        // Total Remaining USD = 100

        summaryTotal = cut.Find(".summary-total").TextContent;
        summaryTotal.Should().Contain("100");

        marioRow = cut.FindAll(".member-row").First(r => r.InnerHtml.Contains("Mario"));
        marioRow.InnerHtml.Should().Contain("0"); // Versato
    }

    [Fact]
    public void TripDashboard_ShouldFormatAmountsWithConfiguredDecimals()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "JPY", new Currency { Symbol = "¥", Decimals = 0, ExpectedQuotaPerMember = 100000 } }
            },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "JPY",
                Amount = 50000.75m,
                Split = new Dictionary<string, decimal> { { "mario", 50000.75m } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // JPY should show 50,001 or 50.001 (rounded by N0) depending on locale
        var summarySub = cut.Find(".summary-sub").TextContent; // Totale versato

        // We check for absence of decimal part
        summarySub.Should().NotContain(".75");
        summarySub.Should().NotContain(",75");

        // It should contain 50,001 or 50.001. We use a regex for flexibility
        summarySub.Should().MatchRegex(@"50[.,]001");

        var txRow = cut.Find(".transaction-row");
        txRow.InnerHtml.Should().MatchRegex(@"¥ 50[.,]001");
        // We ensure it doesn't have decimal digits like .00 or .75
        txRow.InnerHtml.Should().NotMatchRegex(@"50[.,]001[.,]\d+");
    }

    [Fact]
    public void TripDashboard_TransactionsList_ShouldFilterByCurrencyAndSortByDate()
    {
        // Arrange
        var tripSlug = "test-trip";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€" } },
                { "USD", new Currency { Symbol = "$" } }
            },
            Members = new Dictionary<string, User> { { "mario", new User { Name = "Mario" } } }
        };

        var now = DateTime.UtcNow;
        var transactions = new List<Transaction>
        {
            new Transaction { Id = "20260404T080000Z-12345678", Currency = "EUR", Date = now.AddHours(-2), Amount = 10, Description = "Old EUR", Split = new Dictionary<string, decimal>() },
            new Transaction { Id = "20260404T090000Z-87654321", Currency = "EUR", Date = now.AddHours(-1), Amount = 20, Description = "New EUR", Split = new Dictionary<string, decimal>() },
            new Transaction { Id = "20260404T100000Z-11223344", Currency = "USD", Date = now, Amount = 30, Description = "USD TX", Split = new Dictionary<string, decimal>() }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<TripDashboard>(parameters => parameters.Add(p => p.tripSlug, tripSlug));

        // Assert - Default EUR
        var eurTxRows = cut.FindAll(".transaction-row");
        eurTxRows.Should().HaveCount(2);
        eurTxRows[0].InnerHtml.Should().Contain("New EUR"); // Sorted DESC
        eurTxRows[1].InnerHtml.Should().Contain("Old EUR");
        eurTxRows.Any(r => r.InnerHtml.Contains("USD TX")).Should().BeFalse();

        // Act - Switch to USD
        var usdBtn = cut.FindAll(".currency-pill").First(b => b.TextContent.Contains("USD"));
        usdBtn.Click();

        // Assert - USD
        var usdTxRows = cut.FindAll(".transaction-row");
        usdTxRows.Should().HaveCount(1);
        usdTxRows[0].InnerHtml.Should().Contain("USD TX");
        usdTxRows.Any(r => r.InnerHtml.Contains("EUR")).Should().BeFalse();
    }

    [Fact]
    public void MemberDashboard_ShouldCalculateAndDisplayMathCorrectly()
    {
        // Arrange
        var tripSlug = "test-trip";
        var memberSlug = "mario";
        var config = new TripConfig
        {
            Id = "123",
            Name = "Test Trip",
            Currencies = new Dictionary<string, Currency>
            {
                { "EUR", new Currency { Symbol = "€", ExpectedQuotaPerMember = 500 } }
            },
            Members = new Dictionary<string, User>
            {
                { "mario", new User { Name = "Mario", Avatar = "M" } },
                { "luigi", new User { Name = "Luigi", Avatar = "L" } }
            }
        };

        var transactions = new List<Transaction>
        {
            new Transaction
            {
                Id = "t1",
                Type = "contribution",
                Currency = "EUR",
                Amount = 300,
                Split = new Dictionary<string, decimal> { { "mario", 300 } }
            },
            new Transaction
            {
                Id = "t2",
                Type = "expense",
                Currency = "EUR",
                Amount = 100,
                Split = new Dictionary<string, decimal> { { "mario", 50 }, { "luigi", 50 } }
            }
        };

        _storageMock.Setup(s => s.GetTripConfigAsync(tripSlug)).ReturnsAsync(config);
        _storageMock.Setup(s => s.GetTransactionsAsync(tripSlug)).ReturnsAsync(transactions);

        // Act
        var cut = Render<MemberDashboard>(parameters => parameters
            .Add(p => p.tripSlug, tripSlug)
            .Add(p => p.memberSlug, memberSlug)
        );

        // Assert
        // Target: 500
        // Mario Contributed: 300
        // Mario Expense: 50
        // Mario Remaining: 250

        var summaryTotal = cut.Find(".summary-total").TextContent; // Totale versato
        summaryTotal.Should().Contain("300");

        var summarySub = cut.Find(".summary-sub").TextContent; // Totale rimasto
        summarySub.Should().Contain("250");

        var transactionRows = cut.FindAll(".transaction-row");
        transactionRows.Should().HaveCount(2);

        // Specifically check that expense row shows 50 (Mario's split), not 100 (total amount)
        var expenseRow = transactionRows.First(r => r.InnerHtml.Contains("expense-icon"));
        expenseRow.InnerHtml.Should().Contain("50");
        expenseRow.InnerHtml.Should().NotContain("100");
    }
}
